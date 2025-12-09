using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.Generic; // 用 List 比較好過濾重複
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AudioUI
{
    // ... (AudioAppModel 類別保持不變) ...
    public class AudioAppModel
    {
        public string Name { get; set; } = "Unknown";
        public ImageSource? Icon { get; set; }
        public int SystemVolume { get; set; }
        public bool SystemMute { get; set; }
        public AppConfigData? Config { get; set; }

        public string VolumeText => Config != null ? $"音量縮放: {(int)(Config.VolumeScale * 100)}% (AI)" : $"音量縮放: {SystemVolume}%";
        public string ToneText => Config != null ? $"音色調整: {Config.Effect}" : "音色調整: 無";
        public string OtherText => Config != null ? $"路由: {Config.TargetDevice}" : (SystemMute ? "其他: 靜音" : "其他: 正常");
        public Brush BorderColor => Config != null ? System.Windows.Media.Brushes.CornflowerBlue : new SolidColorBrush(Color.FromRgb(85, 85, 85));

        // 為了避免重複，我們需要一個唯一識別碼 (ProcessID)
        public int ProcessId { get; set; }
    }

    public class AudioSessionService
    {
        private ConfigService _configService = new ConfigService();

        public ObservableCollection<AudioAppModel> GetAppsWithConfig()
        {
            var apps = new ObservableCollection<AudioAppModel>();
            var configList = _configService.LoadConfig();

            // 用來紀錄已經加入的 Process ID，避免因為多個裝置導致同一個 App 出現兩次
            var addedProcessIds = new HashSet<int>();

            try
            {
                var deviceEnumerator = new MMDeviceEnumerator();

                // ★★★ 修改重點：列舉所有「Active」的音訊渲染裝置，而不只是 GetDefault ★★★
                var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                foreach (var device in devices)
                {
                    // 有些裝置可能無法存取 SessionManager，加 try-catch 避免崩潰
                    try
                    {
                        var sessions = device.AudioSessionManager.Sessions;

                        for (int i = 0; i < sessions.Count; i++)
                        {
                            var session = sessions[i];

                            // 忽略系統音效 (PID=0) 和過期 Session
                            if (session.GetProcessID == 0 || session.State == AudioSessionState.AudioSessionStateExpired)
                                continue;

                            int pid = (int)session.GetProcessID;

                            // 如果這個 App 已經加過了，就跳過 (避免 Chrome 同時在喇叭和耳機都有 Session 時重複顯示)
                            if (addedProcessIds.Contains(pid))
                                continue;

                            try
                            {
                                var process = Process.GetProcessById(pid);
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
                                    Config = matchedConfig,
                                    ProcessId = pid
                                };

                                if (!string.IsNullOrEmpty(app.Name))
                                {
                                    apps.Add(app);
                                    addedProcessIds.Add(pid); // 標記已加入
                                }
                            }
                            catch
                            {
                                // 忽略無法存取的 Process (權限問題)
                            }
                        }
                    }
                    catch
                    {
                        // 忽略無法讀取 Session 的裝置
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("NAudio Error: " + ex.Message);
            }

            return apps;
        }

        private ImageSource? GetIcon(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
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