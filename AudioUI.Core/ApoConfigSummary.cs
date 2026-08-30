using System.Text.RegularExpressions;

namespace AudioUI
{
    /// <summary>
    /// 從 APO 設定檔的文字讀出「這個裝置現在被套了什麼」。
    /// 通知列上每個程式後面那句效果摘要就是這裡產生的。
    /// </summary>
    public static class ApoConfigSummary
    {
        /// <summary>
        /// 依 <c>Device:</c> 切段，回傳 [關鍵字 → 效果摘要]。
        /// 關鍵字用子字串比對，跟 APO 自己比對裝置的語意一致。
        /// </summary>
        public static Dictionary<string, string> ByKeyword(string? rawConfig, IEnumerable<string> keywords)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(rawConfig)) return result;

            var wanted = keywords?.ToList() ?? new List<string>();
            if (wanted.Count == 0) return result;

            var sections = Regex.Split(rawConfig, @"(?=^Device:)", RegexOptions.Multiline)
                                .Where(s => !string.IsNullOrWhiteSpace(s));

            foreach (var section in sections)
            {
                var line = Regex.Match(section, @"^Device:\s*(.+)$", RegexOptions.Multiline);
                if (!line.Success) continue;

                string deviceLine = line.Groups[1].Value;
                var matched = wanted.FirstOrDefault(k =>
                    !string.IsNullOrEmpty(k) &&
                    deviceLine.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);

                // 先出現的先贏：同一個裝置在檔案裡出現兩次時，第一段才是實際在用的那份。
                if (matched != null && !result.ContainsKey(matched))
                    result[matched] = Describe(section);
            }

            return result;
        }

        /// <summary>把一段設定講成一句話，例如 "MCompressor + EQ + (-6 dB)"。</summary>
        public static string Describe(string? section)
        {
            if (string.IsNullOrWhiteSpace(section) || section == "無") return "無效果";

            var parts = new List<string>();

            // VST 外掛認檔名，路徑怎麼寫都行。
            foreach (Match m in Regex.Matches(section, @"\\([^\\]+?)\.dll", RegexOptions.IgnoreCase))
                parts.Add(m.Groups[1].Value);

            if (section.Contains("GraphicEQ:") && section.Length > 50) parts.Add("EQ");

            var preamp = Regex.Match(section, @"Preamp:\s*([-\d\.]+\s*dB)");
            if (preamp.Success)
            {
                string db = preamp.Groups[1].Value;
                // 0 dB 是「沒動過」，寫出來只會讓每一張卡片都多一個沒有意義的括號。
                if (!db.StartsWith("0.0") && !db.StartsWith("0 dB")) parts.Add($"({db})");
            }

            return parts.Count == 0 ? "自訂設定" : string.Join(" + ", parts);
        }
    }
}
