param(
    [Parameter(Mandatory = $true)]
    [string]$CsvPath,

    [int]$Size = 224,

    # Use 'none' for transparent padding, or 'white' for white padding.
    [string]$Background = 'white'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Common raster image formats. ImageMagick may support more on your machine,
# but these are the ones this script will try to process.
$ImageExtensions = @(
    '.jpg', '.jpeg', '.jpe', '.jfif',
    '.png',
    '.gif',
    '.bmp',
    '.tif', '.tiff',
    '.webp',
    '.heic', '.heif',
    '.avif'
)

function Get-FolderPairsFromCsv {
    param([string]$Path)

    $lines = Get-Content -LiteralPath $Path | Where-Object { $_.Trim() -ne '' }
    if (-not $lines -or $lines.Count -eq 0) {
        return @()
    }

    $firstField = (($lines[0] -split ',')[0]).Trim().Trim('"')

    # If the first field looks like a Windows path, assume there is no header row.
    if ($firstField -match '^[A-Za-z]:\\|^\\\\') {
        return $lines | ConvertFrom-Csv -Header 'InputFolder', 'OutputFolder'
    }

    # Otherwise assume there is a header row and use the first two columns.
    $rows = $lines | ConvertFrom-Csv
    if (-not $rows -or $rows.Count -eq 0) {
        return @()
    }

    $props = @($rows[0].PSObject.Properties.Name)
    if ($props.Count -lt 2) {
        throw "CSV must contain at least two columns."
    }

    foreach ($row in $rows) {
        [pscustomobject]@{
            InputFolder  = $row.($props[0])
            OutputFolder = $row.($props[1])
        }
    }
}

function Resize-ImageFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceFile,

        [Parameter(Mandatory = $true)]
        [string]$DestFile,

        [Parameter(Mandatory = $true)]
        [int]$Size,

        [Parameter(Mandatory = $true)]
        [string]$Background
    )

    $destDir = Split-Path -Parent $DestFile
    if (-not (Test-Path -LiteralPath $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }

    & magick `
        $SourceFile `
        -auto-orient `
        -filter Lanczos `
        -resize "${Size}x${Size}" `
        -background $Background `
        -gravity center `
        -extent "${Size}x${Size}" `
        "PNG:$DestFile"

    if ($LASTEXITCODE -ne 0) {
        throw "ImageMagick failed processing: $SourceFile"
    }
}

$jobs = Get-FolderPairsFromCsv -Path $CsvPath

foreach ($job in $jobs) {
    $inputRoot = [System.IO.Path]::GetFullPath($job.InputFolder)
    $outputBase = [System.IO.Path]::GetFullPath($job.OutputFolder)

    if (-not (Test-Path -LiteralPath $inputRoot)) {
        Write-Warning "Input folder does not exist: $inputRoot"
        continue
    }

    $inputLeaf = Split-Path -Path $inputRoot -Leaf
    $outputRoot = Join-Path $outputBase $inputLeaf

    if (-not (Test-Path -LiteralPath $outputRoot)) {
        New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    }

    $inputRootTrimmed = $inputRoot.TrimEnd('\', '/')

    Get-ChildItem -LiteralPath $inputRoot -Recurse -File |
        Where-Object { $ImageExtensions -contains $_.Extension.ToLowerInvariant() } |
        ForEach-Object {
            $file = $_

            $relativePath = $file.FullName.Substring($inputRootTrimmed.Length).TrimStart('\', '/')
            $relativeDirectory = Split-Path $relativePath -Parent
            $pngFilename = [System.IO.Path]::ChangeExtension((Split-Path $relativePath -Leaf), '.png')

            if ([string]::IsNullOrEmpty($relativeDirectory)) {
                $destFile = Join-Path $outputRoot $pngFilename
            }
            else {
                $destFile = Join-Path (Join-Path $outputRoot $relativeDirectory) $pngFilename
            }

            try {
                Resize-ImageFile -SourceFile $file.FullName -DestFile $destFile -Size $Size -Background $Background
                Write-Host "Processed: $($file.FullName)"
            }
            catch {
                Write-Warning "Skipped '$($file.FullName)': $($_.Exception.Message)"
            }
        }
}