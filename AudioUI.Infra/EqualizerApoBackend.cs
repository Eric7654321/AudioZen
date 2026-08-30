using System.Globalization;

namespace AudioUI
{
    /// <summary>
    /// 把設定交給 Equalizer APO。
    ///
    /// APO 沒有 API，它的介面就是 config 目錄——引擎監看整個目錄，任何檔案變動都會觸發重載。
    /// 所以「套用」＝把檔案放到對的位置。
    ///
    /// 放的是一份獨立的設定檔，再由 <c>config.txt</c> 用 <c>Include:</c> 引入，而不是覆蓋 config.txt 本身：
    /// 覆蓋會連使用者自己在 APO 裡調的東西一起洗掉，而那些設定不是本程式產生的，也就沒有還原的依據。
    /// </summary>
    public sealed class EqualizerApoBackend : IAudioBackend
    {
        private const string MainConfigFileName = "config.txt";

        private readonly string _configDirectory;
        private readonly string _fragmentFileName;
        private readonly RouteTable _routes;
        private readonly string _vstDirectory;

        public EqualizerApoBackend(ApoSettings? settings = null, RouteTable? routes = null)
        {
            settings ??= new ApoSettings();
            _routes = routes ?? RouteTable.Default();
            _vstDirectory = settings.VstDirectory;
            _configDirectory = settings.ConfigDirectory;
            _fragmentFileName = string.IsNullOrWhiteSpace(settings.FragmentFileName)
                ? new ApoSettings().FragmentFileName
                : settings.FragmentFileName;
        }

        public bool IsAvailable => Directory.Exists(_configDirectory);

        public string FragmentPath => Path.Combine(_configDirectory, _fragmentFileName);

        /// <summary>把意圖寫成 APO 設定檔。無法產出時回 null。</summary>
        public string? Write(AudioIntent? eqResponse, string outputPath)
        {
            if (eqResponse == null) return null;

            using (StreamWriter sw = new StreamWriter(outputPath, false)) // false 表示覆寫檔案
            {
                if (eqResponse.Configs != null)
                {
                    foreach (var config in eqResponse.Configs)
                    {
                        // 1. 處理 Device 行
                        string? devicePattern = _routes.ResolveDevicePattern(config.Target);
                        if (devicePattern != null)
                        {
                            sw.WriteLine($"Device: {devicePattern}");
                        }
                        else
                        {
                            // 認不得的目標寫成註解：APO 會忽略它，而檔案本身留下了為什麼這段沒生效。
                            sw.WriteLine($"# Unknown Target: {config.Target}");
                        }

                        // 2. 寫入 Preamp
                        sw.WriteLine($"Preamp: {config.PreampDb.ToString(CultureInfo.InvariantCulture)} dB");

                        // 3. 寫入 GraphicEQ
                        if (!string.IsNullOrEmpty(config.GraphicEqString))
                        {
                            sw.WriteLine($"GraphicEQ: {config.GraphicEqString}");
                        }

                        // 3.5 記下這兩個效果是哪個 preset 產生的。APO 會忽略 # 開頭的行，
                        //     而少了這一行，下次讀回來只看得到一串 base64——認不回是哪一個，
                        //     面板就只能顯示「無」，然後按一次套用把它清掉。
                        string? marker = PresetMarker(config);
                        if (marker != null) sw.WriteLine(marker);

                        string? compChunk = config.CompJson is { Count: > 0 }
                            ? MeldaEncoder.EncodeMeldaChunk(CompressorPresets.ChunkHeader, config.CompJson)
                            : config.CompChunk;
                        if (!string.IsNullOrWhiteSpace(compChunk))
                        {
                            sw.WriteLine(VstPluginLine(Path.Combine("Dynamics", "MCompressor.dll"), compChunk));
                        }

                        string? reverbChunk = config.ReverbJson is { Count: > 0 }
                            ? MeldaEncoder.EncodeMeldaChunk(ReverbPresets.ChunkHeader, config.ReverbJson)
                            : config.ReverbChunk;
                        if (!string.IsNullOrWhiteSpace(reverbChunk))
                        {
                            sw.WriteLine(VstPluginLine(Path.Combine("Reverb", "MCharmVerb.dll"), reverbChunk));
                        }

                        // 4. 加入一個空行分隔不同裝置的設定 (可選)
                        sw.WriteLine();
                    }
                }
            }

            return eqResponse.MessageForUser;
        }

        /// <summary>本程式自己看的註解行，格式 <c># AudioZen: comp=medium; reverb=off</c>。</summary>
        internal const string MarkerPrefix = "# AudioZen:";

