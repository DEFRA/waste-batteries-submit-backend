#!/usr/bin/env bash
# Export ZAP HTML/JSON reports and fail if any High alert was raised against
# this API (default port 8085). Empty traffic is also a failure.
# Passive alerts are raised on a background queue, so this waits until
# recordsToScan is 0 before writing the report or applying the High gate.
set -euo pipefail

API="${ZAP_PROXY_API_URL:-http://127.0.0.1:8080}"
API="${API%/}"
PORT="${ZAP_APP_PORT:-8085}"
REPORTS_DIR="${ZAP_REPORTS_DIR:-zap-reports}"

deadline=$((SECONDS + 60))
remaining=-1
while (( SECONDS < deadline )); do
  remaining="$(
    curl -fsS "${API}/JSON/pscan/view/recordsToScan/" |
      python3 -c 'import json, sys
body=json.load(sys.stdin)
value=body.get("recordsToScan")
if value is None:
    raise SystemExit("ZAP recordsToScan was missing: " + json.dumps(body))
print(int(value))'
  )"
  if [[ "${remaining}" == "0" ]]; then
    break
  fi
  sleep 1
done

if [[ "${remaining}" != "0" ]]; then
  echo "ZAP passive scan still has ${remaining} record(s) to scan after 60s." >&2
  exit 1
fi

mkdir -p "${REPORTS_DIR}"
curl -fsS "${API}/OTHER/core/other/htmlreport/" -o "${REPORTS_DIR}/zap-report.html"
curl -fsS "${API}/OTHER/core/other/jsonreport/" -o "${REPORTS_DIR}/zap-report.json"

python3 - "${API}" "${PORT}" <<'PY'
import json
import sys
import urllib.parse
import urllib.request
from urllib.parse import urlparse

api, port = sys.argv[1], int(sys.argv[2])


def get(path):
    with urllib.request.urlopen(api + path) as response:
        return json.load(response)


sites = get("/JSON/core/view/sites/").get("sites") or []
app_sites = []
for site in sites:
    try:
        if urlparse(site).port == port:
            app_sites.append(site)
    except ValueError:
        continue

if not app_sites:
    recorded = ", ".join(sites) if sites else "(none)"
    raise SystemExit(
        f"ZAP saw no traffic on port {port}. Sites recorded: {recorded}. "
        "curl must use -x http://127.0.0.1:8080 so the request is proxied."
    )

failed = False
for site in app_sites:
    encoded = urllib.parse.quote(site, safe="")
    data = get(f"/JSON/alert/view/alertsSummary/?baseurl={encoded}")
    summary = data.get("alertsSummary") or data
    high = int(summary.get("High") or 0)
    print(
        f"{site} High={high} Medium={summary.get('Medium')} "
        f"Low={summary.get('Low')} Informational={summary.get('Informational')}"
    )
    if high:
        print(
            f"{site} has {high} High alert(s). See zap-reports/zap-report.html "
            "- fix the app rather than weakening this gate.",
            file=sys.stderr,
        )
        failed = True

sys.exit(1 if failed else 0)
PY
