# AudioZen

用自然語言控制 Windows 的每個 app 的音訊。說「遊戲太吵，把 Discord 講話拉清楚」，
Gemini 把它翻成 EQ / preamp / 壓縮器參數，寫成 Equalizer APO 設定檔套用下去。

Windows 沒有 per-application 的 DSP API，Equalizer APO 只能對「音訊裝置」動手。
本專案的作法是：**把每個 app 用虛擬音效卡路由到不同裝置，再對裝置套設定**。
理解這一句，才看得懂下面的裝置對應表為什麼存在。

## 執行需求

除了 .NET，下面四項都是**外部安裝**，不在本 repo 內，缺任何一項功能會少一塊。

| 需求 | 用途 | 沒有它會怎樣 |
|---|---|---|
| Windows 10 build 17763 以上 | `net8.0-windows10.0.17763.0` | 無法建置 |
| .NET 8 SDK | 建置 | 無法建置 |
| [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) | 實際套用音訊處理，安裝時要對目標裝置勾選啟用 | 設定寫得出來但沒有效果 |
| [VB-Audio Voicemeeter](https://vb-audio.com/Voicemeeter/) + [VB-CABLE](https://vb-audio.com/Cable/) | 提供 `Voicemeeter Input` / `Voicemeeter AUX Input` / `CABLE Input` 三個虛擬裝置，讓不同 app 分流 | 只能對全域套設定，per-app 失效 |
| [MeldaProduction MFreeFXBundle](https://www.meldaproduction.com/MFreeFXBundle) | 壓縮器 `MCompressor` 與殘響 `MCharmVerb`，APO 以 VST 載入 | EQ / preamp 可用，壓縮與殘響無效 |
| Windows 中文（台灣）語音辨識套件 | 喚醒詞辨識，`zh-TW` | 喚醒詞失效，仍可用介面手動操作 |
| Gemini API key | 自然語言 → 音訊參數 | 核心功能失效 |

APO 的設定目錄在 `appsettings.json` 的 `apo.configDirectory`，裝在別的位置改這裡。
本程式寫的是 `apo.fragmentFileName`（預設 `audiozen.txt`），再由 APO 的 `config.txt` 用 `Include:` 引入，
所以你原本在 APO 裡調的東西不會被蓋掉。那行 `Include:` 只在缺少時補一次。

Melda VST 的安裝目錄在 `apo.vstDirectory`，壓縮器與殘響的 DLL 從這裡往下找
（`Dynamics\MCompressor.dll`、`Reverb\MCharmVerb.dll`）。

## 建置與執行

```powershell
dotnet build AudioUI.sln -c Release
dotnet run --project AudioUI/AudioUI.csproj
```

跑起來之後先做兩件事，否則會連不上 Gemini、或是 app 對不到裝置：
**齒輪 →「設定檔」填 API key**，以及**齒輪 →「一般」看「執行環境」有沒有缺裝置**。

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

**app → 虛擬裝置對應** — 決定哪個程式的聲音走哪張虛擬音效卡，換機器一定要調整。
改 `appsettings.json` 的 `routes` 一處即可，程式的其他地方都從這裡讀。

| 欄位 | 意義 |
|---|---|
| `id` | 給模型用的邏輯代號。模型只吐這個，不必複述裝置全名 |
| `displayName` | 介面上顯示的名字 |
| `devicePattern` | 寫進 APO `Device:` 後面的比對樣式 |
| `matchKeyword` | 讀回設定檔時用來認出這條路由；省略時自動取 `devicePattern` 前兩個字詞 |
| `processes` | 走這條路由的程式檔名 |

`devicePattern` 每台機器不同，可用 Equalizer APO 的 Configurator 取得。APO 的比對規則是
「以空白分隔的字詞全部都要出現在 `裝置名稱 連接名稱 GUID` 裡」，所以 `Voicemeeter Input`
這種短樣式就會中，不一定要寫完整含 GUID 的字串。

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
| `IAudioBackend` / `ILlmClient` / `IConfigStore` / `ISpeechInput` / `INotifier` / `ITextToSpeech` | 五個接縫。換實作不必動呼叫端 |
| `IApiKeyStore` / `IPreferencesStore` / `IAppAudioRouter` | key 儲存、使用者偏好、把 app 指到裝置 |
| `RouteTable` / `Models/AudioRoute` | app ↔ 虛擬裝置的對應，唯一的一份 |
| `EqBands` | 七段手調頻段 ↔ `graphic_eq_string` 雙向轉換 |
| `TonePreset` | 一般模式的音色預設，以及音量百分比 ↔ preamp dB 換算 |
| `DspPresets` | 壓縮器與殘響的具名 preset |
| `TuningViewModel` | 手動調參面板的狀態。只需 `INotifyPropertyChanged`，所以測得到 |
| `DependencyReport` | 執行環境體檢：路由指到的裝置存不存在 |
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
| `MeldaEncoder` | 把參數編碼成 Melda VST 的 base64 chunk |
| `NAudioSpeechInput` / `TtsService` / `ToastNotifier` | 錄音、語音回覆、通知 |

**`AudioUI`** — WPF 與仍然綁在 UI 上的東西。

| 檔案 | 負責 |
|---|---|
| `MainWindow.xaml{,.cs}` | 介面與視窗層互動。視窗以自己當 `DataContext` |
| `MainWindowViewModel` | 狀態與流程 |
| `AppConfig` | 組裝根。所有實作在這裡被接起來 |
| `SituationManager` | 錄音 → 轉文字 → 解析 → 寫檔 → 套用 → 詢問回退的主線 |
| `AudioSessionService` | 列舉音訊工作階段與裝置 |
| `AppAudioRecorder` | WASAPI per-process 錄音 |
| `AudioProcessor` | 用 NAudio 的 biquad 濾波產生試聽用的預覽音檔 |
| `ConfigService` | 讀回 APO 設定檔，還原成介面上的摘要 |
| `WakeWordTrigger` / `KeyMappingService` / `HotkeyService` | 喚醒詞、按鍵綁定、全域熱鍵 |

## 測試與 CI

```powershell
dotnet test AudioUI.sln
```

CI 在 `windows-latest` 上建置並跑測試，結果寫回 `ci-status` 分支的 `status.md`
（`git show origin/ci-status:status.md`）。

警告分兩層看：`CS8618` / `CS8625` 是**宣告層**通病（欄位沒初始化、null 當預設值），
其餘一律列出全文——**新冒出來的警告代碼才是訊號**。判準寫在 `.github/workflows/build.yml`。

⚠️ **XAML 繫結錯誤不會讓編譯失敗，執行時也不會丟例外，只會靜靜顯示空白。**
測試涵蓋不到這一層，改繫結後要實際開視窗確認。

## 待處理

按建議順序，前面的不做後面的做了也難驗。

**先做**

- [ ] `SituationManager` 搬進 `AudioUI.Core`。它一個 WPF 型別都沒用到，只剩殘留的 `using`；
      搬過去之後才測得到。**目前它是唯一沒有測試保護的主線。**
      建構子的六個介面都已可注入，`AudioUI.Tests/Fakes.cs` 也已備妥

**方案 A 剩下的**（讓使用者只需要開這一個 app）

- [ ] **Voicemeeter Remote API**：目前只做到「app → 虛擬裝置」，
      **虛擬裝置 → 實體喇叭仍要人手動在 Voicemeeter 裡接**。這塊不做，方案 A 不算完成
- [ ] 安裝精靈（帶使用者裝完三個外部相依）。散布 VB-Audio 的元件前要先確認其授權條款
- [ ] 驗證 `AudioPolicyConfigRouter` 真的生效。顯示「已接好」也可能是假的——
      要去 Windows「系統 → 音效 → 音量合成器」確認該 app 的輸出裝置真的變了

**已知缺口**

- [ ] 手動調參面板讀不回壓縮／殘響是哪個 preset（設定檔裡只剩 base64）。
      那個目標本來有壓縮器的話，從面板按套用會**靜靜拿掉它**
- [ ] 「控制」分頁的「調整錄音檔」按鈕沒有接任何動作
- [ ] `DspPresets` 的數值是照參數範圍推的起點，**沒有實際試聽調過**
- [ ] `MainWindowViewModel` 仍不能單元測試，卡在 `AudioAppModel` 帶著 `ImageSource` 與 `Brush`。
      拆成 Core 的 DTO ＋ UI 的包裝要動 XAML

**刻意不做**

- 情境 id `"114514"` 維持字串常數。它同時是 `config/file_mapping.json` 的鍵，
  改成 enum 會讓既有存檔讀不回來
- 剩下的宣告層 nullable 警告。逐一修需要判斷每個欄位「真的可以是 null 嗎」，
  改錯會把 null 悄悄變成空字串

## 由來與授權

本專案原本是交大「多媒體與人機互動總整與實作」的課程專題，六人團隊，
2025-11 ~ 12 開發。程式碼從當時的 repo 用 `git subtree split` 抽出，
**保留完整 commit 歷史**；Equalizer APO 的 C++ 原始碼不在本 repo 內
（它是安裝並註冊到裝置上的系統元件，與本程式沒有建置期相依）。

commit 歷史裡的作者：`Eric7654321`、`james-0520`、`Katrina Hung`、`111550156sakuya`。

本 repo 的程式碼以 [MIT](LICENSE) 授權。所有 NuGet 相依同為 MIT。

Equalizer APO 本體、MeldaProduction VST、VB-Audio 的 Voicemeeter 與 VB-CABLE
均為外部相依，各自遵循其授權，不隨本 repo 散布。
