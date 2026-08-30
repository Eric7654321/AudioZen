namespace AudioUI
{
    /// <summary>一個裝置區塊在設定檔裡的樣子。</summary>
    public class AppConfigData
    {
        public string ProcessName { get; set; } = "";
        public string TargetDevice { get; set; } = "";
        public double VolumeScale { get; set; } = 1.0;
        public string Effect { get; set; } = "無"; // 顯示文字，如 "EQ + Reverb"
    }
}
