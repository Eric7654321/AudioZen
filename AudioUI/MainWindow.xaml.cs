using System.Collections.ObjectModel;
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
using WpfApp1; // 假設這是你的 Gemini 相關 namespace
using Path = System.IO.Path;

namespace AudioUI
{
    public partial class MainWindow : Window
    {
        // 紀錄抽屜是開還是關 (入口頁面用)
        private bool isDrawerOpen = false;

        // 音訊服務
        private AudioSessionService _AudioService = new AudioSessionService();

        // ★★★ 綁定給 UI (ItemsControl) 用的資料集合 ★★★

        // 1. 所有裝置列表 (入口抽屜 & 控制頁面共用)
        public ObservableCollection<AudioAppModel> AppList { get; set; } = new ObservableCollection<AudioAppModel>();

        // 2. 最近調整列表 (控制頁面專用)
        public ObservableCollection<AudioAppModel> RecentAppList { get; set; } = new ObservableCollection<AudioAppModel>();

        // 對應 XAML 的 Command 綁定
        public ICommand MinimizeCommand { get; }
        public ICommand MaximizeCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand MicrophoneCommand { get; }

        private const string API_KEY = "AIzaSyBJe-x4R2675FWctAAY3UrfW8hM1z9taoE"; // TODO: 換成你的 Key
        private const string GEMINI_URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent?key=" + API_KEY;

        GeminiServices _GeminiService = new GeminiServices();
        GeminiParser _GeminiParser = new GeminiParser();
        TtsService _TtsService = new TtsService();

        public MainWindow()
        {
            InitializeComponent();

            // 將自己當成 DataContext，讓 XAML 的 Command 和 ItemsSource 能找到資料
            this.DataContext = this;

            // 建立命令
            MinimizeCommand = new RelayCommand(_ => WindowState = WindowState.Minimized);
            MaximizeCommand = new RelayCommand(_ =>
            {
                WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
            });
            CloseCommand = new RelayCommand(_ => Close());

            MicrophoneCommand = new RelayCommand(async _ =>
            {
                string audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "command.wav");
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
                try
                {
                    _TtsService.Stop();
                    // 1. 錄製音訊
                    // await _GeminiService.RecordAudioAsync(audioPath, 5000);

                    // 2. 轉換為 Base64
                    string base64Audio = _GeminiService.ConvertFileToBase64(audioPath);

                    // 3. 發送給 Gemini
                    string rawJson = await _GeminiService.CallGeminiApiAsync(base64Audio, GEMINI_URL);
                    Console.WriteLine("回傳json:\n" + rawJson);

                    // 4. 解析並寫入 Config
                    if (!string.IsNullOrEmpty(rawJson))
                    {
                        string aiMessage = await Task.Run(() =>
                            _GeminiParser.ParseAndWriteConfig(rawJson, configPath));

                        if (!string.IsNullOrEmpty(aiMessage))
                        {
                            await _TtsService.SpeakAsync(aiMessage);
                        }

                        // ★★★ 寫入後，順便刷新卡片狀態 ★★★
                        RefreshAudioApps();

                        MessageBox.Show($"成功！設定已存至：\n{configPath}", "完成");
                    }
                    else
                    {
                        MessageBox.Show("API 回傳為空或是解析失敗。", "錯誤");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"發生錯誤: {ex.Message}", "例外狀況");
                }
                Console.WriteLine("Microphone button clicked!");
            });

            // 視窗拖曳功能
            this.MouseLeftButtonDown += (s, e) => {
                if (e.ButtonState == MouseButtonState.Pressed)
                    this.DragMove();
            };

            // ★★★ 初始化時先載入一次資料，這樣切換到控制頁面時才會有東西 ★★★
            RefreshAudioApps();
        }

        // ★★★ Tab 切換邏輯 (入口 / 控制) ★★★
        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            string tag = btn.Tag.ToString();

            if (tag == "Entrance")
            {
                // 1. 恢復側邊欄顯示
                SidebarBorder.Visibility = Visibility.Visible;

                // UI 狀態：入口亮
                TabEntrance.Foreground = new SolidColorBrush(Color.FromRgb(51, 153, 255));
                TabEntrance.FontWeight = FontWeights.Bold;
                LineEntrance.Visibility = Visibility.Visible;

                TabControl.Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170));
                TabControl.FontWeight = FontWeights.Normal;
                LineControl.Visibility = Visibility.Hidden;

                // View 切換
                EntranceView.Visibility = Visibility.Visible;
                ControlView.Visibility = Visibility.Hidden;
            }
            else if (tag == "Control")
            {
                // 1. 隱藏側邊欄 (這樣 ControlView 就可以佔用左邊的空間，且 Logo 不會消失)
                SidebarBorder.Visibility = Visibility.Collapsed;

                // UI 狀態：控制亮
                TabControl.Foreground = new SolidColorBrush(Color.FromRgb(51, 153, 255));
                TabControl.FontWeight = FontWeights.Bold;
                LineControl.Visibility = Visibility.Visible;

                TabEntrance.Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170));
                TabEntrance.FontWeight = FontWeights.Normal;
                LineEntrance.Visibility = Visibility.Hidden;

                // View 切換
                EntranceView.Visibility = Visibility.Hidden;
                ControlView.Visibility = Visibility.Visible;

                RefreshAudioApps();
            }
        }

        // 抽屜開關邏輯
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
                // ★★★ 修改：高度加大到 230，因為卡片變高了 (140 + padding) ★★★
                heightAnimation.To = 230;
                DrawerIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronDown;
                RefreshAudioApps();
            }

            StatusDrawer.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation);
            isDrawerOpen = !isDrawerOpen;
        }

        // ★★★ 刷新卡片列表的實作 (包含入口與控制頁面資料) ★★★
        private void RefreshAudioApps()
        {
            AppList.Clear();
            RecentAppList.Clear();

            // 1. 加入一個「整體調整」的假卡片 (模擬截圖中的藍色螢幕)
            // 這裡我們手動給它一個 Config 物件讓它邊框變色，看起來像範例圖
            var globalApp = new AudioAppModel
            {
                Name = "整體調整",
                SystemVolume = 100,
                // Icon 可以之後找個螢幕圖示，現在先用 null
                Config = new AppConfigData { TargetDevice = "System" } // 只是為了觸發藍色邊框
            };
            AppList.Add(globalApp);

            try
            {
                // 2. 嘗試抓取真實資料 (NAudio + Config)
                var sessions = _AudioService.GetAppsWithConfig();

                foreach (var app in sessions)
                {
                    AppList.Add(app);

                    // 3. 填充「最近調整」列表
                    // 邏輯：如果有被 AI 調整過 (Config != null)，或者是清單的前兩筆，就加入最近
                    if (app.Config != null || RecentAppList.Count < 2)
                    {
                        RecentAppList.Add(app);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("抓取失敗: " + ex.Message);
            }

            // 除錯：如果完全沒抓到東西 (例如靜音中)，除了整體調整外沒有別的
            if (AppList.Count <= 1)
            {
                // 可以選擇塞個假資料測試排版，或是就讓它空著
            }
        }

        // 簡單的 RelayCommand 實作
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