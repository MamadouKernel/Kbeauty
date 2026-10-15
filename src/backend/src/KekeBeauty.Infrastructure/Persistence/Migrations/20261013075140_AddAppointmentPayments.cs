using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "invoice_id",
                schema: "billing",
                table: "payments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "appointment_payment_id",
                schema: "billing",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "deposit_percent",
                schema: "catalog",
                table: "establishments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "payment_mode",
                schema: "catalog",
                table: "establishments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "payout_account",
                schema: "catalog",
                table: "establishments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payout_account_name",
                schema: "catalog",
                table: "establishments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payout_operator",
                schema: "catalog",
                table: "establishments",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "awaiting_payment",
                schema: "scheduling",
                table: "appointments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "appointment_payments",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    platform_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payment_url = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    payout_id = table.Column<Guid>(type: "uuid", nullable: true),
                    refund_transaction_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    refund_provider_reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    refunded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refund_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_payments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payouts",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    @operator = table.Column<string>(name: "operator", type: "character varying(40)", maxLength: 40, nullable: false),
                    account = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    account_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    transaction_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payouts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_appointment_payment_id",
                schema: "billing",
                table: "payments",
                column: "appointment_payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_payments_appointment_id",
                schema: "billing",
                table: "appointment_payments",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_payments_establishment_id_status",
                schema: "billing",
                table: "appointment_payments",
                columns: new[] { "establishment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_appointment_payments_payout_id",
                schema: "billing",
                table: "appointment_payments",
                column: "payout_id");

            migrationBuilder.CreateIndex(
                name: "ix_payouts_establishment_id_created_at",
                schema: "billing",
                table: "payouts",
                columns: new[] { "establishment_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payouts_transaction_id",
                schema: "billing",
                table: "payouts",
                column: "transaction_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointment_payments",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "payouts",
                schema: "billing");

            migrationBuilder.DropIndex(
                name: "ix_payments_appointment_payment_id",
                schema: "billing",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "appointment_payment_id",
                schema: "billing",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "deposit_percent",
                schema: "catalog",
                table: "establishments");

            migrationBuilder.DropColumn(
                name: "payment_mode",
                schema: "catalog",
                table: "establishments");

            migrationBuilder.DropColumn(
                name: "payout_account",
                schema: "catalog",
                table: "establishments");

            migrationBuilder.DropColumn(
                name: "payout_account_name",
                schema: "catalog",
                table: "establishments");

            migrationBuilder.DropColumn(
                name: "payout_operator",
                schema: "catalog",
                table: "establishments");

            migrationBuilder.DropColumn(
                name: "awaiting_payment",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.AlterColumn<Guid>(
                name: "invoice_id",
                schema: "billing",
                table: "payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
