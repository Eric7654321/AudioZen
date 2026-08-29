using System.Text.Encodings.Web;
using System.Text.Json;

namespace AudioUI
{
    /// <summary>
    /// 把情境存成 <c>config/file_mapping.json</c>。
    ///
    /// 讀寫失敗只通知、不丟例外：這些呼叫散在 UI 事件處理器裡，讓存檔失敗炸掉整個互動
    /// 比少存一次更糟；而使用者仍然需要知道剛才那筆沒留下來。
    /// </summary>
    public sealed class JsonConfigStore : IConfigStore
    {
        private readonly string _filePath;
        private readonly INotifier _notifier;
        private List<Situation> _situations = new List<Situation>();

        public JsonConfigStore(INotifier notifier, string? filePath = null)
        {
            _notifier = notifier;
            _filePath = filePath
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "file_mapping.json");
        }

        public IReadOnlyList<Situation> Situations => _situations;

        public Situation? ById(string? id) => _situations.FirstOrDefault(x => x.Id == id);

        public IReadOnlyList<SituationSummary> Summaries()
        {
            var list = new List<SituationSummary>();
            foreach (var item in _situations)
            {
                if (item.Id == SituationIds.Transient) continue;

                // 沒有標題時退而求其次用最新一則的使用者輸入，再不然給一個佔位字串。
                string name = item.ChatName;
                if (string.IsNullOrWhiteSpace(name) && item.FileDatas.Count > 0) name = item.FileDatas[0].UserInput;
                if (string.IsNullOrWhiteSpace(name)) name = "New Chat";

                list.Add(new SituationSummary { Id = item.Id, DisplayName = name });
            }
            return list;
        }

        public IReadOnlyList<SituationEntry> History(string id, int limit = 20)
        {
            var item = ById(id);
            if (item == null) return new List<SituationEntry>();

            // 存的順序是新的在前，畫面要舊的在上，所以取前 N 筆再反轉。
            return item.FileDatas.Take(limit).Reverse().ToList();
        }

        public string NextId()
        {
            var used = new HashSet<int>();
            foreach (var item in _situations)
                if (int.TryParse(item.Id, out int value)) used.Add(value);

            int candidate = 0;
            while (used.Contains(candidate)) candidate++;
            return candidate.ToString();
        }

        public void PushFront(string id, SituationEntry entry, string displayName = "", string recordPath = "")
        {
            var item = ById(id);
            if (item == null)
            {
                string title = !string.IsNullOrEmpty(displayName) ? displayName
                             : !string.IsNullOrEmpty(entry.UserInput) ? entry.UserInput
                             : "New Chat";
                if (title.Length > 20) title = title.Substring(0, 20) + "...";

                item = new Situation { Id = id, ChatName = title, RecordPath = recordPath };
                _situations.Add(item);
            }
            else if (string.IsNullOrEmpty(item.RecordPath) && !string.IsNullOrEmpty(recordPath))
            {
                item.RecordPath = recordPath;
            }

            item.FileDatas.Insert(0, entry);
        }

        public SituationEntry? PopFront(string id)
        {
            var item = ById(id);
            if (item == null || item.FileDatas.Count == 0) return null;

            var popped = item.FileDatas[0];
            item.FileDatas.RemoveAt(0);
            return popped;
        }

        public SituationEntry? Front(string id)
        {
            var item = ById(id);
            return item == null || item.FileDatas.Count == 0 ? null : item.FileDatas[0];
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // UnsafeRelaxedJsonEscaping：否則中文會被寫成 \uXXXX，存檔沒辦法用眼睛看。
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                };
                File.WriteAllText(_filePath, JsonSerializer.Serialize(_situations, options));
            }
            catch (Exception ex)
            {
                _notifier.Notify("存檔失敗", ex.Message);
            }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                };
                var loaded = JsonSerializer.Deserialize<List<Situation>>(File.ReadAllText(_filePath), options);
                if (loaded != null) _situations = loaded;
            }
            catch (Exception ex)
            {
                _notifier.Notify("讀檔失敗", ex.Message);
            }
        }
    }
}
