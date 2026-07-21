using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Scheduling.Engine
{
    /// <summary>
    /// What the engine achieved on the soft constraints, measured on the finished timetable.
    /// A schedule with zero hard violations can still be a poor one to live with; this is how
    /// the Academic Head sees the trade-offs that were made rather than taking them on trust.
    /// </summary>
    public sealed record OptimizationReport(
        int PreferencesHonored,
        int PreferencesApplicable,
        double CohortIdleHours,
        double FacultyIdleHours,
        double LoadSpread,
        int MinLoadUnits,
        int MaxLoadUnits,
        double AverageRoomFitPct)
    {
        /// <summary>Share of placements that landed inside a declared window, 0 when none applied.</summary>
        public double PreferenceHonorRatePct => PreferencesApplicable == 0
            ? 100
            : Math.Round(100.0 * PreferencesHonored / PreferencesApplicable, 1);

        /// <summary>
        /// Equity flag (S3). The engine no longer chooses faculty, so it cannot fix an
        /// unbalanced allocation — it reports the spread so the Head can correct the load
        /// assignment and regenerate.
        /// <para>
        /// The test is relative, not a flat unit difference: one member carrying at least
        /// double another is what "uneven" means to the people involved. A 3-vs-9 split is a
        /// real imbalance; 21-vs-27 is the same six-unit gap and barely worth mentioning. The
        /// absolute floor stops trivial loads (1 vs 3 units) tripping the flag.
        /// </para>
        /// </summary>
        public bool LoadLooksUneven =>
            MinLoadUnits > 0
            && MaxLoadUnits >= MinLoadUnits * 2
            && MaxLoadUnits - MinLoadUnits >= 3;

        public static readonly OptimizationReport Empty =
            new(0, 0, 0, 0, 0, 0, 0, 0);

        /// <summary>
        /// Measures the finished timetable. Idle hours are counted per (day, cohort) and
        /// (day, member) chain: sort that day's classes and sum the dead time between
        /// consecutive ones, which is the waiting a real person actually experiences.
        /// </summary>
        public static OptimizationReport Measure(
            IReadOnlyList<SectionAssignment> assignments,
            IReadOnlyList<SectionVar> sections,
            IReadOnlyList<RoomOption> rooms,
            IReadOnlyList<FacultyOption> faculty)
        {
            if (assignments.Count == 0) return Empty;

            var sectionById = sections.ToDictionary(s => s.SectionId);
            var roomById = rooms.ToDictionary(r => r.RoomId);
            var facultyById = faculty.ToDictionary(f => f.FacultyProfileId);

            var placed = assignments
                .Where(a => sectionById.ContainsKey(a.SectionId))
                .Select(a => new
                {
                    Section = sectionById[a.SectionId],
                    Slot = a.Slot,
                    Room = roomById.GetValueOrDefault(a.RoomId),
                    a.FacultyProfileId
                })
                .ToList();

            // ---- S1: preferences honored ----
            var applicable = placed
                .Where(p => facultyById.TryGetValue(p.FacultyProfileId, out var f) && f.Preferences.Count > 0)
                .ToList();
            var honored = applicable
                .Count(p => facultyById[p.FacultyProfileId].Preferences.Any(w => w.Contains(p.Slot)));

            // ---- S2: idle gaps ----
            var cohortIdle = IdleHours(placed.Select(p => (Key: p.Section.CohortKey, p.Slot)));
            var facultyIdle = IdleHours(placed.Select(p => (Key: p.FacultyProfileId.ToString(), p.Slot)));

            // ---- S3: equity of the allocation the Head made ----
            var unitsByFaculty = placed
                .GroupBy(p => p.FacultyProfileId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Section.Units));
            var loads = unitsByFaculty.Values.ToList();
            var mean = loads.Count == 0 ? 0 : loads.Average();
            var spread = loads.Count == 0
                ? 0
                : Math.Sqrt(loads.Sum(u => (u - mean) * (u - mean)) / loads.Count);

            // ---- Room fit ----
            var fits = placed
                .Where(p => p.Room is { Capacity: > 0 })
                .Select(p => 100.0 * p.Section.Capacity / p.Room!.Capacity)
                .ToList();

            return new OptimizationReport(
                honored,
                applicable.Count,
                Math.Round(cohortIdle, 1),
                Math.Round(facultyIdle, 1),
                Math.Round(spread, 1),
                loads.Count == 0 ? 0 : loads.Min(),
                loads.Count == 0 ? 0 : loads.Max(),
                fits.Count == 0 ? 0 : Math.Round(fits.Average(), 1));
        }

        /// <summary>Total dead hours between consecutive same-day classes, summed per key.</summary>
        private static double IdleHours(IEnumerable<(string Key, TimeSlot Slot)> items)
        {
            var total = 0.0;
            foreach (var perDay in items.GroupBy(x => (x.Key, x.Slot.Day)))
            {
                var ordered = perDay.Select(x => x.Slot).OrderBy(s => s.StartMinutes).ToList();
                for (var i = 1; i < ordered.Count; i++)
                {
                    var gap = ordered[i].StartMinutes - ordered[i - 1].EndMinutes;
                    if (gap > 0) total += gap / 60.0;
                }
            }
            return total;
        }
    }
}
