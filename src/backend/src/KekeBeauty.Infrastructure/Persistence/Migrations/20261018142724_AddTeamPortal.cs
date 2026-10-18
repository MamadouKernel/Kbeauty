using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "commission_percent",
                schema: "scheduling",
                table: "resources",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                schema: "scheduling",
                table: "resources",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "finished_at",
                schema: "scheduling",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "started_at",
                schema: "scheduling",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tip_amount",
                schema: "scheduling",
                table: "appointments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "client_technical_notes",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allergies = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_technical_notes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_resources_user_id",
                schema: "scheduling",
                table: "resources",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_technical_notes_establishment_id_client_user_id",
                schema: "scheduling",
                table: "client_technical_notes",
                columns: new[] { "establishment_id", "client_user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_technical_notes",
                schema: "scheduling");

            migrationBuilder.DropIndex(
                name: "ix_resources_user_id",
                schema: "scheduling",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "commission_percent",
                schema: "scheduling",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "user_id",
                schema: "scheduling",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "finished_at",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "started_at",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "tip_amount",
                schema: "scheduling",
                table: "appointments");
        }
    }
}
