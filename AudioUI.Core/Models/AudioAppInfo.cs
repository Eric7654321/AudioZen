namespace AudioUI
{
    /// <summary>
    /// 一個正在出聲的程式。卡片、通知、錄音挑目標，讀的都是這一份。
    ///
    /// 刻意不帶 <c>ImageSource</c> 與 <c>Brush</c>：那兩個是 WPF 型別，帶著它們，
    /// 這個型別就只能住在 UI 層——連帶回傳它的 <see cref="IAudioSessions"/> 與再上面的
    /// ViewModel 都一起被釘在那裡，整條相依鏈都測不了。圖示與框線是「怎麼畫」，
    /// 交給 UI 層從 <see cref="IconPath"/> 與 <see cref="HasConfig"/> 自己算。
    /// </summary>
    public sealed class AudioAppInfo
    {
        public string Name { get; set; } = "Unknown";

        /// <summary>程式執行檔的路徑；UI 層拿它去抽圖示。取不到就是 null。</summary>
        public string? IconPath { get; set; }

        public int SystemVolume { get; set; }
        public bool SystemMute { get; set; }
        public int ProcessId { get; set; }

        /// <summary>設定檔裡對應的區塊。沒有對應的路由就是 null。</summary>
        public AppConfigData? Config { get; set; }

        /// <summary>目前套在這個程式上的效果摘要，例如 "MCompressor + EQ"。通知列用。</summary>
        public string? CurrentEffectInfo { get; set; }

        /// <summary>有沒有被我們設定過。卡片的框線顏色看這個。</summary>
        public bool HasConfig => Config != null;

        public string VolumeText => Config != null
            ? $"音量縮放: {(int)(Config.VolumeScale * 100)}% (AI)"
            : $"音量縮放: {SystemVolume}%";

        public string ToneText => Config != null ? $"音色調整: {Config.Effect}" : "音色調整: 無";

        public string OtherText => Config != null
            ? $"路由: {Config.TargetDevice}"
            : (SystemMute ? "其他: 靜音" : "其他: 正常");
    }
}
