using Xunit;

namespace AudioUI.Tests
{
    /// <summary>
    /// 語音調整主線的測試。這條路徑上的每一個失效模式都是「安靜的」——套上空設定、
    /// 回退問了卻沒退、preset 存到錯的情境——編譯與型別都不會攔，只有這裡會。
    /// </summary>
    public class SituationManagerTests
    {
        private sealed class Rig
        {
            public FakeAudioBackend Backend { get; } = new();
            public FakeNotifier Notifier { get; } = new();
            public FakeSpeechInput Speech { get; } = new();
            public FakeLlmClient Llm { get; } = new();
            public FakeConfigStore Store { get; } = new();
            public FakeTextToSpeech Tts { get; } = new();
            public FakePreferencesStore Prefs { get; } = new();
            public FakeSampleRecorder Recorder { get; } = new();
            public FakeAppStateNotifier AppState { get; } = new();

            public SituationManager Manager() =>
                new SituationManager(Backend, Notifier, Speech, Llm, Store, Tts, Prefs, Recorder, AppState,
                                     recordFolder: @"C:\audiozen\record");

            /// <summary>兩題都答否＝保留這次設定、不存成 preset，也就是最常見的那條路。</summary>
            public Rig KeepsSetting()
            {
                Notifier.Answers.Enqueue(false);
                Notifier.Answers.Enqueue(false);
                return this;
            }

            public Task Run(int situationId = 3) =>
                Manager().RecordAndProcessAsync(situationId, @"C:\audiozen\voice.wav", @"C:\audiozen\eq.txt");
        }

        [Fact]
        public async Task 正常一輪_錄到的話送進模型_產生的設定被套用()
        {
            var rig = new Rig().KeepsSetting();
            rig.Speech.Base64 = "AAAA";
            rig.Llm.Transcript = "遊戲太吵";
            rig.Backend.WriteResult = "已經把遊戲音量調小了";

            await rig.Run();

            Assert.Equal(new[] { "AAAA" }, rig.Llm.Transcriptions);
            Assert.Equal("遊戲太吵", rig.Llm.Interpretations.Single().Text);
            Assert.Equal(@"C:\audiozen\eq.txt", rig.Backend.Writes.Single().Path);
            Assert.Equal(new[] { @"C:\audiozen\eq.txt" }, rig.Backend.Applied);
            Assert.Contains("已經把遊戲音量調小了", rig.Tts.Spoken);
            Assert.Equal(1, rig.Store.Saves);
        }

        [Fact]
        public async Task 正常一輪_錄音與清單在開口問之前就開始()
        {
            var rig = new Rig().KeepsSetting();

            await rig.Run();

            Assert.Equal(1, rig.AppState.Shown);
            Assert.Equal(@"C:\audiozen\record", rig.Recorder.Calls.Single().BaseFolder);
            Assert.Equal(5000, rig.Speech.Recordings.Single().DurationMs);
        }

        [Fact]
        public async Task 模型回不出東西時最多重試三次()
        {
            var rig = new Rig().KeepsSetting();
            rig.Backend.WriteResults.Enqueue(null);
            rig.Backend.WriteResults.Enqueue(null);
            rig.Backend.WriteResults.Enqueue("第三次才成形");

            await rig.Run();

            // 第一次 + 兩次重試。重試要重新問模型，不是拿同一份意圖再寫一次。
            Assert.Equal(3, rig.Backend.Writes.Count);
            Assert.Equal(3, rig.Llm.Interpretations.Count);
            Assert.Equal(new[] { @"C:\audiozen\eq.txt" }, rig.Backend.Applied);
        }

        [Fact]
        public async Task 重試用完仍然失敗時_不可以套用那個半截檔()
        {
            // 寫檔是先截斷再寫，所以失敗留下的是空檔；套上去等於把使用者的設定清成靜音。
            var rig = new Rig();
            rig.Backend.WriteResult = null;

            await rig.Run();

            Assert.Empty(rig.Backend.Applied);
            Assert.Empty(rig.Store.Pushes);
            Assert.Equal(0, rig.Store.Saves);
            Assert.Contains("很抱歉，無法產生有效指令，請稍後再試", rig.Tts.Spoken);
        }

        [Fact]
        public async Task 模型完全沒回應時_什麼都不做()
        {
            var rig = new Rig();
            rig.Llm.Intent = null;

            await rig.Run();

            Assert.Empty(rig.Backend.Writes);
            Assert.Empty(rig.Backend.Applied);
            Assert.Empty(rig.Store.Pushes);
        }

