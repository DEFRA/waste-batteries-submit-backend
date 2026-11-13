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

`POST /pom-submissions` creates one initial submission for one scheme, member, year and quarter. The response is `201 Created`, with `Location: /pom-submissions/{id}` and the stored submission as the body. Documents are stored in the `pom-submissions` collection. The submission and its outbox event commit or roll back in one transaction.

`PATCH /pom-submissions` amends the current submission, using the supplied scheme, member, year and quarter to find it. The amendment inserts a new version and sets the previous submission so it is no longer current. The response is `200 OK` and the new stored submission is the body. The new submission, the update of the previous submission, and the outbox event commit or roll back in one transaction.

`POST /pom-submissions/upsert-batch` creates or amends a batch of submissions. The body is a required JSON array, with each element has the same shape as a single create or amend. A submission is created when no current row exists for its scheme, member, year and quarter, and amended when one does. The response is `200 OK` and the body is a JSON array in the same order as the request is returned with the `requestIndex`, `id` (the submission just saved), `schemeOrgId`, `memberOrgId`, `compliancePeriodYear`, `compliancePeriodQuarter`, and `action` (`Created` or `Amended`). The submissions and the outbox events commit or roll back in one transaction.

`GET /pom-submissions/{id}` returns the stored submission as `200 OK`. Accepting gzip does not compress this response. The read is by id, so a submission that is no longer current is still returned.

`GET /pom-submissions` returns a page of submission objects. The filters are combined with AND: `schemeOrgId`, `memberOrgId`, `compliancePeriodYears` (array), `compliancePeriodQuarters` (array), and `returnCurrentOnly` (optional, defaults to true if omitted). When it is false, the list includes current submissions and submissions that are no longer current. At least one of `schemeOrgId`, `memberOrgId`, `compliancePeriodYears` or `compliancePeriodQuarters` is required. `returnCurrentOnly` alone does not satisfy that rule.
A missing `pageSize`, or one above the limit for that request, is replaced by `MaxPageSize` or `MaxGzipPageSize` (see below). A smaller `pageSize` is kept. `pageNumber` defaults to 1. A page past the end returns the last page. The JSON body has `items`, `pageNumber`, `pageSize`, `totalPages`, `totalCount`, `hasPreviousPage` and `hasNextPage`. No match is `200` with an empty `items` array. The list is gzip-compressed when the caller accepts gzip. A single submission is not. 

#### Page sizes when getting list of submissions

`PomSubmission:MaxPageSize` is the page size when the caller does not accept gzip. `PomSubmission:MaxGzipPageSize` is the page size when `Accept-Encoding` includes `gzip` or `*` with a quality above 0. `gzip;q=0` does not accept gzip, so the smaller limit applies and the body is plain JSON. A caller that does not accept gzip still receives `200` and uncompressed JSON. The response does not fail.

Both numbers were calculated for the current `PomSubmissionModel` shape, using the maximum likely array and field characters.  See `MeasuredPomSubmission.Distinct` for details of this. If that class changes, measure the plain JSON and the gzip response again and update both settings. Until that is done, the limits are for the old shape and a page can pass the 10 MiB CDP payload limit (10,485,760 bytes). The unit tests in `PomSubmissionPageBudgetTests` read `MaxPageSize` and `MaxGzipPageSize` and fail when either full page is not under 10 MiB.

#### Batch size when upserting submissions

When consumers of this API call the `upsert-batch` they will need to ensure that they do not pass the payload limit, otherwise the request will never reach this API.

As well as testing the page sizes for the get list response (see above) the unit tests in `PomSubmissionPageBudgetTests` also have the `RoundedBatchSize` - which is the number of requests the consumers can include in their `upsert-batch` request before hitting 10 MiB.  If the model shape subsequently changes, highlighting that we can allow more or less than submissions then this unit test will fail.  This will allow us to update the frontend limits for the upsert at the point the model shape changes.

Because returning the fully saved submission for each item in the batch would exceed the payload even quicker, upon a successful save we are returning the following for each item in the upsert to help identify what happened to which item:

The `requestIndex`, `id` (the submission just saved), `schemeOrgId`, `memberOrgId`, `compliancePeriodYear`, `compliancePeriodQuarter`, and `action` (`Created` or `Amended`). 

