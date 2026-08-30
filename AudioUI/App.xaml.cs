using System.Windows;

namespace AudioUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        /// <summary>這次執行看到的繫結錯誤。設定頁與結束時的報告都讀這一份。</summary>
        public static BindingErrorLog BindingErrors { get; } = new BindingErrorLog();

        protected override void OnStartup(StartupEventArgs e)
        {
            // 要在第一個視窗建起來之前裝，否則啟動時解不開的那些繫結收不到。
            BindingErrorListener.Install(BindingErrors);
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            BindingErrorListener.Flush(BindingErrors);
            base.OnExit(e);
        }
    }
}
