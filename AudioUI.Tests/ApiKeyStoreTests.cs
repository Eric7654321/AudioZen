using Xunit;

namespace AudioUI.Tests
{
    public class ApiKeyStoreTests
    {
        private const string SampleKey = "AIzaSyTESTKEY_not_a_real_one_1234";

        [Fact]
        public void 存進去讀得回來()
        {
            using var dir = new TempDir();
            var store = new DpapiApiKeyStore(dir.File("apikey.dat"));

            store.Save(SampleKey);

            Assert.Equal(SampleKey, store.Read());
            Assert.True(store.HasKey);
        }

        [Fact]
        public void 磁碟上不是明文()
        {
            // 這是整個加密存在的理由，其他測試過了但這條沒過就等於白做。
            using var dir = new TempDir();
            string path = dir.File("apikey.dat");
            new DpapiApiKeyStore(path).Save(SampleKey);

            string onDisk = File.ReadAllText(path);

            Assert.DoesNotContain(SampleKey, onDisk);
            Assert.DoesNotContain("AIza", onDisk);
        }

        [Fact]
        public void 沒有檔案時回_null_而不是丟例外()
        {
            using var dir = new TempDir();
            var store = new DpapiApiKeyStore(dir.File("不存在.dat"));

            Assert.Null(store.Read());
            Assert.False(store.HasKey);
        }

        [Fact]
        public void 解不開的檔案等於沒有_key()
        {
            // 從別台機器或別的帳號複製過來就會這樣，設定頁該顯示未設定並請使用者重新輸入。
            using var dir = new TempDir();
            string path = dir.File("apikey.dat");
            File.WriteAllText(path, "這不是 base64 也不是密文");

            Assert.Null(new DpapiApiKeyStore(path).Read());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void 存空的等於清除(string? blank)
        {
            using var dir = new TempDir();
            string path = dir.File("apikey.dat");
            var store = new DpapiApiKeyStore(path);
            store.Save(SampleKey);

            store.Save(blank);

            Assert.False(File.Exists(path));
            Assert.Null(store.Read());
        }

        [Fact]
        public void 前後空白會被去掉()
        {
            using var dir = new TempDir();
            var store = new DpapiApiKeyStore(dir.File("apikey.dat"));

            store.Save($"  {SampleKey}  ");

            Assert.Equal(SampleKey, store.Read());
        }

        [Fact]
        public void 換一把會蓋掉舊的()
        {
            using var dir = new TempDir();
            var store = new DpapiApiKeyStore(dir.File("apikey.dat"));
            store.Save(SampleKey);

            store.Save("AIzaSyANOTHER_key_5678");

            Assert.Equal("AIzaSyANOTHER_key_5678", store.Read());
        }
    }

    public class ApiKeyDisplayTests
    {
        [Fact]
        public void 遮罩只露尾四碼()
        {
            var s = new GeminiSettings { ApiKey = "AIzaSyABCD1234" };
            Assert.Equal("****1234", s.Masked);
        }

        [Fact]
        public void 沒設定時顯示未設定()
        {
            Assert.Equal("未設定", new GeminiSettings().Masked);
        }

        [Fact]
        public void 極短的_key_不會露出全部()
        {
            Assert.Equal("****", new GeminiSettings { ApiKey = "ab" }.Masked);
        }

        [Fact]
        public void Redact_把網址裡的_key_換掉()
        {
            string message = "API 請求失敗 https://generativelanguage.googleapis.com/v1beta/models/m:generateContent?key=AIzaSySECRET123 逾時";

            string safe = GeminiSettings.Redact(message);

            Assert.DoesNotContain("AIzaSySECRET123", safe);
            Assert.Contains("key=***", safe);
            Assert.Contains("逾時", safe);
        }

        [Fact]
        public void Redact_沒有_key_的訊息原樣通過()
        {
            Assert.Equal("找不到裝置", GeminiSettings.Redact("找不到裝置"));
            Assert.Equal("", GeminiSettings.Redact(null));
        }
    }
}
