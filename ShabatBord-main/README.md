# לוח שבתות — Shabbat Board

A Hebrew-first, RTL community calendar built with **ASP.NET Core / .NET 10 LTS**
and **React / TypeScript / Vite**. Select a **Gregorian cycle start year** (for
example, 2027) to see every Saturday from **Parashat Bereshit in that year**
until the Saturday before the next Bereshit. Dates remain chronological across
January, with both Hebrew dates and Israeli Torah readings. Shabbat
Mevarchim is highlighted with the blessed month and Rosh Chodesh dates.

Families choose any Shabbat and **enter their name directly**. Names can be
edited and assignments removed. There is no family registry or families page.
Concurrent claims and stale edits are rejected rather than silently overwriting
another family's assignment.

## Quick start

Prerequisites: .NET 10 SDK and Node.js 24 LTS with npm. Install the latest .NET 10
servicing release; `global.json` permits the installed .NET 10 feature band.

```sh
dotnet restore
npm ci --prefix frontend
dotnet run --project src/ShabbatBoard.Api
```

In a second terminal:

```sh
npm run dev --prefix frontend
```

Open <http://localhost:5173/?year=2027>. Vite proxies `/api` and `/health` to
<http://localhost:5131>. The start year defaults to the current Gregorian year
in Israel; supported start years are 1900–2100. The selector is labelled
**מחזור המתחיל בשנת** and the actual inclusive date range is displayed.
Cycle length depends on the Hebrew calendar, not on a fixed 52/53 rows.
For example, selecting **2027** shows **October 30, 2027 through October 7,
2028** (50 Saturdays); Bereshit on October 14, 2028 begins the next cycle and
is excluded. The filter supports all Saturdays, Mevarchim only, and
unassigned Saturdays.

Development API documentation: <http://localhost:5131/swagger>, with the
OpenAPI document at `/openapi/v1.json`. Swagger is disabled in production.

## Architecture

| Project | Responsibility |
| --- | --- |
| `src/ShabbatBoard.Domain` | Assignment invariants, date validation, Saturday enumeration |
| `src/ShabbatBoard.Application` | API DTOs, store/provider contracts, calendar composition |
| `src/ShabbatBoard.Infrastructure` | Hebcal integration, event projection/cache, atomic JSON store |
| `src/ShabbatBoard.Api` | HTTP endpoints, DI, Problem Details, OpenAPI, production static serving |
| `frontend` | Accessible calendar, direct-name dialogs, filtering, Gregorian year routing |
| `tests/ShabbatBoard.Tests` | Domain, persistence, calendar, upstream HTTP and API integration tests |

No database is required. The JSON file contains `schemaVersion: 1` and an
`assignments` array with `date`, `familyName`, and `version`. Calendar data is
not mixed into persisted assignments.

Names are trimmed, Unicode-normalized, and limited to 100 characters.
The same name can be assigned to multiple Saturdays. No fictional families
or production dates are seeded.

## Calendar correctness and Hebcal

