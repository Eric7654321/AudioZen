namespace AudioUI
{
    /// <summary>
    /// 把目前在出聲的每個程式各錄一段，當成這次調整的樣本。
    ///
    /// 跟 <see cref="ISpeechInput"/> 是兩回事：那個錄的是麥克風（使用者說了什麼），
    /// 這個錄的是程式的輸出（使用者聽到的是什麼）。
    /// </summary>
    public interface ISampleRecorder
    {
        /// <summary>回傳這一次錄音的存放資料夾。</summary>
        Task<string> RecordActiveAppsAsync(string baseFolder, TimeSpan duration);
    }
}
