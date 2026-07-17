using System.Diagnostics;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Scheduling.Engine
{
    /// <summary>
    /// Deterministic, rule-based CSP engine (no ML — FR-SCHED-08). Assigns every section a
    /// room, time slot, and faculty member via backtracking search so that ALL hard
    /// constraints hold (FR-SCHED-02, NFR-1):
    ///   • a room is never double-booked in overlapping slots,
    ///   • a faculty member is never double-assigned in overlapping slots,
    ///   • sections of the same student cohort never overlap in time,
    ///   • room capacity (and laboratory requirement) is respected,
    ///   • faculty unit-load ceilings are respected.
    /// Among consistent values it prefers those that balance faculty load and reduce cohort
    /// idle gaps (soft constraints — FR-SCHED-03), but never at the cost of a hard constraint.
    ///
    /// When no schedule exists, the result carries actionable diagnostics: how deep the search
    /// got, which sections blocked it, and which constraint their candidates collided with.
    /// </summary>
    public sealed class CspScheduler
    {
        // Guards so a run always terminates in practical time (FR-SCHED-07): a step budget
        // against pathological search trees and a wall-clock budget against slow candidate sets.
        private const int MaxSteps = 2_000_000;
        private static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(20);

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

            var timeSlotsById = problem.TimeSlots.ToDictionary(t => t.Id);

            // Precompute each section's candidate rooms and faculty (static domain filtering).
            var domains = new Dictionary<Guid, (List<RoomOption> Rooms, List<FacultyOption> Faculty)>();
            var emptyDomainReasons = new List<string>();

            foreach (var section in problem.Sections)
            {
                var rooms = problem.Rooms
                    .Where(r => r.Capacity >= section.Capacity && (!section.RequiresLaboratory || r.IsLaboratory))
                    .ToList();

                var faculty = problem.Faculty
                    .Where(f => string.Equals(f.ProgramCode, section.ProgramCode, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (rooms.Count == 0)
                {
                    emptyDomainReasons.Add(
                        $"{section.SectionCode}: no room with capacity ≥ {section.Capacity}" +
                        (section.RequiresLaboratory ? " that is a laboratory." : "."));
                }

                if (faculty.Count == 0)
                {
                    emptyDomainReasons.Add(
                        $"{section.SectionCode}: no faculty member assigned to program '{section.ProgramCode}'.");
                }

                domains[section.SectionId] = (rooms, faculty);
            }

            if (emptyDomainReasons.Count > 0)
            {
                return ScheduleGenerationResult.Fail(emptyDomainReasons, 0);
            }

            // Aggregate feasibility: total units demanded per program vs. the ceiling its faculty
            // can carry. Catches "not enough teachers" before any search runs.
            foreach (var byProgram in problem.Sections.GroupBy(s => s.ProgramCode, StringComparer.OrdinalIgnoreCase))
            {
                var demand = byProgram.Sum(s => s.Units);
                var supply = problem.Faculty
                    .Where(f => string.Equals(f.ProgramCode, byProgram.Key, StringComparison.OrdinalIgnoreCase))
                    .Sum(f => f.MaxLoadUnits);
                if (demand > supply)
                {
                    emptyDomainReasons.Add(
                        $"Program '{byProgram.Key}' needs {demand} teaching units but its faculty can carry at most " +
                        $"{supply} — raise unit-load ceilings or add faculty before regenerating.");
                }
            }

            if (emptyDomainReasons.Count > 0)
            {
                return ScheduleGenerationResult.Fail(emptyDomainReasons, 0);
            }

            // Most-constrained-variable ordering: hardest sections (fewest candidate rooms×faculty,
            // labs, largest cohorts) are placed first to prune the search tree early.
            var order = problem.Sections
                .OrderBy(s => domains[s.SectionId].Rooms.Count * domains[s.SectionId].Faculty.Count)
                .ThenByDescending(s => s.RequiresLaboratory)
                .ThenByDescending(s => s.Units)
                .ToList();

            var facultyMaxLoad = problem.Faculty.ToDictionary(f => f.FacultyProfileId, f => f.MaxLoadUnits);
            var state = new SearchState(timeSlotsById, facultyMaxLoad);
            var ctx = new SearchContext(Stopwatch.StartNew());

            var solved = Backtrack(0, order, domains, problem.TimeSlots, state, ctx);

            if (solved)
            {
                return ScheduleGenerationResult.Ok(state.ToAssignments(), ctx.Steps);
            }

            var reasons = new List<string>
            {
                ctx.Aborted
                    ? $"Search stopped at its safety limit ({ctx.Steps:N0} steps / {ctx.Clock.Elapsed.TotalSeconds:0}s) — " +
                      "inputs look over-constrained (too many sections for the available rooms, time slots, or faculty)."
                    : "No conflict-free schedule exists for the given sections, rooms, time slots, and faculty."
            };
            reasons.Add(
                $"The search placed at most {ctx.DeepestIndex} of {order.Count} sections before running out of options.");
            reasons.AddRange(Diagnose(order, domains, problem.TimeSlots, timeSlotsById, facultyMaxLoad));

            return ScheduleGenerationResult.Fail(reasons, ctx.Steps);
        }

        private sealed class SearchContext(Stopwatch clock)
        {
            public Stopwatch Clock { get; } = clock;
            public int Steps { get; set; }
            public bool Aborted { get; set; }
            /// <summary>How far the search ever got — the frontier where it kept failing.</summary>
            public int DeepestIndex { get; set; }

            public bool OverBudget() => Steps >= MaxSteps || Clock.Elapsed > TimeBudget;
        }

        private bool Backtrack(
            int index,
            List<SectionVar> order,
            Dictionary<Guid, (List<RoomOption> Rooms, List<FacultyOption> Faculty)> domains,
            IReadOnlyList<TimeSlot> timeSlots,
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
            var (rooms, faculty) = domains[section.SectionId];

            foreach (var value in OrderCandidates(section, rooms, faculty, timeSlots, state))
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
                if (Backtrack(index + 1, order, domains, timeSlots, state, ctx))
                {
                    return true;
                }
                state.Unassign(section, value);

                if (ctx.Aborted)
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Least-constraining / soft-preference value ordering: prefer the faculty member with
        /// the lightest current load (balances distribution) and time slots that sit next to an
        /// existing class for the cohort (reduces idle gaps). Purely an ordering heuristic —
        /// consistency is still enforced by <see cref="SearchState.IsConsistent(SectionVar, SectionAssignment)"/>.
        /// </summary>
        private static IEnumerable<SectionAssignment> OrderCandidates(
            SectionVar section,
            List<RoomOption> rooms,
            List<FacultyOption> faculty,
            IReadOnlyList<TimeSlot> timeSlots,
            SearchState state)
        {
            var candidates =
                from f in faculty
                from t in timeSlots
                from r in rooms
                let softScore = state.SoftScore(section, r, t, f)
                orderby softScore
                select new SectionAssignment(section.SectionId, r.RoomId, t.Id, f.FacultyProfileId);

            return candidates;
        }

        /// <summary>
        /// Failure explanation: replay a deterministic greedy pass (same ordering as the real
        /// search) and, for every section that cannot be placed, tally which hard constraint
        /// each of its candidates collided with. Cheap, deterministic, and tells the Academic
        /// Head what to add or relax — rooms, slots, faculty, or unit ceilings (FR-SCHED-07).
        /// </summary>
        private static IEnumerable<string> Diagnose(
            List<SectionVar> order,
            Dictionary<Guid, (List<RoomOption> Rooms, List<FacultyOption> Faculty)> domains,
            IReadOnlyList<TimeSlot> timeSlots,
            Dictionary<Guid, TimeSlot> timeSlotsById,
            Dictionary<Guid, int> facultyMaxLoad)
        {
            const int maxReports = 5;
            var state = new SearchState(timeSlotsById, facultyMaxLoad);
            var reports = new List<string>();

            foreach (var section in order)
            {
                var (rooms, faculty) = domains[section.SectionId];
                var placedThisSection = false;
                int roomBusy = 0, facultyBusy = 0, cohortBusy = 0, overload = 0, total = 0;

                foreach (var f in faculty)
                {
                    foreach (var t in timeSlots)
                    {
                        foreach (var r in rooms)
                        {
                            total++;
                            var value = new SectionAssignment(section.SectionId, r.RoomId, t.Id, f.FacultyProfileId);
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
                                case ConflictKind.FacultyOverload: overload++; break;
                            }
                        }
                        if (placedThisSection) break;
                    }
                    if (placedThisSection) break;
                }

                if (!placedThisSection && reports.Count < maxReports)
                {
                    var parts = new List<string>();
                    if (roomBusy > 0) parts.Add($"{roomBusy} hit an occupied room");
                    if (facultyBusy > 0) parts.Add($"{facultyBusy} hit a busy faculty member");
                    if (cohortBusy > 0) parts.Add($"{cohortBusy} clashed with the cohort's other classes");
                    if (overload > 0) parts.Add($"{overload} exceeded a faculty unit-load ceiling");
                    reports.Add(
                        $"{section.SectionCode}: none of its {total} candidate placements fit — " +
                        string.Join(", ", parts) + ".");
                }
            }

            if (reports.Count == 0)
            {
                reports.Add(
                    "Every section fits on its own but their combination is infeasible — " +
                    "add rooms or time slots, or spread sections across more faculty.");
            }

            return reports;
        }
    }
}
