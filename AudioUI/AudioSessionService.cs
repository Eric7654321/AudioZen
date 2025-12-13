using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AudioUI
{
    public class AudioAppModel
    {
        public string Name { get; set; } = "Unknown";
        public ImageSource? Icon { get; set; }
        public int SystemVolume { get; set; }
        public bool SystemMute { get; set; }
        public AppConfigData? Config { get; set; }
        public string? CurrentEffectInfo { get; set; }

        public string VolumeText => Config != null ? $"音量縮放: {(int)(Config.VolumeScale * 100)}% (AI)" : $"音量縮放: {SystemVolume}%";
        public string ToneText => Config != null ? $"音色調整: {Config.Effect}" : "音色調整: 無";
        public string OtherText => Config != null ? $"路由: {Config.TargetDevice}" : (SystemMute ? "其他: 靜音" : "其他: 正常");

        // ★★★ 修正：明確指定 System.Windows.Media，避免跟 WinForms/Drawing 打架 ★★★
        public System.Windows.Media.Brush BorderColor => Config != null
            ? System.Windows.Media.Brushes.CornflowerBlue
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 85, 85));

        public int ProcessId { get; set; }
    }

    public class AudioSessionService
    {
        private ConfigService _configService = new ConfigService();

        public ObservableCollection<AudioAppModel> GetAppsWithConfig()
        {
            var apps = new ObservableCollection<AudioAppModel>();
            var addedProcessIds = new HashSet<int>();

            // ==========================================
            // 步驟 1: 定義應用程式與裝置關鍵字的對照表 (路由表)
            // ==========================================
            var appToDeviceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "chrome.exe", "Voicemeeter Input" },
                { "msedge.exe", "CABLE Input" },
                { "eldenring.exe", "CABLE Input" },
                { "VALORANT-Win64-Shipping.exe", "CABLE Input" },
                { "discord.exe", "Voicemeeter AUX Input" }
            };

            // ==========================================
            // 步驟 2: 讀取 Config 並建立 [裝置關鍵字 -> 效果摘要] 的對照表
            // ==========================================
            // 我們只關心路由表中用到的那些裝置名稱
            var deviceKeywords = appToDeviceMap.Values.Distinct().ToList();

            Dictionary<string, string> deviceEffectMap = new Dictionary<string, string>();
            try
            {
                string configPath = _configService.GetConfigPath();
                if (File.Exists(configPath))
                {
                    string rawConfig = File.ReadAllText(configPath);
                    // 呼叫輔助方法解析設定檔
                    deviceEffectMap = ParseConfigByKeywords(rawConfig, deviceKeywords);
                }
            }
            catch { }

            try
            {
                var deviceEnumerator = new MMDeviceEnumerator();
                var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                foreach (var device in devices)
                {
                    try
                    {
                        var sessions = device.AudioSessionManager.Sessions;

                        for (int i = 0; i < sessions.Count; i++)
                        {
                            var session = sessions[i];
                            if (session.GetProcessID == 0 || session.State == AudioSessionState.AudioSessionStateExpired) continue;

                            int pid = (int)session.GetProcessID;
                            if (addedProcessIds.Contains(pid)) continue;

                            try
                            {
                                var process = Process.GetProcessById(pid);

                                // 取得 Process 名稱 (補上 .exe 以符合您的表格格式)
                                string processName = process.ProcessName;
                                string fullProcessName = processName + ".exe";

                                // ==========================================
                                // 步驟 3: 核心比對邏輯
                                // ==========================================
                                string finalSummary = "無";

                                // 3.1 查表：這個 App 是否在我們定義的路由表中？
                                if (appToDeviceMap.TryGetValue(fullProcessName, out string targetDeviceKeyword))
                                {
                                    // 3.2 查設定：如果 Config 檔有針對該裝置的設定
                                    if (deviceEffectMap.ContainsKey(targetDeviceKeyword))
                                    {
                                        finalSummary = deviceEffectMap[targetDeviceKeyword];
                                    }
                                }
                                else
                                {
                                    // (選項) 如果不在表格內，也可以嘗試用 processName (不含.exe) 再查一次
                                    if (appToDeviceMap.TryGetValue(processName, out string targetDeviceKeyword2))
                                    {
                                        if (deviceEffectMap.ContainsKey(targetDeviceKeyword2))
                                        {
                                            finalSummary = deviceEffectMap[targetDeviceKeyword2];
                                        }
                                    }
                                }

                                var app = new AudioAppModel
                                {
                                    Name = !string.IsNullOrEmpty(process.MainWindowTitle) ? process.MainWindowTitle : process.ProcessName,
                                    SystemVolume = (int)(session.SimpleAudioVolume.Volume * 100),
                                    SystemMute = session.SimpleAudioVolume.Mute,
                                    Icon = GetIcon(process.MainModule?.FileName),
                                    ProcessId = pid,

                                    // ★ 綁定摘要結果
                                    CurrentEffectInfo = finalSummary
                                };

                                if (!string.IsNullOrEmpty(app.Name))
                                {
                                    apps.Add(app);
                                    addedProcessIds.Add(pid);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("NAudio Error: " + ex.Message);
            }

            return apps;
        }

        /// <summary>
        /// 解析 Config 文字檔，根據提供的關鍵字列表提取摘要
        /// 回傳: [關鍵字] -> [摘要] (例如: "CABLE Input" -> "MCompressor + EQ")
        /// </summary>
        private Dictionary<string, string> ParseConfigByKeywords(string rawConfig, List<string> keywords)
        {
            var resultMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 根據 Device: 切割區塊
            var parts = Regex.Split(rawConfig, @"(?=^Device:)", RegexOptions.Multiline)
                             .Where(s => !string.IsNullOrWhiteSpace(s));

            foreach (var part in parts)
            {
                // 取得該區段的 Device 行
                var lineMatch = Regex.Match(part, @"^Device:\s*(.+)$", RegexOptions.Multiline);
                if (lineMatch.Success)
                {
                    string configDeviceLine = lineMatch.Groups[1].Value;

                    // 檢查這一行是否包含我們要找的任何一個關鍵字
                    // 例如 configLine 是 "Voicemeeter Input VB-Audio..."
                    // 我們要找 "Voicemeeter Input"
                    var matchedKeyword = keywords.FirstOrDefault(k =>
                        configDeviceLine.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (matchedKeyword != null)
                    {
                        // 解析效果摘要 (使用之前寫好的 GetEffectSummary)
                        string summary = GetEffect(part);

                        // 存入字典
                        if (!resultMap.ContainsKey(matchedKeyword))
                        {
                            resultMap[matchedKeyword] = summary;
                        }
                    }
                }
            }

            return resultMap;
        }

        private ImageSource? GetIcon(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                // ★★★ 修正：這裡明確呼叫 System.Drawing.Icon ★★★
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    return icon == null ? null : Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch { return null; }
        }

        private string GetEffect(string configContent)
        {
            if (string.IsNullOrWhiteSpace(configContent) || configContent == "無")
                return "無效果";

            var components = new List<string>();

            // 1. 抓取所有 VST 插件名稱
            // Regex 解釋: 尋找路徑結尾的檔名，忽略大小寫
            var vstMatches = Regex.Matches(configContent, @"\\([^\\]+?)\.dll", RegexOptions.IgnoreCase);
            foreach (Match match in vstMatches)
            {
                // 取得檔名 (例如 MCompressor)
                components.Add(match.Groups[1].Value);
            }

            // 2. 檢查 EQ (簡單判定: 包含 GraphicEQ 且字串長度夠長代表有設定)
            if (configContent.Contains("GraphicEQ:") && configContent.Length > 50)
            {
                components.Add("EQ");
            }

            // 3. 檢查 Preamp (增益)
            var preampMatch = Regex.Match(configContent, @"Preamp:\s*([-\d\.]+\s*dB)");
            if (preampMatch.Success)
            {
                string dbValue = preampMatch.Groups[1].Value;
                // 如果不是 0 dB 才顯示，避免資訊過多
                if (!dbValue.StartsWith("0.0") && !dbValue.StartsWith("0 dB"))
                {
                    // 為了精簡，可以加個括號放在最後，例如 (-6 dB)
                    components.Add($"({dbValue})");
                }
            }

            // 4. 組合字串
            if (components.Count == 0) return "自訂設定";

            // 使用 " + " 連接所有元件
            return string.Join(" + ", components);
        }
    }
}