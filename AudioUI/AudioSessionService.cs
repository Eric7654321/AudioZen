using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media; // WPF 的媒體庫
using System.Windows.Media.Imaging;

namespace AudioUI
{
    // 1. 定義資料模型
    public class AudioAppModel
    {
        public string Name { get; set; } = "Unknown";
        public ImageSource? Icon { get; set; }

        // 原始系統數據
        public int SystemVolume { get; set; }
        public bool SystemMute { get; set; }

        // Config 數據
        public AppConfigData? Config { get; set; }

        // === UI 顯示邏輯 ===
        public string VolumeText
        {
            get
            {
                if (Config != null)
                    return $"音量縮放: {(int)(Config.VolumeScale * 100)}% (AI)";
                else
                    return $"音量縮放: {SystemVolume}%";
            }
        }

        public string ToneText => Config != null ? $"音色調整: {Config.Effect}" : "音色調整: 無";

        public string OtherText
        {
            get
            {
                if (Config != null)
                    return $"路由: {Config.TargetDevice}";
                else
                    return SystemMute ? "其他: 靜音" : "其他: 正常";
            }
        }

        // 這裡解決了你的報錯：明確指定使用 WPF 的 Brushes
        public Brush BorderColor => Config != null
            ? System.Windows.Media.Brushes.CornflowerBlue
            : new SolidColorBrush(Color.FromRgb(85, 85, 85));
    }

    // 2. 服務邏輯
    public class AudioSessionService
    {
        private ConfigService _configService = new ConfigService();

        public ObservableCollection<AudioAppModel> GetAppsWithConfig()
        {
            var apps = new ObservableCollection<AudioAppModel>();
            var configList = _configService.LoadConfig(); // 讀取設定檔

            try
            {
                var deviceEnumerator = new MMDeviceEnumerator();
                var device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessions = device.AudioSessionManager.Sessions;

                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (session.GetProcessID == 0 || session.State == AudioSessionState.AudioSessionStateExpired)
                        continue;

                    try
                    {
                        var process = Process.GetProcessById((int)session.GetProcessID);
                        string processName = process.ProcessName;

                        // 比對 Config
                        var matchedConfig = configList.FirstOrDefault(c =>
                            c.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase) ||
                            c.ProcessName.Equals(processName + ".exe", StringComparison.OrdinalIgnoreCase));

                        var app = new AudioAppModel
                        {
                            Name = !string.IsNullOrEmpty(process.MainWindowTitle) ? process.MainWindowTitle : process.ProcessName,
                            SystemVolume = (int)(session.SimpleAudioVolume.Volume * 100),
                            SystemMute = session.SimpleAudioVolume.Mute,
                            Icon = GetIcon(process.MainModule?.FileName),
                            Config = matchedConfig
                        };

                        if (!string.IsNullOrEmpty(app.Name))
                        {
                            apps.Add(app);
                        }
                    }
                    catch { /* 忽略系統權限不足的 Process */ }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("NAudio Error: " + ex.Message);
            }

            return apps;
        }

        // 抓取 Icon 的輔助方法
        private ImageSource? GetIcon(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                // 這裡明確使用 System.Drawing 來處理 Icon
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    return icon == null ? null : Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch { return null; }
        }
    }
}