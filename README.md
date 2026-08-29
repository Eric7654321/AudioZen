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

Melda 的 DLL 路徑寫死在 `AudioUI/GeminiServices.cs`，預設 `C:\Program Files\VstPlugins\MeldaProduction\`。
APO 設定檔路徑同樣寫死為 `C:\Program Files\EqualizerAPO\config\config.txt`。裝在別的位置要改 code。

## 建置與執行

```powershell
dotnet build AudioUI.sln -c Release
dotnet run --project AudioUI/AudioUI.csproj
```

首次執行前要先設定下面兩項，否則會連不上 Gemini、或是 app 對不到裝置。

執行期的情境設定、錄音、按鍵綁定寫在建置輸出目錄下的 `config/`，不進版控，刪掉會重建。

## 設定

**Gemini API key** — 目前寫死在 code，兩個地方要一起改：
`AudioUI/GeminiServices.cs` 與 `AudioUI/MainWindow.xaml.cs` 的 `API_KEY` 常數。

**app → 虛擬裝置對應** — 決定哪個程式的聲音走哪張虛擬音效卡，換機器一定要調整。
寫死在三個地方，改一個就要三個一起改：

| 檔案 | 內容 |
|---|---|
| `AudioUI/AudioSessionService.cs` | 程序名 → 裝置名 |
| `AudioUI/MainWindow.xaml.cs` | 裝置全名（含 GUID）→ 程序名 |
| `AudioUI/GeminiServices.cs` | 同上，另有一份寫在送給 Gemini 的 prompt 裡 |

裝置全名與 GUID 每台機器不同，可用 Equalizer APO 的 Configurator 取得。

搭配的 Voicemeeter 路由要自己在 Voicemeeter 裡接：把各 app 的輸出指到對應的虛擬輸入，
再由 Voicemeeter 匯出到實體喇叭。

## 操作

- **喚醒詞**：「心平氣和」，之後講一句指令，錄 5 秒
- **介面**：直接輸入文字指令，可先預覽再套用
- **全域熱鍵**：可把情境綁到按鍵，另有 rollback（回上一個設定）與靜音兩個內建動作

## 專案結構

```
AudioUI.sln
AudioUI/
```

| 檔案 | 負責 |
|---|---|
| `MainWindow.xaml{,.cs}` | 介面，以及目前的流程調度與狀態 |
| `GeminiServices.cs` | Gemini 呼叫、prompt、回應解析、寫 APO 設定檔、rollback |
| `AppAudioRecorder.cs` | WASAPI per-process 錄音 |
| `AudioSessionService.cs` | 列舉音訊工作階段與裝置 |
| `ConfigService.cs` | 讀回 APO 設定檔，還原成介面上的摘要 |
| `JsonMappingManager.cs` | 情境與對話紀錄的 JSON 存取 |
| `MeldaEncoder.cs` | 把參數編碼成 Melda VST 的 base64 chunk |
| `WakeWordTrigger.cs` | 喚醒詞辨識 |
| `KeyMappingService.cs` / `HotkeyService.cs` | 按鍵綁定與全域熱鍵 |
| `AudioProcessor.cs` | 音檔前處理 |
| `TtsService.cs` | 語音回覆 |
| `GeminiData.cs` | API 與設定的資料模型 |

## 授權

Equalizer APO 本體與 VST 外掛均為外部相依，各自遵循其授權，不隨本 repo 散布。
