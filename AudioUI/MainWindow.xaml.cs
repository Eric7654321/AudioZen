using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WpfApp1;
using Path = System.IO.Path;

namespace AudioUI
{
    // 定義排序模式
    public enum SortMode
    {
        NameAsc,    // 名稱 A-Z
        NameDesc,   // 名稱 Z-A
        VolumeDesc  // 音量 大-小
    }

    // ★★★ 新增：裝置資料模型 ★★★
    public class DeviceInfoModel
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string ImagePath { get; set; } = ""; // 圖片路徑 (需設定為 Resource)
    }

    public partial class MainWindow : Window
    {
        private bool isDrawerOpen = false;
        private AudioSessionService _AudioService = new AudioSessionService();

        // 1. 所有裝置列表 (入口/控制頁面)
        public ObservableCollection<AudioAppModel> AppList { get; set; } = new ObservableCollection<AudioAppModel>();

        // 2. 最近調整列表 (控制頁面)
        public ObservableCollection<AudioAppModel> RecentAppList { get; set; } = new ObservableCollection<AudioAppModel>();

        // 3. ★★★ 新增：硬體裝置列表 (裝置頁面) ★★★
        public ObservableCollection<DeviceInfoModel> DeviceList { get; set; } = new ObservableCollection<DeviceInfoModel>();

        // 目前的排序模式
        private SortMode _currentSortMode = SortMode.NameAsc;

        // Commands
        public ICommand MinimizeCommand { get; }
        public ICommand MaximizeCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand MicrophoneCommand { get; }

        // Gemini 相關
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
                // ... (保留原本的錄音邏輯) ...
                Console.WriteLine("Microphone button clicked!");
            });

            this.MouseLeftButtonDown += (s, e) => {
                if (e.ButtonState == MouseButtonState.Pressed) this.DragMove();
            };

            // 初始化載入音訊 APP
            RefreshAudioApps();

            // ★★★ 初始化硬體裝置假資料 ★★★
            InitDevices();
        }

        // 初始化裝置資料
        private void InitDevices()
        {
            // 請確保專案根目錄有這些圖片，並且 Build Action 設為 Resource
            DeviceList.Add(new DeviceInfoModel
            {
                Name = "自定義宏鍵盤",
                Description = "電腦鍵盤",
                ImagePath = "keyboard.png"
            });

            DeviceList.Add(new DeviceInfoModel
            {
                Name = "g304",
                Description = "Logitech G304 Lightspeed",
                ImagePath = "mouse.png"
            });

            DeviceList.Add(new DeviceInfoModel
            {
                Name = "Mouse",
                Description = "Standard Pointing Device",
                ImagePath = "hamster.png"
            });
        }

        // ★★★ Tab 切換邏輯 (重構版) ★★★
        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            string tag = btn.Tag.ToString();

            // 1. 先把所有 Tab 恢復成未選取狀態 (灰色、無底線、隱藏 View)
            ResetTabs();

            // 2. 根據點擊的 Tag 啟用對應頁面
            if (tag == "Entrance")
            {
                SidebarBorder.Visibility = Visibility.Visible; // 顯示側邊欄
                HighlightTab(TabEntrance, LineEntrance);
                EntranceView.Visibility = Visibility.Visible;
            }
            else if (tag == "Control")
            {
                SidebarBorder.Visibility = Visibility.Collapsed; // 隱藏側邊欄
                HighlightTab(TabControl, LineControl);
                ControlView.Visibility = Visibility.Visible;
                RefreshAudioApps(); // 刷新資料
            }
            else if (tag == "Device")
            {
                SidebarBorder.Visibility = Visibility.Collapsed; // 隱藏側邊欄
                HighlightTab(TabDevice, LineDevice);
                DeviceView.Visibility = Visibility.Visible;
            }
        }

        // 輔助方法：重置所有 Tab 樣式
        private void ResetTabs()
        {
            var grayBrush = new SolidColorBrush(Color.FromRgb(170, 170, 170)); // #AAAAAA

            // 重置按鈕樣式
            TabEntrance.Foreground = grayBrush;
            TabEntrance.FontWeight = FontWeights.Normal;
            LineEntrance.Visibility = Visibility.Hidden;

            TabControl.Foreground = grayBrush;
            TabControl.FontWeight = FontWeights.Normal;
            LineControl.Visibility = Visibility.Hidden;

            // 如果你有 TabDevice 按鈕，這邊也要重置
            // 注意：請確保 XAML 裡的裝置按鈕有設定 x:Name="TabDevice" 和 x:Name="LineDevice"
            if (this.FindName("TabDevice") is Button tabDevice)
            {
                tabDevice.Foreground = grayBrush;
                tabDevice.FontWeight = FontWeights.Normal;
            }
            if (this.FindName("LineDevice") is Border lineDevice)
            {
                lineDevice.Visibility = Visibility.Hidden;
            }

            // 隱藏所有視圖
            EntranceView.Visibility = Visibility.Hidden;
            ControlView.Visibility = Visibility.Hidden;
            if (this.FindName("DeviceView") is Grid deviceView)
            {
                deviceView.Visibility = Visibility.Hidden;
            }
        }

        // 輔助方法：高亮特定 Tab
        private void HighlightTab(Button btn, Border line)
        {
            btn.Foreground = new SolidColorBrush(Color.FromRgb(51, 153, 255)); // #3399FF
            btn.FontWeight = FontWeights.Bold;
            if (line != null) line.Visibility = Visibility.Visible;
        }

        // 抽屜開關
        private void DrawerBtn_Click(object sender, RoutedEventArgs e)
        {
            DoubleAnimation heightAnimation = new DoubleAnimation();
            heightAnimation.Duration = TimeSpan.FromSeconds(0.3);
            heightAnimation.EasingFunction = new QuadraticEase();

            if (isDrawerOpen)
            {
                heightAnimation.To = 0;
                DrawerIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronUp;
            }
            else
            {
                // 卡片高度變大了，這裡設為 230
                heightAnimation.To = 230;
                DrawerIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronDown;
                RefreshAudioApps();
            }

            StatusDrawer.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation);
            isDrawerOpen = !isDrawerOpen;
        }

        // 刷新與排序邏輯
        private void RefreshAudioApps()
        {
            AppList.Clear();
            RecentAppList.Clear();

            // 1. 建立「整體調整」卡片
            var globalApp = new AudioAppModel
            {
                Name = "整體調整",
                SystemVolume = 100,
                Config = new AppConfigData { TargetDevice = "System" } // 讓它變藍色
            };

            // 加入列表
            RecentAppList.Add(globalApp);
            AppList.Add(globalApp);

            try
            {
                // 3. 抓取真實資料
                var sessions = _AudioService.GetAppsWithConfig();
                var sessionList = new List<AudioAppModel>(sessions);

                // --- 處理最近調整 (取前 3 個) ---
                var top3Apps = sessionList.Take(3);
                foreach (var app in top3Apps)
                {
                    RecentAppList.Add(app);
                }

                // --- 處理主列表排序 ---
                IEnumerable<AudioAppModel> sortedList = sessionList;

                switch (_currentSortMode)
                {
                    case SortMode.NameAsc:
                        sortedList = sessionList.OrderBy(x => x.Name);
                        break;
                    case SortMode.NameDesc:
                        sortedList = sessionList.OrderByDescending(x => x.Name);
                        break;
                    case SortMode.VolumeDesc:
                        sortedList = sessionList.OrderByDescending(x => x.SystemVolume);
                        break;
                }

                foreach (var app in sortedList)
                {
                    AppList.Add(app);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Refresh Error: " + ex.Message);
            }
        }

        // 排序按鈕點擊
        private void SortBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        // 排序選項點擊
        private void SortOption_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuItem;
            if (item == null) return;

            string tag = item.Tag.ToString();

            switch (tag)
            {
                case "NameAsc":
                    _currentSortMode = SortMode.NameAsc;
                    SortModeText.Text = "排序模式: 名稱 (A-Z)";
                    break;
                case "NameDesc":
                    _currentSortMode = SortMode.NameDesc;
                    SortModeText.Text = "排序模式: 名稱 (Z-A)";
                    break;
                case "VolumeDesc":
                    _currentSortMode = SortMode.VolumeDesc;
                    SortModeText.Text = "排序模式: 音量 (大-小)";
                    break;
            }
            RefreshAudioApps();
        }

        // RelayCommand
        private class RelayCommand : ICommand
        {
            private readonly Action<object?> _execute;
            private readonly Func<object?, bool>? _canExecute;
            public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }
            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
            public void Execute(object? parameter) => _execute(parameter);
            public event EventHandler? CanExecuteChanged
            {
                add => CommandManager.RequerySuggested += value;
                remove => CommandManager.RequerySuggested -= value;
            }
        }
    }
}