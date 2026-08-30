using Xunit;

namespace AudioUI.Tests
{
    public class EqualizerApoBackendTests
    {
        private static RouteTable Routes() => new RouteTable(new[]
        {
            new AudioRoute { Id = "game", DevicePattern = "CABLE Input VB-Audio Virtual Cable {guid}", Processes = { "eldenring.exe" } },
        });

        private static EqualizerApoBackend Backend(TempDir dir) =>
            new EqualizerApoBackend(new ApoSettings { ConfigDirectory = dir.Path, FragmentFileName = "audiozen.txt" }, Routes());

        private static AudioIntent Intent(string target) => new AudioIntent
        {
            MessageForUser = "好了",
            Configs = new List<AudioTargetConfig>
            {
                new AudioTargetConfig { Target = target, PreampDb = -6, GraphicEqString = "25 0; 40 -3" },
            },
        };

        [Fact]
        public void Write_把邏輯代號翻成裝置樣式()
        {
            using var dir = new TempDir();
            string path = dir.File("out.txt");

            string? message = Backend(dir).Write(Intent("game"), path);

            Assert.Equal("好了", message);
            string text = File.ReadAllText(path);
            Assert.Contains("Device: CABLE Input VB-Audio Virtual Cable {guid}", text);
            Assert.Contains("Preamp: -6 dB", text);
            Assert.Contains("GraphicEQ: 25 0; 40 -3", text);
        }

        [Fact]
        public void Write_認不得的目標寫成註解_讓_APO_忽略但檔案留下線索()
        {
            using var dir = new TempDir();
            string path = dir.File("out.txt");

            Backend(dir).Write(Intent("spotify"), path);

            string text = File.ReadAllText(path);
            Assert.Contains("# Unknown Target: spotify", text);
            Assert.DoesNotContain("Device:", text);
        }

