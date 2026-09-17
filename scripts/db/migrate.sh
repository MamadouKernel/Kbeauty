#!/usr/bin/env bash
#
# Applique les migrations SQL versionnees de db/migrations/ sur la base Postgres
# du conteneur kekebeauty-postgres, de facon idempotente et tracee.
#
# Convention de nommage des migrations : NNNN_description.sql, NNNN strictement croissant.
# Chaque migration appliquee est journalisee dans la table schema_migrations (version, checksum).
# Si une migration deja appliquee a ete modifiee sur disque (checksum different), le script
# echoue explicitement avant d'appliquer quoi que ce soit d'autre.
#
# Variables d'environnement lues (avec valeurs par defaut si .env absent) :
#   POSTGRES_USER, POSTGRES_DB, POSTGRES_CONTAINER
#
# Codes de sortie :
#   0 = toutes les migrations en attente ont ete appliquees avec succes
#   1 = le conteneur Postgres n'est pas pret / injoignable
#   2 = checksum invalide detecte (migration deja appliquee modifiee sur disque)
#   3 = echec d'application d'une migration (erreur SQL)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
MIGRATIONS_DIR="$PROJECT_ROOT/db/migrations"

if [ -f "$PROJECT_ROOT/.env" ]; then
  set -o allexport
  # shellcheck disable=SC1090
  source "$PROJECT_ROOT/.env"
  set +o allexport
fi

POSTGRES_USER="${POSTGRES_USER:-kekebeauty}"
POSTGRES_DB="${POSTGRES_DB:-kekebeauty}"
POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-kekebeauty-postgres}"

psql_exec() {
  docker exec -i "$POSTGRES_CONTAINER" psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" "$@"
}

echo "==> Attente de la disponibilite de Postgres ($POSTGRES_CONTAINER)..."
for _ in $(seq 1 30); do
  if docker exec "$POSTGRES_CONTAINER" pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
if ! docker exec "$POSTGRES_CONTAINER" pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB" >/dev/null 2>&1; then
  echo "ERREUR: Postgres n'est pas pret apres 30s (conteneur $POSTGRES_CONTAINER)." >&2
  exit 1
fi

echo "==> Creation de la table de suivi schema_migrations (si absente)..."
psql_exec -c "
CREATE TABLE IF NOT EXISTS schema_migrations (
    version VARCHAR(20) PRIMARY KEY,
    description TEXT NOT NULL,
    checksum VARCHAR(64) NOT NULL,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
" >/dev/null

if [ ! -d "$MIGRATIONS_DIR" ]; then
  echo "ERREUR: dossier de migrations introuvable: $MIGRATIONS_DIR" >&2
  exit 1
fi

shopt -s nullglob
migration_files=("$MIGRATIONS_DIR"/*.sql)
shopt -u nullglob

if [ ${#migration_files[@]} -eq 0 ]; then
  echo "==> Aucune migration trouvee dans $MIGRATIONS_DIR."
  exit 0
fi

IFS=$'\n' migration_files=($(sort <<<"${migration_files[*]}")); unset IFS

for file in "${migration_files[@]}"; do
  filename="$(basename "$file")"
  version="${filename%%_*}"
  description="${filename#*_}"
  checksum="$(sha256sum "$file" | awk '{print $1}')"

  existing_checksum="$(psql_exec -tA -c "SELECT checksum FROM schema_migrations WHERE version = '$version';" || true)"
  existing_checksum="$(echo "$existing_checksum" | tr -d '[:space:]')"

  if [ -n "$existing_checksum" ]; then
    if [ "$existing_checksum" != "$checksum" ]; then
      echo "ERREUR: la migration '$filename' (version $version) a deja ete appliquee avec un" >&2
      echo "        checksum different. Une migration appliquee ne doit jamais etre modifiee." >&2
      echo "        Creez une nouvelle migration corrective a la place." >&2
      exit 2
    fi
    echo "==> $filename deja appliquee (checksum identique), ignoree."
    continue
  fi

  echo "==> Application de $filename..."
  if ! psql_exec < "$file"; then
    echo "ERREUR: echec de l'application de $filename." >&2
    exit 3
  fi

  psql_exec -c "
    INSERT INTO schema_migrations (version, description, checksum)
    VALUES ('$version', '$description', '$checksum');
  " >/dev/null

  echo "==> $filename appliquee avec succes."
done

echo "==> Toutes les migrations sont a jour."
