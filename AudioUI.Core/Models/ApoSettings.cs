namespace AudioUI
{
    /// <summary>Equalizer APO 後端的設定。</summary>
    public sealed class ApoSettings
    {
        /// <summary>APO 的 config 目錄。安裝在別的位置時覆寫這一項。</summary>
        public string ConfigDirectory { get; set; } = @"C:\Program Files\EqualizerAPO\config";

        /// <summary>本程式寫入的設定檔名。</summary>
        public string FragmentFileName { get; set; } = "audiozen.txt";

        /// <summary>MeldaProduction VST 外掛的安裝目錄。壓縮器與殘響的 DLL 從這裡往下找。</summary>
        public string VstDirectory { get; set; } = @"C:\Program Files\VstPlugins\MeldaProduction";
    }
}
