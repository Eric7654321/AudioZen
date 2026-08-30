using System;
using System.Globalization;
using System.Windows.Data;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace AudioUI
{
    /// <summary>
    /// 執行檔路徑 → 圖示。
    ///
    /// 這一步刻意留在 UI 層：<see cref="AudioAppInfo"/> 帶著 ImageSource 的話，
    /// 產生它的服務與拿著它的 ViewModel 就都只能住在 WPF 裡。
    /// </summary>
    public sealed class AppIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            AppIcons.Load(value as string);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>有沒有被設定過 → 卡片框線。</summary>
    public sealed class ConfigBorderConverter : IValueConverter
    {
        private static readonly Brush Configured = Brushes.CornflowerBlue;

        // 凍住，才不會每張卡片每次重整都做一個新的筆刷。
        private static readonly Brush Plain = CreateFrozen(Color.FromRgb(85, 85, 85));

        private static Brush CreateFrozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? Configured : Plain;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
