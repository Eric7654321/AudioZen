using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace AudioUI
{
    // 資料模型：定義 ID 對應 檔名
    public class FileMapItem
    {
        public string Id { get; set; }
        public List<string> FileNames { get; set; } = new List<string>();
    }

    public class MappingManager
    {
        private string _filePath = "file_mapping.json"; // 預設存檔路徑

        // 這是記憶體中的資料清單
        public List<FileMapItem> MapList { get; private set; }

        public MappingManager()
        {
            MapList = new List<FileMapItem>();
        }

        // 新增資料 (防止 ID 重複)
        public void PushFront(string id, string fileName)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);

            if (item == null) // 沒有找到結果
            {
                item = new FileMapItem { Id = id };
                MapList.Add(item);
            }

            // 插入到最前面
            item.FileNames.Insert(0, fileName);
        }

        public string PopFront(string id)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);

            if (item != null && item.FileNames.Count > 0)
            {
                string poppedFileName = item.FileNames[0];
                item.FileNames.RemoveAt(0); // 移除最新的
                return poppedFileName;
            }

            return null; // 找不到 ID 或列表是空的
        }

        public string GetFileNameById(string id)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);
            return item != null ? item.FileNames[0] : "";
        }

        // 存檔
        public void SaveToJson(string filePath)
        {
            filePath = filePath ?? _filePath;
            try
            {
                // 設定 JSON 格式排列整齊 (Indented)
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(MapList, options);
                File.WriteAllText(_filePath, jsonString);
                MessageBox.Show($"存檔成功！路徑: {Path.GetFullPath(_filePath)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"存檔失敗: {ex.Message}");
            }
        }

        // 讀檔 (Deserialize from JSON)
        public void LoadFromJson(string filePath)
        {
            filePath = filePath ?? _filePath;
            try
            {
                if (!File.Exists(_filePath))
                {
                    MessageBox.Show("找不到存檔檔案。");
                    return;
                }

                string jsonString = File.ReadAllText(filePath);
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
