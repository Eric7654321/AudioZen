using Xunit;

namespace AudioUI.Tests
{
    public class TonePresetTests
    {
        [Fact]
        public void 每個內建預設的增益值都對齊七個頻段()
        {
            foreach (var p in TonePresets.BuiltIn.Append(TonePresets.Flat))
                Assert.Equal(EqBands.All.Count, p.BandGains.Count);
        }

        [Fact]
        public void 遊戲模式抬低頻並壓下刺耳的那一段()
        {
            // 訪談講的是「紮實的打擊感」與「討厭金屬刺耳聲」，這兩句話就是這個預設存在的理由。
            var game = TonePresets.ById("game")!;
            Assert.True(game.BandGains[EqBands.IndexOf("0-200")] > 0);
            Assert.True(game.BandGains[EqBands.IndexOf("5k-10k")] < 0);
        }

        [Fact]
        public void ById_認不得的代號回_null()
        {
            Assert.Null(TonePresets.ById("nope"));
            Assert.Null(TonePresets.ById(null));
            Assert.Equal("遊戲模式", TonePresets.ById("game")!.Name);
        }

        [Fact]
        public void Match_認得出目前套的是哪個預設()
        {
            var music = TonePresets.ById("music")!;
            Assert.Same(music, TonePresets.Match(music.BandGains, music.VolumePercent));
        }

        [Fact]
        public void Match_滑桿被推過之後就不屬於任何預設()
        {
            var music = TonePresets.ById("music")!;
            var moved = music.BandGains.ToArray();
            moved[0] += 1.0;

            Assert.Null(TonePresets.Match(moved, music.VolumePercent));
        }

        [Fact]
        public void Match_只改音量也算脫離預設()
        {
            var music = TonePresets.ById("music")!;
            Assert.Null(TonePresets.Match(music.BandGains, music.VolumePercent - 10));
        }

        [Theory]
        [InlineData(100, 0)]
        [InlineData(50, -6.02)]
        [InlineData(65, -3.74)]
        public void VolumePercentToDb_照振幅比換算(double percent, double expectedDb)
        {
            Assert.Equal(expectedDb, TonePresets.VolumePercentToDb(percent), 2);
        }

        [Fact]
        public void VolumePercentToDb_零不會寫出負無限大()
        {
            double db = TonePresets.VolumePercentToDb(0);
            Assert.Equal(TonePresets.MinVolumeDb, db);
            Assert.False(double.IsInfinity(db));
        }

        [Theory]
        [InlineData(100)]
        [InlineData(65)]
        [InlineData(20)]
        public void 音量百分比與_dB_可以來回(double percent)
        {
            Assert.Equal(percent, TonePresets.DbToVolumePercent(TonePresets.VolumePercentToDb(percent)), 3);
        }

        [Fact]
        public void ToTargetConfig_的字串讀得回原本的增益()
        {
            var game = TonePresets.ById("game")!;
            var config = TonePresets.ToTargetConfig("game_target", game);

            Assert.Equal("game_target", config.Target);
            double[] back = EqBands.Parse(config.GraphicEqString);
            for (int i = 0; i < game.BandGains.Count; i++)
                Assert.Equal(game.BandGains[i], back[i], 3);
        }

        [Fact]
        public void ToTargetConfig_的_preamp_同時壓回增益並套上音量()
        {
            // 遊戲模式最大正增益 +3，音量 65% 約 -3.74 dB。
            var config = TonePresets.ToTargetConfig("t", TonePresets.ById("game")!);
            Assert.Equal(-3.0 + TonePresets.VolumePercentToDb(65), config.PreampDb, 2);
        }

        [Fact]
        public void ToTargetConfig_全平的預設不動音量也不壓增益()
        {
            var config = TonePresets.ToTargetConfig("t", TonePresets.Flat);
            Assert.Equal(0, config.PreampDb, 3);
        }
    }
}
