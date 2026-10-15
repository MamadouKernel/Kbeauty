#!/bin/sh
# Logical backup of the Keke Beauty database (custom format, compressed) from the compose postgres service.
# Usage: scripts/backup-postgres.sh [output-directory]    (default: ./backups)
# Production: run it from cron or the platform scheduler AND enable WAL archiving for point-in-time recovery (docs/DEPLOYMENT.md section 4).
set -eu

OUT_DIR="${1:-./backups}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
FILE="$OUT_DIR/kekebeauty-$STAMP.dump"
mkdir -p "$OUT_DIR"

docker compose exec -T postgres pg_dump -U keke -d kekebeauty --format=custom --no-owner > "$FILE"
# A dump that cannot be listed is not a backup.
docker compose exec -T postgres pg_restore --list < "$FILE" > /dev/null
echo "Backup written: $FILE ($(wc -c < "$FILE") bytes)"
