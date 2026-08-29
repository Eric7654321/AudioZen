namespace AudioUI
{
    /// <summary>Equalizer APO 後端的設定。</summary>
    public sealed class ApoSettings
    {
        /// <summary>APO 的 config 目錄。安裝在別的位置時覆寫這一項。</summary>
        public string ConfigDirectory { get; set; } = @"C:\Program Files\EqualizerAPO\config";

        /// <summary>本程式寫入的設定檔名。</summary>
        public string FragmentFileName { get; set; } = "audiozen.txt";
    }
}
