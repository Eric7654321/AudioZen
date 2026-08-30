namespace AudioUI
{
    /// <summary>
    /// 一個裝置區塊在設定檔裡的樣子。純資料，所以住在 Core——
    /// 解析它的 <c>ConfigService</c> 要碰磁碟，那個留在上層。
    /// </summary>
    public class AppConfigData
    {
        public string ProcessName { get; set; } = "";
        public string TargetDevice { get; set; } = "";
        public double VolumeScale { get; set; } = 1.0;
        public string Effect { get; set; } = "無"; // 顯示文字，如 "EQ + Reverb"
    }
}
