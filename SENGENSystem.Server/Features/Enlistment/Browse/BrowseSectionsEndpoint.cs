using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Enlistment.Browse
{
    // Vertical slice: students browse the published schedule as enlistable sections with
    // real-time seat availability (FR-ENL-01/02). Only published assignments are shown —
    // drafts stay invisible until the Registrar publishes (FR-PUB-01).
    // StartMinutes/EndMinutes travel alongside the formatted string so the browser can render the
    // time in the reader's own 12/24-hour Settings preference — a server-baked string cannot.
    public record SectionMeetingDto(
        string Day, string Time, int StartMinutes, int EndMinutes, string Room, string Faculty, bool IsAmended);

    public record BrowseSectionDto(
        Guid SectionId,
        string SubjectCode,
        string SubjectTitle,
        int Units,
        string SectionCode,
        string CohortKey,
        int Capacity,
        int Enrolled,
        int Available,
        IReadOnlyList<SectionMeetingDto> Meetings,
        string? MyStatus,
        Guid? MyRequestId);

    public static class BrowseSectionsEndpoint
    {
        public static IEndpointRouteBuilder MapBrowseSections(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/enlistment/sections", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Student), nameof(UserRole.SchoolAdmin)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            System.Security.Claims.ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var semester = await db.Semesters.AsNoTracking()
                .FirstOrDefaultAsync(s => s.IsActive, cancellationToken);
            if (semester is null)
            {
                return Results.BadRequest(new { message = "No active semester has been set up yet." });
            }

            var eligibility = await EnlistmentEligibility.ResolveAsync(principal, db, cancellationToken);

            var assignments = await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id && a.IsPublished)
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .Include(a => a.Room)
                .Include(a => a.TimeSlot)
                .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                .ToListAsync(cancellationToken);

            var myRequests = eligibility.Registration is { } reg
                ? await db.SlotRequests.AsNoTracking()
                    .Where(r => r.StudentRegistrationId == reg.Id
                        && (r.Status == SlotRequestStatus.Requested || r.Status == SlotRequestStatus.Approved))
                    .ToListAsync(cancellationToken)
                : [];

            var sections = assignments
                .Where(a => a.Section is not null && a.TimeSlot is not null)
                .GroupBy(a => a.SectionId)
                .Select(group =>
                {
                    var section = group.First().Section!;
                    var mine = myRequests.FirstOrDefault(r => r.SectionId == section.Id);
                    return new BrowseSectionDto(
                        section.Id,
                        section.Subject?.Code ?? string.Empty,
                        section.Subject?.Title ?? string.Empty,
                        section.Subject?.Units ?? 0,
                        section.SectionCode,
                        section.CohortKey,
                        section.Capacity,
                        section.EnrolledCount,
                        Math.Max(0, section.Capacity - section.EnrolledCount),
                        group
                            .OrderBy(a => a.TimeSlot!.Day).ThenBy(a => a.TimeSlot!.StartMinutes)
                            .Select(a => new SectionMeetingDto(
                                a.TimeSlot!.Day.ToString(),
                                $"{Format(a.TimeSlot.StartMinutes)}–{Format(a.TimeSlot.EndMinutes)}",
                                a.TimeSlot.StartMinutes,
                                a.TimeSlot.EndMinutes,
                                a.Room?.Name ?? string.Empty,
                                a.FacultyProfile?.User?.FullName ?? string.Empty,
                                a.IsAmended))
                            .ToList(),
                        mine?.Status.ToString(),
                        mine?.Id);
                })
                .OrderBy(s => s.SubjectCode).ThenBy(s => s.SectionCode)
                .ToList();

            return Results.Ok(new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                eligibility = new
                {
                    eligible = eligibility.IsEligible,
                    studentNumber = eligibility.Registration?.StudentNumber,
                    blockers = eligibility.Blockers
                },
                count = sections.Count,
                sections
            });
        }

        private static string Format(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";
    }
}
