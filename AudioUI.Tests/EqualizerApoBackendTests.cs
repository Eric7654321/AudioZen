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
    }
}
