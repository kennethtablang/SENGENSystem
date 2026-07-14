using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SENGENSystem.Server.Common.Auth;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Auth.Login;
using SENGENSystem.Server.Features.Auth.Me;
using SENGENSystem.Server.Features.Auth.Register;
using SENGENSystem.Server.Features.Scheduling.Engine;
using SENGENSystem.Server.Features.Scheduling.GenerateSchedule;
using SENGENSystem.Server.Features.Scheduling.GetSchedule;

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

            builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
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

            // Scheduling slice (FR-SCHED, FR-FAC)
            app.MapGenerateSchedule();
            app.MapGetSchedule();

            app.MapFallbackToFile("/index.html");

            await app.RunAsync();
        }
    }
}
