param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$ocrRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $ocrRepoRoot ('artifacts/release-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '/publish')
}
$ocrPublishDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $ocrPublishDirectory) -and (Get-ChildItem -LiteralPath $ocrPublishDirectory -Force | Select-Object -First 1)) {
    throw "Output directory is not empty: $ocrPublishDirectory"
}
New-Item -ItemType Directory -Path $ocrPublishDirectory -Force | Out-Null
Push-Location $ocrRepoRoot
try {
    & python tools/FrlgOcrReplay/fetch_resources.py --verify
    if ($LASTEXITCODE -ne 0) { throw 'Pinned resource verification failed.' }
    foreach ($ocrProject in @('src/EasyCon2.CLI', 'src/EasyCon2.Avalonia', 'tools/FrlgOcrReplay')) {
        & dotnet publish $ocrProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $ocrPublishDirectory -v minimal
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $ocrProject" }
    }
    Rename-Item -LiteralPath (Join-Path $ocrPublishDirectory 'EasyCon2.Avalonia.exe') -NewName 'EasyCon-FRLG-OCR.exe'
    Rename-Item -LiteralPath (Join-Path $ocrPublishDirectory 'EasyCon2.CLI.exe') -NewName 'ezcon.exe'
    New-Item -ItemType Directory -Path (Join-Path $ocrPublishDirectory 'examples'), (Join-Path $ocrPublishDirectory 'samples'), (Join-Path $ocrPublishDirectory 'licenses') -Force | Out-Null
    Copy-Item -LiteralPath 'examples/FRLG-TID-170a-独立测试.ecs' -Destination (Join-Path $ocrPublishDirectory 'examples')
    Copy-Item -LiteralPath 'test/EasyCon.Tests/TestData/Frlg/nyash_jpn_45345.png','test/EasyCon.Tests/TestData/Frlg/tom_eng_60895.jpg' -Destination (Join-Path $ocrPublishDirectory 'samples')
    Copy-Item -LiteralPath 'docs/FRLG-OCR-170a.md' -Destination (Join-Path $ocrPublishDirectory '使用说明.md')
    Copy-Item -LiteralPath 'docs/FRLG-OCR-VALIDATION.md' -Destination (Join-Path $ocrPublishDirectory '验证记录.md')
    Copy-Item -LiteralPath 'docs/FRLG-OCR-SOURCES.md','docs/frlg-ocr-resources.lock.json','docs/licenses/PokemonAutomation-MIT.txt','LICENSE' -Destination (Join-Path $ocrPublishDirectory 'licenses')
    $ocrCommit = & git rev-parse HEAD
    [ordered]@{
        version = '170a-frlg-tid-r1'
        easyconBaseline = '1aed001c0e2d3a32d211c39bec26546741626bd6'
        sourceCommit = $ocrCommit
        sourceUrl = "https://github.com/axechaso/EasyCon/tree/$ocrCommit"
        sourceHasChanges = [bool](& git status --porcelain)
        builtAt = (Get-Date).ToString('o')
        runtime = 'win-x64 self-contained'
        scenes = @('FRLG_JPN_TID','FRLG_EN_TID')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ocrPublishDirectory 'build-info.json') -Encoding utf8
    $ocrManifest = foreach ($ocrFile in Get-ChildItem -LiteralPath $ocrPublishDirectory -Recurse -File) {
        [ordered]@{ path = [IO.Path]::GetRelativePath($ocrPublishDirectory, $ocrFile.FullName); sha256 = (Get-FileHash -LiteralPath $ocrFile.FullName -Algorithm SHA256).Hash; bytes = $ocrFile.Length }
    }
    $ocrManifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $ocrPublishDirectory 'SHA256.json') -Encoding utf8
    $ocrArchive = Join-Path (Split-Path -Parent $ocrPublishDirectory) 'EasyCon-170a-FRLG-OCR-TID-r1-win-x64.zip'
    Compress-Archive -LiteralPath $ocrPublishDirectory -DestinationPath $ocrArchive -CompressionLevel Optimal
    Write-Output "PUBLISH_DIRECTORY=$ocrPublishDirectory"
    Write-Output "ARCHIVE=$ocrArchive"
}
finally { Pop-Location }
