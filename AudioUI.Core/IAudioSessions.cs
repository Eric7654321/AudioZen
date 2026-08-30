namespace AudioUI
{
    /// <summary>
    /// 「現在有誰在出聲」以及「系統上有哪些輸出裝置」。
    /// 實作要列舉 WASAPI session 並開 process，那在測試機上要嘛不存在、要嘛每次跑都不一樣。
    /// </summary>
    public interface IAudioSessions
    {
        /// <summary>目前在播放音訊的程式。列舉不到就回空的。</summary>
        IReadOnlyList<AudioAppInfo> List();

        /// <summary>
        /// 目前的輸出裝置識別字串，格式對齊 APO 比對用的 <c>裝置名稱 連線名稱 GUID</c>——
        /// 名稱與 GUID 都在裡面，所以路由表的樣式可以直接比。
        /// </summary>
        IReadOnlyList<string> RenderDeviceIdentities();
    }
}
