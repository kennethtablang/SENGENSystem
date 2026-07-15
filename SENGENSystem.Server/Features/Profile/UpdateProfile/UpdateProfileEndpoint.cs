using System.Net.Mail;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Common.Validation;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Auth;

namespace SENGENSystem.Server.Features.Profile.UpdateProfile
{
    // Vertical slice: the signed-in user edits their own name and email.
    public record UpdateProfileRequest(string FirstName, string LastName, string Email);

    public record UpdateProfileResponse(AuthUserDto User);

    public static class UpdateProfileEndpoint
    {
        public static IEndpointRouteBuilder MapUpdateProfile(this IEndpointRouteBuilder app)
        {
            app.MapPut("/api/profile", HandleAsync).RequireAuthorization();
            return app;
        }

        private static async Task<IResult> HandleAsync(
            UpdateProfileRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            CancellationToken cancellationToken)
        {
            var idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(idClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var errors = Validate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var email = request.Email.Trim().ToLowerInvariant();
            var emailTaken = await db.Users.AnyAsync(u => u.Email == email && u.Id != userId, cancellationToken);
            if (emailTaken)
            {
                return Results.Conflict(new { message = "An account with this email address already exists." });
            }

            user.FirstName = NameFormatter.ToProperCase(request.FirstName);
            user.LastName = NameFormatter.ToProperCase(request.LastName);
            user.Email = email;
            audit.Record(AuditAction.ProfileUpdated,
                "Updated their own profile (name and email).", "User", user.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new UpdateProfileResponse(AuthUserDto.From(user)));
        }

        private static Dictionary<string, string[]> Validate(UpdateProfileRequest request)
        {
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.FirstName))
            {
                errors["firstName"] = ["First name is required."];
            }

            if (string.IsNullOrWhiteSpace(request.LastName))
            {
                errors["lastName"] = ["Last name is required."];
            }

            if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email.Trim(), out _))
            {
                errors["email"] = ["A valid email address is required."];
            }

            return errors;
        }
    }
}