#### Outbox pattern when saving a submission

Each of the saves writes one outbox document per distinct scheme and compliance year, meaning several members or quarters for the same scheme and year share one event for a batch upsert.

This is done within the same transaction, which ensures that the save and the outbox are an atomic operation. A relay in this process then publishes each unsent document to the service's SNS topic. Consumers subscribe a queue to that topic.

The outbox event document is stored in the `outbox` collection with 
- `id` (a GUID, the same value if the transaction is retried), 
- `eventType` (`PomSubmissionCreated`, `PomSubmissionAmended`, or `PomSubmissionSaved` when one scheme and year both creates and amends),
- `schemaVersion` (set by the submission event, constant 1), 
- `subjectId` (the scheme id and year), 
- `version` (the commit time in unix milliseconds, so a later save orders after an earlier one), 
- `partitionKey` (the scheme id), 
- `occurredAtUtc`, 
- `actorId` (the users id from the configured JWT user-id claim, otherwise the name claim), 
- `correlationId` (from the `x-cdp-request-id` header, if passed in, else the activity trace id if this exists, else a new id), 
- `sent`, 
- `schemeOrgId`,
- `compliancePeriodYear`. 
 
`sent` is false. `sentAtUtc` and `expiresAtUtc` are omitted until the relay publishes the event. `expiresAtUtc` is `sentAtUtc` plus `OutboxRelay:RetentionDays` (90 by default).

An index named `sent_occurred_at` is on `sent` then `occurredAtUtc`. A unique index named `subject_version` is on `subjectId` then `version`, so one subject cannot store the same version twice.

A TTL index named `expires_at` deletes a document at `expiresAtUtc` (`expireAfterSeconds` 0). An unsent document has no `expiresAtUtc`, so it is not deleted.

The relay is a background service in this API. On each pass it claims the oldest unsent document whose claim has expired and whose next attempt is due, publishes it, and sets `sent`. A second instance cannot take that document until `claimedUntilUtc`. A failed publish leaves `sent` false and sets `nextAttemptAtUtc`, so the same document is tried again with backoff. The published `eventId` is the stored id. AWS assigns its own SNS message id on every publish, and the relay does not use that id.