        [Fact]
        public void Write_意圖為_null_時回_null_且不碰檔案()
        {
            using var dir = new TempDir();
            string path = dir.File("out.txt");

            Assert.Null(Backend(dir).Write(null, path));
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void Write_VST_路徑跟著設定的安裝目錄走()
        {
            using var dir = new TempDir();
            string path = dir.File("out.txt");
            var backend = new EqualizerApoBackend(
                new ApoSettings { ConfigDirectory = dir.Path, VstDirectory = @"D:\Plugins\Melda" }, Routes());

            var intent = Intent("game");
            intent.Configs[0].CompJson = new List<MeldaEntry> { new MeldaEntry { RawKey = "ratio", Value = 4 } };

            backend.Write(intent, path);

            string text = File.ReadAllText(path);
            Assert.Contains(@"D:\Plugins\Melda\Dynamics\MCompressor.dll", text);
            Assert.DoesNotContain(@"C:\Program Files\VstPlugins", text);
        }

        [Fact]
        public void Write_沒有壓縮器參數時不寫_VSTPlugin_行()
        {
            using var dir = new TempDir();
            string path = dir.File("out.txt");

            Backend(dir).Write(Intent("game"), path);

            Assert.DoesNotContain("VSTPlugin:", File.ReadAllText(path));
        }

        [Fact]
        public void Apply_寫成獨立檔案而不是覆蓋_config_txt()
        {
            using var dir = new TempDir();
            File.WriteAllText(Path.Combine(dir.Path, "config.txt"), "Preamp: -3 dB\n");
            string source = dir.File("generated.txt");
            File.WriteAllText(source, "Device: all\n");

            Backend(dir).Apply(source);

            // 使用者原本的內容還在，我們的設定另外放一份。
            string main = File.ReadAllText(Path.Combine(dir.Path, "config.txt"));
            Assert.Contains("Preamp: -3 dB", main);
            Assert.Equal("Device: all\n", File.ReadAllText(Path.Combine(dir.Path, "audiozen.txt")).Replace("\r\n", "\n"));
        }

        [Fact]
        public void Apply_補上_Include_行_而且只補一次()
        {
            using var dir = new TempDir();
            string source = dir.File("generated.txt");
            File.WriteAllText(source, "Device: all\n");
            var backend = Backend(dir);

            backend.Apply(source);
            backend.Apply(source);

            var lines = File.ReadAllLines(Path.Combine(dir.Path, "config.txt"));
            Assert.Single(lines, l => l.Trim() == "Include: audiozen.txt");
        }

        [Fact]
        public void Apply_來源不存在時丟出帶路徑的例外()
        {
            using var dir = new TempDir();
            var ex = Assert.Throws<FileNotFoundException>(() => Backend(dir).Apply(dir.File("nope.txt")));
            Assert.Contains("nope.txt", ex.Message);
        }

        [Fact]
        public void Apply_設定目錄不存在時說得出該去改哪裡()
        {
            var backend = new EqualizerApoBackend(
                new ApoSettings { ConfigDirectory = Path.Combine(Path.GetTempPath(), "audiozen-absent-" + Guid.NewGuid().ToString("N")) },
                Routes());

            using var dir = new TempDir();
            string source = dir.File("generated.txt");
            File.WriteAllText(source, "Device: all\n");

            var ex = Assert.Throws<DirectoryNotFoundException>(() => backend.Apply(source));
            Assert.Contains("apo.configDirectory", ex.Message);
        }

        // --- ReadCurrent：面板打開時要看到現況 ---

        [Fact]
        public void ReadCurrent_套用過的設定讀得回來()
        {
            using var dir = new TempDir();
            var backend = Backend(dir);
            string path = dir.File("out.txt");
            backend.Write(Intent("game"), path);
            backend.Apply(path);

            var current = backend.ReadCurrent("game");

            Assert.NotNull(current);
            Assert.Equal("game", current!.Target);
            Assert.Equal(-6, current.PreampDb, 3);
            Assert.Equal("25 0; 40 -3", current.GraphicEqString);
        }

        [Fact]
        public void ReadCurrent_沒套用過回_null_而不是一份空設定()
        {
            // 這個差別就是這次的 bug：拿到一份全零的設定，按套用就把現況洗掉了。
            using var dir = new TempDir();
            Assert.Null(Backend(dir).ReadCurrent("game"));
        }

        [Fact]
        public void ReadCurrent_認不得的目標回_null()
        {
            using var dir = new TempDir();
            var backend = Backend(dir);
            string path = dir.File("out.txt");
            backend.Write(Intent("game"), path);
            backend.Apply(path);

            Assert.Null(backend.ReadCurrent("不存在的目標"));
        }

        [Fact]
        public void ReadCurrent_只拿自己那一段_不會撈到別的裝置()
        {
            using var dir = new TempDir();
            var backend = new EqualizerApoBackend(
                new ApoSettings { ConfigDirectory = dir.Path, FragmentFileName = "audiozen.txt" },
                new RouteTable(new[]
                {
                    new AudioRoute { Id = "game", DevicePattern = "CABLE Input", Processes = { "eldenring.exe" } },
                    new AudioRoute { Id = "voice", DevicePattern = "Voicemeeter AUX Input", Processes = { "discord.exe" } },
                }));

            string path = dir.File("out.txt");
            backend.Write(new AudioIntent
            {
                MessageForUser = "好了",
                Configs = new List<AudioTargetConfig>
                {
                    new AudioTargetConfig { Target = "game", PreampDb = -6, GraphicEqString = "25 4" },
                    new AudioTargetConfig { Target = "voice", PreampDb = -2, GraphicEqString = "25 -1" },
                },
            }, path);
            backend.Apply(path);

            Assert.Equal(-6, backend.ReadCurrent("game")!.PreampDb, 3);
            Assert.Equal("25 -1", backend.ReadCurrent("voice")!.GraphicEqString);
        }

        [Fact]
        public void ReadCurrent_讀得回自己寫出去的小數_不受地區設定影響()
        {
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                // 小數點是逗號的地區，寫出去的 "-3,74" 讀回來會變成 -3。
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

                using var dir = new TempDir();
                var backend = Backend(dir);
                string path = dir.File("out.txt");
                backend.Write(new AudioIntent
                {
                    MessageForUser = "好了",
                    Configs = new List<AudioTargetConfig>
                    {
                        new AudioTargetConfig { Target = "game", PreampDb = -3.74, GraphicEqString = "25 1.5" },
                    },
                }, path);
                backend.Apply(path);

                Assert.Equal(-3.74, backend.ReadCurrent("game")!.PreampDb, 2);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void ReadCurrent_接得上面板_打開時不是一排零()
        {
            using var dir = new TempDir();
            var backend = Backend(dir);
            string path = dir.File("out.txt");
            backend.Write(Intent("game"), path);
            backend.Apply(path);

            var panel = new TuningViewModel();
            panel.LoadFrom(backend.ReadCurrent("game"));

            // 25 與 40 都落在 0~200 這一段，所以滑桿拿到的是兩者的平均。
            // 十五個點壓成七根滑桿本來就會這樣，重點是「不是零」。
            Assert.Equal(-1.5, panel.Bands[0].Gain, 3);
            Assert.Contains(panel.Bands, b => Math.Abs(b.Gain) > 0.001);
        }
    }
}
