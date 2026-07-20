# DigiPhoto Booth System - Locked Implementation Plan

**Status:** Locked on 2026-07-19  
**Change control:** Any scope, platform, payment, hardware, privacy, or commercial-model change requires an explicit revision to this document before implementation.

## 1. Product and commercial model

Build a Windows-first, offline-capable digital photobooth system that our photography business uses permanently as tenant 1, then sell it as a turnkey hardware-and-SaaS product.

- Rollout: own booth, invite-only partner pilot, then public SaaS.
- Offering: certified turnkey hardware plus SaaS first; software-only support comes later through a strict certification list.
- Billing: one monthly subscription for each active paired booth device. Owner and staff accounts are included.
- Ownership: customers buy hardware and a SaaS license; source code and IP are not sold exclusively.
- Guest payments and SaaS fees are separate money paths. Version 1 takes no percentage of tenants' guest revenue.

## 2. System topology

```text
Canon camera ----USB----> Windows booth engine ----USB----> DNP printer
                              ^       |
                              |       +----HTTPS/outbox----> cloud platform
                              |
                    private booth LAN
                              |
                        guest tablet

guest phone ----QR/HTTPS----> private session gallery
PayMongo <----signed webhooks/API----> cloud platform
```

The Windows booth engine is the hardware authority. The tablet is a user-facing browser, not the camera or printer controller.

## 3. Guest session

1. Show an event-branded attract screen.
2. Guest selects photo or motion package, print design, and allowed copy option.
3. Show the privacy notice and participant or guardian confirmation.
4. When payment is enabled, create a unique five-minute PayMongo QR Ph attempt. Continue only after server-verified payment. Skip this step completely when payment is disabled.
5. Show mirrored live preview and the configured countdown.
6. Capture the number of shots required by the published template.
7. Allow one retake per shot by default.
8. Allow an operator-enabled high-five gesture trigger. Gesture recognition is a booth-engine input and invokes the same persisted capture command as the on-screen shutter; the browser never controls the camera directly.
9. After capture, show a timed Process step where the guest can select photos, replace a selected photo through the normal capture/review path, choose a published layout variant, and apply safe filters.
10. Render and save the final output before printing.
11. Print the package's configured copies without a browser print dialog.
12. Display a private gallery QR code.
13. Reset automatically after completion or inactivity.

Only one guest session and one serialized print queue may be active on a booth. Restart recovery must resume from persisted state without losing captures, charging twice, or printing duplicates.

## 4. Staff and tenant-owner functions

- Create events, packages, prices, schedules, output modes, print sizes, copy counts, timers, retake limits, filters, retention, and payment state.
- Upload logos, static backgrounds, attract-screen GIFs or short videos, and event colors.
- Start from an editable bundled template or a blank canvas.
- Add photo slots, text, tenant assets, shapes, masks, layers, headers, footers, safe areas, and an optional gallery QR placeholder.
- Publish immutable template versions to selected booth devices.
- Pair, activate, replace, revoke, and inspect booth devices.
- Invite staff and control tenant roles.
- Review sessions, payments, gallery status, device heartbeat, disk capacity, camera/printer health, and audit history.
- Reprint through a local operator-PIN-protected history screen.

Staff design templates. Guests do not receive a freeform Canva-like editor.

## 5. Media and print behavior

- Photo mode produces one or more full-resolution stills and a flattened print composite.
- Motion mode uses a short frame burst to produce an optimized GIF and a 2-3 second silent MP4 or boomerang.
- "Live Photo" means the looping MP4 plus a poster image. Apple Live Photo packaging, long video, and audio are excluded.
- Camera exposure is staff-only. Guest image controls are bounded filters rather than camera-setting sliders.
- Preview is mirrored by default; captures and final output are unmirrored by default.
- Originals are written before preview. Final output is written before print submission.
- Printer jobs are serialized. An ambiguous spooler result is surfaced to staff and is never automatically reprinted.
- Initial print profiles are 1200x1800 px 4x6 output and two 600x1800 px 2x6 strips on one 4x6 sheet.

## 6. Technology architecture

### Booth engine

- .NET 10 LTS, Windows 11 x64, WPF shell, WebView2, and an embedded ASP.NET Core local service.
- Canon EDSDK driver for the certified camera, a Windows print-spooler integration, and file-backed simulator drivers for development and automated checks.
- SQLite for device, event-bundle, session, print-job, and outbox state.
- Protected local files, Windows ACLs, BitLocker, and DPAPI-protected credentials.
- React, TypeScript, and Vite guest/operator UI served to WebView2 and the paired tablet.
- Fabric.js 7 pinned to an exact version for template editing, serialization, and WYSIWYG rendering.
- A pinned LGPL-compatible FFmpeg build using Windows Media Foundation H.264. No GPL or nonfree build is bundled.

