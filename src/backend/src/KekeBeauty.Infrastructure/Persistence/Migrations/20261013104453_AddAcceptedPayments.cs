using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAcceptedPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "accepts_cash",
                schema: "catalog",
                table: "establishments",
                type: "boolean",
                nullable: true,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "mobile_money",
                schema: "catalog",
                table: "establishments",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "accepts_cash",
                schema: "catalog",
                table: "establishments");

            migrationBuilder.DropColumn(
                name: "mobile_money",
                schema: "catalog",
                table: "establishments");
        }
    }
}
