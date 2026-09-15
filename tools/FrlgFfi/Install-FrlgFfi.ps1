param(
    [Parameter(Mandatory = $true)]
    [string] $PluginRoot,
    [Parameter(Mandatory = $true)]
    [string] $ScriptLibDirectory
)

$pluginPath = (Resolve-Path -LiteralPath $PluginRoot -ErrorAction Stop).Path
$scriptLibPath = (Resolve-Path -LiteralPath $ScriptLibDirectory -ErrorAction Stop).Path
$dllPath = Join-Path $pluginPath 'FrlgFfi.dll'
$modelPath = Join-Path $pluginPath 'models\frlg'
$templatePath = Join-Path $PSScriptRoot 'FrlgFfi.ecs.template'
$outputPath = Join-Path $scriptLibPath 'FrlgFfi.ecs'

if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
    throw "Plugin DLL not found: $dllPath"
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

$dllValue = $dllPath.Replace('\', '/')
$modelValue = $modelPath.Replace('\', '/')
$source = [System.IO.File]::ReadAllText($templatePath)
$source = $source.Replace('@DLL@', $dllValue).Replace('@MODELS@', $modelValue)
[System.IO.File]::WriteAllText($outputPath, $source, [System.Text.UTF8Encoding]::new($false))
Write-Host "Installed FRLG FFI declarations: $outputPath"
