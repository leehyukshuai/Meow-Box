param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0',
    [switch]$SkipZip,
    [switch]$SkipMsi
)

$ErrorActionPreference = 'Stop'

function Get-MsBuildPath {
    $vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) {
        throw 'MSBuild.exe not found. Install Visual Studio 2022 Community or Build Tools with the Managed Desktop workload.'
    }

    $installPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if ([string]::IsNullOrWhiteSpace($installPath)) {
        throw 'MSBuild.exe not found. Install Visual Studio 2022 Community or Build Tools with the Managed Desktop workload.'
    }

    $msbuild = Join-Path $installPath 'MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path $msbuild)) {
        throw 'MSBuild.exe not found. Install Visual Studio 2022 Community or Build Tools with the Managed Desktop workload.'
    }

    return $msbuild
}

function Remove-PathIfExists {
    param([string]$Path)

    if (Test-Path $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Invoke-MSBuildProject {
    param(
        [string]$MSBuild,
        [string]$ProjectPath,
        [string[]]$Properties = @()
    )

    $arguments = @(
        $ProjectPath,
        '/restore',
        '/t:Rebuild',
        '/verbosity:minimal'
    ) + $Properties

    & $MSBuild @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for '$ProjectPath' with exit code $LASTEXITCODE."
    }
}

function Get-HashHex {
    param([string]$Value)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())
    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $hashBytes = $md5.ComputeHash($bytes)
    }
    finally {
        $md5.Dispose()
    }
    return ([System.BitConverter]::ToString($hashBytes)).Replace('-', '')
}

function New-StableId {
    param(
        [string]$Prefix,
        [string]$Value
    )

    return '{0}_{1}' -f $Prefix, (Get-HashHex $Value).Substring(0, 16)
}

function New-StableGuid {
    param([string]$Value)

    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $hashBytes = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant()))
    }
    finally {
        $md5.Dispose()
    }
    return ([guid]::new($hashBytes)).ToString().ToUpperInvariant()
}

function Escape-Xml {
    param([string]$Value)

    return [System.Security.SecurityElement]::Escape($Value)
}

function Write-PortableDirectory {
    param(
        [string]$DirectoryPath,
        [string]$Indent,
        [System.Collections.Generic.List[string]]$DirectoryLines,
        [System.Collections.Generic.List[string]]$ComponentRefLines
    )

    $files = Get-ChildItem -LiteralPath $DirectoryPath -File | Sort-Object Name
    foreach ($file in $files) {
        $componentId = New-StableId 'CMP' $file.FullName
        $fileId = New-StableId 'FIL' $file.FullName
        $guid = New-StableGuid $file.FullName
        $source = Escape-Xml $file.FullName

        $DirectoryLines.Add(('{0}<Component Id="{1}" Guid="{2}">' -f $Indent, $componentId, $guid))
        $DirectoryLines.Add(('{0}  <File Id="{1}" Source="{2}" KeyPath="yes" />' -f $Indent, $fileId, $source))
        $DirectoryLines.Add(('{0}</Component>' -f $Indent))
        $ComponentRefLines.Add(('      <ComponentRef Id="{0}" />' -f $componentId))
    }

    $directories = Get-ChildItem -LiteralPath $DirectoryPath -Directory | Sort-Object Name
    foreach ($directory in $directories) {
        $directoryId = New-StableId 'DIR' $directory.FullName
        $name = Escape-Xml $directory.Name
        $DirectoryLines.Add(('{0}<Directory Id="{1}" Name="{2}">' -f $Indent, $directoryId, $name))
        Write-PortableDirectory -DirectoryPath $directory.FullName -Indent ($Indent + '  ') -DirectoryLines $DirectoryLines -ComponentRefLines $ComponentRefLines
        $DirectoryLines.Add(('{0}</Directory>' -f $Indent))
    }
}

function New-InstallerFilesWxs {
    param(
        [string]$PortableRoot,
        [string]$OutputPath
    )

    $directoryLines = [System.Collections.Generic.List[string]]::new()
    $componentRefLines = [System.Collections.Generic.List[string]]::new()

    Write-PortableDirectory -DirectoryPath $PortableRoot -Indent '      ' -DirectoryLines $directoryLines -ComponentRefLines $componentRefLines

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
    $lines.Add('  <Fragment>')
    $lines.Add('    <DirectoryRef Id="INSTALLFOLDER">')
    foreach ($line in $directoryLines) { $lines.Add($line) }
    $lines.Add('    </DirectoryRef>')
    $lines.Add('  </Fragment>')
    $lines.Add('  <Fragment>')
    $lines.Add('    <ComponentGroup Id="PortableFiles">')
    foreach ($line in $componentRefLines) { $lines.Add($line) }
    $lines.Add('    </ComponentGroup>')
    $lines.Add('  </Fragment>')
    $lines.Add('</Wix>')

    Set-Content -Path $OutputPath -Value $lines -Encoding UTF8
}