        /// <summary>兩個效果都不知道是哪個 preset（模型自由生成的那條路）時回 null，不寫沒有內容的行。</summary>
        private static string? PresetMarker(AudioTargetConfig config)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(config.CompPresetId)) parts.Add($"comp={config.CompPresetId}");
            if (!string.IsNullOrWhiteSpace(config.ReverbPresetId)) parts.Add($"reverb={config.ReverbPresetId}");
            return parts.Count == 0 ? null : $"{MarkerPrefix} {string.Join("; ", parts)}";
        }

        /// <summary>APO 的 VSTPlugin 指令要絕對路徑，DLL 位置則隨 Melda 的安裝目錄走。</summary>
        private string VstPluginLine(string relativeDll, string chunkData) =>
            $"VSTPlugin: Library \"{Path.Combine(_vstDirectory, relativeDll)}\" ChunkData \"{chunkData}\"";

        public void Apply(string configFilePath)
        {
            if (string.IsNullOrWhiteSpace(configFilePath) || !File.Exists(configFilePath))
                throw new FileNotFoundException($"找不到要套用的設定檔：{configFilePath}");

            if (!IsAvailable)
                throw new DirectoryNotFoundException(
                    $"找不到 Equalizer APO 的設定目錄：{_configDirectory}。" +
                    "請確認 APO 已安裝，或在 appsettings.json 的 apo.configDirectory 指定實際位置。");

            File.Copy(configFilePath, FragmentPath, overwrite: true);
            EnsureIncluded();
        }

        /// <summary>
        /// 從已套用的 fragment 裡讀回某個目標的設定。
        ///
        /// 只還原得了 <c>Preamp</c> 與 <c>GraphicEQ</c>：壓縮與殘響在檔案裡是 Melda 的 base64 chunk，
        /// 認得出「有沒有」卻認不回是哪一個 preset，所以那兩欄留 null 而不是亂猜一個。
        /// </summary>
        public AudioTargetConfig? ReadCurrent(string? targetId)
        {
            string? wanted = _routes.ResolveDevicePattern(targetId);
            if (wanted == null || !File.Exists(FragmentPath)) return null;

            AudioTargetConfig? found = null;
            bool inSection = false;

            foreach (string raw in File.ReadAllLines(FragmentPath))
            {
                string line = raw.Trim();

                if (line.StartsWith("Device:", StringComparison.OrdinalIgnoreCase))
                {
                    // 換到下一個 Device 區段就停：同一個目標只會被寫一次。
                    if (inSection) break;

                    inSection = string.Equals(line[7..].Trim(), wanted, StringComparison.OrdinalIgnoreCase);
                    if (inSection) found = new AudioTargetConfig { Target = targetId ?? RouteTable.GlobalTargetId };
                    continue;
                }

                if (!inSection || found == null) continue;

                if (line.StartsWith("Preamp:", StringComparison.OrdinalIgnoreCase))
                {
                    string value = line[7..].Replace("dB", "", StringComparison.OrdinalIgnoreCase).Trim();
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double db))
                        found.PreampDb = db;
                }
                else if (line.StartsWith("GraphicEQ:", StringComparison.OrdinalIgnoreCase))
                {
                    found.GraphicEqString = line[10..].Trim();
                }
                else if (line.StartsWith(MarkerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    ReadMarker(line[MarkerPrefix.Length..], found);
                }
                else if (line.StartsWith("VSTPlugin:", StringComparison.OrdinalIgnoreCase))
                {
                    string? chunk = ChunkDataOf(line);
                    if (chunk == null) continue;

                    // 認的是 DLL 檔名而不是路徑：Melda 裝在哪由設定決定，可能跟寫的時候不一樣。
                    if (line.Contains("MCompressor.dll", StringComparison.OrdinalIgnoreCase)) found.CompChunk = chunk;
                    else if (line.Contains("MCharmVerb.dll", StringComparison.OrdinalIgnoreCase)) found.ReverbChunk = chunk;
                }
            }

            return found;
        }

        /// <summary>解析 <c>comp=medium; reverb=hall</c>。認不得的欄位跳過，不讓一個錯字毀掉整行。</summary>
        private static void ReadMarker(string body, AudioTargetConfig config)
        {
            foreach (string part in body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;

                string key = part[..eq].Trim();
                string value = part[(eq + 1)..].Trim();
                if (value.Length == 0) continue;

                if (string.Equals(key, "comp", StringComparison.OrdinalIgnoreCase)) config.CompPresetId = value;
                else if (string.Equals(key, "reverb", StringComparison.OrdinalIgnoreCase)) config.ReverbPresetId = value;
            }
        }

        /// <summary>抓 VSTPlugin 行裡 <c>ChunkData "..."</c> 的內容。</summary>
        private static string? ChunkDataOf(string line)
        {
            int at = line.IndexOf("ChunkData", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;

            int open = line.IndexOf('"', at);
            if (open < 0) return null;

            int close = line.IndexOf('"', open + 1);
            return close < 0 ? null : line[(open + 1)..close];
        }

        /// <summary>
        /// 確保 config.txt 有引入本程式的設定檔。只在缺少時才寫，所以使用者原本的內容不會被動到，
        /// 之後每次套用也不必再碰 config.txt。
        /// </summary>
        private void EnsureIncluded()
        {
            string mainConfig = Path.Combine(_configDirectory, MainConfigFileName);
            string includeLine = $"Include: {_fragmentFileName}";

            string[] lines = File.Exists(mainConfig) ? File.ReadAllLines(mainConfig) : Array.Empty<string>();
            if (lines.Any(l => string.Equals(l.Trim(), includeLine, StringComparison.OrdinalIgnoreCase)))
                return;

            var updated = lines.ToList();
            if (updated.Count > 0 && !string.IsNullOrWhiteSpace(updated[^1]))
                updated.Add(string.Empty);
            updated.Add(includeLine);
            File.WriteAllLines(mainConfig, updated);
        }
    }
}
