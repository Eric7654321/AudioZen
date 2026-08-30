namespace AudioUI
{
    /// <summary>一條路由的體檢結果。</summary>
    public sealed class RouteDiagnostic
    {
        public RouteDiagnostic(AudioRoute route, string? matchedDevice)
        {
            RouteId = route.Id;
            DisplayName = string.IsNullOrWhiteSpace(route.DisplayName) ? route.Id : route.DisplayName;
            DevicePattern = route.DevicePattern;
            MatchedDevice = matchedDevice;
        }

        public string RouteId { get; }

        public string DisplayName { get; }

        public string DevicePattern { get; }

        /// <summary>實際對上的裝置名稱；<c>null</c> 表示這條路由指向一個不存在的裝置。</summary>
        public string? MatchedDevice { get; }

        public bool Ok => MatchedDevice != null;

        /// <summary>
        /// 這個樣式看起來是哪個產品提供的。從樣式文字猜的，只用來給使用者一個下載方向，
        /// 不是權威資訊——所以認不出來時回 null 而不是瞎編一個。
        /// </summary>
        public string? LikelyProvider =>
            DevicePattern.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase) ? "VB-Audio Voicemeeter"
            : DevicePattern.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ? "VB-Audio VB-CABLE"
            : null;
    }

    /// <summary>
    /// 執行環境的體檢報告。
    ///
    /// 這個程式要能運作，靠的是三樣它自己沒有的東西：Equalizer APO、以及路由表裡指到的虛擬裝置。
    /// 少了任何一樣，套用設定會安靜地什麼都不做——使用者只會覺得「按了沒反應」。
    /// 把缺什麼講出來，是自動安裝與自動接線的前提：不知道現況就無從決定要做什麼。
    /// </summary>
    public sealed class DependencyReport
    {
        public DependencyReport(bool apoInstalled, IReadOnlyList<RouteDiagnostic> routes)
        {
            ApoInstalled = apoInstalled;
            Routes = routes;
        }

        public bool ApoInstalled { get; }

        public IReadOnlyList<RouteDiagnostic> Routes { get; }

        /// <summary>全部就位才算可用。有一條路由指向不存在的裝置，那條路由上的 app 就調不動。</summary>
        public bool IsReady => ApoInstalled && Routes.All(r => r.Ok);

        /// <summary>可以直接顯示給使用者的問題清單，沒問題時是空的。</summary>
        public IReadOnlyList<string> Problems
        {
            get
            {
                var list = new List<string>();

                if (!ApoInstalled)
                    list.Add("找不到 Equalizer APO 的設定目錄。請確認它已安裝，或在設定裡指定實際位置。");

                foreach (var r in Routes.Where(r => !r.Ok))
                {
                    string provider = r.LikelyProvider == null ? "" : $"，通常由 {r.LikelyProvider} 提供";
                    list.Add($"「{r.DisplayName}」找不到對應的音訊裝置（需要符合「{r.DevicePattern}」的裝置{provider}）。");
                }

                return list;
            }
        }

        /// <summary>一行摘要，給設定頁的標題列用。</summary>
        public string Summary => IsReady
            ? $"就緒，{Routes.Count} 條路由都對得上裝置"
            : $"有 {Problems.Count} 項問題";
    }

    public static class DependencyChecker
    {
        /// <summary>
        /// 用 APO 的比對語意判斷一個裝置是否符合樣式：樣式裡以空白分隔的每個字詞，
        /// 都必須出現在裝置的識別字串裡（子字串、不分大小寫）。
        ///
        /// 刻意跟 APO 一致而不是自己發明一套：判斷結果若跟實際套用時不同，
        /// 這份報告就會說「沒問題」而使用者按下去沒反應，那比沒有報告更糟。
        /// </summary>
        public static bool DeviceMatches(string? devicePattern, string? deviceIdentity)
        {
            if (string.IsNullOrWhiteSpace(devicePattern) || string.IsNullOrWhiteSpace(deviceIdentity)) return false;

            string[] words = devicePattern.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return words.Length > 0
                && words.All(w => deviceIdentity.Contains(w, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 體檢。<paramref name="deviceIdentities"/> 是目前系統上的音訊輸出裝置，
        /// 由呼叫端列舉後傳進來——列舉要碰 COM，而這裡要保持可測。
        /// </summary>
        public static DependencyReport Check(bool apoInstalled, IEnumerable<string>? deviceIdentities, RouteTable? routes)
        {
            var devices = (deviceIdentities ?? Enumerable.Empty<string>())
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .ToList();

            var diagnostics = (routes?.Routes ?? new List<AudioRoute>())
                .Select(r => new RouteDiagnostic(r, devices.FirstOrDefault(d => DeviceMatches(r.DevicePattern, d))))
                .ToList();

            return new DependencyReport(apoInstalled, diagnostics);
        }
    }
}
