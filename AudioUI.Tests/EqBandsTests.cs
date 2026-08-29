using System.Globalization;
using AudioUI;
using Xunit;

namespace AudioUI.Tests
{
    public class EqBandsTests
    {
        [Fact]
        public void 每個頻率點都恰好落在一個頻段裡()
        {
            foreach (int hz in EqBands.Frequencies)
            {
                int hits = EqBands.All.Count(b => b.Contains(hz));
                Assert.True(hits == 1, $"{hz} Hz 落在 {hits} 個頻段");
            }
        }

        [Fact]
        public void 最高的那一段有點可以動()
        {
            // 20000 是為了這件事才加進點位清單的：只有 16000 的話這根滑桿動了也不會改變任何點。
            var top = EqBands.All[^1];
            Assert.True(EqBands.Frequencies.Count(top.Contains) >= 2);
        }

        [Fact]
        public void Format_把每個點都寫出來()
        {
            string s = EqBands.Format(new double[7]);
            Assert.Equal(EqBands.Frequencies.Count, s.Split(';').Length);
            Assert.StartsWith("25 0", s);
        }

        [Fact]
        public void Format_同一段裡的點拿到同樣的增益()
        {
            // 第一段涵蓋 25~160。
            var gains = new double[7];
            gains[0] = 3;
            string s = EqBands.Format(gains);

            Assert.Contains("25 3", s);
            Assert.Contains("160 3", s);
            Assert.Contains("250 0", s);
        }

        [Fact]
        public void Format_不受地區設定影響()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                // 小數點是逗號的地區會把 "1,5" 寫進設定檔，APO 讀到的是兩個欄位。
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var gains = new double[7];
                gains[0] = 1.5;
                Assert.Contains("25 1.5", EqBands.Format(gains));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void Format_超出範圍的增益會被夾回去()
        {
            var gains = new double[7];
            gains[0] = 99;
            gains[1] = -99;
            string s = EqBands.Format(gains);

            Assert.Contains("25 10", s);
            Assert.Contains("250 -10", s);
        }

        [Fact]
        public void Parse_讀得回自己寫出去的東西()
        {
            var gains = new double[] { 3, -2, 0, 1.5, -4, 0, 2 };
            double[] back = EqBands.Parse(EqBands.Format(gains));

            for (int i = 0; i < gains.Length; i++)
                Assert.Equal(gains[i], back[i], 3);
        }

        [Fact]
        public void Parse_一段有多個點時取平均()
        {
            double[] g = EqBands.Parse("250 2; 400 4");
            Assert.Equal(3, g[EqBands.IndexOf("200-600")], 3);
        }

        [Fact]
        public void Parse_沒被提到的段落是零而不是留空()
        {
            double[] g = EqBands.Parse("25 5");
            Assert.Equal(5, g[0], 3);
            for (int i = 1; i < g.Length; i++) Assert.Equal(0, g[i], 3);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Filter: ON PK Fc 25 Hz")]
        public void Parse_壞掉的輸入回一組零而不是丟例外(string? input)
        {
            double[] g = EqBands.Parse(input);
            Assert.Equal(EqBands.All.Count, g.Length);
            Assert.All(g, v => Assert.Equal(0, v, 3));
        }

        [Fact]
        public void Parse_壞掉的片段被略過_其餘照算()
        {
            double[] g = EqBands.Parse("25;40 1;;;");
            Assert.Equal(1, g[0], 3);
        }

        [Fact]
        public void Parse_容忍模型給的其他點位()
        {
            // 模型不保證用我們的點位清單，落在區間裡就該算數。
            double[] g = EqBands.Parse("31.5 6; 12500 -3");
            Assert.Equal(6, g[EqBands.IndexOf("0-200")], 3);
            Assert.Equal(-3, g[EqBands.IndexOf("10k-16k")], 3);
        }

        [Fact]
        public void SuggestPreampDb_把最大的正增益壓回去()
        {
            Assert.Equal(-4, EqBands.SuggestPreampDb(new double[] { 1, 4, -6, 0, 0, 0, 0 }), 3);
        }

        [Fact]
        public void SuggestPreampDb_全是衰減時不必補償()
        {
            Assert.Equal(0, EqBands.SuggestPreampDb(new double[] { -1, -4, 0, 0, 0, 0, 0 }), 3);
        }

        [Fact]
        public void IndexOf_認不得的代號回負一()
        {
            Assert.Equal(-1, EqBands.IndexOf("nope"));
            Assert.Equal(0, EqBands.IndexOf("0-200"));
        }
    }
}
