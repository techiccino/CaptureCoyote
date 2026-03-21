param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "artifacts\publish\$RuntimeIdentifier"
$setupScript = Join-Path $PSScriptRoot "CaptureCoyote.iss"
$propsPath = Join-Path $repoRoot "Directory.Build.props"

if ([string]::IsNullOrWhiteSpace($Version) -and (Test-Path $propsPath)) {
    [xml]$props = Get-Content $propsPath
    $Version = $props.Project.PropertyGroup.Version
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "1.2.3"
}

Write-Host "Publishing CaptureCoyote to $publishDir ..."
dotnet publish (Join-Path $repoRoot "CaptureCoyote.App\CaptureCoyote.App.csproj") `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained false `
    /p:UseAppHost=true `
    /p:PublishSingleFile=false `
    /p:PublishReadyToRun=false `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$programFilesX86 = ${env:ProgramFiles(x86)}
$isccCandidates = @()
if ($programFilesX86) {
    $isccCandidates += Join-Path $programFilesX86 "Inno Setup 6\ISCC.exe"
}
if ($env:ProgramFiles) {
    $isccCandidates += Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"
}
if ($env:LOCALAPPDATA) {
    $isccCandidates += Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
}

$iscc = $isccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        $iscc = $command.Source
    }
}

if (-not $iscc) {
    Write-Warning "Inno Setup 6 was not found. Publish output is ready, but the installer was not built."
    Write-Host "Install Inno Setup and rerun this script to generate the .exe installer."
    exit 0
}

Write-Host "Building installer with Inno Setup ..."
& $iscc "/DAppVersion=$Version" "/DSourceDir=$publishDir" $setupScript

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed."
}

Write-Host "Installer build completed."