$root = $PSScriptRoot
$srcRoot = Join-Path $root 'src'
$buildRoot = Join-Path $root 'build'
$portableRoot = Join-Path $root 'portable'
$portableAppRoot = Join-Path $portableRoot 'FnMappingTool'
$portableWorkerRoot = Join-Path $portableAppRoot 'runtime\worker'
$artifactsRoot = Join-Path $root 'artifacts'
$controllerProject = Join-Path $srcRoot 'FnMappingTool.Controller\FnMappingTool.Controller.csproj'
$workerProject = Join-Path $srcRoot 'FnMappingTool.Worker\FnMappingTool.Worker.csproj'
$installerProject = Join-Path $root 'installer\FnMappingTool.Setup\FnMappingTool.Setup.wixproj'
$generatedInstallerSource = Join-Path $root 'installer\FnMappingTool.Setup\PortableFiles.wxs'
$controllerBuildOutput = Join-Path $buildRoot 'bin\FnMappingTool.Controller\x64\Release\net8.0-windows10.0.19041.0'
$workerBuildOutput = Join-Path $buildRoot 'bin\FnMappingTool.Worker\x64\Release\net8.0-windows10.0.19041.0'
$installerOutputRoot = Join-Path $buildRoot 'bin\FnMappingTool.Setup\x64\Release'
$portableZipPath = Join-Path $artifactsRoot ("FnMappingTool-portable-v{0}.zip" -f $Version)
$msiPath = Join-Path $artifactsRoot ("FnMappingTool-setup-v{0}.msi" -f $Version)

if (-not (Test-Path $controllerProject) -or -not (Test-Path $workerProject)) {
    throw 'Worker or Controller project not found.'
}

$msbuild = Get-MsBuildPath

Get-Process FnMappingTool.Controller -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process FnMappingTool.Worker -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Remove-PathIfExists $portableAppRoot
Remove-PathIfExists $artifactsRoot
Remove-PathIfExists $installerOutputRoot
Remove-PathIfExists $generatedInstallerSource

New-Item -ItemType Directory -Force $portableAppRoot | Out-Null
New-Item -ItemType Directory -Force $portableWorkerRoot | Out-Null
New-Item -ItemType Directory -Force $artifactsRoot | Out-Null

Invoke-MSBuildProject -MSBuild $msbuild -ProjectPath $controllerProject -Properties @('/p:Configuration=Release', '/p:Platform=x64')
Copy-Item (Join-Path $controllerBuildOutput '*') $portableAppRoot -Recurse -Force

Invoke-MSBuildProject -MSBuild $msbuild -ProjectPath $workerProject -Properties @('/p:Configuration=Release', '/p:Platform=x64')
Copy-Item (Join-Path $workerBuildOutput '*') $portableWorkerRoot -Recurse -Force

Get-ChildItem $portableAppRoot -Recurse -Include *.pdb | Remove-Item -Force

if (-not $SkipZip) {
    if (Test-Path $portableZipPath) {
        Remove-Item -LiteralPath $portableZipPath -Force
    }

    Compress-Archive -Path $portableAppRoot -DestinationPath $portableZipPath -CompressionLevel Optimal
}

if (-not $SkipMsi) {
    if (-not (Test-Path $installerProject)) {
        throw 'Installer project not found.'
    }

    New-InstallerFilesWxs -PortableRoot $portableAppRoot -OutputPath $generatedInstallerSource

    $msiBuildSucceeded = $false
    for ($attempt = 1; $attempt -le 3 -and -not $msiBuildSucceeded; $attempt++) {
        & dotnet build $installerProject -c Release -p:InstallerPlatform=x64 -p:ProductVersion=$Version
        if ($LASTEXITCODE -eq 0) {
            $msiBuildSucceeded = $true
            break
        }

        if ($attempt -lt 3) {
            Write-Warning "MSI build attempt $attempt failed, retrying..."
            Start-Sleep -Seconds 2
        }
    }

    if (-not $msiBuildSucceeded) {
        throw "MSI build failed after multiple attempts."
    }

    $builtMsi = Get-ChildItem -Path $installerOutputRoot -Filter *.msi -Recurse | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -eq $builtMsi) {
        throw 'MSI build completed but no .msi output was found.'
    }

    Copy-Item -LiteralPath $builtMsi.FullName -Destination $msiPath -Force
}

Write-Host 'Portable package folder:' $portableAppRoot
Write-Host 'Portable launcher:' (Join-Path $portableAppRoot 'FnMappingTool.Controller.exe')
Write-Host 'Internal worker:' (Join-Path $portableWorkerRoot 'FnMappingTool.Worker.exe')
if (-not $SkipZip) {
    Write-Host 'Portable zip:' $portableZipPath
}
if (-not $SkipMsi) {
    Write-Host 'MSI installer:' $msiPath
}
