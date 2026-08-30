namespace AudioUI
{
    /// <summary>
    /// API key 的儲存。
    ///
    /// 抽成介面是為了讓 Core 不必知道它被加密成什麼樣子——實作綁在 Windows 的 DPAPI 上，
    /// 而「有沒有 key、要不要換一把」這件事本身跟平台無關。
    /// </summary>
    public interface IApiKeyStore
    {
        /// <summary>讀出 key；沒有、或解不開時回 <c>null</c>。</summary>
        string? Read();

        /// <summary>寫入 key。傳空字串或 <c>null</c> 等於清除。失敗時丟例外——存 key 失敗不能無聲。</summary>
        void Save(string? apiKey);

        bool HasKey { get; }
    }
}
