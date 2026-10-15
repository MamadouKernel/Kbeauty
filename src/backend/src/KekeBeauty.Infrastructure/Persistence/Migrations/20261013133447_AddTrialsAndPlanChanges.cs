using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialsAndPlanChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pending_change_at",
                schema: "billing",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_cycle",
                schema: "billing",
                table: "subscriptions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pending_plan_id",
                schema: "billing",
                table: "subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "pending_unit_price",
                schema: "billing",
                table: "subscriptions",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replaces_subscription_id",
                schema: "billing",
                table: "subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trial_ends_at",
                schema: "billing",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "trial_days",
                schema: "billing",
                table: "plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pending_change_at",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "pending_cycle",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "pending_plan_id",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "pending_unit_price",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "replaces_subscription_id",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "trial_ends_at",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "trial_days",
                schema: "billing",
                table: "plans");
        }
    }
}
