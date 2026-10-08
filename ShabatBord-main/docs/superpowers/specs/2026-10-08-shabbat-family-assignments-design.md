# Shabbat family assignments

## Approved scope

A Hebrew-first, right-to-left community application displays **every Saturday in
a Torah-reading cycle starting with Parashat Bereshit**, not just Shabbat
Mevarchim. The selector uses Gregorian start-year numbers only: 2027 means the
Bereshit Shabbat occurring in 2027 through the Saturday immediately before
Bereshit in 2028. Rows remain chronological and the next Bereshit is excluded.
Both boundaries come from Hebcal's Israeli Parasha events, never fixed dates.
The selector defaults to the current Gregorian year in Israel.
Each Shabbat supports one family assignment; a family can serve on many
Shabbatot. The family enters its name directly when choosing a Shabbat; there
is no separate family registry. Hebrew dates remain visible alongside Gregorian dates.

This is a single-calendar application for a trusted private household/community
network. There is no login and it must not be exposed publicly without adding
authentication and authorization. Deployment runs one backend process with a
writable persistent data directory. Multiple API replicas are not supported.

## User interface

React, TypeScript, Vite, and React Router provide one calendar page:

- **Calendar:** Gregorian cycle-start-year selector, labelled "מחזור המתחיל בשנת",
  and previous/next navigation; the actual inclusive date range; chronological
  Shabbat table; Gregorian date, Hebrew date, Israeli Parasha or festival reading,
  Shabbat Mevarchim indicator, blessed month and associated Rosh Chodesh dates,
  assigned family name, and choose/edit/remove actions. Filters show all Shabbatot,
  Shabbat Mevarchim only, or unassigned dates. Summary counts reflect the full
  cycle (not a fixed 52 or 53 rows).

Choosing a Shabbat opens a dialog with a required, trimmed family-name field.
Editing changes that assignment's name; removing frees the Shabbat. Names need
not be unique across dates. No families page or navigation link is included.

The visual direction uses warm off-white backgrounds, forest-green actions,
subtle borders, and a highlighted Mevarchim row. Mobile views retain all information
without page-level overflow. Controls have visible labels, keyboard focus, semantic
markup, and sufficient contrast. Accessible dialogs restore focus and support
Escape; removal requires confirmation. Loading, empty-calendar, and explicit
retryable error states are included. There are no hardcoded production calendar
dates or automatically seeded fictional families.

Anyone with access to the private app may edit or remove an assignment.
Keyboard focus returns to the corresponding date's action after saving, or to
the filter when the row is no longer visible.

## Architecture

ASP.NET Core on .NET 10 (current stable LTS) has four projects:

1. **Domain:** assignment entities and invariants.
2. **Application:** DTOs, calendar/store abstractions, and use cases.
3. **Infrastructure:** configured Hebcal HTTP client, calendar event projection, cache,
   and atomic JSON persistence.
4. **API:** dependency injection, asynchronous endpoints, request validation,
   Problem Details, structured logging, OpenAPI, and Swagger UI in development.

The frontend consumes the API through typed request helpers. Production can serve
its built static assets from the API, keeping requests same-origin. Vite proxies
API requests in development. No database is used.

## Calendar source and correctness

Use the official [Hebcal Jewish Calendar REST API](https://www.hebcal.com/home/195/jewish-calendar-rest-api)
with `v=1`, `cfg=json`, `i=on`, `s=on`, `mvch=on`, `nx=on`, `maj=on`,
`d=on`, `hdp=1`, and `leyning=off`. Fetch December 25 of the preceding year through
January 14 two years after the selected year. This includes two Bereshit
boundaries and the surrounding Rosh Chodesh events.

Find exactly one Israeli Bereshit Parasha in the selected year and exactly one
in the following year. Missing, duplicate, or non-Saturday boundaries produce an
explicit upstream error. Enumerate all Saturdays from the first boundary
(inclusive) to the next boundary (exclusive), then join Hebcal Hebrew-date,
Parasha, holiday, Mevarchim, and Rosh
Chodesh events by date. Missing weekly Parasha on a festival is represented as
the festival reading, not a fabricated ordinary Parasha.

Hebcal directly supplies Mevarchim events, so no fallback calendar engine is
implemented. Verify the source's rules in tests: bless on the Saturday strictly
before the **first** Rosh Chodesh day, including when Rosh Chodesh starts on
Saturday; group two-day Rosh Chodesh; distinguish Adar I and Adar II in leap
years; do not bless Tishrei before Rosh Hashanah. Israeli readings always use
`i=on`. Data projection must preserve Unicode Hebrew and date-only strings.

Successful calendar results are cached for a bounded period. Upstream timeouts,
non-success responses, invalid JSON, or incomplete data produce logged,
retryable API failures, never success-shaped empty calendars or invented data.
The UI credits Hebcal and its CC BY 4.0 calendar data license.

## Data and API

The JSON store holds a schema version and assignments keyed by ISO
Gregorian Shabbat date. Keys do not depend on year selection or a source event
title. Each assignment stores the entered family name. Calendar results are
computed/read separately from assignments.

Endpoints:

- `GET /api/calendar?year=2027`: the cycle beginning with Bereshit in 2027, and
  its assignments, including dates in 2028. `year` is the Gregorian start year.
- `POST /api/assignments/{date}`: claim an unassigned Shabbat with a family name.
- `PUT /api/assignments/{date}`: rename an existing assignment.
- `DELETE /api/assignments/{date}`: remove an assignment.
- `GET /health`: readiness of the API and persistent store.

Names must contain 1–100 characters after trimming. Assignment dates must be Saturdays in the
supported range. Selected cycle start years are bounded to 1900–2100;
assignment dates extend through 2101 so the final cycle is fully assignable.
Existing persisted assignments and versions remain unchanged; selection never
rekeys or deletes them. Validation failures return 400, missing resources 404,
and already-claimed conflicts 409. Mutations include an optimistic concurrency
version: stale edits/deletes return 409 rather than overwriting another family's
change. Upstream failures return 502; storage failures return 503.

## Persistence and operational safety

A singleton store serializes read/modify/write operations with a semaphore.
All mutations validate against the same locked snapshot; write a unique
temporary file in the destination directory, flush, then atomically replace
the target. Failed writes do not update in-memory state. Reads return detached
snapshots. Validate the existing file on load and fail visibly on malformed,
unsupported, or inconsistent persisted state; never overwrite corrupt data
with an empty store.

Support a configurable data path, cancellation, startup validation, and a
documented persistent-volume deployment. Log operational errors without
logging family names or file contents. Persisted family data must not be
included in source control. Atomic file writes are not a replacement for
backups; document an offline backup/restore procedure.

## Verification and documentation

Backend tests cover deterministic source fixtures, all-Saturday enumeration
(52/53 Saturdays in Gregorian test ranges and full cycle counts), Hebrew
leap-year month names, two-day and Saturday Rosh
Chodesh, no Tishrei blessing, Gregorian boundaries, Israeli-versus-diaspora
readings, malformed upstream responses, CRUD invariants, restart persistence,
concurrent updates, corruption handling, and failed writes. Frontend tests cover
year selection, filters, assignment name entry/edit/removal, request errors, and accessible
controls. Cycle tests cover first-row Bereshit, consecutive seven-day intervals,
exclusion of next Bereshit, leap/ordinary cycles, and the final 2100/2101 cycle.
API integration tests cover validation, Problem Details, and the
end-to-end persisted assignment flow without depending on live Hebcal.

Document prerequisites, local launch/build/test commands, API examples, Hebcal
parameters and calendar logic, data path configuration, private-network
deployment restrictions, backups, and production frontend serving.