The official [Hebcal Jewish Calendar REST API](https://www.hebcal.com/home/195/jewish-calendar-rest-api)
directly supports Mevarchim; a custom calendar engine is unnecessary.

For cycle start year `Y`, the backend requests December 25 of `Y-1` through
January 14 of `Y+2` using:

```text
https://www.hebcal.com/hebcal?v=1&cfg=json&start=YYYY-MM-DD&end=YYYY-MM-DD
  &i=on&s=on&mvch=on&nx=on&maj=on&d=on&hdp=1&leyning=off
```

`i=on` selects the **Israeli**, not diaspora, reading schedule.
`s` requests Parasha; `mvch` Mevarchim; `nx` Rosh Chodesh; `maj` festival
readings; `d` daily Hebrew dates; `hdp` Hebrew date parts. `leyning=off` avoids
unused detailed aliyot data.

The application finds exactly one Israeli Bereshit Parasha event in year `Y`
and one in year `Y+1`. It enumerates Saturdays from the first boundary
(inclusive) until the next (exclusive), and joins these events by date.
Missing, duplicate or non-Saturday Bereshit boundaries produce an explicit
upstream error. There are no fixed start/end dates in production code.
A festival Torah reading is shown when the
ordinary weekly Parasha is displaced. Dates are date-only values; the UI never
converts a Shabbat into the preceding date through a timezone offset.

Mevarchim is the Saturday **strictly before the first Rosh Chodesh day**.
If Rosh Chodesh starts on Saturday, the blessing is one week earlier. For
two-day Rosh Chodesh, both dates are shown and the first determines the
blessing. Adar I and II remain distinct in Hebrew leap years. Tishrei is
not blessed before Rosh Hashanah. Padded coverage preserves two-day Rosh
Chodesh across December/January boundaries.

These rules validate Hebcal's supplied Mevarchim events; they do not silently
invent replacements. Missing dates/readings, inconsistent events, failed
HTTP responses and invalid JSON produce an explicit retryable error. Successful
years are cached for six hours; failures are not cached. Upstream timeout is
15 seconds.

Calendar data and recorded test fixtures are provided by
[Hebcal](https://www.hebcal.com/) under
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).
Fixtures in `tests/ShabbatBoard.Tests/Fixtures` are recorded official Israeli
API responses for Gregorian test years 2022, 2024, 2027 and 2028, plus full
cycle coverage for start years 2026, 2027 and 2100, with the parameters above.
They are **test data only**, not production calendar lookup tables.

## HTTP API

All errors use `application/problem+json`. The optional `traceId` can be
used to correlate server logs. Invalid inputs return 400, missing assignments
404, concurrent claims/stale versions 409, Hebcal failures 502, and storage
failures 503.

| Method | Endpoint | Purpose |
| --- | --- | --- |
| GET | `/api/calendar?year=2027` | Bereshit cycle starting in 2027, with current assignments |
| POST | `/api/assignments/2027-10-30` | Claim a free Shabbat |
| PUT | `/api/assignments/2027-10-30` | Edit an existing assignment |
| DELETE | `/api/assignments/2027-10-30?version=UUID` | Remove an assignment |
| GET | `/health` | Verify API and readable persistent storage |

Claim:

```sh
curl -X POST http://localhost:5131/api/assignments/2027-10-30 \
  -H 'Content-Type: application/json' \
  -d '{"familyName":"משפחת כהן"}'
```

The 201 response includes the assignment's UUID `version`. To edit, send:

```json
{
  "familyName": "משפחת לוי",
  "version": "the-current-version-from-the-calendar-or-claim-response"
}
```

Each edit returns a new version. Removal includes that version in the query
string. A conflict requires refreshing the calendar and reviewing the current
assignment. Mutations are never automatically retried after a lost connection;
refresh before retrying because the server might already have saved the change.

The response's `year` is the **Gregorian cycle start year**; row dates can be in
the following year. Assignment dates may range from 1900 through 2101 so the
last selectable cycle (2100) remains fully assignable. Existing assignments and
versions are preserved by their absolute dates, with no JSON migration. An
early-2027 assignment appears in the cycle starting in 2026, not in the cycle
starting with Bereshit in October 2027.

## Persistence and deployment

**This application has no authentication.** Anyone who can access it can read
family names and claim/edit/remove assignments. It is intended only for a
trusted private network. Do not expose it publicly without adding authentication,
authorization, HTTPS and an appropriate deployment security review.

**Run exactly one API instance per data directory.** The process serializes
read/modify/write operations, validates persisted data, writes a unique file in
the same directory, flushes it to disk, then atomically renames it over the
destination. It does not provide a distributed lock for multiple replicas.
Use a local persistent filesystem with atomic rename semantics, not a network
share or ephemeral container filesystem.

Default file: `src/ShabbatBoard.Api/data/assignments.json` during local development.
Override using the environment variable `Persistence__FilePath`, preferably with
an absolute path. Corrupt, unsupported or inconsistent existing files abort
startup; the app never replaces them with empty state. Storage failures are
reported, not swallowed. Persisted family information is excluded from Git.

Restrict data-file access and back it up. To back up/restore: stop the single API
process, copy the JSON file to/from a protected backup location, ensure the API
user owns the restored file, then start and check `/health`. Retain the old file
until restoration is confirmed. Do not repair corruption by deleting the data.

Allowed HTTP hosts default to localhost and loopback addresses. For a trusted
LAN host, explicitly set `AllowedHosts` to its actual hostname/IP and configure
the bind address with `ASPNETCORE_URLS`. Keep the port behind private-network
firewall rules. Vite's development server is not a production server.

### Production build without Docker

```sh
npm ci --prefix frontend
npm run build --prefix frontend
dotnet publish src/ShabbatBoard.Api -c Release -o artifacts/publish
mkdir -p artifacts/publish/wwwroot
cp -R frontend/dist/. artifacts/publish/wwwroot
ASPNETCORE_ENVIRONMENT=Production \
  ASPNETCORE_URLS=http://localhost:8080 \
  Persistence__FilePath="$PWD/private-data/assignments.json" \
  dotnet artifacts/publish/ShabbatBoard.Api.dll
```

The published API serves the SPA and API from the same origin. `index.html`
is not cached; hashed assets may be cached. Unknown `/api` routes return JSON
404 rather than the SPA HTML. Keep `private-data` outside the public web root.

### Docker

```sh
docker compose up --build -d
```

Open <http://localhost:8080>. The supplied Compose configuration binds only to
loopback, runs as a non-root user, and persists assignments in a named volume.
Do not use `docker compose down -v` unless intentionally deleting saved data.
For private LAN access, explicitly change the port binding and configure the
allowed host. Backups still require stopping the API before copying the file.

## Verification

```sh
dotnet test
npm test --prefix frontend -- --run
npm run lint --prefix frontend
npm run build --prefix frontend
dotnet build
npx --prefix frontend playwright install chromium
npm run test:e2e --prefix frontend
```

Backend tests use temporary isolated files and a fake upstream HTTP transport
or recorded fixtures; they do not require live Hebcal. Coverage includes 52/53
Saturdays in Gregorian test ranges, 50/51/55-Saturday Bereshit cycles,
Israeli/diaspora divergence, festival readings, leap Adars, Saturday
and two-day Rosh Chodesh, Gregorian boundaries, source completeness, CRUD,
corruption, restart persistence, concurrent writes, optimistic concurrency
and API Problem Details. Cycle tests check chronological continuity, the first
Bereshit, exclusion of the next Bereshit, invalid boundaries, preserved
assignments across January, and assignment support through the 2100/2101 cycle.
Frontend tests exercise direct name entry, edits,
removal confirmation, year selection, filters and visible error/retry states.
Playwright tests exercise real-browser keyboard focus, Escape, the complete
claim/edit/remove flow, 390px mobile layout and long names. Axe checks WCAG
A/AA issues including contrast on the calendar and assignment dialog.

Native modal dialogs provide focus trapping and Escape handling, restore focus
on close, and expose labelled controls. The calendar supports keyboard
navigation, a skip link, live feedback, reduced motion and responsive RTL
cards on small screens while retaining semantic table markup.
