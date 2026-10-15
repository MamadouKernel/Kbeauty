using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "admin");

            migrationBuilder.EnsureSchema(
                name: "onboarding");

            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "establishment_reviews",
                schema: "onboarding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_establishment_reviews", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kyc_identities",
                schema: "onboarding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    holder_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    document_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    document_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    document_expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    front_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    back_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    selfie_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kyc_identities", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_type_entity_id_at",
                schema: "admin",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_establishment_reviews_establishment_id",
                schema: "onboarding",
                table: "establishment_reviews",
                column: "establishment_id");

            migrationBuilder.CreateIndex(
                name: "ix_establishment_reviews_status_submitted_at",
                schema: "onboarding",
                table: "establishment_reviews",
                columns: new[] { "status", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_kyc_identities_document_type_document_number",
                schema: "onboarding",
                table: "kyc_identities",
                columns: new[] { "document_type", "document_number" });

            migrationBuilder.CreateIndex(
                name: "ix_kyc_identities_user_id",
                schema: "onboarding",
                table: "kyc_identities",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "establishment_reviews",
                schema: "onboarding");

            migrationBuilder.DropTable(
                name: "kyc_identities",
                schema: "onboarding");
        }
    }
}
