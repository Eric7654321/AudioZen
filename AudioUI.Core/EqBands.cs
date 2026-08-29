using System.Globalization;
using System.Text;

namespace AudioUI
{
    /// <summary>
    /// 一條使用者手調的頻段。UI 上是一根滑桿，底下對應 GraphicEQ 的若干個頻率點。
    /// 用頻率「區間」而不是固定的點位清單，是因為模型回來的字串不保證用哪些點，
    /// 換一組點位時這裡不必跟著改。
    /// </summary>
    public sealed class EqBand
    {
        public EqBand(string id, string label, int minHz, int maxHz)
        {
            Id = id;
            Label = label;
            MinHz = minHz;
            MaxHz = maxHz;
        }

        /// <summary>存檔與繫結用的穩定代號，不隨顯示文字改動。</summary>
        public string Id { get; }

        /// <summary>滑桿底下顯示的文字。</summary>
        public string Label { get; }

        /// <summary>下界，含。</summary>
        public int MinHz { get; }

        /// <summary>上界，不含（最後一段例外，見 <see cref="Contains"/>）。</summary>
        public int MaxHz { get; }

        public bool Contains(int hz) =>
            hz >= MinHz && (hz < MaxHz || (hz == MaxHz && MaxHz == EqBands.TopHz));
    }

    /// <summary>
    /// 手調頻段與 GraphicEQ 字串之間的橋。
    ///
    /// 語音那條路徑是模型直接吐 <c>graphic_eq_string</c>，手調這條路徑則是七根滑桿；
    /// 兩者最後都要變成同一種字串交給 <see cref="IAudioBackend"/>，所以轉換放在這裡一份，
    /// 讓 UI 只認得「七個增益值」而不必知道點位長什麼樣。
    /// </summary>
    public static class EqBands
    {
        /// <summary>可聽頻率的上緣，也是最後一段的閉區間端點。</summary>
        public const int TopHz = 20000;

        /// <summary>單一頻段的增益上下限，跟 prompt 給模型的範圍一致。</summary>
        public const double MinGainDb = -10.0;
        public const double MaxGainDb = 10.0;

        /// <summary>七段的切法來自 hi-fi 的專業模式面板。</summary>
        public static readonly IReadOnlyList<EqBand> All = new[]
        {
            new EqBand("0-200",     "0~200",     0,     200),
            new EqBand("200-600",   "200~600",   200,   600),
            new EqBand("600-2k",    "600~2k",    600,   2000),
            new EqBand("2k-5k",     "2k~5k",     2000,  5000),
            new EqBand("5k-10k",    "5k~10k",    5000,  10000),
            new EqBand("10k-16k",   "10k~16k",   10000, 16000),
            new EqBand("16k-20k",   "16k~20k",   16000, TopHz),
        };

        /// <summary>
        /// 寫出去的點位。前十五個跟 prompt 給模型的清單一致；20000 是補的——
        /// 沒有它的話 hi-fi 最上面那根滑桿沒有任何點可以動，會變成純裝飾。
        /// </summary>
        public static readonly IReadOnlyList<int> Frequencies = new[]
        {
            25, 40, 63, 100, 160, 250, 400, 630, 1000, 1600, 2500, 4000, 6300, 10000, 16000, TopHz
        };

        public static int IndexOf(string? bandId)
        {
            for (int i = 0; i < All.Count; i++)
                if (string.Equals(All[i].Id, bandId, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>
        /// 把 GraphicEQ 字串讀成七個增益值，好讓滑桿顯示目前的設定。
        /// 一段裡有多個點時取平均：模型給的是逐點增益，而滑桿只有一個位置可以放。
        /// 讀不到內容的段落回 0，代表「沒有調整」。
        /// </summary>
        public static double[] Parse(string? graphicEqString)
        {
            var sums = new double[All.Count];
            var counts = new int[All.Count];

            foreach (string chunk in (graphicEqString ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = chunk.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) continue;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double hz)) continue;
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double gain)) continue;

                int band = BandIndexFor((int)Math.Round(hz));
                if (band < 0) continue;

                sums[band] += gain;
                counts[band]++;
            }

            var result = new double[All.Count];
            for (int i = 0; i < All.Count; i++)
                result[i] = counts[i] == 0 ? 0.0 : sums[i] / counts[i];
            return result;
        }

        /// <summary>
        /// 把七個增益值寫成 GraphicEQ 字串。同一段裡的每個點都給同樣的增益，
        /// 段與段之間交給 APO 自己內插。
        /// </summary>
        public static string Format(IReadOnlyList<double>? bandGains)
        {
            var sb = new StringBuilder();
            foreach (int hz in Frequencies)
            {
                int band = BandIndexFor(hz);
                double gain = band >= 0 && bandGains != null && band < bandGains.Count
                    ? Clamp(bandGains[band])
                    : 0.0;

                if (sb.Length > 0) sb.Append("; ");
                sb.Append(hz.ToString(CultureInfo.InvariantCulture));
                sb.Append(' ');
                sb.Append(gain.ToString("0.#", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>
        /// GraphicEQ 只描述相對增益，整體音量要靠 preamp 壓回來，否則加出來的增益會削波。
        /// 取最大正增益的相反數，跟 prompt 對模型的要求（|preamp| >= max_gain）同一條規則。
        /// </summary>
        public static double SuggestPreampDb(IReadOnlyList<double>? bandGains)
        {
            if (bandGains == null || bandGains.Count == 0) return 0.0;
            double max = bandGains.Max(Clamp);
            return max > 0 ? -max : 0.0;
        }

        public static double Clamp(double gainDb) => Math.Clamp(gainDb, MinGainDb, MaxGainDb);

        private static int BandIndexFor(int hz)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].Contains(hz)) return i;
            return -1;
        }
    }
}
