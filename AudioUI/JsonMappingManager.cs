using System;
using System.Collections.Generic;
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
        // 支援多個檔名
        public List<FileCreateData> FileDatas { get; set; } = new List<FileCreateData>();
    }

    public class MappingManager
    {
        private string _filePath = Path.Combine(".", "config", "file_mapping.json"); // constant
        public List<FileMapItem> MapList { get; private set; }

        public MappingManager()
        {
            MapList = new List<FileMapItem>();
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
                // 若移除後已無檔名，移除整個項目（避免留下空的 Id）
                if (item.FileDatas.Count == 0)
                {
                    MapList.Remove(item);
                }

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
                MessageBox.Show($"存檔失敗: {ex.Message}");
            }
        }

        // 讀檔
        public void LoadFromJson()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    MessageBox.Show("找不到存檔檔案。");
                    return;
                }

                string jsonString = File.ReadAllText(_filePath);
                var loadedData = JsonSerializer.Deserialize<List<FileMapItem>>(jsonString);

                if (loadedData != null)
                {
                    MapList = loadedData;
                    MessageBox.Show("讀檔成功！");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"讀檔失敗: {ex.Message}");
            }
        }
    }

}