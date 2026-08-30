using System.Text.Encodings.Web;
using System.Text.Json;

namespace AudioUI
{
    /// <summary>
    /// 把偏好存成 <c>config/preferences.json</c>。
    ///
    /// 跟 <see cref="JsonConfigStore"/> 同樣的取捨：讀寫失敗只通知、不丟例外。
    /// 撥一個開關失敗不該讓設定頁整個炸掉，但使用者要知道這次沒存下來。
    /// </summary>
    public sealed class JsonPreferencesStore : IPreferencesStore
    {
        private readonly string _filePath;
        private readonly INotifier _notifier;
        private UserPreferences _current = new UserPreferences();

        public JsonPreferencesStore(INotifier notifier, string? filePath = null)
        {
            _notifier = notifier;
            _filePath = filePath
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "preferences.json");
        }

        public UserPreferences Current => _current;

        public void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;

                var loaded = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(_filePath), Options);
                if (loaded != null) _current = loaded;
            }
            catch (Exception ex)
            {
                // 讀壞掉的檔案就留著預設值，不要讓使用者連設定頁都打不開。
                _notifier.Notify("偏好讀取失敗", ex.Message);
            }
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(_filePath, JsonSerializer.Serialize(_current, Options));
            }
            catch (Exception ex)
            {
                _notifier.Notify("偏好存檔失敗", ex.Message);
            }
        }

        /// <summary>UnsafeRelaxedJsonEscaping：喚醒詞與記憶都是中文，逃脫成 \uXXXX 就沒辦法用眼睛看。</summary>
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }
}
