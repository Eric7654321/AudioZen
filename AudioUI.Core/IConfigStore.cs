namespace AudioUI
{
    /// <summary>
    /// 情境與調整紀錄的存放處。抽成介面之後，流程邏輯不必知道它們躺在哪個 JSON 檔，
    /// 測試也能換成記憶體實作而不碰使用者的存檔。
    /// </summary>
    public interface IConfigStore
    {
        IReadOnlyList<Situation> Situations { get; }

        Situation? ById(string? id);

        /// <summary>側邊欄要顯示的清單，不含暫存情境。</summary>
        IReadOnlyList<SituationSummary> Summaries();

        /// <summary>由舊到新的調整紀錄，最多 <paramref name="limit"/> 筆。</summary>
        IReadOnlyList<SituationEntry> History(string id, int limit = 20);

        /// <summary>沒被用掉的最小非負整數代號。</summary>
        string NextId();

        void PushFront(string id, SituationEntry entry, string displayName = "", string recordPath = "");

        SituationEntry? PopFront(string id);

        SituationEntry? Front(string id);

        void Load();

        void Save();
    }
}
