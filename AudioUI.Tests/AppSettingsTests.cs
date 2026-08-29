using System.Text.Json;
using Xunit;

namespace AudioUI.Tests
{
    public class AppSettingsTests
    {
        private static AppSettings Parse(string json) =>
            JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;

        [Fact]
        public void routes_用_camelCase_讀得回來()
        {
            // 欄位名對不上時反序列化不會報錯，只會安靜地留空、退回內建預設值——
            // 使用者改了 appsettings.json 卻沒有任何效果，而且沒有錯誤訊息。
            var settings = Parse("""
                {
                  "routes": [
                    { "id": "browser", "displayName": "chrome",
                      "devicePattern": "Voicemeeter Input {guid}", "processes": [ "chrome.exe" ] }
                  ]
                }
                """);

            Assert.NotNull(settings.Routes);
            var route = Assert.Single(settings.Routes!);
            Assert.Equal("browser", route.Id);
            Assert.Equal("chrome", route.DisplayName);
            Assert.Equal("Voicemeeter Input {guid}", route.DevicePattern);
            Assert.Equal(new[] { "chrome.exe" }, route.Processes);
        }

        [Fact]
        public void apo_區塊讀得回來()
        {
            var settings = Parse("""
                { "apo": { "configDirectory": "D:\\APO", "fragmentFileName": "x.txt", "vstDirectory": "D:\\Vst" } }
                """);

            Assert.Equal(@"D:\APO", settings.Apo.ConfigDirectory);
            Assert.Equal("x.txt", settings.Apo.FragmentFileName);
            Assert.Equal(@"D:\Vst", settings.Apo.VstDirectory);
        }

        [Fact]
        public void 省略的區塊採用預設值而不是變成_null()
        {
            var settings = Parse("{}");

            Assert.Null(settings.Routes);
            Assert.Equal("audiozen.txt", settings.Apo.FragmentFileName);
            Assert.False(settings.Gemini.IsConfigured);
        }

        [Fact]
        public void 樣板檔本身讀得回來_而且三條路由都解析得到()
        {
            // 這一條擋的是「改了設定型別卻忘了同步樣板」——樣板是使用者唯一的參考。
            string path = Path.Combine(AppContext.BaseDirectory, "appsettings.example.json");
            Assert.True(File.Exists(path), $"樣板檔沒有被複製到輸出目錄：{path}");

            var settings = Parse(File.ReadAllText(path));

            Assert.NotNull(settings.Routes);
            var table = new RouteTable(settings.Routes!);
            Assert.All(new[] { "browser", "voice_chat", "game" },
                       id => Assert.NotNull(table.ResolveDevicePattern(id)));
            Assert.EndsWith("MeldaProduction", settings.Apo.VstDirectory);
            Assert.Equal("gemini-3.6-flash", settings.Gemini.Model);
        }
    }
}
