using Xunit;

namespace AudioUI.Tests
{
    public class DspPresetTests
    {
        public static IEnumerable<object[]> AllPresets() =>
            CompressorPresets.All.Concat(ReverbPresets.All).Select(p => new object[] { p });

        [Fact]
        public void 代號在各自的清單裡不重複()
        {
            Assert.Equal(CompressorPresets.All.Count, CompressorPresets.All.Select(p => p.Id).Distinct().Count());
            Assert.Equal(ReverbPresets.All.Count, ReverbPresets.All.Select(p => p.Id).Distinct().Count());
        }

        [Fact]
        public void 認不得的代號回_無_而不是_null()
        {
            // 存檔裡留著一個已經被拿掉的 preset 代號時，使用者該拿到「沒有效果」而不是崩潰。
            Assert.True(CompressorPresets.ById("已經不存在").IsOff);
            Assert.True(ReverbPresets.ById(null).IsOff);
        }

        [Fact]
        public void 只有無是關掉的()
        {
            Assert.True(CompressorPresets.Off.IsOff);
            Assert.All(CompressorPresets.All.Where(p => p.Id != "off"), p => Assert.False(p.IsOff));
            Assert.All(ReverbPresets.All.Where(p => p.Id != "off"), p => Assert.False(p.IsOff));
        }

        [Fact]
        public void hi_fi_的四種空間都在殘響裡()
        {
            // Reflect Type 在 CharmVerb 沒有對應的 key，那四個選項只能是四組數值。
            foreach (string name in new[] { "Chamber", "Studio", "Cave", "Hall" })
                Assert.Contains(ReverbPresets.All, p => p.Name == name);
        }

        [Theory]
        [MemberData(nameof(AllPresets))]
        public void 每個值都是編碼器認得的型別(DspPreset preset)
        {
            // MeldaEncoder 的型別判斷是一串 if，掉出去的 entry 會被靜靜跳過，
            // 產出一份少了欄位、看起來卻正常的 chunk。
            foreach (var entry in preset.Entries ?? new List<MeldaEntry>())
            {
                object? v = entry.Value;
                bool encodable = v is null || v is double || v is float || v is bool
                                 || v is int || v is long || v is short || v is byte || v is uint || v is ulong
                                 || v is string;
                Assert.True(encodable, $"{preset.Name} 的 {entry.RawKey} 是 {v?.GetType().Name ?? "null"}");
            }
        }

        [Theory]
        [MemberData(nameof(AllPresets))]
        public void 有內容的_preset_編得出_base64(DspPreset preset)
        {
            // 「無」與「維持目前」都沒有參數可編：前者是不要效果，後者是別動設定檔裡那份。
            if (preset.Entries == null) return;

            string header = CompressorPresets.All.Contains(preset)
                ? CompressorPresets.ChunkHeader
                : ReverbPresets.ChunkHeader;

            string chunk = MeldaEncoder.EncodeMeldaChunk(header, preset.ToList()!);

            Assert.False(string.IsNullOrWhiteSpace(chunk));
            Convert.FromBase64String(chunk);
        }

        [Fact]
        public void 壓縮器的必要欄位都在()
        {
            var keys = CompressorPresets.Medium.Entries!.Select(e => e.RawKey).ToList();
            foreach (string k in new[] { "A#threshold", "A#ratio", "A#attack", "A#release", "A#kneesize", "A#kneemode" })
                Assert.Contains(k, keys);
        }

        [Fact]
        public void 防爆麥壓得比輕度更用力()
        {
            // 這個 preset 存在的理由就是 hi-fi 那句「有人爆麥了」。
            double Ratio(DspPreset p) => (double)p.Entries!.First(e => e.RawKey == "A#ratio").Value;
            double Threshold(DspPreset p) => (double)p.Entries!.First(e => e.RawKey == "A#threshold").Value;

            Assert.True(Ratio(CompressorPresets.Shout) > Ratio(CompressorPresets.Light));
            Assert.True(Threshold(CompressorPresets.Shout) < Threshold(CompressorPresets.Light));
        }

        [Fact]
        public void 洞穴比錄音室大也比較長()
        {
            double Of(DspPreset p, string key) => (double)p.Entries!.First(e => e.RawKey == key).Value;

            Assert.True(Of(ReverbPresets.Cave, "A#Size") > Of(ReverbPresets.Studio, "A#Size"));
            Assert.True(Of(ReverbPresets.Cave, "A#Length") > Of(ReverbPresets.Studio, "A#Length"));
        }

        [Fact]
        public void ToList_拿到的是複本_改它不會汙染_preset()
        {
            var first = CompressorPresets.Medium.ToList()!;
            first.Clear();

            Assert.NotEmpty(CompressorPresets.Medium.ToList()!);
        }

        [Fact]
        public void ToTargetConfig_把兩個效果一起帶出去()
        {
            var config = TonePresets.ToTargetConfig("game", new double[7], 100,
                                                    CompressorPresets.Shout, ReverbPresets.Hall);

            Assert.NotNull(config.CompJson);
            Assert.NotNull(config.ReverbJson);
            Assert.Equal("game", config.Target);
        }

        [Fact]
        public void 選了無就不寫那個效果()
        {
            var config = TonePresets.ToTargetConfig("game", new double[7], 100,
                                                    CompressorPresets.Off, ReverbPresets.Off);

            Assert.Null(config.CompJson);
            Assert.Null(config.ReverbJson);
        }
    }
}
