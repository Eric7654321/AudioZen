#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch] $IncludeMelda,
    [switch] $IncludeSpeech,
    [string] $AppDirectory,
    [string] $ReportPath
)

# ShellExec may start 32-bit PowerShell. Keep elevation and run the checks in 64-bit PowerShell.
if ($MyInvocation.InvocationName -ne '.' -and [Environment]::Is64BitOperatingSystem -and
    -not [Environment]::Is64BitProcess) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath,
        '-AppDirectory', $AppDirectory, '-ReportPath', $ReportPath)
    if ($IncludeMelda) { $arguments += '-IncludeMelda' }
    if ($IncludeSpeech) { $arguments += '-IncludeSpeech' }
    & "$env:WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" @arguments
    exit $LASTEXITCODE
}

function Get-RenderDeviceNames {
    $root = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render'
    foreach ($device in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
        $properties = Get-ItemProperty -LiteralPath "$($device.PSPath)\Properties"
        # Some endpoints expose only DeviceDesc. Keep both identities so a renamed
        # friendly name does not hide an installed driver. Disconnected endpoints count.
        foreach ($key in @('{a45c254e-df1c-4efd-8020-67d146a850e0},14',
                           '{a45c254e-df1c-4efd-8020-67d146a850e0},2')) {
            if (-not [string]::IsNullOrWhiteSpace($properties.$key)) { $properties.$key }
        }
    }
}

function Test-DependencyInstalled([string] $Name) {
    switch ($Name) {
        'Voicemeeter' {
            $devices = @(Get-RenderDeviceNames)
            return (@($devices -like '*Voicemeeter Input*').Count -gt 0 -and
                    @($devices -like '*Voicemeeter AUX Input*').Count -gt 0)
        }
        'VbCable' { return @((Get-RenderDeviceNames) -like '*CABLE Input*').Count -gt 0 }
        'EqualizerApo' {
            return ((Test-Path -LiteralPath (Join-Path $script:ApoDirectory 'EqualizerAPO.dll')) -and
                ((Test-Path -LiteralPath (Join-Path $script:ApoDirectory 'DeviceSelector.exe')) -or
                 (Test-Path -LiteralPath (Join-Path $script:ApoDirectory 'Configurator.exe'))))
        }
        'Melda' {
            return ((Test-Path -LiteralPath (Join-Path $script:VstDirectory 'Dynamics\MCompressor.dll')) -and
                    (Test-Path -LiteralPath (Join-Path $script:VstDirectory 'Reverb\MCharmVerb.dll')))
        }
        'Speech' {
            Add-Type -AssemblyName System.Speech
            return @([System.Speech.Recognition.SpeechRecognitionEngine]::InstalledRecognizers() |
                Where-Object { $_.Culture.Name -eq 'zh-TW' }).Count -gt 0
        }
        default { throw "Unknown dependency: $Name" }
    }
}

function Test-DriverRegistered([string] $Name) {
    if ($Name -eq 'VbCable') {
        return (Test-Path 'HKLM:\SYSTEM\CurrentControlSet\Services\VBAudioVACMME')
    }
    $folder = Join-Path ${env:ProgramFiles(x86)} 'VB\Voicemeeter'
    return ((Test-Path -LiteralPath (Join-Path $folder 'voicemeeterpro.exe')) -or
            (Test-Path -LiteralPath (Join-Path $folder 'voicemeeter8.exe')))
}

function Get-ApoDirectory {
    $path = (Get-ItemProperty 'HKLM:\SOFTWARE\EqualizerAPO' -ErrorAction SilentlyContinue).InstallPath
    if ($path -and (Test-Path -LiteralPath $path)) { return $path }
    return (Join-Path $env:ProgramFiles 'EqualizerAPO')
}

function Assert-PackageHash([string] $Path, [string] $ExpectedHash) {
    if ($ExpectedHash -notmatch '^[a-fA-F0-9]{64}$' -or
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $ExpectedHash) {
        throw "下載檔案的 SHA-256 不符，已停止執行：$Path。請重新下載 AudioZen 安裝程式，勿略過驗證。"
    }
}

