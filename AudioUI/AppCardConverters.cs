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
    /// 執行檔路徑 → 圖示。這一步留在 UI 層，<see cref="AudioAppInfo"/> 才不必認識
    /// WPF 的型別。
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
