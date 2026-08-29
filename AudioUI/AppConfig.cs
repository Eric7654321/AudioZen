using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioUI
{
    /// <summary>
    /// 執行期設定。key 不進版控，所以 <c>appsettings.json</c> 被 gitignore，
    /// 版控裡放的是 <c>appsettings.example.json</c> 樣板。
    /// </summary>
    public sealed class AppSettings
    {
        [JsonPropertyName("gemini")]
        public GeminiSettings Gemini { get; set; } = new GeminiSettings();

        /// <summary>省略時採用 <see cref="RouteTable.Default"/>。裝置名稱與 GUID 逐機不同，
        /// 所以這是最常需要按機器覆寫的一段。</summary>
        [JsonPropertyName("routes")]
        public List<AudioRoute>? Routes { get; set; }

        [JsonPropertyName("apo")]
        public ApoSettings Apo { get; set; } = new ApoSettings();
    }

    public sealed class GeminiSettings
    {
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "gemini-3.6-flash";

        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    }

    /// <summary>
    /// 設定的單一入口。key 只在這裡出現一次：多一個地方持有它，就多一個會漂移、
    /// 也多一個會跟著版控走的地方。刻意不用 const —— const 的值一定寫在原始碼裡。
    /// </summary>
    public static class AppConfig
    {
        private const string EnvApiKey = "AUDIOZEN_GEMINI_API_KEY";

        private static readonly Lazy<AppSettings> _settings = new Lazy<AppSettings>(Load);

        private static readonly Lazy<RouteTable> _routes =
            new Lazy<RouteTable>(() => Settings.Routes is { Count: > 0 } r ? new RouteTable(r) : RouteTable.Default());

        public static AppSettings Settings => _settings.Value;

        /// <summary>app 與虛擬裝置的對應。所有需要這份知識的地方都從這裡拿，不各自持有一份。</summary>
        public static RouteTable Routes => _routes.Value;

        /// <summary>套用設定的後端。唯一知道設定該寫到哪裡的地方。</summary>
        public static IAudioBackend AudioBackend => _backend.Value;

        private static readonly Lazy<IAudioBackend> _backend =
            new Lazy<IAudioBackend>(() => new EqualizerApoBackend(Settings.Apo));

        /// <summary>設定是否可用。UI 想在送出前先擋掉可以看這個，不必接例外。</summary>
        public static bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.Gemini.ApiKey);

        /// <summary>
        /// 帶 key 的 generateContent URL。key 放在 query string，
        /// 所以吃 url 的呼叫端（CallGeminiApiAsync 等）不需要知道 key 的存在。
        /// </summary>
        public static string GeminiUrl
        {
            get
            {
                var g = Settings.Gemini;
                if (string.IsNullOrWhiteSpace(g.ApiKey))
                    throw new InvalidOperationException(
                        $"找不到 Gemini API key。請複製 appsettings.example.json 成 appsettings.json 並填入 key，" +
                        $"或設定環境變數 {EnvApiKey}。");

                return $"{g.Endpoint.TrimEnd('/')}/models/{g.Model}:generateContent?key={g.ApiKey}";
            }
        }

        private static AppSettings Load()
        {
            var settings = new AppSettings();

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (File.Exists(path))
            {
                try
                {
                    // 讓 JSON 用 camelCase 寫，與 GeminiSettings 上已有的 JsonPropertyName 慣例一致。
                    var opts = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true,
                    };
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), opts) ?? new AppSettings();
                }
                catch (JsonException ex)
                {
                    // 設定檔壞掉要講清楚是哪個檔，否則會被誤判成「key 沒填」。
                    throw new InvalidOperationException($"appsettings.json 格式錯誤：{ex.Message}", ex);
                }
            }

            // 環境變數優先於檔案，讓 CI / 臨時測試不必在磁碟上留 key。
            string? fromEnv = Environment.GetEnvironmentVariable(EnvApiKey);
            if (!string.IsNullOrWhiteSpace(fromEnv))
                settings.Gemini.ApiKey = fromEnv;

            return settings;
        }
    }
}
