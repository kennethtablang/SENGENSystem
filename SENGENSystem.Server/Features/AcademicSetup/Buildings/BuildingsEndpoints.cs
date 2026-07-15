using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.AcademicSetup.Buildings
{
    // Vertical slice: the School Admin manages campus buildings that group teaching rooms.
    // Safe delete refuses to remove a building that still has rooms (409).
    public record BuildingRequest(string? Name, string? Code);

    public static class BuildingsEndpoints
    {
        public static IEndpointRouteBuilder MapBuildings(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/buildings")
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.SchoolAdmin)));

            group.MapGet("", ListAsync);
            group.MapPost("", CreateAsync);
            group.MapPut("/{id:guid}", UpdateAsync);
            group.MapDelete("/{id:guid}", DeleteAsync);
            return app;
        }

        private static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct)
        {
            var buildings = await db.Buildings
                .AsNoTracking()
                .OrderBy(b => b.Name)
                .Select(b => new { Building = b, Count = db.Rooms.Count(r => r.BuildingId == b.Id) })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                count = buildings.Count,
                buildings = buildings.Select(x => BuildingDto.From(x.Building, x.Count)).ToList()
            });
        }

        private static async Task<IResult> CreateAsync(
            BuildingRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            if (!Validate(request, out var name, out var code, out var problem)) return problem;

            var building = new Building { Name = name, Code = code };
            db.Buildings.Add(building);
            audit.Record(AuditAction.BuildingSaved, $"Created building “{building.Name}”.",
                "Building", building.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/buildings/{building.Id}", BuildingDto.From(building, 0));
        }

        private static async Task<IResult> UpdateAsync(
            Guid id, BuildingRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var building = await db.Buildings.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (building is null) return Results.NotFound(new { message = "Building not found." });

            if (!Validate(request, out var name, out var code, out var problem)) return problem;

            building.Name = name;
            building.Code = code;
            audit.Record(AuditAction.BuildingSaved, $"Updated building “{building.Name}”.",
                "Building", building.Id.ToString());
            await db.SaveChangesAsync(ct);

            var count = await db.Rooms.CountAsync(r => r.BuildingId == building.Id, ct);
            return Results.Ok(BuildingDto.From(building, count));
        }

        private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var building = await db.Buildings.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (building is null) return Results.NotFound(new { message = "Building not found." });

            if (await db.Rooms.AnyAsync(r => r.BuildingId == id, ct))
            {
                return Results.Conflict(new { message = "This building still has rooms. Move or delete them first." });
            }

            db.Buildings.Remove(building);
            audit.Record(AuditAction.BuildingSaved, $"Deleted building “{building.Name}”.",
                "Building", building.Id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        private static bool Validate(BuildingRequest request, out string name, out string? code, out IResult problem)
        {
            name = request.Name?.Trim() ?? string.Empty;
            code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                problem = Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["A building name is required."] });
                return false;
            }

            problem = Results.Empty;
            return true;
        }
    }
}
