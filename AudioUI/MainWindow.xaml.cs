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

//
//                       _oo0oo_
//                      o8888888o
//                      88" . "88
//                      (| -_- |)
//                      0\  =  /0
//                    ___/`---'\___
//                  .' \\|     |// '.
//                 / \\|||  :  |||// \
//                / _||||| -:- |||||- \
//               |   | \\\  -  /// |   |
//               | \_|  ''\---/''  |_/ |
//               \  .-\__  '-'  ___/-. /
//             ___'. .'  /--.--\  `. .'___
//          ."" '<  `.___\_<|>_/___.' >' "".
//         | | :  `- \`.;`\ _ /`;.`/ - ` : | |
//         \  \ `_.   \_ __\ /__ _/   .-` /  /
//     =====`-.____`.___ \_____/___.-`___.-'=====
//                       `=---='
//
//
//     ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
//
//               佛祖保佑         永无BUG
//
//
//

namespace AudioUI
{
    // --- 資料模型 ---
    public enum SortMode { NameAsc, NameDesc, VolumeDesc }

    // --- 主視窗邏輯 ---
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private bool isDrawerOpen = false;

        // 服務層
        private readonly MainWindowViewModel _vm = new MainWindowViewModel();
        private KeyMappingService _KeyMapService = new KeyMappingService();
        private WakeWordTrigger _WakeWordTrigger;
        private PerProcessAudioRecorder _PerProcessAudioRecorder = new PerProcessAudioRecorder();

        private HotkeyService _HotkeyService = new HotkeyService();
        private WinForms.NotifyIcon _notifyIcon;

        // UI 綁定集合
        // XAML 綁在視窗上，實際內容由 ViewModel 持有；轉發是為了讓繫結路徑一個字都不用改。
        public ObservableCollection<AudioAppModel> AppList => _vm.AppList;
        public ObservableCollection<AudioAppModel> RecentAppList => _vm.RecentAppList;
        public ObservableCollection<DeviceInfoModel> DeviceList => _vm.DeviceList;
        public ObservableCollection<ConfigOptionItem> ConfigOptions => _vm.ConfigOptions;
        public ObservableCollection<MacroKeyModel> MacroKeys => _vm.MacroKeys;
        public ObservableCollection<SituationSummary> ChatList => _vm.ChatList;
        public ObservableCollection<ChatMessageModel> ChatMessages => _vm.ChatMessages;

        private string _selectedDeviceName = "";

        public string SelectedDeviceName
        {
            get => _selectedDeviceName;
            set { _selectedDeviceName = value; OnPropertyChanged(); }
        }

        private ConfigOptionItem? _selectedConfigToBind;
        internal float recognitionConfidience = 0.55f;

        public ICommand MinimizeCommand { get; }
        public ICommand MaximizeCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand MicrophoneCommand { get; }

