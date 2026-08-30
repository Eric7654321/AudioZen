using Microsoft.Toolkit.Uwp.Notifications;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AudioUI
{
    /// <summary>
    /// 列出各 app 目前音量與效果的 toast。留在 UI 層而不是 Infra，因為它讀的是 WPF 的
    /// <see cref="ImageSource"/>——把 WPF 型別帶進 Infra 就等於把那層的平台無關性讓掉了。
    /// </summary>
    public sealed class AppListNotifier : IAppStateNotifier
    {
        private readonly AudioSessionService _sessions = new AudioSessionService();

        /// <summary>流程層透過這個入口叫用，所以「要列哪些 app」的知識不必外流到 Core。</summary>
        public void ShowCurrentApps() => ShowAppNotification(_sessions.GetAppsWithConfig());

private string? SaveImageToTempFile(ImageSource? imageSource, string appName)
        {
            if (imageSource is not BitmapSource bitmapSource) return null;

            try
            {
                // 清理檔名中的非法字元
                string safeName = string.Join("_", appName.Split(Path.GetInvalidFileNameChars()));
                string tempPath = Path.Combine(Path.GetTempPath(), $"AudioNotify_{safeName}.png");

                // 如果檔案已存在且很新，可以考慮不重寫 (這裡為了簡單每次都寫入)
                using (var fileStream = new FileStream(tempPath, FileMode.Create))
                {
                    BitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                    encoder.Save(fileStream);
                }
                return tempPath;
            }
            catch
            {
                return null; // 存檔失敗回傳 null，之後會顯示預設圖示或空白
            }
        }

public void ShowAppNotification(IEnumerable<AudioAppModel> apps)
        {
            if (apps == null || !apps.Any()) return;

            var builder = new ToastContentBuilder()
                .AddText("心平氣和")
                .AddText("應用程式狀態列表");

            foreach (var app in apps.Take(5)) // 因為變小了，可以顯示更多個 (例如 5 個)
            {
                string iconPath = SaveImageToTempFile(app.Icon, app.Name) ?? "";

                // --- 策略 1: 合併資訊字串 ---
                // 範例效果: "🔊 80%  |  ✅ 已套用設定  |  🔇 靜音"
                var infoParts = new List<string>();
                infoParts.Add($"🔊 {app.SystemVolume}%");
                infoParts.Add(app.CurrentEffectInfo ?? "未設定");
                if (app.SystemMute) infoParts.Add("🔇 靜音");

                // 使用 " | " 符號將資訊串接成單一行
                string combinedInfo = string.Join("  |  ", infoParts);

                var group = new AdaptiveGroup()
                {
                    Children =
            {
                // 左欄：圖示 (縮小寬度權重)
                new AdaptiveSubgroup()
                {
                    HintWeight = 1,
                    Children =
                    {
                        new AdaptiveImage()
                        {
                            Source = iconPath,
                            HintAlign = AdaptiveImageAlign.Center,
                            // HintCrop = AdaptiveImageCrop.Circle // 視喜好決定是否圓形
                        }
                    }
                },

                // 右欄：文字
                new AdaptiveSubgroup()
                {
                    HintWeight = 4, // 給文字更多空間
                    HintTextStacking = AdaptiveSubgroupTextStacking.Center, // 讓文字垂直置中對齊圖片
                    Children =
                    {
                        // --- 策略 2: 縮小標題 ---
                        // 使用 Base 搭配 Bold，比 Subtitle 更省空間但依然明顯
                        new AdaptiveText()
                        {
                            Text = app.Name,
                            HintStyle = AdaptiveTextStyle.Base,
                        },

                        // --- 策略 3: 單行顯示詳細資訊 ---
                        // CaptionSubtle 是最小的灰色字體
                        new AdaptiveText()
                        {
                            Text = combinedInfo,
                            HintStyle = AdaptiveTextStyle.CaptionSubtle,
                            HintWrap = true // 允許換行 (如果視窗太窄)
                        }
                    }
                }
            }
                };

                builder.AddVisualChild(group);
            }

            if (apps.Count() > 5)
            {
                builder.AddText($"... 還有 {apps.Count() - 5} 個應用程式");
            }

            builder.Show(toast =>
            {
                toast.Tag = "AudioAppsCompact";
                toast.Group = "AudioMonitor";
                toast.ExpirationTime = DateTimeOffset.Now.AddSeconds(5);
            });
        }
    }
}
