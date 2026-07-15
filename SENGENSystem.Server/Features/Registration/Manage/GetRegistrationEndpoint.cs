using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration.Manage
{
    // Vertical slice: the Registrar opens a single SIS record with its full detail and document
    // checklist (FR-SIS-04, FR-DOC-03).
    public static class GetRegistrationEndpoint
    {
        public static IEndpointRouteBuilder MapGetRegistration(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/registration/{id:guid}", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Registrar)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid id,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var registration = await db.StudentRegistrations
                .AsNoTracking()
                .Include(r => r.Semester)
                .Include(r => r.Documents)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

            return registration is null
                ? Results.NotFound(new { message = "Registration not found." })
                : Results.Ok(StudentRegistrationDto.From(registration));
        }
    }
}
