using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AudioUI
{
    // 定義設定檔的資料結構
    public class AppConfigData
    {
        public string ProcessName { get; set; } = "";
        public string TargetDevice { get; set; } = "";
        public double VolumeScale { get; set; } = 1.0;
        public string Effect { get; set; } = "無";
    }

    public class ConfigService
    {
        private readonly string _configDir;
        private readonly string _configPath;

        public ConfigService()
        {
            _configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            // 確保資料夾存在（第一次執行會建立）
            try
            {
                Directory.CreateDirectory(_configDir);
            }
            catch
            {
                // 保守忽略，呼叫端會處理 I/O 錯誤
            }

            _configPath = Path.Combine(_configDir, "config.txt");
        }

        // 回傳目前設定（若檔案不存在或解析失敗回傳空集合）
        public List<AppConfigData> LoadConfig()
        {
            try
            {
                if (!File.Exists(_configPath))
                    return new List<AppConfigData>();

                var json = File.ReadAllText(_configPath);
                var list = JsonSerializer.Deserialize<List<AppConfigData>>(json);
                return list ?? new List<AppConfigData>();
            }
            catch
            {
                return new List<AppConfigData>();
            }
        }

        // 儲存設定（會覆寫），保證資料夾存在
        public void SaveConfig(List<AppConfigData> configs)
        {
            try
            {
                Directory.CreateDirectory(_configDir);
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(configs, options);
                File.WriteAllText(_configPath, json);
            }
            catch
            {
                // 讓呼叫端決定如何處理錯誤（也可改為拋出例外）
            }
        }

        // 供其他程式查詢完整路徑（選用）
        public string GetConfigPath() => _configPath;
    }
}