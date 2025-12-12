using AudioTools;
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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Path = System.IO.Path;
// ★★★ 關鍵修正：只給 WinForms 一個別名，不要整個引用，避免 Button/MessageBox 衝突 ★★★
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

    // --- 主視窗邏輯 ---

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private bool isDrawerOpen = false;

        // 服務層實例
        private AudioSessionService _AudioService = new AudioSessionService();
        private GeminiServices _GeminiService = new GeminiServices();
        private TtsService _TtsService = new TtsService();
        private MappingManager _MappingManager = new MappingManager();
        private KeyMappingService _KeyMapService = new KeyMappingService();
        private WakeWordTrigger _WakeWordTrigger = new WakeWordTrigger();

        // 背景執行與快捷鍵服務
        private HotkeyService _HotkeyService = new HotkeyService();
        private WinForms.NotifyIcon _notifyIcon;

        // UI 綁定集合
        public ObservableCollection<AudioAppModel> AppList { get; set; } = new ObservableCollection<AudioAppModel>();
        public ObservableCollection<AudioAppModel> RecentAppList { get; set; } = new ObservableCollection<AudioAppModel>();
        public ObservableCollection<DeviceInfoModel> DeviceList { get; set; } = new ObservableCollection<DeviceInfoModel>();
        public ObservableCollection<ConfigOptionItem> ConfigOptions { get; set; } = new ObservableCollection<ConfigOptionItem>();
        public ObservableCollection<MacroKeyModel> MacroKeys { get; set; } = new ObservableCollection<MacroKeyModel>();

        private string _selectedDeviceName = "";
        public string SelectedDeviceName
        {
            get => _selectedDeviceName;
            set { _selectedDeviceName = value; OnPropertyChanged(); }
        }

        private ConfigOptionItem? _selectedConfigToBind;
        private SortMode _currentSortMode = SortMode.NameAsc;

        // Commands
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

            // Close 不直接關閉，而是觸發 OnClosing 縮小到系統列
            CloseCommand = new RelayCommand(_ => this.Close());

            MicrophoneCommand = new RelayCommand(async _ =>
            {
                string audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "command.wav");
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string configFileName = $"config_{timestamp}.txt";
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", configFileName);

                try
                {
                    var dir = Path.GetDirectoryName(configPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    //bool isAccepted = await _GeminiService.SendNotificationAndWaitAsync();
                    //System.Windows.MessageBox.Show(isAccepted.ToString());

                    _TtsService.Stop();
                    //await PerProcessAudioRecorder.RecordAllActiveAppsAsync(
                    //    Path.Combine(".", "config", "record"), 
                    //    TimeSpan.FromSeconds(3));

                    await _GeminiService.RecordAndProcessAsync(0, 5000, audioPath, configPath);

                    // 錄音完成後刷新 Config 列表
                    RefreshConfigOptions();

                    // 生成完畢後，直接套用一次
                    ApplyConfigToAPO(configPath);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"錯誤: {ex.Message}");
                }
            });

            this.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) this.DragMove(); };

            // 初始化資料
            RefreshAudioApps();
            InitDevices();

            _MappingManager.LoadFromJson();
            _KeyMapService.Load();

            // 初始化系統列與快捷鍵
            InitSystemTray();
            this.Loaded += (s, e) => InitGlobalHotkeys();
        }

        // --- 1. 系統列 (System Tray) ---
        private void InitSystemTray()
        {
            _notifyIcon = new WinForms.NotifyIcon();

            // ★★★ 修正：明確使用 System.Drawing.Icon 避免衝突 ★★★
            if (File.Exists("icon.ico"))
                _notifyIcon.Icon = new System.Drawing.Icon("icon.ico");
            else
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;

            _notifyIcon.Visible = true;
            _notifyIcon.Text = "AI Audio Mixer - 背景執行中";

            _notifyIcon.DoubleClick += (s, e) => {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            };

            var contextMenu = new WinForms.ContextMenuStrip();
            contextMenu.Items.Add("開啟介面", null, (s, e) => { this.Show(); this.WindowState = WindowState.Normal; });
            contextMenu.Items.Add("離開程式", null, (s, e) => {
                _notifyIcon.Visible = false;
                _HotkeyService.Dispose();
                System.Windows.Application.Current.Shutdown();
            });
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        // 攔截關閉視窗 -> 縮小
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
            base.OnClosing(e);
        }

        // --- 2. 全域快捷鍵 (Global Hotkeys) ---
        private void InitGlobalHotkeys()
        {
            _HotkeyService.Init(this);

            // 註冊 Numpad 0~9, ., Enter (對應 btn01 ~ btn12)
            var keyMap = new Dictionary<string, int>
            {
                { "btn07", 103 }, { "btn08", 104 }, { "btn09", 105 },
                { "btn04", 100 }, { "btn05", 101 }, { "btn06", 102 },
                { "btn01", 97 },  { "btn02", 98 },  { "btn03", 99 },
                { "btn10", 96 },  { "btn11", 110 }, { "btn12", 13 }
            };

            foreach (var kvp in keyMap)
            {
                // ★★★ 修正：把 0 改成 1 (代表 Alt) ★★★
                // 0 = None (會吃掉按鍵，導致無法打字)
                // 1 = Alt
                // 2 = Ctrl
                // 4 = Shift
                // 8 = Win
                // 這裡我們設定為 Alt + 按鍵，這樣就不會影響正常輸入了
                _HotkeyService.Register(kvp.Value, 1, (uint)kvp.Value);
            }

            _HotkeyService.OnHotkeyPressed += HandleGlobalHotkey;
        }

        private void HandleGlobalHotkey(int keyCode)
        {
            // 反查是哪個 btnID
            string btnId = GetBtnIdByKeyCode(keyCode);
            if (string.IsNullOrEmpty(btnId)) return;

            // 查表：綁定了哪個 Config
            string configId = _KeyMapService.GetBoundConfigId(btnId);
            if (string.IsNullOrEmpty(configId)) return;

            // 執行套用
            ExecuteConfig(configId);
        }

        private string GetBtnIdByKeyCode(int code)
        {
            if (code == 103) return "btn07"; if (code == 104) return "btn08"; if (code == 105) return "btn09";
            if (code == 100) return "btn04"; if (code == 101) return "btn05"; if (code == 102) return "btn06";
            if (code == 97) return "btn01"; if (code == 98) return "btn02"; if (code == 99) return "btn03";
            if (code == 96) return "btn10"; if (code == 110) return "btn11"; if (code == 13) return "btn12";
            return "";
        }

        // --- 3. 核心功能執行 ---
        private void ExecuteConfig(string configId)
        {
            if (configId == "cmd_mute")
            {
                _notifyIcon.ShowBalloonTip(1000, "AudioZen", "已全域靜音", WinForms.ToolTipIcon.Info);
                return;
            }
            if (configId == "cmd_rollback")
            {
                return;
            }

            var mapItem = _MappingManager.MapList.FirstOrDefault(x => x.Id == configId);
            if (mapItem != null && mapItem.FileDatas.Count > 0)
            {
                string filePath = mapItem.FileDatas[0].FileName;
                ApplyConfigToAPO(filePath);
                _notifyIcon.ShowBalloonTip(1000, "AudioZen", $"已切換情境: {configId}", WinForms.ToolTipIcon.Info);
            }
        }

        private void ApplyConfigToAPO(string sourcePath)
        {
            if (!File.Exists(sourcePath)) return;

            // ★★★ APO 路徑 (請根據實際情況修改) ★★★
            string apoPath = @"C:\Program Files\EqualizerAPO\config\config.txt";

            try
            {
                var dir = Path.GetDirectoryName(apoPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.Copy(sourcePath, apoPath, true);
                Console.WriteLine($"Config Applied: {sourcePath} -> {apoPath}");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"套用失敗: {ex.Message}\n請確認是否有權限或 APO 路徑正確。");
            }
        }

        // --- 4. UI 互動與初始化 ---

        private void InitDevices()
        {
            DeviceList.Add(new DeviceInfoModel { Name = "自定義宏鍵盤", Description = "交大創客特供版", ImagePath = "keyboard.png" });
            DeviceList.Add(new DeviceInfoModel { Name = "g304", Description = "Logitech G304 Lightspeed", ImagePath = "mouse.png" });
            DeviceList.Add(new DeviceInfoModel { Name = "Mouse", Description = "Standard Pointing Device", ImagePath = "hamster.png" });
        }

        private void DeviceCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is DeviceInfoModel device)
            {
                SelectedDeviceName = device.Name;
                RefreshConfigOptions();
                LoadKeyButtons(device.Name);

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
            if (System.Windows.MessageBox.Show("確定要重置所有按鍵設定嗎？", "重置確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _KeyMapService.ClearAll();
                LoadKeyButtons(SelectedDeviceName);
            }
        }

        private void RefreshConfigOptions()
        {
            ConfigOptions.Clear();
            _MappingManager.LoadFromJson();

            ConfigOptions.Add(new ConfigOptionItem { SituationId = "cmd_unbind", DisplayName = "解除綁定 (Unbind)", Description = "清除按鍵設定" });
            ConfigOptions.Add(new ConfigOptionItem { SituationId = "cmd_rollback", DisplayName = "Rollback", Description = "回復上一個操作" });
            ConfigOptions.Add(new ConfigOptionItem { SituationId = "cmd_mute", DisplayName = "Mute", Description = "全域靜音" });

            foreach (var map in _MappingManager.MapList.Where(x => x.Id != "-1"))
            {
                var latestFile = map.FileDatas.FirstOrDefault();
                if (latestFile != null)
                {
                    ConfigOptions.Add(new ConfigOptionItem
                    {
                        SituationId = map.Id,
                        DisplayName = $"情境 {map.Id}",
                        Description = latestFile.UserInput ?? "AI 自動設定",
                        FilePath = latestFile.FileName
                    });
                }
            }
        }

        private void LoadKeyButtons(string deviceName)
        {
            MacroKeys.Clear();

            if (deviceName.Contains("鍵盤"))
            {
                var numpadLabels = new string[] { "7", "8", "9", "4", "5", "6", "1", "2", "3", "0", ".", "Enter" };

                for (int i = 0; i < 12; i++)
                {
                    string keyId = $"btn{i + 1:00}";
                    string boundId = _KeyMapService.GetBoundConfigId(keyId);

                    string displayText = numpadLabels[i];
                    if (!string.IsNullOrEmpty(boundId))
                    {
                        var config = ConfigOptions.FirstOrDefault(x => x.SituationId == boundId);
                        displayText = config != null ? config.DisplayName : boundId;
                    }

                    MacroKeys.Add(new MacroKeyModel { KeyId = keyId, KeyName = numpadLabels[i], BoundConfigId = boundId, BoundActionName = displayText });
                }
            }
            else
            {
                for (int i = 0; i < 12; i++) MacroKeys.Add(new MacroKeyModel { KeyId = "", KeyName = "", BoundActionName = "-" });
            }
        }

        private void ConfigList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is ConfigOptionItem item)
            {
                _selectedConfigToBind = item;
            }
        }

        private void KeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.DataContext is MacroKeyModel key)
            {
                if (_selectedConfigToBind != null)
                {
                    if (_selectedConfigToBind.SituationId == "cmd_unbind")
                    {
                        _KeyMapService.RemoveBinding(key.KeyId);
                        key.BoundConfigId = null;
                        key.BoundActionName = key.KeyName;
                        System.Windows.MessageBox.Show($"已清除按鍵 [{key.KeyName}] 的綁定");
                    }
                    else
                    {
                        key.BoundConfigId = _selectedConfigToBind.SituationId;
                        key.BoundActionName = _selectedConfigToBind.DisplayName;
                        _KeyMapService.SetBinding(key.KeyId, _selectedConfigToBind.SituationId);
                        _KeyMapService.Save();
                        System.Windows.MessageBox.Show($"綁定成功！\n按鍵 [{key.KeyName}] -> [{_selectedConfigToBind.DisplayName}]");
                    }
                    _selectedConfigToBind = null;
                }
                else
                {
                    string msg = string.IsNullOrEmpty(key.BoundConfigId) ? $"[{key.KeyName}] 尚未綁定功能" : $"[{key.KeyName}] 目前綁定：{key.BoundActionName}";
                    System.Windows.MessageBox.Show(msg + "\n請先從左側點選功能，再點擊按鍵進行修改。", "按鍵資訊");
                }
            }
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;
            string tag = btn.Tag.ToString();
            ResetTabs();

            if (tag == "Entrance")
            {
                SidebarBorder.Visibility = Visibility.Visible;
                HighlightTab(TabEntrance, LineEntrance);
                EntranceView.Visibility = Visibility.Visible;
            }
            else if (tag == "Control")
            {
                SidebarBorder.Visibility = Visibility.Collapsed;
                HighlightTab(TabControl, LineControl);
                ControlView.Visibility = Visibility.Visible;
                RefreshAudioApps();
            }
            else if (tag == "Device")
            {
                SidebarBorder.Visibility = Visibility.Collapsed;
                HighlightTab(TabDevice, LineDevice);
                if (this.FindName("DeviceView") is Grid v) v.Visibility = Visibility.Visible;
                BackToDeviceList_Click(null, null);
            }
        }

        private void ResetTabs()
        {
            var gray = new SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170));
            TabEntrance.Foreground = gray; TabEntrance.FontWeight = FontWeights.Normal; LineEntrance.Visibility = Visibility.Hidden;
            TabControl.Foreground = gray; TabControl.FontWeight = FontWeights.Normal; LineControl.Visibility = Visibility.Hidden;
            if (this.FindName("TabDevice") is System.Windows.Controls.Button t) { t.Foreground = gray; t.FontWeight = FontWeights.Normal; }
            if (this.FindName("LineDevice") is Border l) { l.Visibility = Visibility.Hidden; }

            EntranceView.Visibility = Visibility.Collapsed;
            ControlView.Visibility = Visibility.Collapsed;
            if (this.FindName("DeviceView") is Grid v) v.Visibility = Visibility.Collapsed;
        }

        private void HighlightTab(System.Windows.Controls.Button btn, Border line)
        {
            btn.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 153, 255));
            btn.FontWeight = FontWeights.Bold;
            if (line != null) line.Visibility = Visibility.Visible;
        }

        private void DrawerBtn_Click(object sender, RoutedEventArgs e)
        {
            DoubleAnimation heightAnimation = new DoubleAnimation();
            heightAnimation.Duration = TimeSpan.FromSeconds(0.3);
            heightAnimation.EasingFunction = new QuadraticEase();
            if (isDrawerOpen) { heightAnimation.To = 0; DrawerIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronUp; }
            else { heightAnimation.To = 230; DrawerIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronDown; RefreshAudioApps(); }
            StatusDrawer.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation);
            isDrawerOpen = !isDrawerOpen;
        }
        private void SortBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null) { btn.ContextMenu.PlacementTarget = btn; btn.ContextMenu.IsOpen = true; }
        }
        private void SortOption_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item)
            {
                string tag = item.Tag.ToString();
                switch (tag)
                {
                    case "NameAsc": _currentSortMode = SortMode.NameAsc; SortModeText.Text = "排序模式: 名稱 (A-Z)"; break;
                    case "NameDesc": _currentSortMode = SortMode.NameDesc; SortModeText.Text = "排序模式: 名稱 (Z-A)"; break;
                    case "VolumeDesc": _currentSortMode = SortMode.VolumeDesc; SortModeText.Text = "排序模式: 音量 (大-小)"; break;
                }
                RefreshAudioApps();
            }
        }
        private void RefreshAudioApps()
        {
            AppList.Clear(); RecentAppList.Clear();
            var globalApp = new AudioAppModel { Name = "整體調整", SystemVolume = 100, Config = new AppConfigData { TargetDevice = "System" } };
            RecentAppList.Add(globalApp); AppList.Add(globalApp);
            try
            {
                var sessions = _AudioService.GetAppsWithConfig();
                var sessionList = new List<AudioAppModel>(sessions);
                foreach (var app in sessionList.Take(3)) RecentAppList.Add(app);
                var sorted = _currentSortMode switch
                {
                    SortMode.NameDesc => sessionList.OrderByDescending(x => x.Name),
                    SortMode.VolumeDesc => sessionList.OrderByDescending(x => x.SystemVolume),
                    _ => sessionList.OrderBy(x => x.Name)
                };
                foreach (var app in sorted) AppList.Add(app);
            }
            catch { }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private class RelayCommand : ICommand
        {
            private readonly Action<object?> _execute;
            public RelayCommand(Action<object?> execute) { _execute = execute; }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => _execute(parameter);
            public event EventHandler? CanExecuteChanged;
        }
    }
}