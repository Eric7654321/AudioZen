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
        // 支援多個檔名
        public ObservableCollection<FileCreateData> FileDatas { get; set; } = new ObservableCollection<FileCreateData>();
    }

    public class ChatManager
    {
        private string _filePath = Path.Combine(".", "config", "file_Mapping.json"); // constant
        public List<FileMapItem> MapList { get; private set; }

        public ChatManager()
        {
            MapList = new List<FileMapItem>();
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

        // 建立新的Chat
        public void CreateChat(string id, string ChatName)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);

            if (item == null)
            {
                item = new FileMapItem { Id = id };
                MapList.Add(item);
            }
            MapList.Last().ChatName = ChatName;
        }


        // 功能：PushFront (加入到最上面)
        // 邏輯：如果 ID 不存在則建立，存在則將檔名插入到 Index 0
        public void PushFront(string id, FileCreateData fileData)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);

            if (item == null)
            {
                item = new FileMapItem { Id = id };
                MapList.Add(item);
            }

            // 核心邏輯：插入到最前面 (最新排到最舊)
            // 這裡可以選擇是否允許重複檔名，目前設定為允許
            item.FileDatas.Insert(0, fileData);
        }

        // 功能：PopFront (取出並移除最上面的)
        // 邏輯：移除 Index 0 的項目並回傳，如果清空了則移除 ID 並回傳空字串
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

        // 查詢：取得目前所有堆疊內容
        public FileCreateData GetFront(string id)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);
            if (item == null) return null;
            if (item.FileDatas == null || item.FileDatas.Count == 0) return null;
            return item.FileDatas[0];
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
    }

}