function Get-DependencyInstaller([string] $Name) {
    $package = $script:Packages.$Name
    $path = Join-Path $script:DownloadDirectory $package.file
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host "正在下載 $Name，請稍候……"
        $client = New-Object System.Net.WebClient
        try { $client.DownloadFile($package.url, $path) }
        finally { $client.Dispose() }
    }
    Assert-PackageHash $path $package.sha256
    if ($package.entry) {
        $directory = Join-Path $script:DownloadDirectory $Name
        Expand-Archive -LiteralPath $path -DestinationPath $directory -Force
        return (Join-Path $directory $package.entry)
    }
    return $path
}

function Invoke-Installer([string] $Path) {
    Write-Host "請在官方安裝畫面確認授權與選項；若詢問重開機，請先選擇稍後重新啟動。"
    $process = Start-Process -FilePath $Path -WorkingDirectory (Split-Path $Path) -PassThru -Wait
    if ($process.ExitCode -notin @(0, 3010, 1641)) {
        throw "安裝已取消或失敗（exit code $($process.ExitCode)）：$Path"
    }
    if ($process.ExitCode -in @(3010, 1641)) { Set-RebootRequired }
}

function Get-BootIdentifier {
    return (Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().Ticks.ToString()
}

function Set-RebootRequired {
    $script:RestartRequired = $true
    $null = New-Item 'HKLM:\SOFTWARE\AudioZen\Setup' -Force
    Set-ItemProperty 'HKLM:\SOFTWARE\AudioZen\Setup' -Name RebootBoot -Value (Get-BootIdentifier)
}

function Test-SetupRebootPending {
    $state = Get-ItemProperty 'HKLM:\SOFTWARE\AudioZen\Setup' -ErrorAction SilentlyContinue
    return ($null -ne $state -and $state.RebootBoot -eq (Get-BootIdentifier))
}

function Install-Speech {
    # Windows Speech FOD depends on Basic and TextToSpeech; do not change the UI language.
    foreach ($feature in @('Basic', 'TextToSpeech', 'Speech')) {
        $name = "Language.$feature~~~zh-TW~0.0.1.0"
        $capability = Get-WindowsCapability -Online -Name $name
        if ($capability.State -ne 'Installed') {
            Write-Host "正在從 Windows Update 安裝 $name……"
            $result = Add-WindowsCapability -Online -Name $name
            if ($result.RestartNeeded) { Set-RebootRequired }
        }
    }
}

function Install-Melda {
    $null = [System.Windows.Forms.MessageBox]::Show(
        "請在 MPluginManager 選取 MFreeFXBundle 中的 MCompressor 與 MCharmVerb，安裝 64-bit VST2（不是 VST3）。`r`n`r`n" +
        "VST2 目錄請選擇：$script:VstDirectory`r`n保留 Dynamics 與 Reverb 子目錄。完成後關閉 MPluginManager。",
        'AudioZen：Melda 外掛', 'OK', 'Information')
    Invoke-Installer (Get-DependencyInstaller 'Melda')
    if (-not (Test-DependencyInstalled 'Melda')) {
        $manager = Get-ChildItem (Join-Path $env:ProgramFiles 'MeldaProduction') -Filter MPluginManager.exe -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($manager) { Invoke-Installer $manager.FullName }
    }
    while (-not (Test-DependencyInstalled 'Melda')) {
        $answer = [System.Windows.Forms.MessageBox]::Show(
            "尚未找到 Dynamics\MCompressor.dll 與 Reverb\MCharmVerb.dll。`r`n請在 MPluginManager 完成 64-bit VST2 安裝，目錄：$script:VstDirectory`r`n`r`n" +
            '完成後按「重試」；或按「取消」，回 AudioZen 安裝程式取消勾選 Melda。',
            'AudioZen：外掛尚未完成', 'RetryCancel', 'Warning')
        if ($answer -eq 'Cancel') { throw 'Melda 外掛尚未安裝完成。可返回上一頁取消勾選此選用項目。' }
    }
}

function Invoke-DependencySetup {
    $script:RestartRequired = $false
    if (Test-SetupRebootPending) { return 3010 }

    # New virtual devices must survive a reboot before APO offers them for selection.
    foreach ($name in @('Voicemeeter', 'VbCable')) {
        if (-not (Test-DependencyInstalled $name)) {
            if (Test-DriverRegistered $name) {
                throw "$name 已有安裝紀錄，但找不到預期的音訊裝置。請先重新開機，並確認裝置沒有被停用或改名；必要時使用官方安裝程式修復，再重試。"
            }
            $installer = Get-DependencyInstaller $name
            # Persist before starting: a vendor installer may reboot Windows itself.
            Set-RebootRequired
            Invoke-Installer $installer
        }
    }
    if ($script:RestartRequired) { return 3010 }

    if (-not (Test-DependencyInstalled 'EqualizerApo')) {
        Invoke-Installer (Get-DependencyInstaller 'EqualizerApo')
        $script:ApoDirectory = Get-ApoDirectory
        if (-not (Test-DependencyInstalled 'EqualizerApo')) {
            throw 'Equalizer APO 尚未安裝完成，請重試。'
        }
        # Device attachment may need a reboot even when the installer returns zero.
        Set-RebootRequired
    }
    if ($IncludeMelda -and -not (Test-DependencyInstalled 'Melda')) { Install-Melda }
    if ($IncludeSpeech -and -not (Test-DependencyInstalled 'Speech')) { Install-Speech }
    if ($script:RestartRequired) { return 3010 }
    if ($IncludeSpeech -and -not (Test-DependencyInstalled 'Speech')) {
        throw 'Windows 尚未提供 zh-TW 語音辨識引擎。請在語言設定完成安裝，或返回上一頁取消勾選語音辨識。'
    }
    return 0
}

# Dot-sourcing defines functions for the non-installing tests; it performs no system changes.
if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    $exitCode = 1
    $script:DownloadDirectory = $null
    try {
        if (-not $ReportPath -or -not $AppDirectory) { throw 'ReportPath and AppDirectory are required.' }
        Start-Transcript -Path "$ReportPath.log" -Force | Out-Null
        if (-not [Environment]::Is64BitProcess) { throw 'Please run 64-bit Windows PowerShell.' }
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        if (-not (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)) { throw '安裝系統元件需要管理員授權。' }
        Add-Type -AssemblyName System.Windows.Forms
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $script:Packages = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
        # Each elevated run owns a fresh download directory; no shared executable cache.
        $script:DownloadDirectory = Join-Path $env:TEMP ('AudioZen-' + [Guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $script:DownloadDirectory
        $script:ApoDirectory = Get-ApoDirectory
        $script:VstDirectory = Join-Path $env:ProgramFiles 'VstPlugins\MeldaProduction'
        $existing = $null
        $settingsPath = Join-Path $AppDirectory 'appsettings.json'
        if (Test-Path -LiteralPath $settingsPath) {
            $existing = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
            if ($existing.apo.vstDirectory) { $script:VstDirectory = $existing.apo.vstDirectory }
        }
        $exitCode = Invoke-DependencySetup
        if ($exitCode -eq 0) {
            $configDirectory = (Get-ItemProperty 'HKLM:\SOFTWARE\EqualizerAPO' -ErrorAction SilentlyContinue).ConfigPath
            if (-not $configDirectory) { $configDirectory = Join-Path $script:ApoDirectory 'config' }
            if ($existing.apo.configDirectory) { $configDirectory = $existing.apo.configDirectory }
            if (-not (Test-Path -LiteralPath $configDirectory -PathType Container)) {
                throw "找不到 APO 設定目錄：$configDirectory。請修正 appsettings.json 的 apo.configDirectory 或修復 APO 安裝，再重試。"
            }
            $settings = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'appsettings.example.json') -Raw | ConvertFrom-Json
            $settings.apo.configDirectory = $configDirectory
            $settings.apo.vstDirectory = $script:VstDirectory
            $settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath "$ReportPath.settings.json" -Encoding UTF8
        }
    }
    catch {
        $message = $_.Exception.Message
        Write-Host $message -ForegroundColor Red
        if ($ReportPath) { $message | Set-Content -LiteralPath $ReportPath -Encoding UTF8 }
        $exitCode = 1
    }
    finally {
        if ($script:DownloadDirectory) {
            Remove-Item -LiteralPath $script:DownloadDirectory -Recurse -Force -ErrorAction Continue
        }
        Stop-Transcript -ErrorAction SilentlyContinue | Out-Null
    }
    exit $exitCode
}
