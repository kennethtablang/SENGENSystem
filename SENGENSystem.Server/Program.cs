using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Auth;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.AcademicSetup.Buildings;
using SENGENSystem.Server.Features.Analytics.RoomUtilization;
using SENGENSystem.Server.Features.AcademicSetup.ClassSections;
using SENGENSystem.Server.Features.AcademicSetup.Rooms;
using SENGENSystem.Server.Features.AcademicSetup.SchoolYears;
using SENGENSystem.Server.Features.AcademicSetup.Semesters;
using SENGENSystem.Server.Features.Audit.GetAuditTrail;
using SENGENSystem.Server.Features.Curriculum.Curricula;
using SENGENSystem.Server.Features.Curriculum.Subjects;
using SENGENSystem.Server.Features.Dashboard;
using SENGENSystem.Server.Features.EnrollmentCycle;
using SENGENSystem.Server.Features.Documents.Checklist;
using SENGENSystem.Server.Features.Documents.Reminders;
using SENGENSystem.Server.Features.Documents.Requirements;
using SENGENSystem.Server.Features.Enlistment.Approvals;
using SENGENSystem.Server.Features.Enlistment.Browse;
using SENGENSystem.Server.Features.Enlistment.MyEnlistment;
using SENGENSystem.Server.Features.Enlistment.RequestSlot;
using SENGENSystem.Server.Features.FacultyLoad;
using SENGENSystem.Server.Features.FacultyLoad.Preferences;
using SENGENSystem.Server.Features.Navigation;
using SENGENSystem.Server.Features.Notifications;
using SENGENSystem.Server.Features.Survey;
using SENGENSystem.Server.Features.PreEnrollment.Import;
using SENGENSystem.Server.Features.PreEnrollment.PreAuthorize;
using SENGENSystem.Server.Features.Registration.AssignStudentNumber;
using SENGENSystem.Server.Features.Registration.LinkAccount;
using SENGENSystem.Server.Features.Auth.ForgotPassword;
using SENGENSystem.Server.Features.Auth.Login;
using SENGENSystem.Server.Features.Auth.TwoFactor;
using SENGENSystem.Server.Features.Profile.TwoFactor;
using SENGENSystem.Server.Features.Auth.Me;
using SENGENSystem.Server.Features.Auth.Register;
using SENGENSystem.Server.Features.Profile.ChangeEmail;
using SENGENSystem.Server.Features.Profile.ChangePassword;
using SENGENSystem.Server.Features.Profile.UpdateProfile;
using SENGENSystem.Server.Features.Publishing.GetPublishedSchedule;
using SENGENSystem.Server.Features.Publishing.PublishSchedule;
using SENGENSystem.Server.Features.Registration.Manage;
using SENGENSystem.Server.Features.Reports;
using SENGENSystem.Server.Features.Reports.FacultyLoading;
using SENGENSystem.Server.Features.Reports.Live;
using SENGENSystem.Server.Features.Reports.RoomGrid;
using SENGENSystem.Server.Features.Reports.SemesterExport;
using SENGENSystem.Server.Features.Reports.SystemExport;
using SENGENSystem.Server.Features.Registration.RegisterStudent;
using SENGENSystem.Server.Features.Registration.TermActivation;
using SENGENSystem.Server.Features.SystemParameters;
using SENGENSystem.Server.Features.Scheduling.Board;
using SENGENSystem.Server.Features.Scheduling.Engine;
using SENGENSystem.Server.Features.Scheduling.Finalize;
using SENGENSystem.Server.Features.Scheduling.GenerateSchedule;
using SENGENSystem.Server.Features.Scheduling.GetSchedule;
using SENGENSystem.Server.Features.Scheduling.SoftConstraints;
using SENGENSystem.Server.Features.Scheduling.MySchedule;
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
            // QuestPDF is used for the Consolidated Faculty Loading Report (FR-RPT-02).
            // The Community license is free for organizations under $1M USD annual revenue;
            // above that threshold this must be changed to a purchased license type.
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            builder.Services.AddOpenApi();

            // Swagger / Swashbuckle — interactive API surface for monitoring and testing.
            // Served dev-only below (mirrors the MapOpenApi guard). The Bearer security
            // definition adds an "Authorize" button so JWT-protected endpoints are testable.
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "SEN-GEN API",
                    Version = "v1",
                    Description = "SEN-GEN scheduling & enrollment system API."
                });

                // JwtBearerDefaults.AuthenticationScheme == "Bearer"
                options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Paste a JWT access token (the raw token — the 'Bearer ' prefix is added automatically)."
                });

                options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document, null)] = new List<string>()
                });
            });

            // Without this, an unhandled exception returns a bare 500 with an *empty* body, so the
            // client has nothing to show but a generic "Something went wrong". ProblemDetails +
            // the exception handler below guarantee every failure carries a JSON body with a
            // message and a trace id the user can quote when reporting it.
            builder.Services.AddProblemDetails();

            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
            builder.Services.AddScoped<AuditLog>();
            builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
            builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
            builder.Services.AddScoped<Notifier>();
            builder.Services.AddSignalR();
            builder.Services.AddSingleton<ReportsBroadcaster>();
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
                    // SignalR cannot send an Authorization header on WebSocket connects — the
                    // standard pattern is the access_token query parameter, scoped to hub paths.
                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            var accessToken = context.Request.Query["access_token"];
                            if (!string.IsNullOrEmpty(accessToken)
                                && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            {
                                context.Token = accessToken;
                            }
                            return Task.CompletedTask;
                        }
                    };
                });
            // A School Admin oversees the whole system: this hands their principal every role
            // claim so every RequireRole endpoint accepts them (FR-AUTH-08).
            builder.Services.AddSingleton<Microsoft.AspNetCore.Authentication.IClaimsTransformation, SchoolAdminClaimsTransformation>();
            builder.Services.AddAuthorization();

            var app = builder.Build();

            await DbInitializer.InitializeAsync(app.Services);

            // Global safety net: turn any unhandled exception into a JSON ProblemDetails response
            // instead of an empty-bodied 500. Registered first so it wraps the whole pipeline.
            app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
            {
                var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
                var ex = feature?.Error;
                var traceId = context.TraceIdentifier;

                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("UnhandledException");
                logger.LogError(ex, "Unhandled exception on {Path} [trace {TraceId}]",
                    feature?.Path, traceId);

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/problem+json";

                // This is an internal institutional system, so the exception summary is a
                // reportable diagnostic rather than a leak — it gives staff a real lead instead
                // of a shrug. The trace id ties it to the full stack trace in the server log.
                await context.Response.WriteAsJsonAsync(new
                {
                    title = "The server hit an unexpected error.",
                    detail = ex is null
                        ? "An unknown error occurred."
                        : $"{ex.GetType().Name}: {ex.Message}",
                    status = StatusCodes.Status500InternalServerError,
                    reference = traceId,
                    message = ex is null ? "An unknown error occurred." : $"{ex.GetType().Name}: {ex.Message}"
                });
            }));

            app.UseDefaultFiles();
            app.MapStaticAssets();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();

                app.UseSwagger();
                app.UseSwaggerUI(options =>
                {
                    options.SwaggerEndpoint("/swagger/v1/swagger.json", "SEN-GEN API v1");
                    options.DocumentTitle = "SEN-GEN API";
                });
            }

            app.UseHttpsRedirection();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            // Feature slices (Vertical Slice Architecture)
            app.MapRegister();
            app.MapLogin();
            app.MapTwoFactor();
            app.MapMe();
            app.MapForgotPassword();

            // Profile slice (self-service account editing)
            app.MapUpdateProfile();
            app.MapChangePassword();
            app.MapChangeEmail();
            app.MapProfileTwoFactor();

            // Scheduling slice (FR-SCHED, FR-FAC)
            app.MapGenerateSchedule();
            app.MapGetSchedule();
            app.MapSoftConstraints();
            app.MapFinalizeSchedule();
            app.MapScheduleBoard();
            app.MapMySchedule();

            // Publishing slice — Registrar publishes finalized schedules (FR-PUB)
            app.MapPublishSchedule();
            app.MapGetPublishedSchedule();

            // Registration slice — digital SIS + term activation (FR-SIS, FR-DOC, FR-NOTIF)
            app.MapRegisterStudent();
            app.MapLookupTermActivation();
            app.MapRequestTermActivation();
            app.MapListTermActivations();
            app.MapValidateTermActivation();
            app.MapTermActivationControl();
            app.MapListRegistrations();
            app.MapGetRegistration();
            app.MapUpdateRegistration();
            app.MapLinkAccount();
            // Admission Officer records the external student number against a registration (FR-SIS)
            app.MapAssignStudentNumber();

            // Documents slice — Admission Officer checklist board + reminder emails (FR-DOC)
            app.MapDocumentChecklist();
            app.MapDocumentReminders();
            app.MapRequirements();

            // Pre-enrollment slice — .xlsx ETL import + Admission Officer pre-authorization (FR-PRE)
            app.MapPreEnrollmentImport();
            app.MapPreAuthorization();

            // Enlistment slice — student slot selection + Registrar approvals (FR-ENL)
            app.MapBrowseSections();
            app.MapRequestSlot();
            app.MapMyEnlistment();
            app.MapEnlistmentApprovals();

            // Enrollment cycle slice — the active term's stage banner; Registrar advances it
            app.MapEnrollmentStage();

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
            // and records preferred teaching windows for the engine (FR-SCHED-03)
            app.MapFacultyLoad();
            app.MapFacultyPreferences();

            // System parameters slice — School Admin tunes the scheduling engine's institutional
            // inputs: allowable time slots, unit-load ceilings, section seat cap (FR-SCHED-05)
            app.MapSystemParameters();

            // User management slice — School Admin account CRUD (FR-AUTH-07)
            app.MapListUsers();
            app.MapCreateUser();
            app.MapUpdateUser();
            app.MapSetUserActive();
            app.MapResetUserPassword();

            // Dashboard slice — live semester-scoped metrics + scheduling transparency (FR-DASH)
            app.MapDashboardMetrics();
            app.MapSchedulingTransparency();

            // Analytics slice — institution-wide classroom usage analysis (FR-DASH-02)
            app.MapRoomUtilizationAnalysis();
            app.MapRoomUtilizationExcel();

            // Reports slice — semester-scoped, exportable reports (FR-RPT) + live push channel
            app.MapReports();
            app.MapFacultyLoadingReports();
            app.MapRoomGridSchedule();
            app.MapSemesterExport();
            app.MapSystemParametersExport();
            app.MapHub<ReportsHub>("/hubs/reports");

            // Notifications slice — the signed-in user's in-app bell notices (FR-NOTIF)
            app.MapNotifications();

            // Sidebar badge counts — role-scoped outstanding-work numbers for the nav
            app.MapNavBadges();

            // ISO/IEC 25010 rating survey — public token-gated taker + Super Admin dispatch/results
            app.MapSurvey();
            app.MapSurveyAdmin();

            // Audit trail slice (FR-AUD)
            app.MapGetAuditTrail();

            app.MapFallbackToFile("/index.html");

            await app.RunAsync();
        }
    }
}
