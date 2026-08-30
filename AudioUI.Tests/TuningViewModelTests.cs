using System.ComponentModel;
using Xunit;

namespace AudioUI.Tests
{
    public class TuningViewModelTests
    {
        [Fact]
        public void 七根滑桿對齊七個頻段()
        {
            var vm = new TuningViewModel();
            Assert.Equal(EqBands.All.Count, vm.Bands.Count);
            Assert.Equal("0~200", vm.Bands[0].Label);
        }

        [Fact]
        public void 滑桿超出範圍會被夾回去()
        {
            var vm = new TuningViewModel();
            vm.Bands[0].Gain = 99;
            Assert.Equal(EqBands.MaxGainDb, vm.Bands[0].Gain, 3);
        }

        [Fact]
        public void 增益的顯示文字帶正號()
        {
            var vm = new TuningViewModel();
            vm.Bands[0].Gain = 3;
            Assert.Equal("+3", vm.Bands[0].GainText);

            vm.Bands[0].Gain = -3;
            Assert.Equal("-3", vm.Bands[0].GainText);
        }

        [Fact]
        public void 套用預設之後畫面顯示那個預設的名字()
        {
            var vm = new TuningViewModel();
            vm.ApplyTonePreset("game");

            Assert.Equal("game", vm.ActiveTonePresetId);
            Assert.Equal("遊戲模式", vm.ToneText);
            Assert.Equal(65, vm.VolumePercent, 3);
        }

        [Fact]
        public void 推過任何一根滑桿之後就變成自訂()
        {
            var vm = new TuningViewModel();
            vm.ApplyTonePreset("game");
            vm.Bands[3].Gain += 1;

            Assert.Null(vm.ActiveTonePresetId);
            Assert.Equal("自訂", vm.ToneText);
        }

        [Fact]
        public void 只動音量也會變成自訂()
        {
            var vm = new TuningViewModel();
            vm.ApplyTonePreset("music");
            vm.VolumePercent = 40;

            Assert.Null(vm.ActiveTonePresetId);
        }

        [Fact]
        public void 一開始是全平的所以顯示無()
        {
            Assert.Equal("無", new TuningViewModel().ToneText);
        }

        [Fact]
        public void 認不得的效果代號退回無()
        {
            var vm = new TuningViewModel();
            vm.CompressorPresetId = "不存在";
            vm.ReverbPresetId = "也不存在";

            Assert.Equal("無", vm.CompressorName);
            Assert.Equal("無", vm.ReverbName);
        }

        [Fact]
        public void BuildConfig_的字串讀得回滑桿上的值()
        {
            var vm = new TuningViewModel();
            vm.Bands[0].Gain = 4;
            vm.Bands[4].Gain = -3;

            double[] back = EqBands.Parse(vm.BuildConfig().GraphicEqString);

            Assert.Equal(4, back[0], 3);
            Assert.Equal(-3, back[4], 3);
        }

        [Fact]
        public void BuildConfig_帶上選中的效果_選無就不帶()
        {
            var vm = new TuningViewModel { CompressorPresetId = "shout" };
            var config = vm.BuildConfig();

            Assert.NotNull(config.CompJson);
            Assert.Null(config.ReverbJson);
        }

        [Fact]
        public void BuildConfig_預設打到全域目標()
        {
            Assert.Equal(RouteTable.GlobalTargetId, new TuningViewModel().BuildConfig().Target);
        }

        [Fact]
        public void TargetId_被清空時退回全域()
        {
            var vm = new TuningViewModel { TargetId = "game" };
            vm.TargetId = "  ";
            Assert.Equal(RouteTable.GlobalTargetId, vm.TargetId);
        }

        [Fact]
        public void 存出去再讀回來_滑桿與音量都一樣()
        {
            var vm = new TuningViewModel { TargetId = "game", VolumePercent = 65 };
            vm.Bands[0].Gain = 3;
            vm.Bands[5].Gain = -2;

            var reopened = new TuningViewModel();
            reopened.LoadFrom(vm.BuildConfig());

            Assert.Equal("game", reopened.TargetId);
            Assert.Equal(65, reopened.VolumePercent, 0);
            Assert.Equal(3, reopened.Bands[0].Gain, 3);
            Assert.Equal(-2, reopened.Bands[5].Gain, 3);
        }

        [Fact]
        public void LoadFrom_收到_null_不會清掉現況()
        {
            var vm = new TuningViewModel();
            vm.Bands[0].Gain = 5;
            vm.LoadFrom(null);

            Assert.Equal(5, vm.Bands[0].Gain, 3);
        }

        [Fact]
        public void Reset_把滑桿與效果都歸零()
        {
            var vm = new TuningViewModel { CompressorPresetId = "shout", ReverbPresetId = "hall" };
            vm.Bands[2].Gain = 6;

            vm.Reset();

            Assert.All(vm.Bands, b => Assert.Equal(0, b.Gain, 3));
            Assert.Equal("無", vm.CompressorName);
            Assert.Equal("無", vm.ReverbName);
            Assert.Equal(100, vm.VolumePercent, 3);
        }

        [Fact]
        public void 切換模式會通知畫面_兩個屬性都要發()
        {
            // IsSimpleMode 是給 XAML 省掉轉換器用的反相，忘記發通知的話另一半畫面不會更新。
            var vm = new TuningViewModel();
            var raised = new List<string?>();
            ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.IsProMode = true;

            Assert.Contains(nameof(TuningViewModel.IsProMode), raised);
            Assert.Contains(nameof(TuningViewModel.IsSimpleMode), raised);
            Assert.False(vm.IsSimpleMode);
        }

        [Fact]
        public void 滑桿改變會發出通知()
        {
            var vm = new TuningViewModel();
            var raised = new List<string?>();
            vm.Bands[0].PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Bands[0].Gain = 2;

            Assert.Contains(nameof(EqBandSlider.Gain), raised);
            Assert.Contains(nameof(EqBandSlider.GainText), raised);
        }

        [Fact]
        public void BuildIntent_包成一份可以直接交給後端的意圖()
        {
            var vm = new TuningViewModel { TargetName = "Minecraft" };
            var intent = vm.BuildIntent();

            Assert.Single(intent.Configs);
            Assert.Contains("Minecraft", intent.MessageForUser);
        }
    }
}
