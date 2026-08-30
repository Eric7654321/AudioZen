namespace AudioUI
{
    /// <summary>
    /// 把「現在哪些程式在出聲、各自套了什麼」列給使用者看。
    ///
    /// 跟 <see cref="INotifier"/> 分開是因為這件事要讀系統的音訊 session 與程式圖示，
    /// 而圖示在這個專案裡是 WPF 型別。流程層只需要說「現在把清單秀出來」，
    /// 不必看到那些型別——這正是它搬得進 Core 的原因。
    /// </summary>
    public interface IAppStateNotifier
    {
        void ShowCurrentApps();
    }
}
