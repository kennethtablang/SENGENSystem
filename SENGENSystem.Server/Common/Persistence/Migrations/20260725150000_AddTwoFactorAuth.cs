using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    // FR-AUTH: opt-in two-factor authentication by emailed one-time code. Users gains the flag plus
    // the hashed current code, the hashed login challenge that binds a verify call to its password
    // step, the code's expiry, and a wrong-attempt counter. All nullable/zero-default, so existing
    // accounts are unchanged and simply have 2FA off until they enable it. Hand-authored so
    // MigrateAsync applies it at startup without a design-time build; the snapshot is updated to match.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260725150000_AddTwoFactorAuth")]
    public partial class AddTwoFactorAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TwoFactorEnabled",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorCodeHash",
                table: "Users",
                type: "nvarchar(88)",
                maxLength: 88,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorChallengeHash",
                table: "Users",
                type: "nvarchar(88)",
                maxLength: 88,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TwoFactorCodeExpiresUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TwoFactorAttempts",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TwoFactorEnabled", table: "Users");
            migrationBuilder.DropColumn(name: "TwoFactorCodeHash", table: "Users");
            migrationBuilder.DropColumn(name: "TwoFactorChallengeHash", table: "Users");
            migrationBuilder.DropColumn(name: "TwoFactorCodeExpiresUtc", table: "Users");
            migrationBuilder.DropColumn(name: "TwoFactorAttempts", table: "Users");
        }
    }
}
