using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServicePayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "service_paid_at",
                schema: "scheduling",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "service_paid_method",
                schema: "scheduling",
                table: "appointments",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "service_paid_reference",
                schema: "scheduling",
                table: "appointments",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "service_paid_at",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "service_paid_method",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "service_paid_reference",
                schema: "scheduling",
                table: "appointments");
        }
    }
}
