namespace AudioUI
{
    /// <summary>
    /// 拿一段原始錄音套上設定，產生試聽用的音檔。
    /// 實作要跑 NAudio 的取樣管線，所以流程層只看得到這個介面。
    /// </summary>
    public interface IAudioPreview
    {
        /// <summary>回傳產生的音檔路徑；產不出來就是 null。</summary>
        string? Generate(string inputWavPath, string configPath);
    }
}
