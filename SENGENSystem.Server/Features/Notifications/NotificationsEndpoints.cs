using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Notifications
{
    // Vertical slice: the signed-in user's bell notifications (FR-NOTIF). Read-only list
    // plus mark-read; notices are written by other slices through Common.Notifications.Notifier
    // when something happens to the user (schedule published, enlistment decided, ...).
    public record NotificationDto(
        Guid Id,
        string Kind,
        string Title,
        string Body,
        string? LinkTo,
        bool IsRead,
        string CreatedAtUtc)
    {
        public static NotificationDto From(Notification n) =>
            new(
                n.Id,
                n.Kind.ToString(),
                n.Title,
                n.Body,
                n.LinkTo,
                n.IsRead,
                DateTime.SpecifyKind(n.CreatedAtUtc, DateTimeKind.Utc).ToString("o"));
    }

    public static class NotificationsEndpoints
    {
        private const int DefaultTake = 50;
        private const int MaxTake = 200;

        public static IEndpointRouteBuilder MapNotifications(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/notifications").RequireAuthorization();

            group.MapGet("", ListAsync);
            group.MapPost("{id:guid}/read", MarkReadAsync);
            group.MapPost("read-all", MarkAllReadAsync);
            return app;
        }

        private static async Task<IResult> ListAsync(
            int? take,
            bool? unreadOnly,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            if (CurrentUserId(principal) is not { } userId)
            {
                return Results.Unauthorized();
            }

            var mine = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);

            var unreadCount = await mine.CountAsync(n => !n.IsRead, cancellationToken);

            var query = unreadOnly == true ? mine.Where(n => !n.IsRead) : mine;
            var items = await query
                .OrderByDescending(n => n.CreatedAtUtc)
                .Take(Math.Clamp(take ?? DefaultTake, 1, MaxTake))
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                unreadCount,
                count = items.Count,
                notifications = items.Select(NotificationDto.From).ToList()
            });
        }

        private static async Task<IResult> MarkReadAsync(
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            if (CurrentUserId(principal) is not { } userId)
            {
                return Results.Unauthorized();
            }

            var notification = await db.Notifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);
            if (notification is null)
            {
                return Results.NotFound(new { message = "Notification not found." });
            }

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(NotificationDto.From(notification));
        }

        private static async Task<IResult> MarkAllReadAsync(
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            if (CurrentUserId(principal) is not { } userId)
            {
                return Results.Unauthorized();
            }

            var marked = await db.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true), cancellationToken);

            return Results.Ok(new { marked });
        }

        private static Guid? CurrentUserId(ClaimsPrincipal principal) =>
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
}
