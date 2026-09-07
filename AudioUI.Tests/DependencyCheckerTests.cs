using Xunit;

namespace AudioUI.Tests
{
    public class DependencyCheckerTests
    {
        private const string CableDevice = "CABLE Input (VB-Audio Virtual Cable) Speakers {0a4eba8e-e0ec-457a-90de-e84ce08d5844}";
        private const string VaioDevice = "Voicemeeter Input (VB-Audio Voicemeeter VAIO) Speakers {7bac9b47-61e4-4f81-b81b-2ad6c8186abc}";
        private const string AuxDevice = "Voicemeeter AUX Input (VB-Audio Voicemeeter AUX VAIO) Speakers {e519bb69-d01f-493d-a0b3-bc0e26557e77}";

        private static RouteTable Routes(params (string id, string pattern)[] rows) =>
            new RouteTable(rows.Select(r => new AudioRoute { Id = r.id, DisplayName = r.id, DevicePattern = r.pattern }));

        private static DependencySnapshot CompleteSnapshot(bool apoInstalled = true, IReadOnlyList<string>? devices = null) => new DependencySnapshot
        {
            ApoInstalled = apoInstalled,
            CompressorInstalled = true,
            ReverbInstalled = true,
            ZhTwSpeechInstalled = true,
            ApiKeyConfigured = true,
            RenderDevices = devices ?? new[] { VaioDevice, AuxDevice, CableDevice },
        };

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
                CompleteSnapshot(),
                Routes(("game", "CABLE Input"), ("browser", "Voicemeeter Input")));

