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
    }

    public sealed class GeminiSettings
    {
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "gemini-3.5-flash-lite";

        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    }

    /// <summary>
    /// 設定的單一入口。取代原本散在 GeminiServices 與 MainWindow 的兩份 API_KEY 常數
    /// ——兩份會漂移，而且 const 代表 key 一定進版控。
    /// </summary>
    public static class AppConfig
    {
        private const string EnvApiKey = "AUDIOZEN_GEMINI_API_KEY";

        private static readonly Lazy<AppSettings> _settings = new Lazy<AppSettings>(Load);

        public static AppSettings Settings => _settings.Value;

        /// <summary>設定是否可用。UI 想在送出前先擋掉可以看這個，不必接例外。</summary>
        public static bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.Gemini.ApiKey);

        /// <summary>
        /// 帶 key 的 generateContent URL。維持原本「key 放 query string」的形狀，
        /// 所以 CallGeminiApiAsync 這類吃 url 的方法簽章不用動。
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
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
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
