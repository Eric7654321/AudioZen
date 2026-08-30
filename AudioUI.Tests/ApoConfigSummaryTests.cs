using Xunit;

namespace AudioUI.Tests
{
    public class ApoConfigSummaryTests
    {
        private const string TwoDevices = """
            Device: Voicemeeter Input VB-Audio Voicemeeter VAIO {guid-1}
            Preamp: -6 dB
            GraphicEQ: 25 -3; 40 -3; 63 -2; 100 -1; 160 0; 250 0; 400 0; 630 1; 1000 1
            VSTPlugin: C:\Program Files\Melda\MCompressor.dll

            Device: CABLE Input VB-Audio Virtual Cable {guid-2}
            Preamp: 0 dB
            """;

        [Fact]
        public void ByKeyword_依裝置切段並用子字串比對()
        {
            var map = ApoConfigSummary.ByKeyword(TwoDevices, new[] { "Voicemeeter Input", "CABLE Input" });

            Assert.Equal(2, map.Count);
            Assert.Contains("MCompressor", map["Voicemeeter Input"]);
            Assert.Contains("EQ", map["Voicemeeter Input"]);
            Assert.Contains("(-6 dB)", map["Voicemeeter Input"]);
        }

        [Fact]
        public void ByKeyword_關鍵字不必是整個裝置名()
        {
            // APO 自己就是子字串比對，語意不一致會出現「摘要說沒設定、實際上有」。
            var map = ApoConfigSummary.ByKeyword(TwoDevices, new[] { "CABLE" });

            Assert.Single(map);
            Assert.True(map.ContainsKey("CABLE"));
        }

        [Fact]
        public void ByKeyword_沒中的關鍵字不會出現()
        {
            var map = ApoConfigSummary.ByKeyword(TwoDevices, new[] { "Realtek" });
            Assert.Empty(map);
        }

        [Fact]
        public void ByKeyword_同一裝置出現兩次取第一段()
        {
            string config = """
                Device: CABLE Input {guid}
                Preamp: -6 dB

                Device: CABLE Input {guid}
                Preamp: -20 dB
                """;

            var map = ApoConfigSummary.ByKeyword(config, new[] { "CABLE Input" });

            Assert.Contains("(-6 dB)", map["CABLE Input"]);
        }

        [Fact]
        public void ByKeyword_空輸入回空字典()
        {
            Assert.Empty(ApoConfigSummary.ByKeyword(null, new[] { "CABLE" }));
            Assert.Empty(ApoConfigSummary.ByKeyword("   ", new[] { "CABLE" }));
            Assert.Empty(ApoConfigSummary.ByKeyword(TwoDevices, new string[0]));
        }

        [Fact]
        public void Describe_列出每個_VST_的檔名()
        {
            string section = """
                Device: all
                VSTPlugin: C:\Program Files\Melda\MCompressor.dll
                VSTPlugin: C:\Program Files\Melda\MCharmVerb.dll
                """;

            string text = ApoConfigSummary.Describe(section);

            Assert.Contains("MCompressor", text);
            Assert.Contains("MCharmVerb", text);
        }

        [Fact]
        public void Describe_零分貝不列出來()
        {
            // 每張卡片都掛一個 (0 dB) 只是雜訊，那代表「沒動過」。
            string section = """
                Device: all
                Preamp: 0 dB
                VSTPlugin: X\MCompressor.dll
                """;

            Assert.DoesNotContain("dB", ApoConfigSummary.Describe(section));
        }

        [Fact]
        public void Describe_什麼都認不出來時說自訂設定()
        {
            Assert.Equal("自訂設定", ApoConfigSummary.Describe("Device: all\nCopy: L=R"));
        }

        [Fact]
        public void Describe_空的就是無效果()
        {
            Assert.Equal("無效果", ApoConfigSummary.Describe(""));
            Assert.Equal("無效果", ApoConfigSummary.Describe(null));
            Assert.Equal("無效果", ApoConfigSummary.Describe("無"));
        }
    }
}
