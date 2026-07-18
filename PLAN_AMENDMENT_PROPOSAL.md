# Locked Plan Amendment Proposal

**Status:** Proposed on 2026-07-19; not yet merged into `IMPLEMENTATION_PLAN.md`  
**Reason:** Provider documentation, privacy review, and implementation review found
correctness gaps that should be resolved explicitly before real hardware, guest data,
or money is used.

This file does not silently replace the locked plan. Simulator and contract work may
continue, but the affected production integrations remain gated until the user
approves this amendment and the locked plan is revised.

## 1. Move privacy readiness before the first real guest session

Replace the current partner-pilot timing with:

> Before the first real guest session: designate the privacy/DPO owner; complete the
> privacy and child privacy impact assessments, adult and child notices, lawful-basis
> and consent flow, retention/deletion policy, incident and 72-hour notification
> playbook, NPC registration analysis, and Philippine legal/DPO review. Before the
> partner pilot: execute the tenant processing agreement and the subprocessor and
> cross-border processing schedule.

Also define the default role allocation: the tenant photography business is PIC for
event and guest media; the platform is PIP under tenant instructions; the platform is
separately PIC for tenant accounts, billing, security, and legally required records.
Actual contracts and decision-making prevail over these default labels.

## 2. Clarify removable media and deletion

Replace “SD-card backup” with:

> Configure normal camera capture as host-only. The SD card is blank service/recovery
> media and does not receive guest captures. Any exceptional dual-write process is
> outside version one until access, inventory, retention, and secure erasure are
> defined.

Add a durable cloud-to-booth deletion tombstone and acknowledgement flow. Cloud must
reject late uploads for tombstoned or expired sessions, and restore procedures must
reapply tombstones. Encrypted backup copies rotate out within the documented window.

## 3. Correct QR Ph late-payment handling

Replace “reconciliation or refund” with:

> A late QR Ph success remains attached to the original session and is flagged for
> staff reconciliation. If service cannot be delivered, use an out-of-band
> reimbursement or service credit under the operations runbook; never call
> PayMongo's Refund API for QR Ph.

The payment expiry follows the provider and cannot be extended locally. A replacement
session never inherits or reuses an old payment attempt.

## 4. Add authoritative payment recovery and override semantics

Add:

> Unlock only after the cloud verifies a terminal succeeded state from a valid signed
> webhook or authenticated Payment Intent retrieval in the correct merchant/account
> context. Booth polling reads only cloud-persisted state; QR scans, redirects,
> screenshots, and client-side callbacks are never authoritative.

> Before an online cash/owner override, retrieve and cancel the active Payment Intent
> and verify it has not succeeded. If connectivity leaves provider state unknown,
> mark the attempt `payment_ambiguous`, keep it bound to the original session, audit
> the override, and ensure a later success cannot unlock another session. Override is
> a distinct authorization state and never changes the attempt to paid.

## 5. Correct PayMongo account and SaaS-billing prerequisites

Add:

> Guest payment and SaaS billing use separate webhook routes/signing secrets,
> account contexts, records, and reconciliation. Parent SaaS billing omits
> `Account-Id`; child guest-payment calls use the parent server credential plus the
> tenant's `Account-Id`. Store no tenant full-access API key.

> Gate D requires PayMongo to enable Linked Accounts under a signed agreement and to
> confirm that child-context dynamic QR Ph settles to the child as intended. Do not
> implement parent sweeps, split payments, transfers, or manual marketplace payouts.

> During the pilot, issue the SaaS vendor's required sales invoice separately and use
> a parent-account PayMongo Payment Link only as collection. Only signed
> `link.payment.paid` or authenticated provider retrieval extends `paid_until`.

> Gate E requires approved Cards/Maya and Subscriptions capabilities. Paid invoices
> extend entitlement; past-due status preserves access through the provider retry
> window; unpaid status stops the next offline-license renewal after paid-through and
> never interrupts an active guest session. Do not promise recurring GCash.

## 6. Clarify the private gallery route

Replace “Public gallery route `/g/{token}`” with:

