#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath,
    [switch]$Submit
)

$ErrorActionPreference = 'Stop'
$productId = '9P2WKMHF43PN'
$appUri = "https://manage.devcenter.microsoft.com/v1.0/my/applications/$productId"

$package = Get-Item -LiteralPath $PackagePath
if ($package -isnot [IO.FileInfo] -or $package.Extension -ne '.msixbundle') {
    throw 'Pass the completed GestureSign V2 x64/ARM64 Store .msixbundle.'
}
$bundle = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entry = $bundle.GetEntry('AppxMetadata/AppxBundleManifest.xml')
    if (-not $entry) { throw 'The file has no MSIX bundle manifest.' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
} finally {
    $bundle.Dispose()
}
$identity = $manifest.Bundle.Identity
if ($identity.Name -cne 'TomClancy.GestureSignV2' -or
    $identity.Publisher -cne 'CN=A82713FB-4ABE-47C7-8184-01CF24575B7F') {
    throw 'The bundle does not have the GestureSign V2 Microsoft Store identity.'
}
$appPackages = @($manifest.Bundle.Packages.Package | Where-Object Type -eq 'application')
if ('x64' -notin $appPackages.Architecture -or 'arm64' -notin $appPackages.Architecture) {
    throw 'The Store bundle must contain both x64 and ARM64 application packages.'
}
$version = [version]$identity.Version
$versions = @($version) + @($appPackages | ForEach-Object { [version]$_.Version })
if ($versions.Where({ $_.Revision -ne 0 })) { throw 'Store package versions must end in .0.' }
Write-Host "Verified $($package.Name), bundle version $version (x64 and ARM64)."

foreach ($name in 'STORE_TENANT_ID', 'STORE_CLIENT_ID', 'STORE_CLIENT_SECRET') {
    if (-not [Environment]::GetEnvironmentVariable($name)) {
        throw "Set $name to the credential for the Partner Center Entra application."
    }
}
$tenantId = [guid]$env:STORE_TENANT_ID
$clientId = [guid]$env:STORE_CLIENT_ID
$tokenRequest = @{
    Method = 'Post'
    Uri = "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token"
    TimeoutSec = 60
    ContentType = 'application/x-www-form-urlencoded'
    Body = @{
        grant_type = 'client_credentials'
        client_id = $clientId.ToString()
        client_secret = $env:STORE_CLIENT_SECRET
        scope = 'https://manage.devcenter.microsoft.com/.default'
    }
}
try { $token = Invoke-RestMethod @tokenRequest } catch {
    throw "Microsoft Entra authentication failed (HTTP $([int]$_.Exception.Response.StatusCode))."
}
$request = @{ Headers = @{ Authorization = "Bearer $($token.access_token)" }; TimeoutSec = 60 }
$app = Invoke-RestMethod @request -Method Get -Uri $appUri
if ($app.id -ne $productId -or -not $app.lastPublishedApplicationSubmission.id) {
    throw 'The requested Store product or its published submission is unavailable.'
}
if ($app.pendingApplicationSubmission.id) {
    throw "Store submission $($app.pendingApplicationSubmission.id) is pending. Review it in Partner Center before starting another update."
}
$submissionListUri = "$appUri/submissions"
$published = Invoke-RestMethod @request -Method Get -Uri "$submissionListUri/$($app.lastPublishedApplicationSubmission.id)"
if (-not $published.applicationPackages) { throw 'No published Store packages were returned.' }
foreach ($existing in $published.applicationPackages) {
    if ($versions.Where({ $_ -le [version]$existing.version })) {
        throw "Bundle and app package versions must be newer than published package $($existing.version)."
    }
}
if (-not $Submit) {
    Write-Host "Store preflight passed for $productId. No submission was created."
    return
}

$tempArchive = Join-Path ([IO.Path]::GetTempPath()) "GestureSign-Store-$([guid]::NewGuid()).zip"
try {
    $archive = [IO.Compression.ZipFile]::Open($tempArchive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $package.FullName, $package.Name, [IO.Compression.CompressionLevel]::NoCompression)
    } finally {
        $archive.Dispose()
    }

    # Create through the API without deleting or taking over any existing submission.
    $draft = Invoke-RestMethod @request -Method Post -Uri $submissionListUri
    if (-not $draft.id -or -not $draft.fileUploadUrl) {
        throw 'Partner Center did not return a submission ID and upload URL.'
    }
    $submissionId = $draft.id
    Write-Host "Created Store submission $submissionId for $version."
    $draftUri = "$submissionListUri/$submissionId"
    $uploadUri = [uri]$draft.fileUploadUrl
    if ($uploadUri.Scheme -ne 'https' -or -not $uploadUri.Host.EndsWith('.blob.core.windows.net')) {
        throw 'Partner Center returned an unexpected package upload destination.'
    }

    $draft.applicationPackages = @($draft.applicationPackages) + [pscustomobject]@{
        fileName = $package.Name
        fileStatus = 'PendingUpload'
        minimumDirectXVersion = 'None'
        minimumSystemRam = 'None'
    }
    $draft.targetPublishMode = 'Immediate'
    foreach ($property in 'id', 'status', 'statusDetails', 'fileUploadUrl') {
        $draft.PSObject.Properties.Remove($property)
    }
    $update = @{
        Method = 'Put'
        Uri = $draftUri
        ContentType = 'application/json; charset=utf-8'
        Body = $draft | ConvertTo-Json -Depth 100 -Compress
    }
    [void](Invoke-RestMethod @request @update)
    $upload = @{
        Method = 'Put'
        Uri = $uploadUri
        InFile = $tempArchive
        TimeoutSec = 900
        ContentType = 'application/zip'
        Headers = @{ 'x-ms-blob-type' = 'BlockBlob'; 'x-ms-version' = '2021-12-02' }
    }
    try { [void](Invoke-RestMethod @upload) } catch {
        throw "Package upload failed for Store submission $submissionId (HTTP $([int]$_.Exception.Response.StatusCode))."
    }

    [void](Invoke-RestMethod @request -Method Post -Uri "$draftUri/commit")
    $result = Invoke-RestMethod @request -Method Get -Uri "$draftUri/status"
    if ($result.status -in 'PendingCommit', 'CommitFailed', 'CertificationFailed', 'ReleaseFailed', 'PublishFailed' -or
        -not $result.status) {
        throw "Partner Center returned submission status '$($result.status)' for $submissionId."
    }
    Write-Host "Microsoft Store status: $($result.status). Track submission $submissionId in Partner Center."
} finally {
    if (Test-Path -LiteralPath $tempArchive) { Remove-Item -LiteralPath $tempArchive }
}
