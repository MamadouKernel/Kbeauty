using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServicePromosAndPlanPerks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "perks",
                schema: "catalog",
                table: "services",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "promo_ends_at",
                schema: "catalog",
                table: "services",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "promo_percent",
                schema: "catalog",
                table: "services",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "perks",
                schema: "catalog",
                table: "services");

            migrationBuilder.DropColumn(
                name: "promo_ends_at",
                schema: "catalog",
                table: "services");

            migrationBuilder.DropColumn(
                name: "promo_percent",
                schema: "catalog",
                table: "services");
        }
    }
}
