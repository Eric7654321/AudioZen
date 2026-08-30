using Xunit;

namespace AudioUI.Tests
{
    public class DeviceImageTests
    {
        private static List<DeviceInfoModel> Devices() => new List<DeviceInfoModel>
        {
            new DeviceInfoModel { Name = "自定義宏鍵盤", ImagePath = "keyboard.png" },
            new DeviceInfoModel { Name = "g304", ImagePath = "mouse.png" },
        };

        [Fact]
        public void 沒設定過的裝置沿用內建的圖()
        {
            var devices = Devices();
            new UserPreferences().ApplyDeviceImages(devices);

            Assert.Equal("keyboard.png", devices[0].ImagePath);
            Assert.Equal("mouse.png", devices[1].ImagePath);
        }

        [Fact]
        public void 自訂圖蓋掉內建的_其他裝置不受影響()
        {
            var prefs = new UserPreferences();
            prefs.SetDeviceImage("g304", @"D:\pics\my-mouse.png");

            var devices = Devices();
            prefs.ApplyDeviceImages(devices);

            Assert.Equal("keyboard.png", devices[0].ImagePath);
            Assert.Equal(@"D:\pics\my-mouse.png", devices[1].ImagePath);
        }

        [Fact]
        public void 傳空路徑等於還原成內建的圖()
        {
            var prefs = new UserPreferences();
            prefs.SetDeviceImage("g304", @"D:\pics\my-mouse.png");
            Assert.True(prefs.SetDeviceImage("g304", ""));

            var devices = Devices();
            prefs.ApplyDeviceImages(devices);

            Assert.Equal("mouse.png", devices[1].ImagePath);
            Assert.Empty(prefs.DeviceImages);
        }

        [Fact]
        public void 沒設定過的裝置還原不會炸()
        {
            Assert.False(new UserPreferences().SetDeviceImage("不存在", null));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void 裝置名稱是空的就不收(string? name)
        {
            var prefs = new UserPreferences();

            Assert.False(prefs.SetDeviceImage(name, "x.png"));
            Assert.Null(prefs.DeviceImage(name));
            Assert.Empty(prefs.DeviceImages);
        }

        [Fact]
        public void ApplyDeviceImages_收到_null_不會炸()
        {
            new UserPreferences().ApplyDeviceImages(null);
        }

        [Fact]
        public void 沒有圖的裝置留在_null_讓畫面顯示佔位圖示()
        {
            // MainWindow.xaml 的 fallback 比對的是 {x:Null}，空字串不會觸發。
            Assert.Null(new DeviceInfoModel().ImagePath);
        }

        [Fact]
        public void 自訂圖跟著偏好一起存檔()
        {
            using var dir = new TempDir();
            var store = new JsonPreferencesStore(new FakeNotifier(), dir.File("preferences.json"));
            store.Current.SetDeviceImage("g304", @"D:\pics\my-mouse.png");
            store.Save();

            var reopened = new JsonPreferencesStore(new FakeNotifier(), dir.File("preferences.json"));
            reopened.Load();

            Assert.Equal(@"D:\pics\my-mouse.png", reopened.Current.DeviceImage("g304"));
        }
    }
}
