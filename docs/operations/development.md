# Machi Studio Simulator — Run and Test Manual

This manual explains how to run the current DigiPhoto vertical slice, test each
surface, and distinguish working code from visual simulation. Run every command
from the repository root:

```text
C:\xampp\htdocs\digiphoto-booth-system
```

## Read this first

This is a **simulator-only development build**. Use synthetic images and test data
only. It does not validate a real camera, printer, payment, private gallery, or
production deployment.

The browser kiosk demonstrates a polished three-photo guest journey. The executable
booth API smoke test currently proves a smaller one-photo workflow. Those are two
different test tracks; neither should be presented as physical-hardware proof.

## Feature status

| Area | Current status | What can be tested now |
| --- | --- | --- |
| Development cloud fixture | Executable | Health endpoint, tenant guard, fixture data, asset download, and signed event-bundle publication |
| Booth bundle ingestion | Executable | Tenant/device binding, ES256 signature, expiry, sequence, schema, asset length, and SHA-256 validation |
| Booth session engine | Executable | One active session, persisted transitions, restart recovery, one simulated capture, exact 1200 × 1800 PNG render, serialized print, duplicate-print protection, and reset |
| Camera and printer | Simulated adapters | File-backed camera capture and simulated completed or ambiguous print outcomes |
| Guest kiosk | Browser interaction simulation | Responsive package, privacy/minor, optional payment demo, three-shot capture/review, processing, completion, gallery handoff, and Done/reset journey |
| Machi Studio presentation | Browser interaction simulation | Cozy brand treatment, `まちスタジオ` subline, touch-first layout, process motion, and capybara loading treatment |
| Operations portal | Browser interaction simulation | Event and booth overview, print-warning recovery state, and local unsupervised-payment setting demo |
| Template Studio | Interactive local editor | Fabric canvas, layers, undo/redo, paired 2x6 preview, and validated local 600 × 1800 PNG/JPEG/WebP artwork import |
| Private gallery | Browser interaction simulation | Photo/GIF/video presentation states, local download/link-copy behavior, retention notice, and local-only privacy request |
| Admin payment QR | Browser simulation only | Admin can toggle a browser-local event setting and inspect immutable demo package prices; the kiosk shows a non-scannable marker and simulated verification, but no provider QR or real unlock exists |
| Template upload/publish | Partial local demo | Imported strip artwork exists only in the current editor session; Save demo draft and Simulate publish are status simulations, not cloud persistence |
| Real hardware, payments, gallery, and cloud sync | Deferred | Must pass the production gates near the end of this manual |

## Prerequisites

- Windows 11
- .NET SDK 10.0.302 or a compatible .NET 10 feature band
- Node.js 24 and npm 11
- PowerShell 5.1 or newer
- A browser with current Chromium, Firefox, or Safari behavior

Open a new terminal after installing .NET. If `dotnet` is not on `PATH`, use the
installed fallback at `C:\Program Files\dotnet\dotnet.exe`.

## 1. Restore, build, and run automated checks

Use sequential .NET test commands on this Windows workstation. They avoid the
occasional VSTest startup timeout seen when every project starts in parallel.

```powershell
Set-Location "C:\xampp\htdocs\digiphoto-booth-system"

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }

& $dotnet restore DigiPhoto.slnx
& $dotnet build DigiPhoto.slnx --no-restore --nologo

& $dotnet test tests\DigiPhoto.Contracts.Tests\DigiPhoto.Contracts.Tests.csproj --no-build --nologo
& $dotnet test tests\DigiPhoto.Cloud.Tests\DigiPhoto.Cloud.Tests.csproj --no-build --nologo
& $dotnet test tests\DigiPhoto.Booth.Tests\DigiPhoto.Booth.Tests.csproj --no-build --nologo
& $dotnet list DigiPhoto.slnx package --vulnerable --include-transitive

npm.cmd --prefix src/web ci
npm.cmd --prefix src/web run typecheck
npm.cmd --prefix src/web run lint
npm.cmd --prefix src/web run test
npm.cmd --prefix src/web run build

git diff --check
```

Expected baseline for this manual:

- Contracts: **8 passed, 0 failed**
- Cloud: **3 passed, 0 failed**
- Booth: **35 passed, 0 failed**
- Web: **10 passed, 0 failed**
- Vulnerability scan: no vulnerable packages reported
- Typecheck, lint, build, and `git diff --check`: exit code `0`

Do not continue to manual testing if restore, build, or an affected test fails.

## 2. Four-terminal quick start

Keep all four terminals open. Use `Ctrl+C` to stop a process when instructed.

