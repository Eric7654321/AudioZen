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

        public EqualizerApoBackend(ApoSettings? settings = null)
        {
            settings ??= new ApoSettings();
            _configDirectory = settings.ConfigDirectory;
            _fragmentFileName = string.IsNullOrWhiteSpace(settings.FragmentFileName)
                ? new ApoSettings().FragmentFileName
                : settings.FragmentFileName;
        }

        public bool IsAvailable => Directory.Exists(_configDirectory);

        public string FragmentPath => Path.Combine(_configDirectory, _fragmentFileName);

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
