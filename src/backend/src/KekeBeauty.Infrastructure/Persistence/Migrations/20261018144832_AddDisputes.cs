using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDisputes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "disputes",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolution = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    partner_response = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    partner_responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    admin_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disputes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_appointment_id",
                schema: "scheduling",
                table: "disputes",
                column: "appointment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_disputes_client_user_id",
                schema: "scheduling",
                table: "disputes",
                column: "client_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_establishment_id_created_at",
                schema: "scheduling",
                table: "disputes",
                columns: new[] { "establishment_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_status_created_at",
                schema: "scheduling",
                table: "disputes",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "disputes",
                schema: "scheduling");
        }
    }
}
