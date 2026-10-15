using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "commitment_months",
                schema: "billing",
                table: "subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<string>(
                name: "badge",
                schema: "billing",
                table: "plans",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "commitment_months",
                schema: "billing",
                table: "plans",
                type: "integer",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "billing",
                table: "plans",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            // The limits added to the plan features (stored as JSON) take their old values: videos stay at 3 where they were allowed.
            migrationBuilder.Sql(
                "UPDATE billing.plans SET features = features || jsonb_build_object('MaxVideos', CASE WHEN (features->>'Videos')::boolean THEN 3 ELSE 0 END);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "commitment_months",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "badge",
                schema: "billing",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "commitment_months",
                schema: "billing",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "billing",
                table: "plans");
        }
    }
}
