#!/bin/sh
# Proves that a backup can actually be restored: dumps the live database, restores it into a throw-away database
# on the same server, compares row counts of every table, then drops the throw-away database.
# Usage: scripts/verify-restore.sh
set -eu

CHECK_DB="kekebeauty_restore_check"
PSQL="docker compose exec -T postgres psql -U keke -v ON_ERROR_STOP=1 -At"

$PSQL -d postgres -c "DROP DATABASE IF EXISTS $CHECK_DB" > /dev/null
$PSQL -d postgres -c "CREATE DATABASE $CHECK_DB" > /dev/null

docker compose exec -T postgres pg_dump -U keke -d kekebeauty --format=custom --no-owner \
  | docker compose exec -T postgres pg_restore -U keke -d "$CHECK_DB" --no-owner --exit-on-error

COUNTS_SQL="SELECT table_schema || '.' || table_name || '=' || (xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I', table_schema, table_name), false, true, '')))[1]::text
            FROM information_schema.tables
            WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema', 'tiger', 'tiger_data', 'topology')
            ORDER BY 1"
SOURCE="$($PSQL -d kekebeauty -c "$COUNTS_SQL")"
RESTORED="$($PSQL -d "$CHECK_DB" -c "$COUNTS_SQL")"
$PSQL -d postgres -c "DROP DATABASE $CHECK_DB" > /dev/null

if [ "$SOURCE" = "$RESTORED" ]; then
  echo "Restore verified: $(echo "$SOURCE" | wc -l) tables, identical row counts."
else
  echo "Restore MISMATCH between the live database and the restored copy:" >&2
  echo "$SOURCE" > /tmp/source-counts.txt
  echo "$RESTORED" > /tmp/restored-counts.txt
  diff /tmp/source-counts.txt /tmp/restored-counts.txt >&2 || true
  exit 1
fi
