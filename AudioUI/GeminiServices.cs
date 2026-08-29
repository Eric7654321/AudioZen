using HandyControl.Controls;
using HandyControl.Tools.Extension;
using Microsoft.Toolkit.Uwp.Notifications;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Vanara.PInvoke.Authz;
using static Vanara.PInvoke.Kernel32;

namespace AudioUI
{
    public class GeminiServices
    {
        // --- 功能 1: 使用 NAudio 錄音 ---
                private readonly RouteTable _routes;
        private readonly IAudioBackend _backend;
        private readonly INotifier _notifier;
        private readonly ISpeechInput _speech;
        private readonly ILlmClient _llm;

        /// <summary>全部可注入，測試才有辦法在不碰使用者設定、不寫進 APO、不跳通知、不講話也不打 API 的情況下跑。</summary>
        public GeminiServices(RouteTable? routes = null, IAudioBackend? backend = null,
                              INotifier? notifier = null, ISpeechInput? speech = null,
                              ILlmClient? llm = null)
        {
            _routes = routes ?? AppConfig.Routes;
            _backend = backend ?? AppConfig.AudioBackend;
            _notifier = notifier ?? AppConfig.Notifier;
            _speech = speech ?? AppConfig.SpeechInput;
            _llm = llm ?? AppConfig.LlmClient;
        }





        string promptText =
        "You are an audio engineer. Listen to the user's voice command. " +
        "Based on the request, generate an Equalizer APO GraphicEQ configuration using these specific 15 frequency bands: " +
        "25, 40, 63, 100, 160, 250, 400, 630, 1000, 1600, 2500, 4000, 6300, 10000, 16000. " +
        "Rules for generation: " +
        "1. Determine the gain (dB) for each band based on the user's intent and return in floating number. " +
        "Besides, the gain should be larger than 7.0dB, or just remain no gain." +
        "2. Calculate 'preamp_db'. logic: Identify the maximum positive gain among all bands. " +
        "The preamp_db must be negative and its absolute value must be greater than or equal to that maximum gain (e.g., if max gain is +15.2dB, preamp must be -15.2dB or lower, like -15.8dB). " +
        "3. Construct 'graphic_eq_string' in the format: '25 [gain]; 40 [gain]; ...' " +
        "WARNING: Output ONLY a JSON object with this exact structure: " +
        "{ " +
        "  \"message_for_user\": \"string (Explain briefly what you changed in 15 words in Traditional Chinese)\", " +
        "  \"preamp_db\": float, " +
        "  \"graphic_eq_string\": \"string (The formatted frequency-gain pairs separated by semicolons)\" " +
        "  \"graphic_eq_string\": \"string (The formatted frequency-gain pairs separated by semicolons)\" " +
        "  \"graphic_eq_string\": \"string (The formatted frequency-gain pairs separated by semicolons)\" " +
        "}";



        

        // --- 功能 3: 解析回傳並寫入 Config ---
        /// <summary>把意圖寫成 APO 設定檔。無法產出時回 "-1"，呼叫端據此換成給使用者看的訊息。</summary>
        public string WriteConfig(AudioIntent? eqResponse, string outputPath)
        {
            if (eqResponse == null) return "-1";

            using (StreamWriter sw = new StreamWriter(outputPath, false)) // false 表示覆寫檔案
            {
                if (eqResponse.Configs != null)
                {
                    foreach (var config in eqResponse.Configs)
                    {
                        // 1. 處理 Device 行
                        string? devicePattern = _routes.ResolveDevicePattern(config.Target);
                        if (devicePattern != null)
                        {
                            sw.WriteLine($"Device: {devicePattern}");
                        }
                        else
                        {
                            // 認不得的目標寫成註解：APO 會忽略它，而檔案本身留下了為什麼這段沒生效。
                            sw.WriteLine($"# Unknown Target: {config.Target}");
                        }

                        // 2. 寫入 Preamp
                        sw.WriteLine($"Preamp: {config.PreampDb} dB");

                        // 3. 寫入 GraphicEQ
                        if (!string.IsNullOrEmpty(config.GraphicEqString))
                        {
                            sw.WriteLine($"GraphicEQ: {config.GraphicEqString}");
                        }

                        if (config.CompJson != null && config.CompJson.Count > 0)
                        {
                            string header = "MBXXMCompressorsettings";
                            string base64String = MeldaEncoder.EncodeMeldaChunk(header, config.CompJson);
                            sw.WriteLine($"VSTPlugin: Library \"C:\\Program Files\\VstPlugins\\MeldaProduction\\Dynamics\\MCompressor.dll\" ChunkData \"{base64String}\"");
                        }

                        if (config.ReverbJson != null && config.ReverbJson.Count > 0)
                        {
                            string header = "MBXXMCharmVerbsettings";
                            string base64String = MeldaEncoder.EncodeMeldaChunk(header, config.ReverbJson);
                            sw.WriteLine($"VSTPlugin: Library \"C:\\Program Files\\VstPlugins\\MeldaProduction\\Reverb\\MCharmVerb.dll\" ChunkData \"{base64String}\"");
                        }

                        // 4. 加入一個空行分隔不同裝置的設定 (可選)
                        sw.WriteLine();
                    }
                }
            }

            return eqResponse.MessageForUser;
        }

