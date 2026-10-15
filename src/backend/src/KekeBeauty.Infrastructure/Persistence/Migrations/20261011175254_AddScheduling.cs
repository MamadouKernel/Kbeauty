using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.AddColumn<bool>(
                name: "auto_confirm",
                schema: "catalog",
                table: "establishments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "cancel_limit_minutes",
                schema: "catalog",
                table: "establishments",
                type: "integer",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AddColumn<int>(
                name: "max_horizon_days",
                schema: "catalog",
                table: "establishments",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "min_lead_minutes",
                schema: "catalog",
                table: "establishments",
                type: "integer",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AddColumn<int>(
                name: "slot_step_minutes",
                schema: "catalog",
                table: "establishments",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    template_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "resources",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "appointments",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    total_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    client_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    client_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    proposed_starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    proposed_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointments", x => x.id);
                    table.CheckConstraint("ck_appointments_range", "starts_at < ends_at");
                    table.ForeignKey(
                        name: "fk_appointments_resources_resource_id",
                        column: x => x.resource_id,
                        principalSchema: "scheduling",
                        principalTable: "resources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "resource_services",
                schema: "scheduling",
                columns: table => new
                {
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resource_services", x => new { x.resource_id, x.service_id });
                    table.ForeignKey(
                        name: "fk_resource_services_resources_resource_id",
                        column: x => x.resource_id,
                        principalSchema: "scheduling",
                        principalTable: "resources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "time_off",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_off", x => x.id);
                    table.CheckConstraint("ck_time_off_range", "starts_at < ends_at");
                    table.ForeignKey(
                        name: "fk_time_off_resources_resource_id",
                        column: x => x.resource_id,
                        principalSchema: "scheduling",
                        principalTable: "resources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "appointment_services",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    price_snapshot = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    duration_snapshot = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_services", x => x.id);
                    table.ForeignKey(
                        name: "fk_appointment_services_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalSchema: "scheduling",
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "appointment_status_history",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_appointment_status_history_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalSchema: "scheduling",
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointment_services_appointment_id",
                schema: "scheduling",
                table: "appointment_services",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_status_history_appointment_id_at",
                schema: "scheduling",
                table: "appointment_status_history",
                columns: new[] { "appointment_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_client_user_id_starts_at",
                schema: "scheduling",
                table: "appointments",
                columns: new[] { "client_user_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_establishment_id_starts_at",
                schema: "scheduling",
                table: "appointments",
                columns: new[] { "establishment_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_resource_id",
                schema: "scheduling",
                table: "appointments",
                column: "resource_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_status_expires_at",
                schema: "scheduling",
                table: "appointments",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_channel_created_at",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "user_id", "channel", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_resources_establishment_id",
                schema: "scheduling",
                table: "resources",
                column: "establishment_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_off_establishment_id_starts_at",
                schema: "scheduling",
                table: "time_off",
                columns: new[] { "establishment_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_time_off_resource_id",
                schema: "scheduling",
                table: "time_off",
                column: "resource_id");

            // PRD 8.3: no resource can hold two active appointments over overlapping periods. A violation surfaces as
            // PostgreSQL error 23P01, which the API turns into a 409 ("Ce creneau vient d etre reserve").
            migrationBuilder.Sql(
                """
                ALTER TABLE scheduling.appointments
                    ADD CONSTRAINT no_overlapping_appointments
                    EXCLUDE USING gist (resource_id WITH =, tstzrange(starts_at, ends_at) WITH &&)
                    WHERE (status IN ('Pending', 'Confirmed', 'RescheduleProposed'));
                """);

            // Establishments already published before this lot get their default resource (PRD AGD-05).
            migrationBuilder.Sql(
                """
                INSERT INTO scheduling.resources (id, establishment_id, name, is_active, created_at)
                SELECT gen_random_uuid(), e.id, 'Principal', true, now()
                FROM catalog.establishments e
                WHERE NOT EXISTS (SELECT 1 FROM scheduling.resources r WHERE r.establishment_id = e.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointment_services",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "appointment_status_history",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "resource_services",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "time_off",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "appointments",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "resources",
                schema: "scheduling");

            migrationBuilder.DropColumn(
                name: "auto_confirm",
                schema: "catalog",
                table: "establishments");

            migrationBuilder.DropColumn(
                name: "cancel_limit_minutes",
                schema: "catalog",
                table: "establishments");

            migrationBuilder.DropColumn(
                name: "max_horizon_days",
                schema: "catalog",
                table: "establishments");

            migrationBuilder.DropColumn(
                name: "min_lead_minutes",
                schema: "catalog",
                table: "establishments");

            migrationBuilder.DropColumn(
                name: "slot_step_minutes",
                schema: "catalog",
                table: "establishments");
        }
    }
}
