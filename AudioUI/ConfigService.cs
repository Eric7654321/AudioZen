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
        private string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");

        public List<AppConfigData> LoadConfig()
        {
            if (!File.Exists(_configPath)) return new List<AppConfigData>();

            try
            {
                string jsonString = File.ReadAllText(_configPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<List<AppConfigData>>(jsonString, options);
                return data ?? new List<AppConfigData>();
            }
            catch
            {
                return new List<AppConfigData>();
            }
        }
    }
}