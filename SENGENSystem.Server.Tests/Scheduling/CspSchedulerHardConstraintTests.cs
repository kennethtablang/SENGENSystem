using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Scheduling.Engine;
using static SENGENSystem.Server.Tests.Scheduling.ScheduleProblemBuilder;

namespace SENGENSystem.Server.Tests.Scheduling
{
    /// <summary>
    /// The engine's actual guarantee: a successful run has zero hard-constraint violations (NFR-1).
    ///
    /// <para>
    /// Every test here asserts the <i>contract</i> rather than a specific timetable. The engine
    /// breaks ties with a seeded shuffle and reorders candidates by soft cost, so pinning exact
    /// placements would make the suite fail whenever the heuristics improved — red for a change that
    /// was an improvement is the fastest way to teach people to ignore a test suite.
    /// </para>
    /// </summary>
    public class CspSchedulerHardConstraintTests
    {
        private readonly CspScheduler _engine = new();

        [Fact]
        public void Two_sections_sharing_a_faculty_member_are_never_placed_at_the_same_time()
        {
            // H2. One member, two classes, and enough room in the grid that the only thing forcing
            // them apart is the constraint itself.
            var faculty = Faculty();
            var sections = new[]
            {
                Meeting(faculty, "ITP-1A-IT101", "ITP-1A"),
                Meeting(faculty, "ITP-1B-IT101", "ITP-1B")
            };
            var rooms = new[] { Room(RoomKind.LectureRoom), Room(RoomKind.LectureRoom) };
            var problem = Problem(sections, rooms, Grid(4), [faculty]);

            AssertHardConstraints(_engine.Solve(problem), sections, rooms);
        }

        [Fact]
        public void Two_sections_of_one_cohort_are_never_placed_at_the_same_time()
        {
            // H6. Different faculty and different rooms, so nothing but the cohort rule separates
            // them — a student cannot be in two classes at once.
            var a = Faculty(name: "A");
            var b = Faculty(name: "B");
            var sections = new[]
            {
                Meeting(a, "ITP-1A-IT101", "ITP-1A"),
                Meeting(b, "ITP-1A-IT102", "ITP-1A")
            };
            var rooms = new[] { Room(RoomKind.LectureRoom), Room(RoomKind.LectureRoom) };
            var problem = Problem(sections, rooms, Grid(4), [a, b]);

            AssertHardConstraints(_engine.Solve(problem), sections, rooms);
        }

        [Fact]
        public void Only_one_class_may_occupy_a_room_at_a_time()
        {
            // H1. A single room and two meetings that share nothing else — the room is the only
            // contested resource, so the engine must spread them across the grid.
            var a = Faculty(name: "A");
            var b = Faculty(name: "B");
            var sections = new[]
            {
                Meeting(a, "ITP-1A-IT101", "ITP-1A"),
                Meeting(b, "ITP-2A-IT201", "ITP-2A")
            };
            var rooms = new[] { Room(RoomKind.LectureRoom) };
            var problem = Problem(sections, rooms, Grid(4), [a, b]);

            AssertHardConstraints(_engine.Solve(problem), sections, rooms);
        }

        [Fact]
        public void A_laboratory_meeting_lands_in_the_laboratory_kind_it_requires()
        {
            // H3b, and the reason it exists: a computer laboratory and a kitchen laboratory are not
            // interchangeable, so an ITP lab must never be placed in the HRA kitchen even when the
            // kitchen is free and the right size.
            var faculty = Faculty();
            var sections = new[]
            {
                Meeting(faculty, "ITP-1A-IT102", "ITP-1A",
                    ClassComponent.Laboratory, RoomKind.ComputerLaboratory, requiredMinutes: 180)
            };
            var rooms = new[]
            {
                Room(RoomKind.LectureRoom),
                Room(RoomKind.KitchenLaboratory),
                Room(RoomKind.ComputerLaboratory)
            };
            var problem = Problem(sections, rooms, Grid(4), [faculty]);

            var result = _engine.Solve(problem);

            AssertHardConstraints(result, sections, rooms);
            var placed = Assert.Single(result.Assignments);
            Assert.Equal(RoomKind.ComputerLaboratory, rooms.Single(r => r.RoomId == placed.RoomId).Kind);
        }

        [Fact]
        public void A_section_never_lands_in_a_room_too_small_for_it()
        {
            // H3a. The bigger room is the only legal answer even though the smaller one is a
            // tighter — and therefore softly preferred — fit.
            var faculty = Faculty();
            var sections = new[] { Meeting(faculty, "ITP-1A-IT101", "ITP-1A", capacity: 40) };
            var rooms = new[] { Room(RoomKind.LectureRoom, capacity: 20), Room(RoomKind.LectureRoom, capacity: 45) };
            var problem = Problem(sections, rooms, Grid(4), [faculty]);

            var result = _engine.Solve(problem);

            AssertHardConstraints(result, sections, rooms);
            Assert.Equal(45, rooms.Single(r => r.RoomId == result.Assignments[0].RoomId).Capacity);
        }

