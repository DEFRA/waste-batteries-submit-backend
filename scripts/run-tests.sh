#!/usr/bin/env bash
# Runs both test assemblies. `dotnet test` uses the testing-platform server
# and currently reports that zero tests ran, so the pull request check launches
# the built assemblies directly. Build Debug net10.0 before running this.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unit="${root}/tests/WasteBatteriesSubmitBackend.Tests.Unit/bin/Debug/net10.0/WasteBatteriesSubmitBackend.Tests.Unit.dll"
integration="${root}/tests/WasteBatteriesSubmitBackend.Tests.Integration/bin/Debug/net10.0/WasteBatteriesSubmitBackend.Tests.Integration.dll"

if [[ ! -f "${unit}" || ! -f "${integration}" ]]; then
  echo "Test assemblies were not found. Build Debug net10.0 before running this script." >&2
  exit 1
fi

dotnet "${unit}"
dotnet "${integration}"
