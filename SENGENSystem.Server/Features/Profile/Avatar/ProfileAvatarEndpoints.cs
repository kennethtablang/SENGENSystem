using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Auth;

namespace SENGENSystem.Server.Features.Profile.Avatar
{
    // Vertical slice: the signed-in user's own profile picture — upload, view, remove. Every user
    // of the system has one slot; it is only ever served back to its owner.
    public static class ProfileAvatarEndpoints
    {
        /// <summary>
        /// The client sends a 512×512 JPEG of a few dozen KB; the cap leaves room for a PNG or WebP
        /// without letting the table become file storage.
        /// </summary>
        public const int MaxBytes = 1024 * 1024;

        public static IEndpointRouteBuilder MapProfileAvatar(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/profile/avatar").RequireAuthorization();
            group.MapGet("", GetAsync);
            group.MapPut("", UploadAsync).DisableAntiforgery();
            group.MapDelete("", RemoveAsync);
            return app;
        }

        // GET /api/profile/avatar?v=<version> — the picture itself. The client asks with the version
        // from its user record, so a cached copy is right until the picture changes.
        private static async Task<IResult> GetAsync(
            ClaimsPrincipal principal, HttpContext http, AppDbContext db, CancellationToken ct)
        {
            if (!TryUserId(principal, out var userId)) return Results.Unauthorized();

            var avatar = await db.UserAvatars.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId, ct);
            if (avatar is null) return Results.NotFound();

            var etag = new EntityTagHeaderValue($"\"{avatar.UpdatedAtUtc.Ticks}\"");
            var headers = http.Response.Headers;
            // Private: a shared proxy must never hand one user's face to another. Never sniffed:
            // the bytes are served only as the image type they were verified to be.
            headers.CacheControl = "private, max-age=86400";
            headers.XContentTypeOptions = "nosniff";
            headers.ContentDisposition = "inline";
            return Results.File(avatar.Content, avatar.ContentType, entityTag: etag);
        }

        // PUT /api/profile/avatar — replace the picture (multipart field "file").
        private static async Task<IResult> UploadAsync(
            IFormFile? file, ClaimsPrincipal principal, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            if (!TryUserId(principal, out var userId)) return Results.Unauthorized();
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { message = "Choose a picture to upload." });
            }
            if (file.Length > MaxBytes)
            {
                return Results.BadRequest(new { message = "That picture is too large (1 MB max after resizing)." });
            }

            byte[] bytes;
            await using (var stream = file.OpenReadStream())
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer, ct);
                bytes = buffer.ToArray();
            }

            // What the bytes are, not what the upload claims to be.
            var contentType = SniffImageType(bytes);
            if (contentType is null)
            {
                return Results.BadRequest(new { message = "Only JPEG, PNG, or WebP pictures can be used." });
            }

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
            if (user is null) return Results.Unauthorized();

            var now = DateTime.UtcNow;
            var avatar = await db.UserAvatars.SingleOrDefaultAsync(a => a.UserId == userId, ct);
            if (avatar is null)
            {
                avatar = new UserAvatar { UserId = userId };
                db.UserAvatars.Add(avatar);
            }
            avatar.Content = bytes;
            avatar.ContentType = contentType;
            avatar.UpdatedAtUtc = now;
            user.AvatarUpdatedAtUtc = now;

            audit.Record(AuditAction.ProfileUpdated, "Updated their own profile picture.", "User", user.Id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { user = AuthUserDto.From(user) });
        }

        // DELETE /api/profile/avatar — back to initials.
        private static async Task<IResult> RemoveAsync(
            ClaimsPrincipal principal, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            if (!TryUserId(principal, out var userId)) return Results.Unauthorized();

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
            if (user is null) return Results.Unauthorized();

            var removed = await db.UserAvatars.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
            if (removed > 0 || user.AvatarUpdatedAtUtc is not null)
            {
                user.AvatarUpdatedAtUtc = null;
                audit.Record(AuditAction.ProfileUpdated, "Removed their own profile picture.", "User", user.Id.ToString());
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { user = AuthUserDto.From(user) });
        }

        /// <summary>JPEG, PNG, or WebP by their file signatures; null for anything else (GIF, SVG, HTML…).</summary>
        internal static string? SniffImageType(ReadOnlySpan<byte> b)
        {
            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
            if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
            if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
            return null;
        }

        private static bool TryUserId(ClaimsPrincipal principal, out Guid userId) =>
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId != Guid.Empty;
    }
}
