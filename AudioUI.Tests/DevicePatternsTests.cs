using Xunit;

namespace AudioUI.Tests
{
    public class DevicePatternsTests
    {
        private const string CableDevice = "CABLE Input (VB-Audio Virtual Cable) Speakers {0a4eba8e-e0ec-457a-90de-e84ce08d5844}";
        private const string VaioDevice = "Voicemeeter Input (VB-Audio Voicemeeter VAIO) Speakers {7bac9b47-61e4-4f81-b81b-2ad6c8186abc}";
        private const string AuxDevice = "Voicemeeter AUX Input (VB-Audio Voicemeeter AUX VAIO) Speakers {e519bb69-d01f-493d-a0b3-bc0e26557e77}";

        private static readonly string[] AllDevices = { AuxDevice, VaioDevice, CableDevice };

        [Fact]
        public void 主匯流排的短名稱在_APO_眼裡也會中_AUX()
        {
            // DevicePatterns 存在的理由：APO 的比對只有 AND，
            // 「Voicemeeter」「Input」兩個詞在 AUX 的字串裡都找得到。
            Assert.True(DependencyChecker.DeviceMatches("Voicemeeter Input", AuxDevice));
            Assert.False(DependencyChecker.DeviceMatches("Voicemeeter AUX Input", VaioDevice));
        }

        [Fact]
        public void 撞名時挑名稱裡原封不動出現樣式的那台()
        {
            Assert.Equal(VaioDevice, DevicePatterns.Resolve("Voicemeeter Input", AllDevices));
            Assert.Equal(AuxDevice, DevicePatterns.Resolve("Voicemeeter AUX Input", AllDevices));
        }

        [Fact]
        public void 挑哪一台不受列舉順序影響()
        {
            // 裝置的列舉順序會隨插拔改變，挑法不能跟著改變。
            Assert.Equal(VaioDevice, DevicePatterns.Resolve("Voicemeeter Input", new[] { AuxDevice, VaioDevice }));
            Assert.Equal(VaioDevice, DevicePatterns.Resolve("Voicemeeter Input", new[] { VaioDevice, AuxDevice }));
        }

        [Fact]
        public void 中不到裝置就是_null()
        {
            Assert.Null(DevicePatterns.Resolve("Voicemeeter Input", new[] { CableDevice }));
            Assert.Null(DevicePatterns.Resolve("Voicemeeter Input", Array.Empty<string>()));
            Assert.Null(DevicePatterns.Resolve(null, AllDevices));
        }

        [Fact]
        public void 只中一台時樣式原封不動()
        {
            Assert.Equal("CABLE Input", DevicePatterns.Specific("CABLE Input", AllDevices));
            Assert.Equal("Voicemeeter AUX Input", DevicePatterns.Specific("Voicemeeter AUX Input", AllDevices));
        }

        [Fact]
        public void 撞名時補上_GUID_讓_APO_分得開()
        {
            string pattern = DevicePatterns.Specific("Voicemeeter Input", AllDevices);

            Assert.Equal("Voicemeeter Input {7bac9b47-61e4-4f81-b81b-2ad6c8186abc}", pattern);
            Assert.True(DependencyChecker.DeviceMatches(pattern, VaioDevice));
            Assert.False(DependencyChecker.DeviceMatches(pattern, AuxDevice));
        }

        [Fact]
        public void 裝置字串裡沒有_GUID_時不亂拼一個()
        {
            var devices = new[] { "Voicemeeter Input", "Voicemeeter AUX Input" };

            // 分不開就照實留著分不開的樣式，體檢報告仍然指得出是哪一台。
            Assert.Equal("Voicemeeter Input", DevicePatterns.Specific("Voicemeeter Input", devices));
            Assert.Equal("Voicemeeter Input", DevicePatterns.Resolve("Voicemeeter Input", devices));
        }

        [Fact]
        public void 沒有裝置清單時樣式原封不動()
        {
            // 列舉不到裝置是常態（沒裝驅動、沒有權限），不能因此改寫使用者的設定。
            Assert.Equal("Voicemeeter Input", DevicePatterns.Specific("Voicemeeter Input", null));
            Assert.Equal("Voicemeeter Input", DevicePatterns.Specific("Voicemeeter Input", Array.Empty<string>()));
        }

        [Fact]
        public void 補的是_endpoint_GUID_不是裝置_id_的前半段()
        {
            // RenderDeviceIdentities 給的是「FriendlyName {0.0.0.00000000}.{endpoint guid}」，
            // 前半段每台裝置都一樣，補了等於沒補。
            var devices = new[]
            {
                "Voicemeeter Input (VB-Audio Voicemeeter VAIO) {0.0.0.00000000}.{7bac9b47-61e4-4f81-b81b-2ad6c8186abc}",
                "Voicemeeter AUX Input (VB-Audio Voicemeeter AUX VAIO) {0.0.0.00000000}.{e519bb69-d01f-493d-a0b3-bc0e26557e77}",
            };

            string pattern = DevicePatterns.Specific("Voicemeeter Input", devices);

            Assert.Equal("Voicemeeter Input {7bac9b47-61e4-4f81-b81b-2ad6c8186abc}", pattern);
            Assert.True(DependencyChecker.DeviceMatches(pattern, devices[0]));
            Assert.False(DependencyChecker.DeviceMatches(pattern, devices[1]));
        }
    }
}
