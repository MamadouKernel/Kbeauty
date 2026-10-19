using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGrowthFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reengagement_sent_at",
                schema: "scheduling",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "gift_cards",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    initial_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    recipient_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    buyer_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    holder_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gift_cards", x => x.id);
                    table.CheckConstraint("ck_gift_cards_balance", "balance >= 0 AND balance <= initial_amount");
                });

            migrationBuilder.CreateTable(
                name: "group_booking_requests",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    desired_starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    group_size = table.Column<int>(type: "integer", nullable: false),
                    services = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    quoted_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    partner_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_booking_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_programs",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    points_per1000 = table.Column<int>(type: "integer", nullable: false),
                    reward_threshold = table.Column<int>(type: "integer", nullable: false),
                    reward_description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    active_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loyalty_programs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_transactions",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loyalty_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "waitlist_entries",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_waitlist_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gift_card_redemptions",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gift_card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gift_card_redemptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_gift_card_redemptions_gift_cards_gift_card_id",
                        column: x => x.gift_card_id,
                        principalSchema: "scheduling",
                        principalTable: "gift_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gift_card_redemptions_gift_card_id",
                schema: "scheduling",
                table: "gift_card_redemptions",
                column: "gift_card_id");

            migrationBuilder.CreateIndex(
                name: "ix_gift_cards_code",
                schema: "scheduling",
                table: "gift_cards",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gift_cards_establishment_id_created_at",
                schema: "scheduling",
                table: "gift_cards",
                columns: new[] { "establishment_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_gift_cards_holder_user_id",
                schema: "scheduling",
                table: "gift_cards",
                column: "holder_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_booking_requests_client_user_id",
                schema: "scheduling",
                table: "group_booking_requests",
                column: "client_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_booking_requests_establishment_id_status_created_at",
                schema: "scheduling",
                table: "group_booking_requests",
                columns: new[] { "establishment_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_programs_establishment_id",
                schema: "scheduling",
                table: "loyalty_programs",
                column: "establishment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_transactions_appointment_id",
                schema: "scheduling",
                table: "loyalty_transactions",
                column: "appointment_id",
                unique: true,
                filter: "kind = 'Earn' AND appointment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_transactions_client_user_id",
                schema: "scheduling",
                table: "loyalty_transactions",
                column: "client_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_transactions_establishment_id_client_user_id",
                schema: "scheduling",
                table: "loyalty_transactions",
                columns: new[] { "establishment_id", "client_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_client_user_id",
                schema: "scheduling",
                table: "waitlist_entries",
                column: "client_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_establishment_id_client_user_id_date",
                schema: "scheduling",
                table: "waitlist_entries",
                columns: new[] { "establishment_id", "client_user_id", "date" },
                unique: true,
                filter: "status = 'Waiting'");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_establishment_id_date_status",
                schema: "scheduling",
                table: "waitlist_entries",
                columns: new[] { "establishment_id", "date", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gift_card_redemptions",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "group_booking_requests",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "loyalty_programs",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "loyalty_transactions",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "waitlist_entries",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "gift_cards",
                schema: "scheduling");

            migrationBuilder.DropColumn(
                name: "reengagement_sent_at",
                schema: "scheduling",
                table: "appointments");
        }
    }
}
