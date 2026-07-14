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
    /// </summary>
    public sealed class CspScheduler
    {
        // Guard against pathological inputs so a run always terminates in practical time (FR-SCHED-07).
        private const int MaxSteps = 2_000_000;

        public ScheduleGenerationResult Solve(ScheduleProblem problem)
        {
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

            // Most-constrained-variable ordering: hardest sections (fewest candidate rooms×faculty,
            // labs, largest cohorts) are placed first to prune the search tree early.
            var order = problem.Sections
                .OrderBy(s => domains[s.SectionId].Rooms.Count * domains[s.SectionId].Faculty.Count)
                .ThenByDescending(s => s.RequiresLaboratory)
                .ThenByDescending(s => s.Units)
                .ToList();

            var facultyMaxLoad = problem.Faculty.ToDictionary(f => f.FacultyProfileId, f => f.MaxLoadUnits);
            var state = new SearchState(timeSlotsById, facultyMaxLoad);
            var steps = 0;
            var aborted = false;

            var solved = Backtrack(0, order, domains, problem.TimeSlots, state, ref steps, ref aborted);

            if (solved)
            {
                return ScheduleGenerationResult.Ok(state.ToAssignments(), steps);
            }

            var reason = aborted
                ? $"Search did not converge within {MaxSteps:N0} steps — inputs may be over-constrained (too many sections for the available rooms/slots/faculty)."
                : "No conflict-free schedule exists for the given sections, rooms, time slots, and faculty. Add rooms/slots or relax faculty loads and retry.";
            return ScheduleGenerationResult.Fail([reason], steps);
        }

        private bool Backtrack(
            int index,
            List<SectionVar> order,
            Dictionary<Guid, (List<RoomOption> Rooms, List<FacultyOption> Faculty)> domains,
            IReadOnlyList<TimeSlot> timeSlots,
            SearchState state,
            ref int steps,
            ref bool aborted)
        {
            if (index == order.Count)
            {
                return true;
            }

            if (steps >= MaxSteps)
            {
                aborted = true;
                return false;
            }

            var section = order[index];
            var (rooms, faculty) = domains[section.SectionId];

            foreach (var value in OrderCandidates(section, rooms, faculty, timeSlots, state))
            {
                steps++;
                if (steps >= MaxSteps)
                {
                    aborted = true;
                    return false;
                }

                if (!state.IsConsistent(section, value))
                {
                    continue;
                }

                state.Assign(section, value);
                if (Backtrack(index + 1, order, domains, timeSlots, state, ref steps, ref aborted))
                {
                    return true;
                }
                state.Unassign(section, value);

                if (aborted)
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
        /// consistency is still enforced by <see cref="SearchState.IsConsistent"/>.
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
    }
}
