using System.Text.Json.Serialization;

namespace AudioUI
{
    /// <summary>
    /// 使用者在設定頁按出來的偏好。
    ///
    /// 刻意不併進 <see cref="AppSettings"/>：那份是人手改的機器設定（API key、APO 路徑、
    /// 路由表），這份是程式在使用者撥開關時自己覆寫的。混在同一個檔裡，等於讓程式定期
    /// 重寫一個放著 key 的檔案——排版會掉、手改的內容也可能被蓋。
    /// </summary>
    public sealed class UserPreferences
    {
        /// <summary>語音辨識掛的詞。是成語而不是產品名「心頻氣和」，因為辨識器認得的是前者。</summary>
        public const string DefaultWakeWord = "心平氣和";

        // --- 一般 ---

        [JsonPropertyName("launchAtStartup")]
        public bool LaunchAtStartup { get; set; }

        [JsonPropertyName("autoUpdate")]
        public bool AutoUpdate { get; set; } = true;

        // --- 記憶 ---

        [JsonPropertyName("userMemoryEnabled")]
        public bool UserMemoryEnabled { get; set; } = true;

        /// <summary>使用者自己寫的自我介紹。</summary>
        [JsonPropertyName("userMemory")]
        public string UserMemory { get; set; } = "";

        [JsonPropertyName("aiMemoryEnabled")]
        public bool AiMemoryEnabled { get; set; } = true;

        /// <summary>模型從對話裡歸納出來的偏好，設定頁要能逐條看與刪。</summary>
        [JsonPropertyName("aiMemories")]
        public List<string> AiMemories { get; set; } = new List<string>();

        /// <summary>
        /// 讓模型依歷史自動調整音訊。hi-fi 自己標了 experimental，
        /// 而它會在沒人要求的時候改變聲音，所以預設關閉。
        /// </summary>
        [JsonPropertyName("selfLearningEnabled")]
        public bool SelfLearningEnabled { get; set; }

        // --- 個人化 ---

        [JsonPropertyName("wakeWord")]
        public string WakeWord { get; set; } = DefaultWakeWord;

        /// <summary>實際掛給辨識器的詞。空字串會讓辨識器建不出文法，所以退回預設值。</summary>
        [JsonIgnore]
        public string EffectiveWakeWord =>
            string.IsNullOrWhiteSpace(WakeWord) ? DefaultWakeWord : WakeWord.Trim();

        /// <summary>要餵給模型的記憶。兩個開關各自關掉自己那半，回傳的東西可以直接接進 prompt。</summary>
        public IReadOnlyList<string> MemoriesForPrompt()
        {
            var list = new List<string>();
            if (UserMemoryEnabled && !string.IsNullOrWhiteSpace(UserMemory)) list.Add(UserMemory.Trim());
            if (AiMemoryEnabled) list.AddRange(AiMemories.Where(m => !string.IsNullOrWhiteSpace(m)));
            return list;
        }

        /// <summary>加一條 AI 記憶。空白與重複不進去，否則列表會被同一句話灌滿。</summary>
        public bool AddAiMemory(string? text)
        {
            string trimmed = (text ?? "").Trim();
            if (trimmed.Length == 0) return false;
            if (AiMemories.Any(m => string.Equals(m.Trim(), trimmed, StringComparison.Ordinal))) return false;

            AiMemories.Add(trimmed);
            return true;
        }

        public bool RemoveAiMemory(string? text)
        {
            string trimmed = (text ?? "").Trim();
            int removed = AiMemories.RemoveAll(m => string.Equals(m.Trim(), trimmed, StringComparison.Ordinal));
            return removed > 0;
        }
    }
}
