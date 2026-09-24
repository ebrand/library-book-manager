# Questions the specification leaves open

Specification: `book-manager-01` baseline v3 (CP-3, content hash `sha256:60be074e…a94`).
Each entry says what was undetermined, what was done instead, and what it affects. Where a
choice had to be made, the most conservative option was taken, and a test named for the
requirement (with this entry's id) holds it in place until the question is answered.

**Blocking** marks entries where what was built is known **not** to satisfy an inherited
standard, or where a wrong guess would be expensive to undo.

Status: all three capabilities (BOOK, AUTH, LEND) built.

---

## System-wide

### Q-SYS-01 — Which component implements each capability
**Undetermined:** the specification does not assign capabilities to components.
**Decision:** every capability (BOOK, AUTH, LEND) is **implemented and enforced in `api`**.
`web` is a client of `api`: it presents the capabilities and enforces none of them.
**Reason:** `web` runs in the user's browser and can be bypassed. The rules (uniqueness,
role checks, loan limits) have to hold for every caller, so they live where they can't be
bypassed. The acceptance criteria are therefore tested in full against `api`. `web` tests
carrying the same ids check only that the UI sends what the contract expects and shows
what the API answers.
**Affects:** all 19 requirements.

### Q-SYS-02 — The owning team is `payments` and org-sec-004 speaks of cardholder data
**Undetermined:** `system.yaml` gives `owningTeam: payments` for a library catalogue, and
org-sec-004's scope includes "handles cardholder data". Nothing in the specification
collects card data, and the gaps say fines (the only plausible reason to) are undecided.
**Decision:** no payment or card handling of any kind. Logs are still tested for card
numbers (AC-SEC-013-1), because a caller could put one in any free-text field.
**Question for the owner:** is `payments` the intended owning team?
**Affects:** system ownership; org-sec-004 scope.

### Q-SYS-03 — Persistence
**Undetermined:** storage technology, and how the schema is created and migrated.
**Decision:** EF Core 10 on SQLite, one file (`/data/library.db` in the container). The
schema is created at start-up with `EnsureCreated`, which **cannot migrate** an existing
database.
**Needs deciding before the first production deployment:** the production database, and a
move to EF migrations. Otherwise the first schema change after go-live will need manual work.
This has already happened twice in development: the AUTH and LEND stages each added tables or
columns, and the dev database had to be deleted and recreated both times.
**Affects:** all requirements; REQ-AUTH-006 (two-year retention needs durable storage).

### Q-SYS-04 — Transport shape of errors
**Undetermined:** the criteria name error codes (`ISBN_ALREADY_EXISTS`, `FORBIDDEN`, …) but
not the HTTP status or the body they travel in.
**Decision:** every error is a JSON body `{ "error": CODE, "message": text, "field": name|null }`.
Statuses: 400 for validation (`FIELD_REQUIRED`, `INVALID_FIELD`, `INVALID_REQUEST`,
`INVALID_SEARCH`, `ISBN_IMMUTABLE`), 401 when there is no accepted credential, 403 for
`FORBIDDEN`, 404 for not-found, 409 for conflicts with current state (`ISBN_ALREADY_EXISTS`,
`BARCODE_IN_USE`, `COPY_ON_LOAN`, `COPY_NOT_ON_LOAN`, `LOAN_LIMIT_REACHED`,
`MEMBER_HAS_OVERDUE_LOAN`, `BORROWER_NOT_A_MEMBER`). Every refused sign-in (`INVALID_CREDENTIALS`,
`ACCOUNT_LOCKED`) and every refused session (`SESSION_INVALID`, `SESSION_EXPIRED`) is 401.
**Affects:** every criterion that names an error.

### Q-SYS-05 — Error codes the specification does not name
**Undetermined:** some refusals the implementation has to make have no code in the spec.
**Decision:** the following codes are **invented** and should be confirmed or renamed:

