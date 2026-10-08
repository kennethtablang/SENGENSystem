using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Scheduling.Engine;
using static SENGENSystem.Server.Tests.Scheduling.ScheduleProblemBuilder;

namespace SENGENSystem.Server.Tests.Scheduling
{
    /// <summary>
    /// FR-SCHED-08 reproducibility. The engine is deterministic <i>given a seed</i>, not absolutely:
    /// a fresh seed per run gives the Academic Head variety, and recording that seed is what lets any
    /// past arrangement be reproduced.
    ///
    /// <para>
    /// That property is load-bearing and easy to break by accident — one unordered dictionary
    /// iteration or one <c>Guid.NewGuid()</c> reaching a comparison key is enough to make the same
    /// seed produce different timetables, at which point the recorded seed in the audit trail is a
    /// lie. Hence these tests.
    /// </para>
    /// </summary>
    public class CspSchedulerReproducibilityTests
    {
        private readonly CspScheduler _engine = new();

        /// <summary>
        /// A problem with enough slack that many equally-good timetables exist — which is exactly
        /// where the tie-breaking shuffle decides the answer, and therefore where non-determinism
        /// would show up.
        /// </summary>
        private static (List<SectionVar> Sections, List<RoomOption> Rooms, List<TimeSlot> Slots, List<FacultyOption> Faculty)
            SlackProblem()
        {
            var faculty = Enumerable.Range(0, 3).Select(i => Faculty(name: $"Member {i}")).ToList();
            var sections = new List<SectionVar>();
            for (var i = 0; i < 3; i++)
            {
                sections.Add(Meeting(faculty[i], $"ITP-{i}A-IT10{i}", $"ITP-{i}A"));
                sections.Add(Meeting(faculty[i], $"ITP-{i}B-IT10{i}", $"ITP-{i}B"));
            }
            var rooms = new List<RoomOption>
            {
                Room(RoomKind.LectureRoom), Room(RoomKind.LectureRoom), Room(RoomKind.LectureRoom)
            };
            var slots = Grid(4, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday);
            return (sections, rooms, slots, faculty);
        }

        /// <summary>A timetable reduced to a comparable, order-independent fingerprint.</summary>
        private static IEnumerable<string> Fingerprint(ScheduleGenerationResult result) =>
            result.Assignments
                .Select(a => $"{a.SectionId}|{a.Component}|{a.RoomId}|{a.Slot.Day}|{a.Slot.StartMinutes}|{a.Slot.EndMinutes}")
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

        [Fact]
        public void The_same_seed_produces_the_same_timetable()
        {
            var (sections, rooms, slots, faculty) = SlackProblem();

            var first = _engine.Solve(Problem(sections, rooms, slots, faculty, seed: 12345));
            var second = _engine.Solve(Problem(sections, rooms, slots, faculty, seed: 12345));

            Assert.True(first.Success);
            Assert.True(second.Success);
            Assert.Equal(Fingerprint(first), Fingerprint(second));
            // The search itself must be identical too, not merely the outcome.
            Assert.Equal(first.Steps, second.Steps);
        }

        [Fact]
        public void The_same_seed_is_stable_across_many_runs()
        {
            // Guards the failure mode a single repeat can miss: an unordered collection whose
            // iteration order happens to be stable twice in a row.
            var (sections, rooms, slots, faculty) = SlackProblem();
            var baseline = Fingerprint(_engine.Solve(Problem(sections, rooms, slots, faculty, seed: 777)));

            for (var run = 0; run < 5; run++)
            {
                var again = _engine.Solve(Problem(sections, rooms, slots, faculty, seed: 777));
                Assert.Equal(baseline, Fingerprint(again));
            }
        }

        [Fact]
        public void A_different_seed_still_produces_a_valid_timetable()
        {
            // Variety must never cost correctness — the point of a seeded shuffle is a different
            // answer, not a worse one.
            var (sections, rooms, slots, faculty) = SlackProblem();

            foreach (var seed in new[] { 1, 2, 3, 99, 100_000 })
            {
                var result = _engine.Solve(Problem(sections, rooms, slots, faculty, seed));
                AssertHardConstraints(result, sections, rooms);
            }
        }

        [Fact]
        public void Seeds_do_explore_different_arrangements()
        {
            // The other half of the contract. If every seed gave the same answer, recording the
            // seed would be pointless and "reproduce arrangement #N" would be meaningless — so at
            // least one seed among a spread must differ from the first.
            var (sections, rooms, slots, faculty) = SlackProblem();
            var baseline = Fingerprint(_engine.Solve(Problem(sections, rooms, slots, faculty, seed: 1)));

            var anyDifferent = Enumerable.Range(2, 20)
                .Select(seed => Fingerprint(_engine.Solve(Problem(sections, rooms, slots, faculty, seed))))
                .Any(f => !f.SequenceEqual(baseline));

            Assert.True(anyDifferent,
                "Every seed produced an identical timetable — the seed is not influencing the search, "
                + "which makes FR-SCHED-08's 'reproduce a past arrangement' meaningless.");
        }
    }
}
