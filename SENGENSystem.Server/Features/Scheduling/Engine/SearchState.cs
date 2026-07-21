using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Scheduling.Engine
{
    /// <summary>Which hard constraint a candidate placement ran into (for failure diagnostics).</summary>
    internal enum ConflictKind
    {
        None,
        RoomBusy,
        FacultyBusy,
        CohortBusy
    }

    /// <summary>
    /// Mutable working state of the backtracking search: the sections placed so far.
    /// Encapsulates hard-constraint checking (<see cref="IsConsistent"/>) and soft-preference
    /// scoring (<see cref="SoftScore"/>).
    /// <para>
    /// Faculty unit load (H4) is deliberately absent from the hot path. Because the teaching
    /// faculty comes from the Head's allocation rather than the search, every member's total
    /// load is fixed before the first placement — so it is validated once up front instead of
    /// being re-tested on every candidate.
    /// </para>
    /// </summary>
    internal sealed class SearchState
    {
        private readonly record struct Placed(
            Guid SectionId,
            string CohortKey,
            int Units,
            Guid RoomId,
            Guid FacultyProfileId,
            TimeSlot Slot);

        private readonly Dictionary<Guid, FacultyOption> _facultyById;
        private readonly SoftWeights _weights;
        private readonly List<Placed> _placed = [];

        public SearchState(
            Dictionary<Guid, FacultyOption> facultyById,
            SoftWeights weights)
        {
            _facultyById = facultyById;
            _weights = weights;
        }

        /// <summary>True when the placement violates no hard constraint.</summary>
        public bool IsConsistent(SectionVar section, SectionAssignment value) =>
            IsConsistent(section, value, out _);

        /// <summary>
        /// Hard-constraint check that also reports <paramref name="conflict"/> — the first
        /// constraint the candidate collided with — so failed runs can explain themselves.
        /// </summary>
        public bool IsConsistent(SectionVar section, SectionAssignment value, out ConflictKind conflict)
        {
            var slot = value.Slot;
            foreach (var placed in _placed)
            {
                if (!placed.Slot.OverlapsWith(slot))
                {
                    continue;
                }

                // Overlapping in time — none of these three collisions may happen.
                if (placed.RoomId == value.RoomId)
                {
                    conflict = ConflictKind.RoomBusy;      // H1 room double-booking
                    return false;
                }

                if (placed.FacultyProfileId == value.FacultyProfileId)
                {
                    conflict = ConflictKind.FacultyBusy;   // H2 faculty double-assignment
                    return false;
                }

                if (placed.CohortKey == section.CohortKey)
                {
                    conflict = ConflictKind.CohortBusy;    // H6 cohort clash
                    return false;
                }
            }

            conflict = ConflictKind.None;
            return true;
        }

        public void Assign(SectionVar section, SectionAssignment value) =>
            _placed.Add(new Placed(
                section.SectionId,
                section.CohortKey,
                section.Units,
                value.RoomId,
                value.FacultyProfileId,
                value.Slot));

        public void Unassign(SectionVar section)
        {
            // The search unwinds in stack order, so the matching placement is the last one added.
            for (var i = _placed.Count - 1; i >= 0; i--)
            {
                if (_placed[i].SectionId == section.SectionId)
                {
                    _placed.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// Soft-constraint cost of a candidate placement — lower is better, range 0..1.
        /// Each term is normalised to 0..1 first, then weighted, so no term can dominate by
        /// accident of its units:
        /// <list type="bullet">
        /// <item>S1 preference — 1 when the slot falls outside every window the member declared.</item>
        /// <item>S2 idle gap — how far this class sits from the nearest neighbouring class, for
        /// both the student cohort and the teaching member, scaled against a saturation point.</item>
        /// <item>Room fit — wasted seats as a fraction of the room, so big rooms stay free for
        /// big sections.</item>
        /// </list>
        /// Purely an ordering preference: hard constraints are enforced separately and are
        /// never traded away for a better soft score.
        /// </summary>
        public double SoftScore(SectionVar section, RoomOption room, TimeSlot slot)
        {
            var faculty = _facultyById[section.FacultyProfileId];

            // ---- S1: preferred teaching windows ----
            // A member who declared nothing is treated as indifferent (0), not as dissatisfied,
            // so silence never drags a placement down.
            var preference = faculty.Preferences.Count == 0
                ? 0.0
                : faculty.Preferences.Any(w => w.Contains(slot)) ? 0.0 : 1.0;

            // ---- S2: idle gaps ----
            // Measured in real minutes to the nearest same-day neighbour, not as a flat
            // "adjacent or not" flag: a 30-minute gap and a 4-hour gap are not the same problem.
            var cohortGap = NearestGapHours(slot, p => p.CohortKey == section.CohortKey);
            var facultyGap = NearestGapHours(slot, p => p.FacultyProfileId == section.FacultyProfileId);
            var idleGap = Normalise(Math.Max(cohortGap, facultyGap));

            // ---- Room fit ----
            var roomFit = room.Capacity <= 0
                ? 0.0
                : Math.Clamp((room.Capacity - section.Capacity) / (double)room.Capacity, 0.0, 1.0);

            return _weights.Preference * preference
                + _weights.IdleGap * idleGap
                + _weights.RoomFit * roomFit;
        }

        /// <summary>
        /// Hours of dead time between <paramref name="slot"/> and the closest class already
        /// placed on the same day for the matching cohort or member. Returns 0 when there is
        /// no neighbour that day — an isolated class creates no gap to sit through.
        /// </summary>
        private double NearestGapHours(TimeSlot slot, Func<Placed, bool> matches)
        {
            var smallest = int.MaxValue;
            foreach (var placed in _placed)
            {
                if (placed.Slot.Day != slot.Day || !matches(placed))
                {
                    continue;
                }

                // Distance between the two blocks; 0 when they abut or overlap.
                var gap = placed.Slot.EndMinutes <= slot.StartMinutes
                    ? slot.StartMinutes - placed.Slot.EndMinutes
                    : slot.EndMinutes <= placed.Slot.StartMinutes
                        ? placed.Slot.StartMinutes - slot.EndMinutes
                        : 0;

                if (gap < smallest) smallest = gap;
            }

            return smallest == int.MaxValue ? 0.0 : smallest / 60.0;
        }

        private double Normalise(double gapHours) =>
            Math.Clamp(gapHours / _weights.GapSaturationHours, 0.0, 1.0);

        public IReadOnlyList<SectionAssignment> ToAssignments() =>
            _placed
                .Select(p => new SectionAssignment(p.SectionId, p.RoomId, p.Slot, p.FacultyProfileId))
                .ToList();
    }
}