        // --- 功能 4: 還原設定檔 ---
        public async Task ConfigRollback(string IdString, ChatManager _ChatManager)
        {
            _ChatManager.PopFront(IdString);
            if (_ChatManager.GetFront(IdString) == null)
            {
                await _TtsService.SpeakAsync("已經沒有更早的設定可以還原"); // constant
                return;
            }
            string originconfigPath = _ChatManager.GetFront(IdString).FileName;
            _backend.Apply(originconfigPath);
        }
        TtsService _TtsService = new TtsService();
        AudioSessionService _AudioSessionService = new AudioSessionService();

        /// <summary>
        /// 完整的進行一次錄音、分析與寫入的過程 (goal 1)
        /// </summary>
        public async Task RecordAndProcessAsync(int situationId,string audioFilePath, string eqConfigPath, ChatManager _ChatManager, int recordMs = 5000)
        {
            Task<string> recordPathTask = PerProcessAudioRecorder.RecordAllActiveAppsAsync(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "record"),
                        TimeSpan.FromSeconds(6));

            var appsConfig = _AudioSessionService.GetAppsWithConfig();
            // 0. 第一次回應
            new AppListNotifier().ShowAppNotification(appsConfig);

            await _TtsService.SpeakAsync("請問您想如何調整音訊設定"); // constant
            // 1. 錄音recordMs 毫秒
            string audioBase64 = await _speech.RecordAsync(audioFilePath, recordMs);

            // 2. 提示正在解析
            _notifier.Notify("解析中", "心頻氣和正在分析內容");
            await _TtsService.SpeakAsync("正在解析中"); // constant

            // 3. 呼叫 Gemini API
            string transcribedText = await _llm.TranscribeAsync(audioBase64); // STT
            AudioIntent? intent = await _llm.InterpretAsync(transcribedText);

            if (intent != null)
            {
                // 4. 解析回傳並寫入 Config
                string ttsMessage = WriteConfig(intent, eqConfigPath);
                int retryCount = 0;

                // 模型偶爾會回出不成形的內容，重試幾次通常就過了
                while (ttsMessage=="-1" && retryCount < 3)
                {
                    intent = await _llm.InterpretAsync(transcribedText);
                    ttsMessage = WriteConfig(intent, eqConfigPath);
                    retryCount++;
                }
                if (retryCount == 3)
                {
                    await _TtsService.SpeakAsync("很抱歉，無法產生有效指令，請稍後再試");
                }

                string situationIdString = situationId.ToString();
                _notifier.Notify("已套用設定", "心頻氣和已完成解析並套用設定");

                _backend.Apply(eqConfigPath);

                // 5. 使用 TTS 播放回應訊息
                await _TtsService.SpeakAsync(ttsMessage);

                await _TtsService.SpeakAsync("是否需要調整回原本的內容"); // constant

                FileCreateData newCreateData = new FileCreateData
                {
                    FileName = eqConfigPath,
                    UserInput = transcribedText,
                    AiResponse = ttsMessage
                };

                string recordPath = await recordPathTask;
                

                
                // 6. 詢問是否套用設定
                _ChatManager.PushFront("-1", newCreateData);
                if (await _notifier.ConfirmAsync("回退確認","是否要取消此設定"))
                {
                    // rollback
                    await ConfigRollback("-1", _ChatManager);
                }
                else
                {
                    if(situationIdString != "-1")
                    {
                        _ChatManager.PushFront(situationIdString, newCreateData);
                    }

                    // 7. 詢問是否需要儲存成preset
                    await _TtsService.SpeakAsync("是否需要存為preset"); // constant

                    if (await _notifier.ConfirmAsync("preset設定", "是否要存為preset"))
                    {
                        _ChatManager.PushFront(_ChatManager.GetNextId().ToString(), newCreateData, transcribedText, recordPath);
                    }
                }

                // Inserted wait for one second as requested (非阻塞)
                await Task.Delay(1000);

                _notifier.Notify("設定結束", "調整已結束，請享受更好的聲音");
                // 儲存 Chat
                _ChatManager.SaveToJson();
            }
            return;
        }
    }
}
