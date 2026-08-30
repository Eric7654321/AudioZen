using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    public sealed class UserPreferences : INotifyPropertyChanged
    {
        /// <summary>語音辨識掛的詞。是成語而不是產品名「心頻氣和」，因為辨識器認得的是前者。</summary>
        public const string DefaultWakeWord = "心平氣和";

        // --- 一般 ---

        private bool _launchAtStartup;
        private bool _autoUpdate = true;
        private bool _userMemoryEnabled = true;
        private string _userMemory = "";
        private bool _aiMemoryEnabled = true;
        private bool _selfLearningEnabled;
        private string _wakeWord = DefaultWakeWord;

        [JsonPropertyName("launchAtStartup")]
        public bool LaunchAtStartup { get => _launchAtStartup; set { _launchAtStartup = value; Raise(); } }

        [JsonPropertyName("autoUpdate")]
        public bool AutoUpdate { get => _autoUpdate; set { _autoUpdate = value; Raise(); } }

        // --- 記憶 ---

        [JsonPropertyName("userMemoryEnabled")]
        public bool UserMemoryEnabled { get => _userMemoryEnabled; set { _userMemoryEnabled = value; Raise(); } }

        /// <summary>使用者自己寫的自我介紹。</summary>
        [JsonPropertyName("userMemory")]
        public string UserMemory { get => _userMemory; set { _userMemory = value ?? ""; Raise(); } }

        [JsonPropertyName("aiMemoryEnabled")]
        public bool AiMemoryEnabled { get => _aiMemoryEnabled; set { _aiMemoryEnabled = value; Raise(); } }

        /// <summary>
        /// 模型從對話裡歸納出來的偏好，設定頁要能逐條看與刪。
        /// 用 ObservableCollection 是因為刪掉一條之後畫面要立刻少一行。
        /// </summary>
        [JsonPropertyName("aiMemories")]
        public ObservableCollection<string> AiMemories { get; set; } = new ObservableCollection<string>();

        /// <summary>
        /// 讓模型依歷史自動調整音訊。hi-fi 自己標了 experimental，
        /// 而它會在沒人要求的時候改變聲音，所以預設關閉。
        /// </summary>
        [JsonPropertyName("selfLearningEnabled")]
        public bool SelfLearningEnabled { get => _selfLearningEnabled; set { _selfLearningEnabled = value; Raise(); } }

        // --- 個人化 ---

        /// <summary>
        /// 每台裝置的自訂圖，鍵是裝置名稱。用名稱而不是另發一組 id，
        /// 是因為名稱已經是現成的身分——按鍵綁定就是用 <c>device.Name</c> 去查的。
        /// </summary>
        [JsonPropertyName("deviceImages")]
        public Dictionary<string, string> DeviceImages { get; set; } = new Dictionary<string, string>();

        [JsonPropertyName("wakeWord")]
        public string WakeWord
        {
            get => _wakeWord;
            set { _wakeWord = value ?? ""; Raise(); Raise(nameof(EffectiveWakeWord)); }
        }

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

        // --- 裝置 ---

        /// <summary>某台裝置的自訂圖；沒設定過回 null，呼叫端據此沿用內建的圖。</summary>
        public string? DeviceImage(string? deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return null;
            return DeviceImages.TryGetValue(deviceName.Trim(), out string? path) && !string.IsNullOrWhiteSpace(path)
                ? path
                : null;
        }

        /// <summary>設定或清除自訂圖。傳空路徑等於清除，讓「還原成預設」不必另開一個方法。</summary>
        public bool SetDeviceImage(string? deviceName, string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return false;
            string key = deviceName.Trim();

            if (string.IsNullOrWhiteSpace(imagePath)) return DeviceImages.Remove(key);

            DeviceImages[key] = imagePath.Trim();
            return true;
        }

        /// <summary>把自訂圖蓋到一份裝置清單上。沒設定過的維持原樣。</summary>
        public void ApplyDeviceImages(IEnumerable<DeviceInfoModel>? devices)
        {
            if (devices == null) return;
            foreach (var d in devices)
            {
                string? custom = DeviceImage(d.Name);
                if (custom != null) d.ImagePath = custom;
            }
        }

        public bool RemoveAiMemory(string? text)
        {
            string trimmed = (text ?? "").Trim();
            var match = AiMemories.FirstOrDefault(m => string.Equals(m.Trim(), trimmed, StringComparison.Ordinal));
            return match != null && AiMemories.Remove(match);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
