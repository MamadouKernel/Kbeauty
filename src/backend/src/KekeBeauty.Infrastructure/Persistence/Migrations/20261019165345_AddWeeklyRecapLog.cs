using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyRecapLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "weekly_recap_logs",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_weekly_recap_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_weekly_recap_logs_establishment_id_week_start",
                schema: "scheduling",
                table: "weekly_recap_logs",
                columns: new[] { "establishment_id", "week_start" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "weekly_recap_logs",
                schema: "scheduling");
        }
    }
}
