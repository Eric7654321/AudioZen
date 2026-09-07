# 心頻氣和 (AudioZen)

用自然語言控制 Windows 的每個 app 的音訊。說「遊戲太吵，把 Discord 講話拉清楚」，
Gemini 把它翻成 EQ / preamp / 壓縮器參數，寫成 Equalizer APO 設定檔套用下去。

Windows 沒有 per-application 的 DSP API，Equalizer APO 只能對「音訊裝置」動手。
本專案的作法是：**把每個 app 用虛擬音效卡路由到不同裝置，再對裝置套設定**。
理解這一句，才看得懂下面的裝置對應表為什麼存在。

## 執行需求

一般使用者先執行 **`AudioZen.Setup.exe`**，安裝 AudioZen 本體與相依元件；這份發行檔內含 .NET 8 Runtime，
不需要先安裝 .NET。安裝程式支援 Windows x64，安裝到目前帳號的 `%LOCALAPPDATA%\Programs\AudioZen`。

從原始碼執行需要 **.NET 8 SDK**；若自行產生 framework-dependent 發行檔，電腦至少要有
[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)。這是程式能啟動的前置條件，
因此無法等到程式內的環境檢查才檢查。可先在 PowerShell 執行 `dotnet --list-runtimes`，確認清單中有
`Microsoft.WindowsDesktop.App 8.x`。

獨立安裝程式會檢查既有元件，從官方來源下載缺少的安裝檔、驗證固定的 SHA-256，再開啟第三方安裝畫面。
使用者自行確認授權與裝置選項；安裝系統元件時會顯示 Windows 管理員授權提示。
相依安裝腳本若由 32-bit PowerShell 啟動，會沿用授權轉交給 64-bit Windows PowerShell 執行。
第三方安裝檔不包在 AudioZen 發行檔中，各元件仍遵循原廠授權。