        [Fact]
        public async Task 使用者說取消時_退回上一份設定()
        {
            var rig = new Rig();
            rig.Notifier.Answers.Enqueue(true); // 回退確認
            rig.Store.PushFront(SituationIds.Transient, new SituationEntry { FileName = @"C:\audiozen\before.txt" });

            await rig.Run();

            // 先套新的，再套回舊的——使用者聽得到差別，所以兩次都要真的發生。
            Assert.Equal(new[] { @"C:\audiozen\eq.txt", @"C:\audiozen\before.txt" }, rig.Backend.Applied);
            Assert.Equal(new[] { SituationIds.Transient }, rig.Store.Pops);
        }

        [Fact]
        public async Task 使用者說取消但已經沒有更早的設定時_只出聲不套用()
        {
            var rig = new Rig();
            rig.Notifier.Answers.Enqueue(true);

            await rig.Run();

            Assert.Equal(new[] { @"C:\audiozen\eq.txt" }, rig.Backend.Applied);
            Assert.Contains("已經沒有更早的設定可以還原", rig.Tts.Spoken);
        }

        [Fact]
        public async Task 保留設定時_寫進暫存情境也寫進目前情境()
        {
            var rig = new Rig().KeepsSetting();

            await rig.Run(situationId: 3);

            Assert.Equal(new[] { SituationIds.Transient, "3" }, rig.Store.Pushes.Select(p => p.Id).ToArray());
        }

        [Fact]
        public async Task 本來就在暫存情境時_不重複寫一份()
        {
            // 語音喚醒走的就是 -1。寫兩次會讓「取消此設定」只退掉其中一份。
            var rig = new Rig().KeepsSetting();

            await rig.Run(situationId: -1);

            Assert.Equal(new[] { SituationIds.Transient }, rig.Store.Pushes.Select(p => p.Id).ToArray());
        }

        [Fact]
        public async Task 存成preset時_用新的代號並帶上錄音與說過的話()
        {
            var rig = new Rig();
            rig.Notifier.Answers.Enqueue(false); // 不回退
            rig.Notifier.Answers.Enqueue(true);  // 存成 preset
            rig.Llm.Transcript = "看影片時低音重一點";
            rig.Store.NextIdValue = "9";
            rig.Recorder.Folder = @"C:\audiozen\record\20260830";

            await rig.Run();

            var preset = rig.Store.Pushes.Last();
            Assert.Equal("9", preset.Id);
            Assert.Equal("看影片時低音重一點", preset.DisplayName);
            Assert.Equal(@"C:\audiozen\record\20260830", preset.RecordPath);
        }

        [Fact]
        public async Task 記憶開著時_偏好會跟著指令送進模型()
        {
            var rig = new Rig().KeepsSetting();
            rig.Prefs.Current.UserMemory = "我戴的是開放式耳機";
            rig.Prefs.Current.AiMemories.Add("這個人怕吵");

            await rig.Run();

            Assert.Equal(new[] { "我戴的是開放式耳機", "這個人怕吵" }, rig.Llm.Interpretations.Single().Memories!.ToArray());
        }

        [Fact]
        public async Task 記憶關掉時_一條都不帶進prompt()
        {
            var rig = new Rig().KeepsSetting();
            rig.Prefs.Current.UserMemory = "我戴的是開放式耳機";
            rig.Prefs.Current.AiMemories.Add("這個人怕吵");
            rig.Prefs.Current.UserMemoryEnabled = false;
            rig.Prefs.Current.AiMemoryEnabled = false;

            await rig.Run();

            Assert.Empty(rig.Llm.Interpretations.Single().Memories!);
        }

        [Fact]
        public async Task 問回退之前_一定要先問過使用者才動存放處()
        {
            var rig = new Rig().KeepsSetting();

            await rig.Run();

            Assert.Equal(new[] { "回退確認", "preset設定" }, rig.Notifier.Confirms.ToArray());
        }

        [Fact]
        public async Task 錄不到樣本時_整輪不能被拖垮()
        {
            // process loopback 要 Windows 10 build 20348 以上，舊機器上這裡會丟例外。
            // 設定此時已經套用出去了，讓它把後面的回退詢問與存檔一起帶走是最糟的收場。
            var rig = new Rig().KeepsSetting();
            rig.Recorder.Throws = new NotSupportedException("此功能需要 Windows 10 Build 20348 或以上版本。");

            await rig.Run();

            Assert.Equal(new[] { @"C:\audiozen\eq.txt" }, rig.Backend.Applied);
            Assert.Equal(new[] { SituationIds.Transient, "3" }, rig.Store.Pushes.Select(p => p.Id).ToArray());
            Assert.Equal(1, rig.Store.Saves);
        }
    }
}
