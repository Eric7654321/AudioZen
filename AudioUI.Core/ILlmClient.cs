namespace AudioUI
{
    /// <summary>
    /// 把人話翻成音訊調整意圖的模型。回傳型別化的 <see cref="AudioIntent"/> 而不是原始回應字串，
    /// 讓「模型怎麼回話」的細節不外洩——換一家模型或改 prompt，呼叫端都不必跟著改。
    /// </summary>
    public interface ILlmClient
    {
        /// <summary>
        /// <paramref name="memories"/> 是使用者偏好與模型記下的習慣，會當成背景脈絡附在指令前面。
        /// 兩個記憶開關都關掉時傳空的進來，此時 prompt 與沒有記憶功能時一模一樣。
        /// </summary>
        Task<AudioIntent?> InterpretAsync(string userText, IReadOnlyList<string>? memories = null);

        /// <summary>把一段 base64 的 wav 轉成文字。認不出內容時回空字串。</summary>
        Task<string> TranscribeAsync(string base64Wav);
    }
}
