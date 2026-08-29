namespace AudioUI
{
    /// <summary>
    /// 把人話翻成音訊調整意圖的模型。回傳型別化的 <see cref="AudioIntent"/> 而不是原始回應字串，
    /// 讓「模型怎麼回話」的細節不外洩——換一家模型或改 prompt，呼叫端都不必跟著改。
    /// </summary>
    public interface ILlmClient
    {
        Task<AudioIntent?> InterpretAsync(string userText);

        /// <summary>把一段 base64 的 wav 轉成文字。認不出內容時回空字串。</summary>
        Task<string> TranscribeAsync(string base64Wav);
    }
}
