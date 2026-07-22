using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.AcademicSetup.Rooms
{
    // Vertical slice: the School Admin manages teaching rooms within a building — lecture rooms
    // and the Computer/Kitchen laboratories alike. Capacity and kind are hard-constraint inputs to
    // the scheduling engine (FR-SCHED-02): laboratory hours are placed only in the laboratory the
    // subject requires. Safe delete refuses to remove a room already placed in the schedule (409).
    public record RoomRequest(string? Name, int? Capacity, string? Kind, Guid? BuildingId);

    public static class RoomsEndpoints
    {
        public static IEndpointRouteBuilder MapRooms(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/rooms")
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.SchoolAdmin)));

            group.MapGet("", ListAsync);
            group.MapPost("", CreateAsync);
            group.MapPut("/{id:guid}", UpdateAsync);
            group.MapDelete("/{id:guid}", DeleteAsync);
            return app;
        }

        private static async Task<IResult> ListAsync(Guid? buildingId, AppDbContext db, CancellationToken ct)
        {
            var query = db.Rooms.AsNoTracking().Include(r => r.Building).AsQueryable();
            if (buildingId is { } bid) query = query.Where(r => r.BuildingId == bid);

            var rooms = await query
                .OrderBy(r => r.Name)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                count = rooms.Count,
                rooms = rooms.Select(RoomDto.From).ToList()
            });
        }

        private static async Task<IResult> CreateAsync(
            RoomRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var (ok, name, capacity, kind, buildingId, problem) = await ValidateAsync(request, db, ct);
            if (!ok) return problem;

            var room = new Room
            {
                Name = name,
                Capacity = capacity,
                Kind = kind,
                BuildingId = buildingId
            };
            db.Rooms.Add(room);
            audit.Record(AuditAction.RoomSaved, $"Created {kind.Label().ToLowerInvariant()} “{room.Name}”.",
                "Room", room.Id.ToString());
            await db.SaveChangesAsync(ct);

            await db.Entry(room).Reference(r => r.Building).LoadAsync(ct);
            return Results.Created($"/api/rooms/{room.Id}", RoomDto.From(room));
        }

        private static async Task<IResult> UpdateAsync(
            Guid id, RoomRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (room is null) return Results.NotFound(new { message = "Room not found." });

            var (ok, name, capacity, kind, buildingId, problem) = await ValidateAsync(request, db, ct);
            if (!ok) return problem;

            // Re-typing an occupied room would silently break the room-kind hard constraint on
            // classes already plotted there (a lecture stranded in the kitchen laboratory).
            if (kind != room.Kind && await db.ScheduleAssignments.AnyAsync(a => a.RoomId == id, ct))
            {
                return Results.Conflict(new
                {
                    message = $"“{room.Name}” already has classes scheduled in it, so its type can't be changed. " +
                        "Remove those placements first, or create a separate room."
                });
            }

            room.Name = name;
            room.Capacity = capacity;
            room.Kind = kind;
            room.BuildingId = buildingId;

            audit.Record(AuditAction.RoomSaved, $"Updated room “{room.Name}” ({kind.Label().ToLowerInvariant()}).",
                "Room", room.Id.ToString());
            await db.SaveChangesAsync(ct);

            await db.Entry(room).Reference(r => r.Building).LoadAsync(ct);
            return Results.Ok(RoomDto.From(room));
        }

        private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (room is null) return Results.NotFound(new { message = "Room not found." });

            if (await db.ScheduleAssignments.AnyAsync(a => a.RoomId == id, ct))
            {
                return Results.Conflict(new { message = "This room is used in the schedule and can't be deleted." });
            }

            db.Rooms.Remove(room);
            audit.Record(AuditAction.RoomSaved, $"Deleted room “{room.Name}”.", "Room", room.Id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        private static async Task<(bool Ok, string Name, int Capacity, RoomKind Kind, Guid? BuildingId, IResult Problem)>
            ValidateAsync(RoomRequest request, AppDbContext db, CancellationToken ct)
        {
            var name = request.Name?.Trim() ?? string.Empty;
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["A room name is required."];

            if (!Enum.TryParse<RoomKind>(request.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
            {
                errors["kind"] = ["Please choose whether this is a lecture room, computer laboratory, or kitchen laboratory."];
            }

            if (request.Capacity is not { } cap || cap <= 0)
            {
                errors["capacity"] = ["Capacity must be a positive number."];
            }

            if (request.BuildingId is not { } bid)
            {
                errors["buildingId"] = ["Please choose a building."];
            }
            else if (!await db.Buildings.AnyAsync(b => b.Id == bid, ct))
            {
                errors["buildingId"] = ["The selected building no longer exists."];
            }

            if (errors.Count > 0)
            {
                return (false, name, 0, default, null, Results.ValidationProblem(errors));
            }

            return (true, name, request.Capacity!.Value, kind, request.BuildingId, Results.Empty);
        }
    }
}
