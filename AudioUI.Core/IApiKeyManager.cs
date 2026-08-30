namespace AudioUI
{
    /// <summary>
    /// API key 的存取與現況。設定頁那條路徑要碰 DPAPI 與磁碟，所以隔一層介面。
    /// </summary>
    public interface IApiKeyManager
    {
        /// <summary>存下 key 並讓這次執行立刻改用它。傳空值代表清除。</summary>
        void Save(string? apiKey);

        /// <summary>目前這把 key 的樣子，只露尾四碼。沒有 key 時是一句人看得懂的話。</summary>
        string Masked { get; }

        /// <summary>有沒有可用的 key。送出前想先擋掉可以看這個，不必接例外。</summary>
        bool IsConfigured { get; }
    }
}
