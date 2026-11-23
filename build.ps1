<#
.SYNOPSIS
  Baut die QC-Plugins (99-1 "Wetter", 99-2 "Kamera", 99-3 "Energiefluss") und packt sie als dist\<ID>_<Name>.hsp.

.DESCRIPTION
  Braucht weder Visual Studio noch ein .NET SDK: verwendet den C#-Compiler, der zum .NET Framework 4
  von Windows gehoert (C# 5). Die Gira/DaCom-Bibliotheken werden NICHT mitgeliefert; sie muessen aus der
  eigenen Installation nach lib\gira kopiert werden (der Ordner steht in .gitignore):
    config_framework.dll      aus  <Experte>\quad_config\config
    hs_client_framework.dll   aus  dem Ordner des QuadClients (neben hs_client_net.exe)
  Mit -ExperteDir / -QuadClientDir kopiert das Skript sie selbst dorthin.

.PARAMETER Plugin
  Nur dieses Plugin bauen (z. B. 99-2). Ohne Angabe werden alle gebaut.
.PARAMETER NoBump
  Versionsnummer in version.xml nicht erhoehen (Standard: letzte Stelle + 1 bei jedem Bau, je gebautem Plugin).
.PARAMETER NoTest
  Testprogramme nicht ausfuehren. Die Vorschaubilder im Paket fehlen dann.
.PARAMETER Live
  Tests von 99-1 mit echter Abfrage bei Open-Meteo (Ortssuche und Vorhersage).
.PARAMETER DeployTo
  Ordner, in den die <ID>_client.dll zusaetzlich kopiert werden (Entwicklerpfad <assembly path="..."/> des QuadClients).
#>
[CmdletBinding()]
param(
    [string]$Plugin,
    [switch]$NoBump,
    [switch]$NoTest,
    [switch]$Live,
    [string]$ExperteDir,
    [string]$QuadClientDir,
    [string]$DeployTo
)
$ErrorActionPreference = 'Stop'

$Plugins = @(
    @{ Id = '99-1'; Name = 'Wetter'; Ns = 'QcWetter'; Client = 'Wetter.Client'; Config = 'Wetter.Config' },
    @{ Id = '99-2'; Name = 'Kamera'; Ns = 'QcKamera'; Client = 'Kamera.Client'; Config = 'Kamera.Config' },
    @{ Id = '99-3'; Name = 'Energiefluss'; Ns = 'QcEnergie'; Client = 'Energie.Client'; Config = 'Energie.Config' }
)
if ($Plugin) {
    $Plugins = @($Plugins | Where-Object { $_.Id -eq $Plugin -or $_.Name -eq $Plugin })
    if ($Plugins.Count -eq 0) { throw "Unbekanntes Plugin: $Plugin" }
}

$Root = $PSScriptRoot
$Lib  = Join-Path $Root 'lib\gira'
$Dist = Join-Path $Root 'dist'
$Img  = Join-Path $Root 'docs\img'

# ---- Gira-Bibliotheken ----
New-Item -ItemType Directory -Force $Lib, $Dist | Out-Null
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
$RefClient = '/r:' + (Join-Path $Lib 'hs_client_framework.dll')
$RefConfig = '/r:' + (Join-Path $Lib 'config_framework.dll')

function Invoke-Csc {
    param([string]$Out, [string]$Target, [string[]]$Refs, [string[]]$Sources, [switch]$X86)
    $a = @('/nologo', '/warn:4', '/optimize+', '/debug:pdbonly', "/target:$Target", "/out:$Out")
    if ($X86) { $a += '/platform:x86' }                    # wie der QuadClient: 32-Bit-Prozess
    $a += $Refs + $Sources
    & $Csc @a
    if ($LASTEXITCODE -ne 0) { throw "Uebersetzen fehlgeschlagen: $Out" }
}

function Invoke-Test {
    param([string]$Exe, [string[]]$Arguments, [string]$What, [switch]$Quiet)
    if ($Quiet) { & $Exe @Arguments | Out-Null } else { & $Exe @Arguments }
    if ($LASTEXITCODE -ne 0) { throw "$What meldet Fehler" }
}

foreach ($P in $Plugins) {
    $Id = $P.Id
    $PluginDir = Join-Path $Root "plugins\${Id}_$($P.Name)"
    $Pkg   = Join-Path $PluginDir 'pkg'
    $Tools = Join-Path $PluginDir 'tools'
    $Bin   = Join-Path $Root "bin\$Id"
    $Obj   = Join-Path $Root "obj\$Id"
    New-Item -ItemType Directory -Force $Bin, $Obj | Out-Null

    # ---- Version ----
    $VersionFile = Join-Path $Pkg 'version.xml'
    $vx = Get-Content $VersionFile -Raw -Encoding UTF8
    if ($vx -notmatch '<version>(\d+)\.(\d+)\.(\d+)</version>') { throw "$VersionFile : <version>a.b.c</version> nicht gefunden" }
    $build = [int]$Matches[3]
    if (-not $NoBump) { $build++ }
    $Version = "$($Matches[1]).$($Matches[2]).$build"
    $vx = $vx -replace '<version>\d+\.\d+\.\d+</version>', "<version>$Version</version>"   # wird erst nach erfolgreichem Bau gespeichert
    Write-Host ""
    Write-Host "QC-Plugin $Id $($P.Name), Version $Version"

    $AsmInfo = Join-Path $Obj 'AssemblyInfo.cs'
    @"
using System.Reflection;
[assembly: AssemblyProduct("QC-Plugin $Id $($P.Name)")]
[assembly: AssemblyCompany("Roman by KNXUF")]
[assembly: AssemblyCopyright("GPL-3.0, https://github.com/BadSmiley0815/QC-Plugin")]
[assembly: AssemblyVersion("$Version.0")]
[assembly: AssemblyFileVersion("$Version.0")]
"@ | Set-Content $AsmInfo -Encoding UTF8

    # ---- Uebersetzen ----
    $Shared    = Get-ChildItem (Join-Path $PluginDir 'src\Shared') -Filter *.cs | ForEach-Object FullName
    $ClientSrc = Get-ChildItem (Join-Path $PluginDir "src\$($P.Client)") -Filter *.cs | ForEach-Object FullName
    $ConfigSrc = Get-ChildItem (Join-Path $PluginDir "src\$($P.Config)") -Filter *.cs | ForEach-Object FullName
    $ClientDll = Join-Path $Bin "${Id}_client.dll"
    # Im Paket heisst die Datei plugin_config.dll, die Assembly aber <ID>_config (wie bei Gira). Mit gleichem
    # Assemblynamen laedt der Experte nur die Einstellungen des ersten Plugins, das zweite "kann nicht instanziert werden".
    $ConfigDll = Join-Path $Bin "${Id}_config.dll"
    Remove-Item (Join-Path $Bin 'plugin_config.*') -Force -ErrorAction SilentlyContinue

    Invoke-Csc $ClientDll 'library' (@($RefClient) + $Wpf) (@($AsmInfo) + $Shared + $ClientSrc)
    $LangRes = '/resource:' + (Join-Path $Pkg 'config_lang.xml') + ",$($P.Ns).config_lang.xml"   # Rueckfall-Texte
    Invoke-Csc $ConfigDll 'library' @($RefConfig, $LangRes) (@($AsmInfo) + $Shared + $ConfigSrc)

    # ---- Paket zusammenstellen ----
    $Stage = Join-Path $Obj 'hsp'
    if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
    New-Item -ItemType Directory -Force $Stage, (Join-Path $Stage 'thumbs') | Out-Null
    Copy-Item (Join-Path $Pkg '*.xml') $Stage
    [IO.File]::WriteAllText((Join-Path $Stage 'version.xml'), $vx, (New-Object Text.UTF8Encoding($false)))
    Copy-Item $ClientDll $Stage
    Copy-Item $ConfigDll (Join-Path $Stage 'plugin_config.dll')
    foreach ($n in 1..4) {                                   # je Design des QuadClients dieselbe (leere) Design-Datei
        $d = Join-Path $Stage "design_$n"
        New-Item -ItemType Directory -Force $d | Out-Null
        Copy-Item (Join-Path $Pkg "design\${Id}_design.xml") $d
    }

    # ---- Tests und Vorschaubilder ----
    if (-not $NoTest) {
        Copy-Item (Join-Path $Lib '*.dll') $Bin               # nur fuer die Testprogramme (bin\ steht in .gitignore)
        $ConfigTest = Join-Path $Bin 'ConfigTest.exe'
        $Vorschau   = Join-Path $Bin 'Vorschau.exe'
        $Lang       = Join-Path $Pkg 'config_lang.xml'

        if ($Id -eq '99-1') {
            Invoke-Csc $ConfigTest 'exe' @($RefConfig, "/r:$ConfigDll") @((Join-Path $Tools 'ConfigTest.cs'))
            Invoke-Csc $Vorschau 'exe' (@($RefClient, "/r:$ClientDll") + $Wpf) @((Join-Path $Tools 'Vorschau.cs'))
            $mode = if ($Live) { 'live' } else { 'offline' }
            Invoke-Test $ConfigTest @($Lang, $mode) 'ConfigTest'
            Invoke-Test $ConfigTest @($Lang, 'offline', 'roh') 'ConfigTest (ohne Uebersetzung durch den Experten)' -Quiet
            & $Vorschau (Join-Path $Img "${Id}_wetter_dunkel.png") dark sample 680 320
            & $Vorschau (Join-Path $Img "${Id}_wetter_hell.png") light sample 680 320
            if ($Live) { & $Vorschau (Join-Path $Obj 'vorschau_live.png') dark live 680 320 }
            foreach ($l in 'de', 'en') { & $Vorschau (Join-Path $Stage "thumbs\prev_$l.png") dark sample 681 319 | Out-Null }
            if ($LASTEXITCODE -ne 0) { throw 'Vorschau meldet Fehler' }
        }
        if ($Id -eq '99-2') {
            # Alle Tests laufen gegen eine nachgebaute Kamera auf 127.0.0.1 (tools\TestKamera.cs), ohne echte Kamera.
            $Cam = Join-Path $Tools 'TestKamera.cs'
            $Src = Join-Path $Tools 'TestQuelle.cs'
            $DecoderTest = Join-Path $Bin 'DecoderTest.exe'
            $RtspTest    = Join-Path $Bin 'RtspTest.exe'
            Invoke-Csc $DecoderTest 'exe' (@("/r:$ClientDll") + $Wpf) @($Src, (Join-Path $Tools 'DecoderTest.cs')) -X86
            Invoke-Csc $RtspTest 'exe' @("/r:$ClientDll") @($Src, $Cam, (Join-Path $Tools 'RtspTest.cs')) -X86
            Invoke-Csc $ConfigTest 'exe' @($RefConfig, "/r:$ConfigDll") @($Cam, (Join-Path $Tools 'ConfigTest.cs')) -X86
            Invoke-Csc $Vorschau 'exe' (@($RefClient, "/r:$ClientDll") + $Wpf) @($Src, $Cam, (Join-Path $Tools 'Vorschau.cs')) -X86
            # Pruefprogramm fuer echte Kameras; wird nur gebaut, nicht ausgefuehrt (fragt das Kennwort ab)
            Invoke-Csc (Join-Path $Bin 'KameraTest.exe') 'exe' (@("/r:$ClientDll") + $Wpf) @((Join-Path $Tools 'KameraTest.cs')) -X86
            Write-Host 'Decoder (H.264 von Windows):'
            Invoke-Test $DecoderTest @() 'DecoderTest'
            Write-Host 'RTSP-Empfang:'
            Invoke-Test $RtspTest @() 'RtspTest'
            Write-Host 'Einstellungen im Experten:'
            Invoke-Test $ConfigTest @($Lang) 'ConfigTest'
            Invoke-Test $ConfigTest @($Lang, 'roh') 'ConfigTest (ohne Uebersetzung durch den Experten)' -Quiet
            Write-Host 'Anzeige:'
            Invoke-Test $Vorschau @((Join-Path $Obj 'vorschau_rtsp.png'), 'dark', 'rtsp', '680', '320') 'Vorschau (RTSP)'
            Invoke-Test $Vorschau @((Join-Path $Obj 'vorschau_mjpeg.png'), 'light', 'mjpeg', '680', '320') 'Vorschau (MJPEG)'
            Invoke-Test $Vorschau @((Join-Path $Obj 'vorschau_jpeg.png'), 'dark', 'jpeg', '680', '320') 'Vorschau (Einzelbilder)'
            Invoke-Test $Vorschau @((Join-Path $Obj 'vorschau_aus.png'), 'dark', 'aus', '680', '320') 'Vorschau (Kamera aus)'
            Invoke-Test $Vorschau @((Join-Path $Obj 'vorschau_leer.png'), 'dark', 'leer', '680', '320') 'Vorschau (keine Kamera)'
            # Bilder fuer README, Hilfe und Paket: gezeichnete Beispielszene statt des Testbilds
            Invoke-Test $Vorschau @((Join-Path $Img "${Id}_kamera_dunkel.png"), 'dark', 'szene', '680', '320') 'Vorschau (Beispielszene)'
            Invoke-Test $Vorschau @((Join-Path $Img "${Id}_kamera_hell.png"), 'light', 'szene', '680', '320') 'Vorschau (Beispielszene hell)'
            foreach ($l in 'de', 'en') { Invoke-Test $Vorschau @((Join-Path $Stage "thumbs\prev_$l.png"), 'dark', 'szene', '681', '319') 'Vorschau (Paketbild)' -Quiet }
        }
        if ($Id -eq '99-3') {
            Invoke-Csc $ConfigTest 'exe' @($RefConfig, "/r:$ConfigDll") @((Join-Path $Tools 'ConfigTest.cs'))
            Invoke-Csc $Vorschau 'exe' (@($RefClient, "/r:$ClientDll") + $Wpf) @((Join-Path $Tools 'Vorschau.cs'))
            Write-Host 'Einstellungen im Experten:'
            Invoke-Test $ConfigTest @($Lang) 'ConfigTest'
            Invoke-Test $ConfigTest @($Lang, 'roh') 'ConfigTest (ohne Uebersetzung durch den Experten)' -Quiet
            Write-Host 'Anzeige:'
            Invoke-Test $Vorschau @((Join-Path $Img "${Id}_energiefluss_dunkel.png"), 'dark', 'sample', '680', '320') 'Vorschau'
            Invoke-Test $Vorschau @((Join-Path $Img "${Id}_energiefluss_hell.png"), 'light', 'sample', '680', '320') 'Vorschau (hell)'
            Invoke-Test $Vorschau @((Join-Path $Img "${Id}_energiefluss_einzelwerte.png"), 'dark', 'einzel', '680', '320') 'Vorschau (Einzelwerte)'
            Invoke-Test $Vorschau @((Join-Path $Obj 'vorschau_einzel_hell.png'), 'light', 'einzel', '680', '320') 'Vorschau (Einzelwerte hell)'
            Invoke-Test $Vorschau @((Join-Path $Obj 'vorschau_viele.png'), 'dark', 'viele', '680', '320') 'Vorschau (17 Verbraucher)'
            Invoke-Test $Vorschau @((Join-Path $Obj 'vorschau_ruhe.png'), 'dark', 'ruhe', '680', '320') 'Vorschau (Nacht)'
            Invoke-Test $Vorschau @((Join-Path $Obj 'vorschau_leer.png'), 'light', 'leer', '680', '320') 'Vorschau (kein KO)'
            foreach ($l in 'de', 'en') { Invoke-Test $Vorschau @((Join-Path $Stage "thumbs\prev_$l.png"), 'dark', 'sample', '681', '319') 'Vorschau (Paketbild)' -Quiet }
        }
    }

    # ---- Hilfe: help\p<ID>_<Sprache>.html -> help/p<ID>_<Sprache>.pdf (Edge druckt ohne Fenster) ----
    # Nach den Tests, weil die Hilfe die dort gezeichneten Bilder aus docs\img einbindet.
    $HelpSrc = Get-ChildItem (Join-Path $PluginDir 'help') -Filter "p${Id}_*.html" -ErrorAction SilentlyContinue
    if ($HelpSrc) {
        $Edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe" |
            Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $Edge) { Write-Warning 'Microsoft Edge nicht gefunden: Paket ohne Hilfe (help\*.pdf).' }
        else {
            $HelpOut = Join-Path $Stage 'help'
            $EdgeProfile = Join-Path $Obj 'edge'
            New-Item -ItemType Directory -Force $HelpOut, $EdgeProfile | Out-Null
            foreach ($h in $HelpSrc) {
                $pdf = Join-Path $HelpOut ($h.BaseName + '.pdf')
                $ea = @('--headless', '--disable-gpu', '--no-first-run', '--no-pdf-header-footer',
                        "--user-data-dir=`"$EdgeProfile`"", "--print-to-pdf=`"$pdf`"", ('"' + ([Uri]$h.FullName).AbsoluteUri + '"'))
                Start-Process $Edge -ArgumentList $ea -Wait -WindowStyle Hidden
                if (-not (Test-Path $pdf) -or (Get-Item $pdf).Length -lt 1000) { throw "Hilfe nicht erzeugt: $pdf" }
            }
        }
    }

    # ---- .hsp (ZIP mit '/' als Trenner) ----
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $Hsp = Join-Path $Dist "${Id}_$($P.Name).hsp"
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
}