        [Fact]
        public void A_lecture_laboratory_subject_is_placed_as_two_meetings_that_do_not_collide()
        {
            // The derived-component rule: one subject, two variables, sharing a cohort — so H6 is
            // what keeps a subject's own lecture and laboratory off each other.
            var faculty = Faculty();
            var sectionId = Guid.NewGuid();
            var sections = new[]
            {
                Meeting(faculty, "ITP-1A-IT102", "ITP-1A",
                    ClassComponent.Lecture, RoomKind.LectureRoom, requiredMinutes: 90, units: 3, sectionId: sectionId),
                // Units 0 on the laboratory half, so the faculty ceiling counts the subject once.
                Meeting(faculty, "ITP-1A-IT102", "ITP-1A",
                    ClassComponent.Laboratory, RoomKind.ComputerLaboratory, requiredMinutes: 180, units: 0, sectionId: sectionId)
            };
            var rooms = new[] { Room(RoomKind.LectureRoom), Room(RoomKind.ComputerLaboratory) };
            var problem = Problem(sections, rooms, Grid(6), [faculty]);

            var result = _engine.Solve(problem);

            AssertHardConstraints(result, sections, rooms);
            var lecture = result.Assignments.Single(a => a.Component == ClassComponent.Lecture);
            var lab = result.Assignments.Single(a => a.Component == ClassComponent.Laboratory);
            Assert.False(lecture.Slot.OverlapsWith(lab.Slot));
        }

        [Fact]
        public void A_multi_period_meeting_gets_one_contiguous_block_of_exactly_its_length()
        {
            // A 3-hour laboratory on a 90-minute grid must occupy two adjacent periods as a single
            // block — not two separate 90-minute placements, and not a 270-minute over-allocation.
            var faculty = Faculty();
            var sections = new[]
            {
                Meeting(faculty, "ITP-1A-IT102", "ITP-1A",
                    ClassComponent.Laboratory, RoomKind.ComputerLaboratory, requiredMinutes: 180)
            };
            var rooms = new[] { Room(RoomKind.ComputerLaboratory) };
            var problem = Problem(sections, rooms, Grid(4), [faculty]);

            var result = _engine.Solve(problem);

            AssertHardConstraints(result, sections, rooms);
            var slot = Assert.Single(result.Assignments).Slot;
            Assert.Equal(180, slot.EndMinutes - slot.StartMinutes);
        }

        [Fact]
        public void A_long_block_is_never_placed_across_a_break_in_the_day()
        {
            // The grid here has 08:00–09:30 and 11:00–12:30 with the middle missing. A 3-hour block
            // would have to span the break, so it must be refused rather than scheduled over lunch.
            var faculty = Faculty();
            var sections = new[]
            {
                Meeting(faculty, "ITP-1A-IT102", "ITP-1A",
                    ClassComponent.Laboratory, RoomKind.ComputerLaboratory, requiredMinutes: 180)
            };
            var rooms = new[] { Room(RoomKind.ComputerLaboratory) };
            var problem = Problem(sections, rooms, GridWithLunchGap(), [faculty]);

            var result = _engine.Solve(problem);

            Assert.False(result.Success);
            Assert.Contains(result.UnplacedReasons, r => r.Contains("continuous block"));
        }

        [Fact]
        public void A_full_grid_is_packed_without_a_single_violation()
        {
            // The interesting case: demand exactly equal to supply, so every constraint binds at
            // once and there is only one shape of answer. Four cohorts, four members, two rooms,
            // eight periods.
            var faculty = Enumerable.Range(0, 4).Select(i => Faculty(name: $"Member {i}")).ToList();
            var sections = new List<SectionVar>();
            for (var i = 0; i < 4; i++)
            {
                sections.Add(Meeting(faculty[i], $"ITP-{i}A-IT10{i}", $"ITP-{i}A"));
                sections.Add(Meeting(faculty[i], $"ITP-{i}B-IT10{i}", $"ITP-{i}B"));
            }
            var rooms = new[] { Room(RoomKind.LectureRoom), Room(RoomKind.LectureRoom) };
            var problem = Problem(sections, rooms, Grid(4, DayOfWeek.Monday, DayOfWeek.Tuesday), faculty);

            AssertHardConstraints(_engine.Solve(problem), sections, rooms);
        }
    }
}
