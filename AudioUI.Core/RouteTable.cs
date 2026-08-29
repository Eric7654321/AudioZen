using System.Text;

namespace AudioUI
{
    /// <summary>
    /// 路由表。這份對應關係是設定，不是程式碼——換一台機器裝置名稱與 GUID 就不一樣，
    /// 散在多處的話改一個地方就得記得改其他幾個。所有需要「app ↔ 裝置」的地方都問這裡。
    /// </summary>
    public sealed class RouteTable
    {
        /// <summary>套用到所有裝置的特殊目標，APO 的 <c>Device:</c> 也認得這個字。</summary>
        public const string GlobalTargetId = "all";

        private readonly List<AudioRoute> _routes;

        public RouteTable(IEnumerable<AudioRoute> routes)
        {
            _routes = routes.Where(r => !string.IsNullOrWhiteSpace(r.Id)).ToList();
            foreach (var r in _routes)
            {
                if (string.IsNullOrWhiteSpace(r.MatchKeyword))
                    r.MatchKeyword = FirstTwoWords(r.DevicePattern);
            }
        }

        public IReadOnlyList<AudioRoute> Routes => _routes;

        public AudioRoute? ById(string? id) =>
            _routes.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>用程式檔名找路由。比對時忽略 .exe，因為呼叫端拿到的名稱兩種形式都有。</summary>
        public AudioRoute? ByProcess(string? processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return null;
            string bare = Strip(processName);
            return _routes.FirstOrDefault(r => r.Processes.Any(p => string.Equals(Strip(p), bare, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>用設定檔裡的一行 <c>Device: ...</c> 反查是哪條路由。</summary>
        public AudioRoute? ByDeviceLine(string? deviceLine)
        {
            if (string.IsNullOrWhiteSpace(deviceLine)) return null;
            return _routes.FirstOrDefault(r =>
                !string.IsNullOrWhiteSpace(r.MatchKeyword) &&
                deviceLine.Contains(r.MatchKeyword, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>把邏輯代號翻成 <c>Device:</c> 要寫的樣式；<c>all</c> 原樣通過。</summary>
        public string? ResolveDevicePattern(string? targetId)
        {
            if (string.Equals(targetId, GlobalTargetId, StringComparison.OrdinalIgnoreCase))
                return GlobalTargetId;
            return ById(targetId)?.DevicePattern;
        }

        /// <summary>prompt 裡的目標清單。由這裡生成，讓 prompt 與路由表不可能講不一樣的話。</summary>
        public string PromptTargetLines()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _routes.Count; i++)
                sb.Append($"{i + 2}. '{_routes[i].Id}': {string.Join(", ", _routes[i].Processes)} ");
            return sb.ToString();
        }

        /// <summary>prompt 裡描述 target 欄位允許值的那一段。</summary>
        public string PromptTargetUnion() =>
            string.Join("|", new[] { GlobalTargetId }.Concat(_routes.Select(r => r.Id)).Select(id => $"\"{id}\""));

        public int TargetCount => _routes.Count + 1;

        private static string Strip(string name) =>
            name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

        private static string FirstTwoWords(string s)
        {
            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? $"{parts[0]} {parts[1]}" : s;
        }

        /// <summary>這台機器沒有 appsettings.json 時的內建預設值。</summary>
        public static RouteTable Default() => new RouteTable(new[]
        {
            new AudioRoute
            {
                Id = "browser", DisplayName = "chrome",
                DevicePattern = "Voicemeeter Input VB-Audio Voicemeeter VAIO {7bac9b47-61e4-4f81-b81b-2ad6c8186abc}",
                Processes = { "chrome.exe" }
            },
            new AudioRoute
            {
                Id = "voice_chat", DisplayName = "discord",
                DevicePattern = "Voicemeeter AUX Input VB-Audio Voicemeeter VAIO {ba00bb3e-8c53-44ca-ab44-10c3715d3dbd}",
                Processes = { "discord.exe" }
            },
            new AudioRoute
            {
                Id = "game", DisplayName = "games",
                DevicePattern = "CABLE Input VB-Audio Virtual Cable {0a4eba8e-e0ec-457a-90de-e84ce08d5844}",
                Processes = { "msedge.exe", "eldenring.exe", "VALORANT-Win64-Shipping.exe" }
            },
        });
    }
}
