param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0',
    [switch]$Zip,
    [switch]$Msi
)

$ErrorActionPreference = 'Stop'

function Invoke-Dotnet {
    param([string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed: dotnet $($Arguments -join ' ')"
    }
}

function Remove-PathIfExists {
    param([string]$Path)

    if (Test-Path $Path) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force
        }
        catch {
            throw "Could not remove '$Path'. It may still be in use by a running process."
        }
    }
}

function Copy-DirectoryContent {
    param(
        [string]$SourceDirectory,
        [string]$DestinationDirectory
    )

    New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null
    $null = robocopy $SourceDirectory $DestinationDirectory /E /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) {
        throw "Robocopy failed while copying '$SourceDirectory' to '$DestinationDirectory'."
    }
}

function Get-HashHex {
    param([string]$Value)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())
    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        return ([System.BitConverter]::ToString($md5.ComputeHash($bytes))).Replace('-', '')
    }
    finally {
        $md5.Dispose()
    }
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

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())
    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        return ([guid]::new($md5.ComputeHash($bytes))).ToString().ToUpperInvariant()
    }
    finally {
        $md5.Dispose()
    }
}

function Write-WixDirectory {
    param(
        [string]$DirectoryPath,
        [string]$Indent,
        [System.Collections.Generic.List[string]]$DirectoryLines,
        [System.Collections.Generic.List[string]]$ComponentRefLines
    )

    foreach ($file in Get-ChildItem -LiteralPath $DirectoryPath -File | Sort-Object Name) {
        $componentId = New-StableId 'CMP' $file.FullName
        $fileId = New-StableId 'FIL' $file.FullName
        $guid = New-StableGuid $file.FullName
        $source = [System.Security.SecurityElement]::Escape($file.FullName)

        $DirectoryLines.Add(('{0}<Component Id="{1}" Guid="{2}">' -f $Indent, $componentId, $guid))
        $DirectoryLines.Add(('{0}  <File Id="{1}" Source="{2}" KeyPath="yes" />' -f $Indent, $fileId, $source))
        $DirectoryLines.Add(('{0}</Component>' -f $Indent))
        $ComponentRefLines.Add(('      <ComponentRef Id="{0}" />' -f $componentId))
    }

    foreach ($directory in Get-ChildItem -LiteralPath $DirectoryPath -Directory | Sort-Object Name) {
        $directoryId = New-StableId 'DIR' $directory.FullName
        $name = [System.Security.SecurityElement]::Escape($directory.Name)
        $DirectoryLines.Add(('{0}<Directory Id="{1}" Name="{2}">' -f $Indent, $directoryId, $name))
        Write-WixDirectory -DirectoryPath $directory.FullName -Indent ($Indent + '  ') -DirectoryLines $DirectoryLines -ComponentRefLines $ComponentRefLines
        $DirectoryLines.Add(('{0}</Directory>' -f $Indent))
    }
}

