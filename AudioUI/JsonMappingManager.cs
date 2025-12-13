using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace AudioUI
{
    public class FileCreateData
    {
        public string FileName { get; set; }
        public string UserInput { get; set; }
        public string AiResponse { get; set; }
    }

    public class FileMapItem
    {
        public string Id { get; set; }
        public string ChatName { get; set; }
        public string RecordPath { get; set; }
        // 支援多個檔名
        public ObservableCollection<FileCreateData> FileDatas { get; set; } = new ObservableCollection<FileCreateData>();
    }
    public class ChatSessionInfo
    {
        public string Id { get; set; }
        public string DisplayName { get; set; } 
    }

    public class ChatManager
    {
        private string _filePath = Path.Combine(".", "config", "file_Mapping.json"); // constant
        public List<FileMapItem> MapList { get; private set; }

        public ChatManager()
        {
            MapList = new List<FileMapItem>();
        }

        // 取得聊天紀錄的側邊欄
        public List<ChatSessionInfo> GetChatList()
        {
            var historyList = new List<ChatSessionInfo>();

            // 遍歷所有聊天室
            foreach (var item in MapList)
            {
                // 1. 處理顯示名稱：如果是空的，就顯示 "New Chat" 或 "未命名對話"
                string nameToShow = string.IsNullOrWhiteSpace(item.ChatName)
                                    ? "New Chat"
                                    : item.ChatName;

                // 2. (選用) 取得最後一則訊息當作預覽
                // 因為你的 PushFront 邏輯，FileDatas[0] 是最新的訊息
                if (item.FileDatas != null && item.FileDatas.Count > 0)
                {
                    var latestData = item.FileDatas[0];
                    // 優先顯示 User 的輸入，若無則顯示 AI 回應
                    string rawMsg = !string.IsNullOrEmpty(latestData.UserInput)
                                    ? latestData.UserInput
                                    : latestData.AiResponse;
                }

                if (item.Id != "-1")
                {
                    // 3. 加入列表
                    historyList.Add(new ChatSessionInfo
                    {
                        Id = item.Id,
                        DisplayName = nameToShow
                    });
                }
            }
            return historyList;
        }

        // 取得歷史紀錄
        public List<FileCreateData> GetHistory(string id, int limit = 10)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);
            if (item == null) return new List<FileCreateData>();

            // item.FileDatas 目前是 [最新, 次新, ..., 最舊]
            // 我們先取前 limit 個 (即最近的 N 個)，然後反轉順序變成 [最舊 ... 最新]
            var history = item.FileDatas.Take(limit).Reverse().ToList();

            return history;
        }

        public string GetNextId()
        {
            // 1. 建立一個 HashSet 來儲存目前已存在且能轉為整數的 ID
            // 使用 HashSet 是為了讓後面的查詢 (Contains) 速度最快 (O(1))
            var existingIds = new HashSet<int>();

            foreach (var item in MapList)
            {
                // 嘗試將 string Id 轉為 int
                // 使用 int.TryParse 是為了避免如果有 Id 是 "uuid-xxx" 這種非數字格式時程式崩潰
                // 如果轉換成功，idValue 會是該數字，並加入集合中
                if (int.TryParse(item.Id, out int idValue))
                {
                    existingIds.Add(idValue);
                }
            }

            // 2. 從 0 開始往上數，找到第一個「不在」集合中的非負整數
            int candidate = 0;
            while (existingIds.Contains(candidate))
            {
                candidate++;
            }

            // 3. 回傳結果 (轉回 string 以符合 FileMapItem.Id 的型別)
            return candidate.ToString();
        }


        // 功能：PushFront (加入到最上面)
        // 邏輯：如果 ID 不存在則建立，存在則將檔名插入到 Index 0
        public void PushFront(string id, FileCreateData fileData, string chatName = "", string recordPath = "")
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);

            // 未建立 -> 新增一個
            if (item == null)
            {
                string title = chatName.Length > 20
                ? chatName.Substring(0, 20) + "..."
                : chatName;

                item = new FileMapItem { Id = id, ChatName = title, RecordPath = recordPath };
                MapList.Add(item);
            }

            // 核心邏輯：插入到最前面 (最新排到最舊)
            item.FileDatas.Insert(0, fileData);
        }

        // 功能：PopFront (取出並移除最上面的)
        // 邏輯：移除 Index 0 的項目並回傳
        public FileCreateData PopFront(string id)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);
            if (item != null && item.FileDatas.Count > 0)
            {
                FileCreateData poppedFileName = item.FileDatas[0];
                item.FileDatas.RemoveAt(0);

                return poppedFileName;
            }

            // 找不到 ID 或列表為空，回傳空字串以便呼叫端安全判斷
            return null;
        }

        // 查詢：取得目前第一個內容
        public FileCreateData GetFront(string id)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);
            if (item == null) return null;
            if (item.FileDatas == null || item.FileDatas.Count == 0) return null;
            return item.FileDatas[0];
        }

        // 將指定的 Chat 移動到 List 的第一個位置 (UI 列表通常綁定這個 List)
        public void MoveChatToTop(string id)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                // 如果已經是第一個就不用動
                if (MapList.IndexOf(item) == 0) return;

                MapList.Remove(item);
                MapList.Insert(0, item);

                // 觸發存檔確保順序被保存
                SaveToJson();
            }
        }

        public void RenameChat(string id, string newName)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                item.ChatName = newName;
                SaveToJson();
            }
        }

        // 存檔
        public void SaveToJson()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(MapList, options);
                File.WriteAllText(_filePath, jsonString);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"存檔失敗: {ex.Message}");
            }
        }

        // 讀檔
        public void LoadFromJson()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    System.Windows.MessageBox.Show("找不到存檔檔案。");
                    return;
                }

                string jsonString = File.ReadAllText(_filePath);
                var loadedData = JsonSerializer.Deserialize<List<FileMapItem>>(jsonString);

                if (loadedData != null)
                {
                    MapList = loadedData;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"讀檔失敗: {ex.Message}");
            }
        }

        // 整串刪除
        public bool DeleteChat(string id)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);
            if (item == null) return false;

            // 1. 從列表中移除
            MapList.Remove(item);

            // 2. 觸發存檔
            SaveToJson();
            return true;
        }

        // 單則訊息刪除
        public void DeleteMessage(string id, FileCreateData messageData)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);
            if (item == null || messageData == null) return;

            if (item.FileDatas.Contains(messageData))
            {

                // 1. 移除資料
                item.FileDatas.Remove(messageData);

                // 2. 存檔
                SaveToJson();
            }
        }
    }
}