### Terminal 1 — cloud fixture on port 6147

```powershell
Set-Location "C:\xampp\htdocs\digiphoto-booth-system"

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }

$cloudData = Join-Path $env:LOCALAPPDATA "DigiPhoto\CloudDevelopment"
New-Item -ItemType Directory -Force -Path $cloudData | Out-Null
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__CloudDatabase = "Data Source=$(Join-Path $cloudData 'alpha1.db')"
& $dotnet run --project src/DigiPhoto.Cloud --urls http://127.0.0.1:6147
```

In another terminal, this should return `Healthy`:

```powershell
Invoke-RestMethod http://127.0.0.1:6147/health
```

Development endpoints:

- `GET http://127.0.0.1:6147/health`
- `GET http://127.0.0.1:6147/api/v1/development/signing-key`
- `GET http://127.0.0.1:6147/api/v1/development/fixture`
- `GET http://127.0.0.1:6147/api/v1/development/events/11111111-1111-4111-8111-111111111112/bundle`

The fixture, event-bundle, and asset-download endpoints require this header:

```text
X-DigiPhoto-Tenant-Id: 11111111-1111-4111-8111-111111111111
```

### Terminal 2 — booth simulator on port 6148

The development signing key is ephemeral and changes every time Terminal 1 is
restarted. Start or restart the Booth only after the Cloud is running so it pins the
current public key.

For a clean acceptance run, archive previous **synthetic** Booth data first. Stop any
old Booth process before running this block. Never point this command at customer
media.

```powershell
Set-Location "C:\xampp\htdocs\digiphoto-booth-system"

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }

$boothData = Join-Path $env:LOCALAPPDATA "DigiPhoto\BoothManual"
if (Test-Path -LiteralPath $boothData) {
    $archive = "$boothData.archive.$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Move-Item -LiteralPath $boothData -Destination $archive
    Write-Host "Archived prior simulator data to $archive"
}

$bundleKey = Invoke-RestMethod "http://127.0.0.1:6147/api/v1/development/signing-key"
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Booth__DatabasePath = Join-Path $boothData "booth.db"
$env:Booth__StorageRoot = Join-Path $boothData "media"
$env:Booth__BundleRoot = Join-Path $boothData "bundles"
$env:Booth__Identity__TenantId = "11111111-1111-4111-8111-111111111111"
$env:Booth__Identity__DeviceId = "11111111-1111-4111-8111-111111111115"
$env:Booth__BundleTrust__Algorithm = $bundleKey.algorithm
$env:Booth__BundleTrust__KeyId = $bundleKey.keyId
$env:Booth__BundleTrust__SubjectPublicKeyInfoBase64 = $bundleKey.subjectPublicKeyInfoBase64
& $dotnet run --project src/DigiPhoto.Booth --urls http://127.0.0.1:6148
```

An HTTPS-redirection warning can appear because this development run intentionally
uses loopback HTTP. The local endpoints should still respond.

### Terminal 3 — load the bundle and run the API smoke test

Wait until Terminals 1 and 2 report that they are listening.

```powershell
Set-Location "C:\xampp\htdocs\digiphoto-booth-system"

$boothData = Join-Path $env:LOCALAPPDATA "DigiPhoto\BoothManual"
.\scripts\load-development-bundle.ps1 -BundleRoot (Join-Path $boothData "bundles")
.\scripts\smoke-booth.ps1
```

The bundle loader should report:

- tenant `11111111-1111-4111-8111-111111111111`
- event `11111111-1111-4111-8111-111111111112`
- bundle `11111111-1111-4111-8111-111111111120`
- sequence `1`
- expiry `2035-01-01T00:00:00Z`
- `stagedAssets: 1`

The smoke script should report these states in order:

```text
Attract → PrivacyNotice → LivePreview → Countdown → Review → Rendering → PrintPending → Completed
```

It should also report:

```text
printJobState: Completed
duplicatePrintJobSame: true
activeAfterReset: false
mediaCount: 2
```

This proves that the bundle and asset were verified, consequential states were
persisted, the exact PNG output was accepted, one simulated print completed, a
duplicate request reused the same print job, and reset cleared the active session.

### Terminal 4 — browser surfaces on port 4173

```powershell
Set-Location "C:\xampp\htdocs\digiphoto-booth-system"
npm.cmd --prefix src/web run dev -- --host 127.0.0.1 --port 4173
```

Open `http://127.0.0.1:4173/`.

The web application uses bundled dependencies and synthetic local fixtures. It is a
visual/interaction harness and is not yet connected to the Cloud or Booth APIs.

## 3. Route-by-route manual checks

