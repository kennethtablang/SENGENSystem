using System.Diagnostics;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Scheduling.Engine
{
    /// <summary>
    /// Rule-based CSP engine (no ML — FR-SCHED-08). Places every section into a room and time slot
    /// by backtracking search. The engine is <b>deterministic for a given <see cref="ScheduleProblem.Seed"/></b>
    /// but explores the solution space differently for different seeds, so regenerating produces a
    /// different — yet equally conflict-free and optimised — timetable rather than always the same
    /// one. The seed is returned and audited, so any arrangement stays reproducible.
    ///
    /// <para><b>Hard constraints — never violated (FR-SCHED-02, NFR-1):</b></para>
    /// <list type="bullet">
    /// <item>H1 — a room is never double-booked in overlapping slots.</item>
    /// <item>H2 — a faculty member is never double-assigned in overlapping slots.</item>
    /// <item>H3a — room capacity ≥ the section's seat capacity.</item>
    /// <item>H3b — room <i>kind</i> matches the meeting: lecture hours only in a lecture room,
    /// laboratory hours only in the laboratory the subject requires (Computer or Kitchen). The rule
    /// binds in both directions — a pure lecture may not occupy a laboratory, because the campus has
    /// one Computer Lab and one Kitchen Lab against numerous lecture rooms.</item>
    /// <item>H4 — a member's allocated units never exceed their semester ceiling.</item>
    /// <item>H5 — a member only ever teaches a subject they were allocated.</item>
    /// <item>H6 — sections of one student cohort never overlap in time. This is also what keeps a
    /// lecture-laboratory subject's two meetings from colliding with each other.</item>
    /// </list>
    ///
    /// <para>
    /// A lecture-laboratory subject enters the search as <b>two variables</b>, not one: its lecture
    /// hours and its laboratory hours are placed independently, in different rooms, at different
    /// times. Scheduling them as a single long block would strand the lecture hours inside the one
    /// shared laboratory.
    /// </para>
    ///
    /// <para><b>Soft constraints — optimised, never at a hard constraint's expense:</b></para>
    /// <list type="bullet">
    /// <item>S1 — preferred teaching windows honoured where possible.</item>
    /// <item>S2 — idle gaps between consecutive classes minimised, for cohorts and members alike.</item>
    /// <item>S3 — equitable load across faculty (measured and reported; see the note below).</item>
    /// </list>
    ///
    /// <para>
    /// H3, H4 and H5 are settled <i>before</i> the search rather than tested inside it. Faculty
    /// come from the Head's load allocation, so who teaches what — and therefore each member's
    /// total load — is already fixed; re-checking it per candidate would be wasted work, and
    /// failing deep in the search would produce a far worse error message than failing up front.
    /// The consequence for S3 is deliberate: the engine cannot rebalance an uneven allocation,
    /// so it measures the spread and hands that back for the Head to act on.
    /// </para>
    ///
    /// When no schedule exists, the result carries actionable diagnostics: how deep the search
    /// got, which sections blocked it, and which constraint their candidates collided with.
    /// </summary>
    public sealed class CspScheduler
    {
        // Guards so a run always terminates in practical time (FR-SCHED-07): a step budget against
        // pathological search trees and a wall-clock budget against slow candidate sets. Both are
        // now supplied per run from System Parameters (with the historical defaults on ScheduleProblem).

        public ScheduleGenerationResult Solve(ScheduleProblem problem)
        {
            // Pre-flight: empty resource pools produce a clear message instead of a doomed search.
            var preflight = new List<string>();
            if (problem.Sections.Count == 0) preflight.Add("There are no sections to schedule.");
            if (problem.Rooms.Count == 0) preflight.Add("No rooms are configured — add rooms in Academic setup.");
            if (problem.TimeSlots.Count == 0) preflight.Add("No time slots are configured — the engine has no times to assign.");
            if (problem.Faculty.Count == 0) preflight.Add("No faculty profiles are configured — the engine has no one to assign.");
            if (preflight.Count > 0)
            {
                return ScheduleGenerationResult.Fail(preflight, 0);
            }

            var facultyById = problem.Faculty.ToDictionary(f => f.FacultyProfileId);

            // ---- H4: unit ceilings. Load is fixed by the allocation, so this is a property of
            // the inputs, not of any placement — decide it once, before searching.
            var overloadReasons = new List<string>();
            foreach (var byFaculty in problem.Sections.GroupBy(s => s.FacultyProfileId))
            {
                if (!facultyById.TryGetValue(byFaculty.Key, out var member))
                {
                    var orphaned = byFaculty
                        .OrderBy(s => s.SectionCode, StringComparer.Ordinal)
                        .Select(s => s.SectionCode)
                        .Distinct(StringComparer.Ordinal);
                    overloadReasons.Add(
                        $"These sections are allocated to a faculty member who no longer exists: " +
                        $"{string.Join(", ", orphaned)}. Reassign them on the Faculty load page.");
                    continue;
                }

                // Units sit on one component per section, so this sums a section once even when
                // its lecture and laboratory halves are scheduled separately.
                var assigned = byFaculty.Sum(s => s.Units);
                if (assigned > member.MaxLoadUnits)
                {
                    var sectionCodes = byFaculty
                        .OrderBy(s => s.SectionCode, StringComparer.Ordinal)
                        .Select(s => s.SectionCode)
                        .Distinct(StringComparer.Ordinal);
                    overloadReasons.Add(
                        $"{member.Label} is allocated {assigned} units against a ceiling of {member.MaxLoadUnits} " +
                        $"({assigned - member.MaxLoadUnits} over) across {string.Join(", ", sectionCodes)} — " +
                        "rebalance the load allocation before generating.");
                }
            }
            if (overloadReasons.Count > 0)
            {
                return ScheduleGenerationResult.Fail(overloadReasons, 0);
            }

            // ---- H3a/H3b: candidate rooms per meeting (static domain filtering) ----
            // ---- Weekly-hours coverage: candidate time *blocks* per meeting (FR-SCHED-04) ----
            // A meeting runs for its component's weekly hours, but the grid is 90-minute periods,
            // so a 3-hour laboratory must occupy a contiguous run of periods. Per required duration
            // we precompute every contiguous block the grid offers; a meeting's time domain is the
            // set of blocks long enough to cover its hours. Both domains are settled before the
            // search so an impossible meeting fails with a clear message, not a dead end.
            var domains = new Dictionary<(Guid, ClassComponent), List<RoomOption>>();
            var timeDomains = new Dictionary<(Guid, ClassComponent), List<TimeSlot>>();
            var blocksByDuration = new Dictionary<int, List<TimeSlot>>();
            var emptyDomainReasons = new List<string>();

            foreach (var section in problem.Sections)
            {
                var rooms = problem.Rooms
                    .Where(r => r.Capacity >= section.Capacity && r.Kind == section.RequiredRoomKind)
                    .ToList();

                if (rooms.Count == 0)
                {
                    var anyOfKind = problem.Rooms.Any(r => r.Kind == section.RequiredRoomKind);
                    emptyDomainReasons.Add(anyOfKind
                        ? $"{section.Label}: no {section.RequiredRoomKind.Label().ToLowerInvariant()} " +
                          $"has capacity ≥ {section.Capacity}."
                        : $"{section.Label}: needs a {section.RequiredRoomKind.Label().ToLowerInvariant()} " +
                          "and none is configured — add one in Academic setup → Rooms.");
                }
                domains[section.Key] = rooms;

                if (!blocksByDuration.TryGetValue(section.RequiredMinutes, out var blocks))
                {
                    blocks = BuildContiguousBlocks(problem.TimeSlots, section.RequiredMinutes);
                    blocksByDuration[section.RequiredMinutes] = blocks;
                }
                if (blocks.Count == 0)
                {
                    emptyDomainReasons.Add(
                        $"{section.Label}: needs a {section.RequiredMinutes / 60.0:0.#}-hour continuous block, " +
                        "but no day has that many consecutive time slots — add adjacent time slots.");
                }
                timeDomains[section.Key] = blocks;
            }

            if (emptyDomainReasons.Count > 0)
            {
                return ScheduleGenerationResult.Fail(emptyDomainReasons, 0);
            }

            // ---- Pigeonhole feasibility. One faculty member — or one student cohort — can
            // occupy at most one time slot at a time, so needing more meetings than there are
            // slots is impossible no matter how the search branches. Counted in meetings, not
            // sections, because a lecture-laboratory subject needs two of them. Catching it here
            // costs one pass and replaces a 20-second doomed search with a sentence that names
            // the problem.
            var pigeonhole = new List<string>();
            foreach (var byFaculty in problem.Sections.GroupBy(s => s.FacultyProfileId))
            {
                var count = byFaculty.Count();
                if (count > problem.TimeSlots.Count)
                {
                    pigeonhole.Add(
                        $"{facultyById[byFaculty.Key].Label} is allocated {count} class meetings but only " +
                        $"{problem.TimeSlots.Count} time slots exist — they cannot all be placed. " +
                        "Add time slots or move sections to another member.");
                }
            }
            foreach (var byCohort in problem.Sections.GroupBy(s => s.CohortKey, StringComparer.OrdinalIgnoreCase))
            {
                var count = byCohort.Count();
                if (count > problem.TimeSlots.Count)
                {
                    pigeonhole.Add(
                        $"Cohort {byCohort.Key} has {count} class meetings but only {problem.TimeSlots.Count} " +
                        "time slots exist — they cannot all be placed without a clash.");
                }
            }
            if (pigeonhole.Count > 0)
            {
                return ScheduleGenerationResult.Fail(pigeonhole, 0);
            }

            // Most-constrained-variable ordering: hardest sections (fewest candidate placements —
            // rooms × time blocks — labs, largest cohorts) are placed first to prune the search
            // tree early. Among sections the heuristics rank equally, a seeded shuffle varies which
            // goes first, so a new seed explores a different part of the solution space; SectionCode
            // and SectionId remain the final tiebreakers, keeping the order total and reproducible
            // for any given seed (FR-SCHED-08).
            var order = problem.Sections
                .OrderBy(s => domains[s.Key].Count * timeDomains[s.Key].Count)
                .ThenByDescending(s => s.RequiresLaboratory)
                .ThenByDescending(s => s.Units)
                .ThenBy(s => SeededKey(problem.Seed, s.SectionId, 0, (int)s.Component))
                .ThenBy(s => s.SectionCode, StringComparer.Ordinal)
                .ThenBy(s => s.SectionId)
                .ThenBy(s => (int)s.Component)
                .ToList();

            var state = new SearchState(facultyById, problem.Weights);
            var ctx = new SearchContext(Stopwatch.StartNew(), problem.MaxSteps, problem.TimeBudget);

            var solved = Backtrack(0, order, domains, timeDomains, problem.Seed, state, ctx);

            if (solved)
            {
                var assignments = state.ToAssignments();
                var report = OptimizationReport.Measure(
                    assignments, problem.Sections, problem.Rooms, problem.Faculty);
                return ScheduleGenerationResult.Ok(assignments, ctx.Steps, report);
            }

            var reasons = new List<string>
            {
                ctx.Aborted
                    ? $"Search stopped at its safety limit ({ctx.Steps:N0} steps / {ctx.Clock.Elapsed.TotalSeconds:0}s) — " +
                      "inputs look over-constrained (too many sections for the available rooms or time slots)."
                    : "No conflict-free schedule exists for the given sections, rooms, and time slots."
            };
            reasons.Add(
                $"The search placed at most {ctx.DeepestIndex} of {order.Count} class meetings before running out of options.");
            reasons.AddRange(Diagnose(order, domains, timeDomains, facultyById, problem.Weights));

            return ScheduleGenerationResult.Fail(reasons, ctx.Steps);
        }

        private sealed class SearchContext(Stopwatch clock, int maxSteps, TimeSpan timeBudget)
        {
            public Stopwatch Clock { get; } = clock;
            public int Steps { get; set; }
            public bool Aborted { get; set; }
            /// <summary>How far the search ever got — the frontier where it kept failing.</summary>
            public int DeepestIndex { get; set; }

            public bool OverBudget() => Steps >= maxSteps || Clock.Elapsed > timeBudget;
        }

        private bool Backtrack(
            int index,
            List<SectionVar> order,
            Dictionary<(Guid, ClassComponent), List<RoomOption>> domains,
            Dictionary<(Guid, ClassComponent), List<TimeSlot>> timeDomains,
            int seed,
            SearchState state,
            SearchContext ctx)
        {
            if (index == order.Count)
            {
                return true;
            }

            if (index > ctx.DeepestIndex)
            {
                ctx.DeepestIndex = index;
            }

            if (ctx.OverBudget())
            {
                ctx.Aborted = true;
                return false;
            }

            var section = order[index];

            foreach (var value in OrderCandidates(section, domains[section.Key], timeDomains[section.Key], seed, state))
            {
                ctx.Steps++;
                if (ctx.OverBudget())
                {
                    ctx.Aborted = true;
                    return false;
                }

                if (!state.IsConsistent(section, value))
                {
                    continue;
                }

                state.Assign(section, value);
                if (Backtrack(index + 1, order, domains, timeDomains, seed, state, ctx))
                {
                    return true;
                }
                state.Unassign(section);

                if (ctx.Aborted)
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Soft-preference value ordering: try the (room, block) pairs with the lowest soft cost
        /// first, so the first complete solution found is already a good one. Purely an
        /// ordering heuristic — consistency is still enforced by
        /// <see cref="SearchState.IsConsistent(SectionVar, SectionAssignment)"/>.
        /// <para>
        /// Among candidates with the <i>same</i> soft cost (common), a seeded shuffle decides the
        /// order, so a different seed produces a different — but equally good — placement. A stable
        /// (day, start, end, room) tail keeps the order total and reproducible for that seed; block
        /// ids are not used because they are freshly generated each run.
        /// </para>
        /// </summary>
        private static IEnumerable<SectionAssignment> OrderCandidates(
            SectionVar section,
            List<RoomOption> rooms,
            IReadOnlyList<TimeSlot> blocks,
            int seed,
            SearchState state) =>
            from t in blocks
            from r in rooms
            let softScore = state.SoftScore(section, r, t)
            orderby softScore,
                SeededKey(seed, r.RoomId, t.StartMinutes, (int)t.Day),
                t.Day, t.StartMinutes, t.EndMinutes, r.RoomId
            select new SectionAssignment(section.SectionId, section.Component, r.RoomId, t, section.FacultyProfileId);

        /// <summary>
        /// A stable, seed-dependent sort key. Deterministic for a given seed (and unchanged across
        /// runs — it hashes the guid's bytes, not its <see cref="object.GetHashCode"/>), but a
        /// different seed reorders otherwise-tied candidates, which is what gives each generation a
        /// different valid arrangement.
        /// </summary>
        private static int SeededKey(int seed, Guid id, int a, int b)
        {
            unchecked
            {
                var h = 2166136261u ^ (uint)seed;
                foreach (var by in id.ToByteArray())
                {
                    h = (h ^ by) * 16777619u;
                }
                h = (h ^ (uint)a) * 16777619u;
                h = (h ^ (uint)b) * 16777619u;
                return (int)h;
            }
        }

        /// <summary>
        /// Failure explanation: replay a deterministic greedy pass (same ordering as the real
        /// search) and, for every section that cannot be placed, tally which hard constraint
        /// each of its candidates collided with. Cheap, deterministic, and tells the Academic
        /// Head what to add or relax — rooms, slots, or the load allocation (FR-SCHED-07).
        /// </summary>
        private static IEnumerable<string> Diagnose(
            List<SectionVar> order,
            Dictionary<(Guid, ClassComponent), List<RoomOption>> domains,
            Dictionary<(Guid, ClassComponent), List<TimeSlot>> timeDomains,
            Dictionary<Guid, FacultyOption> facultyById,
            SoftWeights weights)
        {
            const int maxReports = 5;
            var state = new SearchState(facultyById, weights);
            var reports = new List<string>();

            foreach (var section in order)
            {
                var rooms = domains[section.Key];
                var placedThisSection = false;
                int roomBusy = 0, facultyBusy = 0, cohortBusy = 0, total = 0;

                foreach (var t in timeDomains[section.Key])
                {
                    foreach (var r in rooms)
                    {
                        total++;
                        var value = new SectionAssignment(
                            section.SectionId, section.Component, r.RoomId, t, section.FacultyProfileId);
                        if (state.IsConsistent(section, value, out var conflict))
                        {
                            state.Assign(section, value);
                            placedThisSection = true;
                            break;
                        }

                        switch (conflict)
                        {
                            case ConflictKind.RoomBusy: roomBusy++; break;
                            case ConflictKind.FacultyBusy: facultyBusy++; break;
                            case ConflictKind.CohortBusy: cohortBusy++; break;
                        }
                    }
                    if (placedThisSection) break;
                }

                if (!placedThisSection && reports.Count < maxReports)
                {
                    var parts = new List<string>();
                    if (roomBusy > 0) parts.Add($"{roomBusy} hit an occupied room");
                    if (facultyBusy > 0) parts.Add($"{facultyBusy} hit a busy faculty member");
                    if (cohortBusy > 0) parts.Add($"{cohortBusy} clashed with the cohort's other classes");
                    reports.Add(
                        $"{section.Label}: none of its {total} candidate placements fit — " +
                        string.Join(", ", parts) + ".");
                }
            }

            if (reports.Count == 0)
            {
                reports.Add(
                    "Every section fits on its own but their combination is infeasible — " +
                    "add rooms or time slots, or spread the load allocation across more faculty.");
            }

            return reports;
        }

        /// <summary>
        /// Every contiguous block the grid can offer that covers <paramref name="requiredMinutes"/>.
        /// Each block starts at a base period and runs for <i>exactly</i> the meeting's weekly
        /// minutes, so a subject's real hours are honoured rather than rounded up to the period
        /// boundary: on a 90-minute grid a 1-hour subject yields a 60-minute block, a 2-hour
        /// subject a 120-minute block, and a 3-hour subject a 180-minute block. The block is only
        /// offered when adjacent periods cover it with no gap (a lunch break breaks the run), so
        /// the exact-length span always falls on real class time. Blocks are synthetic
        /// <see cref="TimeSlot"/>s (new ids); the endpoint persists each as an on-demand period,
        /// the same way the manual board does.
        /// </summary>
        private static List<TimeSlot> BuildContiguousBlocks(IReadOnlyList<TimeSlot> baseSlots, int requiredMinutes)
        {
            var blocks = new List<TimeSlot>();
            if (requiredMinutes <= 0) return blocks;

            var seen = new HashSet<(DayOfWeek, int, int)>();
            foreach (var byDay in baseSlots.GroupBy(s => s.Day))
            {
                var day = byDay.OrderBy(s => s.StartMinutes).ThenBy(s => s.EndMinutes).ToList();
                for (var i = 0; i < day.Count; i++)
                {
                    var start = day[i].StartMinutes;
                    var covered = day[i].EndMinutes - day[i].StartMinutes;
                    var j = i;

                    // Extend into the next period only while it abuts the current one (no gap).
                    while (covered < requiredMinutes
                        && j + 1 < day.Count
                        && day[j + 1].StartMinutes == day[j].EndMinutes)
                    {
                        j++;
                        covered += day[j].EndMinutes - day[j].StartMinutes;
                    }

                    // The covering run [start, start+covered] is gap-free, so a block of exactly
                    // requiredMinutes anchored at the period start sits entirely on class time.
                    if (covered >= requiredMinutes)
                    {
                        var blockEnd = start + requiredMinutes;
                        if (seen.Add((day[i].Day, start, blockEnd)))
                        {
                            blocks.Add(new TimeSlot { Day = day[i].Day, StartMinutes = start, EndMinutes = blockEnd });
                        }
                    }
                }
            }

            return blocks;
        }
    }
}
