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
        /// <summary>環境變數優先於設定檔，讓 CI 與臨時測試不必在磁碟上留 key。</summary>
        public const string ApiKeyEnvironmentVariable = "AUDIOZEN_GEMINI_API_KEY";

        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "gemini-3.6-flash";

        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

        /// <summary>
        /// 帶 key 的 generateContent URL。key 放在 query string，
        /// 所以拿到這個字串的人不需要另外知道 key 的存在。
        /// </summary>
        public string BuildGenerateContentUrl()
        {
            if (!IsConfigured)
                throw new InvalidOperationException(
                    "找不到 Gemini API key。請複製 appsettings.example.json 成 appsettings.json 並填入 key，" +
                    $"或設定環境變數 {ApiKeyEnvironmentVariable}。");

            return $"{Endpoint.TrimEnd('/')}/models/{Model}:generateContent?key={ApiKey}";
        }
    }
}