> Unauthenticated private bearer-link route `/g/{token}`. Possession of the token is
> authorization; the route is neither public nor discoverable. Store only a token
> digest, redact raw token paths from all telemetry, use no third-party gallery
> analytics, and disclose that anyone holding the link can view the session.

Before gallery metadata and its token first synchronize, the cloud URL may be
unavailable. After metadata synchronizes but before media upload finishes, it may show
`Upload pending`. Version one does not preallocate token pools.

## 7. Add first-session privacy and security details

Add:

- Capture-time notice content and a versioned `SessionManifest.PrivacyRecord` storing
  notice ID/hash, lawful basis, locale, timestamps/action, participant confirmation,
  minor/guardian flag, and separate optional consents.
- The non-biometric question “Is anyone in this session under 18?”; a yes response
  shows the child notice and requires guardian confirmation. Do not collect DOB or ID.
- A gallery privacy-contact/deletion route, tenant privacy-admin revoke/delete tools,
  minimal identity verification, outcome audit, seven-business-day product target,
  and applicable NPC maximum/extension handling.
- MFA for roles with guest-media, payment, privacy-request, or support access;
  per-person operator identity; designer roles without guest-media access; support
  disabled by default and tenant-approved or break-glass, time-boxed, reasoned, and
  audited.
- Immediate platform-to-tenant incident escalation, evidence preservation, incident
  log, 72-hour assessment/notification workflow, and child-plus-guardian notice for a
  qualifying breach affecting a minor.

## 8. Add deterministic rendering and supply-chain details

Add:

- Cloud-owned template drafts; immutable published versions only on booths.
- `fabric@7.4.0`, managed PNG/JPEG/WebP and bundled WOFF2 assets, font completion
  before JSON load/render, and no arbitrary remote or uploaded SVG/PDF in version one.
- Exact-dimension noninteractive Fabric `StaticCanvas` rendering with an identity
  viewport; editor guides/zoom/selection never enter output.
- Original preservation plus sRGB/orientation normalization and output-sized slot
  derivatives; version-one guest filters exactly Original and BlackAndWhite.
- One 600x1800 2x6 strip duplicated side-by-side onto a 1200x1800 sheet.
- Booth-local printer/driver/media calibration profiles separate from templates.
- Motion output at 10 fps for 2–3 seconds; GIF max 720px long edge with palette
  filters; MP4 max 1080px long edge with `h264_mf`, 4:2:0, and `+faststart`; strict
  timeout/cancellation/result validation and temporary-frame cleanup.
- Record FFmpeg artifact/version/SHA-256/license/build configuration and reject GPL or
  nonfree builds.

## 9. Add signed event bundles and tablet authentication

Add:

- Canonical event-bundle manifest, per-asset hashes, signing-key ID, and detached
  ECDSA P-256 signature; pin the cloud key and reject invalid, expired, unsigned, or
  rollback-version bundles.
- Booth-LAN HTTPS, single-use pairing code, rotating paired-device credential,
  short-lived guest-session token, guest-only authorization, strict origins/hosts,
  CSRF protection, and explicit separation from operator/history/maintenance APIs.

## Primary supporting references

- PayMongo refunds: https://docs.paymongo.com/docs/payment-acceptance-refunds
- PayMongo webhooks: https://docs.paymongo.com/reference/webhook-resource
- PayMongo Linked Transactions: https://docs.paymongo.com/docs/account-settings-linked-transactions
- PayMongo subscriptions: https://docs.paymongo.com/docs/payment-acceptance-subscriptions
- NPC security requirements: https://privacy.gov.ph/wp-content/uploads/2024/03/NPC-Circular-Repeal-16-01-Signed.pdf
- NPC child transparency: https://privacy.gov.ph/wp-content/uploads/2024/12/Advisory-2024.12.17-Guidelines-on-Child-Oriented-Transparency-w-SGD.pdf
- NPC data-subject rights: https://privacy.gov.ph/wp-content/uploads/2021/02/NPC-Advisory-2021-01-FINAL.pdf
