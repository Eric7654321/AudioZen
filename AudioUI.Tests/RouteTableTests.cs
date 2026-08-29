using Xunit;

namespace AudioUI.Tests
{
    public class RouteTableTests
    {
        private static RouteTable Table() => new RouteTable(new[]
        {
            new AudioRoute { Id = "browser", DisplayName = "chrome",
                             DevicePattern = "Voicemeeter Input VB-Audio Voicemeeter VAIO {guid-1}",
                             Processes = { "chrome.exe" } },
            new AudioRoute { Id = "game", DisplayName = "games",
                             DevicePattern = "CABLE Input VB-Audio Virtual Cable {guid-2}",
                             Processes = { "eldenring.exe", "VALORANT-Win64-Shipping.exe" } },
        });

        [Fact]
        public void ResolveDevicePattern_翻譯邏輯代號()
        {
            Assert.Equal("Voicemeeter Input VB-Audio Voicemeeter VAIO {guid-1}", Table().ResolveDevicePattern("browser"));
        }

        [Fact]
        public void ResolveDevicePattern_全域目標原樣通過()
        {
            // "all" 是 APO 自己認得的字，不該被當成查不到的路由。
            Assert.Equal("all", Table().ResolveDevicePattern("all"));
        }

        [Fact]
        public void ResolveDevicePattern_認不得的目標回_null()
        {
            Assert.Null(Table().ResolveDevicePattern("spotify"));
            Assert.Null(Table().ResolveDevicePattern(null));
        }

        [Theory]
        [InlineData("chrome.exe")]
        [InlineData("chrome")]
        [InlineData("CHROME.EXE")]
        public void ByProcess_忽略副檔名與大小寫(string name)
        {
            Assert.Equal("browser", Table().ByProcess(name)?.Id);
        }

        [Fact]
        public void ByDeviceLine_用關鍵字反查設定檔裡的裝置行()
        {
            var route = Table().ByDeviceLine("Voicemeeter Input VB-Audio Voicemeeter VAIO {某台機器不同的 guid}");
            Assert.Equal("browser", route?.Id);
        }

        [Fact]
        public void MatchKeyword_省略時取前兩個字詞()
        {
            // 裝置全名逐機不同，但前兩個字詞夠穩定，足以認出是哪條路由。
            Assert.Equal("Voicemeeter Input", Table().ById("browser")!.MatchKeyword);
            Assert.Equal("CABLE Input", Table().ById("game")!.MatchKeyword);
        }

        [Fact]
        public void PromptTargetLines_從第二項開始編號_因為第一項是_all()
        {
            Assert.Equal("2. 'browser': chrome.exe 3. 'game': eldenring.exe, VALORANT-Win64-Shipping.exe ",
                         Table().PromptTargetLines());
        }

        [Fact]
        public void PromptTargetUnion_含全域目標()
        {
            Assert.Equal("\"all\"|\"browser\"|\"game\"", Table().PromptTargetUnion());
        }

        [Fact]
        public void TargetCount_把全域目標算進去()
        {
            Assert.Equal(3, Table().TargetCount);
        }

        [Fact]
        public void 內建預設值的三條路由都解析得到()
        {
            var d = RouteTable.Default();
            Assert.All(new[] { "browser", "voice_chat", "game" }, id => Assert.NotNull(d.ResolveDevicePattern(id)));
        }
    }
}
