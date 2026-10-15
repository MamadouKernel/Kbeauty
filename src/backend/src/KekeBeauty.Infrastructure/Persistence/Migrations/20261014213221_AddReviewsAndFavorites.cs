using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewsAndFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "favorites",
                schema: "catalog",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_favorites", x => new { x.user_id, x.establishment_id });
                    table.ForeignKey(
                        name: "fk_favorites_establishments_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "catalog",
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    hidden_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    reply_text = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    replied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviews", x => x.id);
                    table.CheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_reviews_establishments_establishment_id",
                        column: x => x.establishment_id,
                        principalSchema: "catalog",
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_favorites_establishment_id",
                schema: "catalog",
                table: "favorites",
                column: "establishment_id");

            migrationBuilder.CreateIndex(
                name: "ix_favorites_user_id_created_at",
                schema: "catalog",
                table: "favorites",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_appointment_id",
                schema: "catalog",
                table: "reviews",
                column: "appointment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reviews_establishment_id_status_created_at",
                schema: "catalog",
                table: "reviews",
                columns: new[] { "establishment_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_user_id",
                schema: "catalog",
                table: "reviews",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "favorites",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "reviews",
                schema: "catalog");
        }
    }
}
