using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AudioUI
{
    /// <summary>
    /// 從執行檔路徑抽出圖示。抽圖示是 shell 的 P/Invoke，而清單每次重整都會把每個程式
    /// 重走一遍，所以照路徑快取。
    ///
    /// 抽完就 Freeze：通知列不在 UI 執行緒上跑，沒凍住的 BitmapSource 換執行緒用會炸。
    /// </summary>
    internal static class AppIcons
    {
        private static readonly Dictionary<string, ImageSource?> _cache =
            new Dictionary<string, ImageSource?>(StringComparer.OrdinalIgnoreCase);

        public static ImageSource? Load(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            lock (_cache)
            {
                if (_cache.TryGetValue(path, out var cached)) return cached;
            }

            ImageSource? icon = Extract(path);

            lock (_cache)
            {
                // 抽不到也記下來，否則每次重整都會為同一個路徑再試一次。
                _cache[path] = icon;
            }
            return icon;
        }

        private static ImageSource? Extract(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;

                var source = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            catch { return null; }
        }
    }
}
