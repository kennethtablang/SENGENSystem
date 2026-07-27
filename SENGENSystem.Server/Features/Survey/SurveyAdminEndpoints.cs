using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Auth;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Auth;

namespace SENGENSystem.Server.Features.Survey
{
    // Vertical slice: the Super Admin picks exactly who receives the ISO/IEC 25010 rating survey,
    // pushes it to them (in-app bell notice and/or emailed link), and reviews the usability report
    // the responses build up. Collection keeps running until the Super Admin closes the window.
    // Super-admin only.

    /// <param name="UserIds">Explicitly picked recipients — the primary way the audience is chosen.</param>
    /// <param name="Roles">Optional whole-role shortcut, unioned with <paramref name="UserIds"/>.</param>
    /// <param name="Note">Optional personal message shown on the bell notice and in the email.</param>
    public record SendInvitationsRequest(
        List<Guid>? UserIds,
        List<string>? Roles,
        string? Note,
        bool? PushNotification,
        bool? SendEmail);

    /// <summary>Nudges people who were invited but haven't answered yet.</summary>
    public record RemindRequest(
        List<Guid>? InvitationIds,
        string? Note,
        bool? PushNotification,
        bool? SendEmail);

    /// <summary>Opens or closes the collection window and sets the response goal.</summary>
    public record CollectionRequest(bool? IsOpen, int? TargetResponses);

