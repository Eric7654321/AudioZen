using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AudioUI
{
    /// <summary>
    /// 把 WPF 的繫結診斷接到 <see cref="BindingErrorLog"/>。
    ///
    /// WPF 只有在有 listener 在聽的時候才產生這些訊息，不裝就一個字都不會出現。
    /// </summary>
    internal sealed class BindingErrorListener : TraceListener
    {
        private readonly BindingErrorLog _log;

        private BindingErrorListener(BindingErrorLog log) => _log = log;

        /// <summary>開始收集。呼叫前發生的繫結錯誤收不到，所以要在建視窗之前裝。</summary>
        public static void Install(BindingErrorLog log)
        {
            // 沒有這一行，下面設的 Switch 等級不會生效。
            PresentationTraceSources.Refresh();

            var source = PresentationTraceSources.DataBindingSource;
            source.Listeners.Add(new BindingErrorListener(log));

            // 只收 Error。Warning 大量是「這個屬性沒有實作 INotifyPropertyChanged」
            // 之類的常態雜訊，混進來會把真正的錯誤蓋掉。
            source.Switch.Level = SourceLevels.Error;
        }

        /// <summary>
        /// 把結果寫成檔案。沒有錯誤也要寫：檔案不在代表這個檢查沒跑，
        /// 那跟「跑過而且乾淨」是兩回事，少了這個檔就分不出來。
        /// </summary>
        public static string Flush(BindingErrorLog log)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            string path = Path.Combine(dir, "binding-errors.log");
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{log.Report()}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"繫結報告寫不出去：{ex.Message}");
            }
            return path;
        }

        public override void TraceEvent(TraceEventCache? cache, string source, TraceEventType eventType, int id, string? message)
        {
            if (eventType <= TraceEventType.Error) Take(message);
        }

        public override void TraceEvent(TraceEventCache? cache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
        {
            if (eventType > TraceEventType.Error) return;
            Take(format != null && args != null ? string.Format(format, args) : format);
        }

        /// <summary>
        /// 看到沒見過的錯誤就把報告重寫一次。切到沒去過的分頁才長出來的元素要到那時候
        /// 才會報錯，只在結束時寫的話，當機或被強制結束就什麼都不會留下。
        ///
        /// 只在「第一次看到」時寫，所以次數有上限，不會變成每畫一幀寫一次檔。
        /// </summary>
        private void Take(string? message)
        {
            if (_log.Record(message)) Flush(_log);
        }

        // 訊息一律從 TraceEvent 進來；這兩個是 TraceListener 的必要覆寫，留空避免重複記錄。
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { }
    }
}
