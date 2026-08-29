namespace AudioUI
{
    /// <summary>
    /// 對使用者說話的管道。抽成介面，是為了讓流程邏輯能在沒有桌面通知的環境下被測試——
    /// 也讓「通知長什麼樣」這件事只有一份實作，而不是每個需要通知的類別各寫一次。
    /// </summary>
    public interface INotifier
    {
        void Notify(string title, string message);

        /// <summary>問一個是非題並等使用者回答。逾時或關掉通知都算否。</summary>
        Task<bool> ConfirmAsync(string title, string message);
    }
}
