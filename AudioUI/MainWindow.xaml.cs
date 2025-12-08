using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Media.Animation;

namespace AudioUI
{
    public partial class MainWindow : Window
    {
        // 紀錄抽屜是開還是關
        private bool isDrawerOpen = false;

        public MainWindow()
        {
            InitializeComponent();

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
    }
}