namespace AudioUI
{
    /// <summary>
    /// 一條路由：某幾個程式的聲音走哪個虛擬裝置。
    ///
    /// Windows 沒有 per-application 的音訊處理 API，Equalizer APO 只認裝置，
    /// 所以「哪個 app」必須先被翻譯成「哪個裝置」才有辦法套設定。這個型別就是那張翻譯表的一列。
    /// </summary>
    public sealed class AudioRoute
    {
        /// <summary>給 LLM 用的邏輯代號（browser / voice_chat / game）。
        /// 刻意不讓 LLM 直接吐裝置全名：那是六十幾個字元含 GUID 的字串，模型複述不可靠。</summary>
        public string Id { get; set; } = "";

        /// <summary>介面上顯示的名字。</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>寫進 APO 設定檔 <c>Device:</c> 後面的比對樣式。</summary>
        public string DevicePattern { get; set; } = "";

        /// <summary>從既有設定檔讀回來時，用來認出這一行屬於哪條路由的關鍵字。
        /// 通常是 <see cref="DevicePattern"/> 的前綴；留空則自動取前兩個字詞。</summary>
        public string MatchKeyword { get; set; } = "";

        /// <summary>走這條路由的程式檔名。</summary>
        public List<string> Processes { get; set; } = new List<string>();
    }
}
