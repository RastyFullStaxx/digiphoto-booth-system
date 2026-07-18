# DigiPhoto Booth System — Project Overview

## What we are building

DigiPhoto Booth System is a Windows-first, offline-capable photobooth platform for
professional photography businesses. It captures from a certified camera, guides a
guest on a paired tablet, composes the result through an editable event template,
prints without a browser dialog, and publishes the session to a private QR gallery.

Our photography business is the first permanent tenant. After internal operation and
an invite-only pilot, the same production system becomes a per-active-booth SaaS sold
with a certified hardware kit. Selling the product does not end our own use and does
not transfer the source code or IP exclusively.

The complete locked scope and acceptance gates live in `IMPLEMENTATION_PLAN.md`.

## The problem

Typical photobooth stacks split camera control, layout design, print handling,
payments, and delivery across unrelated tools. That produces fragile event setups,
manual reconciliation, inconsistent output, and no reliable recovery when a cable,
printer, process, or internet connection fails.

This system treats the booth as one recoverable workflow. The camera capture, final
render, print submission, guest payment, and gallery upload each have explicit,
persisted states and idempotency boundaries.

## People and operating context

### Guest

A first-time, non-technical user at an event. They may be in a dim reception or a
bright ballroom, are usually standing, often share the frame with a group, and need
large touch targets, plain language, and a fast path from start to print.

### Booth operator

A photographer or trained staff member responsible for event readiness, camera and
printer health, payment exceptions, reprints, and safe recovery. They need precise
status and explicit intervention paths, not decorative analytics.

### Photography-business owner

The tenant administrator who creates events and packages, publishes templates,
pairs devices, invites staff, reviews operations, manages retention, and handles
billing or privacy requests.

### Platform operator

Our internal support and SaaS team. They manage tenant onboarding, licensing,
deployment health, audited support access, subscriptions, and incident response.

## Product surfaces

1. **Guest kiosk** — responsive React UI on the paired tablet or booth display.
2. **Local operator console** — React UI hosted by the Windows booth for readiness,
   recovery, history, and protected reprints.
3. **Template studio** — Fabric.js editor for staff-created 4x6 and paired 2x6
   layouts, reusable assets, photo slots, masks, text, and immutable publishing.
4. **Tenant portal** — event, package, device, staff, session, payment, gallery, and
   retention administration.
5. **Private gallery** — mobile-first bearer-link page for photo, GIF, and silent MP4
   viewing and download.
6. **Windows booth engine** — the hardware authority, local state machine, renderer,
   print queue, event-bundle cache, and synchronization outbox.
7. **Cloud monolith** — multi-tenant identity, configuration, private media,
   galleries, payment verification, subscriptions, licensing, and audit history.

## System boundary

```text
Certified Canon camera ─USB─> Windows booth engine ─USB/spooler─> DNP printer
                                  ▲          │
                                  │          └─ HTTPS idempotent outbox ─> Cloud
                           private booth LAN
                                  │
                            guest tablet

Guest phone ─ QR/HTTPS ─> private gallery
PayMongo ─ signed webhooks/API ─> cloud platform
```

The tablet never controls the camera or printer directly. The booth remains useful
without the cloud for free-event capture, review, rendering, printing, reprinting,
and local history. Automated payment requires internet and fails closed.

## Main guest workflow

```text
Attract
  → choose package and design
  → privacy/guardian confirmation
  → optional verified payment
  → live preview and countdown
  → capture required shots
  → bounded retake and selection
  → render and persist final output
  → serialized print
  → private gallery QR
  → automatic reset
```

Every consequential transition is persisted. Restart recovery resumes or safely
routes to staff without charging, unlocking, uploading, or printing twice.

## Architecture

### Booth

- .NET 10 WPF shell with WebView2 and an embedded ASP.NET Core local service.
- SQLite for event bundles, sessions, print jobs, device state, and outbox records.
- Canon EDSDK and Windows print-spooler adapters behind file-backed simulators.
- React, TypeScript, and Vite for guest, operator, and editor surfaces.
- Fabric.js 7, pinned exactly, for template editing and deterministic browser render.
- Native FFmpeg process using an LGPL-compatible build and Windows Media Foundation
  H.264 for GIF and short silent MP4 output.

### Cloud

- One ASP.NET Core 10 monolith with REST APIs under `/api/v1`.
- PostgreSQL, private object storage, ASP.NET Core Identity, tenant authorization,
  device credentials, galleries, PayMongo webhooks, subscriptions, and audit logs.
- Azure Southeast Asia deployment using App Service, PostgreSQL Flexible Server,
  Blob Storage, Key Vault, Application Insights, and transactional email.

