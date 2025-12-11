using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Path = System.IO.Path;
using System.IO;
using System;
using System.Linq;
using System.Collections.Generic;

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

    public class KeyBindingItem
    {
        public string ActionName { get; set; }
        public string BoundKey { get; set; }
        public string Description { get; set; }
    }

    public class MacroKeyModel
    {
        public string KeyId { get; set; }
        public string DisplayText { get; set; }
    }

    // --- 主視窗邏輯 ---

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private bool isDrawerOpen = false;
        private AudioSessionService _AudioService = new AudioSessionService();

        // 集合資料
        public ObservableCollection<AudioAppModel> AppList { get; set; } = new ObservableCollection<AudioAppModel>();
        public ObservableCollection<AudioAppModel> RecentAppList { get; set; } = new ObservableCollection<AudioAppModel>();
        public ObservableCollection<DeviceInfoModel> DeviceList { get; set; } = new ObservableCollection<DeviceInfoModel>();

        public ObservableCollection<KeyBindingItem> KeyBindings { get; set; } = new ObservableCollection<KeyBindingItem>();
        public ObservableCollection<MacroKeyModel> MacroKeys { get; set; } = new ObservableCollection<MacroKeyModel>();

        private string _selectedDeviceName = "";
        public string SelectedDeviceName
        {
            get => _selectedDeviceName;
            set { _selectedDeviceName = value; OnPropertyChanged(); }
        }

        private SortMode _currentSortMode = SortMode.NameAsc;

        public ICommand MinimizeCommand { get; }
        public ICommand MaximizeCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand MicrophoneCommand { get; }

        private const string API_KEY = "AIzaSyBJe-x4R2675FWctAAY3UrfW8hM1z9taoE";
        private const string GEMINI_URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent?key=" + API_KEY;
        GeminiServices _GeminiService = new GeminiServices();
        GeminiParser _GeminiParser = new GeminiParser();
        TtsService _TtsService = new TtsService();

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            MinimizeCommand = new RelayCommand(_ => WindowState = WindowState.Minimized);
            MaximizeCommand = new RelayCommand(_ => { WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized; });
            CloseCommand = new RelayCommand(_ => Close());

            MicrophoneCommand = new RelayCommand(async _ =>
            {
                // 保留原本錄音邏輯
                string audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "command.wav");
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string configFileName = $"config_{timestamp}.txt";
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", configFileName);

                try
                {
                    var dir = Path.GetDirectoryName(configPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    _TtsService.Stop();
                    await _GeminiService.RecordAndProcessAsync(5000, audioPath, configPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"錯誤: {ex.Message}");
                }
            });

            this.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) this.DragMove(); };

            RefreshAudioApps();
            InitDevices();
        }

        private void InitDevices()
        {
            DeviceList.Add(new DeviceInfoModel { Name = "自定義宏鍵盤", Description = "交大創客特供版", ImagePath = "keyboard.png" });
            DeviceList.Add(new DeviceInfoModel { Name = "g304", Description = "Logitech G304 Lightspeed", ImagePath = "mouse.png" });
            DeviceList.Add(new DeviceInfoModel { Name = "Mouse", Description = "Standard Pointing Device", ImagePath = "hamster.png" });
        }

        // ★★★★★ [關鍵修正] 點擊卡片跳轉邏輯 ★★★★★
        // 1. 確保 XAML 裡的 DeviceCardTemplate 有寫 MouseLeftButtonUp="DeviceCard_Click"
        // 2. 這裡會把列表隱藏，把詳情頁顯示出來
        private void DeviceCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is DeviceInfoModel device)
            {
                SelectedDeviceName = device.Name;
                LoadMockDetailData(device.Name);

                // 直接控制可見度
                if (DeviceListContainer != null) DeviceListContainer.Visibility = Visibility.Collapsed;
                if (DeviceDetailPanel != null) DeviceDetailPanel.Visibility = Visibility.Visible;
            }
        }

        // ★★★★★ [關鍵修正] 返回按鈕邏輯 ★★★★★
        private void BackToDeviceList_Click(object sender, RoutedEventArgs e)
        {
            // ★ 強制顯示列表 (List)
            if (DeviceListContainer != null) DeviceListContainer.Visibility = Visibility.Visible;

            // ★ 強制隱藏詳情 (Detail) -> 這是消除黑畫面的關鍵
            if (DeviceDetailPanel != null) DeviceDetailPanel.Visibility = Visibility.Collapsed;
        }

        // 載入詳情頁 Mock Data
        private void LoadMockDetailData(string deviceName)
        {
            KeyBindings.Clear();
            MacroKeys.Clear();

            if (deviceName.Contains("鍵盤"))
            {
                // 左側清單
                KeyBindings.Add(new KeyBindingItem { ActionName = "Rollback", BoundKey = "btn10", Description = "回覆上一個操作" });
                KeyBindings.Add(new KeyBindingItem { ActionName = "Mute", BoundKey = "未配置", Description = "靜音所有音訊輸出" });
                KeyBindings.Add(new KeyBindingItem { ActionName = "Screen", BoundKey = "未配置", Description = "呼叫軟體介面(再按一次退出)" });
                KeyBindings.Add(new KeyBindingItem { ActionName = "Preset42", BoundKey = "btn09", Description = "輸出音訊42.mp3" });

                // 右側 3x4 按鈕
                for (int i = 1; i <= 12; i++)
                {
                    string text = "";
                    if (i == 1) text = "Rollback";
                    if (i == 6) text = "Preset42";
                    MacroKeys.Add(new MacroKeyModel { KeyId = $"btn{i:00}", DisplayText = text });
                }
            }
            else
            {
                // 其他裝置
                KeyBindings.Add(new KeyBindingItem { ActionName = "Info", BoundKey = "-", Description = "無可配置的按鍵" });
            }
        }

        private void KeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is MacroKeyModel key)
            {
                string msg = string.IsNullOrEmpty(key.DisplayText)
                    ? $"按鍵 {key.KeyId} 目前是空的"
                    : $"按鍵 {key.KeyId} 已綁定功能: {key.DisplayText}";
                MessageBox.Show(msg, "按鍵設定");
            }
        }

        // Tab 切換邏輯 (確保切換 Tab 時，裝置頁面會重置回列表狀態)
        // MainWindow.xaml.cs 內部的 Tab_Click 方法
        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            string tag = btn.Tag.ToString();

            // 1. 先全部隱藏
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
                // ★ 側邊欄必須隱藏
                SidebarBorder.Visibility = Visibility.Collapsed;

                HighlightTab(TabDevice, LineDevice);

                // ★ 顯示 DeviceView
                // 這裡直接用變數名稱，不要用 FindName
                if (DeviceView != null) DeviceView.Visibility = Visibility.Visible;

                // ★★★ 關鍵：切換到裝置分頁時，強制把「詳情頁」關閉，顯示「列表」 ★★★
                // 如果這行沒執行，或者 BackToDeviceList_Click 邏輯有錯，黑畫面就會蓋住
                BackToDeviceList_Click(null, null);
            }
        }

        private void ResetTabs()
        {
            var grayBrush = new SolidColorBrush(Color.FromRgb(170, 170, 170));

            TabEntrance.Foreground = grayBrush;
            TabEntrance.FontWeight = FontWeights.Normal;
            LineEntrance.Visibility = Visibility.Hidden;

            TabControl.Foreground = grayBrush;
            TabControl.FontWeight = FontWeights.Normal;
            LineControl.Visibility = Visibility.Hidden;

            TabDevice.Foreground = grayBrush;
            TabDevice.FontWeight = FontWeights.Normal;
            LineDevice.Visibility = Visibility.Hidden;

            EntranceView.Visibility = Visibility.Collapsed;
            ControlView.Visibility = Visibility.Collapsed;

            // 直接操作
            if (DeviceView != null) DeviceView.Visibility = Visibility.Collapsed;
        }

        private void HighlightTab(Button btn, Border line)
        {
            btn.Foreground = new SolidColorBrush(Color.FromRgb(51, 153, 255));
            btn.FontWeight = FontWeights.Bold;
            if (line != null) line.Visibility = Visibility.Visible;
        }

        // Drawer, RefreshAudioApps, Sort 等保持不變
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
            if (sender is Button btn && btn.ContextMenu != null) { btn.ContextMenu.PlacementTarget = btn; btn.ContextMenu.IsOpen = true; }
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