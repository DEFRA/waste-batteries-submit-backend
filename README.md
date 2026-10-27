# waste-batteries-submit-backend

API for battery compliance scheme placed-on-market (POM) submissions, built on the core delivery C# ASP.NET backend template.

The target data model is the Compliance Scheme POM Submission page in the [waste-batteries-docs](https://github.com/DEFRA/waste-batteries-docs/blob/main/technology/data-architecture/compliance-scheme-pom-submission/README.md) repository. This page describes what this service implements.

* [Placed-on-market submissions](#placed-on-market-submissions)
* [Install MongoDB](#install-mongodb)
* [Inspect MongoDB](#inspect-mongodb)
* [Testing](#testing)
* [Running](#running)
* [Dependabot](#dependabot)


### Placed-on-market submissions

`POST /pom-submissions` creates one first submission for one scheme member and one quarter. The caller needs a Defra ID bearer token. The response is `201 Created`, with `Location: /pom-submissions/{id}` and the stored submission as the body. Documents are stored in the `pom-submissions` collection.

`GET /pom-submissions/{id}` returns that stored submission as `200 OK` and the same JSON object. `id` must be an ObjectId. A value that is not an ObjectId is a `400` validation problem. A well-formed id that is not stored is `404`. The read is by id, so a submission that is no longer current is still returned.

`GET /pom-submissions` returns a page of those same submission objects. A single submission is read with `GET /pom-submissions/{id}`. List filters are combined with AND: `schemeOrgId`, `memberOrgId`, repeated `compliancePeriodYears`, repeated `compliancePeriodQuarters`, and `isCurrent`. At least one of `schemeOrgId`, `memberOrgId`, `compliancePeriodYears` or `compliancePeriodQuarters` is required. `isCurrent` does not satisfy that rule and defaults to true. A year list or a quarter list matches any of the supplied values. Omit `pageSize`, or set it higher than the limit for that request, and `pageSize` in the response is that limit. A caller that accepts gzip uses `PomSubmission:MaxGzipPageSize`. Any other caller uses `PomSubmission:MaxPageSize`. `pageNumber` then defaults to 1. A smaller `pageSize` is kept. A page past the end returns the last page. The body has `items`, `pageNumber`, `pageSize`, `totalPages`, `totalCount`, `hasPreviousPage` and `hasNextPage`. No match is `200` with an empty `items` array. A missing filter, or a bad organisation id, year, quarter or page, is a `400` validation problem. The same bearer token is required. A GET that sends `Accept-Encoding: gzip` receives a gzip body.

`PomSubmission:MaxPageSize` is 750. That is the page size when the caller does not accept gzip. `PomSubmission:MaxGzipPageSize` is 17500. That is the page size when `Accept-Encoding` includes `gzip` or `*` with a quality above 0. `gzip;q=0` does not accept gzip, so the smaller limit applies and the body is plain JSON. A caller that does not accept gzip still receives `200` and uncompressed JSON. The response does not fail.

Both numbers were calculated for the current `PomSubmissionModel` shape, including its 80 tonnage entries. If that class changes, measure the JSON and the gzip again and update both settings. Until that is done, the limits are for the old shape and a page can pass the 10 MiB CDP payload limit (10,485,760 bytes).

The measured submission has a 160-character member name, a 40-character scheme membership reference, BPRN `BPRN-NIEA-2026-000123`, data basis `Estimated`, tonnes `99999.999`, and chemistry `Nickel-metal hydride` with a two-digit suffix on every entry. One of those objects is 13,578 bytes. As uncompressed JSON, 772 of them stay under 10 MiB and 773 do not. 750 is 772 rounded down. The shorter example fields are about 12,207 bytes each and would fit 858 submissions. That larger count is not the plain-JSON limit, because long member names and long chemistry text would then pass 10 MiB.

The gzip limit uses that same long submission, with each submission different (member, tonnes, and ids). Identical copies compress far more than a real list, so they are not the measure. Gzip is `Optimal`, which is what the API uses. 17,696 of those submissions gzip to 10,485,286 bytes. 17,697 goes over 10 MiB. 17500 is 17,696 rounded down.

The caller posts `schemeOrgId`, `memberOrgId`, `memberName`, `memberBprn`, `schemeMembershipRef`, `compliancePeriodYear`, `compliancePeriodQuarter`, `dataBasis` and at least one `tonnageEntries` item. `schemeOrgId` and `memberOrgId` are ObjectIds. `memberBprn` matches `BPRN-{EA|NRW|SEPA|NIEA}-{year}-{6 digits}`, and that year equals `compliancePeriodYear`. `compliancePeriodYear` is a four-digit year on the request. Each entry posts `chemistry` and `tonnes`. `tonnes` is a string with exactly three digits after the decimal point, stored as `Decimal128` and returned as that string. `batteryCategory` is required on the stored classification. The request must not include it, and posting it is a bad request. The service sets it to `Portable` for every submission. `subCategory`, `cellType` and `equipmentState` are not on the classification. Posting one of them, or any other property the request model does not declare, is a bad request and is logged as an error.

```json
{
  "schemeOrgId": "674a1b2c3d4e5f6789012345",
  "memberOrgId": "674a1b2c3d4e5f6789012346",
  "memberName": "Northwind Batteries Ltd",
  "memberBprn": "BPRN-EA-2026-000123",
  "schemeMembershipRef": "SCH-00417",
  "compliancePeriodYear": 2026,
  "compliancePeriodQuarter": "Quarter 2",
  "dataBasis": "Actual",
  "tonnageEntries": [
    {
      "classification": {
        "chemistry": "Alkaline"
      },
      "tonnes": "12.500"
    }
  ]
}
```

The service sets `id`, `schemaVersion`, `version`, `status`, `isCurrent`, `submittedOnUtc`, `createdAtUtc`, `updatedAtUtc`, `supersedesSubmissionId`, each entry's `id` and `activity`, and `batteryCategory` (`Portable`). A first submission returns `schemaVersion` `2` and `version` `1`. `schemaVersion` is a constant. `2` is the classification that stores `batteryCategory` and `chemistry` only. `version`, `status` and `activity` come from the `PomSubmission` configuration, and the defaults are `1`, `Submitted` and `Placed`. `submittedOnUtc` equals `createdAtUtc` when the status is `Submitted`. `supersedesSubmissionId` and `updatedAtUtc` are null. Both `schemaVersion` and `version` are on the response. `schemaVersion` says which vocabulary the stored texts use. `version` is the concurrency token a later write sends back. Create does not check it. The declaration is omitted.

A repeated classification is rejected. The comparison is `batteryCategory` and `chemistry`. Activity is not part of that key. A second submission is rejected while a current submission already exists for the same scheme, member, year and quarter. That lookup runs when those key fields are well formed, and it reads the collection once. Field failures, including that duplicate, are `400` validation problems and are logged at information.

The unique index is `schemeOrgId`, `memberOrgId`, `compliancePeriodYear` and `compliancePeriodQuarter`, partial on `isCurrent: true`. A submission with `isCurrent` false can share that key. The other indexes are not unique: scheme plus year plus quarter, member plus year where `isCurrent` is true, `memberBprn`, chemistry plus battery category on the entries, and a sparse index on `supersedesSubmissionId`.

Not built yet: amendments, the declaration, checking quarter, chemistry and activity against the closed vocabularies, resolving the member from the BPRN, inferring the compliance year, and checking `version` on write. The mapper can already set `supersedesSubmissionId` and a supplied `version`. The create path does not.


### Docker Compose

A Docker Compose template is in [compose.yml](compose.yml).

A local environment with:

- Localstack for AWS services (S3, SQS)
- Redis
- MongoDB
- This service.
- A commented out frontend example.

```bash
docker compose up --build -d
```

A more extensive setup is available in [github.com/DEFRA/cdp-local-environment](https://github.com/DEFRA/cdp-local-environment)

### MongoDB

#### MongoDB via Docker

See above.

```
docker compose up -d mongodb
```

#### MongoDB locally

Alternatively install MongoDB locally:

- Install [MongoDB](https://www.mongodb.com/docs/manual/tutorial/#installation) on your local machine
- Start MongoDB:
```bash
sudo mongod --dbpath ~/mongodb-cdp
```

#### MongoDB in CDP environments

In CDP environments a MongoDB instance is already set up
and the credentials exposed as enviromment variables.


### Inspect MongoDB

To inspect the Database and Collections locally:
```bash
mongosh
```

You can use the CDP Terminal to access the environments' MongoDB.

### Testing

Build the Debug assemblies, then run them directly:

```bash
dotnet build --configuration Debug
bash scripts/run-tests.sh
```

`dotnet test` uses the testing-platform server and currently reports that zero tests ran. The pull request workflow builds Debug and runs `scripts/run-tests.sh`. On PowerShell, run the two assemblies the script launches:

```powershell
dotnet tests/WasteBatteriesSubmitBackend.Tests.Unit/bin/Debug/net10.0/WasteBatteriesSubmitBackend.Tests.Unit.dll
dotnet tests/WasteBatteriesSubmitBackend.Tests.Integration/bin/Debug/net10.0/WasteBatteriesSubmitBackend.Tests.Integration.dll
```

`WasteBatteriesSubmitBackend.Tests.Unit` covers validators, mapping, the submission service and handlers without hosting the API.

`WasteBatteriesSubmitBackend.Tests.Integration` hosts the API in process with `WebApplicationFactory`. The placed-on-market submission tests start MongoDB 7 with Testcontainers, so Docker must be running.

### Running

Run CDP-Deployments application:
```bash
dotnet run --project src/WasteBatteriesSubmitBackend --launch-profile Development
```

The backend requires a Defra ID access token in the `Authorization: Bearer <token>`
header for every endpoint except `/health`, `/openapi/v1.json`, and `/swagger`.
The token is validated against the configured OIDC metadata and audience.

Outside Production, Swagger UI is available at `/swagger` and the OpenAPI
document is available at `/openapi/v1.json`. Set `Swagger:Enabled` to `true`
to turn the explorer on in Production.

Local development uses the Defra ID stub values in `appsettings.Development.json`.
Deployed environments must provide `Jwt__MetadataAddress` and `Jwt__Audience`
configuration for the real Defra ID/API audience.

### SonarCloud

SonarCloud analysis runs from the pull request, publish and publish-hotfix GitHub Action workflows.

To run the same scan locally:

```bash
SONAR_TOKEN=your-token ./sonarCloudLocal.sh
```

The script writes unresolved issues to `sonar-issues.json` and, when `python3`
is available, a copy/paste friendly `sonar-issues.md`.

To match the SonarCloud pull request summary view, pass the pull request key:

```bash
SONAR_TOKEN=your-token SONAR_PULL_REQUEST=1 ./sonarCloudLocal.sh
```

### Security scanning (ZAP)

Every pull request runs a **passive** [OWASP ZAP](https://www.zaproxy.org/)
scan as a `zap` job. The compose API is started from
[compose-github.override-zap.yml](./compose-github.override-zap.yml) and
`/health` is requested through the ZAP proxy so the daemon inspects the
response. There is no Playwright suite in this repo.

The pull request fails if ZAP reports any **High** alerts against this API
(port 8085). The assert script waits until ZAP's passive scan queue is empty
before it reads those alerts. Medium and Low findings stay in the report;
they do not fail the check. An empty scan (ZAP recorded no traffic on 8085)
also fails.

The HTML and JSON reports are uploaded as the `zap-test-report` artefact and
linked from a comment on the pull request.

ZAP is CI/test-only — it does not change app configuration.

To run the same scan locally:

```bash
docker compose -f compose.yml -f compose-github.override-zap.yml \
  up -d --wait floci mongodb your-backend
docker compose -f compose.yml -f compose-github.override-zap.yml up -d zap
# wait until http://127.0.0.1:8080 and http://localhost:8085/health answer
curl -fsS -x http://127.0.0.1:8080 http://localhost:8085/health
bash scripts/zap-assert.sh
docker compose -f compose.yml -f compose-github.override-zap.yml down
```

On PowerShell, `curl` is an alias for `Invoke-WebRequest` and does not accept
`-x`. After the same `docker compose` commands above, use `curl.exe` for the
proxied health check and `scripts/zap-assert.ps1` in place of the bash script.
Always `down` the overlay stack afterwards, including when the scan fails.

```powershell
curl.exe -fsS -x http://127.0.0.1:8080 http://localhost:8085/health
.\scripts\zap-assert.ps1
docker compose -f compose.yml -f compose-github.override-zap.yml down
```

### Dependabot

We have added an example dependabot configuration file to the repository. You can enable it by renaming
the [.github/example.dependabot.yml](.github/example.dependabot.yml) to `.github/dependabot.yml`


### About the licence

The Open Government Licence (OGL) was developed by the Controller of Her Majesty's Stationery Office (HMSO) to enable
information providers in the public sector to license the use and re-use of their information under a common open
licence.

It is designed to encourage use and re-use of information freely and flexibly, with only a few conditions.