The message body is `eventId`, `eventType`, `schemaVersion`, `subjectId`, `version` (the commit time in unix milliseconds, not the submission row's version), `partitionKey`, `occurredAt`, `actorId`, `correlationId`, `schemeOrgId` and `compliancePeriodYear`. `eventId` and `eventType` are the SNS message attributes. `sent` and `sentAtUtc` stay on the document.

The relay does nothing unless `OutboxRelay:Enabled` is true. `OutboxRelay:PollIntervalSeconds` defaults to 5. In CDP, request one standard SNS topic named `waste-batteries-submit-backend-pom-submissions-by-scheme-and-year` (Portal, Create, Resource Request). `cdp-app-config` sets `OutboxRelay__Enabled=true` and `OutboxRelay__TopicArn` to that topic's ARN for dev, test, perf-test, ext-test and prod. Redeploy after the topic exists. Leave `OutboxRelay__ServiceUrl` empty so the SDK uses the regional endpoint and the task role. Each API task runs the relay. The claim means only one task publishes a document.

Locally the relay publishes to the topic `waste_batteries_submit_events`. A demo queue, `waste_batteries_submit_events_demo`, is subscribed to that topic. After a successful publish the outbox document has `sent` true and `expiresAtUtc` 90 days later. The document stays in Mongo. The envelope stays on the demo queue until something deletes it. [Running](#running) covers starting the API and reading that queue. 


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

Two unique indexes cover a submission. The current-row index is `schemeOrgId`, `memberOrgId`, `compliancePeriodYear` and `compliancePeriodQuarter`, partial on `isCurrent: true`, so one current submission can use that key. The version index is those fields plus `version`, so each version of that key is stored once, including submissions that are no longer current.

The other indexes are scheme plus year plus quarter, member plus year where `isCurrent` is true, `memberBprn`, chemistry plus battery category on the entries, and a sparse index on `supersedesSubmissionId`.


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

The container starts a single-node replica set named `rs0`. The healthcheck initiates it and waits until the node is primary. `docker compose up` without `--wait` can return before that, and a save sent in that window fails.

#### MongoDB locally

Alternatively install MongoDB locally. Creates, amendments and batch upserts use a multi-document transaction, so the local server has to be a single-node replica set. A standalone `mongod` rejects the transaction.

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

Retryable reads and retryable writes stay at the MongoDB .NET driver default, which is on. The connection strings in this repo do not turn them off. A transient network error on a single-document read or write is retried by the driver. A create, an amendment and a batch upsert each run in one transaction with their outbox events. The driver retries that transaction when the error is labeled `TransientTransactionError`, and when the commit result is labeled `UnknownTransactionCommitResult`. Submission ids, outbox event ids and the timestamp are chosen before the transaction starts, so a retry writes the same documents. Duplicate-key conflicts and validation failures are not retried. The transaction aborts and nothing from that save is left behind.

#### MongoDB in CDP environments

In CDP environments a MongoDB instance is already set up
and the credentials exposed as environment variables.
That instance is a replica set, which is what the save transaction requires. The deployed connection string is supplied by the platform. Do not add `directConnection=true` there: the driver should discover the set.


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

`WasteBatteriesSubmitBackend.Tests.Integration` hosts the API in process with `WebApplicationFactory`. The placed-on-market submission tests start MongoDB 7 as a single-node replica set with Testcontainers, so Docker must be running. The replica set is what lets a save commit or roll back the submission writes and the outbox events together.

### Running

The Development launch profile listens on <http://localhost:3001>. Swagger UI is at `/swagger`. In that profile the relay is on. It publishes to floci at `http://localhost:4566` about every 5 seconds (`OutboxRelay:PollIntervalSeconds`).

#### API from the debugger or the command line

Start MongoDB and floci from this repo. When the floci container starts, `compose/floci/start.d/10-setup-resources.sh` creates the local topic and the demo queue.

```bash
docker compose up -d --wait mongodb floci
dotnet run --project src/WasteBatteriesSubmitBackend --launch-profile Development
```

Visual Studio uses the same Development profile. The profile sets the local floci credentials (`AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY` are `test`).

To run the submit UI against this API, start the Defra ID stub, MongoDB and floci from `waste-batteries-submit-frontend`, then the UI. That compose publishes port 4566, so leave floci stopped in this repo while you do.

```bash
docker compose up -d cdp-defra-id-stub mongodb
npm install
npm run dev
```

The frontend floci script `compose/floci/start.d/30-create-submit-outbox-topic.sh` creates the same topic and demo queue. Then run this API with the Development profile, as above. Create or amend a submission. Within about one poll the matching outbox document is `sent` and the envelope is on the demo queue.

#### API in Compose

```bash
docker compose up --build -d
```

The API container listens on <http://localhost:8085>. The relay in that container publishes to `http://floci:4566`.

#### Read a published message

The demo queue is `waste_batteries_submit_events_demo`. These commands run the AWS CLI that is already in the floci image, so they work on Windows, macOS and Linux with Docker alone. Run them from the directory of the Compose file that started floci: this repo, or `waste-batteries-submit-frontend` when the UI compose started it.

Count the messages waiting:

```bash
docker compose exec floci aws sqs get-queue-attributes --queue-url http://localhost:4566/000000000000/waste_batteries_submit_events_demo --attribute-names ApproximateNumberOfMessages
```

Read one envelope and leave it visible on the queue:

```bash
docker compose exec floci aws sqs receive-message --queue-url http://localhost:4566/000000000000/waste_batteries_submit_events_demo --visibility-timeout 0
```

`--visibility-timeout 0` keeps the message visible to the next read. A receive without it hides the message for the queue's visibility timeout. The message stays on the queue until something deletes it.

With the [AWS CLI](https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html) installed on the machine, call floci on localhost directly:

```bash
aws --endpoint-url http://localhost:4566 sqs get-queue-attributes --queue-url http://localhost:4566/000000000000/waste_batteries_submit_events_demo --attribute-names ApproximateNumberOfMessages
```

```bash
aws --endpoint-url http://localhost:4566 sqs receive-message --queue-url http://localhost:4566/000000000000/waste_batteries_submit_events_demo --visibility-timeout 0
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
