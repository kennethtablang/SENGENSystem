using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Scheduling.Engine;

namespace SENGENSystem.Server.Tests.Scheduling
{
    /// <summary>
    /// Builds <see cref="ScheduleProblem"/>s for the engine tests, and — more importantly — asserts
    /// the guarantee the engine actually makes.
    ///
    /// <para>
    /// <see cref="AssertHardConstraints"/> is the centre of this file. The engine's contract is
    /// "a successful result has zero hard-constraint violations" (NFR-1), and the only honest way to
    /// test a search is to check its <i>output</i> against that contract rather than to assert a
    /// particular timetable. A test that pins exact placements would break every time the heuristics
    /// improved, which is precisely when you least want your safety net to go red for no reason.
    /// </para>
    /// </summary>
    internal static class ScheduleProblemBuilder
    {
        public const int NineAm = 9 * 60;

        /// <summary>A day of back-to-back 90-minute periods starting at 09:00.</summary>
        public static List<TimeSlot> Grid(int periodsPerDay, params DayOfWeek[] days)
        {
            var slots = new List<TimeSlot>();
            foreach (var day in days.Length > 0 ? days : [DayOfWeek.Monday])
            {
                for (var i = 0; i < periodsPerDay; i++)
                {
                    slots.Add(new TimeSlot
                    {
                        Day = day,
                        StartMinutes = NineAm + i * 90,
                        EndMinutes = NineAm + (i + 1) * 90
                    });
                }
            }
            return slots;
        }

        /// <summary>
        /// A grid with a lunch break splitting the day, so no run of periods spans it. Used to prove
        /// that a long block is never offered across a gap.
        /// </summary>
        public static List<TimeSlot> GridWithLunchGap(DayOfWeek day = DayOfWeek.Monday) =>
        [
            new TimeSlot { Day = day, StartMinutes = 8 * 60, EndMinutes = 9 * 60 + 30 },
            // 09:30 → 11:00 missing: the break.
            new TimeSlot { Day = day, StartMinutes = 11 * 60, EndMinutes = 12 * 60 + 30 }
        ];

        public static RoomOption Room(RoomKind kind, int capacity = 40) => new(Guid.NewGuid(), capacity, kind);

        public static FacultyOption Faculty(
            int maxLoadUnits = 30,
            string name = "Test Faculty",
            IReadOnlyList<PreferredWindow>? windows = null) =>
            new(Guid.NewGuid(), "ITP", maxLoadUnits, windows, name);

        public static SectionVar Meeting(
            FacultyOption faculty,
            string sectionCode,
            string cohortKey,
            ClassComponent component = ClassComponent.Lecture,
            RoomKind roomKind = RoomKind.LectureRoom,
            int requiredMinutes = 90,
            int units = 3,
            int capacity = 40,
            Guid? sectionId = null) =>
            new(sectionId ?? Guid.NewGuid(), sectionCode, "ITP", cohortKey, capacity, units,
                component, roomKind, faculty.FacultyProfileId, requiredMinutes);

        public static ScheduleProblem Problem(
            IReadOnlyList<SectionVar> sections,
            IReadOnlyList<RoomOption> rooms,
            IReadOnlyList<TimeSlot> slots,
            IReadOnlyList<FacultyOption> faculty,
            int seed = 1) =>
            new()
            {
                Sections = sections,
                Rooms = rooms,
                TimeSlots = slots,
                Faculty = faculty,
                Seed = seed,
                // Small, bounded budgets: these problems are tiny, and a test that can hang for 20
                // seconds on a regression is a test people start skipping.
                TimeBudget = TimeSpan.FromSeconds(5),
                MaxSteps = 200_000
            };

        /// <summary>
        /// Every hard constraint the engine promises, checked against a produced timetable:
        /// H1 no room double-booking, H2 no faculty double-booking, H6 no cohort clash,
        /// H3a capacity, H3b room-kind suitability — plus full coverage and exact block length.
        /// </summary>
        public static void AssertHardConstraints(
            ScheduleGenerationResult result,
            IReadOnlyList<SectionVar> sections,
            IReadOnlyList<RoomOption> rooms)
        {
            Assert.True(result.Success,
                "Expected a feasible schedule. Reasons: " + string.Join(" | ", result.UnplacedReasons));

            var byKey = sections.ToDictionary(s => s.Key);
            var roomsById = rooms.ToDictionary(r => r.RoomId);

            // Coverage: every meeting placed, exactly once. A lecture-laboratory subject is two
            // meetings and must appear twice — once per component.
            Assert.Equal(sections.Count, result.Assignments.Count);
            Assert.Equal(
                sections.Select(s => s.Key).OrderBy(k => k.SectionId).ThenBy(k => k.Component),
                result.Assignments.Select(a => (a.SectionId, a.Component)).OrderBy(k => k.SectionId).ThenBy(k => k.Component));

            foreach (var assignment in result.Assignments)
            {
                var section = byKey[(assignment.SectionId, assignment.Component)];
                var room = roomsById[assignment.RoomId];

                // H3b — the room-kind rule. This is the one that decides whether an ITP class and
                // an HRA class contend for the same laboratory at all.
                Assert.Equal(section.RequiredRoomKind, room.Kind);

                // H3a — capacity.
                Assert.True(room.Capacity >= section.Capacity,
                    $"{section.Label} placed in a room that seats {room.Capacity} but needs {section.Capacity}.");

                // Weekly-hours coverage: the block is exactly as long as the meeting requires.
                Assert.Equal(section.RequiredMinutes, assignment.Slot.EndMinutes - assignment.Slot.StartMinutes);

                // The faculty is not a decision variable — it must survive untouched.
                Assert.Equal(section.FacultyProfileId, assignment.FacultyProfileId);
            }

            // The three pairwise collisions, over every overlapping pair.
            for (var i = 0; i < result.Assignments.Count; i++)
            {
                for (var j = i + 1; j < result.Assignments.Count; j++)
                {
                    var a = result.Assignments[i];
                    var b = result.Assignments[j];
                    if (!a.Slot.OverlapsWith(b.Slot)) continue;

                    var sa = byKey[(a.SectionId, a.Component)];
                    var sb = byKey[(b.SectionId, b.Component)];

                    Assert.True(a.RoomId != b.RoomId,
                        $"H1 violated: {sa.Label} and {sb.Label} share a room at the same time.");
                    Assert.True(a.FacultyProfileId != b.FacultyProfileId,
                        $"H2 violated: {sa.Label} and {sb.Label} share a faculty member at the same time.");
                    Assert.False(string.Equals(sa.CohortKey, sb.CohortKey, StringComparison.OrdinalIgnoreCase),
                        $"H6 violated: cohort {sa.CohortKey} has {sa.Label} and {sb.Label} at the same time.");
                }
            }
        }
    }
}
