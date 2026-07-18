param(
    [string] $BaseUrl = "http://127.0.0.1:6148/api/v1/booth"
)

$ErrorActionPreference = "Stop"

function Invoke-JsonPost {
    param(
        [Parameter(Mandatory)]
        [string] $Uri,

        [Parameter(Mandatory)]
        [object] $Value
    )

    Invoke-RestMethod `
        -Method Post `
        -Uri $Uri `
        -ContentType "application/json" `
        -Body ($Value | ConvertTo-Json -Depth 10)
}

$sessionId = [Guid]::NewGuid()
$started = Invoke-JsonPost -Uri "$BaseUrl/sessions" -Value @{
    sessionId = $sessionId
    eventId = "11111111-1111-4111-8111-111111111112"
}

$null = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$sessionId/begin"
$packaged = Invoke-JsonPost -Uri "$BaseUrl/sessions/$sessionId/package" -Value @{
    packageVersionId = "11111111-1111-4111-8111-111111111114"
}

$displayedAt = [DateTimeOffset]::UtcNow
$noticed = Invoke-JsonPost -Uri "$BaseUrl/sessions/$sessionId/privacy" -Value @{
    displayedAtUtc = $displayedAt.ToString("O")
    assentedAtUtc = $displayedAt.AddSeconds(1).ToString("O")
    assentingAction = "continue"
    participantsConfirmed = $true
    includesMinor = $false
    guardianConfirmed = $false
    promotionConsent = $false
    publicDisplayConsent = $false
}

$countdown = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$sessionId/countdown"
$captured = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$sessionId/capture"
$reviewed = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$sessionId/review"

Add-Type -AssemblyName System.Drawing
$bitmap = New-Object -TypeName System.Drawing.Bitmap -ArgumentList 1200, 1800
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$pngStream = New-Object System.IO.MemoryStream
try {
    $graphics.Clear([System.Drawing.Color]::White)
    $bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $pngStream.ToArray()
}
finally {
    $pngStream.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$rendered = Invoke-RestMethod `
    -Method Post `
    -Uri "$BaseUrl/sessions/$sessionId/render" `
    -ContentType "image/png" `
    -Body $pngBytes
$printed = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$sessionId/print"
$duplicate = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$sessionId/print"
$reset = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$sessionId/reset"

if ($printed.state -ne "Completed") {
    throw "Expected a completed print, received $($printed.state)."
}

if ($printed.printJob.printJobId -ne $duplicate.printJob.printJobId) {
    throw "A duplicate print command created a second print job."
}

if ($reset.isActive) {
    throw "The completed session remained active after reset."
}

[pscustomobject]@{
    sessionId = $sessionId
    states = @(
        $started.state,
        $packaged.state,
        $noticed.state,
        $countdown.state,
        $captured.state,
        $reviewed.state,
        $rendered.state,
        $printed.state
    )
    printJobState = $printed.printJob.state
    duplicatePrintJobSame = $true
    activeAfterReset = $reset.isActive
    mediaCount = $printed.media.Count
} | ConvertTo-Json -Depth 5
