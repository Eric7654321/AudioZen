using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace AudioUI
{
    public class ConfigService
    {
        private readonly string _configDir;
        private readonly string _configPath;

        public ConfigService()
        {
            _configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            try
            {
                if (!Directory.Exists(_configDir)) Directory.CreateDirectory(_configDir);
            }
            catch { }

            _configPath = Path.Combine(_configDir, "config.txt");
        }

        // ★★★ 核心修正：改為解析 Equalizer APO 的文字格式 ★★★
        public List<AppConfigData> LoadConfig()
        {
            var list = new List<AppConfigData>();

            if (!File.Exists(_configPath)) return list;

            try
            {
                string[] lines = File.ReadAllLines(_configPath);
                AppConfigData? currentConfig = null;

                foreach (string line in lines)
                {
                    string tLine = line.Trim();
                    if (string.IsNullOrWhiteSpace(tLine) || tLine.StartsWith("#")) continue;

                    // 1. 偵測 Device (視為新區塊的開始)
                    // 格式: Device: Voicemeeter Input VB-Audio...
                    if (tLine.StartsWith("Device:", StringComparison.OrdinalIgnoreCase))
                    {
                        // 儲存上一個區塊
                        if (currentConfig != null) list.Add(currentConfig);

                        currentConfig = new AppConfigData();

                        // 解析裝置名稱
                        string deviceName = tLine.Substring(7).Trim();
                        currentConfig.TargetDevice = deviceName;

                        // 反查介面要顯示的 App 名稱
                        currentConfig.ProcessName = MapDeviceToProcessName(deviceName);
                    }
                    // 2. 解析 Preamp (音量)
                    // 格式: Preamp: -6 dB
                    else if (currentConfig != null && tLine.StartsWith("Preamp:", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = Regex.Match(tLine, @"Preamp:\s*([\d\.\-]+)\s*dB", RegexOptions.IgnoreCase);
                        if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double db))
                        {
                            // dB 轉 比例: 10^(dB/20)
                            currentConfig.VolumeScale = Math.Pow(10, db / 20.0);
                        }
                    }
                    // 3. 解析 GraphicEQ
                    else if (currentConfig != null && tLine.StartsWith("GraphicEQ:", StringComparison.OrdinalIgnoreCase))
                    {
                        currentConfig.Effect = AppendEffect(currentConfig.Effect, "EQ");
                    }
                    // 4. 解析 VSTPlugin (Compressor / Reverb)
                    else if (currentConfig != null && tLine.StartsWith("VSTPlugin:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (tLine.Contains("MCompressor", StringComparison.OrdinalIgnoreCase))
                            currentConfig.Effect = AppendEffect(currentConfig.Effect, "Compressor");

                        if (tLine.Contains("MCharmVerb", StringComparison.OrdinalIgnoreCase))
                            currentConfig.Effect = AppendEffect(currentConfig.Effect, "Reverb");
                    }
                }

                // 加入最後一個
                if (currentConfig != null) list.Add(currentConfig);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config Parse Error: {ex.Message}");
            }

            return list;
        }

        // 輔助方法：將效果字串串接 (e.g., "EQ, Reverb")
        private string AppendEffect(string current, string newEffect)
        {
            if (string.IsNullOrEmpty(current) || current == "無") return newEffect;
            if (current.Contains(newEffect)) return current; // 避免重複
            return $"{current} + {newEffect}";
        }

        // 把設定檔裡落落長的裝置名稱轉回介面要顯示的 App 名稱。
        private string MapDeviceToProcessName(string deviceName)
        {
            if (deviceName.Equals(RouteTable.GlobalTargetId, StringComparison.OrdinalIgnoreCase))
                return "System";

            return AppConfig.Routes.ByDeviceLine(deviceName)?.DisplayName ?? "Unknown";
        }

        // 供查詢完整路徑
        public string GetConfigPath() => _configPath;
    }
}