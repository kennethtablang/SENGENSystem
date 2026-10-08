using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.AcademicRecords;
using SENGENSystem.Server.Features.EnrollmentCycle;
using SENGENSystem.Server.Features.Registration;

namespace SENGENSystem.Server.Features.Enlistment.RequestSlot
{
    // Vertical slice: an eligible student requests a seat in a published section (FR-ENL-04
    // request leg). Gates: pre-authorized + confirmed registration (FR-ENL-05), no duplicate
    // subject, seats still available (FR-ENL-03), and no time overlap with the student's other
    // requested/approved sections (FR-ENL-07).
    public record RequestSlotRequest(Guid SectionId);

    public static class RequestSlotEndpoint
    {
        public static IEndpointRouteBuilder MapRequestSlot(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/enlistment/requests", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Student)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            RequestSlotRequest request,
            System.Security.Claims.ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            Features.Reports.Live.ReportsBroadcaster broadcaster,
            CancellationToken cancellationToken)
        {
            var eligibility = await EnlistmentEligibility.ResolveAsync(principal, db, cancellationToken);
            if (!eligibility.IsEligible)
            {
                return Results.Json(new
                {
                    message = "You are not yet cleared to enlist.",
                    reasons = eligibility.Blockers
                }, statusCode: StatusCodes.Status403Forbidden);
            }
            var registration = eligibility.Registration!;

            // Institution-wide gate (FR-CYC-01, FR-ENL-08): the term must actually be in the
            // enlistment stage, and the Registrar's pause switch must be off. Previously only the
            // switch was checked, so a term sitting in "Enrollment closed" still accepted seat
            // requests — the ticker said one thing and the API did another.
            var window = await EnrollmentCyclePolicy.CheckEnlistmentAsync(db, cancellationToken);
            if (!window.Open)
            {
                return Results.Json(new { message = window.Reason },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var settings = await db.GetSettingsAsync(cancellationToken);

            var semester = await db.Semesters.AsNoTracking()
                .FirstOrDefaultAsync(s => s.IsActive, cancellationToken);
            if (semester is null)
            {
                return Results.BadRequest(new { message = "No active semester has been set up yet." });
            }

            var section = await db.Sections
                .Include(s => s.Subject)
                .FirstOrDefaultAsync(s => s.Id == request.SectionId && s.SemesterId == semester.Id, cancellationToken);
            if (section is null)
            {
                return Results.NotFound(new { message = "Section not found for the active semester." });
            }

            // FR-ENL-01/06: a seat may only be requested in a subject this student's own program and
            // year level still owe for this term. The browser already hides everything else, so
            // reaching here means a stale page or a hand-made request — either way the answer is the
            // same, and it names the subject rather than just refusing. Falls open when no plan
            // could be resolved (no curriculum for their program): a setup gap must not silently
            // block every student in that program from enlisting at all.
            var plan = await EnlistmentPlanner.ResolveAsync(db, registration, semester, cancellationToken);
            if (plan.IsResolved && !plan.SubjectIds.Contains(section.SubjectId))
            {
                return Results.Json(new
                {
                    message = $"{section.Subject?.Code} is not one of your subjects this term. " +
                              $"You are taking the {plan.ProgramCode} {YearLevelPolicy.Label(plan.YearLevel)} " +
                              $"{plan.TermLabel} subjects — pick from those, or see the Registrar if your " +
                              "year level looks wrong."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            // FR-ENL-06 / F-10: the prerequisite chain, which until now was modeled, editable, and
            // printed on the prospectus while being enforced nowhere. It is checked here rather than
            // only at approval so the student learns at the moment they click, not days later from a
            // Registrar — and it is checked at approval too, because a record can change in between.
            //
            // Falls open for a student with no academic history at all: see AcademicHistory
            // .IsEnforceable for why that is the honest reading of an empty record rather than a
            // loophole.
            var history = await AcademicHistory.LoadAsync(db, registration.Id, cancellationToken);
            if (history.IsEnforceable)
            {
                var unmet = await history.UnmetPrerequisitesAsync(db, section.SubjectId, cancellationToken);
                if (unmet.Count > 0)
                {
                    var code = section.Subject?.Code ?? "this subject";
                    audit.Record(AuditAction.PrerequisiteBlocked,
                        $"{registration.StudentNumber} was refused a seat in {code} — unmet prerequisite(s): " +
                        $"{string.Join(", ", unmet.Select(s => s.Code))}.",
                        "StudentRegistration", registration.Id.ToString());
                    await db.SaveChangesAsync(cancellationToken);

                    return Results.Json(new
                    {
                        message = AcademicHistory.Refusal(code, unmet, aboutSelf: true),
                        reasons = unmet.Select(s => $"{s.Code} — {s.Title}").ToList()
                    }, statusCode: StatusCodes.Status409Conflict);
                }
            }

            var sectionSlots = await PublishedSlotsAsync(db, [section.Id], cancellationToken);
            if (sectionSlots.Count == 0)
            {
                return Results.BadRequest(new { message = "This section's schedule has not been published yet." });
            }

            // Only this term's live requests. Every check below — duplicate subject, the per-student
            // unit ceiling, time overlap — is a statement about one semester's load, so carrying a
            // previous term's approvals in would tell a returning student they "already have" the
            // subject they are re-enrolling in and count last term's units against this term's cap.
            var active = await db.SlotRequests
                .Include(r => r.Section).ThenInclude(s => s!.Subject)
                .Where(r => r.StudentRegistrationId == registration.Id
                    && r.Section!.SemesterId == semester.Id
                    && (r.Status == SlotRequestStatus.Requested || r.Status == SlotRequestStatus.Approved))
                .ToListAsync(cancellationToken);

            if (active.Any(r => r.SectionId == section.Id))
            {
                return Results.Conflict(new { message = "You already have a request for this section." });
            }
            var sameSubject = active.FirstOrDefault(r => r.Section?.SubjectId == section.SubjectId);
            if (sameSubject is not null)
            {
                return Results.Conflict(new
                {
                    message = $"You already have a {sameSubject.Status} request for " +
                              $"{section.Subject?.Code} in section {sameSubject.Section?.SectionCode}."
                });
            }

            // Institutional per-student unit ceiling (FR-ENL, System Parameters): 0 means no limit.
            if (settings.MaxEnlistmentUnitsPerStudent > 0)
            {
                var currentUnits = active.Sum(r => r.Section?.Subject?.Units ?? 0);
                var thisUnits = section.Subject?.Units ?? 0;
                if (currentUnits + thisUnits > settings.MaxEnlistmentUnitsPerStudent)
                {
                    return Results.Conflict(new
                    {
                        message = $"This would put you at {currentUnits + thisUnits} units, over the " +
                                  $"{settings.MaxEnlistmentUnitsPerStudent}-unit enlistment ceiling. Drop a subject first."
                    });
                }
            }

            if (section.EnrolledCount >= section.Capacity)
            {
                return Results.Conflict(new { message = "This section is already full. Please pick another section." });
            }

            // FR-ENL-07: no overlapping time slots across the student's chosen sections.
            var mySlots = await PublishedSlotsAsync(db, active.Select(r => r.SectionId).ToList(), cancellationToken);
            foreach (var candidate in sectionSlots)
            {
                var clash = mySlots.FirstOrDefault(s => s.Slot.OverlapsWith(candidate.Slot));
                if (clash != default)
                {
                    return Results.Conflict(new
                    {
                        message = $"This section overlaps your {clash.SubjectCode} class on " +
                                  $"{candidate.Slot.Day} {Format(candidate.Slot.StartMinutes)}–{Format(candidate.Slot.EndMinutes)}."
                    });
                }
            }

            var slotRequest = new SlotRequest
            {
                StudentRegistrationId = registration.Id,
                SectionId = section.Id
            };
            db.SlotRequests.Add(slotRequest);
            audit.Record(AuditAction.SlotRequested,
                $"{registration.StudentNumber} requested a seat in {section.Subject?.Code} ({section.SectionCode}).",
                "SlotRequest", slotRequest.Id.ToString());

            // Put the new request on every active Registrar's bell, in the same transaction.
            var registrarIds = await db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.Role == UserRole.Registrar)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);
            notifier.NotifyMany(registrarIds, NotificationKind.SlotRequested,
                "New slot request",
                $"{registration.FullName} ({registration.StudentNumber}) requested a seat in " +
                $"{section.Subject?.Code} ({section.SectionCode}).",
                "/approvals");

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The unique (student, section) live-request index caught a race.
                return Results.Conflict(new { message = "You already have a request for this section." });
            }

            broadcaster.Announce("enlistment");
            return Results.Created($"/api/enlistment/requests/{slotRequest.Id}", new
            {
                requestId = slotRequest.Id,
                sectionId = section.Id,
                status = slotRequest.Status.ToString(),
                message = $"Seat requested for {section.Subject?.Code} ({section.SectionCode}) — awaiting Registrar approval."
            });
        }

        private static async Task<List<(TimeSlot Slot, string SubjectCode)>> PublishedSlotsAsync(
            AppDbContext db, List<Guid> sectionIds, CancellationToken cancellationToken)
        {
            if (sectionIds.Count == 0) return [];
            var rows = await db.ScheduleAssignments.AsNoTracking()
                .Where(a => sectionIds.Contains(a.SectionId) && a.IsPublished)
                .Include(a => a.TimeSlot)
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .ToListAsync(cancellationToken);
            return rows
                .Where(a => a.TimeSlot is not null)
                .Select(a => (a.TimeSlot!, a.Section?.Subject?.Code ?? string.Empty))
                .ToList();
        }

        private static string Format(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";
    }
}
