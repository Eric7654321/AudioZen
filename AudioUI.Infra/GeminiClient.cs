using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AudioUI
{
    /// <summary>
    /// 用 Gemini 實作 <see cref="ILlmClient"/>。prompt、HTTP、以及回應外殼的拆解都關在這裡，
    /// 呼叫端只看得到 <see cref="AudioIntent"/>。
    /// </summary>
    public sealed class GeminiClient : ILlmClient
    {
        private readonly GeminiSettings _settings;
        private readonly RouteTable _routes;

        public GeminiClient(GeminiSettings settings, RouteTable routes)
        {
            _settings = settings;
            _routes = routes;
        }

        // 每次取用時才組，因為 key 可能來自環境變數，而設定是延遲載入的。
        private string GeminiUrl => _settings.BuildGenerateContentUrl();

        public async Task<AudioIntent?> InterpretAsync(string userText, IReadOnlyList<string>? memories = null)
        {
            string raw = await CallGeminiApiAsync(userText, GeminiUrl, memories);
            return ParseIntent(raw);
        }

        public Task<string> TranscribeAsync(string base64Wav) => TranscribeWithGeminiAsync(base64Wav, GeminiUrl);

        /// <summary>把回應外殼剝掉，取出模型真正寫的那段 JSON。認不出來時回 null。</summary>
        private static AudioIntent? ParseIntent(string rawResponse)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var response = JsonSerializer.Deserialize<GeminiResponse>(rawResponse, options);

            if (response?.Candidates == null || response.Candidates.Count == 0) return null;

            // Gemini 3.x 會夾帶只有 thoughtSignature、沒有 text 的思考片段，
            // 所以不能寫死 Parts[0]，要取第一個真的有內容的。
            string? json = response.Candidates[0].Content?.Parts
                ?.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Text))?.Text;

            if (string.IsNullOrWhiteSpace(json)) return null;

            // 即使要求了 application/json，模型偶爾仍會包一層 markdown 圍欄。
            json = json.Replace("```json", "").Replace("```", "").Trim();
            return JsonSerializer.Deserialize<AudioIntent>(json, options);
        }

        // --- 功能 2: 呼叫 Gemini API ---
        // 目標清單由路由表生成，所以 prompt 與實際能解析的目標不可能講不一樣的話。
        // 模型只需要吐 browser / voice_chat / game 這種代號，不必複述含 GUID 的裝置全名。
        string optimizeText =>
        "You are an audio engineer. Analyze to the user's text command. " +
        "Based on the request, generate an Equalizer APO configuration. " +
        $"You manage {_routes.TargetCount} specific targets: " +
        "1. 'all': Applies to everything (Global). " +
        _routes.PromptTargetLines() +
        "Frequencies: 25, 40, 63, 100, 160, 250, 400, 630, 1000, 1600, 2500, 4000, 6300, 10000, 16000. " +
        "Rules: " +
        "1. Decide which target(s) to modify based on the user's intent. If vague, use 'all'. You can return multiple configs if needed. " +
        "2. For each config, calculate 'preamp_db' (usually negative, |preamp| >= max_gain). " +
        "   - positive value only available if user intend to increase the volume, but max_gain must be <= 6.0" +
        "3. Construct 'graphic_eq_string'. " +
        "   [CRITICAL FORMATTING RULE ABOUT RULE 3]: " +
"   - The output MUST differ from standard filter syntax. " +
"   - CORRECT FORMAT: '25 4.3; 40 1.5; 63 -4.0; ...' (Frequency[space]Gain[semicolon]) (Gain ranges from -10.0 to 10.0). " +
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
        "      \"target\": " + _routes.PromptTargetUnion() + ", " +
        "      \"preamp_db\": float, " +
        "      \"graphic_eq_string\": \"string\" " +
        "      \"comp_json\": [ ... ], " +
        "      \"reverb_json\": [ ... ] " +
        "    } " +
        "  ] " +
        "}";

        /// <summary>
        /// 組出送給模型的完整 prompt。抽成獨立方法是為了讓記憶有沒有真的進去這件事測得到——
        /// 走 HTTP 才驗得到的話，這條路上就沒有任何測試。
        /// </summary>
        public string BuildPrompt(string userSpeech, IReadOnlyList<string>? memories = null)
        {
            var sb = new StringBuilder(optimizeText);

            // 沒有記憶時一個字都不加：記憶功能關掉的 prompt 要跟沒有這個功能時完全相同。
            var usable = memories?.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
            if (usable is { Count: > 0 })
            {
                sb.Append("\n\nKnown user preferences (context only, the User Command still wins):");
                foreach (string m in usable) sb.Append("\n- ").Append(m.Trim());
            }

            sb.Append($"\n\nUser Command: \"{userSpeech}\"");
            return sb.ToString();
        }

        private async Task<string> CallGeminiApiAsync(string userSpeech, string url, IReadOnlyList<string>? memories = null)
        {
            // 將語音轉換成文字
            if (string.IsNullOrWhiteSpace(userSpeech))
            {
                return "{\"message_for_user\": \"我聽不清楚，請再說一次。\", \"configs\": []}";
            }


            string fullPrompt = BuildPrompt(userSpeech, memories);

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
                // Gemini 3.x 會夾帶只有 thoughtSignature、沒有 text 的思考片段，
                // 所以不能寫死 parts[0]，要取第一個真的有內容的。
                var parts = node?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
                var text = parts?
                    .Select(p => p?["text"]?.ToString())
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

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

                    // 5xx 與 429 都是等一下再試就會好的暫時性失敗，其餘 4xx 重試幾次也不會變。
                    // 429 要單獨列：它不在 >= 500 的範圍內，而配額是這裡最常見的失敗原因。
                    if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
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
    }
}
