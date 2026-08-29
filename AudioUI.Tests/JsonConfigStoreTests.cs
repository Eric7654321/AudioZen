using Xunit;

namespace AudioUI.Tests
{
    public class JsonConfigStoreTests
    {
        private static SituationEntry Entry(string file, string input = "說了什麼") =>
            new SituationEntry { FileName = file, UserInput = input, AiResponse = "回了什麼" };

        private static JsonConfigStore Store(TempDir dir, out FakeNotifier notifier)
        {
            notifier = new FakeNotifier();
            return new JsonConfigStore(notifier, dir.File("file_mapping.json"));
        }

        [Fact]
        public void PushFront_新情境用輸入內容當標題()
        {
            using var dir = new TempDir();
            var store = Store(dir, out _);

            store.PushFront("0", Entry("a.txt", "把低音調低"));

            Assert.Equal("把低音調低", store.ById("0")!.ChatName);
        }

        [Fact]
        public void PushFront_過長的標題會被截斷()
        {
            using var dir = new TempDir();
            var store = Store(dir, out _);

            store.PushFront("0", Entry("a.txt", new string('長', 30)));

            Assert.Equal(new string('長', 20) + "...", store.ById("0")!.ChatName);
        }

        [Fact]
        public void PushFront_最新的排最前面()
        {
            using var dir = new TempDir();
            var store = Store(dir, out _);

            store.PushFront("0", Entry("舊.txt"));
            store.PushFront("0", Entry("新.txt"));

            Assert.Equal("新.txt", store.Front("0")!.FileName);
        }

        [Fact]
        public void PopFront_退回上一筆_退到空為止()
        {
            using var dir = new TempDir();
            var store = Store(dir, out _);
            store.PushFront("0", Entry("舊.txt"));
            store.PushFront("0", Entry("新.txt"));

            store.PopFront("0");

            Assert.Equal("舊.txt", store.Front("0")!.FileName);
            store.PopFront("0");
            Assert.Null(store.Front("0"));
            Assert.Null(store.PopFront("0"));
        }

        [Fact]
        public void 情境找不到時各查詢都不炸()
        {
            using var dir = new TempDir();
            var store = Store(dir, out _);

            Assert.Null(store.ById("沒這個"));
            Assert.Null(store.Front("沒這個"));
            Assert.Null(store.PopFront("沒這個"));
            Assert.Empty(store.History("沒這個"));
        }

        [Fact]
        public void NextId_補上被刪掉留下的空號()
        {
            using var dir = new TempDir();
            var store = Store(dir, out _);
            store.PushFront("0", Entry("a"));
            store.PushFront("2", Entry("b"));

            Assert.Equal("1", store.NextId());
        }

        [Fact]
        public void Summaries_不列出暫存情境()
        {
            using var dir = new TempDir();
            var store = Store(dir, out _);
            store.PushFront(SituationIds.Transient, Entry("暫存"));
            store.PushFront("0", Entry("留著"));

            var summaries = store.Summaries();

            Assert.Single(summaries);
            Assert.Equal("0", summaries[0].Id);
        }

        [Fact]
        public void History_由舊到新_供畫面由上往下排()
        {
            using var dir = new TempDir();
            var store = Store(dir, out _);
            store.PushFront("0", Entry("第一"));
            store.PushFront("0", Entry("第二"));

            Assert.Equal(new[] { "第一", "第二" }, store.History("0").Select(e => e.FileName));
        }

        [Fact]
        public void 存檔讀檔可以來回_而且中文不會變成跳脫序列()
        {
            using var dir = new TempDir();
            var store = Store(dir, out _);
            store.PushFront("0", Entry("設定.txt", "把人聲拉清楚"));
            store.Save();

            var reloaded = Store(dir, out _);
            reloaded.Load();

            Assert.Equal("把人聲拉清楚", reloaded.Front("0")!.UserInput);
            Assert.Contains("把人聲拉清楚", File.ReadAllText(dir.File("file_mapping.json")));
        }

        [Fact]
        public void 讀到壞掉的檔案只通知_不丟例外()
        {
            using var dir = new TempDir();
            File.WriteAllText(dir.File("file_mapping.json"), "{ 這不是合法的 JSON");
            var store = Store(dir, out var notifier);

            store.Load();

            Assert.Contains(notifier.Messages, m => m.Title == "讀檔失敗");
            Assert.Empty(store.Situations);
        }

        [Fact]
        public void 檔案不存在時_Load_安靜地什麼都不做()
        {
            using var dir = new TempDir();
            var store = Store(dir, out var notifier);

            store.Load();

            Assert.Empty(notifier.Messages);
            Assert.Empty(store.Situations);
        }
    }
}