### Product index — `/`

- Confirm the Machi Studio brand and `まちスタジオ` subline render clearly.
- Confirm every photo is labeled as synthetic simulator media.
- Open Guest kiosk, Operations overview, Template Studio, and Private gallery.
- Use keyboard `Tab` and `Enter`; focus must remain visible and the route links must
  work without a mouse.

### Guest kiosk — `/kiosk`

Run this happy path:

1. Select **Start session**.
2. Choose either print package. Confirm the forward action is visually primary and
   **Start over** is secondary.
3. On Privacy, try to continue without checking the notice or answering the minor
   question. Confirm an actionable error appears and the camera stays locked.
4. Select **Yes, a minor is included** without guardian confirmation. Confirm the
   guardian gate blocks progress. Then confirm it, or select the adult-only answer.
5. Continue to the camera. Confirm the view says **High-five detection is ready**.
   In the simulator, dispatch `window.dispatchEvent(new Event('digiphoto:high-five'))`
   from DevTools for one shot, then use **Take photo** for the others. Both triggers
   must use the same countdown and review.
6. Confirm the Shutter Rail advances once per accepted photo and never advances on a
   retake.
7. In **Process**, confirm the two-minute timer, **+30 sec**, adjustments, filters,
   and photo selection work. Open the filter selector and confirm all five choices
   preview the selected photo before applying one. Select **Replace photo**, take and
   confirm a replacement, and verify the kiosk returns to Process without losing the
   other photos.
8. Select **Finish and print**. Confirm the animated capybara and current status
   communicate print progress without blocking the result.
9. On completion, confirm the wide dark viewfinder, three-frame contact sheet,
   watercolor capybara/printer scene, outlined **Open private gallery** action, and
   oversized pink **Done** action follow
   `docs/design/concepts/machi-studio-guest-completion.png`. The local gallery link
   is a demo; this screen does not claim to expose a production QR code.
10. Confirm the reset timer begins at 45 seconds. Select **Add more time** and confirm
   it returns to 90 seconds without leaving the completion screen.
11. Select **Done**. The kiosk must reset to the attract screen with no prior captures
    or session choices visible. On a separate completion run, leave the timer alone
    and confirm it resets the kiosk automatically after 45 seconds.

The browser kiosk runs a three-shot visual simulation. Its high-five control simulates
a booth-engine gesture event; it does not perform browser hand tracking. It does not call the Booth
API, send a print job, create a real gallery token, or scan/generate a real provider
QR code.

### Operations portal — `/portal`

- Confirm event, booth health, recent sessions, and attention state are readable.
- Select **Simulate print warning**. Confirm the UI says the outcome is unknown and
  does not offer an automatic reprint. Restore it with **Mark simulator healthy**.
- Select **Open event**, then find **Unsupervised payment QR** under **Simulation
  only**.
- Turn it on. Confirm the label becomes **Demo setting on** and the immutable demo
  package snapshots show Classic photo strip as PHP 150.00 and 4x6 portrait as PHP
  200.00. The event switch does not own or edit package prices.
- Refresh the route and confirm the valid demo setting remains in this browser.
- With the switch still on, open a **new** `/kiosk` route. Switch between both print
  packages and confirm the displayed amount follows the selected published package
  snapshot. Advance through privacy, then confirm **Payment verification demo**
  appears, the amount matches, and its marker says **DEMO ONLY** and is non-scannable.
- Confirm **Back to privacy** returns safely. Re-enter the payment step and select
  **Simulate cloud-verified payment**. Observe Awaiting → Checking → Verified before
  the browser simulation advances to the camera.
- Return to `/portal`, turn the setting off, and confirm payment returns to **Off for
  this event**. A newly opened kiosk should now skip the payment step.

The master payment switch is stored as a validated, versioned event setting in
browser `localStorage`; amounts come from immutable in-code demo package snapshots.
Invalid or unavailable storage fails safely to Off. A zero-price package would skip
the payment step. The kiosk step is timed UI simulation only: it does not create a
provider QR, accept money, contact PayMongo, or unlock the executable Booth. The
Booth payment endpoint intentionally returns HTTP `501`.

### Template Studio — `/templates/editor`

Use a tablet or desktop viewport for editing; a narrow phone shows a review-only
surface.

- Add Demo photo, Text, Shape, and Gallery QR objects.
- Select and move an object, then verify Undo and Redo.
- Verify layer selection, lock/unlock, and show/hide behavior.
- Select **Preview paired print**. Confirm one 600 × 1800 design is duplicated into
  the two sides of a 1200 × 1800 4x6 sheet.
