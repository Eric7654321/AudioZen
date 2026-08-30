using Xunit;

namespace AudioUI.Tests
{
    public class AudioAppInfoTests
    {
        [Fact]
        public void 沒有設定時顯示系統音量()
        {
            var app = new AudioAppInfo { Name = "Chrome", SystemVolume = 80 };

            Assert.False(app.HasConfig);
            Assert.Equal("音量縮放: 80%", app.VolumeText);
            Assert.Equal("音色調整: 無", app.ToneText);
            Assert.Equal("其他: 正常", app.OtherText);
        }

        [Fact]
        public void 沒有設定且被靜音時講出來()
        {
            var app = new AudioAppInfo { SystemVolume = 80, SystemMute = true };
            Assert.Equal("其他: 靜音", app.OtherText);
        }

        [Fact]
        public void 有設定時音量看的是縮放比例不是系統音量()
        {
            var app = new AudioAppInfo
            {
                SystemVolume = 80,
                Config = new AppConfigData { VolumeScale = 0.5, Effect = "EQ + Reverb", TargetDevice = "CABLE Input" }
            };

            Assert.True(app.HasConfig);
            Assert.Equal("音量縮放: 50% (AI)", app.VolumeText);
            Assert.Equal("音色調整: EQ + Reverb", app.ToneText);
            Assert.Equal("路由: CABLE Input", app.OtherText);
        }

        [Fact]
        public void 有設定時靜音不再蓋掉路由()
        {
            // 有設定的卡片要顯示路由，靜音是系統層的事，兩者不是同一格。
            var app = new AudioAppInfo
            {
                SystemMute = true,
                Config = new AppConfigData { TargetDevice = "System" }
            };

            Assert.Equal("路由: System", app.OtherText);
        }
    }
}
