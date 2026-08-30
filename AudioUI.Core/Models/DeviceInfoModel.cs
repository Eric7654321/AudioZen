using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioUI
{
    public class DeviceInfoModel
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        /// <summary>
        /// 卡片上的圖。可以是建置進去的資源名（"keyboard.png"）或使用者選的磁碟絕對路徑，
        /// Image.Source 兩種都吃得下。
        ///
        /// 預設是 null 而不是空字串：MainWindow.xaml 的 fallback 比對的是 {x:Null}，
        /// 給空字串的話沒有圖的卡片會是一片空白，而不是預期的佔位圖示。
        /// </summary>
        public string? ImagePath { get; set; }
    }
}
