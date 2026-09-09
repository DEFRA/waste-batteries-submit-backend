#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

if [[ -z "${SONAR_TOKEN:-}" ]]; then
  echo "SONAR_TOKEN is required. Run: export SONAR_TOKEN=your-token" >&2
  exit 1
fi

if ! command -v java >/dev/null 2>&1; then
  echo "Java is required. Install it with: brew install --cask temurin@17" >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is required but was not found on PATH." >&2
  exit 1
fi

if ! command -v curl >/dev/null 2>&1; then
  echo "curl is required but was not found on PATH." >&2
  exit 1
fi

project_key="DEFRA_waste-batteries-submit-backend"
branch_name="${SONAR_BRANCH:-$(git branch --show-current 2>/dev/null || true)}"
pull_request="${SONAR_PULL_REQUEST:-}"
pull_request_base="${SONAR_PULL_REQUEST_BASE:-main}"
issues_json="sonar-issues.json"
issues_markdown="sonar-issues.md"
scanner_log=".sonar/scanner-output.log"
analysis_context="$branch_name"

mkdir -p ./.sonar/scanner ./.sonar/coverage

dotnet tool update dotnet-sonarscanner --tool-path ./.sonar/scanner
dotnet tool update dotnet-coverage --tool-path ./.sonar/coverage

sonar_begin_args=(
  begin
  /k:"$project_key"
  /o:"defra"
  /d:sonar.token="$SONAR_TOKEN"
  /d:sonar.host.url="https://sonarcloud.io"
  /d:sonar.cs.vscoveragexml.reportsPaths=coverage.xml
)

if [[ -n "$pull_request" ]]; then
  sonar_begin_args+=(
    /d:sonar.pullrequest.key="$pull_request"
    /d:sonar.pullrequest.branch="$branch_name"
    /d:sonar.pullrequest.base="$pull_request_base"
  )
  analysis_context="PR $pull_request ($branch_name -> $pull_request_base)"
elif [[ -n "$branch_name" ]]; then
  sonar_begin_args+=(/d:sonar.branch.name="$branch_name")
fi

./.sonar/scanner/dotnet-sonarscanner "${sonar_begin_args[@]}"

dotnet build --no-incremental

./.sonar/coverage/dotnet-coverage collect "dotnet test" \
  -f xml \
  -o "coverage.xml"

./.sonar/scanner/dotnet-sonarscanner end \
  /d:sonar.token="$SONAR_TOKEN" \
  | tee "$scanner_log"

if command -v python3 >/dev/null 2>&1; then
  ce_task_id="$(
    python3 - "$scanner_log" <<'PY'
import re
import sys

with open(sys.argv[1], encoding="utf-8") as file:
    matches = re.findall(r"api/ce/task\?id=([A-Za-z0-9_-]+)", file.read())

print(matches[-1] if matches else "")
PY
  )"

  if [[ -n "$ce_task_id" ]]; then
    echo "Waiting for SonarCloud to process analysis..."

    for _ in {1..30}; do
      task_json="$(
        curl --fail --silent --show-error \
          --header "Authorization: Bearer ${SONAR_TOKEN}" \
          "https://sonarcloud.io/api/ce/task?id=${ce_task_id}"
      )"
      task_status="$(
        python3 -c 'import json, sys; print(json.load(sys.stdin)["task"]["status"])' <<<"$task_json"
      )"

      case "$task_status" in
        SUCCESS)
          break
          ;;
        FAILED|CANCELED)
          echo "SonarCloud processing finished with status: $task_status" >&2
          exit 1
          ;;
        *)
          echo "SonarCloud processing status: $task_status"
          ;;
      esac

      sleep 2
    done
  fi
fi

issues_url="https://sonarcloud.io/api/issues/search?componentKeys=${project_key}&resolved=false&ps=500"

if [[ -n "$pull_request" ]]; then
  issues_url="${issues_url}&pullRequest=${pull_request}"
elif [[ -n "$branch_name" ]]; then
  issues_url="${issues_url}&branch=${branch_name}"
fi

curl --fail --silent --show-error \
  --header "Authorization: Bearer ${SONAR_TOKEN}" \
  "$issues_url" \
  --output "$issues_json"

if command -v python3 >/dev/null 2>&1; then
  python3 - "$issues_json" "$issues_markdown" "$project_key" "$analysis_context" <<'PY'
import json
import sys

issues_json, issues_markdown, project_key, analysis_context = sys.argv[1:5]

with open(issues_json, encoding="utf-8") as file:
    payload = json.load(file)

issues = payload.get("issues", [])
heading = f"# SonarCloud issues for {project_key}"
if analysis_context:
    heading += f" ({analysis_context})"

lines = [
    heading,
    "",
    f"Total unresolved issues exported: {payload.get('total', len(issues))}",
    "",
]

if not issues:
    lines.append("No unresolved issues found.")
else:
    for issue in issues:
        component = issue.get("component", "")
        path = component.split(":", 1)[-1]
        line = issue.get("line")
        location = f"{path}:{line}" if line else path
        lines.extend(
            [
                f"## {issue.get('severity', 'UNKNOWN')} {issue.get('type', 'ISSUE')}",
                "",
                f"- File: `{location}`",
                f"- Rule: `{issue.get('rule', 'unknown')}`",
                f"- Message: {issue.get('message', '')}",
                f"- Key: `{issue.get('key', '')}`",
                "",
            ]
        )

with open(issues_markdown, "w", encoding="utf-8") as file:
    file.write("\n".join(lines))
    file.write("\n")
PY

  echo "Wrote $issues_json and $issues_markdown"
else
  echo "Wrote $issues_json"
  echo "Install python3 if you also want a markdown summary."
fi
