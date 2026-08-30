namespace AudioUI
{
    /// <summary>
    /// 讓一份音訊設定真正生效的後端。
    ///
    /// 抽成介面是為了讓「決定要套什麼」和「怎麼套」可以分開換：目前唯一的實作把設定交給
    /// Equalizer APO，日後若改成在行程內自己做 DSP，換的是這裡的實作而不是呼叫端。
    /// </summary>
    public interface IAudioBackend
    {
        /// <summary>後端所需的東西是否就位（例如 APO 有沒有安裝）。</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// 把意圖寫成這個後端看得懂的設定檔。回傳要給使用者看的訊息；
        /// 意圖不成形而寫不出東西時回 <c>null</c>，呼叫端據此決定是否重試。
        /// </summary>
        string? Write(AudioIntent? intent, string outputPath);

        /// <summary>套用一份已生成的設定檔。失敗時丟例外，訊息要能讓使用者知道下一步做什麼。</summary>
        void Apply(string configFilePath);

        /// <summary>
        /// 讀出某個目標目前生效的設定；沒套用過或讀不到時回 <c>null</c>。
        ///
        /// 手調面板打開時要顯示現況而不是一排零，而「現況」的唯一事實來源是後端真正在套的東西——
        /// 語音改的與手調改的都在那裡，程式重開也還在。
        /// </summary>
        AudioTargetConfig? ReadCurrent(string? targetId);
    }
}
