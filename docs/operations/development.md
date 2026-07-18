# Development Runbook

This runbook starts the simulator-only vertical slice. It does not validate a Canon
camera, DNP printer, PayMongo payment, production identity, or real guest media.

## Prerequisites

- Windows 11
- .NET SDK 10.0.302 or a compatible 10.0 feature band
- Node.js 24 and npm 11

Open a new terminal after installing .NET so `dotnet` is available on `PATH`. In the
current machine image, the direct fallback is `C:\Program Files\dotnet\dotnet.exe`.

## Restore and verify

From the repository root:

```powershell
dotnet restore DigiPhoto.slnx
dotnet build DigiPhoto.slnx --no-restore --nologo
dotnet test DigiPhoto.slnx --no-build --nologo
dotnet list DigiPhoto.slnx package --vulnerable --include-transitive

npm --prefix src/web ci
npm --prefix src/web run typecheck
npm --prefix src/web run lint
npm --prefix src/web run test
npm --prefix src/web run build
```

## Run the cloud development fixture

```powershell
$cloudData = Join-Path $env:LOCALAPPDATA "DigiPhoto\CloudDevelopment"
New-Item -ItemType Directory -Force -Path $cloudData | Out-Null
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__CloudDatabase = "Data Source=$(Join-Path $cloudData 'alpha1.db')"
dotnet run --project src/DigiPhoto.Cloud --urls http://127.0.0.1:6147
```

Useful endpoints:

- `GET http://127.0.0.1:6147/health`
- `GET http://127.0.0.1:6147/api/v1/development/signing-key`
- `GET http://127.0.0.1:6147/api/v1/development/fixture`
- `GET http://127.0.0.1:6147/api/v1/development/events/11111111-1111-4111-8111-111111111112/bundle`

The last two requests require this header:

```text
X-DigiPhoto-Tenant-Id: 11111111-1111-4111-8111-111111111111
```

Development uses synthetic fixture records and an ephemeral event-bundle signing
key. Restarting the process intentionally rotates that key.

## Run the booth simulator

Keep the cloud fixture running. In a second terminal, fetch its ephemeral public key
and start the booth with an explicit tenant/device identity. Use a local development
data directory that contains no customer media:

```powershell
$boothData = Join-Path $env:LOCALAPPDATA "DigiPhoto\BoothDevelopment"
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
dotnet run --project src/DigiPhoto.Booth --urls http://127.0.0.1:6148
```

In a third terminal, stage the fixture assets under their immutable
tenant/event/bundle namespace, ask the booth to verify and load the signed bundle,
then exercise the complete free-photo API path:

```powershell
.\scripts\load-development-bundle.ps1 -BundleRoot (Join-Path $env:LOCALAPPDATA "DigiPhoto\BoothDevelopment\bundles")
.\scripts\smoke-booth.ps1
```

The loader checks downloaded asset lengths and SHA-256 values before the booth
independently checks the asset inventory, manifest, tenant, expiry, monotonic bundle
sequence, pinned ES256 signature, template hashes, and package references. The smoke
script then starts a session from that immutable bundle, accepts the fixture notice,
captures through the file-backed camera, persists an exact 1200x1800 PNG render,
submits to the serialized simulated printer, proves a duplicate print command reuses
the same job, and resets the booth.

## Run the web surfaces

```powershell
npm --prefix src/web run dev -- --host 127.0.0.1 --port 4173
```

Routes:

- `http://127.0.0.1:4173/` — prototype index
- `http://127.0.0.1:4173/kiosk` — guest kiosk simulator
- `http://127.0.0.1:4173/portal` — tenant operations overview
- `http://127.0.0.1:4173/templates/editor` — Fabric template-studio foundation
- `http://127.0.0.1:4173/g/demo` — private-gallery visual fixture

The current web routes use explicit synthetic local fixtures. They are a visual and
interaction harness; they are not yet authenticated or wired to physical hardware,
cloud galleries, or PayMongo.

## Safety boundaries

- Do not use real guest images or personal data in this slice.
- Do not enable the payment step. The booth payment endpoint deliberately returns
  `501` and cannot unlock a session.
- Do not treat the simulated printer result as proof of spooler, cutter, media, or
  physical copy-count correctness.
- Gate A in `IMPLEMENTATION_PLAN.md` remains open until the exact certified camera,
  printer, driver, media, cables, and power-cycle matrix pass on site.
