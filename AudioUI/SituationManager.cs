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
    public class SituationManager
    {
        // --- 功能 1: 使用 NAudio 錄音 ---
                private readonly IAudioBackend _backend;
        private readonly INotifier _notifier;
        private readonly ISpeechInput _speech;
        private readonly ILlmClient _llm;
        private readonly IConfigStore _store;

        /// <summary>全部可注入，測試才有辦法在不碰使用者設定、不寫進 APO、不跳通知、不講話也不打 API 的情況下跑。</summary>
        public SituationManager(IAudioBackend? backend = null, INotifier? notifier = null,
                               ISpeechInput? speech = null, ILlmClient? llm = null,
                               IConfigStore? store = null, ITextToSpeech? tts = null)
        {
            _TtsService = tts ?? AppConfig.TextToSpeech;
            _store = store ?? AppConfig.ConfigStore;
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

        // --- 功能 4: 還原設定檔 ---
        public async Task ConfigRollback(string IdString)
        {
            _store.PopFront(IdString);
            if (_store.Front(IdString) == null)
            {
                await _TtsService.SpeakAsync("已經沒有更早的設定可以還原"); // constant
                return;
            }
            string originconfigPath = _store.Front(IdString).FileName;
            _backend.Apply(originconfigPath);
        }
        private readonly ITextToSpeech _TtsService;
        AudioSessionService _AudioSessionService = new AudioSessionService();

        /// <summary>
        /// 完整的進行一次錄音、分析與寫入的過程 (goal 1)
        /// </summary>
        public async Task RecordAndProcessAsync(int situationId, string audioFilePath, string eqConfigPath, int recordMs = 5000)
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
                string? ttsMessage = _backend.Write(intent, eqConfigPath);
                int retryCount = 0;

                // 模型偶爾會回出不成形的內容，重試幾次通常就過了
                while (ttsMessage == null && retryCount < 3)
                {
                    intent = await _llm.InterpretAsync(transcribedText);
                    ttsMessage = _backend.Write(intent, eqConfigPath);
                    retryCount++;
                }
                if (ttsMessage == null)
                {
                    // 寫檔是先截斷再寫，所以失敗留下的是空檔或半截檔；套用它等於把設定清掉。
                    await _TtsService.SpeakAsync("很抱歉，無法產生有效指令，請稍後再試");
                    return;
                }

                string situationIdString = situationId.ToString();
                _notifier.Notify("已套用設定", "心頻氣和已完成解析並套用設定");

                _backend.Apply(eqConfigPath);

                // 5. 使用 TTS 播放回應訊息
                await _TtsService.SpeakAsync(ttsMessage);

                await _TtsService.SpeakAsync("是否需要調整回原本的內容"); // constant

                SituationEntry newCreateData = new SituationEntry
                {
                    FileName = eqConfigPath,
                    UserInput = transcribedText,
                    AiResponse = ttsMessage
                };

                string recordPath = await recordPathTask;
                

                
                // 6. 詢問是否套用設定
                _store.PushFront(SituationIds.Transient, newCreateData);
                if (await _notifier.ConfirmAsync("回退確認","是否要取消此設定"))
                {
                    // rollback
                    await ConfigRollback(SituationIds.Transient);
                }
                else
                {
                    if(situationIdString != SituationIds.Transient)
                    {
                        _store.PushFront(situationIdString, newCreateData);
                    }

                    // 7. 詢問是否需要儲存成preset
                    await _TtsService.SpeakAsync("是否需要存為preset"); // constant

                    if (await _notifier.ConfirmAsync("preset設定", "是否要存為preset"))
                    {
                        _store.PushFront(_store.NextId().ToString(), newCreateData, transcribedText, recordPath);
                    }
                }

                // Inserted wait for one second as requested (非阻塞)
                await Task.Delay(1000);

                _notifier.Notify("設定結束", "調整已結束，請享受更好的聲音");
                // 儲存 Chat
                _store.Save();
            }
            return;
        }
    }
}
