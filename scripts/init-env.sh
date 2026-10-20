#!/bin/sh
# Creates .env from .env.example when missing, then fills the secrets that are still empty with random values.
# Nothing secret belongs in the repository (it is public): run this once on each machine, never commit .env.
# Usage: sh scripts/init-env.sh        (safe to run again: only empty values are filled, existing ones are kept)
set -eu
cd "$(dirname "$0")/.."

[ -f .env ] || { cp .env.example .env; chmod 600 .env; echo "Created .env from .env.example"; }

random() { # $1 = number of random bytes; URL-safe, no characters that need quoting in .env or a connection string
  if command -v openssl >/dev/null 2>&1; then openssl rand -base64 "$1" | tr -d '=\n+/' ; else head -c "$1" /dev/urandom | base64 | tr -d '=\n+/'; fi
}

fill() { # $1 = variable, $2 = value
  if grep -q "^$1=" .env; then
    if [ -z "$(grep "^$1=" .env | head -1 | cut -d= -f2-)" ]; then
      sed -i "s|^$1=.*|$1=$2|" .env && echo "Filled $1"
    fi
  else
    printf '%s=%s\n' "$1" "$2" >> .env && echo "Added $1"
  fi
}

fill POSTGRES_PASSWORD "$(random 24)"
fill MINIO_ROOT_USER "keke$(random 6)"
fill MINIO_ROOT_PASSWORD "$(random 24)"
fill JWT_SIGNING_KEY "$(random 48)"
chmod 600 .env
echo "Done. Values are in .env (ignored by git)."
