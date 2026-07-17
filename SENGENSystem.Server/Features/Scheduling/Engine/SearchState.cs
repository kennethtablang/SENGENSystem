using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Scheduling.Engine
{
    /// <summary>Which hard constraint a candidate assignment ran into (for failure diagnostics).</summary>
    internal enum ConflictKind
    {
        None,
        FacultyOverload,
        RoomBusy,
        FacultyBusy,
        CohortBusy
    }

    /// <summary>
    /// Mutable working state of the backtracking search: the sections placed so far and the
    /// running faculty load. Encapsulates hard-constraint checking (<see cref="IsConsistent"/>)
    /// and soft-preference scoring (<see cref="SoftScore"/>).
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

        private readonly Dictionary<Guid, TimeSlot> _timeSlotsById;
        private readonly Dictionary<Guid, int> _facultyMaxLoad;
        private readonly Dictionary<Guid, int> _facultyLoad = new();
        private readonly List<Placed> _placed = new();

        public SearchState(Dictionary<Guid, TimeSlot> timeSlotsById, Dictionary<Guid, int> facultyMaxLoad)
        {
            _timeSlotsById = timeSlotsById;
            _facultyMaxLoad = facultyMaxLoad;
        }

        /// <summary>True when assigning <paramref name="value"/> to <paramref name="section"/> violates no hard constraint.</summary>
        public bool IsConsistent(SectionVar section, SectionAssignment value) =>
            IsConsistent(section, value, out _);

        /// <summary>
        /// Hard-constraint check that also reports <paramref name="conflict"/> — the first
        /// constraint the candidate collided with — so failed runs can explain themselves.
        /// </summary>
        public bool IsConsistent(SectionVar section, SectionAssignment value, out ConflictKind conflict)
        {
            // Faculty unit-load ceiling (FR-SCHED-02, FR-FAC-03).
            var projectedLoad = _facultyLoad.GetValueOrDefault(value.FacultyProfileId) + section.Units;
            if (projectedLoad > _facultyMaxLoad[value.FacultyProfileId])
            {
                conflict = ConflictKind.FacultyOverload;
                return false;
            }

            var slot = _timeSlotsById[value.TimeSlotId];
            foreach (var placed in _placed)
            {
                if (!placed.Slot.OverlapsWith(slot))
                {
                    continue;
                }

                // Overlapping in time — none of these three collisions may happen.
                if (placed.RoomId == value.RoomId)
                {
                    conflict = ConflictKind.RoomBusy; // room double-booking
                    return false;
                }

                if (placed.FacultyProfileId == value.FacultyProfileId)
                {
                    conflict = ConflictKind.FacultyBusy; // faculty double-assignment
                    return false;
                }

                if (placed.CohortKey == section.CohortKey)
                {
                    conflict = ConflictKind.CohortBusy; // two classes for the same student cohort at once
                    return false;
                }
            }

            conflict = ConflictKind.None;
            return true;
        }

        public void Assign(SectionVar section, SectionAssignment value)
        {
            _placed.Add(new Placed(
                section.SectionId,
                section.CohortKey,
                section.Units,
                value.RoomId,
                value.FacultyProfileId,
                _timeSlotsById[value.TimeSlotId]));

            _facultyLoad[value.FacultyProfileId] = _facultyLoad.GetValueOrDefault(value.FacultyProfileId) + section.Units;
        }

        public void Unassign(SectionVar section, SectionAssignment value)
        {
            // The search unwinds in stack order, so the matching placement is the last one added.
            for (var i = _placed.Count - 1; i >= 0; i--)
            {
                if (_placed[i].SectionId == section.SectionId)
                {
                    _placed.RemoveAt(i);
                    break;
                }
            }

            _facultyLoad[value.FacultyProfileId] -= section.Units;
        }

        /// <summary>
        /// Lower is better. Encodes soft constraints (FR-SCHED-03): balance faculty load
        /// (dominant term), honor faculty time-slot preferences, keep a cohort's classes
        /// contiguous to cut idle gaps, and prefer a snug room so large rooms stay free for
        /// large sections.
        /// </summary>
        public int SoftScore(SectionVar section, RoomOption room, TimeSlot slot, FacultyOption faculty)
        {
            var score = _facultyLoad.GetValueOrDefault(faculty.FacultyProfileId) * 10;
            score += room.Capacity;

            // Faculty time-slot preferences: reward a slot inside a preferred window, penalize
            // one outside (only when the member declared preferences at all).
            if (faculty.Preferences.Count > 0)
            {
                score += faculty.Preferences.Any(w => w.Contains(slot)) ? -8 : 4;
            }

            foreach (var placed in _placed)
            {
                if (placed.CohortKey != section.CohortKey || placed.Slot.Day != slot.Day)
                {
                    continue;
                }

                var adjacent = placed.Slot.EndMinutes == slot.StartMinutes || slot.EndMinutes == placed.Slot.StartMinutes;
                score += adjacent ? -5 : 1;
            }

            return score;
        }

        public IReadOnlyList<SectionAssignment> ToAssignments() =>
            _placed
                .Select(p => new SectionAssignment(p.SectionId, p.RoomId, p.Slot.Id, p.FacultyProfileId))
                .ToList();
    }
}
