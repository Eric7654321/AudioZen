using Xunit;

namespace AudioUI.Tests
{
    public class GeminiPromptTests
    {
        private static GeminiClient Client() =>
            new GeminiClient(new GeminiSettings { ApiKey = "K" }, RouteTable.Default());

        [Fact]
        public void 沒有記憶時_prompt_不多一個字()
        {
            // 記憶開關關掉時，送出去的東西要跟沒有這個功能時完全一樣。
            string withNull = Client().BuildPrompt("把低音調低", null);
            string withEmpty = Client().BuildPrompt("把低音調低", new string[0]);

            Assert.Equal(withNull, withEmpty);
            Assert.DoesNotContain("Known user preferences", withNull);
        }

        [Fact]
        public void 使用者指令排在最後()
        {
            // 記憶是背景脈絡，指令才是這次要做的事；順序反過來會讓模型照著舊偏好改。
            string prompt = Client().BuildPrompt("把低音調低", new[] { "偏好紮實的打擊感" });

            Assert.EndsWith("User Command: \"把低音調低\"", prompt);
            Assert.True(prompt.IndexOf("偏好紮實的打擊感") < prompt.IndexOf("User Command"));
        }

        [Fact]
        public void 每一條記憶都進得去()
        {
            string prompt = Client().BuildPrompt("調一下", new[] { "討厭金屬刺耳聲", "偏好紮實的打擊感" });

            Assert.Contains("討厭金屬刺耳聲", prompt);
            Assert.Contains("偏好紮實的打擊感", prompt);
            Assert.Contains("Known user preferences", prompt);
        }

        [Fact]
        public void 空白的記憶不佔一行()
        {
            string prompt = Client().BuildPrompt("調一下", new[] { "", "   ", "真的記憶" });

            // 基礎 prompt 整段沒有換行，所以每個 "\n- " 就是一條記憶。
            Assert.Equal(1, prompt.Split("\n- ").Length - 1);
            Assert.Contains("真的記憶", prompt);
        }

        [Fact]
        public void 記憶全是空白時等於沒有記憶()
        {
            string blank = Client().BuildPrompt("調一下", new[] { "", "  " });
            Assert.Equal(Client().BuildPrompt("調一下", null), blank);
        }

        [Fact]
        public void prompt_仍然帶著路由表生成的目標清單()
        {
            // 記憶區段不該把原本的內容擠掉。
            string prompt = Client().BuildPrompt("調一下", new[] { "記憶" });
            Assert.Contains("browser", prompt);
            Assert.Contains("graphic_eq_string", prompt);
        }

        [Fact]
        public void 偏好裡的記憶接得上_BuildPrompt()
        {
            var prefs = new UserPreferences { UserMemory = "我是重度耳機使用者" };
            prefs.AddAiMemory("討厭金屬刺耳聲");

            string prompt = Client().BuildPrompt("調一下", prefs.MemoriesForPrompt());

            Assert.Contains("我是重度耳機使用者", prompt);
            Assert.Contains("討厭金屬刺耳聲", prompt);
        }
    }
}
