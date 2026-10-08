using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Common.Validation;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration.SelfService
{
    // Only the fields supplied are changed. Deliberately a short list — see the slice comment.
    public record MyRegistrationUpdateRequest(
        string? FirstName,
        string? LastName,
        string? MiddleName,
        string? DateOfBirth,
        string? Birthplace,
        string? MobileNumber,
        string? AddressLine,
        string? Barangay,
        string? CityMunicipality,
        string? Province,
        string? ZipCode,
        string? GuardianName,
        string? GuardianMobile);

    /// <summary>
    /// Vertical slice (F-04): a student reads and corrects their own SIS while it is still awaiting
    /// confirmation. Before this a typo in a name or address needed a staff visit — the exact
    /// counter-queue friction the digital SIS exists to remove.
    /// <para>
    /// <b>Editable only while <see cref="RegistrationStatus.Submitted"/>.</b> Once the Registrar
    /// confirms, the record is what they vouched for, and changing it underneath them would make
    /// the confirmation mean nothing; from then on corrections go through staff as before.
    /// </para>
    /// <para>
    /// <b>The editable set is the typo-prone personal and contact fields</b> — names, birth details,
    /// mobile, address, guardian. Not the email (it is the account's sign-in and has its own
    /// verified change flow), and not program or student type: those decide the requirements
    /// checklist, the curriculum, and for a transferee the credit evaluation, and changing them is
    /// an admission decision rather than a correction.
    /// </para>
    /// </summary>
    public static class MyRegistrationEndpoints
    {
        public static IEndpointRouteBuilder MapMyRegistration(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/registration/mine", GetAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Student)));
            app.MapPut("/api/registration/mine", UpdateAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Student)));
            return app;
        }

        private static Guid? UserId(ClaimsPrincipal principal) =>
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        private static string? LockedReason(StudentRegistration r) => r.Status switch
        {
            RegistrationStatus.Submitted => null,
            RegistrationStatus.Confirmed =>
                "Your registration has been confirmed by the Registrar, so it can no longer be edited here. " +
                "Ask the Registrar's office to correct anything that is wrong.",
            _ => "This registration is closed and can no longer be edited."
        };

        private static object ToDto(StudentRegistration r) => new
        {
            id = r.Id,
            studentNumber = r.StudentNumber,
            status = r.Status.ToString(),
            canEdit = LockedReason(r) is null,
            lockedReason = LockedReason(r),
            program = r.Program.ToString(),
            studentType = r.StudentType.ToString(),
            email = r.Email,
            firstName = r.FirstName,
            lastName = r.LastName,
            middleName = r.MiddleName,
            dateOfBirth = r.DateOfBirth.ToString("yyyy-MM-dd"),
            birthplace = r.Birthplace,
            mobileNumber = r.MobileNumber,
            addressLine = r.AddressLine,
            barangay = r.Barangay,
            cityMunicipality = r.CityMunicipality,
            province = r.Province,
            zipCode = r.ZipCode,
            guardianName = r.GuardianName,
            guardianMobile = r.GuardianMobile
        };

        private static Task<StudentRegistration?> FindAsync(AppDbContext db, Guid userId, CancellationToken ct) =>
            db.StudentRegistrations
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefaultAsync(r => r.UserId == userId, ct);

        private static async Task<IResult> GetAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
        {
            if (UserId(principal) is not { } userId) return Results.Unauthorized();
            var registration = await FindAsync(db, userId, ct);
            return registration is null
                ? Results.NotFound(new { message = "No registration is linked to your account." })
                : Results.Ok(ToDto(registration));
        }

        private static async Task<IResult> UpdateAsync(
            MyRegistrationUpdateRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            CancellationToken ct)
        {
            if (UserId(principal) is not { } userId) return Results.Unauthorized();
            var registration = await FindAsync(db, userId, ct);
            if (registration is null)
            {
                return Results.NotFound(new { message = "No registration is linked to your account." });
            }
            if (LockedReason(registration) is { } locked)
            {
                return Results.Conflict(new { message = locked });
            }

            var errors = new Dictionary<string, string[]>();

            // A supplied field may not be blanked — every one of these is required on the SIS.
            // Middle name is the exception: not everyone has one.
            void Required(string key, string? value)
            {
                if (value is not null && string.IsNullOrWhiteSpace(value)) errors[key] = ["This field is required."];
            }
            Required("firstName", request.FirstName);
            Required("lastName", request.LastName);
            Required("birthplace", request.Birthplace);
            Required("mobileNumber", request.MobileNumber);
            Required("addressLine", request.AddressLine);
            Required("barangay", request.Barangay);
            Required("cityMunicipality", request.CityMunicipality);
            Required("province", request.Province);
            Required("zipCode", request.ZipCode);
            Required("guardianName", request.GuardianName);
            Required("guardianMobile", request.GuardianMobile);

            DateOnly? dob = null;
            if (request.DateOfBirth is not null)
            {
                if (!DateOnly.TryParse(request.DateOfBirth, out var parsed))
                {
                    errors["dateOfBirth"] = ["A valid date of birth is required."];
                }
                else
                {
                    dob = parsed;
                }
            }

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var changed = new List<string>();
            void Set(string label, string? value, Func<string> get, Action<string> set, bool caps = true)
            {
                if (value is null) return;
                var next = caps ? SisText.Caps(value) : value.Trim();
                if (next == get()) return;
                set(next);
                changed.Add(label);
            }

            var oldFirst = registration.FirstName;
            var oldLast = registration.LastName;

            Set("first name", request.FirstName, () => registration.FirstName, v => registration.FirstName = v);
            Set("last name", request.LastName, () => registration.LastName, v => registration.LastName = v);
            Set("middle name", request.MiddleName, () => registration.MiddleName, v => registration.MiddleName = v);
            Set("birthplace", request.Birthplace, () => registration.Birthplace, v => registration.Birthplace = v);
            Set("mobile number", request.MobileNumber, () => registration.MobileNumber, v => registration.MobileNumber = v, caps: false);
            Set("address", request.AddressLine, () => registration.AddressLine, v => registration.AddressLine = v);
            Set("barangay", request.Barangay, () => registration.Barangay, v => registration.Barangay = v);
            Set("city/municipality", request.CityMunicipality, () => registration.CityMunicipality, v => registration.CityMunicipality = v);
            Set("province", request.Province, () => registration.Province, v => registration.Province = v);
            Set("ZIP code", request.ZipCode, () => registration.ZipCode, v => registration.ZipCode = v, caps: false);
            Set("guardian name", request.GuardianName, () => registration.GuardianName, v => registration.GuardianName = v);
            Set("guardian mobile", request.GuardianMobile, () => registration.GuardianMobile, v => registration.GuardianMobile = v, caps: false);
            if (dob is { } d && d != registration.DateOfBirth)
            {
                registration.DateOfBirth = d;
                changed.Add("date of birth");
            }

            if (changed.Count == 0) return Results.Ok(ToDto(registration));

            // The account was provisioned from the SIS name (StudentAccountProvisioner), so a name
            // typo fixed here would otherwise survive on every screen that greets the student.
            if (registration.FirstName != oldFirst || registration.LastName != oldLast)
            {
                var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
                if (user is not null)
                {
                    user.FirstName = NameFormatter.ToProperCase(registration.FirstName);
                    user.LastName = NameFormatter.ToProperCase(registration.LastName);
                }
            }

            audit.Record(AuditAction.RegistrationSelfCorrected,
                $"{registration.StudentNumber} corrected their own SIS: {string.Join(", ", changed)}.",
                "StudentRegistration", registration.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToDto(registration));
        }
    }
}
