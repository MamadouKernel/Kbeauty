using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhatsAppConsentRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "whatsapp_consents",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    purpose = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    granted = table.Column<bool>(type: "boolean", nullable: false),
                    text_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    text_snapshot = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_whatsapp_consents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_consents_user_id_phone_purpose_at",
                schema: "notifications",
                table: "whatsapp_consents",
                columns: new[] { "user_id", "phone", "purpose", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "whatsapp_consents",
                schema: "notifications");
        }
    }
}