### Cloud platform

- One ASP.NET Core 10 monolith. Do not split into microservices.
- Azure Southeast Asia: App Service, PostgreSQL Flexible Server, private Blob Storage, Key Vault, Application Insights, and transactional email.
- ASP.NET Core Identity with verified email, MFA for portal administrators, tenant roles, and device credentials.
- Private object storage with short-lived download URLs.
- REST APIs versioned under `/api/v1`.
- Public gallery route `/g/{token}`.
- Separate PayMongo webhook routes for SaaS billing and tenant guest payments.

### Core cloud records

`Tenant`, `Membership`, `Device`, `License`, `Event`, `Theme`, `Package`, `Template`, `TemplateVersion`, `Session`, `Asset`, `Gallery`, `PaymentAttempt`, `Subscription`, and `AuditLog`.

Every tenant-owned row includes `tenant_id`. Tenant isolation is enforced in authorization, database queries, tests, and object paths.

### Versioned contracts

- `TemplateDocument`: schema version, Fabric major version, pixel width, pixel height, DPI, canvas JSON, and managed asset IDs.
- `EventBundle`: published event, theme, packages, privacy notice, templates, fonts, assets, and checksum.
- `SessionManifest`: state, template snapshot, output inventory, SHA-256 hashes, consent record, and timestamps.
- `Heartbeat`: app version, lease status, last sync, disk capacity, camera state, and printer state.

Cloud-to-booth configuration is published and downloaded. Booth sessions and media move upward through an idempotent outbox. There is no general-purpose two-way merge.

## 7. Offline behavior

- A booth must download and validate its event bundle before an event.
- Capture, review, rendering, printing, reprinting, and local history work without internet.
- Gallery tokens are generated locally. If media is not uploaded, the gallery presents an upload-pending state and becomes available after synchronization.
- Uploads retry idempotently using stable item IDs and content hashes.
- Automated payment requires internet and fails closed during an outage.
- An owner-PIN override can allow cash or an emergency bypass, but requires a reason and creates an audit record.
- Device licenses use a signed offline lease and never interrupt an already active guest session.

## 8. Certified version 1 hardware

- ASUS NUC 14 Pro, Intel Core Ultra 5, 16 GB RAM, 1 TB NVMe, Windows 11 Pro x64.
- Canon EOS R50 with RF-S 18-45 mm lens and continuous AC coupler.
- DNP DP-DS620 regional model with 4x6 media and 2-inch cutting support.
- 10-inch Android tablet or iPad.
- Dedicated WPA2/WPA3 router or access point, verified USB data cables, SD-card backup, and 1500 VA UPS.

The exact DNP regional SKU, Windows driver, recurring media supply, warranty, and service availability in the Philippines are procurement gates. macOS hosting and other camera or printer models are separate certification projects.

## 9. Payments and subscriptions

### Guest payment

- Our own booth begins with our business's PayMongo merchant account.
- Each payable session uses a dynamic QR Ph code with an exact PHP amount and five-minute expiry.
- Signed webhooks verify timestamp, tenant account, provider ID, event ID, amount, currency, environment, and terminal success state.
- Provider event and payment IDs are unique. Duplicate delivery cannot unlock twice.
- Late payment after expiry does not reopen a new guest session; it is flagged for staff reconciliation or refund.
- Static personal GCash, Maya, or bank QR codes cannot automatically unlock the booth.

### Independent SaaS tenants

- External tenants onboard through PayMongo Linked Accounts before automated guest payments are enabled.
- Guest funds remain attributed to the tenant's child merchant account.
- The platform stores child account identifiers and webhook secrets, not tenant full-access API keys.
- The platform does not collect all client funds and manually redistribute them.

### SaaS billing

- Partner-pilot invoices use PayMongo Payment Links and a platform-managed `paid_until` date.
- Public launch uses one PayMongo recurring subscription per active device.
- Hardware is invoiced separately.
- The internal tenant is billing-exempt but runs the same production code path.

## 10. Galleries, privacy, and security

- Every session gets a private bearer URL with at least 256 bits of cryptographic randomness.
- No public event-wide gallery in version 1.
- Gallery and local media retention default to 30 days; tenants may choose only 7, 30, or 90 days.
- Expiry deletes originals, derivatives, thumbnails, temporary frames, local cache, and gallery records. Payment/accounting retention remains separate.
- Pages use HTTPS, no indexing, no referrer leakage, rate limits, revocation, and short-lived media URLs.
- Guests see the tenant identity, purpose, retention period, rights/contact details, and the fact that anyone holding the link can view it.
- Promotional, marketing, or public-display consent is separate and unchecked by default.
- Sessions involving a minor require guardian confirmation and a child-appropriate notice.
- No facial recognition, face matching, age inference, biometric templates, or training on guest media.
- Portal accounts use MFA and least privilege. Local maintenance PIN attempts are rate-limited and audited.
- Cross-tenant authorization, media access, payment events, deletion, and support access fail closed.
- Before partner pilot: complete privacy and child privacy impact assessments, incident-response procedure, tenant processing agreement, and Philippine legal/DPO review.

