using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KekeBeauty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // unaccent() is only STABLE, so no index can be built on it. An IMMUTABLE wrapper (safe: the dictionary is fixed) lets
            // accent-insensitive text searches use trigram indexes instead of scanning every row.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION public.keke_unaccent(text) RETURNS text
                    LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                    AS $$ SELECT public.unaccent('public.unaccent'::regdictionary, $1) $$;");
            migrationBuilder.Sql("CREATE INDEX ix_establishments_name_unaccent ON catalog.establishments USING gin (public.keke_unaccent(name) gin_trgm_ops);");
            migrationBuilder.Sql("CREATE INDEX ix_services_name_unaccent ON catalog.services USING gin (public.keke_unaccent(name) gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS catalog.ix_services_name_unaccent;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS catalog.ix_establishments_name_unaccent;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.keke_unaccent(text);");
        }
    }
}
