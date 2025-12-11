using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Net.Http.Headers;

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
        string optimizeText =
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
        "Besides, the gain should be larger than 7.0dB, or just remain no gain."+ // TODO: 改成其他合適的數字
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

        private const string API_KEY = "AIzaSyAbcdVglE0htVqhzzajRshijkK41qBblPg";
        private const string GEMINI_URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent?key=" + API_KEY;
        GeminiParser _GeminiParser = new GeminiParser();
        TtsService _TtsService = new TtsService();


        public async Task RecordAndProcessAsync(int recordMs,string audioFilePath, string eqConfigPath)
        {
            var myDeviceMap = new Dictionary<string, string>
{
                { "first", "Speakers (Realtek(R) Audio)" },    // 第一個裝置的真實名稱
                { "second", "Headphones (HyperX Cloud II)" },  // 第二個裝置的真實名稱
                { "third", "VG279Q (NVIDIA High Definition Audio)" } // 第三個裝置
            };  
            // 第一次回應
            _TtsService.SpeakAsync("請開始說出您的音效調整需求，錄音將持續五秒鐘。").Wait();

            // 1. 錄音 5 秒
            string audioBase64 = await RecordAudioAsync(audioFilePath, recordMs);

            // 3. 呼叫 Gemini API
            string geminiResponse = await CallGeminiApiAsync(audioBase64, GEMINI_URL);

            if (!string.IsNullOrEmpty(geminiResponse))
            {
                // 4. 解析回傳並寫入 Config
                string ttsMessage = _GeminiParser.ParseAndWriteConfig(geminiResponse, eqConfigPath, myDeviceMap);

                // 5. 使用 TTS 播放回應訊息
                await _TtsService.SpeakAsync(ttsMessage);
            }
            return;
        }
    }
}
