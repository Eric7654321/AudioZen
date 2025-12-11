using HandyControl.Controls;
using HandyControl.Tools.Extension;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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



        // --- 功能 3: 呼叫 Gemini API ---
        string optimizeText = // constant
        "You are an audio engineer. Listen to the user's voice command. " +
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
        "WARNING: Output ONLY a JSON object with this exact structure: " +
        "{ " +
        "  \"message_for_user\": \"string (Explain in 15 words in Traditional Chinese)\", " +
        "  \"configs\": [ " +
        "    { " +
        "      \"target\": \"all\"|\"first\"|\"second\"|\"third\", " +
        "      \"preamp_db\": float, " +
        "      \"graphic_eq_string\": \"string\" " +
        "    } " +
        "  ] " +
        "}";



        string promptText =
        "You are an audio engineer. Listen to the user's voice command. " +
        "Based on the request, generate an Equalizer APO GraphicEQ configuration using these specific 15 frequency bands: " +
        "25, 40, 63, 100, 160, 250, 400, 630, 1000, 1600, 2500, 4000, 6300, 10000, 16000. " +
        "Rules for generation: " +
        "1. Determine the gain (dB) for each band based on the user's intent and return in floating number. " +
        "Besides, the gain should be larger than 7.0dB, or just remain no gain."+
        "2. Calculate 'preamp_db'. logic: Identify the maximum positive gain among all bands. " +
        "The preamp_db must be negative and its absolute value must be greater than or equal to that maximum gain (e.g., if max gain is +15.2dB, preamp must be -15.2dB or lower, like -15.8dB). " +
        "3. Construct 'graphic_eq_string' in the format: '25 [gain]; 40 [gain]; ...' " +
        "WARNING: Output ONLY a JSON object with this exact structure: " +
        "{ " +
        "  \"message_for_user\": \"string (Explain briefly what you changed in 15 words in Traditional Chinese)\", " +
        "  \"preamp_db\": float, " +
        "  \"graphic_eq_string\": \"string (The formatted frequency-gain pairs separated by semicolons)\" " +
        "}";
        public async Task<string> CallGeminiApiAsync(string base64Audio, string url)
        {
            using var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AudioUI/1.0");

            // 建構 JSON Payload (使用匿名物件讓 System.Text.Json 自動序列化)
            var payload = new
            {
                generationConfig = new { responseMimeType = "application/json" },
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = optimizeText },
                            new { inlineData = new { mimeType = "audio/wav", data = base64Audio } }
                        }
                    }
                }
            };

            string jsonContent = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // 重試機制
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
                    if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode == HttpStatusCode.ServiceUnavailable)
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

            throw new InvalidOperationException("不可達的程式路徑：CallGeminiApiAsync 重試機制異常結束。");
        }

        // --- 功能 4: 解析回傳並寫入 Config ---
        public string ParseAndWriteConfig(string rawResponse, string outputPath, Dictionary<string, string> deviceMap)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(rawResponse, options);

            if (geminiResponse?.Candidates == null || geminiResponse.Candidates.Count == 0)
                throw new Exception("No candidates found.");

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

                        // 4. 加入一個空行分隔不同裝置的設定 (可選)
                        sw.WriteLine();
                    }
                }
            }

            return eqResponse.MessageForUser;
        }

        public async Task ConfigRollback(string IdString, string configPath)
        {
            _MappingManager.PopFront(IdString);
            if (_MappingManager.GetFront(IdString) == "")
            {
                await _TtsService.SpeakAsync("已經沒有更早的設定可以還原"); // constant
                return;
            }
            string originconfigPath = _MappingManager.GetFront(IdString);
            File.Copy(originconfigPath, configPath, overwrite: true);
        }


        private const string API_KEY = "AIzaSyBJe-x4R2675FWctAAY3UrfW8hM1z9taoE"; // constant
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
            // 第一次回應
            await _TtsService.SpeakAsync("請問​今天​需要​我​幫忙​做​什​麼"); // constant

            // 錄音recordMs 毫秒
            string audioBase64 = await RecordAudioAsync(audioFilePath, recordMs);

            // 提示正在解析
            await _TtsService.SpeakAsync("正在解析中"); // constant

            // 3. 呼叫 Gemini API
            string geminiResponse = await CallGeminiApiAsync(audioBase64, GEMINI_URL);

            if (!string.IsNullOrEmpty(geminiResponse))
            {
                // 4. 解析回傳並寫入 Config
                string ttsMessage = ParseAndWriteConfig(geminiResponse, eqConfigPath, myDeviceMap);

                // 套用到目前config
                string configPath = Path.Combine(".", "config", "config.txt"); // constant
                string situationIdString = situationId.ToString();
                _MappingManager.PushFront(situationIdString, eqConfigPath);

                File.Copy(eqConfigPath, configPath, overwrite: true);

                // 5. 使用 TTS 播放回應訊息
                await _TtsService.SpeakAsync(ttsMessage);

                await _TtsService.SpeakAsync("是否需要調整回原本的內容"); // constant

                // 輸入需要還原
                MessageBox.Show(_MappingManager.MapList[0].FileNames.Count.ToString());
                if (false)
                {
                    // rollback
                    await ConfigRollback(situationIdString, configPath);
                }
                _MappingManager.SaveToJson();
            }
            return;
        }
    }
}
