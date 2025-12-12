using HandyControl.Controls;
using HandyControl.Tools.Extension;
using Microsoft.Toolkit.Uwp.Notifications;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace AudioUI
{
    public class GeminiServices
    {
        // --- 功能 1: 使用 NAudio 錄音 ---
        public Task<string> RecordAudioAsync(string filePath, int durationMs)
        {
            // 1. 改成 TaskCompletionSource<string> 以便回傳字串
            var tcs = new TaskCompletionSource<string>();

            var waveFormat = new WaveFormat(44100, 16, 1);
            var waveIn = new WaveInEvent();
            waveIn.WaveFormat = waveFormat;

            var writer = new WaveFileWriter(filePath, waveIn.WaveFormat);

            waveIn.DataAvailable += (s, a) =>
            {
                writer.Write(a.Buffer, 0, a.BytesRecorded);
            };

            // 2. 將讀取檔案與回傳結果的邏輯移到 RecordingStopped 事件中
            waveIn.RecordingStopped += (s, a) =>
            {
                try
                {
                    // 必須先 Dispose 釋放檔案鎖定 (File Lock)
                    writer.Dispose();
                    waveIn.Dispose();

                    // 檢查錄音過程是否有錯誤
                    if (a.Exception != null)
                    {
                        tcs.SetException(a.Exception);
                        return;
                    }

                    // 此時檔案已經存檔完畢且解除鎖定，可以安全讀取
                    byte[] bytes = File.ReadAllBytes(filePath);
                    string returnString = Convert.ToBase64String(bytes);
                    //string configPath = Path.Combine(".", "fixedCommand.wav");
                    //byte[] bytes = File.ReadAllBytes(configPath);

                    // 設定 Task 完成並回傳字串
                    tcs.SetResult(returnString);
                }
                catch (Exception ex)
                {
                    // 捕捉讀檔過程可能發生的錯誤
                    tcs.SetException(ex);
                }
            };

            waveIn.StartRecording();

            // 設定計時器，時間到停止錄音 (這會觸發上面的 RecordingStopped 事件)
            Task.Delay(durationMs).ContinueWith(_ =>
            {
                waveIn.StopRecording();
            });

            // 回傳 Task，等待 RecordingStopped 裡的 SetResult 被呼叫
            return tcs.Task;
        }

        // --- 功能 2: 呼叫 Gemini API ---
        string optimizeText = // constant
        "You are an audio engineer. Analyze to the user's text command. " +
        "Based on the request, generate an Equalizer APO configuration. " +
        "You manage 4 specific targets: " +
        "1. 'all': Applies to everything (Global). " +
        "2. 'first': Primary device (e.g., Speakers). " +
        "3. 'second': Secondary device (e.g., Headphones). " +
        "4. 'third': Tertiary device (e.g., Communication/Game). " +
        "Frequencies: 25, 40, 63, 100, 160, 250, 400, 630, 1000, 1600, 2500, 4000, 6300, 10000, 16000. " +
        "Rules: " +
        "1. Decide which target(s) to modify based on the user's intent. If vague, use 'all'. You can return multiple configs if needed. " +
        "2. For each config, calculate 'preamp_db' (must be negative, |preamp| >= max_gain). " +
        "3. Construct 'graphic_eq_string'. " +
        "   [CRITICAL FORMATTING RULE ABOUT RULE 3]: " +
"   - The output MUST differ from standard filter syntax. " +
"   - CORRECT FORMAT: '25 0.5; 40 1.2; 63 -2.0; ...' (Frequency[space]Gain[semicolon]). " +
"   - WRONG FORMAT: 'Filter: ON PK Fc 25 Hz...' (DO NOT USE THIS). " +
"   - FORBIDDEN WORDS: 'Filter', 'ON', 'PK', 'Fc', 'Hz', 'Gain', 'Q', ':'. " +
"   - ONLY use numbers, spaces, and semicolons. " +
        "4. Construct 'Meldaproduction Compressor VST' JSON -- comp_json." +
        "    - Please fill in the values." +
        "    - Format:" +
        "    [" +
        "        { \"raw_key\": \"A#\", \"value\": 1 }," +
        "        { \"raw_key\": \"A#gain\", \"value\": (float, -24.0 to 24.0) }," +
        "        { \"raw_key\": \"A#outputgain\", \"value\": (float, -24.0 to 24.0) }," +
        "        { \"raw_key\": \"A#attack\", \"value\": (float, 0.0 to 1.0) }," +
        "        { \"raw_key\": \"A#release\", \"value\": (float, 0.0 to 1.0) }," +
        "        { \"raw_key\": \"A#rmslength\", \"value\": (float, 0.0 to 0.1) }," +
        "        { \"raw_key\": \"A#threshold\", \"value\": (float, 0.01 to 1.0) }," +
        "        { \"raw_key\": \"A#ratio\", \"value\": (float, 1.0 to 20.0) }," +
        "        { \"raw_key\": \"A#kneemode\", \"value\": (string, \"Hard\" or \"Linear\" or \"Soft\") }," +
        "        { \"raw_key\": \"A#kneesize\", \"value\": (float, 0.0 to 1.0) }," +
        "        { \"raw_key\": \"A#maximize\", \"value\": (0 or 1) }," +
        "        { \"raw_key\": \"A#customshape\", \"value\": (0 or 1) }," +
        "        { \"raw_key\": \"AVersion\", \"value\": 1115136 }," +
        "        { \"raw_key\": \"AMIDIProgramChangeEnable\", \"value\": 0 }," +
        "        { \"raw_key\": \"AProgramChangeCategorizer\", \"value\": 0 }," +
        "        { \"raw_key\": \"AEditorSize\", \"value\": \"901,527\" }," +
        "        { \"raw_key\": \"AControllersEnabled\", \"value\": 0 }," +
        "        { \"raw_key\": \"APluginToolbarCollapsed\", \"value\": 255 }," +
        "        { \"raw_key\": \"AMaxLFOBlockSize\", \"value\": 32 }," +
        "        { \"raw_key\": \"ALRSplitterSize1\", \"value\": 534.0 }," +
        "        { \"raw_key\": \"ALRSplitterSize2\", \"value\": 306.0 }," +
        "        { \"raw_key\": \"ASideChainEnable\", \"value\": (0 or 1) }," +
        "        { \"raw_key\": \"ASideChainMinFrequency\", \"value\": (float, 20.0 to 19999.99999999998) }," +
        "        { \"raw_key\": \"ASideChainMaxFrequency\", \"value\": (float, 20.0 to 19999.99999999998) }," +
        "        { \"raw_key\": \"Ahalfgain\", \"value\": 0 }," +
        "        { \"raw_key\": \"Aamplituderatio\", \"value\": 0 }," +
        "        { \"raw_key\": \"Xgraph\", \"value\": None }," +
        "        { \"raw_key\": \"Mode\", \"value\": \"Normal\" }, " +
        "        { \"raw_key\": \"XPoint\", \"value\": None }," +
        "        { \"raw_key\": \"FlagsB\", \"value\": 143 }," +
        "        { \"raw_key\": \"/XPoint\", \"value\": None }," +
        "        { \"raw_key\": \"x\", \"value\": (float, 0.01 to 1.0) }," +
        "        { \"raw_key\": \"Ay\", \"value\": (float, 0.01 to 1.0) }," +
        "        { \"raw_key\": \"AFlagsB\", \"value\": 140 }," +
        "        { \"raw_key\": \"/XPoint\", \"value\": None }," +
        "        { \"raw_key\": \"x\", \"value\": (float, 0.01 to 1.0) }," +
        "        { \"raw_key\": \"Ay\", \"value\": (float, 0.01 to 1.0) }," +
        "        { \"raw_key\": \"AFlagsB\", \"value\": 141 }," +
        "    ]" +
        "5. Construct 'Meldaproduction CharmVerb VST' JSON -- reverb_json." +
        "    - Please fill in the values." +
        "    - Format:" +
        "    [" +
        "        { \"raw_key\": \"A#\", \"value\": 1 }," +
        "        { \"raw_key\": \"A#DryWet\", \"value\": (float, 0.0 to 1.0) }," +
        "        { \"raw_key\": \"A#Length\", \"value\": (float, 0.1 to 60.0) }," +
        "        { \"raw_key\": \"A#Size\", \"value\": (float, 0.0 to 1.0) }," +
        "        { \"raw_key\": \"A#LPF\", \"value\": (float, 3.0 to 3.3010299956639813 }," +
        "        { \"raw_key\": \"A#HPF\", \"value\": (float, 3.0 to 3.3010299956639813 }," +
        "        { \"raw_key\": \"A#Predelay\", \"value\": (float, 0.0 to 1.0) }," +
        "        { \"raw_key\": \"A#Gain\", \"value\": (float, -24.0 to 24.0) }," +
        "        { \"raw_key\": \"A#Widening\", \"value\": (float, -1.0 to 2.0) }," +
        "        { \"raw_key\": \"A#DampLowF\", \"value\": (float, 20.0 to 19999.99999999998) }," +
        "        { \"raw_key\": \"A#DampLowG\", \"value\": (float, -20.0 to 0.0) }," +
        "        { \"raw_key\": \"A#DampLowQ\", \"value\": (float, 0.05 to 0.7071067811865476) }," +
        "        { \"raw_key\": \"A#DampHighF\", \"value\": (float, 20.0 to 19999.99999999998) }," +
        "        { \"raw_key\": \"A#DampHighG\", \"value\": (float, -20.0 to 0.0) }," +
        "        { \"raw_key\": \"A#DampHighQ\", \"value\": (float, 0.05 to 0.7071067811865476) }," +
        "        { \"raw_key\": \"A#DesignerCollapsed\", \"value\": 0 }," +
        "        { \"raw_key\": \"A#Complexity\", \"value\": (int 1 to 64) }," +
        "        { \"raw_key\": \"A#Modulation\", \"value\": (float, 0.0 to 1.0) }," +
        "        { \"raw_key\": \"A#Seed\", \"value\": 1791916693 }," +
        "        { \"raw_key\": \"A#DelayMin\", \"value\": (float, 0.0 to 1.0) }," +
        "        { \"raw_key\": \"A#DelayMax\", \"value\": (float, 0.0 to 1.0) }," +
        "        { \"raw_key\": \"A#FocusDelay\", \"value\": (float, -4.0 to 4.0) }," +
        "        { \"raw_key\": \"A#WidthDelay\", \"value\": (float, 0.0 to 1.0) }," +
        "        { \"raw_key\": \"A#OrderDelay\", \"value\": (string, \"Up\" or \"Up (2 sets)\" or \"Up (3 sets)\" or \"Up (4 sets)\" or \"Down\" or \"Down (2 sets)\" or \"Down (3 sets)\" or \"Down (4 sets)\" or \"Random\") }," +
        "        { \"raw_key\": \"A#ModulationRate\", \"value\": (float, 0.010000000000000004 to 10.0) }," +
        "        { \"raw_key\": \"AVersion\", \"value\": 1115136 }," +
        "        { \"raw_key\": \"AMIDIProgramChangeEnable\", \"value\": 0 }," +
        "        { \"raw_key\": \"AProgramChangeCategorizer\", \"value\": 0 }," +
        "        { \"raw_key\": \"AEditorSize\", \"value\": 751573 }," +
        "        { \"raw_key\": \"APluginToolbarCollapsed\", \"value\": 255 }," +
        "        { \"raw_key\": \"AMaxLFOBlockSize\", \"value\": 32 }," +
        "    ]" +
        "WARNING: Output ONLY a JSON object with this exact structure: " +
        "{ " +
        "  \"message_for_user\": \"string (Explain in 15 words in Traditional Chinese)\", " +
        "  \"configs\": [ " +
        "    { " +
        "      \"target\": \"all\"|\"first\"|\"second\"|\"third\", " +
        "      \"preamp_db\": float, " +
        "      \"graphic_eq_string\": \"string\" " +
        "      \"comp_json\": [ ... ], " +
        "      \"reverb_json\": [ ... ] " +
        "    } " +
        "  ] " +
        "}";



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
        public async Task<string> CallGeminiApiAsync(string userSpeech, string url)
        {
            // 將語音轉換成文字
            if (string.IsNullOrWhiteSpace(userSpeech))
            {
                return "{\"message_for_user\": \"我聽不清楚，請再說一次。\", \"configs\": []}";
            }


            // 組合最終的 Prompt
            string fullPrompt = optimizeText + $"\n\nUser Command: \"{userSpeech}\"";

            // 建構 JSON Payload
            var payload = new
            {
                generationConfig = new { responseMimeType = "application/json" },
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = fullPrompt }
                        }
                    }
                }
            };

            string jsonContent = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // 呼叫 Gemini API request並回傳結果
            return await SendGeminiRequestAsync(url, payload);
        }

        // 輔助功能：將語音轉換成文字
        private async Task<string> TranscribeWithGeminiAsync(string base64Audio, string url)
        {
            // 簡單的 Prompt，告訴 Gemini 只要轉錄就好
            var payload = new
            {
                contents = new[]
                {
                new
                {
                    parts = new object[]
                    {
                        new { text = "Please transcribe this audio into Traditional Chinese text exactly as spoken. Do not add any introduction or explanation. Output only the text." },
                        new { inlineData = new { mimeType = "audio/wav", data = base64Audio } }
                    }
                }
            }
            };
            string responseJson = await SendGeminiRequestAsync(url, payload);
            try
            {
                // 使用 JsonNode 解析複雜的巢狀結構
                var node = JsonNode.Parse(responseJson);
                // 路徑通常是: candidates[0].content.parts[0].text
                var text = node?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

                return text?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        // 輔助功能：發送 Gemini API 請求並處理重試邏輯
        private async Task<string> SendGeminiRequestAsync(string url, object payload)
        {
            using var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };
            var jsonPayload = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                HttpResponseMessage response = null!;
                string responseBody = "";
                try
                {
                    response = await client.PostAsync(url, httpContent);
                    responseBody = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        return responseBody;
                    }

                    // 若為 5xx 或 429 (rate limit) 或 503，嘗試重試
                    if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        // 若已是最後一次重試，拋出包含 body 的例外，便於診斷
                        if (attempt == maxRetries)
                        {
                            throw new HttpRequestException($"API 無法服務 (狀態碼: {(int)response.StatusCode} {response.ReasonPhrase}). Response body: {responseBody}");
                        }

                        // 等待指數退避
                        int delayMs = (int)Math.Pow(2, attempt) * 1000;
                        await Task.Delay(delayMs);
                        continue;
                    }

                    // 其他非成功回應（例如 4xx），直接拋出詳細例外
                    throw new HttpRequestException($"API 請求失敗 (狀態碼: {(int)response.StatusCode} {response.ReasonPhrase}). Response body: {responseBody}");
                }
                catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
                {
                    // timeout
                    if (attempt == maxRetries) throw new TimeoutException("API 請求逾時。", ex);
                    await Task.Delay((int)Math.Pow(2, attempt) * 1000);
                    continue;
                }
                catch (Exception)
                {
                    // 若是最後一次重試，rethrow，否則等待再重試
                    if (attempt == maxRetries) throw;
                    await Task.Delay((int)Math.Pow(2, attempt) * 1000);
                }
            }
            throw new InvalidOperationException("不可達的程式路徑：重試機制異常結束。");
        }

        

        // --- 功能 3: 解析回傳並寫入 Config ---
        public string ParseAndWriteConfig(string rawResponse, string outputPath, Dictionary<string, string> deviceMap)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(rawResponse, options);

            if (geminiResponse?.Candidates == null || geminiResponse.Candidates.Count == 0)
            {
                return "-1";
            }

            string innerJsonText = geminiResponse.Candidates[0].Content.Parts[0].Text;

            // 清理 Markdown
            innerJsonText = innerJsonText.Replace("```json", "").Replace("```", "").Trim();

            // 解析新的結構
            var eqResponse = JsonSerializer.Deserialize<EqConfig>(innerJsonText, options);

            using (StreamWriter sw = new StreamWriter(outputPath, false)) // false 表示覆寫檔案
            {
                if (eqResponse.Configs != null)
                {
                    foreach (var config in eqResponse.Configs)
                    {
                        // 1. 處理 Device 行
                        string targetKey = config.Target?.ToLower();

                        if (targetKey == "all")
                        {
                            // 如果是 all，通常不需要指定 Device，或者您可以根據需求決定是否要重置 Device 選擇
                            // 這裡示範：寫入一行註解，或者什麼都不寫代表全域
                            sw.WriteLine("# Global Setting");
                        }
                        else if (!string.IsNullOrEmpty(targetKey) && deviceMap.ContainsKey(targetKey))
                        {
                            // 根據 map 寫入實際裝置名稱
                            sw.WriteLine($"Device: {deviceMap[targetKey]}");
                        }
                        else
                        {
                            // 如果 AI 回傳了 first 但 map 裡沒有，可以選擇跳過或記錄錯誤
                            // 這裡選擇寫入一個預設註解以供除錯
                            sw.WriteLine($"# Unknown Target: {targetKey}");
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
        public async Task ConfigRollback(string IdString, string configPath)
        {
            if (_MappingManager.GetFront(IdString) == null)
            {
                await _TtsService.SpeakAsync("已經沒有更早的設定可以還原"); // constant
                return;
            }
            string originconfigPath = _MappingManager.GetFront(IdString).FileName;
            File.Copy(originconfigPath, configPath, overwrite: true);
        }
        /// <summary>
        /// 發送通知
        /// </summary>
        private void SendNotification(string title, string context)
        {
            new ToastContentBuilder()
                .AddText(title) // 標題
                .AddText(context) // 內文
                .Show(); // 發送通知
        }

        /// <summary>
        /// 發送通知並「非同步等待」使用者回應
        /// </summary>
        /// <returns>Task<bool>: True=接受, False=拒絕或超時</returns>
        public async Task<bool> SendNotificationAndWaitAsync(string title, string context)
        {
            // 1. 建立一個任務完成來源，用來當作暫停點
            var tcs = new TaskCompletionSource<bool>();

            // 2. 定義臨時的事件處理邏輯
            // 這裡我們用 lambda 抓取回傳結果
            void handler(ToastNotificationActivatedEventArgsCompat e)
            {
                var args = ToastArguments.Parse(e.Argument);

                // 檢查是否有 action 參數
                if (args.TryGetValue("action", out string action))
                {
                    // 根據使用者的選擇設定結果
                    if (action == "yes")
                    {
                        tcs.TrySetResult(true);
                    }
                    else
                    {
                        tcs.TrySetResult(false);
                    }
                }
            };

            // 3. 註冊事件監聽
            ToastNotificationManagerCompat.OnActivated += handler;

            try
            {
                // 4. 建構並發送通知
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(context)
                    .AddButton(new ToastButton("是", "action=yes"))
                    .AddButton(new ToastButton("否", "action=no"))
                    .Show();

                // 5. 等待使用者回應，或是等待 10 秒超時 (避免程式永遠卡住)
                // Task.WhenAny 會回傳最先完成的那個 Task
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(10000));

                if (completedTask == tcs.Task)
                {
                    // 使用者在時間內回應了
                    return await tcs.Task;
                }
                else
                {
                    // 超時了 (使用者沒點)
                    // 這裡可以選擇清除通知，避免過期點擊
                    ToastNotificationManagerCompat.History.Clear();
                    return false; // 預設回傳 false
                }
            }
            finally
            {
                // 6. 重要：無論結果如何，一定要取消註冊事件，避免記憶體洩漏或重複觸發
                ToastNotificationManagerCompat.OnActivated -= handler;
            }
        }





        private const string API_KEY = "AIzaSyC58BU_c7KfydnxiAGXWNn7Ry220kmFsZo"; // constant AIzaSyCMRnOADLA-VpgjY0e9dfAPLAkd-LApf_8
        private const string GEMINI_URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent?key=" + API_KEY;
        TtsService _TtsService = new TtsService();
        MappingManager _MappingManager = new MappingManager();

        /// <summary>
        /// 完整的進行一次錄音、分析與寫入的過程 (goal 1)
        /// </summary>
        public async Task RecordAndProcessAsync(int situationId, int recordMs,string audioFilePath, string eqConfigPath)
        {
            var myDeviceMap = new Dictionary<string, string>
{
                { "first", "Speakers (Realtek(R) Audio)" },    // 第一個裝置的真實名稱  // constant
                { "second", "Headphones (HyperX Cloud II)" },  // 第二個裝置的真實名稱
                { "third", "VG279Q (NVIDIA High Definition Audio)" } // 第三個裝置
            };  
            // 0. 第一次回應
            await _TtsService.SpeakAsync("請問您想如何調整音訊設定"); // constant

            // 1. 錄音recordMs 毫秒
            string audioBase64 = await RecordAudioAsync(audioFilePath, recordMs);

            // 2. 提示正在解析
            await _TtsService.SpeakAsync("正在解析中"); // constant

            // 3. 呼叫 Gemini API
            string transcribedText = await TranscribeWithGeminiAsync(audioBase64, GEMINI_URL); // STT
            System.Windows.MessageBox.Show(transcribedText);
            string geminiResponse = await CallGeminiApiAsync(transcribedText, GEMINI_URL);

            if (!string.IsNullOrEmpty(geminiResponse))
            {
                // 4. 解析回傳並寫入 Config
                string ttsMessage = ParseAndWriteConfig(geminiResponse, eqConfigPath, myDeviceMap);
                int retryCount = 0;

                // 處理geminiResponse的無效回應
                while (ttsMessage=="-1" && retryCount < 3)
                {
                    geminiResponse = await CallGeminiApiAsync(transcribedText, GEMINI_URL);
                    ttsMessage = ParseAndWriteConfig(geminiResponse, eqConfigPath, myDeviceMap);
                    retryCount++;
                }
                if (retryCount == 3)
                {
                    await _TtsService.SpeakAsync("很抱歉，無法產生有效指令，請稍後再試");
                }

                // 套用到config.txt
                string configTxtPath = Path.Combine(".", "config", "config.txt"); // constant
                string situationIdString = situationId.ToString();
                
                File.Copy(eqConfigPath, configTxtPath, overwrite: true);

                // 5. 使用 TTS 播放回應訊息
                await _TtsService.SpeakAsync(ttsMessage);

                await _TtsService.SpeakAsync("是否需要調整回原本的內容"); // constant

                FileCreateData newCreateData = new FileCreateData
                {
                    FileName = eqConfigPath,
                    UserInput = transcribedText,
                    AiResponse = ttsMessage
                };
                // 6. 詢問是否套用設定
                if (await SendNotificationAndWaitAsync("回退確認","是否要取消此設定"))
                {
                    // rollback
                    await ConfigRollback("-1", configTxtPath);
                }
                else
                {
                    _MappingManager.PushFront("-1", newCreateData);
                }
                // 7. 詢問是否需要儲存成preset
                await _TtsService.SpeakAsync("是否需要存為preset"); // constant
                if (await SendNotificationAndWaitAsync("preset設定","是否要存為preset"))
                {
                    _MappingManager.PushFront(situationIdString, newCreateData);
                }

                // 儲存 mapping
                _MappingManager.SaveToJson();
            }
            return;
        }
    }
}
