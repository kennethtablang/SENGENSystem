using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Scheduling.Engine;
using static SENGENSystem.Server.Tests.Scheduling.ScheduleProblemBuilder;

namespace SENGENSystem.Server.Tests.Scheduling
{
    /// <summary>
    /// What the engine says when it cannot succeed (FR-SCHED-07).
    ///
    /// <para>
    /// These matter as much as the placements. The Academic Head cannot read a search tree, so an
    /// infeasible run has to come back with a sentence naming what to change — and the pre-flight
    /// checks have to fire <i>before</i> the search, or the answer arrives twenty seconds later
    /// having burned a time budget on a problem that was impossible from the first line.
    /// </para>
    /// </summary>
    public class CspSchedulerDiagnosticsTests
    {
        private readonly CspScheduler _engine = new();

        [Fact]
        public void Empty_resource_pools_are_named_individually()
        {
            var problem = Problem([], [], [], []);

            var result = _engine.Solve(problem);

            Assert.False(result.Success);
            Assert.Equal(0, result.Steps);
            Assert.Contains(result.UnplacedReasons, r => r.Contains("no sections"));
            Assert.Contains(result.UnplacedReasons, r => r.Contains("No rooms"));
            Assert.Contains(result.UnplacedReasons, r => r.Contains("No time slots"));
            Assert.Contains(result.UnplacedReasons, r => r.Contains("No faculty"));
        }

        [Fact]
        public void An_overloaded_faculty_member_is_named_with_the_numbers_and_the_sections()
        {
            // H4 is a property of the allocation, not of any placement, so it is decided before the
            // search — Steps == 0 is the assertion that this did not cost a doomed search.
            var faculty = Faculty(maxLoadUnits: 6, name: "Prof. Reyes");
            var sections = new[]
            {
                Meeting(faculty, "ITP-1A-IT101", "ITP-1A", units: 3),
                Meeting(faculty, "ITP-1B-IT101", "ITP-1B", units: 3),
                Meeting(faculty, "ITP-1C-IT101", "ITP-1C", units: 3)
            };
            var problem = Problem(sections, [Room(RoomKind.LectureRoom)], Grid(6), [faculty]);

            var result = _engine.Solve(problem);

            Assert.False(result.Success);
            Assert.Equal(0, result.Steps);
            var reason = Assert.Single(result.UnplacedReasons);
            Assert.Contains("Prof. Reyes", reason);
            Assert.Contains("9 units", reason);
            Assert.Contains("ceiling of 6", reason);
            // The sections at fault, so the Head knows what to rebalance.
            Assert.Contains("ITP-1A-IT101", reason);
        }

        [Fact]
        public void A_missing_room_kind_says_which_kind_and_that_none_is_configured()
        {
            var faculty = Faculty();
            var sections = new[]
            {
                Meeting(faculty, "HRA-1A-HRA102", "HRA-1A",
                    ClassComponent.Laboratory, RoomKind.KitchenLaboratory, requiredMinutes: 180)
            };
            // Only a computer laboratory exists — the wrong kind entirely.
            var problem = Problem(sections, [Room(RoomKind.ComputerLaboratory)], Grid(4), [faculty]);

            var result = _engine.Solve(problem);

            Assert.False(result.Success);
            var reason = Assert.Single(result.UnplacedReasons);
            Assert.Contains("HRA-1A-HRA102", reason);
            Assert.Contains("none is configured", reason);
        }

        [Fact]
        public void A_room_of_the_right_kind_but_too_small_says_so_distinctly()
        {
            // Deliberately a different message from "none is configured": the remedy is not the
            // same, and telling someone to add a laboratory they already have wastes their time.
            var faculty = Faculty();
            var sections = new[] { Meeting(faculty, "ITP-1A-IT101", "ITP-1A", capacity: 40) };
            var problem = Problem(sections, [Room(RoomKind.LectureRoom, capacity: 10)], Grid(4), [faculty]);

            var result = _engine.Solve(problem);

            Assert.False(result.Success);
            var reason = Assert.Single(result.UnplacedReasons);
            Assert.Contains("capacity", reason);
            Assert.DoesNotContain("none is configured", reason);
        }

        [Fact]
        public void More_meetings_than_time_slots_for_one_member_is_caught_by_the_pigeonhole_check()
        {
            // Three meetings, two periods, one member. No branch of the search can succeed, and
            // discovering that by exhausting the tree would cost the whole time budget.
            var faculty = Faculty(maxLoadUnits: 99, name: "Prof. Cruz");
            var sections = new[]
            {
                Meeting(faculty, "ITP-1A-IT101", "ITP-1A"),
                Meeting(faculty, "ITP-1B-IT101", "ITP-1B"),
                Meeting(faculty, "ITP-1C-IT101", "ITP-1C")
            };
            var rooms = new[] { Room(RoomKind.LectureRoom), Room(RoomKind.LectureRoom), Room(RoomKind.LectureRoom) };
            var problem = Problem(sections, rooms, Grid(2), [faculty]);

            var result = _engine.Solve(problem);

            Assert.False(result.Success);
            Assert.Equal(0, result.Steps);
            Assert.Contains(result.UnplacedReasons, r => r.Contains("Prof. Cruz") && r.Contains("time slots"));
        }

        [Fact]
        public void More_meetings_than_time_slots_for_one_cohort_is_caught_too()
        {
            // The same impossibility from the students' side: one cohort cannot attend three
            // classes across two periods however many staff and rooms are free.
            var faculty = Enumerable.Range(0, 3).Select(i => Faculty(name: $"M{i}")).ToList();
            var sections = faculty
                .Select((f, i) => Meeting(f, $"ITP-1A-IT10{i}", "ITP-1A"))
                .ToList();
            var rooms = new[] { Room(RoomKind.LectureRoom), Room(RoomKind.LectureRoom), Room(RoomKind.LectureRoom) };
            var problem = Problem(sections, rooms, Grid(2), faculty);

            var result = _engine.Solve(problem);

            Assert.False(result.Success);
            Assert.Contains(result.UnplacedReasons, r => r.Contains("ITP-1A"));
        }

        [Fact]
        public void A_section_allocated_to_a_faculty_member_who_no_longer_exists_is_reported()
        {
            // The allocation references someone deleted since. Without this the engine would fault
            // on a dictionary miss instead of telling anyone what to fix.
            var real = Faculty(name: "Present");
            var ghost = Faculty(name: "Deleted");
            var sections = new[] { Meeting(ghost, "ITP-1A-IT101", "ITP-1A") };
            var problem = Problem(sections, [Room(RoomKind.LectureRoom)], Grid(4), [real]);

            var result = _engine.Solve(problem);

            Assert.False(result.Success);
            var reason = Assert.Single(result.UnplacedReasons);
            Assert.Contains("no longer exists", reason);
            Assert.Contains("ITP-1A-IT101", reason);
        }
    }
}
