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
    public partial class MainWindow : Window
    {
        // 紀錄抽屜是開還是關
        private bool isDrawerOpen = false;

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

            // 將自己當成 DataContext，讓 XAML 的 Command binding 能找到命令
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
                // 這裡放點擊麥克風按鈕要做的事
                string audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "command.wav");
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
                try
                {
                    _TtsService.Stop();
                    // 1. 錄製音訊 (5秒)
                    // StatusLabel.Content = "錄音中..."; 
                    //await _GeminiService.RecordAudioAsync(audioPath, 5000);

                    // 2. 轉換為 Base64
                    // StatusLabel.Content = "處理中...";
                    string base64Audio = _GeminiService.ConvertFileToBase64("fixedCommand.wav"); // TODO: 要改回用audioPath

                    // 3. 發送給 Gemini 並取得 JSON
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
                            // 也可以順便 Show 在 UI 上
                        }
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
                finally
                {
                    // StatusLabel.Content = "就緒";
                }
                Console.WriteLine("Microphone button clicked!");
            });

            // 視窗拖曳功能
            this.MouseLeftButtonDown += (s, e) => {
                if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                    this.DragMove();
            };
        }

        // 抽屜開關邏輯
        private void DrawerBtn_Click(object sender, RoutedEventArgs e)
        {
            // 1. 定義動畫 (DoubleAnimation 用來改變數值)
            DoubleAnimation heightAnimation = new DoubleAnimation();
            heightAnimation.Duration = TimeSpan.FromSeconds(0.3); // 動畫時間 0.3秒
            heightAnimation.EasingFunction = new QuadraticEase(); // 加個緩動效果比較順滑

            if (isDrawerOpen)
            {
                // 如果是開的 -> 關起來 (高度變 0)
                heightAnimation.To = 0;
                DrawerIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronUp; // 箭頭朝上
            }
            else
            {
                // 如果是關的 -> 打開 (高度變 150，你可以自己調)
                heightAnimation.To = 150;
                DrawerIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronDown; // 箭頭朝下
            }

            // 2. 開始播放動畫 (針對 StatusDrawer 的 Height 屬性)
            StatusDrawer.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation);

            // 切換狀態
            isDrawerOpen = !isDrawerOpen;
        }

        // 簡單的 RelayCommand 實作
        private class RelayCommand : ICommand
        {
            private readonly Action<object?> _execute; // 預設為 NULL object
            private readonly Func<object?, bool>? _canExecute;

            public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true; // 如果沒有提供 canExecute，預設回傳 true

            public void Execute(object? parameter) => _execute(parameter);

            public event EventHandler? CanExecuteChanged
            {
                add => CommandManager.RequerySuggested += value;
                remove => CommandManager.RequerySuggested -= value;
            }
        }
    }
}