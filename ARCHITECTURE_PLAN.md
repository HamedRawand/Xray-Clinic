# Diagnostic Clinic Manager (originally built for Nasim Diagnostic X-ray Clinic) — Architecture Plan

**Status:** Draft v5 (2026-11-01) — Phases 1–9 built and live-verified 2026-09-22 (see §14). A post-launch **workflow redesign** (steps A–D), per-exam **pricing snapshot and discounts**, **AFN currency**, **paging/search on every list**, reporting/**Excel export** and a new **"Clinical Teal" theme** were then built and user-tested on 2026-11-01 — see §15–§23. That later work is **not yet committed to git** (policy: commit only when the user asks, and only the parts they name). VPN setup, real-server certificate/installer/service setup, native Dari review and the app icon remain — see §24.

**Scope confirmed with user:**
- **Multi-modality clinic (added 2026-09-22, not scoped when the app was first named):** the clinic
  offers **X-ray, ultrasound, and echocardiography** — not X-ray alone. App keeps the "X-ray
  Clinic" brand name regardless (user's explicit call, like OPD Clinic being branded "Rx Writer"
  despite being a general clinic app), but the data model and workflow cover all three. See §6.2.
- LAN-only, 2–5 PCs, one dedicated server PC, client-server with REST API, records/reports only (no DICOM image storage — applies to all three modalities, not just X-ray)
- **Confirmed: at least one clinic PC runs Windows 7/8** → client target is **net6.0-windows** (locked in, not a placeholder). Directly reuse OPD Clinic's Win7-compatibility fixes: BMP-only ICO (no PNG-compressed frames), `Window.Icon` class handler in `App.xaml.cs`, Viewbox+Canvas+Path vector icons instead of Segoe MDL2 Assets glyphs, `<LangVersion>12</LangVersion>` + `Polyfills.cs` shims for the `required` keyword on net6.0 BCL
- Bilingual **English + Dari UI with Solar Hijri (Shamsi) dates** — same pattern as OPD Clinic
- **Basic billing/invoicing in scope from the start** (exam pricing, per-patient invoices, payment tracking)
- **Remote maintenance access to the server PC needed** (RDP or similar) — see §7 and §11
- **Currency is Afghani (AFN)** (added 2026-11-01): every amount is shown with "AFN" — see §17.
- **Discounts and free exams are allowed** (added 2026-11-01): reception may give them with a required reason, tracked per exam with who/when/why — see §17.
- **The app is generic — one installation per clinic** (added 2026-11-01, user decision): the clinic's name, contact details and logo are data (Clinic Settings), not code — see §25. Any similar clinic can install its own copy and enter its own name.
- **Clinic time zone is Asia/Kabul (UTC+4:30)** (added 2026-11-01): timestamps stay UTC in the database; "today" and date ranges are evaluated in clinic time — see §15.

---

## 1. Goals

- Multi-PC clinic app covering **X-ray, ultrasound, and echocardiography** services: reception, technician, and radiologist/doctor stations all see the **same live data**, on the clinic's local network.
- User-level authentication with role-based permissions (admin / radiologist / technician / receptionist, plus custom roles). One generic "Radiologist" role covers whoever reports on any imaging study, regardless of modality — confirmed, no separate Sonographer/Cardiologist role needed for now; the custom-role system already supports adding one later without a code change if that changes.
- Encrypted backup & restore of the full dataset, triggerable by an admin.
- Self-updating: new versions are published as GitHub Releases and the app fetches/installs them automatically, following the proven pattern from the OPD Clinic app.
- C# throughout (WPF desktop client + ASP.NET Core backend).

## 2. Why client-server instead of shared SQLite

The OPD Clinic app (single-PC) uses a local SQLite file per machine — that pattern breaks down the moment two PCs need to see the same patient list live. For this app:

- A **central Web API** owns the database and all business rules (permission checks happen once, server-side — not duplicated per client).
- Client PCs are thin: they authenticate, call the API, render data. No direct DB access from clients, no file-locking issues, no risk of two PCs corrupting a shared SQLite file over a network share.
- This also leaves a clean path to remote/multi-site access later (not needed now, but the architecture doesn't block it).

## 3. Tech Stack

| Layer | Choice | Notes |
|---|---|---|
| Client UI | WPF, **net6.0-windows**, C# 12 | Confirmed: at least one clinic PC runs Win7/8 — same target as OPD Clinic (Win7 SP1 → Win11); reuse its Win7 fixes directly (BMP-only ICO, Viewbox+Path icons, `Polyfills.cs`) |
| Client MVVM | CommunityToolkit.Mvvm | Same pattern as OPD Clinic (`[ObservableProperty]`, `[RelayCommand]`) |
| Client i18n | `LanguageService` ResourceDictionary toggle (EN/Dari) + `AfghanShamsiDatePicker` + `PersianCalendar` | Direct reuse of OPD Clinic's bilingual pattern — see that project's [[afghan-shamsi-datepicker]] skill and `AppSessionProxy` pattern |
| Backend API | ASP.NET Core Web API, **.NET 8** | Server-only component — no Win7 constraint applies here, so the API itself can target current .NET regardless of client OS; hosted as a Windows Service on the server PC |
| Database | **PostgreSQL** (free, no per-core licensing, solid Windows support) | Runs on the server PC as a Windows service |
| ORM | Entity Framework Core + Npgsql provider | Server-side only — clients never touch EF/DB directly |
| Auth | ASP.NET Core Identity *or* custom user table (mirroring OPD Clinic's `User`/`Permission` model) + **JWT bearer tokens** | BCrypt.Net-Next for password hashing (proven in OPD Clinic) |
| Reports/PDF | QuestPDF | Same as OPD Clinic — A4 portrait, RTL Dari support, Calibri font, B&W print optimization patterns all reusable |
| Logging | Serilog, file sink on both client and server | Matches OPD Clinic's `%LocalAppData%\...\Logs\` pattern |
| Client updater | GitHub Releases API + Inno Setup installer | Same mechanism as OPD Clinic's Phase 21 auto-update |
| Backup format | AES-256 encrypted archive (custom `.nxb` format, same scheme as OPD Clinic's `.rxb`) | Server-side `pg_dump` wrapped in encryption, not a raw SQLite file copy |
| Remote server access | Windows RDP over VPN (see §7) | For admin/maintenance of the server PC only — not part of the clinic app's own network surface |

## 4. High-Level Topology

```
                     Clinic LAN (private, no internet exposure required)
 ┌───────────────────────────────────────────────────────────────────────┐
 │                                                                       │
 │   ┌─────────────────────┐        ┌───────────────┐  ┌───────────────┐│
 │   │   SERVER PC          │        │ Reception PC  │  │ Radiologist PC││
 │   │ ──────────────────── │        │ (WPF Client)  │  │ (WPF Client)  ││
 │   │ PostgreSQL (service)  │◄──────►│               │  │               ││
 │   │ ASP.NET Core API      │  HTTP  └───────────────┘  └───────────────┘│
 │   │  (Kestrel, Windows    │  (JWT)          ▲                 ▲        │
 │   │   Service, port 5001) │                 │                 │        │
 │   │ Backup files on disk  │                 └────────┬────────┘        │
 │   └─────────────────────┘                          ... more PCs ...   │
 │                                                                       │
 └───────────────────────────────────────────────────────────────────────┘
```

- The **server PC** is also usable as a normal client (runs both the API/DB service and a WPF client instance).
- Clients find the server via a **one-time Settings screen** (enter server IP/hostname + port, "Test Connection" button) — no need for network auto-discovery at this scale.
- HTTPS with a self-signed certificate (or an internal CA) is still worth doing even on a LAN, since patient data + login credentials cross the wire — see §7.
- **Accepted tradeoff (confirmed): single point of failure, no offline mode.** If the server PC is down (power loss, crash, disk failure), every client PC loses access until it's back — there's no local caching or offline fallback. For a small clinic this is acceptable as-is; the nightly encrypted backups (§8) protect the data itself, and downtime is treated as tolerable rather than something to engineer around. No UPS/runbook was requested — revisit only if this becomes a real operational pain point after launch.

## 5. Backend (ASP.NET Core Web API)

### 5.1 Hosting
- Published as a self-contained Windows Service (`Microsoft.Extensions.Hosting.WindowsServices`), auto-starts on server boot — no need for IIS.
- Listens only on the LAN interface; Windows Firewall rule scoped to the local subnet.

### 5.2 API surface (initial cut — the endpoints as built are listed in §21)
- `POST /api/auth/login` → JWT + refresh token
- `GET/POST/PUT /api/patients`
- `GET/POST/PUT /api/exams` (exam orders / studies — type, requesting physician, status)
- `GET/POST /api/reports` (radiologist findings/impression, PDF generation)
- `GET/POST/PUT/DELETE /api/users` (admin only)
- `GET/POST /api/roles` (custom roles, same permission-enum pattern as OPD Clinic)
- `POST /api/backup/create`, `POST /api/backup/restore` (admin only)
- `GET /api/system/version` (used by client update checker, and to confirm client/server compatibility)

### 5.3 Authorization model
Reuse the OPD Clinic pattern almost directly:
- A `Permission` enum (granular: `ViewPatients`, `ManageUsers`, `CreateReport`, `FinalizeReport`, `AmendReport`, `ManageBilling`, `ManageBackup`, …). Full list to be finalized during Phase 1 scaffolding.
- Built-in roles (Admin, Radiologist, Technician, Receptionist) map to permission sets; **custom roles** supported the same way OPD Clinic does it.
- Every API endpoint is decorated with a permission-based `[Authorize(Policy = "...")]`, checked server-side — the WPF client also hides UI it can't use, but the server is the actual enforcement point (important now that it's not a single-user desktop app).

### 5.4 Concurrency & validation
- Server-side is now the single source of truth, so this removes an entire class of bugs OPD Clinic had to handle client-side (e.g., "existing patient edits silently discarded" — see that project's memory). Optimistic concurrency tokens (`xmin` in Postgres, or a `RowVersion`) on mutable entities to catch two staff editing the same record.

## 6. Database

- **PostgreSQL 16**, installed as a Windows service on the server PC.
- EF Core migrations, same workflow as OPD Clinic (`dotnet ef migrations add ...`), run against the server DB from a dev machine or via a startup migration step in the API.
- **Confirmed: clean start, no legacy data import.** No existing digital patient/exam records to migrate — the app launches empty and staff enter data going forward. No import wizard needed (unlike OPD Clinic's now-removed Access import tool); revisit only if that changes before launch.
- Core entities to start with (deliberately minimal — extend per phase, not up front):
  - `User`, `Role`/`CustomRole`, `AuditLog`
  - `Patient`
  - `ReferringPhysician`
  - `ExamType` (lookup: modality/body part/**price** — feeds billing directly; `Modality` is free text — e.g. `"X-Ray"`, `"Ultrasound"`, `"Echocardiography"` — not an enum, so adding a new modality or ultrasound sub-type is data entry, never a schema change)
  - `ExamOrder` (patient + exam type + requested-by + **`Status`** enum: `Pending` → `InProgress` → `Completed` → `Reported`, plus `Cancelled`) — confirmed: full status workflow, not just "has a report or not." Lets each role's worklist filter to what's relevant to them (reception sees Pending, technician sees Pending/InProgress, radiologist sees Completed-awaiting-report).
    - **As built since Phase 9 (2026-11-01):** `ExamOrder` also carries `ExamNumber` (`E-000123`, unique, assigned at creation), `Price` (the exam type's list price **snapshotted at booking**), `DiscountAmount` / `DiscountReason` / `DiscountedById` / `DiscountedAt`, `PerformedById` / `PerformedAt` (set when the exam is completed) and `CancelReason`. The charge is `Price − DiscountAmount`. See §16–§17.
  - `Report` (findings, impression, reported-by, links to `ExamOrder`, **`IsFinalized bool`, `FinalizedAt`, `FinalizedById`**) — confirmed: reports lock on finalize (see §6.1 below)
  - `ReportAmendment` (report id, changed fields/diff or full snapshot, amended-by, amended-at, reason) — audit trail for any post-finalization change
  - `MeasurementFieldDefinition` (exam-type id, field key, label, unit, data type, display order, is active) — admin-configurable structured fields per `ExamType`, added for echo/ultrasound (see §6.2)
  - `ReportMeasurement` (report id, field-definition id, value) — the actual measurement values entered on one report
  - `Invoice` (patient + one or more `ExamOrder`s + total + status [Unpaid/Paid/Partially Paid])
  - `Payment` (invoice + amount + method [Cash/Card/etc.] + received-by + timestamp)
- No DICOM/image BLOBs per your answer — if a report needs to reference an external image, store a path/ID pointing at wherever the X-ray, ultrasound, or echo machine's own software keeps it, not a copy of the file. Applies equally across all three modalities.
- **Currency and discounts (2026-11-01):** the currency is AFN (§17). An invoice's `Total` is the sum of each exam's charged amount (list price minus discount); a visit where everything is free produces a `Total` of 0 that is created already `Paid`. Discounts are only allowed before an exam is invoiced (after payment would need refunds — not built).
- **Confirmed: cash-only billing.** One invoice can cover multiple exam orders, payments are recorded against an invoice, balance is computed (not stored) as `Invoice.Total - Σ Payments`. No tax, no insurance/payer accounts, no multi-currency — keeps Phase 5 small. Revisit only if the clinic's needs change.

### 6.1 Report finalization & amendment (confirmed)

Once a radiologist marks a report **Final**, it becomes read-only in the normal edit flow — matches standard medical-record practice and protects against silent changes after a report has been printed or handed to a patient/referring doctor.

- A separate **"Amend Report"** action (permission-gated, e.g. `AmendReport`, likely Admin + the reporting radiologist only) is the only way to change a finalized report.
- Each amendment writes a `ReportAmendment` row (who, when, what changed, why) rather than overwriting history — the original finalized content stays visible/auditable.
- `ExamOrder.Status` moves to `Reported` when its `Report.IsFinalized` becomes true; an amendment does not revert the exam status.

### 6.2 Structured measurements for echo/ultrasound (confirmed, added 2026-09-22)

X-ray reports stay free-text (Findings/Impression). Echocardiography and ultrasound reports
additionally need **structured measurement fields** — echo's are fairly standardized (LVEF,
chamber dimensions, valve gradients), but ultrasound's vary a lot by sub-type (abdominal vs.
obstetric vs. thyroid vs. renal, etc., each with different fields). Confirmed approach:
**generic, admin-configurable, template-driven fields** rather than hardcoding columns or
entities per exam sub-type — hardcoding would mean a code change + migration every time a new
ultrasound sub-type is added, which doesn't scale for a clinic that may add exam types over time.

**How it works:**
- Each `ExamType` (e.g. "Echocardiography", "Obstetric Ultrasound", "Abdominal Ultrasound") owns
  a list of `MeasurementFieldDefinition` rows — field key, display label, unit (e.g. `%`, `mm`,
  `cm`), data type (Number or Text for v1), and display order. An admin defines these once per
  exam type via an Options-style screen (same CRUD pattern as OPD Clinic's lookup-table options
  tabs) — no developer involvement needed to add a new field or a new ultrasound sub-type later.
- When a radiologist opens a report for an `ExamOrder`, the report form loads that order's
  `ExamType`'s `MeasurementFieldDefinition` list and renders a dynamic set of inputs alongside the
  usual Findings/Impression text — X-ray exam types simply have zero field definitions, so their
  report form looks exactly like it does today, no special-casing needed in the UI.
- Entered values save as `ReportMeasurement` rows (one per field per report), keeping `Value` as
  a string regardless of declared data type — validation/formatting (e.g. numeric-only input,
  unit display) happens in the UI layer based on the field definition, not the storage layer.
  This keeps the schema stable even if a future data type is added.
- This is purely additive to the schema already built in Phase 1 — no rework needed there.
  `MeasurementFieldDefinition`/`ReportMeasurement` land as part of Phase 3 (core clinic workflow),
  alongside the Patient/ExamOrder/Report screens and controllers that don't exist yet.

## 7. Security

- **JWT bearer auth**, short-lived access token + refresh token; token carries user id + role/permission claims.
- **HTTPS even on LAN** — a private/internal cert (self-signed, or issued once via a tiny internal CA) avoids sending passwords and patient data in plaintext over the clinic Wi-Fi/switch. This is a change from OPD Clinic (which had no network boundary at all) and shouldn't be skipped just because it's "just a LAN."
- Passwords: BCrypt (cost 12), same as OPD Clinic.
- Same lockout policy as OPD Clinic (5 failed attempts → `IsLocked`, admin unlock).
- Full audit log server-side (`AuditService` equivalent) — who viewed/edited/deleted what, when.
- API itself still only binds to the LAN interface — the clinic app's own traffic never needs to leave the local network.
- **Remote maintenance access (confirmed needed):** kept fully separate from the app's own API. **Confirmed approach: router-based VPN** — enable the clinic's router/firewall's built-in VPN server (WireGuard or OpenVPN, whichever it supports) so no extra software runs on the server PC itself; connect via VPN, then RDP normally to the server PC once inside the tunnel. Do **not** port-forward RDP (3389) or the API port directly to the internet — that's a common ransomware entry point for exactly this kind of small-business server. Needs confirming the actual router model supports VPN server mode before Phase 9; if it doesn't, fall back to the Windows VPN server role or a remote-support tool as documented alternatives.

## 8. Backup & Restore

Different from OPD Clinic's "zip up the SQLite file" approach, since data now lives in Postgres:

- **Create Backup** (admin-only, from the WPF client or a scheduled server task): server runs `pg_dump`, then encrypts the dump with AES-256 (same `Rfc2898DeriveBytes.Pbkdf2` + AES-CBC scheme proven in OPD Clinic's `.rxb` format) into a `.nxb` file, written to a configurable backup folder (ideally on a second drive or network share, not just the server's C: drive).
- **Restore** (admin-only): decrypts, then `pg_restore` into a fresh/target database, inside a maintenance window (API should refuse normal traffic or run in a "restore in progress" mode while this happens).
- **Scheduled automatic backups**: a background hosted service on the API (e.g., nightly) — recommended in addition to the manual admin button, since a multi-PC clinic app is a bigger loss if data is lost than a single desktop.
- Keep the last N backups; surface "Last Backup" date/size on the client dashboard (same UX as OPD Clinic's Dashboard cards).

## 9. Auto-Update System

Two independent pieces, both using GitHub Releases (public repo, same as OPD Clinic — private repos return 404 to unauthenticated API calls):

1. **Client updater** (each WPF PC): near-identical to OPD Clinic's Phase 21 — checks `GET /repos/{owner}/{repo}/releases/latest` on startup (with a cooldown file), compares `<Version>`, prompts, downloads the installer, runs it, relaunches. All the pitfalls already documented in OPD Clinic's memory (7-digit hex colors, file-stream-locked-at-Process.Start, `skipifsilent` blocking relaunch, three csproj version fields) apply directly here — reuse that checklist.
2. **Server updater**: since there's only one server instance, this can be simpler — either (a) the same GitHub-release-based checker running as part of the API, which downloads a new self-contained build and stages it for the admin to apply during a maintenance window (auto-swapping a running Windows Service binary is messy), or (b) just a documented manual update procedure for now. Recommend starting with (b) and automating later if it proves annoying.

## 10. Client (WPF)

- Reuses proven OPD Clinic patterns: MVVM, `CommunityToolkit.Mvvm`, DialogHelper, idle-lock, keyboard shortcuts, Serilog + global exception handler, QuestPDF reports.
- New concern versus OPD Clinic: **network resilience**. The client now depends on the server being reachable. Needs:
  - Clear "Server unavailable" state instead of silent failure/crash.
  - Retry/backoff on transient network errors.
  - Explicit login-session expiry handling (refresh token flow).
- `HttpClient`-based `ApiService` layer replacing OPD Clinic's direct `AppDbContext` factory usage — this is the main structural difference in the client codebase.

### 10.1 UI Design System — Ocean Breeze (confirmed 2026-09-22) → superseded by "Clinical Teal" (2026-11-01)

> **Update 2026-11-01:** the user found the Ocean Breeze look "off" (white-on-white, royal blue, every item an outlined button) and chose **Clinical Teal** from a visual comparison. The palette and control styles below are **superseded** — see §20 for the current theme. What still applies from this section: Segoe UI, the control inventory, and the **Win7 rule that every icon must be a Viewbox+Canvas+Path vector, never a Segoe MDL2 glyph**.

**Confirmed: use the Ocean Breeze design system** (the `ocean-breeze-wpf-ui` skill — production-proven in PharmaCare Pro), rather than reinventing OPD Clinic's ad-hoc Styles.xaml or starting from scratch.

- **Palette:** light theme, `#F8FAFC` page bg / white cards, `#1D4ED8` accent blue (primary buttons, focus rings, title bars), `#0F172A`/`#334155`/`#64748B` text hierarchy, semantic Danger `#DC2626` / Success `#16A34A` / Warning `#D97706`.
- **Controls covered:** `ModernTextBox`, `ModernComboBox`, Primary/Secondary/Danger/Success buttons, `CardBorder`, `ModernDataGrid`, frameless dialog windows (colored title bar + drop shadow), page header bars, filter/toolbar bars, status badges, KPI/stat cards, a currency-column pattern.
- **Font:** Segoe UI (not OPD Clinic's Calibri) — fine on Win7 (ships since Vista); this app will look visually distinct from OPD Clinic/PharmaCare Pro's exact shade only in the accent hex (`#1D4ED8` vs OPD Clinic's `#1565C0`), not a technical concern, just noting it won't be pixel-identical to either sibling app.

**⚠️ Critical adaptation required — Win7 icon compatibility:** Ocean Breeze's documented icon usage is plain `Segoe MDL2 Assets` glyphs (`&#xE721;` etc.) rendered as `TextBlock` text. OPD Clinic already hit this exact failure mode — those glyphs silently fail to render on Windows 7 — and fixed it by replacing all 35 glyphs with `Viewbox+Canvas+Path` vector icons (Material Design paths). Since this client also targets net6.0-windows for a Win7/8 PC:
- Every Ocean Breeze icon (search, calendar, chevron, add, delete, edit, refresh, print, save, etc.) must be built as a `Viewbox+Canvas+Path` vector icon from the start, not as an MDL2 glyph — bake this into the Phase 2 (bilingual shell) or an early "icon set" task, not retrofitted after most views are built.
- Apply this to every place Ocean Breeze's reference XAML shows a glyph: ComboBox chevron, DatePicker calendar toggle, dialog close button, nav icons, toolbar buttons.
- Everything else in Ocean Breeze (color brushes, control templates, dialogs, cards, DataGrid) is pure vector/XAML and Win7-safe as documented — no changes needed there.
- Same title-bar-icon caveat as OPD Clinic applies separately (BMP-only `.ico`, `Window.Icon` class handler) — unrelated to Ocean Breeze itself, still needed for the app's own window/taskbar icon.

## 11. Deployment

- **Server PC**: one installer sets up PostgreSQL (or a documented prerequisite install) + the API Windows Service + firewall rule. Inno Setup, same tool as OPD Clinic.
- **Client PCs**: a separate, lighter installer (just the WPF app), same GitHub-release auto-update flow as OPD Clinic.
- **Confirmed: single public repo, two release tag prefixes** — `HamedRawand/Xray-Clinic` (repo name finalized 2026-09-22) with tags like `client-vX.Y.Z` and `server-vX.Y.Z`. The client updater's `GET /repos/{owner}/{repo}/releases/latest` call needs to filter by tag prefix (or use a dedicated "latest client release" query) rather than trusting GitHub's single `/latest` endpoint, since that endpoint only ever returns the most recently published release regardless of which component it's for — worth flagging as an implementation detail, not just picking the naming convention.

## 12. Suggested Build Phases

1. **Foundation** — Postgres schema, EF Core migrations, API skeleton, JWT auth, user/role/permission model, seed admin account.
2. **Bilingual shell** — `LanguageService` toggle, EN/Dari resource dictionaries, RTL FlowDirection plumbing, `AfghanShamsiDatePicker` wired into the client shell early (retrofitting bilingual support later touches every view, as it did in OPD Clinic — cheaper to build it into the shell from phase 2 than bolt on afterward).
3. **Core clinic workflow** — patients, exam orders, reports (incl. §6.2's template-driven measurement fields for echo/ultrasound), referring physicians; WPF client screens calling the API.
4. **PDF reports** — QuestPDF report generation (reuse OPD Clinic's PdfService patterns, incl. Shamsi date formatting).
5. **Billing** — `ExamType` pricing, `Invoice`/`Payment` entities and API endpoints, invoice screen + payment recording, receipt PDF.
6. **User management & audit** — admin CRUD, custom roles, audit log viewer.
7. **Backup/restore** — pg_dump-based encrypted backup, manual + scheduled, restore flow.
8. **Auto-update** — client updater first (higher value, more PCs), then server update process.
9. **Hardening** — network resilience, HTTPS/cert setup, idle lock, lockout policy, router-based VPN + RDP remote-access setup for the server PC, load-test with all clinic PCs open at once.
10. **Workflow redesign (2026-11-01)** — A: exams register; B: status workflow, queues, permission-aware UI; C: reception check-in; D: dashboard, statistics, Excel export. See §15.
11. **Refinements (2026-11-01)** — pricing snapshot, discounts/free exams, AFN currency, paging and search on every list, patient exam count / last visit, invoice totals + export, Clinical Teal theme. See §16–§20.

## 13. Open Questions

None outstanding for the built system. Deliberately **deferred** ideas (raised during the 2026-11-01 work, not needed yet):

- Discounts **after** payment (needs a refund/credit-note flow) and discount **limits / admin approval thresholds** (e.g. reception up to 20 %, larger needs admin) — the user chose "reception too, with a required reason".
- Editing an exam's **list price** per exam (only discount/free exists).
- Making the **currency configurable** (it is a single constant, `Currency.Code = "AFN"`).
- A **server-side installer / Windows Service package**, an **app icon**, an **x86 build** for the Win7/8 PC, and a native-speaker **Dari review** — see §24.

---

## 14. Phase 9 Hardening — what shipped (2026-09-22)

- **Silent token refresh**: expired access tokens are now transparently refreshed and the
  original request retried — previously every action just failed with no recovery once the
  30-minute access token expired. Live-verified with a temporarily-shortened 1-minute token.
- **JWT clock skew tightened** from the 5-minute library default to 30 seconds — `Jwt:AccessTokenMinutes`
  now actually reflects the token's real accepted lifetime.
- **Idle lock**: the whole app (every open window, not just the main one) locks after 10 minutes
  of no mouse/keyboard input; unlocking requires the current user's password via a dedicated
  `POST /api/auth/verify-password` endpoint that doesn't touch session tokens or lockout state.
  Live-verified with a temporarily-shortened 20-second threshold.
- **HTTPS with trust-on-first-use certificate pinning**: a self-signed cert (5-year validity,
  bound to the server's actual hostname) can be enabled via `Https:Enabled`/`Https:PfxPath`
  config. The client never blindly trusts any self-signed cert — `ServerSettingsWindow` shows the
  presented certificate's fingerprint and requires an explicit "Trust This Certificate" click
  before pinning it; every connection after that is validated against that exact thumbprint, not
  just "any cert this server happens to present." HTTP stays available in parallel (no forced
  redirect) so clients can opt into HTTPS one at a time rather than an all-or-nothing cutover
  breaking anyone who hasn't gone through the trust step yet. Live-verified end-to-end.
- **Load test**: 20 concurrent simulated clinic PCs (well beyond the real 2–5 PC scale) each
  logging in and doing 8 rounds of list+create against the real API and Postgres — 340/340 calls
  succeeded, 0 errors, all 235 resulting records had unique auto-generated codes (confirmed no ID
  collisions or lost writes under concurrent load), completed in ~3 seconds.
- **Lockout policy**: already built and verified in Phase 1/6 — no new work needed here.

### 14.0 Server package (built 2026-11-02 — see `DEPLOYMENT.md` for the runbook)

`deploy\server.iss` builds `DiagnosticClinicManager_Server_Setup_v<ver>.exe` (self-contained API, no .NET needed on the server). It runs `deploy\Install-Server.ps1`, which:
installs PostgreSQL 17 silently if absent (EnterpriseDB installer, signature checked) → creates the `clinic` database + its own login → generates the JWT secret, the backup encryption password and a random initial `admin` password
→ issues a 5-year self-signed certificate (SANs: host name, FQDN, localhost, all LAN IPs) → writes `%ProgramData%\DiagnosticClinicManager\server.settings.json` (admins + service only) →
registers the `DiagnosticClinicManagerServer` service (delayed auto-start, depends on PostgreSQL, auto-restart on crash, **Local Service** account) → firewall rule for TCP 5443 from the local subnet only → starts it and
health-checks it over HTTPS. Re-running a newer installer is the **upgrade** path (keeps DB/secrets/certificate, takes a `pre-upgrade-*.dump` first).
API changes that made this possible: `UseWindowsService`, optional external `server.settings.json` (env var `DCM_SERVER_SETTINGS` overrides its path), `Seed:InitialAdminPassword` (the published default `Admin@1234` is used only on dev machines), API logs moved to `%ProgramData%`, API assembly renamed `DiagnosticClinicManager.Server`, version 1.1.0.
**Verified 2026-11-02:** published server run against the dev DB with generated cert + settings file (HTTP + HTTPS, thumbprint matches); elevated dry-run on the dev PC (temporary service `DcmTestServer`, ports 6080/6443): fresh install, restart, upgrade pass with safety dump — 25/25 checks (service account, auto-start, PostgreSQL dependency, failure actions, firewall scope, folder ACLs, settings/secrets/cert files, seed password removed, clean uninstall).
**Server 1.1.1 (2026-11-02, readiness pass):** production now REFUSES to seed the administrator without `Seed:InitialAdminPassword` (the published default `Admin@1234` works only in the Development environment); backup/restore failures are logged server-side.
Verified on a throw-away PostgreSQL cluster (no admin needed): empty DB → all 7 migrations apply, generated admin password signs in / default rejected, encrypted backup via the API, restore rehearsal (wrong password refused, data returns), guard test (Production refuses, Development still seeds, Production + seed works).
Verified in an elevated run: the service as **Local Service** creates and restores backups itself, survives a restart and reports 1.1.1 (19/19).
One unexplained HTTP 400 on the very first service restore attempt did not reproduce (the rerun and 24 non-elevated backup/restore rounds all passed) — restore failures are now logged so a recurrence is diagnosable. The first start of a freshly installed service can take minutes (antivirus scan); the setup script now says so.
Known minor: two backups made in the same second share one file name (the second overwrites the first).
**Server 1.1.2 (2026-11-02, first real-PC test):** on the user's own Windows 11 PC (build 26300) the service crashed on start as **Local Service** with `CryptographicException: Access denied` while importing the HTTPS .pfx (its key-store write was refused); run by hand as admin the same server was fine. 1.1.2: (1) `ServerCertificate.Load` tries the account's key store, then the machine key store, then memory-only; (2) setup retries with the **System account** when the service cannot start and later runs keep that account; (3) the passwords/superuser password are written to `INSTALL-SECRETS.txt` as soon as they exist, and a later run recovers them from a still-present `Seed` section (a failed first run no longer loses them); (4) unhandled startup exceptions are written to the server log. Verified by an elevated test that denies Local Service the .pfx (16/16: normal install, recovery to LocalSystem, later upgrade keeps it, passwords recovered). Not reproducible here: the exact key-store failure itself, so whether the new loader alone would have fixed the user's PC is unproven — the System-account fallback is the guaranteed part.
**NOT verified (needs a clean PC):** the silent PostgreSQL install itself (the `--superpassword … --unattendedmodeui none` flag set is the documented BitRock/EDB one but could not be exercised here), the Inno wizard end to end, and reboot auto-start.

### 14.1 Still manual (not automatable from a dev session) — checklist for the real deployment

- [ ] Confirm the clinic's actual router supports VPN server mode (WireGuard/OpenVPN) — see §7.
      If not, fall back to the Windows VPN server role or a remote-support tool.
- [ ] Once the server PC is physically deployed, regenerate the self-signed HTTPS certificate
      bound to *that* machine's real hostname/IP (the one made this session is bound to the dev
      machine's hostname and isn't valid for the real server).
- [ ] Each clinic PC does its own one-time "Trust This Certificate" step in Server Settings after
      the real server is deployed — this is by design (TOFU), not a gap to close later.
- [ ] Decide whether to eventually re-enable `app.UseHttpsRedirection()` once every clinic PC has
      completed the trust step, to stop accepting plain HTTP entirely.
- [x] (done 2026-11-02, client-v1.1.0) Everything in §15–§23 was uncommitted — when releasing, the user names which parts to commit; then cut `client-v1.1.0` (three-field version bump + `installer.iss` + two-step `gh release create` / `upload`).
- [ ] (added 2026-11-01) After upgrading, **users must sign out and back in** to receive the new `ApplyDiscount` permission claim; custom roles need it ticked in Custom Roles.
- [ ] (added 2026-11-01) The database migrates itself on API start (`AddExamRegister`, `AddExamWorkflow`, `AddExamPrice`, `AddExamDiscount`) — take a backup first on the real server.

---

## 15. Workflow redesign — steps A–D (built and user-tested 2026-11-01)

**Why:** with ~30 patients/day (some with several exams) the app was patient-centric: an exam could only be reached by
opening its patient, lists were silently capped at 500 rows, nothing could count "Chest X-rays today", and a visit took
five separate screens. The redesign adds a searchable, all-history exams register and a reception-first flow.

| Step | What | Key pieces |
|---|---|---|
| **A** Exams register | Cross-patient list of every exam, searchable and filterable, paged, with counts | `GET /api/exam-orders/search`; filters: search (name / patient ID / phone / exam no.), period presets (Today … All time, Shamsi-aware), exam type, modality, status, referring physician, billing (not invoiced / unpaid / paid / discounted / free); per-exam-type count chips; `ExamNumber` (`E-000123`); `ClinicClock` (clinic-local days); indexes on date / type / status / patient |
| **B** Status & queues | Exam lifecycle buttons, role queues, permission-aware UI | §16 |
| **C** Check-in | One reception window: find or register the patient (duplicate warning), tick several exams, pick the doctor once, discounts, invoice, payment, receipt | `CheckInWindow`; one transactional `POST /api/check-in` (patient + exams + invoice + payment all-or-nothing); `GET /api/patients/possible-duplicates` (same phone, or the name — a single word must match exactly, several words must all appear in any order) |
| **D** Reporting | Dashboard, statistics, Excel export | §19 |

**Clinic time:** `ClinicClock` (`Clinic:TimeZoneId`, default `Asia/Kabul`, fixed UTC+4:30 fallback because Afghanistan has no DST)
converts the clinic-local calendar dates the client sends into UTC bounds; the client converts UTC timestamps to the PC's local
clock for display (`CalendarFormat`). Before this fix every date-time column was ~4.5 h behind.

## 16. Exam lifecycle, queues and permission-aware UI

- **Transitions (enforced server-side, `PUT /api/exam-orders/{id}/status`):** Pending → InProgress | Cancelled;
  InProgress → Completed | Pending | Cancelled; Completed → InProgress (reopen). **Reported** (set by finalizing the report) and
  **Cancelled** are terminal; a finalized report only changes through the audited Amend flow.
- **Completing** an exam records `PerformedById/At` (cleared on reopen). **Cancelling** needs a reason (kept in the audit log) and is refused
  if the exam is already invoiced; cancelled exams are excluded from invoicing, reports and statistics counts.
- **Queues** (one-click filters on the Exams screen, all-time, with live counts from `GET /api/exam-orders/queue-counts`, refreshed every 30 s):
  Waiting for technician (Pending), In progress, Waiting for report (Completed), Needs billing (not invoiced, not cancelled), Unpaid.
  Radiologists land on *Waiting for report*, technicians on *Waiting for technician*, admins on the Dashboard, reception on Patients.
- **Permission-aware UI:** sidebar items and buttons the user can't use are hidden (the server remains the enforcement point); the report
  window is read-only for users without `CreateReport` / `FinalizeReport` / `AmendReport`.
- **Exams grid (default window ≈ 920 px of grid):** one primary workflow button per row (Start / Complete / Reopen) plus a **⋯** menu
  (Open report, Open patient, Discount, Cancel exam — built per row from status and permissions). *Performed by* is a tooltip on the status badge;
  the referring physician is a tooltip on the exam type (both are in the Excel export).

## 17. Pricing, discounts and currency

- **Price snapshot:** `ExamOrder.Price` copies the exam type's price when the exam is booked; invoices, statistics and receipts use it, so later
  list-price changes never rewrite history. Migration `AddExamPrice` backfilled existing exams (exam type price, or the invoice total for an exam alone on an invoice).
- **Discounts / free exams (user decision: reception may give them, with a required reason):** new permission **`ApplyDiscount`**
  (Admin + Receptionist by default; appended last in the enum so stored custom-role lists stay valid). Per exam: `DiscountAmount`, `DiscountReason`,
  `DiscountedById`, `DiscountedAt`; charged amount = `Price − DiscountAmount`; free = discount equals price. Set at check-in (per-exam *Charge* box,
  *Free* button, reason) or before invoicing via `PUT /api/exam-orders/{id}/discount` (`ExamDiscountDialog`). Rules: reason required, charge between 0 and list price,
  refused once invoiced or cancelled, stale `RowVersion` → 409, audit entries `ExamDiscountApplied` / `ExamDiscountRemoved`.
  Accountability: statistics/dashboard show discounts given, discounted and free exam counts and a **discounts-by-staff-member** table;
  the register can filter Discounted / Free.
- **Invoices:** `Total` = Σ charged amounts; lines carry `ListPrice`, `DiscountAmount`, `DiscountReason`; the receipt PDF and invoice detail show the discount and reason;
  an all-free visit gets a 0 total invoice already `Paid`.
- **Currency:** `NasimXray.Shared.Currency` (`Code = "AFN"`, `Format(decimal)` → `1,500.00 AFN`, invariant culture) is the only formatter — used by all views,
  `MoneyConverter` in XAML, the PDF receipt, server messages and audit details; Excel money cells stay numeric with number format `#,##0.00 "AFN"`; amount input labels say "(AFN)".

## 18. Lists, paging and search

The old silent `Take(500)` cap is gone. `PagedResult<T>` (+ `Paging.Normalize`: page clamped to 1..last, page size ≤ 200, default 50) and a shared
`PagerControl` ("Page x of y · N records", hidden when everything fits on one page) serve:

| List | Search / filters |
|---|---|
| Exams register (§15) | see above |
| Patients | name / patient ID / phone; shows **Exams** (non-cancelled count) and **Last visit** columns |
| Invoices | patient name / patient ID / invoice no. (`12` or `#12`), status; **footer totals** (Total, Paid, Balance) over *all* matching invoices (`InvoiceListResult` sums), **Export to Excel** (sheet "Invoices" with totals row + sheet "Payments") |
| Audit log | text (user / action / details), action drop-down (`GET /api/audit-logs/actions`), clinic-local date range |

## 19. Reporting and Excel export

- **Dashboard** (`GET /api/stats/dashboard`): today's exams / patients / new patients, the five queue sizes (cards open that queue), exams by type and status, and — only for users with
  `ViewBilling` — money collected today (cash / card / other), invoiced today, outstanding balance, discounts and free exams.
- **Statistics** (`GET /api/stats/report?dateFrom&dateTo`, ≤ 1100 days): totals, breakdowns by exam type / modality / referring physician / day, cash by staff member, discounts by staff member;
  period presets follow the active calendar (this month / this year in Shamsi when selected). Aggregation is done in memory because grouping by clinic-local day can't be expressed in SQL.
  Money fields are `null` without `ViewBilling`.
- **Excel** (ClosedXML 0.102.3, `ExcelExportService`): register export (all filtered rows, cap 50 000), statistics workbook (summary + one sheet per breakdown), invoices export (cap 20 000).
  Sheets are right-to-left in Dari; dates are text in the active calendar; money/count cells are real numbers.

## 20. UI theme — "Clinical Teal" (replaces Ocean Breeze)

Single palette file `Resources/Colors.xaml`; every style uses `DynamicResource` brushes (retune the whole app there).

- Dark teal sidebar `#0E3B43` (active item `#16626E` with a `#5EEAD4` bar; flat `NavButton` style, `Tag="Active"` set by `MainWindow.ShowContent` according to the view shown;
  the bright `NavPrimaryButton` is *+ New Check-in*), cool grey-blue page `#EEF3F5`, white cards, accent **cyan `#0E7490`** (deliberately not green, so it can't be confused with the green of "paid"/"reported"),
  thin 1 px `#CBD5E1` inputs that turn accent on focus, text `#0F172A` / `#475569` / `#64748B`.
- **Status badges** (`StatusBadge` control + `StatusToBadgeKindConverter`): Pending amber, In progress cyan, Completed blue, Reported / Paid green, Cancelled grey, Unpaid red, Partly paid amber.
- Themed `CheckBox`, `ComboBoxItem`, `ListBoxItem`, `ContextMenu` / `MenuItem`; one shared `ModernDataGridCell` (text vertically centred, accent tint when selected); a `DangerButton` with its own hover;
  the Windows system highlight brushes are overridden so Windows-blue selection never appears.
- Fixes found while building it: text inside inputs was indented twice (template margin + automatic padding); resource strings with trailing spaces need `xml:space="preserve"`;
  fixed-width columns made star columns collapse below ~1400 px, so grids are sized for the default window (≈ 920 px of grid).
- PDF accent colour and Excel header fill follow the palette.

## 21. API surface as built

`auth` (login, refresh, change-password, verify-password) · `patients` (paged list with exam count / last visit, get, create, update, **possible-duplicates**) ·
`exam-orders` (legacy list by patient/status/uninvoiced, **search**, **export**, **queue-counts**, get, create, **PUT {id}/status**, **PUT {id}/discount**, and `{id}/report` get / save draft / finalize / amend) ·
**`check-in`** (POST) · **clinic profile** (public `GET system/clinic`, `GET system/clinic/logo`; `PUT clinic`, `PUT/DELETE clinic/logo`) · `invoices` (paged list with sums, **export**, get, create, `{id}/payments`) · **`stats`** (dashboard, report) · `audit-logs` (paged, **actions**) ·
`users`, `roles`, `exam-types` (+ `measurement-fields`), `referring-physicians`, `backup` (list / create / restore), `system/version`.

**Permissions** (`Permission` enum): ViewPatients, ManagePatients, ViewExamOrders, ManageExamOrders, ViewReports, CreateReport, FinalizeReport, AmendReport, ViewBilling, ManageBilling,
ProcessPayment, ManageUsers, ManageRoles, ManageExamTypes, ManageReferringPhysicians, ManageBackup, ViewAuditLog, **ApplyDiscount**, **ManageClinicSettings**.

## 22. Data-model changes since Phase 9 (EF migrations, all applied automatically at API start)

`InitialCreate`, `AddClinicalWorkflow` (Phase 3) → `AddExamRegister` (`ExamNumber` + indexes) → `AddExamWorkflow` (`PerformedById/At`, `CancelReason`) → `AddExamPrice` (`Price`, backfilled) → `AddExamDiscount` (discount columns) → `AddClinicProfile` (single-row `ClinicProfiles` table; **no data inserted by the migration** — the seeder creates a neutral "My Clinic" row).
`ExamOrder.RowVersion` (Postgres `xmin`) keeps protecting concurrent edits. The app's database role cannot create databases (least privilege) — keep it that way.

## 23. Verification approach and known limitations

- Server logic was verified with throwaway harness programs that call the real controllers: **read-only** checks against the dev database (paging, sums, counts, filters), rolled-back transactions
  (pricing/stat checks), and — for check-in, which opens its own transaction — a **throw-away schema** (`scratch_discount_test`) that was dropped afterwards. A clean `dotnet build` does not catch missing DI registrations or view-load errors.
- UI verification without logging in: a harness references the Client project, merges the real resource dictionaries, instantiates **every** view/dialog (null API client) to catch XAML/resource errors, renders views to PNG
  (`RenderTargetBitmap`) and generates sample Excel/PDF files for inspection.
- **Known limitations:** Dari text is machine-assisted; the Exams/Dashboard/Statistics screens were verified by rendering with sample data and by the user, not with a stub server; discounts after payment and discount limits are not built;
  `Currency` is a constant; the stray `ARCHITECTURE_PLAN.html` next to this file is an old auto-generated preview (git-ignored) and is **out of date** — this markdown file is the source of truth.

## 24. What remains

0. ~~Product naming~~ — done 2026-11-02 (§25): "Diagnostic Clinic Manager"; only the GitHub repo name is left to decide at release time.
1. **Real-server deployment** (§14.1): router VPN support, regenerate the HTTPS certificate on the real server PC, server installer / Windows Service, firewall rule, first backup, then each clinic PC's one-time "Trust This Certificate".
2. **Native-speaker review of the Dari text** (all new screens included).
3. **App icon** (BMP-only `.ico` + `Window.Icon` class handler for Win7, per §10.1) and an **x86 build** if the Win7/8 PC is 32-bit.
4. ~~Commit and release~~ — done 2026-11-02: five commits pushed to master, `client-v1.1.0` released (installer `DiagnosticClinicManager_Setup_v1.1.0.exe`, 54.8 MB).
5. Optional later: discounts after payment (refunds), discount limits/approval, per-exam list-price edits, configurable currency.

## 25. Clinic profile — one installation per clinic (built 2026-11-01)

**Decision (user):** each clinic has its own installation (own server PC, own database); a new clinic installs the app and enters its own name once.
Multi-tenant hosting of several clinics in one database was considered and **rejected as out of scope** (every table would need a clinic id).

- **Data:** one-row table `ClinicProfiles` — English name (required), Dari name, tagline (the line under the name on printouts, e.g. services offered), address, phone, email,
  optional logo (PNG/JPEG ≤ 300 KB, validated by file signature), `IsConfigured`, `UpdatedAt`, `xmin` row version. A fresh install is seeded with the neutral placeholder "My Clinic"
  (never a real clinic's name — the migration inserts nothing).
- **API:** reading is **public** (`GET /api/system/clinic`, `…/logo`) because the login screen shows the clinic name before anyone signs in (none of it is secret);
  changing it needs the new permission **`ManageClinicSettings`** (`PUT /api/clinic`, `PUT/DELETE /api/clinic/logo`), audited (`ClinicProfileUpdated`, `ClinicLogoUpdated/Removed`).
- **Client:** `ClinicInfoService` loads the profile (and caches it on disk so the right name shows even when the server is briefly unreachable), replaces the application-level
  `App.Title` resource — so the window titles, top bar, login screen and **every message-box title** change live — and follows the UI language (English UI → English name, Dari UI → Dari name,
  falling back to the other). The main window refreshes every 2 minutes, so other PCs pick up a change within a couple of minutes. `ClinicLogoImage` shows the logo in the top bar and on the login screen.
- **Clinic Settings screen** (sidebar, admins by default): the fields above, logo choose/remove, live preview. Until the profile has been saved once, admins see a yellow
  "set up your clinic" banner with an *Open Clinic Settings* button.
- **Printouts:** the letterhead of reports and receipts is built from the profile — optional logo, English and Dari names side by side, tagline, and an address · phone · email line;
  it degrades gracefully (English only, or the neutral name when nothing is configured). The old hard-coded "X-ray · Ultrasound · Echocardiography" line is now just the clinic's tagline.
- **Product name: "Diagnostic Clinic Manager"** (user decision, 2026-11-02). Renamed: the Inno Setup installer (`installer.iss` — app name, shortcuts, install folder `DiagnosticClinicManager`,
  output `DiagnosticClinicManager_Setup_v…`; the `AppId` GUID is **unchanged** so existing installs upgrade in place, and `[InstallDelete]` removes the old `NasimXray.Client.*` files), the client exe
  (`AssemblyName` → `DiagnosticClinicManager.exe`, plus product / title / description file properties), the neutral fallback title (`App.Title`, `ClinicInfoService`), the updater's user-agent and temp-file name,
  and the per-machine data folder (`%LocalAppData%\DiagnosticClinicManager` for client settings / logs / clinic cache and API logs, `%ProgramData%\DiagnosticClinicManager\Backups` for the default backup folder).
  `NasimXray.Shared.AppFolders.Resolve()` is the single place that builds those paths and **moves an existing `NasimXrayClinic` folder across once**, so no one loses the server URL, trusted-certificate pin or backups.
- **Deliberately NOT renamed:** the internal project / namespace names `NasimXray.*` (~500 occurrences, invisible to users, zero benefit); the JWT `Issuer` / `Audience` strings (renaming would sign everyone out and
  must stay in step with each server's config); the GitHub repo / update feed `HamedRawand/Xray-Clinic` (renaming a repo is outward-facing — GitHub redirects the old name, so installed clients keep updating either way;
  decide at release time); the solution folder name. Domain terms (X-ray / ultrasound / echo modalities, the Radiologist role) remain; they are data or generic enough for any diagnostic clinic.
- **Upgrade note:** after upgrading, the existing clinic shows "My Clinic" until an admin signs out and back in (to receive `ManageClinicSettings`) and enters the real name in Clinic Settings.

---

*Phases 1–9 were built and live-verified on 2026-09-22; the workflow redesign, pricing/discounts, AFN, paging, reporting/export and the Clinical Teal theme were built and user-tested on 2026-11-01.
What remains is deployment-specific (§24).*
