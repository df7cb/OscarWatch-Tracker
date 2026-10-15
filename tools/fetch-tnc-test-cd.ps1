<#
.SYNOPSIS
  Downloads the WA8LMF TNC Test CD (version 2.0) and converts its FLAC tracks to WAV for the
  built-in modem benchmark (Wa8lmfBenchmarkTests).

.DESCRIPTION
  The CD is the de facto 1200 baud packet benchmark that Direwolf and UZ7HO publish results against.
  It is about 250 MB, has no stated licence and is never committed: output goes to the git-ignored
  testdata/wa8lmf folder, or to the folder in OSCARWATCH_TNC_TEST_CD.

  The benchmark only runs when OSCARWATCH_TNC_TEST_CD is set (to the folder, or to 1 for
  testdata/wa8lmf), so ordinary test runs stay fast.

  Needs 7-Zip (7z) or Windows ISO mounting, and ffmpeg or flac.

  WA8LMF asks that these tracks are never played over the air: his callsign would be igated.

.EXAMPLE
  pwsh tools/fetch-tnc-test-cd.ps1
#>
[CmdletBinding()]
param(
    [string]$Destination = $env:OSCARWATCH_TNC_TEST_CD,
    [string]$IsoUrl = 'http://wa8lmf.net/TNCtest/TNC_Test_2.iso'
)

$ErrorActionPreference = 'Stop'
if (-not $Destination -or $Destination.Trim() -in @('1', 'repo')) {
    $Destination = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\testdata\wa8lmf'
}
$Destination = [System.IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $Destination | Out-Null

if ((Get-ChildItem -Path $Destination -Filter 'track*.wav' -ErrorAction SilentlyContinue).Count -ge 7) {
    Write-Host "WA8LMF tracks already present in $Destination"
    return
}

$iso = Join-Path $Destination 'TNC_Test_2.iso'
if (-not (Test-Path $iso)) {
    Write-Host "Downloading $IsoUrl (about 250 MB)"
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $IsoUrl -OutFile "$iso.part" -UseBasicParsing
    Move-Item "$iso.part" $iso -Force
}

$extract = Join-Path $Destination 'iso'
New-Item -ItemType Directory -Force -Path $extract | Out-Null

function Find-Tool([string[]]$names, [string[]]$extraPaths) {
    foreach ($n in $names) {
        $c = Get-Command $n -ErrorAction SilentlyContinue
        if ($c) { return $c.Source }
    }
    foreach ($p in $extraPaths) {
        if ($p -and (Test-Path $p)) { return $p }
    }
    return $null
}

$sevenZip = Find-Tool @('7z', '7za', '7zz') @("$env:ProgramFiles\7-Zip\7z.exe")
if ($sevenZip) {
    & $sevenZip x -y "-o$extract" $iso '*.flac' -r | Out-Null
}
elseif ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $image = Mount-DiskImage -ImagePath $iso -PassThru
    try {
        $drive = ($image | Get-Volume).DriveLetter
        Get-ChildItem -Path "${drive}:\" -Recurse -Filter '*.flac' | Copy-Item -Destination $extract
    }
    finally {
        Dismount-DiskImage -ImagePath $iso | Out-Null
    }
}
else {
    throw 'Install 7-Zip (p7zip) to extract the ISO.'
}

$flacFiles = Get-ChildItem -Path $extract -Recurse -Filter '*.flac' | Sort-Object Name
if ($flacFiles.Count -eq 0) {
    throw "No FLAC files found in $iso"
}

$ffmpeg = Find-Tool @('ffmpeg') @()
$flac = Find-Tool @('flac') @()
if (-not $ffmpeg -and -not $flac) {
    throw 'Install ffmpeg or flac to convert the tracks to WAV.'
}

foreach ($file in $flacFiles) {
    # Track number is the first number in the file name, for example "01 - Track 1.flac".
    if ($file.BaseName -notmatch '(\d+)') {
        Write-Warning "Skipping $($file.Name): no track number in the name"
        continue
    }

    $wav = Join-Path $Destination ('track{0:D2}.wav' -f [int]$Matches[1])
    Write-Host "Converting $($file.Name) to $(Split-Path $wav -Leaf)"
    if ($ffmpeg) {
        & $ffmpeg -nostdin -loglevel error -y -i $file.FullName -c:a pcm_s16le $wav
    }
    else {
        & $flac -d -f -s -o $wav $file.FullName
    }
}

Remove-Item -Recurse -Force $extract
# The WAV files are all the tests need; dropping the ISO halves the CI cache.
Remove-Item -Force $iso
Write-Host "Done. Tracks are in $Destination"
Write-Host 'The benchmark is opt-in: set OSCARWATCH_TNC_TEST_CD=1 (or to this folder) before dotnet test.'
