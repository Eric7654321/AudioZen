namespace AudioUI
{
    /// <summary>
    /// 語音回覆。抽成介面之後，測流程時不會有人真的對著喇叭講話，
    /// 也才能斷言「這個情況下應該對使用者說什麼」。
    /// </summary>
    public interface ITextToSpeech
    {
        Task SpeakAsync(string text);

        /// <summary>中斷目前正在播的內容。</summary>
        void Stop();
    }
}
