#Requires -Version 5.1
# Non-installing checks: no downloads, registry writes, UAC, or driver installation.
$ErrorActionPreference = 'Stop'
foreach ($file in Get-ChildItem $PSScriptRoot -Filter '*.ps1') {
    $parseErrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref] $null, [ref] $parseErrors)
    if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
}
. (Join-Path $PSScriptRoot 'Install-Dependencies.ps1')

function Assert($Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$packages = Get-Content (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
Assert (@($packages.PSObject.Properties).Count -eq 4) 'Expected four pinned packages.'
foreach ($entry in $packages.PSObject.Properties) {
    $package = $entry.Value
    Assert (([uri] $package.url).Scheme -eq 'https') 'Downloads must use HTTPS.'
    Assert ($package.sha256 -match '^[a-f0-9]{64}$') 'Every download requires a pinned SHA-256.'
    Assert ($package.file -eq [IO.Path]::GetFileName($package.file)) 'Package filename must be local.'
}

$testFile = [IO.Path]::GetTempFileName()
try {
    [IO.File]::WriteAllText($testFile, 'AudioZen hash check')
    Assert-PackageHash $testFile (Get-FileHash $testFile -Algorithm SHA256).Hash
    $rejected = $false
    try { Assert-PackageHash $testFile ('0' * 64) } catch { $rejected = $true }
    Assert $rejected 'A modified download must be rejected before execution.'
} finally { Remove-Item -LiteralPath $testFile }

# Exercise the real registry reader and dependency checks with endpoint fixtures.
& {
    function Get-ChildItem { @('main', 'aux', 'cable') | ForEach-Object { [pscustomobject]@{ PSPath = $_ } } }
    function Get-ItemProperty {
        param($LiteralPath)
        $description = switch ($LiteralPath) {
            'main\Properties' { 'Voicemeeter Input' }
            'aux\Properties' { 'Voicemeeter AUX Input' }
            'cable\Properties' { 'CABLE Input' }
        }
        @{ '{a45c254e-df1c-4efd-8020-67d146a850e0},14' = $script:FixtureFriendlyName
           '{a45c254e-df1c-4efd-8020-67d146a850e0},2' = $description }
    }
    foreach ($friendlyName in @($null, '', 'My renamed endpoint')) {
        $script:FixtureFriendlyName = $friendlyName
        Assert (Test-DependencyInstalled 'Voicemeeter') 'DeviceDesc must identify main and AUX when FriendlyName is missing or renamed.'
        Assert (Test-DependencyInstalled 'VbCable') 'DeviceDesc must identify VB-CABLE when FriendlyName is missing or renamed.'
    }
    function Get-ItemProperty { @{ '{a45c254e-df1c-4efd-8020-67d146a850e0},14' = 'Speakers' } }
    Assert (-not (Test-DependencyInstalled 'Voicemeeter')) 'Unrelated endpoints must not satisfy Voicemeeter.'
    Assert (-not (Test-DependencyInstalled 'VbCable')) 'Unrelated endpoints must not satisfy VB-CABLE.'
}

# Replace only the OS/process boundary; exercise the real orchestration below.
function Reset-Scenario {
    $script:Installed = @{ Voicemeeter = $true; VbCable = $true; EqualizerApo = $true; Melda = $true; Speech = $true }
    $script:Events = [Collections.Generic.List[string]]::new()
    $script:PendingReboot = $false
    $script:FailInstaller = ''
    $script:UndetectedInstaller = ''
    $script:RegisteredDriver = ''
    $script:IncludeMelda = $false
    $script:IncludeSpeech = $false
}
function Test-DependencyInstalled([string] $Name) { return $script:Installed[$Name] }
function Test-DriverRegistered([string] $Name) { return $Name -eq $script:RegisteredDriver }
function Test-SetupRebootPending { return $script:PendingReboot }
function Set-RebootRequired { $script:RestartRequired = $true }
function Get-ApoDirectory { return 'C:\Program Files\EqualizerAPO' }
function Get-DependencyInstaller([string] $Name) {
    $script:Events.Add("download:$Name")
    return $Name
}
function Invoke-Installer([string] $Path) {
    $script:Events.Add("install:$Path")
    if ($Path -eq $script:FailInstaller) { throw 'Simulated installer cancellation.' }
    if ($Path -ne $script:UndetectedInstaller) { $script:Installed[$Path] = $true }
}
function Install-Melda {
    $script:Events.Add('melda')
    $script:Installed.Melda = $true
}
function Install-Speech {
    $script:Events.Add('speech')
    $script:Installed.Speech = $true
}

Reset-Scenario
Assert ((Invoke-DependencySetup) -eq 0) 'An already prepared machine must succeed.'
Assert ($script:Events.Count -eq 0) 'Existing dependencies must not be reinstalled.'

Reset-Scenario
$script:Installed.Voicemeeter = $false
$script:Installed.VbCable = $false
$script:Installed.EqualizerApo = $false
Assert ((Invoke-DependencySetup) -eq 3010) 'New drivers require a restart.'
Assert (($script:Events -join ',') -eq 'download:Voicemeeter,install:Voicemeeter,download:VbCable,install:VbCable') 'Install drivers before APO.'
$script:Events.Clear()
Assert ((Invoke-DependencySetup) -eq 3010) 'After the driver restart, APO installation needs a restart.'
Assert (($script:Events -join ',') -eq 'download:EqualizerApo,install:EqualizerApo') 'Resume must skip installed drivers.'
$script:Events.Clear()
Assert ((Invoke-DependencySetup) -eq 0) 'After APO restart, installation can complete.'
Assert ($script:Events.Count -eq 0) 'Resume must not reinstall APO.'

Reset-Scenario
$script:PendingReboot = $true
$script:Installed.EqualizerApo = $false
Assert ((Invoke-DependencySetup) -eq 3010) 'Rerunning before reboot must remain blocked.'
Assert ($script:Events.Count -eq 0) 'Pending reboot must not start another installer.'

Reset-Scenario
$script:Installed.VbCable = $false
$script:RegisteredDriver = 'VbCable'
$failed = $false
try { $null = Invoke-DependencySetup } catch { $failed = $true }
Assert $failed 'An installed driver with missing endpoints must request repair.'
Assert ($script:Events.Count -eq 0) 'Do not relaunch a registered driver installer in uninstall mode.'

Reset-Scenario
$script:Installed.Voicemeeter = $false
$script:FailInstaller = 'Voicemeeter'
$failed = $false
try { $null = Invoke-DependencySetup } catch { $failed = $true }
Assert $failed 'Cancellation must not be reported as installation success.'
Assert (-not $script:Installed.Voicemeeter) 'Cancelled driver must stay missing.'

Reset-Scenario
$script:Installed.EqualizerApo = $false
$script:UndetectedInstaller = 'EqualizerApo'
$failed = $false
try { $null = Invoke-DependencySetup } catch { $failed = $true }
Assert $failed 'A zero exit code without installed APO files must not count as success.'

Reset-Scenario
$script:Installed.Melda = $false
$script:Installed.Speech = $false
Assert ((Invoke-DependencySetup) -eq 0) 'Unselected optional dependencies may remain missing.'
Assert ($script:Events.Count -eq 0) 'Unselected optional dependencies must not be installed.'
$script:IncludeMelda = $true
$script:IncludeSpeech = $true
Assert ((Invoke-DependencySetup) -eq 0) 'Selected optional dependencies should install.'
Assert (($script:Events -join ',') -eq 'melda,speech') 'Install both selected optional dependencies.'

Write-Host 'PASS: PowerShell syntax, package manifest, hash rejection, existing installs, reboot/resume, missing endpoints, cancellation, post-install verification, and optional selection.'
