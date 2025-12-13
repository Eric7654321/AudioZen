using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Path = System.IO.Path;
using WinForms = System.Windows.Forms;

namespace AudioUI
{
    // --- 資料模型 ---
    public enum SortMode { NameAsc, NameDesc, VolumeDesc }

    public class DeviceInfoModel
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string ImagePath { get; set; } = "";
    }

    public class ConfigOptionItem
    {
        public string SituationId { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string FilePath { get; set; }
    }

    public class MacroKeyModel : INotifyPropertyChanged
    {
        public string KeyId { get; set; }
        public string KeyName { get; set; }

        private string _boundActionName;
        public string BoundActionName
        {
            get => _boundActionName;
            set { _boundActionName = value; OnPropertyChanged(); }
        }
        public string BoundConfigId { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // UI 用對話模型 (純資料版)
    public class ChatMessageModel
    {
        public bool IsUser { get; set; }
        public string Message { get; set; }
        public string AudioFolderPath { get; set; } // 這是預覽音檔路徑
        public string ConfigPath { get; set; }      // ★★★ 新增：這是該次生成的 Config 路徑 ★★★

        public bool HasAudio => !string.IsNullOrEmpty(AudioFolderPath);
        // 如果有 ConfigPath，代表這是 AI 的回應，可以顯示「套用」按鈕
        public bool CanApply => !IsUser && !string.IsNullOrEmpty(ConfigPath);
    }

    // --- 主視窗邏輯 ---
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private bool isDrawerOpen = false;

        // 服務層
        private AudioSessionService _AudioService = new AudioSessionService();
        private GeminiServices _GeminiService = new GeminiServices();
        private TtsService _TtsService = new TtsService();
        private KeyMappingService _KeyMapService = new KeyMappingService();
        private WakeWordTrigger _WakeWordTrigger = new WakeWordTrigger();
        private PerProcessAudioRecorder _PerProcessAudioRecorder = new PerProcessAudioRecorder();
        public ChatManager _ChatManager = new ChatManager();

        private HotkeyService _HotkeyService = new HotkeyService();
        private WinForms.NotifyIcon _notifyIcon;

        // UI 綁定集合
        public ObservableCollection<AudioAppModel> AppList { get; set; } = new ObservableCollection<AudioAppModel>();
        public ObservableCollection<AudioAppModel> RecentAppList { get; set; } = new ObservableCollection<AudioAppModel>();
        public ObservableCollection<DeviceInfoModel> DeviceList { get; set; } = new ObservableCollection<DeviceInfoModel>();
        public ObservableCollection<ConfigOptionItem> ConfigOptions { get; set; } = new ObservableCollection<ConfigOptionItem>();
        public ObservableCollection<MacroKeyModel> MacroKeys { get; set; } = new ObservableCollection<MacroKeyModel>();
        public ObservableCollection<ChatSessionInfo> ChatList { get; set; } = new ObservableCollection<ChatSessionInfo>();
        public ObservableCollection<ChatMessageModel> ChatMessages { get; set; } = new ObservableCollection<ChatMessageModel>();

        private int _currentSituationId = -1;
        private string _selectedDeviceName = "";

        public string SelectedDeviceName
        {
            get => _selectedDeviceName;
            set { _selectedDeviceName = value; OnPropertyChanged(); }
        }

        private ConfigOptionItem? _selectedConfigToBind;
        private SortMode _currentSortMode = SortMode.NameAsc;
        internal float recognitionConfidience = 0.3f;

        // ★★★ API KEY 設定 (填入你的 Key) ★★★
        private const string API_KEY = "AIzaSyCG3tw4Whn_8XmdN_p2FaFl8IDzubYGk3k"; // 
        private const string GEMINI_URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent?key=" + API_KEY;

        public ICommand MinimizeCommand { get; }
        public ICommand MaximizeCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand MicrophoneCommand { get; }

        public MainWindow()
        {
            InitializeComponent();
            _WakeWordTrigger.InitializeSpeechRecognition();
            this.DataContext = this;

            MinimizeCommand = new RelayCommand(_ => WindowState = WindowState.Minimized);
            MaximizeCommand = new RelayCommand(_ => { WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized; });
            CloseCommand = new RelayCommand(_ => this.Close());

            MicrophoneCommand = new RelayCommand(async _ =>
            {
                // ★★★ 確保路徑指向正確的 BaseDirectory (解決路徑跑掉問題) ★★★
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string configDir = Path.Combine(baseDir, "config");
                string audioPath = Path.Combine(configDir, "command.wav");

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string configFileName = $"config_{timestamp}.txt";
                string configPath = Path.Combine(configDir, configFileName);

                try
                {
                    if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);

                    _TtsService.Stop();

                    // 呼叫錄音並處理
                    await _GeminiService.RecordAndProcessAsync(_currentSituationId, audioPath, configPath, _ChatManager, 5000);

                    // 刷新 UI
                    RefreshConfigOptions();
                    RefreshChatList();

                    if (_currentSituationId != -1)
                    {
                        LoadChatHistory(_currentSituationId.ToString());
                    }
                    else
                    {
                        RefreshChatList();
                    }

                    ApplyConfigToAPO(configPath);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"錯誤: {ex.Message}");
                }
            });

            this.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) this.DragMove(); };

            RefreshAudioApps();
            InitDevices();

            _ChatManager.LoadFromJson();
            InitMuteConfig();
            _KeyMapService.Load();
            RefreshChatList();

            InitSystemTray();
            this.Loaded += (s, e) => InitGlobalHotkeys();
        }

        // --- 1. 聊天室邏輯 ---

        private void RefreshChatList()
        {
            ChatList.Clear();
            var sessions = _ChatManager.GetChatList();
            foreach (var session in sessions)
            {
                ChatList.Add(session);
            }
        }

        private void NewChat_Click(object sender, RoutedEventArgs e)
        {
            _currentSituationId = -1;
            if (this.FindName("DefaultEntrancePanel") is Grid home) home.Visibility = Visibility.Visible;
            if (this.FindName("ChatSessionPanel") is Grid chat) chat.Visibility = Visibility.Collapsed;
        }

        private void ChatSession_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string idStr && int.TryParse(idStr, out int id))
            {
                _currentSituationId = id;
                LoadChatHistory(idStr);

                if (this.FindName("DefaultEntrancePanel") is Grid home) home.Visibility = Visibility.Collapsed;
                if (this.FindName("ChatSessionPanel") is Grid chat) chat.Visibility = Visibility.Visible;
            }
        }

        private void LoadChatHistory(string id)
        {
            ChatMessages.Clear();

            var mapItem = _ChatManager.MapList.FirstOrDefault(x => x.Id == id);

            if (mapItem == null)
            {
                System.Windows.MessageBox.Show($"找不到 ID: {id} 的資料");
                return;
            }

            string recordPath = mapItem.RecordPath ?? "";
            var history = _ChatManager.GetHistory(id, 20);

            // ★★★ 除錯：印出對話內容 ★★★
            string debugMsg = $"ID: {id}, RecordPath: {recordPath}\n抓到 {history.Count} 筆對話:\n";
            foreach (var h in history)
            {
                debugMsg += $"User: {h.UserInput}\nAI: {h.AiResponse}\n---\n";
            }
            System.Windows.MessageBox.Show(debugMsg, "對話內容檢查");

            // 填入 ChatMessages
            foreach (var msg in history)
            {
                if (!string.IsNullOrEmpty(msg.UserInput))
                {
                    ChatMessages.Add(new ChatMessageModel
                    {
                        IsUser = true,
                        Message = msg.UserInput
                    });
                }

                if (!string.IsNullOrEmpty(msg.AiResponse))
                {
                    ChatMessages.Add(new ChatMessageModel
                    {
                        IsUser = false,
                        Message = msg.AiResponse,
                        AudioFolderPath = recordPath
                    });
                }
            }
        }

        private void PlayAudio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string path)
            {
                try
                {
                    // 情況 A: 如果是檔案 (Preview.wav)，直接播放該檔案
                    if (File.Exists(path))
                    {
                        // 簡單播放單檔邏輯
                        var player = new System.Media.SoundPlayer(path);
                        player.Play();
                        // 或者用 NAudio:
                        // using var audioFile = new AudioFileReader(path);
                        // using var outputDevice = new WaveOutEvent();
                        // outputDevice.Init(audioFile);
                        // outputDevice.Play();
                        // while (outputDevice.PlaybackState == PlaybackState.Playing) { Thread.Sleep(100); }
                    }
                    // 情況 B: 如果是資料夾 (原始錄音包)，播放資料夾內所有檔案
                    else if (Directory.Exists(path))
                    {
                        _PerProcessAudioRecorder.PlayAllInFolder(path);
                        SendNotification("播放原始錄音", "正在播放原始錄音樣本...");
                    }
                    else
                    {
                        System.Windows.MessageBox.Show($"找不到檔案或資料夾：\n{path}");
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"播放失敗: {ex.Message}");
                }
            }
        }

        private void AdjustmentInputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendAdjustment_Click(sender, null);
            }
        }

        private async void SendAdjustment_Click(object sender, RoutedEventArgs e)
        {
            if (this.FindName("AdjustmentInputBox") is System.Windows.Controls.TextBox inputBox && !string.IsNullOrWhiteSpace(inputBox.Text))
            {
                string userText = inputBox.Text;
                inputBox.Text = "";

                // 1. 顯示 User 訊息
                ChatMessages.Add(new ChatMessageModel { IsUser = true, Message = userText });

                var thinkingMsg = new ChatMessageModel { IsUser = false, Message = "思考中..." };
                ChatMessages.Add(thinkingMsg);

                try
                {
                    // 2. 呼叫 Gemini 生成新 Config
                    string geminiResponse = await _GeminiService.CallGeminiApiAsync(userText, GEMINI_URL);

                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string configFileName = $"config_{timestamp}.txt";
                    string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", configFileName);

                    var deviceMap = new Dictionary<string, string>
                    {
                        { "first", "Speakers (Realtek(R) Audio)" },
                        { "second", "Headphones (HyperX Cloud II)" },
                        { "third", "VG279Q (NVIDIA High Definition Audio)" }
                    };

                    string aiMessage = _GeminiService.ParseAndWriteConfig(geminiResponse, configPath, deviceMap);
                    if (aiMessage == "-1") aiMessage = "抱歉，我無法理解您的調整需求。";

                    // 3. ★★★ 關鍵修改：不直接 Apply，而是生成預覽音檔 ★★★
                    // ApplyConfigToAPO(configPath); <--- 註解掉這行！

                    // 取得原始錄音檔路徑
                    // 假設原始錄音檔叫做 command.wav，或者在 RecordPath 裡
                    var currentChat = _ChatManager.MapList.FirstOrDefault(x => x.Id == _currentSituationId.ToString());
                    string recordFolder = currentChat?.RecordPath ?? "";

                    // 嘗試找到原始錄音檔 (假設是資料夾裡的第一個 wav)
                    string originalWav = "";
                    if (Directory.Exists(recordFolder))
                    {
                        originalWav = Directory.GetFiles(recordFolder, "*.wav").FirstOrDefault();
                    }

                    // 生成預覽檔 (Preview)
                    string previewWavPath = "";
                    if (!string.IsNullOrEmpty(originalWav))
                    {
                        previewWavPath = AudioProcessor.GeneratePreview(originalWav, configPath);
                    }

                    // 4. 存入 ChatManager (保持紀錄)
                    var newData = new FileCreateData
                    {
                        FileName = configPath,
                        UserInput = userText,
                        AiResponse = aiMessage
                    };
                    _ChatManager.PushFront(_currentSituationId.ToString(), newData, userText, recordFolder);
                    _ChatManager.SaveToJson();

                    // 5. 更新 UI
                    ChatMessages.Remove(thinkingMsg);
                    ChatMessages.Add(new ChatMessageModel
                    {
                        IsUser = false,
                        Message = aiMessage,
                        AudioFolderPath = !string.IsNullOrEmpty(previewWavPath) ? previewWavPath : recordFolder, // 優先播預覽檔
                        ConfigPath = configPath // ★★★ 綁定 Config 路徑 ★★★
                    });

                    RefreshChatList();
                }
                catch (Exception ex)
                {
                    ChatMessages.Remove(thinkingMsg);
                    ChatMessages.Add(new ChatMessageModel { IsUser = false, Message = $"發生錯誤: {ex.Message}" });
                }
            }
        }

        // ★★★ 最終修正：只更新狀態，不強制覆蓋系統 APO ★★★
        private void ApplyPreview_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string configPath)
            {
                if (File.Exists(configPath))
                {
                    // 1. 這裡 "不" 呼叫 ApplyConfigToAPO
                    // ApplyConfigToAPO(configPath); <--- 拿掉這行

                    // 2. 邏輯說明：
                    // 在 SendAdjustment_Click 時，我們已經呼叫了 _ChatManager.PushFront
                    // 所以這份 Config 已經是該情境 (SituationID) 的 "最新設定" 了。
                    // 使用者點擊這個按鈕，代表他 "確認" 這是他要的。

                    // 3. (選用) 如果你希望點擊舊訊息的套用能把舊設定拉到最上面，可以在這裡實作
                    // 但為了 Demo 穩定，我們假設使用者都是針對最新回應點套用。

                    // 4. 跳出通知，告知使用者設定已保存
                    SendNotification("設定已保存", "✅ 已更新此情境的預設值 (請按快捷鍵套用)");
                }
                else
                {
                    System.Windows.MessageBox.Show("設定檔已遺失，無法套用。");
                }
            }
        }

        // --- 2. 系統列 & 快捷鍵 & Config ---

        private void InitSystemTray()
        {
            _notifyIcon = new WinForms.NotifyIcon();
            if (File.Exists("icon.ico")) _notifyIcon.Icon = new Drawing.Icon("icon.ico");
            else _notifyIcon.Icon = Drawing.SystemIcons.Application;
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "AI Audio Mixer - 背景執行中";
            _notifyIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = WindowState.Normal; this.Activate(); };
            var contextMenu = new WinForms.ContextMenuStrip();
            contextMenu.Items.Add("開啟介面", null, (s, e) => { this.Show(); this.WindowState = WindowState.Normal; });
            contextMenu.Items.Add("離開程式", null, (s, e) => { _notifyIcon.Visible = false; _HotkeyService.Dispose(); System.Windows.Application.Current.Shutdown(); });
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        protected override void OnClosing(CancelEventArgs e) { e.Cancel = true; this.Hide(); base.OnClosing(e); }

        private void InitGlobalHotkeys()
        {
            _HotkeyService.Init(this);
            var keyMap = new Dictionary<string, int>
            {
                { "btn01", 103 }, { "btn02", 104 }, { "btn03", 105 },
                { "btn04", 100 }, { "btn05", 101 }, { "btn06", 102 },
                { "btn07", 97 },  { "btn08", 98 },  { "btn09", 99 },
                { "btn10", 96 },  { "btn11", 110 }, { "btn12", 13 }
            };
            foreach (var kvp in keyMap) { _HotkeyService.Register(kvp.Value, 1, (uint)kvp.Value); }
            _HotkeyService.OnHotkeyPressed += HandleGlobalHotkey;
        }

        private void HandleGlobalHotkey(int keyCode)
        {
            string btnId = GetBtnIdByKeyCode(keyCode); if (string.IsNullOrEmpty(btnId)) return;
            string configId = _KeyMapService.GetBoundConfigId(btnId); if (string.IsNullOrEmpty(configId)) return;
            ExecuteConfig(configId);
        }

        private string GetBtnIdByKeyCode(int code)
        {
            if (code == 103) return "btn01"; if (code == 104) return "btn02"; if (code == 105) return "btn03";
            if (code == 100) return "btn04"; if (code == 101) return "btn05"; if (code == 102) return "btn06";
            if (code == 97) return "btn07"; if (code == 98) return "btn08"; if (code == 99) return "btn09";
            if (code == 96) return "btn10"; if (code == 110) return "btn11"; if (code == 13) return "btn12";
            return "";
        }

        private void SendNotification(string title, string content)
        {
            new ToastContentBuilder().AddText(title).AddText(content).Show(t => t.ExpirationTime = DateTimeOffset.Now.AddSeconds(5));
        }

        private async Task ExecuteConfig(string configId)
        {
            string apoPath = @"C:\Program Files\EqualizerAPO\config\config.txt";

            if (configId == "cmd_rollback")
            {
                try { await _GeminiService.ConfigRollback("-1", apoPath, _ChatManager); SendNotification("快捷鍵觸發", "↩️ 已回復上一個設定"); RefreshConfigOptions(); }
                catch { SendNotification("無法回復", "沒有歷史紀錄可供還原。"); }
                return;
            }
            else if (configId == "cmd_mute" || configId == "114514")
            {
                var muteItem = _ChatManager.MapList.FirstOrDefault(x => x.Id == "114514");
                if (muteItem != null && muteItem.FileDatas.Count > 0) { ApplyConfigToAPO(muteItem.FileDatas[0].FileName); SendNotification("快捷鍵觸發", "🔇 已全域靜音"); }
                else SendNotification("錯誤", "找不到靜音設定檔");
                return;
            }
            else
            {
                var mapItem = _ChatManager.MapList.FirstOrDefault(x => x.Id == configId);
                if (mapItem == null) { _ChatManager.LoadFromJson(); mapItem = _ChatManager.MapList.FirstOrDefault(x => x.Id == configId); }

                if (mapItem != null && mapItem.FileDatas.Count > 0)
                {
                    ApplyConfigToAPO(mapItem.FileDatas[0].FileName);
                    string name = string.IsNullOrEmpty(mapItem.ChatName) ? $"情境 {configId}" : mapItem.ChatName;
                    SendNotification("設定已套用", $"⚡ 已切換至：{name}");
                }
                else SendNotification("設定失敗", $"找不到情境 ID: {configId}");
            }
        }

        private void ApplyConfigToAPO(string sourcePath)
        {
            if (!File.Exists(sourcePath)) { SendNotification("檔案遺失", $"找不到來源：{Path.GetFileName(sourcePath)}"); return; }
            string apoPath = @"C:\Program Files\EqualizerAPO\config\config.txt";
            try { var dir = Path.GetDirectoryName(apoPath); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir); File.Copy(sourcePath, apoPath, true); Console.WriteLine($"Config Applied: {sourcePath} -> {apoPath}"); }
            catch (Exception ex) { SendNotification("套用失敗", ex.Message); }
        }

        // --- 4. UI 互動與初始化 ---
        private void InitDevices()
        {
            DeviceList.Clear();
            DeviceList.Add(new DeviceInfoModel { Name = "自定義宏鍵盤", Description = "交大創客特供版", ImagePath = "keyboard.png" });
            DeviceList.Add(new DeviceInfoModel { Name = "g304", Description = "Logitech G304 Lightspeed", ImagePath = "mouse.png" });
            DeviceList.Add(new DeviceInfoModel { Name = "Mouse", Description = "Standard Pointing Device", ImagePath = "hamster.png" });
        }

        private void InitMuteConfig()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string configDir = Path.Combine(baseDir, "config");
            if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);

            string muteFilePath = Path.Combine(configDir, "mute.txt");
            if (!File.Exists(muteFilePath)) File.WriteAllText(muteFilePath, "Device: all\r\nPreamp: -100 dB\r\n# System Mute Config");

            var existing = _ChatManager.MapList.FirstOrDefault(x => x.Id == "114514");
            if (existing == null)
            {
                var muteData = new FileCreateData { FileName = muteFilePath, UserInput = "系統強制靜音", AiResponse = "已將前級擴大 (Preamp) 設為 -100dB。" };
                _ChatManager.PushFront("114514", muteData, "全域靜音", "");
                _ChatManager.SaveToJson();
            }
        }

        private void DeviceCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is DeviceInfoModel device)
            {
                SelectedDeviceName = device.Name; RefreshConfigOptions(); LoadKeyButtons(device.Name);
                if (this.FindName("DeviceListContainer") is FrameworkElement list) list.Visibility = Visibility.Collapsed;
                if (this.FindName("DeviceDetailPanel") is FrameworkElement detail) detail.Visibility = Visibility.Visible;
            }
        }

        private void BackToDeviceList_Click(object sender, RoutedEventArgs e)
        {
            if (this.FindName("DeviceListContainer") is FrameworkElement list) list.Visibility = Visibility.Visible;
            if (this.FindName("DeviceDetailPanel") is FrameworkElement detail) detail.Visibility = Visibility.Collapsed;
            _selectedConfigToBind = null;
        }

        private void ResetAllKeys_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show("重置所有按鍵設定？", "確認", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { _KeyMapService.ClearAll(); LoadKeyButtons(SelectedDeviceName); }
        }

        private void RefreshConfigOptions()
        {
            ConfigOptions.Clear(); _ChatManager.LoadFromJson();
            ConfigOptions.Add(new ConfigOptionItem { SituationId = "cmd_unbind", DisplayName = "解除綁定 (Unbind)", Description = "清除按鍵設定" });
            ConfigOptions.Add(new ConfigOptionItem { SituationId = "cmd_rollback", DisplayName = "Rollback", Description = "回復上一個操作" });

            foreach (var map in _ChatManager.MapList.Where(x => x.Id != "-1"))
            {
                var latest = map.FileDatas.FirstOrDefault();
                if (latest != null) ConfigOptions.Add(new ConfigOptionItem { SituationId = map.Id, DisplayName = string.IsNullOrEmpty(map.ChatName) ? $"情境 {map.Id}" : map.ChatName, Description = latest.UserInput ?? "AI 設定", FilePath = latest.FileName });
            }
        }

        private void LoadKeyButtons(string deviceName)
        {
            MacroKeys.Clear();
            if (deviceName.Contains("鍵盤"))
            {
                var labels = new[] { "7", "8", "9", "4", "5", "6", "1", "2", "3", "0", ".", "Enter" };
                for (int i = 0; i < 12; i++)
                {
                    string keyId = $"btn{i + 1:00}"; string boundId = _KeyMapService.GetBoundConfigId(keyId); string display = labels[i];
                    if (!string.IsNullOrEmpty(boundId))
                    {
                        var cfg = ConfigOptions.FirstOrDefault(x => x.SituationId == boundId);
                        display = cfg != null ? cfg.DisplayName : boundId;
                        if (boundId == "cmd_rollback") display = "Rollback"; if (boundId == "114514") display = "Mute";
                    }
                    MacroKeys.Add(new MacroKeyModel { KeyId = keyId, KeyName = labels[i], BoundConfigId = boundId, BoundActionName = display });
                }
            }
            else { for (int i = 0; i < 12; i++) MacroKeys.Add(new MacroKeyModel { KeyId = "", KeyName = "", BoundActionName = "-" }); }
        }

        private void ConfigList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (e.AddedItems.Count > 0 && e.AddedItems[0] is ConfigOptionItem item) _selectedConfigToBind = item; }
        private void KeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.DataContext is MacroKeyModel key)
            {
                if (_selectedConfigToBind != null)
                {
                    if (_selectedConfigToBind.SituationId == "cmd_unbind") { _KeyMapService.RemoveBinding(key.KeyId); key.BoundConfigId = null; key.BoundActionName = key.KeyName; System.Windows.MessageBox.Show($"已清除 [{key.KeyName}]"); }
                    else
                    {
                        key.BoundConfigId = _selectedConfigToBind.SituationId; key.BoundActionName = _selectedConfigToBind.DisplayName;
                        _KeyMapService.SetBinding(key.KeyId, _selectedConfigToBind.SituationId); _KeyMapService.Save();
                        string msg = _selectedConfigToBind.SituationId == "114514" ? "全域靜音" : _selectedConfigToBind.DisplayName;
                        System.Windows.MessageBox.Show($"綁定成功！\n[{key.KeyName}] -> [{msg}]");
                    }
                    _selectedConfigToBind = null;
                }
                else { string msg = string.IsNullOrEmpty(key.BoundConfigId) ? $"[{key.KeyName}] 未綁定" : $"[{key.KeyName}] 綁定：{key.BoundActionName}"; System.Windows.MessageBox.Show(msg + "\n請先點選左側功能進行綁定。"); }
            }
        }
        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button; if (btn == null) return; string tag = btn.Tag.ToString(); ResetTabs();
            if (tag == "Entrance") { SidebarBorder.Visibility = Visibility.Visible; HighlightTab(TabEntrance, LineEntrance); EntranceView.Visibility = Visibility.Visible; }
            else if (tag == "Control") { SidebarBorder.Visibility = Visibility.Collapsed; HighlightTab(TabControl, LineControl); ControlView.Visibility = Visibility.Visible; RefreshAudioApps(); }
            else if (tag == "Device") { SidebarBorder.Visibility = Visibility.Collapsed; HighlightTab(TabDevice, LineDevice); if (this.FindName("DeviceView") is Grid v) v.Visibility = Visibility.Visible; BackToDeviceList_Click(null, null); }
        }
        private void ResetTabs()
        {
            var gray = new SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170));
            TabEntrance.Foreground = gray; TabEntrance.FontWeight = FontWeights.Normal; LineEntrance.Visibility = Visibility.Hidden;
            TabControl.Foreground = gray; TabControl.FontWeight = FontWeights.Normal; LineControl.Visibility = Visibility.Hidden;
            if (this.FindName("TabDevice") is System.Windows.Controls.Button t) { t.Foreground = gray; t.FontWeight = FontWeights.Normal; }
            if (this.FindName("LineDevice") is Border l) { l.Visibility = Visibility.Hidden; }
            EntranceView.Visibility = Visibility.Collapsed; ControlView.Visibility = Visibility.Collapsed;
            if (this.FindName("DeviceView") is Grid v) v.Visibility = Visibility.Collapsed;
        }
        private void HighlightTab(System.Windows.Controls.Button btn, Border line) { btn.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 153, 255)); btn.FontWeight = FontWeights.Bold; if (line != null) line.Visibility = Visibility.Visible; }
        private void DrawerBtn_Click(object sender, RoutedEventArgs e)
        {
            DoubleAnimation heightAnimation = new DoubleAnimation(); heightAnimation.Duration = TimeSpan.FromSeconds(0.3); heightAnimation.EasingFunction = new QuadraticEase();
            if (isDrawerOpen) { heightAnimation.To = 0; DrawerIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronUp; }
            else { heightAnimation.To = 230; DrawerIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronDown; RefreshAudioApps(); }
            StatusDrawer.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation); isDrawerOpen = !isDrawerOpen;
        }
        private void SortBtn_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null) { btn.ContextMenu.PlacementTarget = btn; btn.ContextMenu.IsOpen = true; } }
        private void SortOption_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item) { string tag = item.Tag.ToString(); switch (tag) { case "NameAsc": _currentSortMode = SortMode.NameAsc; SortModeText.Text = "排序模式: 名稱 (A-Z)"; break; case "NameDesc": _currentSortMode = SortMode.NameDesc; SortModeText.Text = "排序模式: 名稱 (Z-A)"; break; case "VolumeDesc": _currentSortMode = SortMode.VolumeDesc; SortModeText.Text = "排序模式: 音量 (大-小)"; break; } RefreshAudioApps(); }
        }
        private void RefreshAudioApps()
        {
            AppList.Clear(); RecentAppList.Clear();
            var globalApp = new AudioAppModel { Name = "整體調整", SystemVolume = 100, Config = new AppConfigData { TargetDevice = "System" } };
            RecentAppList.Add(globalApp); AppList.Add(globalApp);
            try { var sessions = _AudioService.GetAppsWithConfig(); var sessionList = new List<AudioAppModel>(sessions); foreach (var app in sessionList.Take(3)) RecentAppList.Add(app); var sorted = _currentSortMode switch { SortMode.NameDesc => sessionList.OrderByDescending(x => x.Name), SortMode.VolumeDesc => sessionList.OrderByDescending(x => x.SystemVolume), _ => sessionList.OrderBy(x => x.Name) }; foreach (var app in sorted) AppList.Add(app); } catch { }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        private class RelayCommand : ICommand { private readonly Action<object?> _execute; public RelayCommand(Action<object?> execute) { _execute = execute; } public bool CanExecute(object? parameter) => true; public void Execute(object? parameter) => _execute(parameter); public event EventHandler? CanExecuteChanged; }
    }
}