### Synchronization

Configuration moves cloud-to-booth as checksum-verified published event bundles.
Sessions and media move booth-to-cloud through a stable-ID, content-hash,
idempotent outbox. There is no arbitrary two-way data merge.

## Core contracts

- `TemplateDocument` — version, Fabric major, target pixels, DPI, dataless canvas
  JSON, and managed asset IDs.
- `EventBundle` — published event, packages, privacy notice, templates, fonts,
  assets, and checksum.
- `SessionManifest` — state, template snapshot, capture and output inventory,
  SHA-256 hashes, consent record, and timestamps.
- `Heartbeat` — version, signed-license lease, sync, disk, camera, and printer health.

Cloud records center on `Tenant`, `Membership`, `Device`, `License`, `Event`,
`Theme`, `Package`, `Template`, `TemplateVersion`, `Session`, `Asset`, `Gallery`,
`PaymentAttempt`, `Subscription`, and `AuditLog`.

## Payment model

Guest payment is a per-event or per-package toggle. When active, the cloud creates a
unique, exact-amount, five-minute PayMongo QR Ph attempt and unlocks only from a
validated terminal webhook. Static personal QR codes cannot automate unlock.

Our booth initially uses our PayMongo merchant account. External SaaS tenants use
PayMongo Linked Accounts so guest revenue stays attributed to the tenant. SaaS
subscription billing is a separate parent-account path: manual PayMongo Payment Link
in the partner pilot, then one recurring subscription per active device at public
launch.

## Privacy and security baseline

- Private, revocable, non-indexed gallery per session with a random bearer token.
- Default 30-day media retention; only 7, 30, or 90 days are allowed.
- Separate unchecked consent for promotional or public-display use.
- Guardian confirmation and child-appropriate notice for minor sessions.
- No AI, facial recognition, biometrics, age inference, or media training.
- Tenant isolation in authorization, queries, tests, and storage paths.
- MFA for portal administrators, least privilege, audited support, encrypted
  transport/storage, secure local credentials, and explicit deletion workflows.

The product baseline does not replace Philippine legal and DPO review. Privacy and
child privacy impact assessments, processing agreements, incident response, and
registration analysis are launch gates.

## First executable vertical slice

The first runnable slice uses simulator drivers and proves the full shape of the
system without pretending hardware has been certified:

1. Create and publish a sample tenant, event, package, privacy notice, and template.
2. Pair a simulated booth and download a verified event bundle.
3. Run one free guest photo session through privacy, countdown, simulated capture,
   selection, render, simulated print, gallery token, and local reset.
4. Persist every state locally, restart at selected checkpoints, and recover safely.
5. Upload the manifest and output exactly once to the cloud.
6. Open the private gallery on a phone-sized browser.
7. Run a second session with payment enabled and prove success, duplicate, wrong
   amount, expired, and offline-fail-closed cases using a fake provider adapter.

This slice establishes the contracts and failure semantics used by real Canon, DNP,
and PayMongo integrations later.

## Delivery gates

- **A — hardware feasibility:** exact camera, printer, media, crop/cut, reconnect,
  paper-out, and 50-session proof.
- **B — internal booth:** simulator-proven state machine replaced by certified
  adapters, production template/render path, kiosk hardening, and restart recovery.
- **C — gallery and payment:** private delivery, resumable upload, retention, real
  PayMongo sandbox validation, and audited override.
- **D — partner SaaS pilot:** tenant isolation, device licensing, Linked Accounts,
  billing, signed installer, monitoring, and support runbooks for 3–5 businesses.
- **E — public SaaS:** self-service onboarding, recurring subscriptions, signed
  updates, disaster recovery, documentation, alerts, and support operations.

## Version-one exclusions

No macOS host, uncertified hardware, guest freeform editor, RAW pipeline, long video,
audio, Apple Live Photo package, public event album, automatic social posting, AI or
biometrics, static-QR validation, revenue sharing, coupons, metering, complex plans,
custom domains, full white-labeling, or microservices.

## Working glossary

- **Booth:** one paired and licensed Windows device controlling one camera and one
  serialized printer queue.
- **Event bundle:** immutable, locally validated configuration required before an
  event can start.
- **Session:** one guest journey from start through output or a terminal recovery
  state.
- **Template version:** immutable published layout snapshot; drafts remain editable.
- **Output:** flattened still, GIF, or silent MP4 created from a session.
- **Gallery:** private session-scoped bearer-link delivery page.
- **Payment attempt:** one exact-amount, expiring guest-payment authorization record.
- **Outbox:** durable local work queue for idempotent cloud synchronization.
