using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "discount_percent",
                schema: "billing",
                table: "subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "discount_periods_left",
                schema: "billing",
                table: "subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "promo_code_id",
                schema: "billing",
                table: "subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "promo_ends_at",
                schema: "billing",
                table: "plans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "promo_percent",
                schema: "billing",
                table: "plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "promo_periods",
                schema: "billing",
                table: "plans",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "promo_codes",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    percent = table.Column<int>(type: "integer", nullable: false),
                    periods = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    max_uses = table.Column<int>(type: "integer", nullable: true),
                    used_count = table.Column<int>(type: "integer", nullable: false),
                    plan_codes = table.Column<List<string>>(type: "text[]", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promo_codes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_promo_codes_code",
                schema: "billing",
                table: "promo_codes",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "promo_codes",
                schema: "billing");

            migrationBuilder.DropColumn(
                name: "discount_percent",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "discount_periods_left",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "promo_code_id",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "promo_ends_at",
                schema: "billing",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "promo_percent",
                schema: "billing",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "promo_periods",
                schema: "billing",
                table: "plans");
        }
    }
}
