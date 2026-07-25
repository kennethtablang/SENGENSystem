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
    // Vertical slice: the Super Admin dispatches the ISO/IEC 25010 rating survey by emailed link and
    // reviews the collected results (FR-AUTH, evaluation). Super-admin only.
    public record SendInvitationsRequest(List<string>? Roles);

    public static class SurveyAdminEndpoints
    {
        public static IEndpointRouteBuilder MapSurveyAdmin(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/admin/survey")
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.SuperAdmin)));

            group.MapGet("/invitations", ListInvitationsAsync);
            group.MapPost("/invitations", SendInvitationsAsync);
            group.MapGet("/results", ResultsAsync);
            return app;
        }

        private static async Task<IResult> SendInvitationsAsync(
            SendInvitationsRequest request,
            AppDbContext db,
            AuditLog audit,
            IEmailSender email,
            IOptions<EmailOptions> emailOptions,
            CancellationToken ct)
        {
            var roleFilter = (request.Roles ?? [])
                .Select(r => Enum.TryParse<UserRole>(r, ignoreCase: true, out var parsed) ? (UserRole?)parsed : null)
                .Where(r => r is not null)
                .Select(r => r!.Value)
                .ToHashSet();

            var users = await db.Users.AsNoTracking()
                .Where(u => u.IsActive && (roleFilter.Count == 0 || roleFilter.Contains(u.Role)))
                .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
                .ToListAsync(ct);

            var existing = (await db.SurveyInvitations
                    .Where(i => users.Select(u => u.Id).Contains(i.UserId))
                    .ToListAsync(ct))
                .ToDictionary(i => i.UserId);

            var baseUrl = emailOptions.Value.ClientBaseUrl.TrimEnd('/');
            var outgoing = new List<(User User, string Token)>();
            int created = 0, resent = 0, skipped = 0;

            foreach (var user in users)
            {
                if (existing.TryGetValue(user.Id, out var invite))
                {
                    if (invite.CompletedAtUtc is not null) { skipped++; continue; } // already answered
                    // Pending — refresh the token and re-send the link.
                    var t = OneTimeToken.Generate();
                    invite.TokenHash = OneTimeToken.Hash(t);
                    invite.SentAtUtc = DateTime.UtcNow;
                    invite.RecipientName = user.FullName;
                    invite.RecipientEmail = user.Email;
                    invite.RecipientRole = user.Role.ToString();
                    outgoing.Add((user, t));
                    resent++;
                }
                else
                {
                    var t = OneTimeToken.Generate();
                    db.SurveyInvitations.Add(new SurveyInvitation
                    {
                        UserId = user.Id,
                        RecipientName = user.FullName,
                        RecipientEmail = user.Email,
                        RecipientRole = user.Role.ToString(),
                        TokenHash = OneTimeToken.Hash(t)
                    });
                    outgoing.Add((user, t));
                    created++;
                }
            }

            audit.Record(AuditAction.SurveyInvitationsSent,
                $"Sent the ISO 25010 rating survey to {created + resent} user(s) " +
                $"({created} new, {resent} resent, {skipped} already answered).");
            await db.SaveChangesAsync(ct);

            // Best-effort delivery: invitations are committed, so a mail hiccup doesn't lose them.
            foreach (var (user, token) in outgoing)
            {
                var (subject, body) = AccountEmails.SurveyInvitation(user, $"{baseUrl}/survey/{Uri.EscapeDataString(token)}");
                await email.SendAsync(user.Email, user.FullName, subject, body, ct);
            }

            return Results.Ok(new { created, resent, skipped, targeted = users.Count });
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
                    completedAtUtc = i.CompletedAtUtc is { } c ? Utc(c) : null
                })
            });
        }

        private static async Task<IResult> ResultsAsync(AppDbContext db, CancellationToken ct)
        {
            var responses = await db.SurveyResponses.AsNoTracking()
                .OrderByDescending(r => r.SubmittedAtUtc)
                .ToListAsync(ct);
            var invitedCount = await db.SurveyInvitations.CountAsync(ct);

            var keyToChar = SurveyContent.QuestionCharacteristic();
            var perQuestion = new Dictionary<string, List<int>>();
            var perCharacteristic = SurveyContent.Characteristics.ToDictionary(c => c.Code, _ => new List<int>());

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
                    r.Suggestions,
                    r.FurtherComments
                });
            }

            var characteristics = SurveyContent.Characteristics.Select(c => new
            {
                c.Code,
                c.NameEn,
                c.NameFil,
                average = perCharacteristic[c.Code].Count > 0 ? Math.Round(perCharacteristic[c.Code].Average(), 2) : 0.0,
                count = perCharacteristic[c.Code].Count,
                questions = c.Questions.Select(q => new
                {
                    q.Key,
                    q.En,
                    q.Fil,
                    average = perQuestion.TryGetValue(q.Key, out var s) && s.Count > 0 ? Math.Round(s.Average(), 2) : 0.0
                })
            }).ToList();

            var allScores = perQuestion.Values.SelectMany(v => v).ToList();

            return Results.Ok(new
            {
                responseCount = responses.Count,
                invitedCount,
                responseRate = invitedCount > 0 ? Math.Round(100.0 * responses.Count / invitedCount, 1) : 0.0,
                overallAverage = allScores.Count > 0 ? Math.Round(allScores.Average(), 2) : 0.0,
                characteristics,
                responses = respondentRows
            });
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
