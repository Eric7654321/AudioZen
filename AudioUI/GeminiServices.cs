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

namespace WpfApp1
{
    public class GeminiServices
    {
        // --- 功能 1: 使用 NAudio 錄音 ---
        public Task RecordAudioAsync(string filePath, int durationMs)
        {
            var tcs = new TaskCompletionSource<bool>();

            // 設定錄音格式 (44.1kHz, Mono)
            // 為了相容性和檔案大小，這裡使用 16-bit PCM (Gemini 吃這個沒問題)
            var waveFormat = new WaveFormat(44100, 16, 1);

            var waveIn = new WaveInEvent();
            waveIn.WaveFormat = waveFormat;

            var writer = new WaveFileWriter(filePath, waveIn.WaveFormat);

            // 當有聲音資料進來時寫入檔案
            waveIn.DataAvailable += (s, a) =>
            {
                writer.Write(a.Buffer, 0, a.BytesRecorded);
            };

            // 當錄音停止時，釋放資源
            waveIn.RecordingStopped += (s, a) =>
            {
                writer.Dispose();
                waveIn.Dispose();
                tcs.SetResult(true); // 通知 Task 完成
            };

            waveIn.StartRecording();

            // 設定一個計時器來停止錄音
            Task.Delay(durationMs).ContinueWith(_ =>
            {
                waveIn.StopRecording();
            });

            return tcs.Task;
        }

        // --- 功能 2: 檔案轉 Base64 ---
        public string ConvertFileToBase64(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            return Convert.ToBase64String(bytes);
        }

        // --- 功能 3: 呼叫 Gemini API ---
        string optimizeText = 
        """
        You are an audio engineer. Listen to the user's voice command and generate an Equalizer APO GraphicEQ configuration using these 15 frequency bands: 
        25, 40, 63, 100, 160, 250, 400, 630, 1000, 1600, 2500, 4000, 6300, 10000, 16000.
        Begin with a concise checklist (3-7 bullets) of what you will do; keep items conceptual, not implementation-level.
        Generation Rules:
        1. For each band, assign the gain (in dB) as a floating-point number based on the user's intent. 
        Only assign a gain if the intended adjustment exceeds 7.0 dB; otherwise, set the gain for that band to 0.0 dB.
        2. Determine 'preamp_db': Find the maximum positive gain across all bands. 
        If any gain is positive, set preamp_db to a negative value whose magnitude is greater than or equal to this maximum (e.g., if max gain is +15.2 dB, preamp_db should be -15.2 dB or lower, such as -15.8 dB). 
        If no positive gains exist (all bands are 0.0 dB or negative), set preamp_db to 0.0.
        3. Compose 'graphic_eq_string' in this precise order and format: 
        '25 [gain]; 40 [gain]; 63 [gain]; 100 [gain]; 160 [gain]; 250 [gain]; 400 [gain]; 630 [gain]; 1000 [gain]; 1600 [gain]; 2500 [gain]; 4000 [gain]; 6300 [gain]; 10000 [gain]; 16000 [gain]'.
        If the user's request is silent or ambiguous, set all gains to 0.0 dB and preamp_db to 0.0.
        After generating the output, validate that the JSON object matches the required schema and confirm all fields are present and formatted as specified; self-correct if validation fails.
        ATTENTION: Output ONLY a JSON object using precisely this schema:
        {
        "message_for_user": "string (A brief summary in Traditional Chinese, max 15 words, describing change)",
        "preamp_db": float,
        "graphic_eq_string": "string (The formatted frequency-gain pairs separated by semicolons)"
        }
        ## Output Format
        Return a single JSON object with:
        - message_for_user: (string) Brief Traditional Chinese summary (maximum 15 words) of the change made.
        - preamp_db: (float) As specified by rule #2 above, or 0.0 if no positive gain.
        - graphic_eq_string: (string) The 15 frequency bands formatted as 'frequency gain', separated by semicolons, and in ascending order.
        If input is silent or ambiguous, all gains must be 0.0.
        Example output:
        {
        "message_for_user": "已根據您的需求調整各頻段等化器",
        "preamp_db": -8.5,
        "graphic_eq_string": "25 0.0; 40 0.0; 63 8.5; 100 0.0; 160 0.0; 250 0.0; 400 0.0; 630 0.0; 1000 0.0; 1600 0.0; 2500 0.0; 4000 0.0; 6300 0.0; 10000 0.0; 16000 0.0"
        }
        """;



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
    }
}
