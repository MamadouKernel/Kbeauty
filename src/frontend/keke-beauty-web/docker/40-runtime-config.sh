#!/bin/sh
# Writes /config.json from environment variables when the web container starts (nginx runs
# /docker-entrypoint.d/*.sh before the server). Lets one image serve any environment.
set -eu

escape() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

# nginx needs an explicit resolver to look the API up at request time (see nginx.conf).
nameserver=$(awk '/^nameserver/ { print $2; exit }' /etc/resolv.conf)
printf 'resolver %s valid=10s ipv6=off;\n' "${nameserver:-127.0.0.11}" > /etc/nginx/resolver.inc

yango=$(escape "${YANGO_URL_TEMPLATE:-}")

# Map tiles: an https template ({z}/{x}/{y}, optional {s}). Anything else falls back to OpenStreetMap in the app.
# The tile host is also allowed in the Content-Security-Policy (img-src) through $csp_map_tiles.
tiles_url="${MAP_TILES_URL:-}"
tiles_host=$(printf '%s' "$tiles_url" | sed -n 's#^https://\([A-Za-z0-9.{}*-]*\)/.*#\1#p' | sed 's/{s}/*/g')
if [ -z "$tiles_host" ]; then
  # No provider configured: the site's own cached tile proxy (nginx.conf, /tiles/), served from the same origin.
  tiles_url="/tiles/{z}/{x}/{y}.png"
  tiles_host="tile.openstreetmap.org"
fi
printf 'set $csp_map_tiles "https://%s";\n' "$tiles_host" > /etc/nginx/map-tiles.inc

# Google sign-in loads Google's own script, frame and styles: the browser may fetch them only when the platform turned it on, and only from the
# exact addresses Google documents. Disabled (the default), the policy stays "self" only and nothing outside the site can load.
if [ "${GOOGLE_AUTH_ENABLED:-false}" = "true" ]; then
  {
    printf 'set $csp_google_script "https://accounts.google.com/gsi/client";\n'
    printf 'set $csp_google_style "https://accounts.google.com/gsi/style";\n'
    printf 'set $csp_google_frame "https://accounts.google.com/gsi/";\n'
    printf 'set $csp_google_connect "https://accounts.google.com/gsi/";\n'
  } > /etc/nginx/google-csp.inc
else
  {
    printf 'set $csp_google_script "";\n'
    printf 'set $csp_google_style "";\n'
    printf 'set $csp_google_frame "";\n'
    printf 'set $csp_google_connect "";\n'
  } > /etc/nginx/google-csp.inc
fi

# Trusted reverse proxies (comma separated addresses or CIDRs). Only a connection from one of them may say who the real client is; anyone else
# sending X-Forwarded-For is ignored. Validated: a stray character would otherwise end up in the nginx configuration.
{
  if [ -n "${TRUSTED_PROXIES:-}" ]; then
    echo "$TRUSTED_PROXIES" | tr ',' '\n' | while read -r proxy; do
      proxy=$(printf '%s' "$proxy" | tr -d ' ')
      [ -z "$proxy" ] && continue
      if printf '%s' "$proxy" | grep -Eq '^[0-9a-fA-F:.]+(/[0-9]{1,3})?$'; then
        printf 'set_real_ip_from %s;\n' "$proxy"
      else
        echo "TRUSTED_PROXIES: ignored invalid entry" >&2
      fi
    done
    printf 'real_ip_header X-Forwarded-For;\nreal_ip_recursive on;\n'
  else
    printf '# TRUSTED_PROXIES is empty: the connecting peer is the client.\n'
  fi
} > /etc/nginx/real-ip.inc

tiles=$(escape "$tiles_url")
attribution=$(escape "${MAP_TILES_ATTRIBUTION:-}")
printf '{ "yangoUrlTemplate": "%s", "mapTilesUrl": "%s", "mapAttribution": "%s" }\n' "$yango" "$tiles" "$attribution" > /usr/share/nginx/html/config.json
