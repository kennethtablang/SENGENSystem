namespace SENGENSystem.Server.Features.Scheduling.Engine
{
    /// <summary>
    /// Outcome of a generation run. A successful result is guaranteed to have zero
    /// hard-constraint violations (NFR-1); an unsuccessful one explains why so the
    /// Academic Head can correct inputs before retrying.
    /// </summary>
    public sealed class ScheduleGenerationResult
    {
        public bool Success { get; init; }

        public IReadOnlyList<SectionAssignment> Assignments { get; init; } = [];

        /// <summary>Sections that could not be placed, with the reason (empty domain or backtracking dead-end).</summary>
        public IReadOnlyList<string> UnplacedReasons { get; init; } = [];

        /// <summary>How many backtracking steps the search took — surfaced for scheduling transparency (FR-DASH-03).</summary>
        public int Steps { get; init; }

        public static ScheduleGenerationResult Ok(IReadOnlyList<SectionAssignment> assignments, int steps) =>
            new() { Success = true, Assignments = assignments, Steps = steps };

        public static ScheduleGenerationResult Fail(IReadOnlyList<string> reasons, int steps) =>
            new() { Success = false, UnplacedReasons = reasons, Steps = steps };
    }
}