            Assert.True(report.IsReady);
            Assert.Empty(report.Problems);
            Assert.All(report.Routes, r => Assert.True(r.Ok));
        }

        [Fact]
        public void 缺裝置時指名是哪一條路由()
        {
            var report = DependencyChecker.Check(
                CompleteSnapshot(devices: new[] { CableDevice }),
                Routes(("game", "CABLE Input"), ("browser", "Voicemeeter Input")));

            Assert.False(report.IsReady);
            Assert.Equal(2, report.Problems.Count);
            Assert.Contains("browser", report.Items.Single(i => i.Kind == DependencyKind.RouteConfiguration).Detail);
        }

        [Fact]
        public void 缺裝置時指得出通常是哪個產品提供的()
        {
            var report = DependencyChecker.Check(CompleteSnapshot(devices: Array.Empty<string>()),
                Routes(("game", "CABLE Input"), ("browser", "Voicemeeter Input")));

            Assert.Contains("VB-CABLE", string.Join(" ", report.Problems));
            Assert.Contains("Voicemeeter", string.Join(" ", report.Problems));
        }

        [Fact]
        public void 認不出產品時不亂猜()
        {
            var report = DependencyChecker.Check(CompleteSnapshot(devices: Array.Empty<string>()), Routes(("x", "Some Random Device")));

            Assert.Null(report.Routes[0].LikelyProvider);
            Assert.DoesNotContain("通常由", report.Items.Single(i => i.Kind == DependencyKind.RouteConfiguration).Detail);
        }

        [Fact]
        public void APO_沒裝時也算問題()
        {
            var report = DependencyChecker.Check(CompleteSnapshot(apoInstalled: false), Routes(("game", "CABLE Input")));

            Assert.False(report.IsReady);
            Assert.Contains("Equalizer APO", report.Problems[0]);
        }

        [Fact]
        public void 對上的裝置名稱會留下來()
        {
            var report = DependencyChecker.Check(CompleteSnapshot(devices: new[] { "別的裝置", CableDevice }), Routes(("game", "CABLE Input")));

            Assert.Equal(CableDevice, report.Routes[0].MatchedDevice);
        }

        [Fact]
        public void 什麼都沒有時不會炸()
        {
            var report = DependencyChecker.Check(new DependencySnapshot(), null);

            Assert.False(report.IsReady);
            Assert.Empty(report.Routes);
            Assert.Equal(4, report.Problems.Count);
        }

        [Fact]
        public void 內建預設的三條路由在乾淨的機器上會全部報缺()
        {
            // 沒裝任何虛擬音效卡的機器上，它應該要回報無法配對。
            var report = DependencyChecker.Check(CompleteSnapshot(devices: new[] { "Speakers (Realtek High Definition Audio)" }), RouteTable.Default());

            Assert.False(report.IsReady);
            Assert.Equal(3, report.Problems.Count);
        }

        [Fact]
        public void Summary_講得出是就緒還是幾項問題()
        {
            Assert.Contains("就緒", DependencyChecker.Check(CompleteSnapshot(), Routes(("game", "CABLE Input"))).Summary);
            Assert.Contains("1 個必要項目", DependencyChecker.Check(CompleteSnapshot(apoInstalled: false), Routes(("game", "CABLE Input"))).Summary);
        }

        [Fact]
        public void 安裝精靈一次整理必要與選用項目()
        {
            var report = DependencyChecker.Check(
                CompleteSnapshot(),
                Routes(("browser", VaioDevice), ("voice_chat", AuxDevice), ("game", CableDevice)));

            Assert.True(report.IsReady);
            Assert.Equal(7, report.Items.Count);
            Assert.All(report.Items, item => Assert.True(item.IsReady));
            Assert.Equal("全部就緒", report.Summary);
        }

        [Fact]
        public void 缺選用功能不會擋住必要項目就緒()
        {
            var complete = CompleteSnapshot();
            var snapshot = new DependencySnapshot
            {
                ApoInstalled = complete.ApoInstalled,
                ApiKeyConfigured = complete.ApiKeyConfigured,
                RenderDevices = complete.RenderDevices,
            };

            var report = DependencyChecker.Check(
                snapshot,
                Routes(("browser", VaioDevice), ("voice_chat", AuxDevice), ("game", CableDevice)));

            Assert.True(report.IsReady);
            Assert.Equal(2, report.Items.Count(item => item.IsOptional && !item.IsReady));
            Assert.Contains("2 個選用項目", report.Summary);
        }

        [Fact]
        public void 缺_API_key_會列成一個必要項目()
        {
            var complete = CompleteSnapshot();
            var snapshot = new DependencySnapshot
            {
                ApoInstalled = true,
                CompressorInstalled = true,
                ReverbInstalled = true,
                ZhTwSpeechInstalled = true,
                ApiKeyConfigured = false,
                RenderDevices = complete.RenderDevices,
            };

            var report = DependencyChecker.Check(
                snapshot,
                Routes(("browser", VaioDevice), ("voice_chat", AuxDevice), ("game", CableDevice)));

            Assert.False(report.IsReady);
            Assert.Equal(DependencyKind.GeminiApiKey,
                Assert.Single(report.Items, item => item.IsRequired && !item.IsReady).Kind);
        }

        [Fact]
        public void 只有_AUX_裝置不等於完整的_Voicemeeter()
        {
            var snapshot = new DependencySnapshot
            {
                ApoInstalled = true,
                CompressorInstalled = true,
                ReverbInstalled = true,
                ZhTwSpeechInstalled = true,
                ApiKeyConfigured = true,
                RenderDevices = new[] { AuxDevice, CableDevice },
            };

            var report = DependencyChecker.Check(
                snapshot,
                Routes(("voice_chat", AuxDevice), ("game", CableDevice)));

            Assert.False(report.Items.Single(item => item.Kind == DependencyKind.Voicemeeter).IsReady);
        }

        [Fact]
        public void 驅動都在但路由配不到時只需修正路由設定()
        {
            var report = DependencyChecker.Check(
                CompleteSnapshot(),
                Routes(("browser", "不存在的裝置")));

            Assert.True(report.Items.Single(item => item.Kind == DependencyKind.Voicemeeter).IsReady);
            Assert.True(report.Items.Single(item => item.Kind == DependencyKind.VbCable).IsReady);
            Assert.False(report.Items.Single(item => item.Kind == DependencyKind.RouteConfiguration).IsReady);
        }

        [Fact]
        public void 主匯流排與_AUX_各自對到自己那台_不看列舉順序()
        {
            // 「Voicemeeter Input」在 APO 眼裡也會中 AUX，所以挑法不能取決於裝置的列舉順序。
            foreach (var devices in new[]
            {
                new[] { VaioDevice, AuxDevice, CableDevice },
                new[] { AuxDevice, VaioDevice, CableDevice },
            })
            {
                var report = DependencyChecker.Check(
                    CompleteSnapshot(devices: devices),
                    Routes(("browser", "Voicemeeter Input"), ("voice_chat", "Voicemeeter AUX Input")));

                Assert.Equal(VaioDevice, report.Routes.Single(r => r.RouteId == "browser").MatchedDevice);
                Assert.Equal(AuxDevice, report.Routes.Single(r => r.RouteId == "voice_chat").MatchedDevice);
            }
        }
    }
}
