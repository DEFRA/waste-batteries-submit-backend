# waste-batteries-submit-backend

API for battery compliance scheme placed-on-market (POM) submissions, built on the core delivery C# ASP.NET backend template.

The target data model is the Compliance Scheme POM Submission page in the [waste-batteries-docs](https://github.com/DEFRA/waste-batteries-docs/blob/main/technology/data-architecture/compliance-scheme-pom-submission/README.md) repository. This page describes what this service implements.

* [Placed-on-market submissions](#placed-on-market-submissions)
* [Install MongoDB](#install-mongodb)
* [Inspect MongoDB](#inspect-mongodb)
* [Testing](#testing)
* [Running](#running)
* [Dependabot](#dependabot)


### Placed-on-market submission endpoints

All requests expect a Defra ID bearer token.

`POST /pom-submissions` creates one initial submission for one scheme, member, year and quarter. The response is `201 Created`, with `Location: /pom-submissions/{id}` and the stored submission as the body. Documents are stored in the `pom-submissions` collection.

`PATCH /pom-submissions` amends the current submission, using the supplied scheme, member, year and quarter to find it. The amendment inserts a new version and sets the previous submission so it is no longer current. The response is `200 OK` and the new stored submission is the body. Both the new submission and updating the existing submission commit or roll back in one transaction.

`GET /pom-submissions/{id}` returns the stored submission as `200 OK`. Accepting gzip does not compress this response. The read is by id, so a submission that is no longer current is still returned.

`GET /pom-submissions` returns a page of submission objects. The filters are combined with AND: `schemeOrgId`, `memberOrgId`, `compliancePeriodYears` (array), `compliancePeriodQuarters` (array), and `isCurrent` (optional, defaults to true). At least one of `schemeOrgId`, `memberOrgId`, `compliancePeriodYears` or `compliancePeriodQuarters` is required. `isCurrent` alone does not satisfy that rule.
A missing `pageSize`, or one above the limit for that request, is replaced by `MaxPageSize` or `MaxGzipPageSize` (see below). A smaller `pageSize` is kept. `pageNumber` defaults to 1. A page past the end returns the last page. The JSON body has `items`, `pageNumber`, `pageSize`, `totalPages`, `totalCount`, `hasPreviousPage` and `hasNextPage`. No match is `200` with an empty `items` array. The list is gzip-compressed when the caller accepts gzip. A single submission is not. 

#### Page sizes when getting list of submissions

`PomSubmission:MaxPageSize` is the page size when the caller does not accept gzip. `PomSubmission:MaxGzipPageSize` is the page size when `Accept-Encoding` includes `gzip` or `*` with a quality above 0. `gzip;q=0` does not accept gzip, so the smaller limit applies and the body is plain JSON. A caller that does not accept gzip still receives `200` and uncompressed JSON. The response does not fail.

Both numbers were calculated for the current `PomSubmissionModel` shape, using the maximum likely array and field characters.  See `MeasuredPomSubmission.Distinct` for details of this. If that class changes, measure the plain JSON and the gzip response again and update both settings. Until that is done, the limits are for the old shape and a page can pass the 10 MiB CDP payload limit (10,485,760 bytes). The unit tests in `PomSubmissionPageBudgetTests` read `MaxPageSize` and `MaxGzipPageSize` and fail when either full page is not under 10 MiB.

#### Request JSON

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

The service sets these on the stored submission and includes them in responses:

- `id`
- `schemaVersion` (constant 1; a stored-shape change is a code change)
- `version` (from `PomSubmission:InitialVersion`, default 1; an amendment stores one more than the submission it replaces)
- `status` (from `PomSubmission:InitialStatus`, default Submitted)
- `isCurrent` (true on a new or amended submission; the superseded submission is set to false)
- `submittedOnUtc` (set to `createdAtUtc` when the status text is Submitted)
- `createdAtUtc`
- `updatedAtUtc` (null on create; set on the superseded submission)
- `supersedesSubmissionId` (null on create; set on the amendment)

Each tonnage entry also stores:

- `id`
- `activity` (from `PomSubmission:InitialActivity`, default Placed)
- `batteryCategory` (from `PomSubmission:InitialBatteryCategory`, default Portable; not accepted on the request) 

The unique index for a submission in the database is `schemeOrgId`, `memberOrgId`, `compliancePeriodYear` and `compliancePeriodQuarter`, partial on `isCurrent: true`. A submission with `isCurrent` false can share that key. 

The other indexes are not unique: scheme plus year plus quarter, member plus year where `isCurrent` is true, `memberBprn`, chemistry plus battery category on the entries, and a sparse index on `supersedesSubmissionId`.


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
docker compose up -d --wait mongodb
```

The container starts a single-node replica set named `rs0`. The healthcheck initiates it and waits until the node is primary. `docker compose up` without `--wait` can return before that, and an amendment sent in that window fails.

#### MongoDB locally

Alternatively install MongoDB locally. Amendments use a multi-document transaction, so the local server has to be a single-node replica set. A standalone `mongod` rejects the transaction.

- Install [MongoDB](https://www.mongodb.com/docs/manual/tutorial/#installation) on your local machine
- Start MongoDB:

```bash
sudo mongod --replSet rs0 --bind_ip_all --dbpath ~/mongodb-cdp
```

- Initiate the set once. An existing standalone data directory can be initiated the same way:

```javascript
rs.initiate({_id: "rs0", members: [{_id: 0, host: "localhost:27017"}]})
```

Connect with `directConnection=true`. The development and Compose connection strings do. That keeps the driver on the server you named. A client that discovers the set would follow the member host, which inside Compose is not the host published to your machine.

#### Retries

Retryable reads and retryable writes stay at the MongoDB .NET driver default, which is on. The connection strings in this repo do not turn them off. A transient network error on a single-document read or write is retried by the driver. An amendment runs in one transaction. The driver retries that transaction when the error is labeled `TransientTransactionError`, and when the commit result is labeled `UnknownTransactionCommitResult`. The new document's id and timestamp are chosen before the transaction starts, so a retry writes the same document. Duplicate-key conflicts and validation failures are not retried. The transaction aborts and the previous current row is left unchanged.

#### MongoDB in CDP environments

In CDP environments a MongoDB instance is already set up
and the credentials exposed as environment variables.
That instance is a replica set, which is what the amendment transaction requires. The deployed connection string is supplied by the platform. Do not add `directConnection=true` there: the driver should discover the set.


### Inspect MongoDB

To inspect the Database and Collections locally:
```bash
mongosh "mongodb://127.0.0.1:27017/?directConnection=true"
```

You can use the CDP Terminal to access the environments' MongoDB.

### Testing

```bash
dotnet test
```

`dotnet test` runs both suites. The pull request check and the SonarCloud check both use it. The pull request check fails when line coverage of `WasteBatteriesSubmitBackend` is below 90%.

`WasteBatteriesSubmitBackend.Tests.Unit` covers validators, mapping, the submission service and handlers without hosting the API.

`WasteBatteriesSubmitBackend.Tests.Integration` hosts the API in process with `WebApplicationFactory`. The placed-on-market submission tests start MongoDB 7 as a single-node replica set with Testcontainers, so Docker must be running. The replica set is what lets an amendment commit or roll back its two writes.

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
