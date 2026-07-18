param(
    [string] $CloudBaseUrl = "http://127.0.0.1:6147",
    [string] $BoothBaseUrl = "http://127.0.0.1:6148",
    [string] $BundleRoot = (Join-Path $env:LOCALAPPDATA "DigiPhoto\BoothDevelopment\bundles"),
    [Guid] $TenantId = "11111111-1111-4111-8111-111111111111",
    [Guid] $EventId = "11111111-1111-4111-8111-111111111112"
)

$ErrorActionPreference = "Stop"
$tenantHeaders = @{ "X-DigiPhoto-Tenant-Id" = $TenantId.ToString() }
$bundleResponse = Invoke-WebRequest `
    -Uri "$CloudBaseUrl/api/v1/development/events/$EventId/bundle" `
    -Headers $tenantHeaders `
    -UseBasicParsing
$bundle = $bundleResponse.Content | ConvertFrom-Json

$versionRoot = Join-Path $BundleRoot $TenantId.ToString("N")
$versionRoot = Join-Path $versionRoot $EventId.ToString("N")
$versionRoot = Join-Path $versionRoot ([Guid] $bundle.manifest.bundleId).ToString("N")
$resolvedVersionRoot = [System.IO.Path]::GetFullPath($versionRoot)
$rootPrefix = $resolvedVersionRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar

foreach ($asset in $bundle.manifest.assets) {
    $relativePath = [string] $asset.relativePath
    if ([System.IO.Path]::IsPathRooted($relativePath)) {
        throw "Bundle asset path is rooted: $relativePath"
    }

    $targetPath = [System.IO.Path]::GetFullPath((Join-Path $resolvedVersionRoot $relativePath))
    if (-not $targetPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Bundle asset path escapes the version root: $relativePath"
    }

    New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($targetPath)) | Out-Null
    Invoke-WebRequest `
        -Uri "$CloudBaseUrl/api/v1/development/assets/$($asset.assetId)" `
        -Headers $tenantHeaders `
        -OutFile $targetPath `
        -UseBasicParsing

    $file = Get-Item -LiteralPath $targetPath
    if ($file.Length -ne [long] $asset.byteLength) {
        throw "Downloaded bundle asset has the wrong length: $relativePath"
    }

    $stream = [System.IO.File]::OpenRead($targetPath)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $actualHash = [BitConverter]::ToString($sha256.ComputeHash($stream))
            $actualHash = $actualHash.Replace("-", "").ToLowerInvariant()
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    if ($actualHash -ne ([string] $asset.sha256).ToLowerInvariant()) {
        throw "Downloaded bundle asset failed its SHA-256 check: $relativePath"
    }
}

$loaded = Invoke-RestMethod `
    -Method Post `
    -Uri "$BoothBaseUrl/api/v1/booth/bundles" `
    -ContentType "application/json" `
    -Body $bundleResponse.Content

[pscustomobject]@{
    tenantId = $loaded.tenantId
    eventId = $loaded.eventId
    bundleId = $loaded.bundleId
    sequence = $loaded.sequence
    expiresAtUtc = $loaded.expiresAtUtc
    stagedAssets = $bundle.manifest.assets.Count
} | ConvertTo-Json