    public static class SurveyAdminEndpoints
    {
        public static IEndpointRouteBuilder MapSurveyAdmin(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/admin/survey")
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.SuperAdmin)));

            group.MapGet("/audience", AudienceAsync);
            group.MapGet("/invitations", ListInvitationsAsync);
            group.MapPost("/invitations", SendInvitationsAsync);
            group.MapPost("/invitations/remind", RemindAsync);
            group.MapDelete("/invitations/{id:guid}", WithdrawAsync);
            group.MapGet("/collection", GetCollectionAsync);
            group.MapPost("/collection", SetCollectionAsync);
            group.MapGet("/results", ResultsAsync);
            group.MapGet("/results/export", ExportAsync);
            return app;
        }

        // ---- Audience: every account the Super Admin can choose from, with its invite status ----

        private static async Task<IResult> AudienceAsync(AppDbContext db, CancellationToken ct)
        {
            var users = await db.Users.AsNoTracking()
                .Where(u => u.IsActive)
                .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.Role })
                .ToListAsync(ct);

            var invitations = await db.SurveyInvitations.AsNoTracking()
                .Select(i => new { i.Id, i.UserId, i.SentAtUtc, i.CompletedAtUtc, i.NotifiedAtUtc, i.ReminderCount })
                .ToListAsync(ct);
            var byUser = invitations.ToDictionary(i => i.UserId);

            return Results.Ok(new
            {
                users = users.Select(u =>
                {
                    byUser.TryGetValue(u.Id, out var inv);
                    return new
                    {
                        u.Id,
                        name = $"{u.LastName}, {u.FirstName}",
                        email = u.Email,
                        role = u.Role.ToString(),
                        invitationId = inv?.Id,
                        status = inv is null ? "not-invited"
                            : inv.CompletedAtUtc is not null ? "answered"
                            : "pending",
                        sentAtUtc = inv is null ? null : Utc(inv.SentAtUtc),
                        completedAtUtc = inv?.CompletedAtUtc is { } c ? Utc(c) : null,
                        notifiedAtUtc = inv?.NotifiedAtUtc is { } n ? Utc(n) : null,
                        reminderCount = inv?.ReminderCount ?? 0
                    };
                })
            });
        }

        // ---- Dispatch ----

        private static async Task<IResult> SendInvitationsAsync(
            SendInvitationsRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            IEmailSender email,
            IOptions<EmailOptions> emailOptions,
            CancellationToken ct)
        {
            var campaign = await GetOrCreateCampaignAsync(db, ct);
            if (!campaign.IsOpen)
            {
                return Results.BadRequest(new
                {
                    message = "Collection is closed. Reopen it before inviting more respondents."
                });
            }

            var pickedIds = (request.UserIds ?? []).ToHashSet();
            var roleFilter = ParseRoles(request.Roles);
            if (pickedIds.Count == 0 && roleFilter.Count == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["userIds"] = ["Select at least one person to receive the survey."]
                });
            }

            // The picker sends explicit users; roles stay available as a bulk shortcut. Union of both.
            var users = await db.Users
                .Where(u => u.IsActive && (pickedIds.Contains(u.Id) || roleFilter.Contains(u.Role)))
                .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
                .ToListAsync(ct);

            if (users.Count == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["userIds"] = ["None of the selected accounts are active."]
                });
            }

            var userIds = users.Select(u => u.Id).ToList();
            var existing = await db.SurveyInvitations
                .Where(i => userIds.Contains(i.UserId))
                .ToListAsync(ct);
            var byUser = existing.ToDictionary(i => i.UserId);

            var pushNotification = request.PushNotification ?? true;
            var sendEmail = request.SendEmail ?? true;
            var note = Trimmed(request.Note, 500);
            var actor = ActorName(principal);
            var baseUrl = emailOptions.Value.ClientBaseUrl.TrimEnd('/');

            var outgoing = new List<(User User, string Token)>();
            int created = 0, resent = 0, skipped = 0;

            foreach (var user in users)
            {
                if (byUser.TryGetValue(user.Id, out var invite))
                {
                    if (invite.CompletedAtUtc is not null) { skipped++; continue; } // already answered
                    // Pending — refresh the token so the re-sent link is the only live one.
                    var refreshed = OneTimeToken.Generate();
                    invite.TokenHash = OneTimeToken.Hash(refreshed);
                    invite.SentAtUtc = DateTime.UtcNow;
                    invite.RecipientName = user.FullName;
                    invite.RecipientEmail = user.Email;
                    invite.RecipientRole = user.Role.ToString();
                    invite.Note = note ?? invite.Note;
                    invite.InvitedBy = actor;
                    if (pushNotification) invite.NotifiedAtUtc = DateTime.UtcNow;
                    outgoing.Add((user, refreshed));
                    resent++;
                }
                else
                {
                    var token = OneTimeToken.Generate();
                    db.SurveyInvitations.Add(new SurveyInvitation
                    {
                        UserId = user.Id,
                        RecipientName = user.FullName,
                        RecipientEmail = user.Email,
                        RecipientRole = user.Role.ToString(),
                        TokenHash = OneTimeToken.Hash(token),
                        Note = note,
                        InvitedBy = actor,
                        NotifiedAtUtc = pushNotification ? DateTime.UtcNow : null
                    });
                    outgoing.Add((user, token));
                    created++;
                }

                // The in-app push the Super Admin controls: the recipient sees it on their bell and
                // opens /survey signed in, no emailed token needed.
                if (pushNotification)
                {
                    notifier.Notify(
                        user.Id,
                        NotificationKind.Survey,
                        "Please answer the SEN-GEN evaluation survey",
                        string.IsNullOrWhiteSpace(note)
                            ? "You've been invited to rate SEN-GEN using the ISO/IEC 25010 evaluation. " +
                              "Inaanyayahan kang suriin ang SEN-GEN — bukas ang sagutan sa English at Filipino."
                            : note,
                        "/survey");
                }
            }

            audit.Record(AuditAction.SurveyInvitationsSent,
                $"Sent the ISO 25010 rating survey to {created + resent} selected user(s) " +
                $"({created} new, {resent} resent, {skipped} already answered).");
            await db.SaveChangesAsync(ct);

            // Best-effort delivery: invitations are committed, so a mail hiccup doesn't lose them.
            if (sendEmail)
            {
                foreach (var (user, token) in outgoing)
                {
                    var (subject, body) = AccountEmails.SurveyInvitation(user, $"{baseUrl}/survey/{Uri.EscapeDataString(token)}");
                    await email.SendAsync(user.Email, user.FullName, subject, body, ct);
                }
            }

            return Results.Ok(new { created, resent, skipped, targeted = users.Count, pushed = pushNotification, emailed = sendEmail });
        }

        // ---- Reminders: nudge the people who haven't answered ----

        private static async Task<IResult> RemindAsync(
            RemindRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            IEmailSender email,
            IOptions<EmailOptions> emailOptions,
            CancellationToken ct)
        {
            var campaign = await GetOrCreateCampaignAsync(db, ct);
            if (!campaign.IsOpen)
            {
                return Results.BadRequest(new { message = "Collection is closed, so reminders would lead nowhere." });
            }

            var ids = (request.InvitationIds ?? []).ToHashSet();
            var pending = await db.SurveyInvitations
                .Where(i => i.CompletedAtUtc == null && (ids.Count == 0 || ids.Contains(i.Id)))
                .ToListAsync(ct);

            if (pending.Count == 0)
            {
                return Results.Ok(new { reminded = 0 });
            }

            var pushNotification = request.PushNotification ?? true;
            var sendEmail = request.SendEmail ?? false;
            var note = Trimmed(request.Note, 500);
            var baseUrl = emailOptions.Value.ClientBaseUrl.TrimEnd('/');
            var outgoing = new List<(SurveyInvitation Invite, string Token)>();

            foreach (var invite in pending)
            {
                invite.ReminderCount++;
                if (pushNotification)
                {
                    invite.NotifiedAtUtc = DateTime.UtcNow;
                    notifier.Notify(
                        invite.UserId,
                        NotificationKind.Survey,
                        "Reminder: the SEN-GEN evaluation survey is waiting",
                        string.IsNullOrWhiteSpace(note)
                            ? "Your evaluation hasn't been submitted yet. It takes only a few minutes. " +
                              "Hindi pa naipapasa ang iyong pagsusuri — ilang minuto lang ito."
                            : note,
                        "/survey");
                }
                if (sendEmail)
                {
                    // A fresh token per reminder keeps exactly one link live per person.
                    var token = OneTimeToken.Generate();
                    invite.TokenHash = OneTimeToken.Hash(token);
                    invite.SentAtUtc = DateTime.UtcNow;
                    outgoing.Add((invite, token));
                }
            }

            audit.Record(AuditAction.SurveyRemindersSent,
                $"Sent survey reminders to {pending.Count} pending respondent(s).");
            await db.SaveChangesAsync(ct);

            foreach (var (invite, token) in outgoing)
            {
                var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == invite.UserId, ct);
                if (user is null) continue;
                var (subject, body) = AccountEmails.SurveyInvitation(user, $"{baseUrl}/survey/{Uri.EscapeDataString(token)}");
                await email.SendAsync(user.Email, user.FullName, subject, body, ct);
            }

            return Results.Ok(new { reminded = pending.Count });
        }

        // ---- Withdraw: the Super Admin also controls who stops having access ----

        private static async Task<IResult> WithdrawAsync(
            Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var invite = await db.SurveyInvitations
                .Include(i => i.Response)
                .FirstOrDefaultAsync(i => i.Id == id, ct);
            if (invite is null)
            {
                return Results.NotFound(new { message = "Invitation not found." });
            }
            if (invite.CompletedAtUtc is not null)
            {
                return Results.BadRequest(new
                {
                    message = "This person already answered — their response is part of the results and is kept."
                });
            }

            db.SurveyInvitations.Remove(invite);
            audit.Record(AuditAction.SurveyInvitationWithdrawn,
                $"Withdrew the survey invitation for {invite.RecipientName}.",
                "SurveyInvitation", invite.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { withdrawn = true });
        }

        // ---- Collection window ----

        private static async Task<IResult> GetCollectionAsync(AppDbContext db, CancellationToken ct)
        {
            var campaign = await GetOrCreateCampaignAsync(db, ct);
            await db.SaveChangesAsync(ct); // persists the row on very first read
            var responses = await db.SurveyResponses.CountAsync(ct);
            return Results.Ok(CollectionDto(campaign, responses));
        }

        private static async Task<IResult> SetCollectionAsync(
            CollectionRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            CancellationToken ct)
        {
            var campaign = await GetOrCreateCampaignAsync(db, ct);

            if (request.TargetResponses is { } target)
            {
                campaign.TargetResponses = Math.Clamp(target, 1, 100000);
            }

            if (request.IsOpen is { } isOpen && isOpen != campaign.IsOpen)
            {
                campaign.IsOpen = isOpen;
                campaign.LastChangedBy = ActorName(principal);
                if (isOpen)
                {
                    campaign.OpenedAtUtc = DateTime.UtcNow;
                    campaign.ClosedAtUtc = null;
                }
                else
                {
                    campaign.ClosedAtUtc = DateTime.UtcNow;
                }

                var count = await db.SurveyResponses.CountAsync(ct);
                audit.Record(AuditAction.SurveyCollectionChanged,
                    isOpen
                        ? "Reopened survey collection."
                        : $"Closed survey collection with {count} response(s) gathered.");
            }

            await db.SaveChangesAsync(ct);
            var responses = await db.SurveyResponses.CountAsync(ct);
            return Results.Ok(CollectionDto(campaign, responses));
        }

        // ---- Results: the usability report ----

        private static async Task<IResult> ResultsAsync(AppDbContext db, CancellationToken ct)
        {
            var responses = await db.SurveyResponses.AsNoTracking()
                .OrderByDescending(r => r.SubmittedAtUtc)
                .ToListAsync(ct);
            var invitedCount = await db.SurveyInvitations.CountAsync(ct);
            var campaign = await GetOrCreateCampaignAsync(db, ct);

            var keyToChar = SurveyContent.QuestionCharacteristic();
            var perQuestion = new Dictionary<string, List<int>>();
            var perCharacteristic = SurveyContent.Characteristics.ToDictionary(c => c.Code, _ => new List<int>());
            var perRole = new Dictionary<string, List<int>>();
            var distribution = new Dictionary<int, int> { [1] = 0, [2] = 0, [3] = 0, [4] = 0, [5] = 0 };

            var respondentRows = new List<object>();
            foreach (var r in responses)
            {
                var answers = Deserialize(r.AnswersJson);
                foreach (var (key, score) in answers)
                {
                    (perQuestion.TryGetValue(key, out var q) ? q : perQuestion[key] = []).Add(score);
                    if (keyToChar.TryGetValue(key, out var code) && perCharacteristic.TryGetValue(code, out var list))
                    {
                        list.Add(score);
                    }
                    if (distribution.ContainsKey(score)) distribution[score]++;

                    var role = string.IsNullOrWhiteSpace(r.RespondentRole) ? "Unknown" : r.RespondentRole;
                    (perRole.TryGetValue(role, out var rl) ? rl : perRole[role] = []).Add(score);
                }

                var overall = answers.Count > 0 ? Math.Round(answers.Values.Average(), 2) : 0;
                respondentRows.Add(new
                {
                    r.RespondentName,
                    r.RespondentRole,
                    r.RespondentEmail,
                    r.Position,
                    r.Age,
                    r.Sex,
                    r.Department,
                    r.YearsUsing,
                    submittedAtUtc = Utc(r.SubmittedAtUtc),
                    average = overall,
                    interpretation = Interpret(overall),
                    r.Suggestions,
                    r.FurtherComments
                });
            }

            var characteristics = SurveyContent.Characteristics.Select(c =>
            {
                var scores = perCharacteristic[c.Code];
                var avg = scores.Count > 0 ? Math.Round(scores.Average(), 2) : 0.0;
                return new
                {
                    c.Code,
                    c.NameEn,
                    c.NameFil,
                    average = avg,
                    interpretation = Interpret(avg),
                    count = scores.Count,
                    questions = c.Questions.Select(q =>
                    {
                        var qs = perQuestion.TryGetValue(q.Key, out var s) ? s : [];
                        var qavg = qs.Count > 0 ? Math.Round(qs.Average(), 2) : 0.0;
                        return new
                        {
                            q.Key,
                            q.En,
                            q.Fil,
                            average = qavg,
                            interpretation = Interpret(qavg),
                            count = qs.Count
                        };
                    })
                };
            }).ToList();

            var allScores = perQuestion.Values.SelectMany(v => v).ToList();
            var overallAverage = allScores.Count > 0 ? Math.Round(allScores.Average(), 2) : 0.0;

            // Usability is the ISO 25010 characteristic the report headlines, so surface it directly.
            var usabilityScores = perCharacteristic.TryGetValue("usability", out var us) ? us : [];

            return Results.Ok(new
            {
                responseCount = responses.Count,
                invitedCount,
                responseRate = invitedCount > 0 ? Math.Round(100.0 * responses.Count / invitedCount, 1) : 0.0,
                overallAverage,
                overallInterpretation = Interpret(overallAverage),
                usabilityAverage = usabilityScores.Count > 0 ? Math.Round(usabilityScores.Average(), 2) : 0.0,
                collection = CollectionDto(campaign, responses.Count),
                distribution = distribution.OrderBy(d => d.Key).Select(d => new
                {
                    score = d.Key,
                    count = d.Value,
                    percent = allScores.Count > 0 ? Math.Round(100.0 * d.Value / allScores.Count, 1) : 0.0
                }),
                byRole = perRole.OrderByDescending(p => p.Value.Count).Select(p => new
                {
                    role = p.Key,
                    average = Math.Round(p.Value.Average(), 2),
                    interpretation = Interpret(Math.Round(p.Value.Average(), 2)),
                    respondents = responses.Count(r => (string.IsNullOrWhiteSpace(r.RespondentRole) ? "Unknown" : r.RespondentRole) == p.Key)
                }),
                characteristics,
                responses = respondentRows
            });
        }

        // ---- CSV export of the raw responses, for offline analysis in the study write-up ----

        private static async Task<IResult> ExportAsync(AppDbContext db, CancellationToken ct)
        {
            var responses = await db.SurveyResponses.AsNoTracking()
                .OrderBy(r => r.SubmittedAtUtc)
                .ToListAsync(ct);

            var keys = SurveyContent.AllQuestionKeys();
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(',', new[]
                {
                    "Name", "Email", "Role", "Position", "Department", "Age", "Sex", "YearsUsing", "SubmittedUtc"
                }
                .Concat(keys)
                .Concat(["Average", "Suggestions", "FurtherComments"])
                .Select(Csv)));

            foreach (var r in responses)
            {
                var answers = Deserialize(r.AnswersJson);
                var average = answers.Count > 0 ? Math.Round(answers.Values.Average(), 2) : 0;
                var cells = new List<string>
                {
                    r.RespondentName, r.RespondentEmail, r.RespondentRole, r.Position, r.Department,
                    r.Age?.ToString(CultureInfo.InvariantCulture) ?? "", r.Sex, r.YearsUsing, Utc(r.SubmittedAtUtc)
                };
                cells.AddRange(keys.Select(k => answers.TryGetValue(k, out var v) ? v.ToString(CultureInfo.InvariantCulture) : ""));
                cells.Add(average.ToString(CultureInfo.InvariantCulture));
                cells.Add(r.Suggestions ?? "");
                cells.Add(r.FurtherComments ?? "");
                sb.AppendLine(string.Join(',', cells.Select(Csv)));
            }

            // BOM so Excel opens the Filipino text in the comments correctly.
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return Results.File(bytes, "text/csv", $"sengen-survey-results-{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        private static async Task<IResult> ListInvitationsAsync(AppDbContext db, CancellationToken ct)
        {
            var items = await db.SurveyInvitations.AsNoTracking()
                .OrderByDescending(i => i.SentAtUtc)
                .Take(1000)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                total = items.Count,
                completed = items.Count(i => i.CompletedAtUtc is not null),
                invitations = items.Select(i => new
                {
                    i.Id,
                    name = i.RecipientName,
                    email = i.RecipientEmail,
                    role = i.RecipientRole,
                    sentAtUtc = Utc(i.SentAtUtc),
                    completedAtUtc = i.CompletedAtUtc is { } c ? Utc(c) : null,
                    notifiedAtUtc = i.NotifiedAtUtc is { } n ? Utc(n) : null,
                    i.ReminderCount,
                    i.Note,
                    i.InvitedBy
                })
            });
        }

        // ---- Shared helpers ----

        /// <summary>
        /// The one collection-window row, created on first use so a fresh database needs no seeding
        /// step. Callers persist it with their own SaveChangesAsync.
        /// </summary>
        internal static async Task<SurveyCampaign> GetOrCreateCampaignAsync(AppDbContext db, CancellationToken ct)
        {
            var campaign = await db.SurveyCampaigns.FirstOrDefaultAsync(c => c.Id == SurveyCampaign.SingletonId, ct);
            if (campaign is null)
            {
                campaign = new SurveyCampaign { Id = SurveyCampaign.SingletonId };
                db.SurveyCampaigns.Add(campaign);
            }
            return campaign;
        }

        private static object CollectionDto(SurveyCampaign c, int responseCount) => new
        {
            isOpen = c.IsOpen,
            targetResponses = c.TargetResponses,
            responseCount,
            progress = c.TargetResponses > 0
                ? Math.Min(100, Math.Round(100.0 * responseCount / c.TargetResponses, 1))
                : 0.0,
            targetMet = responseCount >= c.TargetResponses,
            openedAtUtc = Utc(c.OpenedAtUtc),
            closedAtUtc = c.ClosedAtUtc is { } closed ? Utc(closed) : null,
            lastChangedBy = c.LastChangedBy
        };

        /// <summary>Verbal interpretation of a Likert mean, the convention these studies report with.</summary>
        internal static string Interpret(double average) => average switch
        {
            0 => "No data",
            < 1.50 => "Strongly Disagree",
            < 2.50 => "Disagree",
            < 3.50 => "Neutral",
            < 4.50 => "Agree",
            _ => "Strongly Agree"
        };

        private static HashSet<UserRole> ParseRoles(List<string>? roles) =>
            (roles ?? [])
                .Select(r => Enum.TryParse<UserRole>(r, ignoreCase: true, out var parsed) ? (UserRole?)parsed : null)
                .Where(r => r is not null)
                .Select(r => r!.Value)
                .ToHashSet();

        private static string ActorName(ClaimsPrincipal principal) =>
            principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name) ?? "Super Admin";

        private static string? Trimmed(string? value, int max) =>
            string.IsNullOrWhiteSpace(value) ? null
            : value.Trim().Length > max ? value.Trim()[..max]
            : value.Trim();

        private static string Csv(string value)
        {
            var v = value ?? "";
            return v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r')
                ? $"\"{v.Replace("\"", "\"\"")}\""
                : v;
        }

        private static Dictionary<string, int> Deserialize(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private static string Utc(DateTime value) =>
            DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("o");
    }
}
