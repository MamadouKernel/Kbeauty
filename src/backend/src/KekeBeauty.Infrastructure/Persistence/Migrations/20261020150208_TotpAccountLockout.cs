using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TotpAccountLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "totp_failed_count",
                schema: "identity",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "totp_locked_until",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "totp_lockout_level",
                schema: "identity",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "totp_failed_count",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "totp_locked_until",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "totp_lockout_level",
                schema: "identity",
                table: "users");
        }
    }
}
