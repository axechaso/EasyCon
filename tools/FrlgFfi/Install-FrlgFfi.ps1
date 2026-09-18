param(
    [Parameter(Mandatory = $true)]
    [string] $PluginRoot,
    [Parameter(Mandatory = $true)]
    [string] $ScriptLibDirectory
)

$pluginPath = (Resolve-Path -LiteralPath $PluginRoot -ErrorAction Stop).Path
$scriptLibPath = (Resolve-Path -LiteralPath $ScriptLibDirectory -ErrorAction Stop).Path
$modelPath = Join-Path $pluginPath 'models\frlg'
$templatePath = Join-Path $PSScriptRoot 'FrlgFfi.ecs.template'
$outputPath = Join-Path $scriptLibPath 'FrlgFfi.ecs'
$packagePath = Join-Path (Split-Path -Parent $scriptLibPath) 'FrlgFfi'
$packageFiles = @(
    'FrlgFfi.dll',
    'ezcv_native.dll',
    'leptonica-1.85.0.dll',
    'onnxruntime.dll',
    'onnxruntime_providers_shared.dll',
    'opencv_videoio_ffmpeg500_64.dll',
    'opencv_world500.dll',
    'tesseract55.dll'
)

foreach ($file in $packageFiles) {
    $path = Join-Path $pluginPath $file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Plugin dependency not found: $path"
    }
}
if (-not (Test-Path -LiteralPath $modelPath -PathType Container)) {
    throw "Plugin model directory not found: $modelPath"
}
if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
    throw "ECS template not found: $templatePath"
}
if (Test-Path -LiteralPath $outputPath) {
    throw "Refusing to overwrite existing script module: $outputPath"
}
if (Test-Path -LiteralPath $packagePath) {
    throw "Refusing to overwrite existing plugin package: $packagePath"
}

New-Item -ItemType Directory -Path $packagePath | Out-Null
foreach ($file in $packageFiles) {
    Copy-Item -LiteralPath (Join-Path $pluginPath $file) -Destination $packagePath
}
Copy-Item -LiteralPath (Join-Path $pluginPath 'models') -Destination $packagePath -Recurse

$source = [System.IO.File]::ReadAllText($templatePath)
[System.IO.File]::WriteAllText($outputPath, $source, [System.Text.UTF8Encoding]::new($false))
Write-Host "Installed FRLG FFI package: $packagePath"
Write-Host "Installed FRLG FFI declarations: $outputPath"
