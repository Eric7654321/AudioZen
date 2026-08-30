using Xunit;

namespace AudioUI.Tests
{
    public class DependencyCheckerTests
    {
        private const string CableDevice = "CABLE Input (VB-Audio Virtual Cable) Speakers {0a4eba8e-e0ec-457a-90de-e84ce08d5844}";
        private const string VaioDevice = "Voicemeeter Input (VB-Audio Voicemeeter VAIO) Speakers {7bac9b47-61e4-4f81-b81b-2ad6c8186abc}";

        private static RouteTable Routes(params (string id, string pattern)[] rows) =>
            new RouteTable(rows.Select(r => new AudioRoute { Id = r.id, DisplayName = r.id, DevicePattern = r.pattern }));

        [Fact]
        public void 樣式的每個字詞都要出現才算對上()
        {
            // APO 的 Device: 是子字串比對，不是精確比對——判斷方式要跟實際套用時一致。
            Assert.True(DependencyChecker.DeviceMatches("CABLE Input", CableDevice));
            Assert.True(DependencyChecker.DeviceMatches("Voicemeeter VAIO", VaioDevice));
            Assert.False(DependencyChecker.DeviceMatches("CABLE Output", CableDevice));
        }

        [Fact]
        public void 比對不分大小寫()
        {
            Assert.True(DependencyChecker.DeviceMatches("cable input", CableDevice));
        }

        [Theory]
        [InlineData(null, CableDevice)]
        [InlineData("", CableDevice)]
        [InlineData("   ", CableDevice)]
        [InlineData("CABLE Input", null)]
        [InlineData("CABLE Input", "")]
        public void 空樣式或空裝置不算對上(string? pattern, string? device)
        {
            // 空樣式若當成「符合任何裝置」，報告會在什麼都沒裝時說一切正常。
            Assert.False(DependencyChecker.DeviceMatches(pattern, device));
        }

        [Fact]
        public void 全部就位時是就緒()
        {
            var report = DependencyChecker.Check(
                apoInstalled: true,
                new[] { CableDevice, VaioDevice },
                Routes(("game", "CABLE Input"), ("browser", "Voicemeeter Input")));

            Assert.True(report.IsReady);
            Assert.Empty(report.Problems);
            Assert.All(report.Routes, r => Assert.True(r.Ok));
        }

        [Fact]
        public void 缺裝置時指名是哪一條路由()
        {
            var report = DependencyChecker.Check(
                apoInstalled: true,
                new[] { CableDevice },
                Routes(("game", "CABLE Input"), ("browser", "Voicemeeter Input")));

            Assert.False(report.IsReady);
            Assert.Single(report.Problems);
            Assert.Contains("browser", report.Problems[0]);
        }

        [Fact]
        public void 缺裝置時指得出通常是哪個產品提供的()
        {
            var report = DependencyChecker.Check(true, Array.Empty<string>(),
                Routes(("game", "CABLE Input"), ("browser", "Voicemeeter Input")));

            Assert.Contains("VB-CABLE", string.Join(" ", report.Problems));
            Assert.Contains("Voicemeeter", string.Join(" ", report.Problems));
        }

        [Fact]
        public void 認不出產品時不亂猜()
        {
            var report = DependencyChecker.Check(true, Array.Empty<string>(), Routes(("x", "Some Random Device")));

            Assert.Null(report.Routes[0].LikelyProvider);
            Assert.DoesNotContain("通常由", report.Problems[0]);
        }

        [Fact]
        public void APO_沒裝時也算問題()
        {
            var report = DependencyChecker.Check(false, new[] { CableDevice }, Routes(("game", "CABLE Input")));

            Assert.False(report.IsReady);
            Assert.Contains("Equalizer APO", report.Problems[0]);
        }

        [Fact]
        public void 對上的裝置名稱會留下來()
        {
            var report = DependencyChecker.Check(true, new[] { "別的裝置", CableDevice }, Routes(("game", "CABLE Input")));

            Assert.Equal(CableDevice, report.Routes[0].MatchedDevice);
        }

        [Fact]
        public void 什麼都沒有時不會炸()
        {
            var report = DependencyChecker.Check(false, null, null);

            Assert.False(report.IsReady);
            Assert.Empty(report.Routes);
            Assert.Single(report.Problems);
        }

        [Fact]
        public void 內建預設的三條路由在乾淨的機器上會全部報缺()
        {
            // 沒裝任何虛擬音效卡的機器上，這個程式其實一條路由都跑不動——
            // 而現在它會安靜地什麼都不做。
            var report = DependencyChecker.Check(true, new[] { "Speakers (Realtek High Definition Audio)" }, RouteTable.Default());

            Assert.False(report.IsReady);
            Assert.Equal(3, report.Problems.Count);
        }

        [Fact]
        public void Summary_講得出是就緒還是幾項問題()
        {
            Assert.Contains("就緒", DependencyChecker.Check(true, new[] { CableDevice }, Routes(("game", "CABLE Input"))).Summary);
            Assert.Contains("1 項問題", DependencyChecker.Check(false, new[] { CableDevice }, Routes(("game", "CABLE Input"))).Summary);
        }
    }
}
