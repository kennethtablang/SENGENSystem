using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Auth;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Survey
{
    // Vertical slice: the public, token-gated ISO/IEC 25010 rating survey a user opens from the
    // emailed link. No sign-in — the opaque token in the URL identifies the invitation. The
    // instrument itself is the fixed bilingual bank in SurveyContent; a link answers exactly once.
    public record SubmitSurveyRequest(
        string? RespondentName,
        string? Position,
        int? Age,
        string? Sex,
        string? Department,
        string? YearsUsing,
        Dictionary<string, int>? Answers,
        string? Suggestions,
        string? FurtherComments);

    public static class SurveyEndpoints
    {
        public static IEndpointRouteBuilder MapSurvey(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/survey/{token}", GetAsync).AllowAnonymous();
            app.MapPost("/api/survey/{token}", SubmitAsync).AllowAnonymous();
            return app;
        }

        // The fixed bilingual instrument the client renders — shared by the taker and results views.
        internal static object BuildInstrument() => new
        {
            title = SurveyContent.Title,
            titleFil = SurveyContent.TitleFil,
            scale = SurveyContent.Scale.Select(s => new { s.Value, s.En, s.Fil }),
            characteristics = SurveyContent.Characteristics.Select(c => new
            {
                c.Code,
                c.NameEn,
                c.NameFil,
                c.HintEn,
                c.HintFil,
                questions = c.Questions.Select(q => new { q.Key, q.En, q.Fil })
            }),
            suggestionsEn = SurveyContent.SuggestionsEn,
            suggestionsFil = SurveyContent.SuggestionsFil,
            commentsEn = SurveyContent.CommentsEn,
            commentsFil = SurveyContent.CommentsFil
        };

        private static async Task<IResult> GetAsync(string token, AppDbContext db, CancellationToken ct)
        {
            var invitation = await FindByTokenAsync(db, token, ct);
            if (invitation is null)
            {
                return Results.NotFound(new { message = "This survey link is invalid or has been withdrawn." });
            }

            return Results.Ok(new
            {
                completed = invitation.CompletedAtUtc is not null,
                respondent = new
                {
                    name = invitation.RecipientName,
                    email = invitation.RecipientEmail,
                    role = invitation.RecipientRole
                },
                instrument = BuildInstrument()
            });
        }

        private static async Task<IResult> SubmitAsync(
            string token, SubmitSurveyRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var invitation = await FindByTokenAsync(db, token, ct);
            if (invitation is null)
            {
                return Results.NotFound(new { message = "This survey link is invalid or has been withdrawn." });
            }
            if (invitation.CompletedAtUtc is not null)
            {
                return Results.Conflict(new { message = "This survey has already been answered. Thank you!" });
            }

            var errors = new Dictionary<string, string[]>();

            var name = string.IsNullOrWhiteSpace(request.RespondentName)
                ? invitation.RecipientName
                : request.RespondentName.Trim();
            if (string.IsNullOrWhiteSpace(name)) errors["respondentName"] = ["Your name is required."];

            // Every ISO 25010 item must be answered on the 1–5 scale.
            var answers = request.Answers ?? new Dictionary<string, int>();
            var missing = SurveyContent.AllQuestionKeys()
                .Where(k => !answers.TryGetValue(k, out var v) || v < 1 || v > 5)
                .ToList();
            if (missing.Count > 0)
            {
                errors["answers"] = ["Please answer every question (rate each from 1 to 5)."];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            // Keep only known question keys, so a tampered payload can't store junk.
            var clean = SurveyContent.AllQuestionKeys().ToDictionary(k => k, k => answers[k]);

            var response = new SurveyResponse
            {
                InvitationId = invitation.Id,
                RespondentName = name,
                RespondentEmail = invitation.RecipientEmail,
                RespondentRole = invitation.RecipientRole,
                Position = request.Position?.Trim() ?? string.Empty,
                Age = request.Age is > 0 and < 120 ? request.Age : null,
                Sex = request.Sex?.Trim() ?? string.Empty,
                Department = request.Department?.Trim() ?? string.Empty,
                YearsUsing = request.YearsUsing?.Trim() ?? string.Empty,
                AnswersJson = JsonSerializer.Serialize(clean),
                Suggestions = string.IsNullOrWhiteSpace(request.Suggestions) ? null : request.Suggestions.Trim(),
                FurtherComments = string.IsNullOrWhiteSpace(request.FurtherComments) ? null : request.FurtherComments.Trim()
            };
            db.SurveyResponses.Add(response);
            invitation.CompletedAtUtc = DateTime.UtcNow;

            audit.RecordAnonymous(AuditAction.SurveyResponseSubmitted,
                "Submitted the ISO 25010 rating survey.", name, "SurveyResponse", response.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { message = "Thank you! Your evaluation has been recorded." });
        }

        private static Task<SurveyInvitation?> FindByTokenAsync(AppDbContext db, string token, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return Task.FromResult<SurveyInvitation?>(null);
            }
            var hash = OneTimeToken.Hash(token.Trim());
            return db.SurveyInvitations.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        }
    }
}
