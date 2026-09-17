-- Bootstrap execute une seule fois par Postgres au premier demarrage du volume
-- (docker-entrypoint-initdb.d). Le schema metier n'est PAS ici : il est porte par
-- les migrations versionnees dans db/migrations/, appliquees via scripts/db/migrate.sh
-- (voir specs/001-infra-postgres/research.md, Decision 2).

CREATE EXTENSION IF NOT EXISTS "pgcrypto";
