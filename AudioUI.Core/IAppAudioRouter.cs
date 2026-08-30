namespace AudioUI
{
    /// <summary>一次路由嘗試的結果。失敗時 <see cref="Message"/> 要能讓人知道下一步做什麼。</summary>
    public sealed class RouteResult
    {
        private RouteResult(bool ok, string message) { Ok = ok; Message = message; }

        public bool Ok { get; }

        public string Message { get; }

        public static RouteResult Success(string message) => new RouteResult(true, message);

        public static RouteResult Failure(string message) => new RouteResult(false, message);
    }

    /// <summary>
    /// 把某個程式的音訊輸出指到指定的裝置。
    ///
    /// 這是「使用者只開一個 app」的關鍵：Equalizer APO 只認得裝置，所以要對某個程式套設定，
    /// 得先讓那個程式的聲音真的走到那個裝置上。現在這件事要人手動去 Voicemeeter 與
    /// 系統音量設定裡點，收進程式之後使用者就不必知道底下有幾個東西。
    ///
    /// 抽成介面的另一個理由是它一定會失敗：用到的是沒有文件的系統介面，
    /// 不同 Windows 版本的介面識別碼不一樣。呼叫端要能在它不可用時照常運作。
    /// </summary>
    public interface IAppAudioRouter
    {
        /// <summary>這台機器上能不能用。取得系統介面失敗時是 false。</summary>
        bool IsSupported { get; }

        /// <summary>把某個程式指到某條路由對應的裝置。</summary>
        RouteResult Route(int processId, string? targetId);

        /// <summary>取消指定，讓那個程式回到系統預設裝置。</summary>
        RouteResult ResetToSystemDefault(int processId);

        /// <summary>查某個程式目前被指到哪個裝置；沒有指定時回 null。</summary>
        string? CurrentDeviceId(int processId);
    }
}