## 11. Delivery sequence

### Gate A: hardware feasibility

Prove the exact camera and printer combination before building broad UI: live view, trigger, JPEG download, power-cycle reconnect, 50 consecutive capture sessions, exact crop/cut alignment, copies, paper-out recovery, and reprint behavior.

### Gate B: internal booth

Deliver the state machine, certified camera integration, template editor/renderer, guest tablet flow, offline storage, kiosk lock, silent print path, and restart recovery for our business.

### Gate C: gallery and payment

Deliver private QR galleries, resumable upload, retention deletion, PayMongo sandbox states, duplicate/wrong/late webhook protection, and audited override.

### Gate D: partner SaaS pilot

Deliver tenant isolation, device licensing, tenant administration, Linked Accounts onboarding, manual monthly billing, health monitoring, signed installer, and support runbooks. Operate 3-5 turnkey partner businesses.

### Gate E: public SaaS

Deliver self-service onboarding, recurring per-device subscriptions, safe signed updates, public documentation, backup/restore drills, operational alerts, and customer support workflow.

## 12. Public-launch acceptance gates

- At least 500 completed real sessions across our booth and partner booths.
- At least 99% complete without developer intervention.
- No software-caused capture or final-output loss.
- No duplicate payment unlock or automatic duplicate print.
- Recovery proven after process, PC, camera, printer, tablet-network, and internet interruption.
- Free-event sessions complete entirely offline and synchronize exactly once later.
- Payment mode never unlocks from an unsigned, duplicate, wrong-amount, wrong-tenant, failed, or expired event.
- Physical 4x6 and 2x6 output matches preview and calibrated cut/bleed targets.
- Template save/reload preserves text, fonts, layers, masks, crops, filters, locks, and asset references.
- GIF and MP4 work in WebView2 and representative Android and iPhone browsers.
- Cross-tenant access tests fail for every portal/API/gallery/object-storage path.
- Retention and deletion are demonstrated end to end, including backup rotation.
- Guest UI is verified on the real tablet for touch targets, keyboard/focus, contrast, reduced motion, reconnect, and inactivity reset.

## 13. Explicitly excluded from version 1

- macOS booth engine.
- Uncertified cameras or printers.
- Full guest canvas editing.
- Long video, audio, 360 booths, and Apple Live Photo export.
- RAW editing pipeline.
- Public event albums and automatic social posting.
- AI or biometric media features.
- Static QR automatic payment validation.
- Revenue sharing, marketplace payouts, coupons, metering, and complex SaaS plans.
- Custom gallery domains and complete white-labeling.
- Microservices or speculative abstractions for future hardware.

## 14. Design and engineering execution policy

1. Sol 5.6's product and design judgment is primary.
2. Ponytail full mode enforces the smallest root-cause solution, native capabilities first, and no speculative abstractions.
3. Impeccable, Anthropic Frontend Design, and Taste Skill are secondary critique lenses. They may challenge a decision but do not replace product context or the locked plan.
4. Design Motion Principles governs purposeful interaction feedback and responsive motion. Every animation must explain hierarchy, feedback, or state change and honor reduced motion.
5. The guest surface prioritizes speed, legibility, touch ergonomics, recovery, and event lighting over decorative novelty.
6. The owner portal prioritizes clear operational state, safe defaults, and low cognitive load.
7. Context7 supplies current framework and SDK documentation before implementation choices.
8. Playwright verifies responsive behavior, accessibility structure, console/network health, and critical browser flows.
9. The optional agent-browser package is not installed while Playwright provides the required coverage.
10. Security, payment correctness, data-loss prevention, privacy, accessibility, and physical calibration are never simplified away.

## 15. Primary implementation references

- .NET support: https://dotnet.microsoft.com/en-us/platform/support/policy
- Canon camera SDK: https://www.usa.canon.com/support/sdk
- DNP DS620: https://dnpphoto.eu/product/dye-sublimation-printers/ds620/
- Fabric.js: https://fabricjs.com/docs/core-concepts/
- FFmpeg license: https://ffmpeg.org/doxygen/7.0/md_LICENSE.html
- PayMongo QR Ph: https://docs.paymongo.com/docs/payment-acceptance-qr-ph-api
- PayMongo Linked Accounts: https://docs.paymongo.com/docs/account-settings-linked-accounts
- PayMongo subscriptions: https://docs.paymongo.com/docs/payment-acceptance-subscriptions
- Philippine privacy guidance: https://privacy.gov.ph/data-privacy-act/
