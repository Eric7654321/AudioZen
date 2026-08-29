using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AudioUI
{
    // 負責管理按鍵與 Config 的綁定關係 (存成 key_bindings.json)
    public class KeyMappingService
    {
        // 存檔路徑：跟其他 config 放一起
        private string _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "key_bindings.json");

        // 資料結構：Key = 按鍵ID (如 "btn01"), Value = ConfigID (如 "gaming_mode" 或 "cmd_mute")
        private Dictionary<string, string> _bindings = new Dictionary<string, string>();

        public KeyMappingService()
        {
            // 建構時不一定馬上讀取，通常由外部呼叫 Load()
        }

        // 讀取設定檔
        public void Load()
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    string json = File.ReadAllText(_filePath);
                    _bindings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch
                {
                    // 讀取失敗或是格式錯誤，就重置為空
                    _bindings = new Dictionary<string, string>();
                }
            }
        }

        // 儲存設定檔
        public void Save()
        {
            try
            {
                // 確保 config 資料夾存在
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // 寫入 JSON (縮排方便人類閱讀)
                string json = JsonSerializer.Serialize(_bindings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"儲存綁定失敗: {ex.Message}");
            }
        }

        // 設定綁定 (若已存在則覆蓋，若無則新增)
        public void SetBinding(string keyId, string configId)
        {
            if (_bindings.ContainsKey(keyId))
            {
                _bindings[keyId] = configId;
            }
            else
            {
                _bindings.Add(keyId, configId);
            }
            // 設定完通常順便存檔比較保險，但也可以由外部控制
        }

        // 移除單個綁定
        public void RemoveBinding(string keyId)
        {
            if (_bindings.ContainsKey(keyId))
            {
                _bindings.Remove(keyId);
                Save(); // 移除後立即存檔
            }
        }

        // 清空所有綁定
        public void ClearAll()
        {
            _bindings.Clear();
            Save(); // 清空後立即存檔
        }

        // 查詢某個按鍵綁定了什麼 (沒綁定回傳 null)
        public string? GetBoundConfigId(string keyId)
        {
            return _bindings.ContainsKey(keyId) ? _bindings[keyId] : null;
        }
    }
}