using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AudioUI
{
    /// <summary>
    /// 把 WPF 的繫結診斷接到 <see cref="BindingErrorLog"/>。
    ///
    /// WPF 只有在真的有人在聽的時候才產生這些訊息，所以這個 listener 不裝，
    /// 繫結錯誤就一個字都不會出現——而它平常只寫進偵錯輸出，沒有偵錯器的時候等於不存在。
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

            // 只收 Error：Warning 裡大量是「這個屬性沒有實作 INotifyPropertyChanged」之類的
            // 常態雜訊，混進來就會重演計畫裡那次「通病蓋掉孤例」。
            source.Switch.Level = SourceLevels.Error;
        }

        /// <summary>
        /// 把結果寫成檔案。**沒有錯誤也要寫**——檔案不在代表這個檢查根本沒跑，
        /// 那跟「跑過而且乾淨」是兩件事，但少了這個檔就分不出來。
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
            if (eventType <= TraceEventType.Error) _log.Record(message);
        }

        public override void TraceEvent(TraceEventCache? cache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
        {
            if (eventType > TraceEventType.Error) return;
            _log.Record(format != null && args != null ? string.Format(format, args) : format);
        }

        // 訊息一律從 TraceEvent 進來；這兩個是 TraceListener 的必要覆寫，留空避免重複記錄。
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { }
    }
}
