namespace AudioUI
{
    /// <summary>
    /// 麥克風輸入。抽成介面，流程測試才能餵一段固定的音訊，而不必真的對著機器講話。
    /// </summary>
    public interface ISpeechInput
    {
        /// <summary>錄一段指定長度的音訊寫到 <paramref name="filePath"/>，並回傳它的 base64。</summary>
        Task<string> RecordAsync(string filePath, int durationMs);
    }
}
