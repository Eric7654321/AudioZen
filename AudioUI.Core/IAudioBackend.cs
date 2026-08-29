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
        /// 內容不成形時回 <c>"-1"</c>，呼叫端據此決定是否重試。
        /// </summary>
        string Write(AudioIntent? intent, string outputPath);

        /// <summary>套用一份已生成的設定檔。失敗時丟例外，訊息要能讓使用者知道下一步做什麼。</summary>
        void Apply(string configFilePath);
    }
}