| Code | When |
|---|---|
| `UNAUTHENTICATED` | no credential presented (distinct from AUTH's `SESSION_INVALID` / `SESSION_EXPIRED`, which are for a credential that was presented and refused) |
| `INVALID_FIELD` | a field is present but malformed (e.g. ISBN of the wrong shape, non-numeric year) |
| `INVALID_REQUEST` | the body is not a JSON object |
| `INVALID_SEARCH` | a search names zero or several fields, or an invalid page |
| `TITLE_NOT_FOUND` | an ISBN not in the catalogue |
| `USER_NOT_FOUND` | changing the role of, lending to, or (as a librarian) listing the loans of a user id that does not exist |
| `BORROWER_NOT_A_MEMBER` | checking out to a user whose role is not member |

`COPY_NOT_FOUND` is taken from AC-LEND-001-3 and reused for removing an unknown copy.
**Affects:** REQ-BOOK-001..005, REQ-AUTH-004, REQ-AUTH-005, REQ-LEND-001, REQ-LEND-006.

### Q-SYS-06 — Public host name and port
**Undetermined:** where the system is served.
**Decision:** none is assumed. The OpenAPI `servers` entry is a placeholder
(`library.example.invalid`). The HTTPS port that plaintext is redirected to is read from
the `HTTPS_PORT` setting. Without it, the redirect names the container's internal port
(checked by running the container: without the setting it redirected to `:8443`, and with
`HTTPS_PORT=18443` to `:18443`).
**Affects:** REQ-SEC-011 in deployment.

---

## Capability BOOK

### Q-BOOK-01 — Who may change the catalogue
**Undetermined:** REQ-BOOK-001..004 say "a librarian shall be able to…" but don't say whether
an **administrator** may too. REQ-AUTH-004 makes roles mutually exclusive ("exactly one of"),
so an administrator is not also a librarian.
**Decision:** least privilege. Only the `librarian` role may add or change titles and add or
remove copies. Members **and administrators** are refused with `FORBIDDEN`. Viewing a
title's detail (below) is also librarian-only.
**Affects:** REQ-BOOK-001..004, REQ-AUTH-004. Test: `REQ-BOOK-001..004 (Q-BOOK-01)`.

### Q-BOOK-02 — Required fields other than publicationYear
**Undetermined:** only AC-BOOK-001-3 is explicit (empty `publicationYear` → `FIELD_REQUIRED`).
**Decision:** REQ-BOOK-001 lists four fields to be supplied, so ISBN, title and author are
also required, with `FIELD_REQUIRED` naming the field. Absent, `null` and whitespace-only
all count as empty. When several are empty, only the first is reported, in the order isbn,
title, author, publicationYear. An update may not blank a field it sends.
**Affects:** REQ-BOOK-001, REQ-BOOK-003.

### Q-BOOK-03 — What counts as an ISBN
**Undetermined:** format, validation, and whether `978-0-261-10221-7` and `9780261102217` are the same ISBN.
**Decision:** hyphens and spaces are removed and a trailing `x` is upper-cased before
storing or comparing, so both spellings above are the same title (and a duplicate). After
that, the value must be 13 digits, or 9 digits plus a digit or `X`; otherwise
`INVALID_FIELD`. The **check digit is not verified**. **ISBN-10 and ISBN-13 forms of the same
book are treated as different titles.**
**Affects:** REQ-BOOK-001 (uniqueness), REQ-BOOK-005 (ISBN search).

### Q-BOOK-04 — Range of publicationYear
**Undetermined:** valid range.
**Decision:** any whole number is accepted (no lower or upper bound, future years included).
A non-integer is refused with `INVALID_FIELD`.
**Affects:** REQ-BOOK-001, REQ-BOOK-003.

### Q-BOOK-05 — Adding a copy to an ISBN not in the catalogue
**Decision:** refused, 404 `TITLE_NOT_FOUND` (invented code, Q-SYS-05).
**Affects:** REQ-BOOK-002.

### Q-BOOK-06 — Barcodes, and what "removed" means
**Undetermined:** REQ-BOOK-002 says the system *assigns* the barcode, while AC-BOOK-002-2 has
a librarian *supplying* one. The barcode format isn't given, and nothing says whether a
removed copy's barcode may be reused.
**Decision:**
- A librarian may supply a barcode. If none is supplied, the system assigns `LBM` + 10 random digits.
- Supplied barcodes are any non-empty text, compared case-sensitively. No format is imposed.
- Removal is a **soft delete**. The copy disappears from the catalogue, counts and search,
  can't be lent (AC-LEND-001-3), and its row is kept so its loan history survives (REQ-BOOK-003).
- A removed copy's barcode is **never reassigned** (`BARCODE_IN_USE`). This avoids two
  physical items sharing a barcode across the loan history.
- Removal can't be undone. Restoring a removed copy is not specified and not built.
**Affects:** REQ-BOOK-002, REQ-BOOK-004, AC-LEND-001-3.

### Q-BOOK-07 — Update semantics and ISBN_IMMUTABLE
**Decision:** `PATCH /v1/titles/{isbn}` changes only the fields sent. An `isbn` field equal to
the current ISBN (after normalisation) is not a change and is accepted. A different one
refuses the **whole** request with `ISBN_IMMUTABLE`, and nothing else in it is applied.
**Affects:** REQ-BOOK-003.

### Q-BOOK-08 — One search box, or one field at a time?
**Undetermined:** "search the catalogue by title, author or ISBN" could mean one free-text term
matched against all three.
**Decision:** a search names **exactly one** of `title`, `author` or `isbn`, because
AC-BOOK-005-1 has a member searching "for author 'Le Guin'". Naming none or several is
refused with `INVALID_SEARCH`. A combined search is not provided.
**Affects:** REQ-BOOK-005.

### Q-BOOK-09 — Paging
**Undetermined:** page numbering, whether the client may choose a smaller page, and what a page past the end returns.
**Decision:** pages are numbered from 1 (AC-BOOK-005-2 calls 101–120 "the third page"). The
size is fixed at 50 and a client-supplied size is ignored. A page past the end returns no
items with the true total. Page 0, negative or non-numeric pages get `INVALID_SEARCH`.
**Affects:** REQ-BOOK-005.

### Q-BOOK-10 — Matching and ordering
**Undetermined:** exact or partial match, case sensitivity, and result order (paging needs a stable order).
**Decision:** title and author match on any part of the field, case-insensitively. `%` and
`_` in the term are taken literally. ISBN matches exactly after normalisation (Q-BOOK-03).
Results are ordered by title, then ISBN.
**Known limitation:** SQLite folds case for ASCII only, so `Ö` does not match `ö`. This goes
away with a production database whose collation handles it (Q-SYS-03).
**Affects:** REQ-BOOK-005.

### Q-BOOK-11 — Endpoint to read one title
**Undetermined:** no requirement asks for a title's detail, but "the title reports three copies"
(AC-BOOK-002-1, -003-1, -004-1) and removing a copy both need a way to see a title's copies.
**Decision:** `GET /v1/titles/{isbn}` returns the title with its copies' barcodes and on-loan
flags. It is **librarian-only** (Q-BOOK-01). Members see counts and availability only, via search.
**Affects:** REQ-BOOK-002..004.

### Q-BOOK-12 — Removing a title
**Undetermined:** the specification lets copies be removed but says nothing about titles (AC-BOOK-004-3 keeps a zero-copy title).
**Decision:** not built. A title, once added, stays in the catalogue.

### Q-BOOK-13 — What "on the shelf" means
**Decision:** a copy is on the shelf when it is in the catalogue (not removed) and has no
open loan. No other copy states (lost, damaged, in transit, at another branch) exist,
because none are specified. The branch-transfer rules are explicitly unsettled (LEND gap).
**Affects:** REQ-BOOK-004, REQ-BOOK-006, REQ-LEND-001.

### Q-BOOK-14 — Removing a copy at the moment it is checked out
**Undetermined:** the order in which a simultaneous removal and checkout of one copy take effect.
**Decision:** whichever reaches the database first wins, and the other is refused
(`COPY_NOT_FOUND` or `COPY_ON_LOAN`). Removal, checkout and return each run in a
write-locking transaction, so they can't interleave (Q-LEND-06). A test runs the race ten
times against a file-backed database: never both, never neither.
**Affects:** REQ-BOOK-004, REQ-LEND-001.

---

## Capability AUTH

### Q-AUTH-01 — What a session is, and how it travels
**Undetermined:** the form of the "session" in REQ-AUTH-001.
**Decision:** an opaque random token (256 bits), returned once by `POST /v1/sessions` and sent
as `Authorization: Bearer <token>`. Only its SHA-256 hash is stored, so a copy of the
database does not hand out live sessions. There is no absolute lifetime: only the 30-day idle
limit (REQ-AUTH-005) and sign-out end a session. Signing out ends that one session, not the
user's others.
**Trade-off to confirm:** `web` keeps the token in `sessionStorage`. That survives a reload
but not closing the tab, and it is readable by any script that manages to run on the page
(XSS). The alternative is an HttpOnly cookie, which is safe from scripts but needs
cross-site request forgery protection. Nothing in the spec decides between them.
**Affects:** REQ-AUTH-001, REQ-AUTH-005.

### Q-AUTH-02 — Does lockout reveal which emails have accounts?
**Undetermined:** REQ-AUTH-003 locks "an account". AC-AUTH-001-3 insists that an unknown email is
answered identically to a wrong password, which only makes sense if account existence is
meant to stay hidden. If only real accounts could lock, the fifth attempt would reveal it.
**Decision:** failures are counted **per email address, whether or not an account exists**.
An unknown email locks after five failures exactly as a real one does. For an unknown email,
the password is also run through the same hashing cost, so response time doesn't obviously
differ. Measured against the dev server with production iterations: about 105 ms for a wrong
password against about 102 ms for an unknown email.
**Known limitation:** a lockout row is created for any email anyone tries and is never
deleted, so the table can be grown by an attacker. Clean-up rules aren't specified.
**Known limitation:** attempts that arrive at the same moment are all counted (the count is
incremented in one statement), but several attempts already in flight when the fifth fails
still get a password check.
**Affects:** REQ-AUTH-001, REQ-AUTH-003.

### Q-AUTH-03 — Attempts during and after a lock
**Undetermined:** whether attempts during a lock extend it, and what the count is afterwards.
**Decision:** during the 15 minutes every attempt is refused with `ACCOUNT_LOCKED`, the password
is not checked, the attempt is not counted, and the lock is not extended. The lock runs 15
minutes from the fifth failure. When it ends the count restarts from zero. A success at any
point resets the count (AC-AUTH-003-3). The lock response is 401 with `ACCOUNT_LOCKED`.
**Affects:** REQ-AUTH-003.

### Q-AUTH-04 — What the audit records
**Undetermined:** whether "every sign-in" includes failed ones.
**Decision:** successful sign-ins, sign-outs and role changes are recorded, as required.
**Refused** sign-ins are also recorded, with the email as typed, the error returned and the
account if one exists, but never the password. Account creation and password setting by the
operator commands (Q-AUTH-10) are recorded too, with no acting user. Session expiry is not
an event, since nothing happens at that moment. A role change to the role the user already
has changes nothing and is not recorded.
There is **no endpoint to read the audit**. Who may read it is not specified, so it is
readable only in the database.
**Affects:** REQ-AUTH-006.

### Q-AUTH-05 — "Retain those records for two years": delete afterwards?
**Undetermined:** whether two years is a minimum to keep, or also a time to delete.
**Decision:** deleting audit records can't be undone, and keeping them can be reversed later.
So by default **nothing is ever deleted**. A daily retention job exists. It deletes records
older than two calendar years only when `Audit:PurgeAfterRetention=true`, and never anything
younger. AC-AUTH-006-2 is tested both ways.
**Affects:** REQ-AUTH-006.

### Q-AUTH-06 — Can an administrator change their own role?
**Undetermined:** the criteria speak of changing *another* user's role.
**Decision:** refused with `FORBIDDEN`. This also stops the last administrator removing the
only way to administer the system.
**Affects:** REQ-AUTH-004.

### Q-AUTH-07 — Role values on the wire
**Decision:** roles are the lower-case strings `member`, `librarian` and `administrator`,
matched exactly. Anything else is `INVALID_FIELD`, and an empty role is `FIELD_REQUIRED`.
Roles are read from the database on every request, so a change takes effect on the user's
very next request, in sessions already open.
**Affects:** REQ-AUTH-004.

### Q-AUTH-08 — Email addresses
**Decision:** trimmed and lower-cased before storing and comparing, so `Reader@Example.test` and
`reader@example.test` are the same account. Only a minimal shape check applies when creating an
account (one `@` with text on both sides, no spaces).
**Affects:** REQ-AUTH-001.

### Q-AUTH-09 — Empty credentials
**Decision:** a sign-in with an empty email or password is refused with `FIELD_REQUIRED` naming
the field, and is **not** counted as a failed attempt, since no password was tried.
**Affects:** REQ-AUTH-001, REQ-AUTH-003.

### Q-AUTH-10 — How accounts come into existence — **Needs an answer**
**Undetermined:** the criteria start from "a registered member" and "a member who has just set a
password", but nothing says how anyone registers or sets a password.
**Decision:** no self-registration and no password endpoint. Both would be public
account-creation or credential-changing surfaces the spec never asked for. Accounts are
created by whoever operates the service:

    dotnet LibraryBookManager.Api.dll create-user --email a@b.c --role member|librarian|administrator
    dotnet LibraryBookManager.Api.dll set-password --email a@b.c

The password is read from standard input. When typed at a terminal it is not echoed.
**Not verified:** the no-echo path for a real terminal. Piped input was tested for real.
Passwords must be at least 8 characters, which is my addition (NIST SP 800-63B minimum).
The spec sets no password policy.
Setting a password neither ends the user's existing sessions nor clears a lockout. Neither is specified.
There is no way to delete or deactivate a user.
**Affects:** REQ-AUTH-001, REQ-AUTH-002, REQ-AUTH-004.

### Q-AUTH-11 — Password hashing parameters
**Decision:** PBKDF2-HMAC-SHA256, 600,000 iterations (OWASP guidance), 128-bit random salt per
password, 256-bit output. The stored form records its own iteration count, so the count can
be raised later without breaking existing passwords. Tests use 1,000 iterations for speed.
Production's default is tested separately, and the dev database was confirmed to hold 600,000.
**Affects:** REQ-AUTH-002.

### Q-AUTH-12 — Who may read the user list
**Undetermined:** no requirement lists users. The administrator UI needs one to change roles.
**Decision:** `GET /v1/users` exists, is administrator-only and returns every user (no paging).
`GET /v1/users/me` returns the caller.
**Affects:** REQ-AUTH-004. LEND will need librarians to find members, and that will be recorded then.

---

## Capability LEND

### Q-LEND-00 — The declared gaps
**Not built**, as the specification directs:
- **Holds and reservations.** No way to queue for a title that is fully out. A test fails if
  any route mentions holds, reservations, queues or waitlists.
- **Overdue fines and payment.** Nothing assumes fines exist. A late return is recorded as late
  (AC-LEND-004-3) and an overdue loan blocks further borrowing (REQ-LEND-003), and that is all.
  A test fails if loans, their stored columns or the API contract mention fines, fees, charges,
  payments, amounts, balances or penalties. The web UI shows no money wording.
Renewals (extending a due date) are also not specified and not built.

### Q-LEND-01 — Whose "day"? — **Needs an answer**
**Undetermined:** "due 21 days from the day it is checked out", "returned on 10 March" and "the
day after its due date" all need a calendar day, and the library's time zone is not given.
**Decision:** the day is the calendar day in the zone named by `Library:TimeZone` (an IANA id,
e.g. `Europe/London`). **When unset it is UTC**, which is a guess. Due, checkout and return
dates are stored and published as plain dates. A loan is overdue from 00:00 (library time) on
the day after its due date. Tested for Pacific/Auckland against UTC, including in the Linux
container image.
**Affects:** REQ-LEND-001, -003, -004, -005.

### Q-LEND-02 — Who lends and returns
**Decision:** only librarians check out and return. Administrators and members are refused
with `FORBIDDEN`, the same least-privilege reading as Q-BOOK-01.
**Affects:** REQ-LEND-001, REQ-LEND-004.

### Q-LEND-03 — Who may borrow; what "good standing" means
**Undetermined:** whether librarians or administrators may borrow, and what "a member in good
standing" (AC-LEND-001-1) means.
**Decision:** only users whose role is `member` may borrow. Others are refused with
`BORROWER_NOT_A_MEMBER` (invented code). "Good standing" is taken to mean nothing beyond
REQ-LEND-002 and -003 (under the limit, nothing overdue). There are no suspensions or other states.
When several refusals apply, the first in this order is reported: `COPY_NOT_FOUND`,
`USER_NOT_FOUND`, `BORROWER_NOT_A_MEMBER`, `COPY_ON_LOAN`, `MEMBER_HAS_OVERDUE_LOAN`,
`LOAN_LIMIT_REACHED`.
**Affects:** REQ-LEND-001..003.

### Q-LEND-04 — How a librarian identifies the member
**Undetermined:** the criteria say "checks the copy out to them" but not how the member is named.
There is no library-card number in the specification.
**Decision:** the API takes the member's user id. Librarians can't list users (Q-AUTH-12), so a
librarian-only lookup `GET /v1/members?email=` returns the member with exactly that email
(case-insensitive), or nothing. Only users with role `member` are found.
**Affects:** REQ-LEND-001, REQ-LEND-006.

### Q-LEND-05 — Who sees which loans
**Decision:** a member sees only their own current loans. A librarian sees any member's, and
the full report (REQ-LEND-007). Administrators see neither: "non-librarian" in AC-LEND-007-2
includes them, and the same reading is applied to REQ-LEND-006. A member asking about any other
id gets `FORBIDDEN`, whether or not that id exists, so ids can't be probed.
"Current loans" means loans not yet returned. There is no endpoint for past loans: loan history
is kept (REQ-BOOK-003) but reading it is not specified.
**Affects:** REQ-LEND-006, REQ-LEND-007.

### Q-LEND-06 — Simultaneous requests
**Undetermined:** nothing says what happens when two librarians act on the same copy or member at once.
**Decision:** checkout, return and copy removal each run in a write-locking (serialisable)
transaction. A unique index also guarantees at most one open loan per copy, even if the
application is wrong. Tests against a file-backed database:
- 20 simultaneous checkouts of one copy give exactly one loan.
- 11 simultaneous checkouts to a member holding nine give exactly one.
- The database itself refuses a second open loan.
**Note:** SQLite serialises all writes. A production database (Q-SYS-03) will need the same
guarantees rebuilt with its own locking.
**Affects:** REQ-LEND-001, -002, REQ-BOOK-004.

### Q-LEND-07 — The report's size and order
**Undetermined:** "a complete report" gives no size, paging or order.
**Decision:** every open loan in one response, ordered by due date then barcode, with title,
ISBN, barcode, member id and email, dates and an overdue flag. No paging, so a very large
library would get a very large response. A member's loans use the same shape and order.
**Affects:** REQ-LEND-006, REQ-LEND-007.

### Q-LEND-08 — "Mark a loan overdue"
**Undetermined:** whether "mark" means a stored flag, a scheduled job or a notification.
**Decision:** overdue is worked out whenever a loan is read or a checkout is checked:
not returned, and today is after the due date. Nothing is stored, no job runs and nobody is
notified. Notifications aren't specified.
**Affects:** REQ-LEND-005.

---

## Inherited standards

### Q-STD-01 — Platform component library, router and data client — **Blocking**
**Undetermined:** plt-ui-001 REQ-UI-003 and plt-ui-react-001 REQ-UIR-002 require the *platform*
component library, router and data client. Their package names and APIs aren't in the
specification or the brief, and nothing outside the repository and spec could be read.
**Decision:** `web/platform-standin/` provides buttons, form controls, dialogs, tables,
navigation, a router and a data client behind the import path `@platform-standin/*`.
Application code under `web/src` gets primitives, routing and fetching **only** from there.
A test enforces this: no `<button>`, `<input>`, `<table>`, `<dialog>` or `<nav>` in `src`,
and no `react-router`, `axios`, `swr` or `fetch(`. The stand-in is named so it can't be
mistaken for the platform package.
**This is a known nonconformance** with REQ-UI-003 and REQ-UIR-002. A real conformance
check should flag `web/platform-standin/` as local duplicates of platform primitives, and it
would be right to.
**Needed:** the platform package names. The swap is confined to one import path, plus
adapting to the real components' props.

### Q-STD-02 — The standards' criteria describe the platform conformance check
**Undetermined:** most plt-* criteria have the form "given a repository …, when the platform
conformance check runs, then the check passes/fails". That describes the check's
behaviour, and the check is not available to this build.
**Decision:** those criteria are listed in TRACE.md as untested, with this reason.
Repository-state tests named for each requirement assert what the check would look for:
target framework, lock files, `strict`, the react version, OpenAPI route coverage, and no
local primitives. AC-UI-002-2 is the exception: it is about the build itself, so it has a
real test that builds a copy of the repo with an injected type error.

### Q-STD-03 — Platform base image — **Blocking**
**Undetermined:** plt-svc-001 REQ-SVC-003 requires a FROM line naming "a platform base image
tag in the supported set". Neither the platform registry nor the supported set is given.
**Decision:** `api/Dockerfile` uses `mcr.microsoft.com/dotnet/sdk:10.0` / `aspnet:10.0` and is
commented as a **nonconforming placeholder**. The image was built and run locally (below).
**Needed:** the platform registry and tag.

### Q-STD-04 — Which .NET
**Undetermined:** REQ-SVC-001 allows .NET 8 "or a later LTS", and AC-SVC-001-1 uses `net8.0` as its example.
**Decision:** `net10.0`. .NET 10 LTS reached GA in November 2025, so under REQ-SVC-002 a
service still on .NET 8 fails in November 2026, two months from this build. .NET 10 is also
the only SDK installed here.

### Q-STD-05 — React and TypeScript versions
**Decision:** React 19.3.0 (REQ-UI-001 / REQ-UIR-001 require 18 or later) and TypeScript 7.0.2,
both pinned exactly in `package.json`, with `package-lock.json` committed. REQ-UI-001 and
REQ-UI-004 overlap (both require TypeScript). One implementation satisfies both.

### Q-STD-06 — gRPC for internal interfaces
**Undetermined:** plt-api-001 REQ-API-002 requires gRPC for internal service-to-service APIs.
**Decision:** there are none. `web` is a browser application calling `api`'s external REST
interface. No gRPC or protobuf is built. If `web` is later given a backend of its own that
calls `api`, that call becomes an internal interface and REQ-API-002 applies.

### Q-STD-07 — Where the OpenAPI document lives
**Decision:** hand-written at `api/src/LibraryBookManager.Api/Contract/openapi.v1.yaml`
(OpenAPI 3.1.1). That directory is taken to be "the API's own contract module" (AC-API-004-2).
A test fails if any served route is missing from it, or it documents a route not served.
The major version is carried in the path (`/v1/…`) and in `info.version`.
The document is not served over HTTP: REQ-API-001 asks for it in the repository.

### Q-STD-08 — CI
**Undetermined:** the standards run their checks "in CI", but no CI system is specified.
**Decision:** no CI pipeline is added, since none could be run here to verify it. Settings
that CI would rely on are in place: `RestoreLockedMode` when `CI=true` (verified with
`CI=true dotnet restore`), `npm ci` against the committed lock file, and `npm run build`
type-checking before bundling.

---

## org-sec-004

### Q-SEC-01 — Mutual TLS for inbound integrations (REQ-SEC-012)
**Undetermined:** the specification describes no integration from an external *system*. The
API's callers are people using a browser.
**Decision:** nothing built. AC-SEC-012-1 has no test (see TRACE.md). If any
system-to-system inbound interface is added, it needs client-certificate authentication
before any application processing.
**Question:** is the browser-facing API meant to count as an "inbound integration"? If so,
every library member would need a client certificate, and that looks unintended.

### Q-SEC-02 — Where TLS terminates — **Blocking for deployment**
**Undetermined:** whether TLS terminates in the service or at a load balancer or ingress.
**Decision:** it terminates in Kestrel. Kestrel accepts TLS 1.2 and 1.3 only, and plaintext
gets a `301` to HTTPS.
**Warning:** forwarded headers are **not** trusted (no `X-Forwarded-Proto` handling). Behind
a proxy that terminates TLS and forwards plain HTTP, every request would look plaintext and
be redirected, in a loop. That deployment needs a decision on which proxies to trust before
this can go behind one.

### Q-SEC-03 — TLS 1.3 depends on the host OS
**Observed:** on the macOS development host, .NET's server-side TLS (SecureTransport) cannot
negotiate TLS 1.3. In the Linux container image, openssl probes gave: TLS 1.1 refused
(`protocol version` alert), TLS 1.2 negotiated, TLS 1.3 negotiated.
**Decision:** the automated test asserts that TLS 1.1 is refused and TLS 1.2 is accepted.
TLS 1.3 is **not** asserted, because REQ-SEC-011 only requires "1.2 or later".

### Q-SEC-04 — HSTS
**Undetermined:** REQ-SEC-011 requires redirecting plaintext but does not mention HSTS.
**Decision:** not added, to avoid inventing a requirement. It's cheap and ordinary, so worth considering.

---

## Technology choices the specification left open

| Area | Choice |
|---|---|
| API framework | ASP.NET Core minimal APIs, .NET 10 (Q-STD-04) |
| API persistence | EF Core 10 + SQLite (Q-SYS-03) |
| API tests | xUnit 2 + `Microsoft.AspNetCore.Mvc.Testing`; real Kestrel + `openssl` for TLS tests; `Microsoft.OpenApi` 3 to validate the contract |
| Web build | Vite 8, TypeScript 7 (`tsc -b` must pass before `vite build`) |
| Web tests | Vitest 5, Testing Library, jsdom |
| Resource identifiers | ISBN for titles, barcode for copies, a random GUID for users; database keys are never exposed (REQ-API-004) |

### Q-WEB-01 — How `web` is served
**Undetermined:** hosting for `web`, and whether it shares an origin with `api`.
**Decision:** same-origin is assumed (the data client uses relative `/v1/…` paths). In
development, Vite proxies `/v1` to `https://localhost:7043`. No production hosting for `web`
is built.

### Q-WEB-02 — Wording and layout
**Undetermined:** no UI wording is specified.
**Decision:** all messages, labels and page structure in `web` are mine.

### Q-WEB-03 — What `web` shows to each role
**Decision:** everyone who signs in can search. Members are also offered their own loans.
Librarians are also offered adding titles, managing a title's copies, the lending desk
(check out, return, a member's loans) and the on-loan report. Administrators are offered the
users page. Going directly to a
page a role can't use shows "Your role does not permit this." and calls nothing. The API
enforces every rule regardless (Q-SYS-01). A refused session (`SESSION_INVALID`,
`SESSION_EXPIRED`) on any request returns the user to sign-in with the reason.

### Q-WEB-04 — The main menu
**Undetermined:** the specification says nothing about navigation or visual design. The menu
was asked for after the three capabilities were built, and it is presentation only.
**Decision:** once signed in, a bar across the top holds the application's name (which leads
back to search), the pages the role is offered (Q-WEB-03) with the page being shown marked as
current, and the signed-in user with a sign-out button. Below 48rem wide the pages fold behind
a "Menu" button. Choosing a page or pressing Escape closes it, and Escape returns focus to the button.
The menu is a navigation primitive, so it lives in `web/platform-standin/` (REQ-UI-003,
Q-STD-01) and will be replaced by the platform library's navigation.
**Visual choices (mine):** Atkinson Hyperlegible type, a dark green bar, and a brass marker on
the current page. Only the menu and page frame are styled; forms and tables are still unstyled.
**Needs an answer:** the font is loaded from Google Fonts, so every user's browser contacts
Google when the app opens. If that is unwanted, the font can be served with the app instead,
or dropped for the system font.
**Not yet verified:** the look and the narrow-screen layout in a real browser. The tests check
behaviour in jsdom, which applies no CSS.
