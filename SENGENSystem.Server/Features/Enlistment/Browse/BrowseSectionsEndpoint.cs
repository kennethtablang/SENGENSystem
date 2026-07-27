using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Registration;

namespace SENGENSystem.Server.Features.Enlistment.Browse
{
    // Vertical slice: students browse the published schedule as enlistable sections with
    // real-time seat availability (FR-ENL-01/02). Only published assignments are shown —
    // drafts stay invisible until the Registrar publishes (FR-PUB-01).
    //
    // What a student sees is their own curriculum, not the institution's: the sections are filtered
    // to the subjects their program and year level still owe for this term (see EnlistmentPlan), so
    // a 2nd-year ITP student is never shown — and can never mis-click into — an HRA class or a
    // subject from a year they are not in. Staff browsing the same endpoint, and any student whose
    // program has no curriculum set up yet, see everything with a notice saying so.
    //
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

    /// <summary>One line of "what you still have to take this term", with where it stands.</summary>
    public record PlannedSubjectDto(
        string SubjectCode,
        string SubjectTitle,
        int Units,
        int YearLevel,
        bool IsBackSubject,
        int SectionCount,
        int SeatsAvailable,
        // Approved · Requested · Open (sections to pick from) · NoSection (nothing published yet).
        string Status);

    public static class BrowseSectionsEndpoint
    {
        public static IEndpointRouteBuilder MapBrowseSections(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/enlistment/sections", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Student), nameof(UserRole.SchoolAdmin)));
            return app;
        }

        // `all=true` lifts the curriculum filter for one request. It is an escape hatch, not a
        // loophole: the request leg re-checks the plan, so browsing wider never widens what a
        // student may actually enlist in.
        private static async Task<IResult> HandleAsync(
            System.Security.Claims.ClaimsPrincipal principal,
            bool? all,
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
            var plan = await EnlistmentPlanner.ResolveAsync(
                db, eligibility.Registration, semester, cancellationToken);

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
                    return new
                    {
                        section.SubjectId,
                        Dto = new BrowseSectionDto(
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
                            mine?.Id)
                    };
                })
                .OrderBy(s => s.Dto.SubjectCode).ThenBy(s => s.Dto.SectionCode)
                .ToList();

            var filtered = plan.IsResolved && all != true;
            var visible = (filtered
                    ? sections.Where(s => plan.SubjectIds.Contains(s.SubjectId))
                    : sections)
                .Select(s => s.Dto)
                .ToList();

            // The plan read as a checklist: every subject they owe, whether a section exists for it
            // yet, and where their request stands. A subject with no published section is the most
            // useful line on the page — it is the one thing the student cannot act on and would
            // otherwise never learn about.
            var planned = plan.Subjects.Select(subject =>
            {
                var forSubject = sections.Where(s => s.SubjectId == subject.SubjectId).Select(s => s.Dto).ToList();
                var mine = forSubject.FirstOrDefault(s => s.MyStatus is not null)?.MyStatus;
                return new PlannedSubjectDto(
                    subject.Code,
                    subject.Title,
                    subject.Units,
                    subject.YearLevel,
                    subject.IsBackSubject,
                    forSubject.Count,
                    forSubject.Sum(s => s.Available),
                    mine ?? (forSubject.Count == 0 ? "NoSection" : "Open"));
            }).ToList();

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
                plan = new
                {
                    resolved = plan.IsResolved,
                    filtered,
                    programCode = plan.ProgramCode,
                    programName = plan.Curriculum?.ProgramName,
                    yearLevel = plan.YearLevel,
                    yearLevelLabel = YearLevelPolicy.Label(plan.YearLevel),
                    termLabel = plan.TermLabel,
                    // Said plainly, because the student is the one who has to make sense of an
                    // empty page: either the plan is empty (nothing owed) or it could not be built.
                    notice = PlanNotice(plan, eligibility.Registration is not null),
                    subjectCount = planned.Count,
                    units = plan.Subjects.Sum(s => s.Units),
                    subjects = planned
                },
                count = visible.Count,
                totalCount = sections.Count,
                sections = visible
            });
        }

        private static string? PlanNotice(EnlistmentPlan plan, bool hasRegistration) =>
            !hasRegistration
                ? null
                : !plan.IsResolved
                    ? "We couldn't work out your subject list — no curriculum is set up for your program yet. "
                      + "Every published section is shown; ask the Registrar which ones are yours before requesting a seat."
                    : plan.Subjects.Count == 0
                        ? $"Your curriculum lists no {plan.TermLabel} subjects for {YearLevelPolicy.Label(plan.YearLevel)}. "
                          + "If that looks wrong, ask the Registrar to check your year level."
                        : null;

        private static string Format(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";
    }
}
