namespace AudioUI
{
    /// <summary>
    /// 收集畫面繫結失效的訊息。繫結錯誤不會讓編譯失敗也不會讓測試變紅，執行時只是
    /// 那一格空白，跟「本來就沒東西」看起來一樣，所以要有地方把它數出來。
    ///
    /// 去重：一條壞掉的繫結寫在 DataTemplate 裡，畫面上有幾筆資料就會重複報幾次，
    /// 五十筆清單能把一個錯誤放大成五十行，把真正的第二個錯誤推出視線外。
    /// </summary>
    public sealed class BindingErrorLog
    {
        private readonly List<string> _messages = new();
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly int _limit;

        /// <param name="limit">最多留幾種不同的訊息。超過就只計數，不再留內容。</param>
        public BindingErrorLog(int limit = 50) => _limit = limit;

        /// <summary>看過的訊息種類，依第一次出現的順序。</summary>
        public IReadOnlyList<string> Messages => _messages;

        /// <summary>不同的錯誤有幾種。重複的同一條只算一次。</summary>
        public int DistinctCount => _seen.Count;

        /// <summary>總共被報了幾次，含重複。</summary>
        public int TotalCount { get; private set; }

        public bool IsEmpty => TotalCount == 0;

        /// <summary>記下一條。回傳這條是不是第一次看到。</summary>
        public bool Record(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            string text = message.Trim();
            TotalCount++;

            if (!_seen.Add(text)) return false;
            if (_messages.Count < _limit) _messages.Add(text);
            return true;
        }

        /// <summary>一句話講清楚有沒有事、有幾件。給通知用。</summary>
        public string Summary => IsEmpty
            ? "繫結全部解得開。"
            : $"有 {DistinctCount} 種繫結解不開（共 {TotalCount} 次）。";

        /// <summary>完整報告，寫檔用。</summary>
        public string Report()
        {
            if (IsEmpty) return Summary;

            var lines = new List<string> { Summary };
            lines.AddRange(_messages.Select((m, i) => $"{i + 1}. {m}"));
            if (DistinctCount > _messages.Count)
                lines.Add($"…另有 {DistinctCount - _messages.Count} 種未列出。");
            return string.Join(Environment.NewLine, lines);
        }
    }
}