- Under **Assets → Managed assets**, select **Import flattened strip**. Confirm it is
  labeled **Local demo only**.
- If you do not have test artwork, create a synthetic 600 × 1800 PNG in the Windows
  temporary directory with this safe PowerShell block:

  ```powershell
  Add-Type -AssemblyName System.Drawing

  $fixturePath = Join-Path $env:TEMP ("machi-strip-upload-{0}.png" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $bitmap = [System.Drawing.Bitmap]::new(600, 1800)
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $blushBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(251, 221, 221))

  try {
      $graphics.Clear([System.Drawing.Color]::FromArgb(255, 252, 251))
      $graphics.FillRectangle($blushBrush, 48, 48, 504, 1704)
      $bitmap.Save($fixturePath, [System.Drawing.Imaging.ImageFormat]::Png)
  }
  finally {
      $blushBrush.Dispose()
      $graphics.Dispose()
      $bitmap.Dispose()
  }

  Write-Host "Created synthetic template fixture: $fixturePath"
  ```

  This creates a new synthetic file and does not modify customer media.
- Import a real PNG, JPEG, or WebP that is exactly **600 × 1800 pixels** and no more
  than **15 MB**. Confirm it
  becomes the locked, non-selectable **Local artwork background** behind the other
  objects and participates in current-session Undo/Redo.
- Try an SVG, PDF, renamed unsupported file, or wrong-size image. Confirm the editor
  rejects it with an accessible error and leaves the existing design intact.
- Refresh or leave the route. The imported artwork should not be retained.

The import checks actual PNG/JPEG/WebP signatures and decoded dimensions. It does
not upload to cloud storage, create a managed cloud asset, save across reloads, or
publish an immutable bundle version. **Save demo draft** and **Simulate publish**
are disabled while local artwork is present; otherwise their feedback is simulation
only.

### Private gallery — `/g/demo`

- Switch among Photo, GIF, and Video. GIF/video are presentation states, not encoded
  guest files.
- Test Download and Copy private link. The downloaded asset is synthetic.
- Open gallery details and confirm the private-link warning is visible.
- Expand **Request access or deletion**, submit a test email, and confirm the UI says
  no information was sent or stored.
- Confirm 30-day retention and non-public gallery notices remain visible.

## 4. Responsive, motion, and control-placement checks

Use browser responsive mode at each size below and repeat the guest happy path at
least once on the target 10-inch tablet:

| Viewport | Main purpose |
| --- | --- |
| 320 × 800 | Small phone and gallery fallback |
| 768 × 1024 | Portrait tablet |
| 1024 × 768 | Landscape 10-inch tablet target |
| 1280 × 800 | Booth PC / compact laptop |
| 1440 × 900 | Desktop portal and editor |

For every guest state, confirm:

- no horizontal scrollbar, clipped action, or content hidden behind the viewport;
- the current task and primary next action are immediately clear;
- forward/confirm actions occupy the consistent primary position, while Back,
  Retake, Start over, End session, and other escape actions remain secondary;
- touch targets are at least 44 × 44 CSS pixels;
- progress, countdown, disabled, error, success, and reset states are visible;
- keyboard focus is visible and logical;
- at 200% zoom, controls remain reachable and content reflows;
- with `prefers-reduced-motion: reduce`, the workflow stays understandable and no
  completion depends on animation.

Animations should explain capture, processing, completion, or continuity. They must
not delay input after the state is ready.

## 5. Prove the simulator's offline booth path

Dependency installation and the first event-bundle download require the files to be
available locally. After the four-terminal startup and successful bundle load:

1. Stop Terminal 1 (Cloud) with `Ctrl+C`.
2. Disconnect the computer from external Wi-Fi/Ethernet.
3. Keep Terminal 2 (Booth) and Terminal 4 (Web) running.
4. In Terminal 3, run `.\scripts\smoke-booth.ps1` again.
5. Complete `/kiosk` and open `/g/demo` in the same browser.
6. Confirm the Booth smoke still completes and local browser routes still work.
7. Reconnect the computer, restart Terminal 1, then restart Terminal 2 with the new
   signing key before loading another development bundle.

This proves the cached simulator session/print path and local UI can run without an
external network. It does **not** prove production sync, payment, remote gallery
delivery, or a first-time event setup while offline. A production event with payment
enabled must fail closed without internet.

## 6. Stop and archive safely

1. Press `Ctrl+C` once in the Web, Booth, and Cloud terminals.
2. Confirm ports are no longer listening:

```powershell
Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object LocalPort -In 6147, 6148, 4173
```

3. Keep simulator data for recovery testing, or archive it after every process has
   stopped:

