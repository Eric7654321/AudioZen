namespace AudioUI
{
    public enum DependencyKind
    {
        EqualizerApo,
        Voicemeeter,
        VbCable,
        GeminiApiKey,
        RouteConfiguration,
        MeldaVst,
        ZhTwSpeech,
    }

    /// <summary>相依的資訊說明、必要性與就緒狀態。</summary>
    public sealed class DependencyItem
    {
        public DependencyItem(DependencyKind kind, string name, string detail, bool isRequired, bool isReady)
        {
            Kind = kind;
            Name = name;
            Detail = detail;
            IsRequired = isRequired;
            IsReady = isReady;
        }

        public DependencyKind Kind { get; }
        public string Name { get; }
        public string Detail { get; }
        public bool IsRequired { get; }
        public bool IsOptional => !IsRequired;
        public bool IsReady { get; }

    }

    /// <summary>Windows 層收集到的資訊；不在這裡做產品判斷，讓判斷保持可測。</summary>
    public sealed class DependencySnapshot
    {
        public bool ApoInstalled { get; init; }
        public bool CompressorInstalled { get; init; }
        public bool ReverbInstalled { get; init; }
        public bool ZhTwSpeechInstalled { get; init; }
        public bool ApiKeyConfigured { get; init; }
        public IReadOnlyList<string> RenderDevices { get; init; } = Array.Empty<string>();
    }

    public interface IDependencyProbe
    {
        DependencySnapshot Inspect();
    }

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

    /// <summary>執行環境的體檢報告，可找出缺少的安裝項目。</summary>
    public sealed class DependencyReport
    {
        public DependencyReport(bool apoInstalled, IReadOnlyList<RouteDiagnostic> routes,
                                IReadOnlyList<DependencyItem> items)
        {
            ApoInstalled = apoInstalled;
            Routes = routes;
            Items = items;
        }

        public bool ApoInstalled { get; }

        public IReadOnlyList<RouteDiagnostic> Routes { get; }
        public IReadOnlyList<DependencyItem> Items { get; }

        /// <summary>全部就位才算可用。有一條路由指向不存在的裝置，那條路由上的 app 就調不動。</summary>
        public bool IsReady => Items.Where(i => i.IsRequired).All(i => i.IsReady);

        /// <summary>可以直接顯示給使用者的問題清單，沒問題時是空的。</summary>
        public IReadOnlyList<string> Problems =>
            Items.Where(i => i.IsRequired && !i.IsReady).Select(i => i.Detail).ToList();

        /// <summary>一行摘要，給設定頁的標題列用。</summary>
        public string Summary
        {
            get
            {
                int required = Items.Count(i => i.IsRequired && !i.IsReady);
                int optional = Items.Count(i => i.IsOptional && !i.IsReady);
                if (required == 0 && optional == 0) return "全部就緒";
                if (required == 0) return $"必要項目已就緒，另有 {optional} 個選用項目";
                return $"有 {required} 個必要項目尚未完成";
            }
        }
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
        /// 體檢。snapshot 的裝置由 Windows 層列舉後傳進來。
        /// </summary>
        public static DependencyReport Check(DependencySnapshot snapshot, RouteTable? routes)
        {
            var devices = CleanDevices(snapshot.RenderDevices);
            var routeDiagnostics = DiagnoseRoutes(devices, routes);

            bool voicemeeter = devices.Any(IsVoicemeeterMain) && devices.Any(IsVoicemeeterAux);
            bool cable = devices.Any(IsVbCable);
            bool routesReady = routeDiagnostics.All(r => r.Ok);

            var items = new List<DependencyItem>
            {
                new(DependencyKind.EqualizerApo, "Equalizer APO",
                    "找不到 Equalizer APO 的設定目錄。請確認它已安裝，或在設定裡指定實際位置。", true, snapshot.ApoInstalled),
                new(DependencyKind.Voicemeeter, "Voicemeeter Banana / Potato",
                    "需要同時提供 Voicemeeter Input 與 AUX Input，才能分開處理瀏覽器和語音聊天。", true, voicemeeter),
                new(DependencyKind.VbCable, "VB-CABLE",
                    "提供 CABLE Input，讓遊戲走獨立的虛擬音訊路徑。", true, cable),
                new(DependencyKind.GeminiApiKey, "Gemini API key",
                    "自然語言與語音指令需要 API key；儲存後請再測試一次連線。", true, snapshot.ApiKeyConfigured),
                new(DependencyKind.RouteConfiguration, "音訊路由設定",
                    routesReady
                        ? "appsettings.json 的每條路由都能配對目前的音訊裝置。"
                        : string.Join(Environment.NewLine, routeDiagnostics.Where(r => !r.Ok).Select(r =>
                            $"「{r.DisplayName}」找不到對應的音訊裝置（需要符合「{r.DevicePattern}」的裝置" +
                            (r.LikelyProvider == null ? "" : $"，通常由 {r.LikelyProvider} 提供") + "）。")),
                    true, routesReady),
                new(DependencyKind.MeldaVst, "Melda MFreeFXBundle",
                    "提供 MCompressor 與 MCharmVerb；缺少時 EQ 與 preamp 仍可使用。", false,
                    snapshot.CompressorInstalled && snapshot.ReverbInstalled),
                new(DependencyKind.ZhTwSpeech, "繁體中文（台灣）語音辨識",
                    "提供「心平氣和」喚醒詞；缺少時仍可使用文字與手動控制。", false,
                    snapshot.ZhTwSpeechInstalled),
            };

            return new DependencyReport(snapshot.ApoInstalled, routeDiagnostics, items);
        }

        private static List<string> CleanDevices(IEnumerable<string>? devices) =>
            (devices ?? Enumerable.Empty<string>()).Where(d => !string.IsNullOrWhiteSpace(d)).ToList();

        private static List<RouteDiagnostic> DiagnoseRoutes(IReadOnlyList<string> devices, RouteTable? routes) =>
            (routes?.Routes ?? Array.Empty<AudioRoute>())
                .Select(r => new RouteDiagnostic(r, devices.FirstOrDefault(d => DeviceMatches(r.DevicePattern, d))))
                .ToList();

        internal static bool IsVoicemeeterMain(string device) =>
            device.Contains("Voicemeeter Input", StringComparison.OrdinalIgnoreCase)
            && !device.Contains("Voicemeeter AUX Input", StringComparison.OrdinalIgnoreCase);

        internal static bool IsVoicemeeterAux(string device) =>
            device.Contains("Voicemeeter AUX Input", StringComparison.OrdinalIgnoreCase);

        internal static bool IsVbCable(string device) =>
            device.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase);
    }
}
