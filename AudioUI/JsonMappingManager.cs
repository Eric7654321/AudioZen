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
        // ★★★ 修正：使用絕對路徑，確保讀寫同一個檔案 ★★★
        private string _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "file_mapping.json");

        public List<FileMapItem> MapList { get; private set; }

        public ChatManager()
        {
            MapList = new List<FileMapItem>();
        }

        public List<ChatSessionInfo> GetChatList()
        {
            var historyList = new List<ChatSessionInfo>();
            foreach (var item in MapList)
            {
                // 如果 ChatName 是空的，嘗試用第一則訊息的 UserInput 當標題
                string nameToShow = item.ChatName;
                if (string.IsNullOrWhiteSpace(nameToShow) && item.FileDatas.Count > 0)
                {
                    nameToShow = item.FileDatas[0].UserInput; // 拿最新的對話當標題
                }
                if (string.IsNullOrWhiteSpace(nameToShow)) nameToShow = "New Chat";

                if (item.Id != "-1")
                {
                    historyList.Add(new ChatSessionInfo
                    {
                        Id = item.Id,
                        DisplayName = nameToShow
                    });
                }
            }
            return historyList;
        }

        public List<FileCreateData> GetHistory(string id, int limit = 20)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);
            if (item == null) return new List<FileCreateData>();
            // 取前 N 筆並反轉 (讓舊的在上面，新的在下面)
            return item.FileDatas.Take(limit).Reverse().ToList();
        }

        public string GetNextId()
        {
            var existingIds = new HashSet<int>();
            foreach (var item in MapList)
            {
                if (int.TryParse(item.Id, out int idValue)) existingIds.Add(idValue);
            }
            int candidate = 0;
            while (existingIds.Contains(candidate)) candidate++;
            return candidate.ToString();
        }

        public void PushFront(string id, FileCreateData fileData, string chatName = "", string recordPath = "")
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);

            if (item == null)
            {
                // 如果是新對話，標題優先使用傳入的 chatName，如果沒有，就用 UserInput
                string title = !string.IsNullOrEmpty(chatName) ? chatName :
                               (!string.IsNullOrEmpty(fileData.UserInput) ? fileData.UserInput : "New Chat");

                // 截斷過長的標題
                if (title.Length > 20) title = title.Substring(0, 20) + "...";

                item = new FileMapItem { Id = id, ChatName = title, RecordPath = recordPath };
                MapList.Add(item);
            }
            else
            {
                // 如果已經存在，但 RecordPath 是空的，補上去
                if (string.IsNullOrEmpty(item.RecordPath) && !string.IsNullOrEmpty(recordPath))
                {
                    item.RecordPath = recordPath;
                }
            }

            item.FileDatas.Insert(0, fileData);
        }

        public FileCreateData PopFront(string id)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);
            if (item != null && item.FileDatas.Count > 0)
            {
                FileCreateData popped = item.FileDatas[0];
                item.FileDatas.RemoveAt(0);
                return popped;
            }
            return null;
        }

        public FileCreateData GetFront(string id)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);
            if (item == null || item.FileDatas.Count == 0) return null;
            return item.FileDatas[0];
        }

        public void SaveToJson()
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // 加入 Encoder 設定，避免中文變成 \uXXXX
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string jsonString = JsonSerializer.Serialize(MapList, options);
                File.WriteAllText(_filePath, jsonString);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"存檔失敗: {ex.Message}");
            }
        }

        public void LoadFromJson()
        {
            try
            {
                if (!File.Exists(_filePath)) return;

                string jsonString = File.ReadAllText(_filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var loadedData = JsonSerializer.Deserialize<List<FileMapItem>>(jsonString, options);

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

        // ... DeleteChat, DeleteMessage (保持不變) ...
    }
}