```powershell
$boothData = Join-Path $env:LOCALAPPDATA "DigiPhoto\BoothManual"
if (Test-Path -LiteralPath $boothData) {
    $archive = "$boothData.archive.$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Move-Item -LiteralPath $boothData -Destination $archive
    Write-Host "Archived to $archive"
}
```

Do not recursively delete unknown directories. The archive is recoverable and keeps
synthetic databases, media, bundles, and print artifacts separate from the next run.

## 7. Troubleshooting

### `dotnet` is not recognized

Open a new terminal or replace `dotnet` with:

```powershell
& "C:\Program Files\dotnet\dotnet.exe"
```

### A port is already in use

```powershell
Get-NetTCPConnection -State Listen -LocalPort 6147,6148,4173 |
    Select-Object LocalAddress, LocalPort, OwningProcess
```

Stop the known development process with `Ctrl+C`. Do not kill an unknown process
until its owner is identified.

### The bundle is rejected after restarting Cloud

This is expected if the Booth still pins the previous ephemeral key. Stop Booth,
start Cloud, fetch the new key by rerunning the Terminal 2 block, and use a fresh or
intentionally archived simulator data directory for the clean test.

### Booth reports an incompatible or old local schema

Stop Booth and archive `DigiPhoto\BoothManual`; then start with a fresh directory.
The Booth fails closed instead of destructively migrating unknown local media.

### The combined .NET test command times out

Run the three test projects sequentially as shown in section 1. If the VSTest host
still starts slowly, set this only for the current terminal and retry:

```powershell
$env:VSTEST_CONNECTION_TIMEOUT = "180"
```

### `npm.ps1` is blocked by PowerShell execution policy

Use `npm.cmd`, as shown in this manual.

### Local template artwork is rejected

Confirm the decoded image is exactly 600 × 1800 pixels and its real file format is
PNG, JPEG, or WebP. Renaming an SVG or PDF extension does not make it valid. Import
is deliberately not retained after leaving the editor.

### Template Studio is blank or reports a failed dynamic import

Stop the web server with `Ctrl+C`, then rebuild Vite's optimized dependency cache:

```powershell
npm.cmd --prefix src/web run dev -- --host 127.0.0.1 --port 4173 --force
```

Reload `/templates/editor`. This is especially relevant after the pinned Fabric
dependency or its optimized hash changes. Do not treat a passing TypeScript check as
proof that the lazy editor module loaded; verify the live canvas in the browser.

### Payment setting does not appear in an already-open kiosk

The kiosk reads the browser-local event setting when the route mounts. Return to the
portal, confirm **Demo setting on**, then open a new kiosk route. The displayed gate
is still simulation only; the executable Booth payment endpoint is not wired to it.
Do not use it with real money.

## 8. Gates before physical or production use

The following remain required and unproven:

- certify the exact Canon camera, tethering SDK/driver, cables, reconnect behavior,
  orientation, color, and power-cycle matrix;
- certify the exact DNP printer, driver, media, printable bounds, scale/offset,
  cutter mode, copy count, spooler ambiguity, and physical calibration sheet;
- connect the React UI to authenticated Cloud and booth-local APIs, with paired
  tablet HTTPS trust, origin/host controls, CSRF protection, scoped rotating device
  credentials, and rate limits;
- replace development SQLite cloud storage with production PostgreSQL and private
  object storage, tenant isolation, MFA, audit, support-access controls, and secret
  management;
- implement PayMongo QR creation and server-side terminal verification for the
  correct tenant, event, immutable package, amount, currency, and account context;
  validate duplicate, wrong, late, expired, ambiguous, override, webhook, and
  reconciliation cases without ever unlocking from the browser alone;
- persist template drafts and managed assets in the cloud, impose production upload
  limits, publish immutable versions, sign them into event bundles, and prove exact
  WebView2/Fabric output pixels;
- generate real private gallery tokens and QR codes, upload still/GIF/MP4 output,
  protect/redact media access, and test download/revocation behavior;
- run the pinned .NET FFmpeg pipeline for actual GIF/MP4 files;
- prove retention deletion and tombstones across cloud objects, database rows, booth
  cache, temp frames, thumbnails, spool artifacts, outbox, restore, and backups;
- complete legal/privacy review of the immutable notice, minor/guardian flow,
  optional consents, 7/30/90-day retention, and data-subject request handling.

Gate A in `IMPLEMENTATION_PLAN.md` remains open until the exact physical camera,
printer, driver, media, cables, calibration, reconnect, and power-cycle matrix passes
on site. Simulator success is not a substitute for that gate.
