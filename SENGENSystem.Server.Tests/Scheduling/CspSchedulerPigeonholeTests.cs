using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Scheduling.Engine;
using static SENGENSystem.Server.Tests.Scheduling.ScheduleProblemBuilder;

namespace SENGENSystem.Server.Tests.Scheduling
{
    /// <summary>
    /// The pigeonhole pre-check, and the known edge it does not cover.
    ///
    /// <para>
    /// The check counts <i>meetings</i> against <i>base time slots</i>. That is sound — it never
    /// rejects a feasible problem — but it is not complete: a meeting that consumes several
    /// consecutive periods eats more of a member's week than one slot, so a problem can be
    /// impossible while still passing the count. These tests pin both the guarantee and the gap, so
    /// that anyone tightening the check can see exactly what changes.
    /// </para>
    /// </summary>
    public class CspSchedulerPigeonholeTests
    {
        private readonly CspScheduler _engine = new();

        [Fact]
        public void The_pre_check_fires_before_the_search_when_meetings_outnumber_slots()
        {
            // The case it does catch: 3 single-period meetings, 2 periods. Steps == 0 is the point —
            // discovering this by exhausting the tree would cost the whole time budget.
            var faculty = Faculty(maxLoadUnits: 99, name: "Prof. Santos");
            var sections = Enumerable.Range(0, 3)
                .Select(i => Meeting(faculty, $"ITP-1{(char)('A' + i)}-IT101", $"ITP-1{(char)('A' + i)}"))
                .ToList();
            var rooms = Enumerable.Range(0, 3).Select(_ => Room(RoomKind.LectureRoom)).ToList();

            var result = _engine.Solve(Problem(sections, rooms, Grid(2), [faculty]));

            Assert.False(result.Success);
            Assert.Equal(0, result.Steps);
        }

        [Fact]
        public void Multi_period_meetings_that_cannot_fit_are_caught_before_the_search()
        {
            // The case meeting-counting alone misses: two 3-hour meetings for one member against
            // three 90-minute periods. The count test sees 2 meetings ≤ 3 slots and waves it
            // through — but 6 hours of teaching cannot fit in a 4.5-hour week, so the minutes test
            // catches it. Before that existed this reached the search and failed there: correct,
            // but at the cost of the time budget.
            var faculty = Faculty(maxLoadUnits: 99, name: "Prof. Lim");
            var sections = new[]
            {
                Meeting(faculty, "ITP-1A-IT102", "ITP-1A",
                    ClassComponent.Laboratory, RoomKind.ComputerLaboratory, requiredMinutes: 180),
                Meeting(faculty, "ITP-1B-IT102", "ITP-1B",
                    ClassComponent.Laboratory, RoomKind.ComputerLaboratory, requiredMinutes: 180)
            };
            var rooms = new[] { Room(RoomKind.ComputerLaboratory), Room(RoomKind.ComputerLaboratory) };

            var result = _engine.Solve(Problem(sections, rooms, Grid(3), [faculty]));

            Assert.False(result.Success);
            Assert.Equal(0, result.Steps);
            var reason = Assert.Single(result.UnplacedReasons);
            Assert.Contains("Prof. Lim", reason);
            // The numbers, not just the verdict — the Head has to know how much to move.
            Assert.Contains("6 hours", reason);
            Assert.Contains("4.5 hours", reason);
        }

        [Fact]
        public void A_cohort_owing_more_class_hours_than_the_week_offers_is_caught_too()
        {
            // The same impossibility from the students' side, and the reason the minutes test is
            // applied to both: a cohort cannot attend 6 hours of class in a 4.5-hour timetable
            // however many staff and rooms are free.
            var a = Faculty(name: "A");
            var b = Faculty(name: "B");
            var sections = new[]
            {
                Meeting(a, "ITP-1A-IT102", "ITP-1A",
                    ClassComponent.Laboratory, RoomKind.ComputerLaboratory, requiredMinutes: 180),
                Meeting(b, "ITP-1A-IT103", "ITP-1A",
                    ClassComponent.Laboratory, RoomKind.ComputerLaboratory, requiredMinutes: 180)
            };
            var rooms = new[] { Room(RoomKind.ComputerLaboratory), Room(RoomKind.ComputerLaboratory) };

            var result = _engine.Solve(Problem(sections, rooms, Grid(3), [a, b]));

            Assert.False(result.Success);
            Assert.Equal(0, result.Steps);
            Assert.Contains(result.UnplacedReasons, r => r.Contains("ITP-1A") && r.Contains("hours"));
        }

        [Fact]
        public void A_multi_period_meeting_that_does_fit_is_still_scheduled()
        {
            // The other side of the same edge, and why tightening the check needs care: two 3-hour
            // meetings across four periods *is* feasible, and a stricter pre-check that counted
            // carelessly would reject a timetable the engine can actually produce.
            var faculty = Faculty(maxLoadUnits: 99);
            var sections = new[]
            {
                Meeting(faculty, "ITP-1A-IT102", "ITP-1A",
                    ClassComponent.Laboratory, RoomKind.ComputerLaboratory, requiredMinutes: 180),
                Meeting(faculty, "ITP-1B-IT102", "ITP-1B",
                    ClassComponent.Laboratory, RoomKind.ComputerLaboratory, requiredMinutes: 180)
            };
            var rooms = new[] { Room(RoomKind.ComputerLaboratory), Room(RoomKind.ComputerLaboratory) };

            var result = _engine.Solve(Problem(sections, rooms, Grid(4), [faculty]));

            AssertHardConstraints(result, sections, rooms);
        }
    }
}
