#!/bin/sh
# Scans container images for HIGH and CRITICAL vulnerabilities with Trivy (run in a container, nothing to install).
# Usage: sh scripts/scan-images.sh                 scans every image of docker-compose.yml (observability profile included)
#        sh scripts/scan-images.sh image [image...]
# One line per image: operating system, totals, and how many have a fixed version available. Rescan on every image update and in CI.
set -u
cd "$(dirname "$0")/.."
if [ "$#" -eq 0 ]; then
  set -- $(docker compose --profile observability config --images | sort -u)
fi
for image in "$@"; do
  printf '%-46s ' "$image"
  timeout 600 docker run --rm -v /var/run/docker.sock:/var/run/docker.sock aquasec/trivy:latest image \
    --quiet --severity HIGH,CRITICAL --scanners vuln --format json "$image" 2>/dev/null | python3 -I -c '
import sys, json, collections
raw = sys.stdin.read(); start = raw.find("{")
if start < 0:
    print("scan impossible"); sys.exit()
data = json.loads(raw[start:]); results = data.get("Results", [])
found = collections.Counter(v["Severity"] for r in results for v in (r.get("Vulnerabilities") or []))
fixed = collections.Counter(v["Severity"] for r in results for v in (r.get("Vulnerabilities") or []) if v.get("FixedVersion"))
os_ = data.get("Metadata", {}).get("OS", {})
print(os_.get("Family"), os_.get("Name"), "| total:", dict(found) or "clean", "| fixable:", dict(fixed) or "none")'
done
