using System.Text.RegularExpressions;

namespace AudioUI
{
    /// <summary>
    /// 路由樣式 ↔ 這台機器上的實際裝置。
    ///
    /// APO 的比對規則是「以空白分隔的詞全部都要出現在裝置字串裡」，只有 AND、沒有否定
    /// （EqualizerAPO 的 <c>DeviceFilterFactory::matchDevice</c>；含 <c>{</c> 的詞比對含 GUID 的原字串，
    /// 其餘的詞比對去掉 GUID 之後的字串）。
    ///
    /// 於是「Voicemeeter Input」的每個詞都出現在「Voicemeeter AUX Input ...」裡面：
    /// 主匯流排的短名稱一定會連 AUX 一起中，而反過來不會。短名稱裡找不到能區分兩者的詞，
    /// 唯一分得開的是 GUID——所以樣式撞到兩台以上的裝置時，寫給 APO 的 <c>Device:</c> 要把 GUID 補回去。
    /// </summary>
    public static class DevicePatterns
    {
        private static readonly Regex GuidPattern = new Regex(
            @"\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}",
            RegexOptions.Compiled);

        /// <summary>這個樣式在這台機器上會中的所有裝置。兩個以上就是分不開。</summary>
        public static IReadOnlyList<string> Candidates(string? pattern, IEnumerable<string>? devices) =>
            (devices ?? Enumerable.Empty<string>())
                .Where(d => DependencyChecker.DeviceMatches(pattern, d))
                .ToList();

        /// <summary>
        /// 樣式指的是哪一台裝置。中不到就是 <c>null</c>。
        ///
        /// 中一台以上時取「樣式原封不動出現在裝置名稱裡」的那台：主匯流排叫「Voicemeeter Input」，
        /// AUX 叫「Voicemeeter AUX Input」，中間插了字就不算。這跟安裝程式判斷裝置在不在的方式一致，
        /// 不是靠列舉順序決定。
        /// </summary>
        public static string? Resolve(string? pattern, IEnumerable<string>? devices)
        {
            var candidates = Candidates(pattern, devices);
            if (candidates.Count <= 1) return candidates.FirstOrDefault();

            string phrase = Phrase(pattern);
            var exact = candidates
                .Where(d => d.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return exact.Count == 1 ? exact[0] : candidates[0];
        }

        /// <summary>
        /// 寫進 APO 設定檔的 <c>Device:</c> 樣式。
        ///
        /// 只中一台就原樣送出——樣式是使用者的設定，沒有理由改寫。中兩台以上才補上 GUID，
        /// 否則 APO 會把這一段套到兩台裝置上，兩段設定還會在其中一台身上疊起來。
        /// </summary>
        public static string Specific(string? pattern, IEnumerable<string>? devices)
        {
            string text = (pattern ?? "").Trim();
            var candidates = Candidates(text, devices);
            if (candidates.Count <= 1) return text;

            string? guid = GuidOf(Resolve(text, candidates));
            return guid == null ? text : $"{text} {guid}";
        }

        /// <summary>裝置字串裡的 endpoint GUID。認不出來就回 <c>null</c>，不亂拼一個。</summary>
        internal static string? GuidOf(string? identity)
        {
            if (string.IsNullOrWhiteSpace(identity)) return null;

            var matches = GuidPattern.Matches(identity);
            return matches.Count == 0 ? null : matches[^1].Value;
        }

        /// <summary>把樣式還原成一句話，用來判斷裝置名稱裡有沒有原封不動的它。</summary>
        private static string Phrase(string? pattern) =>
            string.Join(" ", (pattern ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
