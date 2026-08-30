namespace AudioUI
{
    /// <summary>
    /// 壓縮器或殘響的一組具名設定。
    ///
    /// hi-fi 的專業模式把 Melda 的參數逐個畫成滑桿，這裡刻意不那樣做：那些值最後要編成
    /// Melda 的二進位 preset，格式是逆向出來的，Melda 改版就整個失效。把可調的東西收斂成
    /// 幾個調好的組合，暴露面只剩「選哪一個」，而 EQ 與音量仍然可以自由調整——
    /// 那兩者是純文字格式，錯了聽得出來也改得回來。
    ///
    /// hi-fi 上兩個對不到 Melda key 的東西也在這裡解決：Reflect Type 的四種空間
    /// 直接變成四個殘響 preset，Sustain 併進壓縮器 preset 的釋放時間。
    /// </summary>
    public sealed class DspPreset
    {
        public DspPreset(string id, string name, IReadOnlyList<MeldaEntry>? entries)
        {
            Id = id;
            Name = name;
            Entries = entries;
        }

        public string Id { get; }

        public string Name { get; }

        /// <summary>要寫進設定檔的參數；<c>null</c> 表示這個 preset 就是「不要這個效果」。</summary>
        public IReadOnlyList<MeldaEntry>? Entries { get; }

        public bool IsOff => Entries == null;

        /// <summary>給後端用的可變清單。<see cref="AudioTargetConfig"/> 收的是 List。</summary>
        public List<MeldaEntry>? ToList() => Entries?.ToList();
    }

    internal static class MeldaEntryFactory
    {
        internal static MeldaEntry E(string key, object? value) =>
            new MeldaEntry { RawKey = key, Value = value! };
    }

    /// <summary>
    /// 壓縮器的預設。數值是照參數範圍推出來的合理起點，**沒有實際試聽過**——
    /// 要調得好聽只能戴著耳機一個一個試，這裡給的是可以開始調的地方。
    /// </summary>
    public static class CompressorPresets
    {
        public static readonly DspPreset Off = new DspPreset("off", "無", null);

        /// <summary>輕輕收一下動態，聽起來幾乎沒有壓縮感。</summary>
        public static readonly DspPreset Light =
            new DspPreset("light", "輕度", Build(threshold: 0.50, ratio: 2.0, attack: 0.15, release: 0.30, kneeSize: 0.5, kneeMode: "Soft"));

        /// <summary>一般用途，把忽大忽小的音量拉平。</summary>
        public static readonly DspPreset Medium =
            new DspPreset("medium", "中度", Build(threshold: 0.30, ratio: 4.0, attack: 0.08, release: 0.25, kneeSize: 0.4, kneeMode: "Soft"));

        /// <summary>對應 hi-fi 那個「有人爆麥了」的情境：快速起音、高比例，把突然的巨大音量壓住。</summary>
        public static readonly DspPreset Shout =
            new DspPreset("shout", "防爆麥", Build(threshold: 0.15, ratio: 8.0, attack: 0.02, release: 0.15, kneeSize: 0.2, kneeMode: "Hard"));

        public static readonly IReadOnlyList<DspPreset> All = new[] { Off, Light, Medium, Shout };

        public static DspPreset ById(string? id) =>
            All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Off;

        /// <summary>Melda 的 chunk 標頭，<c>EqualizerApoBackend</c> 寫 VSTPlugin 行時要用同一個字。</summary>
        public const string ChunkHeader = "MBXXMCompressorsettings";

        /// <summary>
        /// 結構性的欄位（版本、編輯器大小、圖形節點）每個 preset 都一樣，所以只寫一次；
        /// preset 之間真正不同的只有簽章上那幾個。
        /// </summary>
        private static List<MeldaEntry> Build(double threshold, double ratio, double attack,
                                              double release, double kneeSize, string kneeMode)
        {
            var E = new Func<string, object?, MeldaEntry>(MeldaEntryFactory.E);
            return new List<MeldaEntry>
            {
                E("A#", 1),
                E("A#gain", 0.0),
                E("A#outputgain", 0.0),
                E("A#attack", attack),
                E("A#release", release),
                E("A#rmslength", 0.01),
                E("A#threshold", threshold),
                E("A#ratio", ratio),
                E("A#kneemode", kneeMode),
                E("A#kneesize", kneeSize),
                E("A#maximize", 0),
                E("A#customshape", 0),
                E("AVersion", 1115136),
                E("AMIDIProgramChangeEnable", 0),
                E("AProgramChangeCategorizer", 0),
                E("AEditorSize", "901,527"),
                E("AControllersEnabled", 0),
                E("APluginToolbarCollapsed", 255),
                E("AMaxLFOBlockSize", 32),
                E("ALRSplitterSize1", 534.0),
                E("ALRSplitterSize2", 306.0),
                E("ASideChainEnable", 0),
                E("ASideChainMinFrequency", 20.0),
                E("ASideChainMaxFrequency", 19999.0),
                E("Ahalfgain", 0),
                E("Aamplituderatio", 0),
                E("Xgraph", null),
                E("Mode", "Normal"),
                E("XPoint", null),
                E("FlagsB", 143),
                E("/XPoint", null),
                E("x", threshold),
                E("Ay", threshold),
                E("AFlagsB", 140),
                E("/XPoint", null),
                E("x", 1.0),
                E("Ay", 1.0),
                E("AFlagsB", 141),
            };
        }
    }

    /// <summary>
    /// 殘響的預設。四個空間名稱直接來自 hi-fi 的 Reflect Type——
    /// CharmVerb 沒有「反射類型」這個參數，那四個選項本來就只能是四組數值。
    /// 同樣沒有實際試聽過。
    /// </summary>
    public static class ReverbPresets
    {
        public static readonly DspPreset Off = new DspPreset("off", "無", null);

        public static readonly DspPreset Chamber =
            new DspPreset("chamber", "Chamber", Build(dryWet: 0.18, length: 0.8, size: 0.35, predelay: 0.02, widening: 0.20, complexity: 16, modulation: 0.10));

        public static readonly DspPreset Studio =
            new DspPreset("studio", "Studio", Build(dryWet: 0.12, length: 0.5, size: 0.25, predelay: 0.01, widening: 0.10, complexity: 12, modulation: 0.05));

        public static readonly DspPreset Cave =
            new DspPreset("cave", "Cave", Build(dryWet: 0.35, length: 3.5, size: 0.85, predelay: 0.05, widening: 0.60, complexity: 32, modulation: 0.25));

        public static readonly DspPreset Hall =
            new DspPreset("hall", "Hall", Build(dryWet: 0.28, length: 2.2, size: 0.70, predelay: 0.04, widening: 0.45, complexity: 24, modulation: 0.15));

        public static readonly IReadOnlyList<DspPreset> All = new[] { Off, Chamber, Studio, Cave, Hall };

        public static DspPreset ById(string? id) =>
            All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Off;

        public const string ChunkHeader = "MBXXMCharmVerbsettings";

        private static List<MeldaEntry> Build(double dryWet, double length, double size, double predelay,
                                              double widening, int complexity, double modulation)
        {
            var E = new Func<string, object?, MeldaEntry>(MeldaEntryFactory.E);
            return new List<MeldaEntry>
            {
                E("A#", 1),
                E("A#DryWet", dryWet),
                E("A#Length", length),
                E("A#Size", size),
                // LPF / HPF 的範圍在 prompt 裡就是這兩個奇怪的對數端點，取兩端等於不額外濾波。
                E("A#LPF", 3.3010299956639813),
                E("A#HPF", 3.0),
                E("A#Predelay", predelay),
                E("A#Gain", 0.0),
                E("A#Widening", widening),
                E("A#DampLowF", 200.0),
                E("A#DampLowG", 0.0),
                E("A#DampLowQ", 0.7071067811865476),
                E("A#DampHighF", 8000.0),
                E("A#DampHighG", 0.0),
                E("A#DampHighQ", 0.7071067811865476),
                E("A#DesignerCollapsed", 0),
                E("A#Complexity", complexity),
                E("A#Modulation", modulation),
                E("A#Seed", 1791916693),
                E("A#DelayMin", 0.0),
                E("A#DelayMax", 1.0),
                E("A#FocusDelay", 0.0),
                E("A#WidthDelay", 0.5),
                E("A#OrderDelay", "Up"),
                E("A#ModulationRate", 1.0),
                E("AVersion", 1115136),
                E("AMIDIProgramChangeEnable", 0),
                E("AProgramChangeCategorizer", 0),
                E("AEditorSize", 751573),
                E("APluginToolbarCollapsed", 255),
                E("AMaxLFOBlockSize", 32),
            };
        }
    }
}
