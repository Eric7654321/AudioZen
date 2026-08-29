using System.Collections.ObjectModel;

namespace AudioUI
{
    /// <summary>
    /// 一次調整的紀錄：使用者說了什麼、模型回了什麼、產生的設定檔在哪。
    /// 屬性名稱就是 file_mapping.json 的欄位名，改動會讓既有檔案讀不回來。
    /// </summary>
    public class SituationEntry
    {
        public string FileName { get; set; }
        public string UserInput { get; set; }
        public string AiResponse { get; set; }
    }

    /// <summary>一個情境，以及它底下由新到舊的調整紀錄。</summary>
    public class Situation
    {
        public string Id { get; set; }
        public string ChatName { get; set; }
        public string RecordPath { get; set; }
        public ObservableCollection<SituationEntry> FileDatas { get; set; } = new ObservableCollection<SituationEntry>();
    }

    /// <summary>側邊欄要顯示的一列。</summary>
    public class SituationSummary
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
    }

    /// <summary>
    /// 有特殊意義的情境代號。這些字串同時是存檔裡的鍵，所以值本身不能改——
    /// 具名是為了讓讀 code 的人知道 "114514" 指的是靜音，而不是某個使用者情境。
    /// </summary>
    public static class SituationIds
    {
        /// <summary>語音指令的暫存情境，不會出現在側邊欄。</summary>
        public const string Transient = "-1";

        /// <summary>全域靜音。</summary>
        public const string Mute = "114514";
    }
}
