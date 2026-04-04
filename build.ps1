$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$srcRoot = Join-Path $root 'src'
$buildRoot = Join-Path $root 'build'
$portableRoot = Join-Path $root 'portable'
$portableAppRoot = Join-Path $portableRoot 'FnMappingTool'
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$workerProject = Join-Path $srcRoot 'FnMappingTool.Worker\FnMappingTool.Worker.csproj'
$controllerProject = Join-Path $srcRoot 'FnMappingTool.Controller\FnMappingTool.Controller.csproj'
$controllerBuildOutput = Join-Path $buildRoot 'bin\FnMappingTool.Controller\x64\Release\net8.0-windows10.0.19041.0'
$workerBuildOutput = Join-Path $buildRoot 'bin\FnMappingTool.Worker\x64\Release\net8.0-windows10.0.19041.0'

if (-not (Test-Path $workerProject) -or -not (Test-Path $controllerProject)) {
    throw 'Worker or Controller project not found.'
}

$installPath = $null
if (Test-Path $vswhere) {
    $installPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
}

$msbuild = $null
if ($installPath) {
    $candidate = Join-Path $installPath 'MSBuild\Current\Bin\MSBuild.exe'
    if (Test-Path $candidate) {
        $msbuild = $candidate
    }
}

if (-not $msbuild) {
    throw 'MSBuild.exe not found. Install Visual Studio 2022 Community or Build Tools with the Managed Desktop workload.'
}

Get-Process FnMappingTool.Controller -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process FnMappingTool.Worker -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if (Test-Path $portableAppRoot) {
    Remove-Item -LiteralPath $portableAppRoot -Recurse -Force
}

New-Item -ItemType Directory -Force $portableAppRoot | Out-Null

& $msbuild $controllerProject /restore /t:Rebuild /p:Configuration=Release /p:Platform=x64 /verbosity:minimal

Copy-Item (Join-Path $controllerBuildOutput '*') $portableAppRoot -Recurse -Force

& $msbuild $workerProject /restore /t:Rebuild /p:Configuration=Release /p:Platform=x64 /verbosity:minimal

Copy-Item (Join-Path $workerBuildOutput '*') $portableAppRoot -Recurse -Force
Get-ChildItem $portableAppRoot -Recurse -Include *.pdb | Remove-Item -Force

Write-Host 'Build root:' $buildRoot
Write-Host 'Portable controller:' (Join-Path $portableAppRoot 'FnMappingTool.Controller.exe')
Write-Host 'Portable worker:' (Join-Path $portableAppRoot 'FnMappingTool.Worker.exe')
Write-Host 'Portable package folder:' $portableAppRoot
