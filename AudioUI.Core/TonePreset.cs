
namespace AudioUI
{
    /// <summary>
    /// 一般模式的一個音色預設。
    ///
    /// 一般模式與專業模式操作的是同一組東西——七段增益加一個音量——差別只在
    /// 前者給幾個調好的組合、後者讓使用者自己推滑桿。所以預設集就是一組
    /// 具名的增益值，而不是另一條產生設定檔的路徑。
    /// </summary>
    public sealed class TonePreset
    {
        public TonePreset(string id, string name, double volumePercent, params double[] bandGains)
        {
            Id = id;
            Name = name;
            VolumePercent = volumePercent;
            BandGains = bandGains;
        }

        /// <summary>存檔與繫結用的穩定代號。</summary>
        public string Id { get; }

        /// <summary>按鈕上顯示的名字。</summary>
        public string Name { get; }

        /// <summary>hi-fi 的「音量縮放」，100 表示不動。</summary>
        public double VolumePercent { get; }

        /// <summary>對應 <see cref="EqBands.All"/> 的七個增益值。</summary>
        public IReadOnlyList<double> BandGains { get; }
    }

    public static class TonePresets
    {
        /// <summary>沒有套用任何預設時的狀態，也是「自訂」的起點。</summary>
        public static readonly TonePreset Flat =
            new("flat", "無", 100, 0, 0, 0, 0, 0, 0, 0);

        /// <summary>
        /// 內建的兩個預設來自 hi-fi 的一般模式。數值照使用者訪談裡講的話走：
        /// 遊戲要「紮實的打擊感」與「不刺耳的金屬聲」＝ 抬低頻、壓 5k~10k；
        /// 音樂要人聲與空氣感 ＝ 抬中頻與高頻，低頻只微調。
        /// </summary>
        public static readonly IReadOnlyList<TonePreset> BuiltIn = new[]
        {
            //                                     0~200 200~600 600~2k 2k~5k 5k~10k 10k~16k 16k~20k
            new TonePreset("game",  "遊戲模式", 65,   3.0,   0.5,   0.0,  1.0,  -4.0,   -1.0,  -2.0),
            new TonePreset("music", "音樂模式", 100,  1.0,   0.0,   1.5,  2.0,   0.5,    1.0,   0.5),
        };

        public static TonePreset? ById(string? id) =>
            string.Equals(id, Flat.Id, StringComparison.OrdinalIgnoreCase)
                ? Flat
                : BuiltIn.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 找出目前的增益值對應哪個預設。滑桿被推過之後就不再是任何預設，
        /// 一般模式與專業模式來回切換時要靠這個決定哪顆按鈕該亮著。
        /// </summary>
        public static TonePreset? Match(IReadOnlyList<double>? bandGains, double volumePercent)
        {
            if (bandGains == null) return null;
            foreach (var p in BuiltIn.Append(Flat))
            {
                if (Math.Abs(p.VolumePercent - volumePercent) > 0.01) continue;
                if (p.BandGains.Count != bandGains.Count) continue;
                if (p.BandGains.Where((g, i) => Math.Abs(g - bandGains[i]) > 0.01).Any()) continue;
                return p;
            }
            return null;
        }

        /// <summary>
        /// 把一組手調的值變成後端要的設定。preamp 同時承擔兩件事：把 EQ 加出來的增益壓回去，
        /// 以及套用音量縮放——APO 的 <c>Preamp:</c> 是唯一的整體增益，兩者本來就會相加。
        /// </summary>
        public static AudioTargetConfig ToTargetConfig(string targetId, IReadOnlyList<double> bandGains, double volumePercent)
        {
            return new AudioTargetConfig
            {
                Target = targetId,
                PreampDb = Math.Round(EqBands.SuggestPreampDb(bandGains) + VolumePercentToDb(volumePercent), 2),
                GraphicEqString = EqBands.Format(bandGains),
            };
        }

        public static AudioTargetConfig ToTargetConfig(string targetId, TonePreset preset) =>
            ToTargetConfig(targetId, preset.BandGains, preset.VolumePercent);

        /// <summary>
        /// 音量百分比換成 dB。0% 沒有對應的 dB（是負無限大），交給呼叫端當靜音處理，
        /// 這裡回一個 APO 認得的下限而不是 -∞，否則寫進設定檔的是 "-∞ dB"。
        /// </summary>
        public static double VolumePercentToDb(double percent)
        {
            if (percent <= 0) return MinVolumeDb;
            return Math.Max(MinVolumeDb, 20.0 * Math.Log10(percent / 100.0));
        }

        /// <summary>反向換算，供顯示用。</summary>
        public static double DbToVolumePercent(double db) =>
            db <= MinVolumeDb ? 0 : Math.Pow(10.0, db / 20.0) * 100.0;

        /// <summary>等同靜音的下限。-60 dB 是千分之一的振幅，聽起來就是沒有聲音。</summary>
        public const double MinVolumeDb = -60.0;
    }
}