function New-PortableFilesWxs {
    param(
        [string]$PortableRoot,
        [string]$OutputPath
    )

    $directoryLines = [System.Collections.Generic.List[string]]::new()
    $componentRefLines = [System.Collections.Generic.List[string]]::new()

    Write-WixDirectory -DirectoryPath $PortableRoot -Indent '      ' -DirectoryLines $directoryLines -ComponentRefLines $componentRefLines

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
$legacyPackageRoot = Join-Path $buildRoot 'package'
$artifactsRoot = Join-Path $root 'artifacts'
$publishRoot = Join-Path $buildRoot 'publish'
$publishBuildRoot = Join-Path $buildRoot 'publish-bin'
$controllerPublishOutput = Join-Path $publishRoot 'controller'
$workerPublishOutput = Join-Path $publishRoot 'worker'
$controllerPublishBuildOutput = Join-Path $publishBuildRoot 'controller'
$workerPublishBuildOutput = Join-Path $publishBuildRoot 'worker'
$portableRoot = Join-Path $artifactsRoot 'MeowBox'
$portableWorkerRoot = Join-Path $portableRoot 'runtime\worker'
$portableZipPath = Join-Path $artifactsRoot ("MeowBox-portable-v{0}.zip" -f $Version)
$msiPath = Join-Path $artifactsRoot ("MeowBox-setup-v{0}.msi" -f $Version)
$controllerProject = Join-Path $srcRoot 'MeowBox.Controller\MeowBox.Controller.csproj'
$workerProject = Join-Path $srcRoot 'MeowBox.Worker\MeowBox.Worker.csproj'
$installerProject = Join-Path $srcRoot 'MeowBox.Setup\MeowBox.Setup.wixproj'
$installerOutputRoot = Join-Path $buildRoot 'bin\MeowBox.Setup'
$portableFilesWxs = Join-Path $srcRoot 'MeowBox.Setup\PortableFiles.wxs'

Remove-PathIfExists $controllerPublishOutput
Remove-PathIfExists $workerPublishOutput
Remove-PathIfExists $publishBuildRoot
Remove-PathIfExists $legacyPackageRoot
Remove-PathIfExists $artifactsRoot
Remove-PathIfExists $installerOutputRoot
Remove-PathIfExists $portableFilesWxs

New-Item -ItemType Directory -Force -Path $controllerPublishOutput | Out-Null
New-Item -ItemType Directory -Force -Path $workerPublishOutput | Out-Null
New-Item -ItemType Directory -Force -Path $controllerPublishBuildOutput | Out-Null
New-Item -ItemType Directory -Force -Path $workerPublishBuildOutput | Out-Null
New-Item -ItemType Directory -Force -Path $portableWorkerRoot | Out-Null
New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null

$publishArguments = @(
    '-c', 'Release',
    '-r', 'win-x64',
    '-p:Platform=x64',
    '-p:SelfContained=false'
)

$controllerPublishArguments = @(
    'publish',
    $controllerProject,
    '-o', $controllerPublishOutput,
    ('-p:OutputPath={0}' -f $controllerPublishBuildOutput),
    '-p:SkipBuildWorkerForLocalRuntime=true'
) + $publishArguments
Invoke-Dotnet $controllerPublishArguments
Copy-DirectoryContent -SourceDirectory $controllerPublishOutput -DestinationDirectory $portableRoot

$workerPublishArguments = @(
    'publish',
    $workerProject,
    '-o', $workerPublishOutput,
    ('-p:OutputPath={0}' -f $workerPublishBuildOutput)
) + $publishArguments
Invoke-Dotnet $workerPublishArguments
Copy-DirectoryContent -SourceDirectory $workerPublishOutput -DestinationDirectory $portableWorkerRoot

Get-ChildItem -Path $portableRoot -Recurse -Include *.pdb | Remove-Item -Force

if ($Zip) {
    if (Test-Path $portableZipPath) {
        Remove-Item -LiteralPath $portableZipPath -Force
    }

    Compress-Archive -Path $portableRoot -DestinationPath $portableZipPath -CompressionLevel Optimal
}

if ($Msi) {
    New-PortableFilesWxs -PortableRoot $portableRoot -OutputPath $portableFilesWxs
    Invoke-Dotnet @(
        'build',
        $installerProject,
        '-c', 'Release',
        '-p:InstallerPlatform=x64',
        ('-p:ProductVersion={0}' -f $Version)
    )

    $builtMsi = Get-ChildItem -Path $installerOutputRoot -Filter *.msi -Recurse |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $builtMsi) {
        throw 'MSI build completed but no .msi output was found.'
    }

    Copy-Item -LiteralPath $builtMsi.FullName -Destination $msiPath -Force
}

Remove-PathIfExists $portableFilesWxs
Remove-PathIfExists $publishRoot
Remove-PathIfExists $publishBuildRoot
Remove-PathIfExists $installerOutputRoot

Write-Host 'Portable folder:' $portableRoot
Write-Host 'Launcher:' (Join-Path $portableRoot 'MeowBox.Controller.exe')
Write-Host 'Internal worker:' (Join-Path $portableRoot 'runtime\worker\MeowBox.Worker.exe')
if ($Zip) {
    Write-Host 'Portable zip:' $portableZipPath
}
if ($Msi) {
    Write-Host 'MSI installer:' $msiPath
}
