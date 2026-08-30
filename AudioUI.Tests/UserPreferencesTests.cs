using Xunit;

namespace AudioUI.Tests
{
    public class UserPreferencesTests
    {
        private static JsonPreferencesStore Store(TempDir dir, out FakeNotifier notifier)
        {
            notifier = new FakeNotifier();
            return new JsonPreferencesStore(notifier, dir.File("preferences.json"));
        }

        [Fact]
        public void 沒有檔案時是一份預設值而不是_null()
        {
            using var dir = new TempDir();
            var store = Store(dir, out var notifier);

            store.Load();

            Assert.NotNull(store.Current);
            Assert.Equal(UserPreferences.DefaultWakeWord, store.Current.EffectiveWakeWord);
            Assert.Empty(notifier.Messages);
        }

        [Fact]
        public void 自學功能預設關閉()
        {
            // 它會在沒人要求的時候改變聲音，預設值本身就是一個決定。
            Assert.False(new UserPreferences().SelfLearningEnabled);
            Assert.True(new UserPreferences().AutoUpdate);
            Assert.True(new UserPreferences().UserMemoryEnabled);
        }

        [Fact]
        public void 存檔讀檔可以來回_而且中文不會變成跳脫序列()
        {
            using var dir = new TempDir();
            var store = Store(dir, out _);
            store.Current.WakeWord = "寶貝";
            store.Current.UserMemory = "我打遊戲時討厭金屬刺耳聲";
            store.Current.LaunchAtStartup = true;
            store.Current.AddAiMemory("使用者偏好較紮實的打擊感");
            store.Save();

            string raw = File.ReadAllText(dir.File("preferences.json"));
            Assert.Contains("寶貝", raw);
            Assert.DoesNotContain("\\u", raw);

            var reopened = Store(dir, out _);
            reopened.Load();

            Assert.Equal("寶貝", reopened.Current.WakeWord);
            Assert.True(reopened.Current.LaunchAtStartup);
            Assert.Equal("我打遊戲時討厭金屬刺耳聲", reopened.Current.UserMemory);
            Assert.Single(reopened.Current.AiMemories);
        }

        [Fact]
        public void 讀到壞掉的檔案只通知_不丟例外_而且留著預設值()
        {
            using var dir = new TempDir();
            File.WriteAllText(dir.File("preferences.json"), "{ 這不是 json");
            var store = Store(dir, out var notifier);

            store.Load();

            Assert.Single(notifier.Messages);
            Assert.Equal(UserPreferences.DefaultWakeWord, store.Current.EffectiveWakeWord);
        }

        [Fact]
        public void 舊檔案缺欄位時採用預設值()
        {
            // 加新偏好之後，使用者磁碟上的舊檔仍然要讀得起來。
            using var dir = new TempDir();
            File.WriteAllText(dir.File("preferences.json"), "{ \"wakeWord\": \"哈囉\" }");
            var store = Store(dir, out _);

            store.Load();

            Assert.Equal("哈囉", store.Current.WakeWord);
            Assert.True(store.Current.AutoUpdate);
            Assert.NotNull(store.Current.AiMemories);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void 喚醒詞被清空時退回預設值(string wakeWord)
        {
            // 空字串會讓辨識器建不出文法，等於整個喚醒功能靜靜地失效。
            Assert.Equal(UserPreferences.DefaultWakeWord,
                new UserPreferences { WakeWord = wakeWord }.EffectiveWakeWord);
        }

        [Fact]
        public void AddAiMemory_擋掉空白與重複()
        {
            var p = new UserPreferences();

            Assert.True(p.AddAiMemory("喜歡低頻"));
            Assert.False(p.AddAiMemory("喜歡低頻"));
            Assert.False(p.AddAiMemory("  喜歡低頻  "));
            Assert.False(p.AddAiMemory("   "));
            Assert.False(p.AddAiMemory(null));

            Assert.Single(p.AiMemories);
        }

        [Fact]
        public void RemoveAiMemory_刪得掉也刪不到()
        {
            var p = new UserPreferences();
            p.AddAiMemory("喜歡低頻");

            Assert.True(p.RemoveAiMemory("喜歡低頻"));
            Assert.False(p.RemoveAiMemory("喜歡低頻"));
            Assert.Empty(p.AiMemories);
        }

        [Fact]
        public void MemoriesForPrompt_兩個開關各自關掉自己那半()
        {
            var p = new UserPreferences { UserMemory = "我是誰" };
            p.AddAiMemory("模型記下的偏好");

            Assert.Equal(2, p.MemoriesForPrompt().Count);

            p.UserMemoryEnabled = false;
            Assert.Equal(new[] { "模型記下的偏好" }, p.MemoriesForPrompt().ToArray());

            p.AiMemoryEnabled = false;
            Assert.Empty(p.MemoriesForPrompt());
        }

        [Fact]
        public void MemoriesForPrompt_使用者沒寫自我介紹時不會塞空字串進去()
        {
            var p = new UserPreferences { UserMemory = "   " };
            Assert.Empty(p.MemoriesForPrompt());
        }
    }
}
