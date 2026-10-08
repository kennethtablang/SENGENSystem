using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Scheduling.Engine;
using static SENGENSystem.Server.Tests.Scheduling.ScheduleProblemBuilder;

namespace SENGENSystem.Server.Tests.Scheduling
{
    /// <summary>
    /// Block granularity: what happens when a subject's weekly hours are not a multiple of the
    /// 90-minute base period.
    ///
    /// <para>
    /// The backlog carried a finding that "a 2-hour subject rounds up to a 3h block (full coverage,
    /// never short)". These tests exist to pin down whether that is still true, because the current
    /// <c>BuildContiguousBlocks</c> anchors a block of <i>exactly</i> the required length at a period
    /// start rather than consuming whole periods — and an over-allocating engine and an exact one
    /// are very different things to plan a timetable around.
    /// </para>
    /// </summary>
    public class CspSchedulerBlockGranularityTests
    {
        private readonly CspScheduler _engine = new();

        [Theory]
        [InlineData(60)]    // 1 hour — shorter than one base period
        [InlineData(120)]   // 2 hours — the case the finding named
        [InlineData(150)]   // 2.5 hours — straddles two periods unevenly
        [InlineData(180)]   // 3 hours — exactly two periods
        public void A_meeting_occupies_exactly_its_required_minutes(int requiredMinutes)
        {
            var faculty = Faculty();
            var sections = new[]
            {
                Meeting(faculty, "ITP-1A-IT101", "ITP-1A", requiredMinutes: requiredMinutes)
            };
            var rooms = new[] { Room(RoomKind.LectureRoom) };
            var problem = Problem(sections, rooms, Grid(4), [faculty]);

            var result = _engine.Solve(problem);

            AssertHardConstraints(result, sections, rooms);
            var slot = Assert.Single(result.Assignments).Slot;
            Assert.Equal(requiredMinutes, slot.EndMinutes - slot.StartMinutes);
        }

        [Fact]
        public void A_two_hour_meeting_is_not_rounded_up_to_a_three_hour_block()
        {
            // The finding, tested directly. Rounding up would cost the institution an hour of room
            // and faculty availability per such subject per week — invisible on the timetable, since
            // the block simply looks longer than the subject.
            var faculty = Faculty();
            var sections = new[]
            {
                Meeting(faculty, "ITP-1A-IT101", "ITP-1A", requiredMinutes: 120)
            };
            var problem = Problem(sections, [Room(RoomKind.LectureRoom)], Grid(4), [faculty]);

            var slot = Assert.Single(_engine.Solve(problem).Assignments).Slot;

            Assert.Equal(120, slot.EndMinutes - slot.StartMinutes);
            Assert.NotEqual(180, slot.EndMinutes - slot.StartMinutes);
        }

        [Fact]
        public void An_odd_length_block_still_starts_on_a_period_boundary()
        {
            // Exact-length blocks must not drift to arbitrary start times: a 09:07 class would be
            // correct by the arithmetic and useless to a school. The block is anchored at the start
            // of a real period, which is what keeps it on the published grid.
            var faculty = Faculty();
            var sections = new[]
            {
                Meeting(faculty, "ITP-1A-IT101", "ITP-1A", requiredMinutes: 120)
            };
            var problem = Problem(sections, [Room(RoomKind.LectureRoom)], Grid(4), [faculty]);

            var slot = Assert.Single(_engine.Solve(problem).Assignments).Slot;

            var offsetFromFirstPeriod = slot.StartMinutes - NineAm;
            Assert.Equal(0, offsetFromFirstPeriod % 90);
        }

        [Fact]
        public void A_meeting_longer_than_the_whole_day_is_refused_with_a_clear_reason()
        {
            // Four 90-minute periods is six hours; a seven-hour meeting cannot fit however the
            // blocks are cut, and must say so rather than silently shortening itself.
            var faculty = Faculty();
            var sections = new[]
            {
                Meeting(faculty, "ITP-1A-IT101", "ITP-1A", requiredMinutes: 7 * 60)
            };
            var problem = Problem(sections, [Room(RoomKind.LectureRoom)], Grid(4), [faculty]);

            var result = _engine.Solve(problem);

            Assert.False(result.Success);
            Assert.Contains(result.UnplacedReasons, r => r.Contains("continuous block"));
        }

        [Fact]
        public void Two_odd_length_meetings_in_one_room_do_not_overlap()
        {
            // Exact-length blocks start at period boundaries but end mid-period, so consecutive
            // placements can abut awkwardly — the overlap check has to work on real minutes rather
            // than on period indices. This is the case that would break if it did not.
            var a = Faculty(name: "A");
            var b = Faculty(name: "B");
            var sections = new[]
            {
                Meeting(a, "ITP-1A-IT101", "ITP-1A", requiredMinutes: 120),
                Meeting(b, "ITP-2A-IT201", "ITP-2A", requiredMinutes: 120)
            };
            var rooms = new[] { Room(RoomKind.LectureRoom) };
            var problem = Problem(sections, rooms, Grid(4), [a, b]);

            var result = _engine.Solve(problem);

            AssertHardConstraints(result, sections, rooms);
            Assert.False(result.Assignments[0].Slot.OverlapsWith(result.Assignments[1].Slot));
        }
    }
}
