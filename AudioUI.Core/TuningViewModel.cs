using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioUI
{
    /// <summary>一根 EQ 滑桿。</summary>
    public sealed class EqBandSlider : INotifyPropertyChanged
    {
        private double _gain;

        public EqBandSlider(EqBand band, Action? onChanged = null)
        {
            Band = band;
            OnChanged = onChanged;
        }

        public EqBand Band { get; }

        public string Label => Band.Label;

        public double Min => EqBands.MinGainDb;

        public double Max => EqBands.MaxGainDb;

        public double Gain
        {
            get => _gain;
            set
            {
                double clamped = EqBands.Clamp(value);
                if (Math.Abs(_gain - clamped) < 0.001) return;
                _gain = clamped;
                Raise(nameof(Gain));
                Raise(nameof(GainText));
                OnChanged?.Invoke();
            }
        }

        /// <summary>滑桿旁邊的數字。帶正號是因為 "+3" 與 "3" 在一排數字裡看起來意思不同。</summary>
        public string GainText => _gain > 0 ? $"+{_gain:0.#}" : $"{_gain:0.#}";

        private Action? OnChanged { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 手動調參面板的狀態。
    ///
    /// 放在 Core 而不是 WPF 專案：它只需要 <see cref="INotifyPropertyChanged"/>，
    /// 沒有任何 WPF 型別，所以「推滑桿之後會產生什麼設定」這件事測得到——
    /// 而那正是這個面板唯一會出錯的地方。
    /// </summary>
    public sealed class TuningViewModel : INotifyPropertyChanged
    {
        private bool _isProMode;
        private double _volumePercent = 100;
        private string _targetId = RouteTable.GlobalTargetId;
        private string _targetName = "整體調整";
        private string _compressorPresetId = CompressorPresets.Off.Id;
        private string _reverbPresetId = ReverbPresets.Off.Id;

        public TuningViewModel()
        {
            foreach (var band in EqBands.All)
                Bands.Add(new EqBandSlider(band, OnBandChanged));

            // 全平也是一個預設（"無"）。不算一次的話，面板一打開就顯示「自訂」。
            RefreshActiveTonePreset();
        }

        public ObservableCollection<EqBandSlider> Bands { get; } = new ObservableCollection<EqBandSlider>();

        public IReadOnlyList<DspPreset> CompressorOptions => CompressorPresets.All;

        public IReadOnlyList<DspPreset> ReverbOptions => ReverbPresets.All;

        public IReadOnlyList<TonePreset> ToneOptions => TonePresets.BuiltIn;

        /// <summary>要調哪一個目標；預設是全域。</summary>
        public string TargetId
        {
            get => _targetId;
            set { _targetId = string.IsNullOrWhiteSpace(value) ? RouteTable.GlobalTargetId : value; Raise(); }
        }

        public string TargetName
        {
            get => _targetName;
            set { _targetName = value; Raise(); }
        }

        public bool IsProMode
        {
            get => _isProMode;
            set { _isProMode = value; Raise(); Raise(nameof(IsSimpleMode)); Raise(nameof(ModeName)); Raise(nameof(ModeToggleText)); }
        }

        /// <summary>給 XAML 用的反相，省掉一個轉換器。</summary>
        public bool IsSimpleMode => !_isProMode;

        public string ModeName => _isProMode ? "專業模式" : "一般模式";

        /// <summary>切換鈕上的字：指向要去的地方，不是現在在哪。</summary>
        public string ModeToggleText => _isProMode ? "< 一般模式" : "專業模式 >";

        public double VolumePercent
        {
            get => _volumePercent;
            set
            {
                double clamped = Math.Clamp(value, 0, 100);
                if (Math.Abs(_volumePercent - clamped) < 0.001) return;
                _volumePercent = clamped;
                Raise();
                Raise(nameof(VolumeText));
                RefreshActiveTonePreset();
            }
        }

        public string VolumeText => $"{_volumePercent:0}%";

        public string CompressorPresetId
        {
            get => _compressorPresetId;
            set { _compressorPresetId = CompressorPresets.ById(value).Id; Raise(); Raise(nameof(CompressorName)); }
        }

        public string CompressorName => CompressorPresets.ById(_compressorPresetId).Name;

        public string ReverbPresetId
        {
            get => _reverbPresetId;
            set { _reverbPresetId = ReverbPresets.ById(value).Id; Raise(); Raise(nameof(ReverbName)); }
        }

        public string ReverbName => ReverbPresets.ById(_reverbPresetId).Name;

        /// <summary>目前的值剛好等於哪個音色預設；推過滑桿之後是 null，代表「自訂」。</summary>
        public string? ActiveTonePresetId { get; private set; }

        public string ToneText => ActiveTonePresetId == null
            ? "自訂"
            : TonePresets.ById(ActiveTonePresetId)?.Name ?? "無";

        /// <summary>套用一個一般模式的預設，七根滑桿與音量一起跳到那組值。</summary>
        public void ApplyTonePreset(string? presetId)
        {
            var preset = TonePresets.ById(presetId);
            if (preset == null) return;

            for (int i = 0; i < Bands.Count && i < preset.BandGains.Count; i++)
                Bands[i].Gain = preset.BandGains[i];

            VolumePercent = preset.VolumePercent;
            RefreshActiveTonePreset();
        }

        /// <summary>把既有的設定讀進滑桿，讓面板打開時顯示的是現況而不是一排零。</summary>
        public void LoadFrom(AudioTargetConfig? config)
        {
            if (config == null) return;

            double[] gains = EqBands.Parse(config.GraphicEqString);
            for (int i = 0; i < Bands.Count && i < gains.Length; i++)
                Bands[i].Gain = gains[i];

            // preamp 同時含了音量與 EQ 的補償，扣掉補償剩下的才是音量。
            double volumeDb = config.PreampDb - EqBands.SuggestPreampDb(gains);
            VolumePercent = Math.Round(TonePresets.DbToVolumePercent(volumeDb));

            if (!string.IsNullOrWhiteSpace(config.Target)) TargetId = config.Target;

            // 設定檔裡只剩編碼過的參數，認不回是哪個 preset；能確定的只有「沒有這個效果」。
            // 所以有內容時維持現在選的，不去猜。
            if (config.CompJson == null) CompressorPresetId = CompressorPresets.Off.Id;
            if (config.ReverbJson == null) ReverbPresetId = ReverbPresets.Off.Id;

            RefreshActiveTonePreset();
        }

        /// <summary>把面板上的東西變成一份可以交給後端的設定。</summary>
        public AudioTargetConfig BuildConfig() =>
            TonePresets.ToTargetConfig(TargetId, Bands.Select(b => b.Gain).ToList(), VolumePercent,
                                       CompressorPresets.ById(_compressorPresetId),
                                       ReverbPresets.ById(_reverbPresetId));

        public AudioIntent BuildIntent() => new AudioIntent
        {
            MessageForUser = $"已套用手動調整（{TargetName}）",
            Configs = new List<AudioTargetConfig> { BuildConfig() },
        };

        /// <summary>把所有滑桿歸零、效果關掉。</summary>
        public void Reset()
        {
            ApplyTonePreset(TonePresets.Flat.Id);
            CompressorPresetId = CompressorPresets.Off.Id;
            ReverbPresetId = ReverbPresets.Off.Id;
        }

        private void OnBandChanged() => RefreshActiveTonePreset();

        private void RefreshActiveTonePreset()
        {
            ActiveTonePresetId = TonePresets.Match(Bands.Select(b => b.Gain).ToList(), VolumePercent)?.Id;
            Raise(nameof(ActiveTonePresetId));
            Raise(nameof(ToneText));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
