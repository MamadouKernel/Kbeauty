using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.CreateTable(
                name: "establishments",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    customer_service_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    commune_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_line = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    location = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false, computedColumnSql: "ST_Y(location::geometry)", stored: true),
                    longitude = table.Column<double>(type: "double precision", nullable: false, computedColumnSql: "ST_X(location::geometry)", stored: true),
                    timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_featured = table.Column<bool>(type: "boolean", nullable: false),
                    booking_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_establishments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "opening_hours",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    opens_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    closes_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_opening_hours", x => x.id);
                    table.CheckConstraint("ck_opening_hours_range", "opens_at < closes_at");
                });

            migrationBuilder.CreateTable(
                name: "establishment_categories",
                schema: "catalog",
                columns: table => new
                {
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_establishment_categories", x => new { x.establishment_id, x.category_id });
                    table.ForeignKey(
                        name: "fk_establishment_categories_beauty_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "catalog",
                        principalTable: "beauty_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_establishment_categories_establishments_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "catalog",
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "establishment_stats_daily",
                schema: "catalog",
                columns: table => new
                {
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    views = table.Column<int>(type: "integer", nullable: false),
                    call_clicks = table.Column<int>(type: "integer", nullable: false),
                    directions_clicks = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_establishment_stats_daily", x => new { x.establishment_id, x.day });
                    table.ForeignKey(
                        name: "fk_establishment_stats_daily_establishments_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "catalog",
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "media",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    thumb_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    poster_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    alt_text = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_cover = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media", x => x.id);
                    table.ForeignKey(
                        name: "fk_media_establishments_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "catalog",
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_groups",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_groups_establishments_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "catalog",
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "services",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    price_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_bookable = table.Column<bool>(type: "boolean", nullable: false),
                    is_visible = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                    table.ForeignKey(
                        name: "fk_services_beauty_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "catalog",
                        principalTable: "beauty_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_services_establishments_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "catalog",
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_services_service_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "catalog",
                        principalTable: "service_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_establishment_categories_category_id",
                schema: "catalog",
                table: "establishment_categories",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_establishments_location",
                schema: "catalog",
                table: "establishments",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_establishments_name",
                schema: "catalog",
                table: "establishments",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_establishments_slug",
                schema: "catalog",
                table: "establishments",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_establishments_status_commune_id",
                schema: "catalog",
                table: "establishments",
                columns: new[] { "status", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_media_establishment_id_sort_order",
                schema: "catalog",
                table: "media",
                columns: new[] { "establishment_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_opening_hours_establishment_id_day_of_week",
                schema: "scheduling",
                table: "opening_hours",
                columns: new[] { "establishment_id", "day_of_week" });

            migrationBuilder.CreateIndex(
                name: "ix_service_groups_establishment_id",
                schema: "catalog",
                table: "service_groups",
                column: "establishment_id");

            migrationBuilder.CreateIndex(
                name: "ix_services_category_id",
                schema: "catalog",
                table: "services",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_services_establishment_id_sort_order",
                schema: "catalog",
                table: "services",
                columns: new[] { "establishment_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_services_group_id",
                schema: "catalog",
                table: "services",
                column: "group_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "establishment_categories",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "establishment_stats_daily",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "media",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "opening_hours",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "services",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "service_groups",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "establishments",
                schema: "catalog");
        }
    }
}
