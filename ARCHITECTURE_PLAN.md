# Nasim Diagnostic X-ray Clinic — Architecture Plan

**Status:** Draft v4 — Phases 1–8 (Foundation, Bilingual Shell, Core Clinic Workflow, PDF Reports, Billing, User Management & Audit, Backup/Restore, Client Auto-Update logic) built 2026-09-22. Phase 8's live GitHub-release flow is not yet end-to-end tested — the real `HamedRawand/Xray-Clinic` repo doesn't exist yet (deliberate: verified via logic tests instead this session). Next: Phase 9 (hardening), or set up the real repo/installer to close out Phase 8 fully.
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

### 5.2 API surface (initial cut)
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
  - `Report` (findings, impression, reported-by, links to `ExamOrder`, **`IsFinalized bool`, `FinalizedAt`, `FinalizedById`**) — confirmed: reports lock on finalize (see §6.1 below)
  - `ReportAmendment` (report id, changed fields/diff or full snapshot, amended-by, amended-at, reason) — audit trail for any post-finalization change
  - `MeasurementFieldDefinition` (exam-type id, field key, label, unit, data type, display order, is active) — admin-configurable structured fields per `ExamType`, added for echo/ultrasound (see §6.2)
  - `ReportMeasurement` (report id, field-definition id, value) — the actual measurement values entered on one report
  - `Invoice` (patient + one or more `ExamOrder`s + total + status [Unpaid/Paid/Partially Paid])
  - `Payment` (invoice + amount + method [Cash/Card/etc.] + received-by + timestamp)
- No DICOM/image BLOBs per your answer — if a report needs to reference an external image, store a path/ID pointing at wherever the X-ray, ultrasound, or echo machine's own software keeps it, not a copy of the file. Applies equally across all three modalities.
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

### 10.1 UI Design System — Ocean Breeze (confirmed)

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

## 13. Open Questions

None outstanding — all planning questions from the last review are resolved. One pre-Phase-9 verification item: confirm the clinic's actual router model supports VPN server mode (WireGuard/OpenVPN); if not, fall back to the Windows VPN server role or a remote-support tool per §7.

---

*All architectural decisions are settled — next step is to scaffold the solution (API + WPF client projects, EF Core model, and the Postgres schema) and start Phase 1.*