| 需求 | 用途 | 沒有它會怎樣 |
|---|---|---|
| Windows 10 build 17763 以上 | `net8.0-windows10.0.17763.0` | 無法建置 |
| .NET 8 SDK / Desktop Runtime | 從原始碼建置 / 執行發行檔 | 無法建置 / 啟動 |
| [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) | 實際套用音訊處理，安裝時要對目標裝置勾選啟用 | 設定寫得出來但沒有效果 |
| [Voicemeeter Banana / Potato](https://vb-audio.com/Voicemeeter/) + [VB-CABLE](https://vb-audio.com/Cable/) | 提供 `Voicemeeter Input` / `Voicemeeter AUX Input` / `CABLE Input` 三個虛擬裝置，讓不同 app 分流 | 只能對全域套設定，per-app 失效 |
| [MeldaProduction MFreeFXBundle](https://www.meldaproduction.com/MFreeFXBundle) | 壓縮器 `MCompressor` 與殘響 `MCharmVerb`，APO 以 VST 載入 | EQ / preamp 可用，壓縮與殘響無效 |
| Windows 中文（台灣）語音辨識套件 | 喚醒詞辨識，`zh-TW` | 喚醒詞失效，仍可用介面手動操作 |
| Gemini API key | 自然語言 → 音訊參數 | 核心功能失效 |

APO 的設定目錄在 `appsettings.json` 的 `apo.configDirectory`，裝在別的位置改這裡。
本程式寫的是 `apo.fragmentFileName`（預設 `audiozen.txt`），再由 APO 的 `config.txt` 用 `Include:` 引入，
所以你原本在 APO 裡調的東西不會被蓋掉。那行 `Include:` 只在缺少時補一次。

Melda VST 的安裝目錄在 `apo.vstDirectory`，壓縮器與殘響的 DLL 從這裡往下找
（`Dynamics\MCompressor.dll`、`Reverb\MCharmVerb.dll`）。

## 建置與執行

### 一般使用者：整合安裝

1. 執行 `AudioZen.Setup.exe`。Equalizer APO、Voicemeeter Banana 與 VB-CABLE 為必要項目。
   已有符合需求的元件會略過；Melda 外掛與 `zh-TW` 語音辨識預設勾選，可以取消。
2. 確認管理員授權，在各官方安裝畫面完成操作。第三方若詢問重開機，請先選「稍後」。
3. 虛擬音效卡安裝後，AudioZen Setup 會提示重新啟動 Windows；儲存工作後重開機，登入原帳號會繼續。
   接著安裝 Equalizer APO，裝置掛載完成後也可能再要求重開機。尚未重開機時重跑 Setup 不會跳過這個步驟。
4. 選用 Melda 時，依畫面提示透過 **MPluginManager** 安裝 `MCompressor` 與 `MCharmVerb` 的 **64-bit VST2**，
   不是只有安裝 MPluginManager 或 VST3。預設目錄是 `C:\Program Files\VstPlugins\MeldaProduction`，
   保留 `Dynamics` 與 `Reverb` 子目錄；安裝程式會檢查實際 DLL 是否存在。
5. 必要元件及已選取的選用元件完成後才安裝 AudioZen、建立開始功能表捷徑。
   開啟後仍需設定 Gemini API key、APO 目標裝置、Voicemeeter 的 A1 實體輸出與音訊路由。

下載、授權取消或安裝驗證失敗時會停止，可重試或返回上一步取消選用項目；不會把失敗當成完成。
若已有驅動安裝紀錄卻缺少預期 endpoint，會要求先重開機、確認裝置名稱或修復，不會反覆執行可能變成解除安裝的驅動安裝程式。
Windows 語音功能使用 Windows Update；組織的更新政策可能阻止下載，這時可取消語音項目並使用文字控制。
安裝元件代表軟體／驅動已存在，**不代表音訊接線或 APO 效果已驗證生效**。

重開機續接副本與記錄位於 `%LOCALAPPDATA%\AudioZen\Setup`：
`AudioZen.Setup.exe` 可手動重跑，`dependencies.log` 記錄最近一次相依安裝。
解除安裝 AudioZen 不會移除共享的第三方驅動、外掛或 Windows 語言功能，也不刪除使用者的 `config/` 與 `appsettings.json`。

### 產生安裝檔

在 Windows 安裝 .NET 8 SDK（或可建置 .NET 8 的較新 SDK）與官方 [Inno Setup 6.2 以上](https://jrsoftware.org/isdl.php)，執行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
```

Inno Setup 只用於建置安裝封裝；使用者電腦不需要它。若使用其他 compiler 路徑：

```powershell
.\installer\Build-Installer.ps1 -Version 1.0.0 -IsccPath 'C:\Tools\Inno Setup 6\ISCC.exe'
```

輸出為 `artifacts\installer\AudioZen.Setup.exe`。建置使用全新的 self-contained `win-x64` publish 目錄，
排除開發機的 `appsettings.json`、`config/` 與錄音；新安裝只寫入不含 API key 的範例設定。
已存在的設定不會被升級安裝覆蓋。此流程未設定程式碼簽章，正式散布前應使用自己的簽章憑證簽署安裝檔。

GitHub Actions 的 `installer` workflow 會建置並上傳 `AudioZen-Setup-win-x64` artifact，
也可在 Actions 手動執行；它不會在 runner 上安裝音訊驅動。

第三方版本與 SHA-256 集中在 `installer/dependencies.json`。來源：
[Equalizer APO](https://sourceforge.net/projects/equalizerapo/files/)、
[Voicemeeter Banana](https://vb-audio.com/Voicemeeter/banana.htm)、
[VB-CABLE](https://vb-audio.com/Cable/)、[Melda](https://www.meldaproduction.com/downloads)。
升級時需從官方取得新檔、核對內容及 SHA-256，再修改 manifest；不能關閉雜湊驗證或改用未固定版本的 latest URL。

### 從原始碼執行

```powershell
dotnet build AudioUI.sln -c Release
dotnet run --project AudioUI/AudioUI.csproj
```

第一次開啟時會顯示「安裝後設定」，提供 APO 裝置選擇、Voicemeeter 實體輸出、Gemini API key 與自動接線。
之後可從**齒輪 → 一般 → 開啟安裝後設定**重新開啟，按「重新檢查」更新環境狀態。
程式內不再提供元件勾選與官方補裝導引；缺少系統元件時，請重新執行 `AudioZen.Setup.exe`。
安裝驅動後通常需要重新啟動 Windows，再進行安裝後設定。

執行期的資料（情境、錄音、按鍵綁定、偏好、加密後的 key）都寫在建置輸出目錄下的 `config/`，
不進版控，刪掉會重建——連同 key 一起。

## 設定

**Gemini API key** — 在程式裡填：右上角齒輪 →「設定檔」，貼上 key 後按儲存。
存下來時會用 Windows 帳號加密（DPAPI）寫進 `config/apikey.dat`，換帳號或換機器都解不開。
旁邊的「測試連線」會實際打一次 API——存得起來不代表能用。

key 的來源優先序是**環境變數 → 設定頁存的 → `appsettings.json`**：

- `AUDIOZEN_GEMINI_API_KEY` 最優先，適合 CI 或不想在磁碟留東西的場合
- `appsettings.json` 的 `gemini.apiKey` 是最後的後備，手動編輯仍然有效
  （`copy AudioUI\appsettings.example.json AudioUI\appsettings.json`）

`gemini.model` 預設 `gemini-3.6-flash`。舊的 `gemini-2.5-flash-lite` 已經不對新 key 開放，
換成新 key 之後沿用舊型號會拿到 404。

**app → 虛擬裝置對應** — 決定哪個程式的聲音走哪張虛擬音效卡。
改 `appsettings.json` 的 `routes` 一處即可，程式的其他地方都從這裡讀。

| 欄位 | 意義 |
|---|---|
| `id` | 給模型用的邏輯代號。模型只吐這個，不必複述裝置全名 |
| `displayName` | 介面上顯示的名字 |
| `devicePattern` | 寫進 APO `Device:` 後面的比對樣式 |
| `matchKeyword` | 讀回設定檔時用來認出這條路由；省略時自動取 `devicePattern` 前兩個字詞 |
| `processes` | 走這條路由的程式檔名 |

內建與範例設定使用 `Voicemeeter Input`、`Voicemeeter AUX Input`、`CABLE Input` 這些可攜的短名稱。
環境檢查只比對目前裝置，不會改寫路由或 `matchKeyword`。既有設定若含有別台電腦的 GUID，
請自行修改 `appsettings.json`，改用對應短名稱或本機的裝置識別。

若使用不在內建三項中的裝置，可用 Equalizer APO Configurator 取得 `devicePattern`。APO 的比對規則是
「以空白分隔的字詞全部都要出現在 `裝置名稱 連接名稱 GUID` 裡」。

省略整個 `routes` 區塊時採用內建預設值（見 `AudioUI.Core/RouteTable.cs`）。

這條路由要通，需要兩段接線：

1. **app → 虛擬裝置**：程式可以代勞。齒輪 →「一般」→「自動接線」，
   會把正在播放、而且路由表認得的程式各自指到該走的虛擬裝置。
2. **虛擬裝置 → 實體喇叭**：**目前仍要自己在 Voicemeeter 裡接。**

同一頁的「執行環境」會逐條檢查路由指到的裝置實際存不存在，缺的會指名是哪一條、
需要什麼樣式、通常由哪個產品提供。裝置被插拔後可以按「重新檢查」。

## 操作

- **喚醒詞**：預設「心平氣和」，之後講一句指令，錄 5 秒。可在齒輪 →「個人化」改，改完要重開程式
- **文字指令**：入口的輸入框打字按 Enter，或在聊天畫面輸入。可先預覽再套用
- **手動調參**：「控制」分頁點任一 app 卡片進面板
  - 一般模式：音量滑桿 + 音色預設（遊戲 / 音樂）
  - 專業模式：EQ 七段滑桿，壓縮器與殘響各選一個 preset
- **全域熱鍵**：`Alt` + 數字鍵盤。可把情境綁到按鍵，另有 rollback（回上一個設定）與靜音兩個內建動作。
  **NumLock 關掉時數字鍵盤送出的鍵碼不同，整組熱鍵不會觸發**
- **記憶**：齒輪 →「記憶」可寫自我介紹、檢視與刪除模型記下的偏好。兩個開關都關掉時，
  送給模型的 prompt 與沒有這個功能時完全相同

## 專案結構

四個專案，相依方向是單向的：`AudioUI` → `AudioUI.Infra` → `AudioUI.Core`。

```
AudioUI.sln
├── AudioUI.Core/     net8.0（刻意不是 -windows）
├── AudioUI.Infra/    net8.0-windows，平台實作
├── AudioUI/          WPF app
└── AudioUI.Tests/    xunit
```

**`AudioUI.Core`** — 介面與不依賴 UI 框架的邏輯。目標框架刻意是 `net8.0` 而非 `net8.0-windows`，
**靠編譯器擋住 WPF 型別滲進來**，而不是靠自律。

| 檔案 | 負責 |
|---|---|
| `SituationManager` | 錄音 → 轉文字 → 解析 → 寫檔 → 套用 → 詢問回退的主線 |
| `MainWindowViewModel` | 主視窗的狀態與流程。相依全部由建構式傳入，沒有預設值 |
| `IAudioBackend` / `ILlmClient` / `IConfigStore` / `ISpeechInput` / `INotifier` / `ITextToSpeech` | 主要接縫。換實作不必動呼叫端 |
| `IApiKeyStore` / `IApiKeyManager` / `IPreferencesStore` / `IAppAudioRouter` | key 儲存與現況、使用者偏好、把 app 指到裝置 |
| `IAudioSessions` / `Models/AudioAppInfo` | 誰在出聲。只有純資料，圖示與框線由 UI 層從中算出來 |
| `IAppStateNotifier` / `ISampleRecorder` / `IAudioPreview` | 列出正在出聲的程式、錄樣本、產生試聽音檔 |
| `RouteTable` / `Models/AudioRoute` | app ↔ 虛擬裝置的對應，唯一的一份 |
| `ApoConfigSummary` | 從 APO 設定檔讀出某個裝置目前被套了什麼 |
| `BindingErrorLog` | 收集畫面繫結失效的訊息 |
| `EqBands` | 七段手調頻段 ↔ `graphic_eq_string` 雙向轉換 |
| `TonePreset` | 一般模式的音色預設，以及音量百分比 ↔ preamp dB 換算 |
| `DspPresets` | 壓縮器與殘響的具名 preset |
| `TuningViewModel` | 手動調參面板的狀態。只需 `INotifyPropertyChanged`，所以測得到 |
| `DependencyReport` | 環境檢查的可測判斷：必要／選用項目與路由是否就緒 |
| `MmDeviceIds` | 裝置 id 在列舉形式與指定形式之間的轉換 |
| `Models/` | `AudioIntent`、`Situation`、`AppSettings`、`UserPreferences` 等資料模型 |

**`AudioUI.Infra`** — 需要 Windows 或外部服務的實作。

| 檔案 | 負責 |
|---|---|
| `EqualizerApoBackend` | 寫 APO 設定檔、套用、讀回現況 |
| `GeminiClient` | Gemini 呼叫、prompt 組裝、回應解析 |
| `JsonConfigStore` / `JsonPreferencesStore` | 情境與偏好的 JSON 存取 |
| `DpapiApiKeyStore` | API key 的加密儲存 |
| `AudioPolicyConfigRouter` | 指定單一程式的輸出裝置（Windows 內部介面） |
| `WindowsDependencyProbe` | 收集 APO、VST、語音、API key 與音訊 endpoint 現況 |
| `MeldaEncoder` | 把參數編碼成 Melda VST 的 base64 chunk |
| `NAudioSpeechInput` / `TtsService` / `ToastNotifier` | 錄音、語音回覆、通知 |

**`AudioUI`** — WPF 與仍然綁在 UI 上的東西。

| 檔案 | 負責 |
|---|---|
| `MainWindow.xaml{,.cs}` | 介面與視窗層互動。視窗以自己當 `DataContext`，集合轉發給 ViewModel |
| `AppConfig` | 組裝根。所有實作在這裡被接起來，是唯一知道「用哪個實作」的地方 |
| `AudioSessionService` | 列舉音訊工作階段與裝置 |
| `AppAudioRecorder` | WASAPI per-process 錄音 |
| `AudioProcessor` | 用 NAudio 的 biquad 濾波產生試聽用的預覽音檔 |
| `AppIcons` / `AppCardConverters` | 從執行檔路徑抽圖示、決定卡片框線。WPF 型別只出現在這裡 |
| `BindingErrorListener` | 把 WPF 的繫結診斷接到 `BindingErrorLog` |
| `ConfigService` | APO 設定檔的路徑 |
| `WakeWordTrigger` / `KeyMappingService` / `HotkeyService` | 喚醒詞、按鍵綁定、全域熱鍵 |

## 測試與 CI

```powershell
dotnet test AudioUI.sln
```

CI 在 `windows-latest` 上建置並跑測試，結果寫回 `ci-status` 分支的 `status.md`
（`git show origin/ci-status:status.md`）。

安裝流程另有不安裝任何元件的 PowerShell 檢查：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\Test-Setup.ps1
```

檢查涵蓋下載雜湊拒絕、既有元件略過、驅動 → 重開機 → APO 順序、重開機前重跑、取消／失敗與選用項目。
`Build-Installer.ps1` 會先執行此檢查再封裝。這些測試替換了系統操作，不能取代 Windows 實機驗證。
發佈前仍需在可重開機的乾淨 Windows x64 VM 驗證：

- 無 .NET／音訊元件時完整安裝、兩階段重開機、原帳號續接與首次開啟。
- 管理員授權取消、第三方取消、斷線、錯誤 SHA-256，均不應啟動 AudioZen。
- 取消選用項目、已有 Banana／Potato、既有設定升級後保留與解除安裝保留個人資料。
- 在首次設定頁確認 APO 目標裝置、API key、A1 輸出與每條路由，實際播放音訊驗證效果。

警告分兩層看：`CS8618` / `CS8625` 是**宣告層**通病（欄位沒初始化、null 當預設值），
其餘一律列出全文——**新冒出來的警告代碼才是訊號**。判準寫在 `.github/workflows/build.yml`。

**XAML 繫結錯誤不會讓編譯失敗，執行時也不會丟例外，只會靜靜顯示空白**，
測試涵蓋不到這一層。程式因此在啟動時裝一個 listener 收 WPF 的繫結診斷，
把結果寫到執行輸出目錄的 `config/binding-errors.log`——**沒有錯誤也會寫**，
因為「檔案不在」代表這個檢查沒跑，跟「跑過而且乾淨」是兩回事。

改過 XAML 之後開一次視窗、讀那個檔就知道有沒有壞。要注意 collapsed 的分頁
不會被 measure，樣板繫結不會跑到，所以要切過每個分頁才算驗過。

## 待處理

按建議順序，前面的不做後面的做了也難驗。

**方案 A 剩下的**（讓使用者只需要開這一個 app）

- [ ] **Voicemeeter Remote API**：目前只做到「app → 虛擬裝置」，
      **虛擬裝置 → 實體喇叭仍要人手動在 Voicemeeter 裡接**。這塊不做，方案 A 不算完成
- [ ] 驗證 `AudioPolicyConfigRouter` 真的生效。顯示「已接好」也可能是假的——
      要去 Windows「系統 → 音效 → 音量合成器」確認該 app 的輸出裝置真的變了

**已知缺口**

- [ ] 卡片上的「音色調整」永遠顯示「無」。設定檔的回讀解析（`ConfigService.LoadConfig`）
      沒有任何地方呼叫，所以 `AudioAppInfo.Config` 對真實的 app 一律是 null。
      通知列走的是另一條路（`ApoConfigSummary`），那條是對的
- [ ] 「控制」分頁的「調整錄音檔」按鈕沒有接任何動作
- [ ] `DspPresets` 的數值是照參數範圍推的起點，**沒有實際試聽調過**

**刻意不做**

- 情境 id `"114514"` 維持字串常數。它同時是 `config/file_mapping.json` 的鍵，
  改成 enum 會讓既有存檔讀不回來
- 剩下的宣告層 nullable 警告。逐一修需要判斷每個欄位「真的可以是 null 嗎」，
  改錯會把 null 悄悄變成空字串

## 授權

本 repo 的程式碼以 [MIT](LICENSE) 授權。所有 NuGet 相依同為 MIT。

Equalizer APO 本體、MeldaProduction VST、VB-Audio 的 Voicemeeter 與 VB-CABLE
均為外部相依，各自遵循其授權，不隨本 repo 散布。
