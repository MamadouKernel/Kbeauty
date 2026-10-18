using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamPayoutsTipsPhotosLateFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tip_payment_id",
                schema: "billing",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "late_cancel_fee_percent",
                schema: "catalog",
                table: "establishments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "collaborator_payout_id",
                schema: "scheduling",
                table: "appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "commission_amount",
                schema: "scheduling",
                table: "appointments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "commission_user_id",
                schema: "scheduling",
                table: "appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "refund_amount",
                schema: "billing",
                table: "appointment_payments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "retained_net",
                schema: "billing",
                table: "appointment_payments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "collaborator_payout_accounts",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    @operator = table.Column<string>(name: "operator", type: "character varying(40)", maxLength: 40, nullable: false),
                    account = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    account_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collaborator_payout_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "collaborator_payouts",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    commission_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    tip_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
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
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    settled_against_payout_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collaborator_payouts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "review_photos",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    thumb_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_photos_reviews_review_id",
                        column: x => x.review_id,
                        principalSchema: "catalog",
                        principalTable: "reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tip_payments",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collaborator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_url = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    collaborator_payout_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tip_payments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tip_payment_id",
                schema: "billing",
                table: "payments",
                column: "tip_payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_collaborator_payout_id",
                schema: "scheduling",
                table: "appointments",
                column: "collaborator_payout_id");

            migrationBuilder.CreateIndex(
                name: "ix_collaborator_payout_accounts_user_id",
                schema: "billing",
                table: "collaborator_payout_accounts",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_collaborator_payouts_establishment_id_created_at",
                schema: "billing",
                table: "collaborator_payouts",
                columns: new[] { "establishment_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_collaborator_payouts_transaction_id",
                schema: "billing",
                table: "collaborator_payouts",
                column: "transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_collaborator_payouts_user_id_created_at",
                schema: "billing",
                table: "collaborator_payouts",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_review_photos_review_id",
                schema: "catalog",
                table: "review_photos",
                column: "review_id");

            migrationBuilder.CreateIndex(
                name: "ix_tip_payments_appointment_id",
                schema: "billing",
                table: "tip_payments",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_tip_payments_collaborator_payout_id",
                schema: "billing",
                table: "tip_payments",
                column: "collaborator_payout_id");

            migrationBuilder.CreateIndex(
                name: "ix_tip_payments_collaborator_user_id_establishment_id_status",
                schema: "billing",
                table: "tip_payments",
                columns: new[] { "collaborator_user_id", "establishment_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collaborator_payout_accounts",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "collaborator_payouts",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "review_photos",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "tip_payments",
                schema: "billing");

            migrationBuilder.DropIndex(
                name: "ix_payments_tip_payment_id",
                schema: "billing",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_appointments_collaborator_payout_id",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "tip_payment_id",
                schema: "billing",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "late_cancel_fee_percent",
                schema: "catalog",
                table: "establishments");

            migrationBuilder.DropColumn(
                name: "collaborator_payout_id",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "commission_amount",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "commission_user_id",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "refund_amount",
                schema: "billing",
                table: "appointment_payments");

            migrationBuilder.DropColumn(
                name: "retained_net",
                schema: "billing",
                table: "appointment_payments");
        }
    }
}
