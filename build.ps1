<#
.SYNOPSIS
  Baut das QC-Plugin 99-1 "Wetter" und packt es als dist\99-1_Wetter.hsp.

.DESCRIPTION
  Braucht weder Visual Studio noch ein .NET SDK: verwendet den C#-Compiler, der zum .NET Framework 4
  von Windows gehoert (C# 5). Die Gira/DaCom-Bibliotheken werden NICHT mitgeliefert; sie muessen aus der
  eigenen Installation nach lib\gira kopiert werden (der Ordner steht in .gitignore):
    config_framework.dll      aus  <Experte>\quad_config\config
    hs_client_framework.dll   aus  dem Ordner des QuadClients (neben hs_client_net.exe)
  Mit -ExperteDir / -QuadClientDir kopiert das Skript sie selbst dorthin.

.PARAMETER NoBump
  Versionsnummer in version.xml nicht erhoehen (Standard: letzte Stelle + 1 bei jedem Bau).
.PARAMETER NoTest
  Testprogramme (ConfigTest, Vorschau) nicht ausfuehren. Die Vorschaubilder im Paket fehlen dann.
.PARAMETER Live
  Tests mit echter Abfrage bei Open-Meteo (Ortssuche und Vorhersage).
.PARAMETER DeployTo
  Ordner, in den 99-1_client.dll zusaetzlich kopiert wird (Entwicklerpfad <assembly path="..."/> des QuadClients).
#>
[CmdletBinding()]
param(
    [switch]$NoBump,
    [switch]$NoTest,
    [switch]$Live,
    [string]$ExperteDir,
    [string]$QuadClientDir,
    [string]$DeployTo
)
$ErrorActionPreference = 'Stop'

$PluginId   = '99-1'
$PluginName = 'Wetter'
$Root   = $PSScriptRoot
$Plugin = Join-Path $Root "plugins\${PluginId}_$PluginName"
$Lib    = Join-Path $Root 'lib\gira'
$Bin    = Join-Path $Root 'bin'
$Obj    = Join-Path $Root 'obj'
$Dist   = Join-Path $Root 'dist'

# ---- Gira-Bibliotheken ----
New-Item -ItemType Directory -Force $Lib, $Bin, $Obj, $Dist | Out-Null
if ($ExperteDir) {
    $c = Join-Path $ExperteDir 'quad_config\config'
    Copy-Item (Join-Path $c 'config_framework.dll'), (Join-Path $c 'config_lib.dll') $Lib
}
if ($QuadClientDir) {
    Copy-Item (Join-Path $QuadClientDir 'hs_client_framework.dll'), (Join-Path $QuadClientDir 'QC.Common.dll') $Lib
}
foreach ($f in 'config_framework.dll', 'hs_client_framework.dll') {
    if (-not (Test-Path (Join-Path $Lib $f))) {
        throw "lib\gira\$f fehlt. Aus der eigenen Gira-Installation kopieren oder -ExperteDir / -QuadClientDir angeben (siehe README)."
    }
}

# ---- Compiler ----
$Fw  = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$Csc = Join-Path $Fw 'csc.exe'
if (-not (Test-Path $Csc)) { throw "C#-Compiler nicht gefunden: $Csc" }
$Wpf = 'PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll' | ForEach-Object { '/r:' + (Join-Path $Fw "WPF\$_") }
$Wpf += '/r:' + (Join-Path $Fw 'System.Xaml.dll')

function Invoke-Csc {
    param([string]$Out, [string]$Target, [string[]]$Refs, [string[]]$Sources)
    $a = @('/nologo', '/warn:4', '/optimize+', '/debug:pdbonly', "/target:$Target", "/out:$Out") + $Refs + $Sources
    & $Csc @a
    if ($LASTEXITCODE -ne 0) { throw "Uebersetzen fehlgeschlagen: $Out" }
}

# ---- Version ----
$VersionFile = Join-Path $Plugin 'pkg\version.xml'
$vx = Get-Content $VersionFile -Raw -Encoding UTF8
if ($vx -notmatch '<version>(\d+)\.(\d+)\.(\d+)</version>') { throw 'version.xml: <version>a.b.c</version> nicht gefunden' }
$build = [int]$Matches[3]
if (-not $NoBump) { $build++ }
$Version = "$($Matches[1]).$($Matches[2]).$build"
$vx = $vx -replace '<version>\d+\.\d+\.\d+</version>', "<version>$Version</version>"   # wird erst nach erfolgreichem Bau gespeichert
Write-Host "QC-Plugin $PluginId $PluginName, Version $Version"

$AsmInfo = Join-Path $Obj 'AssemblyInfo.cs'
@"
using System.Reflection;
[assembly: AssemblyProduct("QC-Plugin $PluginId $PluginName")]
[assembly: AssemblyCompany("Roman by KNXUF")]
[assembly: AssemblyCopyright("GPL-3.0, https://github.com/BadSmiley0815/QC-Plugin")]
[assembly: AssemblyVersion("$Version.0")]
[assembly: AssemblyFileVersion("$Version.0")]
"@ | Set-Content $AsmInfo -Encoding UTF8

# ---- Uebersetzen ----
$Shared    = Get-ChildItem (Join-Path $Plugin 'src\Shared') -Filter *.cs | ForEach-Object FullName
$ClientSrc = Get-ChildItem (Join-Path $Plugin 'src\Wetter.Client') -Filter *.cs | ForEach-Object FullName
$ConfigSrc = Get-ChildItem (Join-Path $Plugin 'src\Wetter.Config') -Filter *.cs | ForEach-Object FullName
$ClientDll = Join-Path $Bin "${PluginId}_client.dll"
$ConfigDll = Join-Path $Bin 'plugin_config.dll'
$RefClient = '/r:' + (Join-Path $Lib 'hs_client_framework.dll')
$RefConfig = '/r:' + (Join-Path $Lib 'config_framework.dll')

Invoke-Csc $ClientDll 'library' (@($RefClient) + $Wpf) (@($AsmInfo) + $Shared + $ClientSrc)
$LangRes = '/resource:' + (Join-Path $Plugin 'pkg\config_lang.xml') + ',QcWetter.config_lang.xml'   # Rueckfall-Texte
Invoke-Csc $ConfigDll 'library' @($RefConfig, $LangRes) (@($AsmInfo) + $Shared + $ConfigSrc)

# ---- Paket zusammenstellen ----
$Stage = Join-Path $Obj 'hsp'
if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Force $Stage, (Join-Path $Stage 'thumbs') | Out-Null
$Pkg = Join-Path $Plugin 'pkg'
Copy-Item (Join-Path $Pkg '*.xml') $Stage
[IO.File]::WriteAllText((Join-Path $Stage 'version.xml'), $vx, (New-Object Text.UTF8Encoding($false)))
Copy-Item $ClientDll, $ConfigDll $Stage
foreach ($n in 1..4) {                                   # je Design des QuadClients dieselbe (leere) Design-Datei
    $d = Join-Path $Stage "design_$n"
    New-Item -ItemType Directory -Force $d | Out-Null
    Copy-Item (Join-Path $Pkg "design\${PluginId}_design.xml") $d
}

# ---- Tests und Vorschaubilder ----
if (-not $NoTest) {
    Copy-Item (Join-Path $Lib '*.dll') $Bin               # nur fuer die Testprogramme (bin\ steht in .gitignore)
    $Tools = Join-Path $Plugin 'tools'
    $ConfigTest = Join-Path $Bin 'ConfigTest.exe'
    $Vorschau   = Join-Path $Bin 'Vorschau.exe'
    Invoke-Csc $ConfigTest 'exe' @($RefConfig, "/r:$ConfigDll") @((Join-Path $Tools 'ConfigTest.cs'))
    Invoke-Csc $Vorschau 'exe' (@($RefClient, "/r:$ClientDll") + $Wpf) @((Join-Path $Tools 'Vorschau.cs'))

    $mode = if ($Live) { 'live' } else { 'offline' }
    & $ConfigTest (Join-Path $Pkg 'config_lang.xml') $mode
    if ($LASTEXITCODE -ne 0) { throw 'ConfigTest meldet Fehler' }
    & $ConfigTest (Join-Path $Pkg 'config_lang.xml') offline roh | Out-Null    # Experte liefert keine Texte -> eingebaute Liste
    if ($LASTEXITCODE -ne 0) { throw 'ConfigTest (ohne Uebersetzung durch den Experten) meldet Fehler' }

    $Img = Join-Path $Root 'docs\img'
    & $Vorschau (Join-Path $Img "${PluginId}_wetter_dunkel.png") dark sample 680 320
    & $Vorschau (Join-Path $Img "${PluginId}_wetter_hell.png") light sample 680 320
    if ($Live) { & $Vorschau (Join-Path $Obj 'vorschau_live.png') dark live 680 320 }
    foreach ($l in 'de', 'en') { & $Vorschau (Join-Path $Stage "thumbs\prev_$l.png") dark sample 681 319 | Out-Null }
    if ($LASTEXITCODE -ne 0) { throw 'Vorschau meldet Fehler' }
}

# ---- .hsp (ZIP mit '/' als Trenner) ----
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Hsp = Join-Path $Dist "${PluginId}_$PluginName.hsp"
if (Test-Path $Hsp) { Remove-Item $Hsp -Force }
$zip = [IO.Compression.ZipFile]::Open($Hsp, 'Create')
try {
    Get-ChildItem $Stage -Recurse -File | Sort-Object FullName | ForEach-Object {
        $name = $_.FullName.Substring($Stage.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $name, 'Optimal') | Out-Null
    }
} finally { $zip.Dispose() }
if (-not $NoBump) { [IO.File]::WriteAllText($VersionFile, $vx, (New-Object Text.UTF8Encoding($false))) }

if ($DeployTo) {
    New-Item -ItemType Directory -Force $DeployTo | Out-Null
    Copy-Item $ClientDll $DeployTo -Force
    Write-Host "Client-DLL kopiert nach $DeployTo"
}
Write-Host ("Fertig: {0} ({1:N0} Bytes)" -f $Hsp, (Get-Item $Hsp).Length)
