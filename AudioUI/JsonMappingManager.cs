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
        public string FileName { get; set; }
    }

    // 管理類別：負責邏輯處理
    public class MappingManager
    {
        private string _filePath = "file_mapping.json"; // 預設存檔路徑

        // 這是我們在記憶體中的資料清單
        public List<FileMapItem> MapList { get; private set; }

        public MappingManager()
        {
            MapList = new List<FileMapItem>();
        }

        // 新增資料 (防止 ID 重複)
        public bool AddItem(string id, string fileName)
        {
            if (MapList.Any(x => x.Id == id))
            {
                return false; // ID 已存在
            }

            MapList.Add(new FileMapItem { Id = id, FileName = fileName });
            return true;
        }

        public bool AddItemForce(string id, string fileName)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                item.FileName = fileName; // 修改
            }
            else
            {
                MapList.Add(new FileMapItem { Id = id, FileName = fileName }); // 新增
            }
            return true;
        }

        public bool ModifyItem(string id, string newFileName)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                item.FileName = newFileName;
                return true;
            }
            return false; // 找不到該 ID
        }
        
        // 存檔 (Serialize to JSON)
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

        // 根據 ID 查詢檔名
        public string GetFileNameById(string id)
        {
            var item = MapList.FirstOrDefault(x => x.Id == id);
            return item != null ? item.FileName : null;
        }
    }
}
