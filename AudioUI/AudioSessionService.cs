using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.Generic;
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
            var configList = _configService.LoadConfig();
            var addedProcessIds = new HashSet<int>();

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

                            if (session.GetProcessID == 0 || session.State == AudioSessionState.AudioSessionStateExpired)
                                continue;

                            int pid = (int)session.GetProcessID;

                            if (addedProcessIds.Contains(pid))
                                continue;

                            try
                            {
                                var process = Process.GetProcessById(pid);
                                string processName = process.ProcessName;

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
    }
}