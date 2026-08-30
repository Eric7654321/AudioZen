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

                        if (config.CompJson != null && config.CompJson.Count > 0)
                        {
                            string base64String = MeldaEncoder.EncodeMeldaChunk(CompressorPresets.ChunkHeader, config.CompJson);
                            sw.WriteLine(VstPluginLine(Path.Combine("Dynamics", "MCompressor.dll"), base64String));
                        }

                        if (config.ReverbJson != null && config.ReverbJson.Count > 0)
                        {
                            string base64String = MeldaEncoder.EncodeMeldaChunk(ReverbPresets.ChunkHeader, config.ReverbJson);
                            sw.WriteLine(VstPluginLine(Path.Combine("Reverb", "MCharmVerb.dll"), base64String));
                        }

                        // 4. 加入一個空行分隔不同裝置的設定 (可選)
                        sw.WriteLine();
                    }
                }
            }

            return eqResponse.MessageForUser;
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
            }

            return found;
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
