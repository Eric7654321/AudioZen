using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AudioUI
{
    /// <summary>
    /// 列舉 WASAPI 上正在出聲的程式，回傳 Core 的 <see cref="AudioAppInfo"/>。
    ///
    /// 回傳型別刻意不帶 WPF 的圖示與筆刷：那會讓所有拿到這份清單的人一起被釘在 UI 層。
    /// 這裡只負責「誰在出聲、路徑是什麼」，長相交給畫面決定。
    /// </summary>
    public class AudioSessionService : IAudioSessions
    {
        private readonly ConfigService _configService = new ConfigService();

        public IReadOnlyList<string> RenderDeviceIdentities()
        {
            var list = new List<string>();
            try
            {
                var enumerator = new MMDeviceEnumerator();
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    try { list.Add($"{device.FriendlyName} {device.ID}"); }
                    catch { }
                }
            }
            catch
            {
                // 列舉不到就回空的：呼叫端會把它報成「每條路由都找不到裝置」，
                // 那正好是使用者當下的實際處境。
            }
            return list;
        }

        public IReadOnlyList<AudioAppInfo> List()
        {
            var apps = new List<AudioAppInfo>();
            var addedProcessIds = new HashSet<int>();

            var routes = AppConfig.Routes;

            // 只關心路由表用得到的那些裝置，其他區塊解析了也沒人看。
            var deviceKeywords = routes.Routes.Select(r => r.MatchKeyword).Distinct().ToList();

            Dictionary<string, string> deviceEffectMap = new Dictionary<string, string>();
            try
            {
                string configPath = _configService.GetConfigPath();
                if (File.Exists(configPath))
                    deviceEffectMap = ApoConfigSummary.ByKeyword(File.ReadAllText(configPath), deviceKeywords);
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

                                string processName = process.ProcessName;
                                string fullProcessName = processName + ".exe";

                                string finalSummary = "無";

                                // ByProcess 本身就忽略 .exe，所以不必為了「有沒有副檔名」查兩次。
                                var route = routes.ByProcess(fullProcessName) ?? routes.ByProcess(processName);
                                if (route != null && deviceEffectMap.TryGetValue(route.MatchKeyword, out string? effect))
                                    finalSummary = effect;

                                var app = new AudioAppInfo
                                {
                                    Name = !string.IsNullOrEmpty(process.MainWindowTitle) ? process.MainWindowTitle : process.ProcessName,
                                    SystemVolume = (int)(session.SimpleAudioVolume.Volume * 100),
                                    SystemMute = session.SimpleAudioVolume.Mute,
                                    IconPath = SafeMainModulePath(process),
                                    ProcessId = pid,
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
        /// 讀執行檔路徑。跨權限的 process（例如系統服務）讀 MainModule 會丟例外，
        /// 而那不是錯誤——那個程式只是沒有圖示可以給。
        /// </summary>
        private static string? SafeMainModulePath(Process process)
        {
            try { return process.MainModule?.FileName; }
            catch { return null; }
        }
    }
}