        public MainWindow()
        {
            InitializeComponent();
            _WakeWordTrigger = new WakeWordTrigger(this);
            _WakeWordTrigger.InitializeSpeechRecognition();
            this.DataContext = this;

            MinimizeCommand = new RelayCommand(_ => WindowState = WindowState.Minimized);
            MaximizeCommand = new RelayCommand(_ => { WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized; });
            CloseCommand = new RelayCommand(_ => this.Close());

            MicrophoneCommand = new RelayCommand(async _ => await _vm.RecordAndProcessCurrentAsync());

            this.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) this.DragMove(); };

            _vm.RefreshAudioApps();
            _vm.InitDevices();
            _vm.Load();
            _vm.InitMuteConfig();
            _KeyMapService.Load();
            _vm.RefreshChatList();

            InitSystemTray();
            this.Loaded += (s, e) => InitGlobalHotkeys();
        }

        // --- 1. 聊天室邏輯 ---


        private void NewChat_Click(object sender, RoutedEventArgs e)
        {
            _vm.CurrentSituationId = -1;
            if (this.FindName("DefaultEntrancePanel") is Grid home) home.Visibility = Visibility.Visible;
            if (this.FindName("ChatSessionPanel") is Grid chat) chat.Visibility = Visibility.Collapsed;
        }

        private void ChatSession_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string idStr && int.TryParse(idStr, out int id))
            {
                _vm.CurrentSituationId = id;
                _vm.LoadChatHistory(idStr);

                if (this.FindName("DefaultEntrancePanel") is Grid home) home.Visibility = Visibility.Collapsed;
                if (this.FindName("ChatSessionPanel") is Grid chat) chat.Visibility = Visibility.Visible;
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
            if (this.FindName("AdjustmentInputBox") is System.Windows.Controls.TextBox inputBox
                && !string.IsNullOrWhiteSpace(inputBox.Text))
            {
                string userText = inputBox.Text;
                inputBox.Text = "";
                await _vm.SendAdjustmentAsync(userText);
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
                    // _vm.ApplyConfigToAPO(configPath); <--- 拿掉這行

                    // 2. 邏輯說明：
                    // 送出時 ViewModel 已經把這份設定推進該情境的紀錄
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

        // async void 是事件處理器唯一能等待 Task 的形狀，代價是例外不會往外傳，
        // 所以這裡必須自己接完——否則熱鍵套用失敗會安靜到連按的人都不知道發生過什麼。
        private async void HandleGlobalHotkey(int keyCode)
        {
            string btnId = _vm.GetBtnIdByKeyCode(keyCode); if (string.IsNullOrEmpty(btnId)) return;
            string configId = _KeyMapService.GetBoundConfigId(btnId); if (string.IsNullOrEmpty(configId)) return;
            try { await _vm.ExecuteConfig(configId); }
            catch (Exception ex) { SendNotification("快捷鍵失敗", ex.Message); }
        }


        private void SendNotification(string title, string content) => AppConfig.Notifier.Notify(title, content);





        private void DeviceCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is DeviceInfoModel device)
            {
                SelectedDeviceName = device.Name; _vm.RefreshConfigOptions(); LoadKeyButtons(device.Name);
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
                        if (boundId == "cmd_rollback") display = "Rollback"; if (boundId == SituationIds.Mute) display = "Mute";
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
                        string msg = _selectedConfigToBind.SituationId == SituationIds.Mute ? "全域靜音" : _selectedConfigToBind.DisplayName;
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
            else if (tag == "Control") { SidebarBorder.Visibility = Visibility.Collapsed; HighlightTab(TabControl, LineControl); ControlView.Visibility = Visibility.Visible; _vm.RefreshAudioApps(); }
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
            else { heightAnimation.To = 230; DrawerIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronDown; _vm.RefreshAudioApps(); }
            StatusDrawer.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation); isDrawerOpen = !isDrawerOpen;
        }
        private void SortBtn_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null) { btn.ContextMenu.PlacementTarget = btn; btn.ContextMenu.IsOpen = true; } }
        private void SortOption_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item) { string tag = item.Tag.ToString(); switch (tag) { case "NameAsc": _vm.CurrentSortMode = SortMode.NameAsc; SortModeText.Text = "排序模式: 名稱 (A-Z)"; break; case "NameDesc": _vm.CurrentSortMode = SortMode.NameDesc; SortModeText.Text = "排序模式: 名稱 (Z-A)"; break; case "VolumeDesc": _vm.CurrentSortMode = SortMode.VolumeDesc; SortModeText.Text = "排序模式: 音量 (大-小)"; break; } _vm.RefreshAudioApps(); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        private class RelayCommand : ICommand { private readonly Action<object?> _execute; public RelayCommand(Action<object?> execute) { _execute = execute; } public bool CanExecute(object? parameter) => true; public void Execute(object? parameter) => _execute(parameter); public event EventHandler? CanExecuteChanged; }
    }
}