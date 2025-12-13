using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace AudioUI
{
    public class AudioProcessor
    {
        /// <summary>
        /// 讀取原始錄音，根據 Config (Preamp + EQ) 產生預覽檔
        /// </summary>
        public static string GeneratePreview(string inputWavPath, string configPath)
        {
            if (!File.Exists(inputWavPath) || !File.Exists(configPath)) return null;

            string outputWavPath = inputWavPath.Replace(".wav", $"_preview_{DateTime.Now.Ticks}.wav");

            try
            {
                // 讀取設定
                double volumeScale = ParsePreampFromConfig(configPath);
                var eqBands = ParseGraphicEqFromConfig(configPath);

                using (var reader = new AudioFileReader(inputWavPath))
                {
                    // 1. 套用音量 (Preamp)
                    ISampleProvider signalChain = new VolumeSampleProvider(reader)
                    {
                        Volume = (float)volumeScale
                    };

                    // 2. 套用 EQ (如果有解析到)
                    if (eqBands != null && eqBands.Count > 0)
                    {
                        signalChain = new EqualizerSampleProvider(signalChain, eqBands);
                    }

                    // 3. ★★★ 修正 CS1503 錯誤：使用 CreateWaveFile16 ★★★
                    // 這會自動將 ISampleProvider (32-bit Float) 轉回 16-bit PCM 寫入檔案
                    WaveFileWriter.CreateWaveFile16(outputWavPath, signalChain);
                }

                return outputWavPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"生成預覽失敗: {ex.Message}");
                return inputWavPath; // 失敗回傳原檔
            }
        }

        private static double ParsePreampFromConfig(string configPath)
        {
            try
            {
                var lines = File.ReadAllLines(configPath);
                foreach (var line in lines)
                {
                    if (line.Trim().StartsWith("Preamp:", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = Regex.Match(line, @"Preamp:\s*([\d\.\-]+)\s*dB", RegexOptions.IgnoreCase);
                        if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double db))
                        {
                            return Math.Pow(10, db / 20.0);
                        }
                    }
                }
            }
            catch { }
            return 1.0;
        }

        // 解析 GraphicEQ: 25 2.2; 40 1.6; ...
        private static List<EqBand> ParseGraphicEqFromConfig(string configPath)
        {
            var bands = new List<EqBand>();
            try
            {
                var lines = File.ReadAllLines(configPath);
                foreach (var line in lines)
                {
                    if (line.Trim().StartsWith("GraphicEQ:", StringComparison.OrdinalIgnoreCase))
                    {
                        // 格式: 25 2.2; 40 1.6
                        string content = line.Substring(10).Trim();
                        var pairs = content.Split(';');
                        foreach (var pair in pairs)
                        {
                            var parts = pair.Trim().Split(' ');
                            if (parts.Length >= 2 &&
                                float.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float freq) &&
                                float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out float gain))
                            {
                                bands.Add(new EqBand { Frequency = freq, Gain = gain });
                            }
                        }
                    }
                }
            }
            catch { }
            return bands;
        }
    }

    // 簡單的 EQ 頻段結構
    public class EqBand
    {
        public float Frequency { get; set; }
        public float Gain { get; set; }
        public float Q { get; set; } = 1.41f; // 預設 Q 值 (約 1 個八度音)
    }

    // ★★★ 自定義 EQ 處理器 (使用 BiQuadFilter) ★★★
    public class EqualizerSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly BiQuadFilter[,] _filters; // [Channels, Bands]
        private readonly List<EqBand> _bands;
        private readonly int _channels;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public EqualizerSampleProvider(ISampleProvider source, List<EqBand> bands)
        {
            _source = source;
            _bands = bands;
            _channels = source.WaveFormat.Channels;
            _filters = new BiQuadFilter[_channels, _bands.Count];
            CreateFilters();
        }

        private void CreateFilters()
        {
            for (int ch = 0; ch < _channels; ch++)
            {
                for (int b = 0; b < _bands.Count; b++)
                {
                    // 使用 PeakingEQ 模擬 GraphicEQ 的推桿
                    _filters[ch, b] = BiQuadFilter.PeakingEQ(_source.WaveFormat.SampleRate, _bands[b].Frequency, _bands[b].Q, _bands[b].Gain);
                }
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);

            // 對每個 Sample 應用所有 Filter
            for (int n = 0; n < samplesRead; n++)
            {
                int ch = n % _channels; // 當前聲道

                // 串接通過所有頻段的 Filter
                for (int b = 0; b < _bands.Count; b++)
                {
                    buffer[offset + n] = _filters[ch, b].Transform(buffer[offset + n]);
                }
            }
            return samplesRead;
        }
    }
}