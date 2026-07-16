using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Auth;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.AcademicSetup.Buildings;
using SENGENSystem.Server.Features.AcademicSetup.ClassSections;
using SENGENSystem.Server.Features.AcademicSetup.Rooms;
using SENGENSystem.Server.Features.AcademicSetup.SchoolYears;
using SENGENSystem.Server.Features.AcademicSetup.Semesters;
using SENGENSystem.Server.Features.Audit.GetAuditTrail;
using SENGENSystem.Server.Features.Curriculum.Curricula;
using SENGENSystem.Server.Features.Curriculum.Subjects;
using SENGENSystem.Server.Features.FacultyLoad;
using SENGENSystem.Server.Features.Auth.Login;
using SENGENSystem.Server.Features.Auth.Me;
using SENGENSystem.Server.Features.Auth.Register;
using SENGENSystem.Server.Features.Profile.ChangePassword;
using SENGENSystem.Server.Features.Profile.UpdateProfile;
using SENGENSystem.Server.Features.Registration.Manage;
using SENGENSystem.Server.Features.Registration.RegisterStudent;
using SENGENSystem.Server.Features.Registration.TermActivation;
using SENGENSystem.Server.Features.Scheduling.Board;
using SENGENSystem.Server.Features.Scheduling.Engine;
using SENGENSystem.Server.Features.Scheduling.GenerateSchedule;
using SENGENSystem.Server.Features.Scheduling.GetSchedule;
using SENGENSystem.Server.Features.UserManagement.CreateUser;
using SENGENSystem.Server.Features.UserManagement.ListUsers;
using SENGENSystem.Server.Features.UserManagement.ResetUserPassword;
using SENGENSystem.Server.Features.UserManagement.SetUserActive;
using SENGENSystem.Server.Features.UserManagement.UpdateUser;

namespace SENGENSystem.Server
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            builder.Services.AddOpenApi();

            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
            builder.Services.AddScoped<AuditLog>();
            builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
            builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
            builder.Services.AddSingleton<JwtTokenService>();
            builder.Services.AddSingleton<CspScheduler>();
            builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

            var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                ?? throw new InvalidOperationException("Missing Jwt configuration section.");

            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwt.Issuer,
                        ValidAudience = jwt.Audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key))
                    };
                });
            builder.Services.AddAuthorization();

            var app = builder.Build();

            await DbInitializer.InitializeAsync(app.Services);

            app.UseDefaultFiles();
            app.MapStaticAssets();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            // Feature slices (Vertical Slice Architecture)
            app.MapRegister();
            app.MapLogin();
            app.MapMe();

            // Profile slice (self-service account editing)
            app.MapUpdateProfile();
            app.MapChangePassword();

            // Scheduling slice (FR-SCHED, FR-FAC)
            app.MapGenerateSchedule();
            app.MapGetSchedule();
            app.MapScheduleBoard();

            // Registration slice — digital SIS + term activation (FR-SIS, FR-DOC, FR-NOTIF)
            app.MapRegisterStudent();
            app.MapRequestTermActivation();
            app.MapListTermActivations();
            app.MapValidateTermActivation();
            app.MapListRegistrations();
            app.MapGetRegistration();
            app.MapUpdateRegistration();

            // Academic setup slice — School Admin manages school years, semesters, buildings, rooms
            app.MapSchoolYears();
            app.MapSemesters();
            app.MapBuildings();
            app.MapRooms();
            app.MapClassSections();

            // Curriculum slice — Academic Head manages program curricula and their subjects (FR-SCHED-04)
            app.MapCurricula();
            app.MapSubjects();

            // Faculty load slice — Academic Head allocates subjects to faculty (FR-FAC-01)
            app.MapFacultyLoad();

            // User management slice — School Admin account CRUD (FR-AUTH-07)
            app.MapListUsers();
            app.MapCreateUser();
            app.MapUpdateUser();
            app.MapSetUserActive();
            app.MapResetUserPassword();

            // Audit trail slice (FR-AUD)
            app.MapGetAuditTrail();

            app.MapFallbackToFile("/index.html");

            await app.RunAsync();
        }
    }
}
