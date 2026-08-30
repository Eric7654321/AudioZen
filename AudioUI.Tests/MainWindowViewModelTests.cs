using Xunit;

namespace AudioUI.Tests
{
    /// <summary>
    /// 主視窗狀態的測試。這一層的失效模式幾乎都是安靜的——清單少一筆、通知沒發、
    /// 錯誤訊息把 key 一起印出來——編譯與型別都不會攔。
    /// </summary>
    public class MainWindowViewModelTests
    {
        private sealed class Rig : IDisposable
        {
            public FakeAudioSessions Sessions { get; } = new();
            public FakeConfigStore Store { get; } = new();
            public FakeTextToSpeech Tts { get; } = new();
            public FakeNotifier Notifier { get; } = new();
            public FakeAudioBackend Backend { get; } = new();
            public FakeLlmClient Llm { get; } = new();
            public FakePreferencesStore Prefs { get; } = new();
            public FakeApiKeyManager ApiKeys { get; } = new();
            public FakeAudioPreview Preview { get; } = new();
            public FakeAppAudioRouter Router { get; } = new();
            public FakeSpeechInput Speech { get; } = new();
            public FakeSampleRecorder Recorder { get; } = new();
            public FakeAppStateNotifier AppState { get; } = new();
            public TempDir Dir { get; } = new();

            public RouteTable Routes { get; set; } = new RouteTable(new[]
            {
                new AudioRoute { Id = "browser", DisplayName = "chrome",
                                 DevicePattern = "Voicemeeter Input", Processes = { "chrome.exe" } },
                new AudioRoute { Id = "voice_chat", DisplayName = "discord",
                                 DevicePattern = "CABLE Input", Processes = { "discord.exe" } },
            });

            public MainWindowViewModel Build() =>
                new MainWindowViewModel(
                    Sessions,
                    new SituationManager(Backend, Notifier, Speech, Llm, Store, Tts, Prefs,
                                         Recorder, AppState, Dir.Path, settleDelayMs: 0),
                    Store, Tts, Notifier, Backend, Llm, Prefs, ApiKeys, Preview, Router, Routes, Dir.Path);

            public void Dispose() => Dir.Dispose();
        }

        private static AudioAppInfo App(string name, int volume = 50, int pid = 0) =>
            new AudioAppInfo { Name = name, SystemVolume = volume, ProcessId = pid };

        // --- 清單 ---

        [Fact]
        public void RefreshAudioApps_整體調整永遠在第一個()
        {
            using var rig = new Rig();
            rig.Sessions.Apps.Add(App("chrome"));
            var vm = rig.Build();

            vm.RefreshAudioApps();

            Assert.Equal("整體調整", vm.AppList[0].Name);
            Assert.Equal("整體調整", vm.RecentAppList[0].Name);
            // 它是唯一帶設定的一筆，卡片靠這個決定框線。
            Assert.True(vm.AppList[0].HasConfig);
        }

        [Fact]
        public void RefreshAudioApps_最近調整最多再放三個()
        {
            using var rig = new Rig();
            for (int i = 0; i < 6; i++) rig.Sessions.Apps.Add(App($"app{i}"));
            var vm = rig.Build();

            vm.RefreshAudioApps();

            Assert.Equal(4, vm.RecentAppList.Count);
            Assert.Equal(7, vm.AppList.Count);
        }

        [Fact]
        public void RefreshAudioApps_照排序模式排_整體調整不參與()
        {
            using var rig = new Rig();
            rig.Sessions.Apps.Add(App("bravo", volume: 10));
            rig.Sessions.Apps.Add(App("alpha", volume: 90));
            var vm = rig.Build();
            vm.CurrentSortMode = SortMode.VolumeDesc;

            vm.RefreshAudioApps();

            Assert.Equal(new[] { "整體調整", "alpha", "bravo" }, vm.AppList.Select(x => x.Name));
        }

        [Fact]
        public void RefreshAudioApps_重整不會把上一次的留著()
        {
            using var rig = new Rig();
            rig.Sessions.Apps.Add(App("chrome"));
            var vm = rig.Build();

            vm.RefreshAudioApps();
            vm.RefreshAudioApps();

            Assert.Equal(2, vm.AppList.Count);
        }

        // --- API key ---

        [Fact]
        public void SaveApiKey_存完提醒去測連線()
        {
            using var rig = new Rig();
            var vm = rig.Build();

            vm.SaveApiKey("AIza-secret");

            Assert.Equal(new[] { "AIza-secret" }, rig.ApiKeys.Saved);
            Assert.Contains("測試連線", vm.ApiKeyStatus);
        }

        [Fact]
        public void SaveApiKey_空的算清除()
        {
            using var rig = new Rig();
            var vm = rig.Build();

            vm.SaveApiKey("   ");

            Assert.Equal("已清除", vm.ApiKeyStatus);
        }

        [Fact]
        public void SaveApiKey_存不進去要講出來而不是靜靜失敗()
        {
            using var rig = new Rig();
            rig.ApiKeys.ThrowsOnSave = new InvalidOperationException("金鑰儲存區壞了");
            var vm = rig.Build();

            vm.SaveApiKey("AIza-secret");

            Assert.Contains("金鑰儲存區壞了", vm.ApiKeyStatus);
        }

        [Fact]
        public void SaveApiKey_要通知畫面重讀遮罩後的樣子()
        {
            using var rig = new Rig();
            var vm = rig.Build();
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.SaveApiKey("AIza-secret");

            // 不發這個通知的話，設定頁上的尾四碼會停在舊的那一把。
            Assert.Contains(nameof(vm.ApiKeyMasked), raised);
        }

        [Fact]
        public async Task TestApiKey_沒有_key_就不要打網路()
        {
            using var rig = new Rig();
            rig.ApiKeys.IsConfigured = false;
            var vm = rig.Build();

            await vm.TestApiKeyAsync();

            Assert.Equal("還沒有 key", vm.ApiKeyStatus);
            Assert.Empty(rig.Llm.Interpretations);
        }

        [Fact]
        public async Task TestApiKey_連得上就說正常()
        {
            using var rig = new Rig();
            var vm = rig.Build();

            await vm.TestApiKeyAsync();

            Assert.Equal("連線正常", vm.ApiKeyStatus);
        }

        [Fact]
        public async Task TestApiKey_失敗訊息不可以夾著_key()
        {
            using var rig = new Rig();
            rig.Llm.Throws = new HttpRequestException(
                "GET https://generativelanguage.googleapis.com/v1/models?key=AIzaSyTOPSECRET failed");
            var vm = rig.Build();

            await vm.TestApiKeyAsync();

            // 這句會被顯示出來，key 不能跟著跑出去。
            Assert.DoesNotContain("AIzaSyTOPSECRET", vm.ApiKeyStatus);
            Assert.StartsWith("連線失敗：", vm.ApiKeyStatus);
        }

        // --- 自動接線 ---

        [Fact]
        public void WireRouting_取不到系統介面就照實說()
        {
            using var rig = new Rig();
            rig.Router.IsSupported = false;
            rig.Router.Message = "這個 Windows 版本沒有這個介面。";
            var vm = rig.Build();

            vm.WireRouting();

            Assert.Equal("這個 Windows 版本沒有這個介面。", vm.RoutingStatus);
        }

        [Fact]
        public void WireRouting_只接路由表認得而且有_pid_的()
        {
            using var rig = new Rig();
            rig.Sessions.Apps.Add(App("chrome.exe", pid: 100));
            rig.Sessions.Apps.Add(App("notepad.exe", pid: 200));   // 不在路由表
            rig.Sessions.Apps.Add(App("discord.exe", pid: 0));     // 沒有 pid
            var vm = rig.Build();

            vm.WireRouting();

            Assert.Equal(new[] { (100, (string?)"browser") }, rig.Router.Routed);
            Assert.Equal("已接好 1 個程式。", vm.RoutingStatus);
        }

        [Fact]
        public void WireRouting_一個都沒接到不算成功()
        {
            using var rig = new Rig();
            var vm = rig.Build();

            vm.WireRouting();

            // 「接好 0 個」跟「沒有東西可接」是不同的處境，講錯會讓人去找不存在的問題。
            Assert.Equal("沒有找到路由表認得、而且正在播放的程式。", vm.RoutingStatus);
        }

        [Fact]
        public void WireRouting_部分失敗要點名是哪一個()
        {
            using var rig = new Rig();
            rig.Sessions.Apps.Add(App("chrome.exe", pid: 100));
            rig.Sessions.Apps.Add(App("discord.exe", pid: 200));
            rig.Router.Fails.Add(200);
            var vm = rig.Build();

            vm.WireRouting();

            Assert.Contains("接好 1 個", vm.RoutingStatus);
            Assert.Contains("discord.exe", vm.RoutingStatus);
        }

        // --- 快捷鍵套設定 ---

        [Fact]
        public async Task ExecuteConfig_找不到情境要說出是哪個_id()
        {
            using var rig = new Rig();
            var vm = rig.Build();

            await vm.ExecuteConfig("42");

            Assert.Contains(rig.Notifier.Messages, m => m.Message.Contains("42"));
            Assert.Empty(rig.Backend.Applied);
        }

        [Fact]
        public async Task ExecuteConfig_套用最新的那一份並報出情境名稱()
        {
            using var rig = new Rig();
            rig.Store.PushFront("3", new SituationEntry { FileName = "old.txt" }, "打遊戲");
            rig.Store.PushFront("3", new SituationEntry { FileName = "new.txt" }, "打遊戲");
            var vm = rig.Build();

            await vm.ExecuteConfig("3");

            Assert.Equal(new[] { "new.txt" }, rig.Backend.Applied);
            Assert.Contains(rig.Notifier.Messages, m => m.Message.Contains("打遊戲"));
        }

        [Fact]
        public async Task ExecuteConfig_靜音走的是靜音那一份()
        {
            using var rig = new Rig();
            rig.Store.PushFront(SituationIds.Mute, new SituationEntry { FileName = "mute.txt" }, "全域靜音");
            var vm = rig.Build();

            await vm.ExecuteConfig("cmd_mute");

            Assert.Equal(new[] { "mute.txt" }, rig.Backend.Applied);
        }

        [Fact]
        public async Task ExecuteConfig_真的回退了才說已回復()
        {
            using var rig = new Rig();
            rig.Store.PushFront(SituationIds.Transient, new SituationEntry { FileName = "old.txt" });
            rig.Store.PushFront(SituationIds.Transient, new SituationEntry { FileName = "new.txt" });
            var vm = rig.Build();

            await vm.ExecuteConfig("cmd_rollback");

            Assert.Equal(new[] { "old.txt" }, rig.Backend.Applied);
            Assert.Contains(rig.Notifier.Messages, m => m.Message.Contains("已回復"));
        }

        [Fact]
        public async Task ExecuteConfig_沒有歷史可回退時不會靜靜地什麼都不做()
        {
            using var rig = new Rig();
            var vm = rig.Build();

            await vm.ExecuteConfig("cmd_rollback");

            Assert.Contains(rig.Notifier.Messages, m => m.Title == "無法回復");
            Assert.DoesNotContain(rig.Notifier.Messages, m => m.Message.Contains("已回復"));
        }

        // --- 面板套用 ---

        [Fact]
        public void ApplyTuning_產不出設定檔就不要套用()
        {
            using var rig = new Rig();
            rig.Backend.WriteResult = null;
            var vm = rig.Build();

            vm.ApplyTuning();

            Assert.Empty(rig.Backend.Applied);
            Assert.Contains(rig.Notifier.Messages, m => m.Title == "套用失敗");
        }

        [Fact]
        public void ApplyTuning_寫出來的檔就是套用的那個()
        {
            using var rig = new Rig();
            var vm = rig.Build();

            vm.ApplyTuning();

            Assert.Equal(rig.Backend.Writes.Single().Path, rig.Backend.Applied.Single());
        }

        [Fact]
        public void BeginTuning_面板打開時讀的是目前生效的設定()
        {
            using var rig = new Rig();
            rig.Backend.Current = new AudioTargetConfig { Target = "browser", PreampDb = -6, GraphicEqString = "" };
            var vm = rig.Build();

            vm.BeginTuning("browser", "Chrome");

            // 不讀的話面板是一排零，按下套用就把現況洗掉。
            Assert.Equal("browser", vm.Tuning.TargetId);
            Assert.Equal("Chrome", vm.Tuning.TargetName);
        }

        // --- 聊天紀錄 ---

        [Fact]
        public void LoadChatHistory_找不到情境要通知而不是留一片空白()
        {
            using var rig = new Rig();
            var vm = rig.Build();

            vm.LoadChatHistory("99");

            Assert.Empty(vm.ChatMessages);
            Assert.Contains(rig.Notifier.Messages, m => m.Title == "找不到情境");
        }

        [Fact]
        public void LoadChatHistory_一問一答各成一則()
        {
            using var rig = new Rig();
            rig.Store.PushFront("3", new SituationEntry { UserInput = "太吵", AiResponse = "調小了" }, "打遊戲");
            var vm = rig.Build();

            vm.LoadChatHistory("3");

            Assert.Equal(2, vm.ChatMessages.Count);
            Assert.True(vm.ChatMessages[0].IsUser);
            Assert.False(vm.ChatMessages[1].IsUser);
        }

        // --- 送出文字指令 ---

        [Fact]
        public async Task SendAdjustment_思考中那則要被換掉()
        {
            using var rig = new Rig();
            rig.Backend.WriteResult = "已經調小了";
            var vm = rig.Build();

            await vm.SendAdjustmentAsync("遊戲太吵");

            Assert.DoesNotContain(vm.ChatMessages, m => m.Message == "思考中...");
            Assert.Equal(new[] { "遊戲太吵", "已經調小了" }, vm.ChatMessages.Select(m => m.Message));
            Assert.Equal(1, rig.Store.Saves);
        }

        [Fact]
        public async Task SendAdjustment_刻意不直接套用_使用者要先聽過()
        {
            using var rig = new Rig();
            var vm = rig.Build();

            await vm.SendAdjustmentAsync("遊戲太吵");

            Assert.Single(rig.Backend.Writes);
            Assert.Empty(rig.Backend.Applied);
        }

        [Fact]
        public async Task SendAdjustment_模型回不出東西時給一句人看得懂的話()
        {
            using var rig = new Rig();
            rig.Backend.WriteResult = null;
            var vm = rig.Build();

            await vm.SendAdjustmentAsync("嗯");

            Assert.Contains("無法理解", vm.ChatMessages.Last().Message);
        }

        [Fact]
        public async Task SendAdjustment_錯誤訊息不可以夾著_key()
        {
            using var rig = new Rig();
            rig.Llm.Throws = new HttpRequestException(
                "POST https://generativelanguage.googleapis.com/v1/x?key=AIzaSyTOPSECRET failed");
            var vm = rig.Build();

            await vm.SendAdjustmentAsync("遊戲太吵");

            // 這一則會被存進聊天紀錄，key 跟著寫進磁碟就收不回來了。
            Assert.DoesNotContain("AIzaSyTOPSECRET", vm.ChatMessages.Last().Message);
        }

        [Fact]
        public async Task SendAdjustment_沒有原始錄音就不產試聽檔()
        {
            using var rig = new Rig();
            var vm = rig.Build();

            await vm.SendAdjustmentAsync("遊戲太吵");

            Assert.Empty(rig.Preview.Calls);
        }

        // --- 裝置與設定清單 ---

        [Fact]
        public void InitDevices_使用者換過的圖會蓋掉內建的()
        {
            using var rig = new Rig();
            rig.Prefs.Current.SetDeviceImage("g304", @"D:\pics\cat.png");
            var vm = rig.Build();

            vm.InitDevices();

            Assert.Equal(@"D:\pics\cat.png", vm.DeviceList.Single(d => d.Name == "g304").ImagePath);
            Assert.Equal("keyboard.png", vm.DeviceList.Single(d => d.Name == "自定義宏鍵盤").ImagePath);
        }

        [Fact]
        public void SetDeviceImage_換完就存並且立刻反映在清單上()
        {
            using var rig = new Rig();
            var vm = rig.Build();
            vm.InitDevices();

            vm.SetDeviceImage("Mouse", @"D:\pics\hamster.png");

            Assert.Equal(@"D:\pics\hamster.png", vm.DeviceList.Single(d => d.Name == "Mouse").ImagePath);
            Assert.True(rig.Prefs.Saves > 0);
        }

        [Fact]
        public void RefreshConfigOptions_固定兩個指令在前面_暫存情境不列出來()
        {
            using var rig = new Rig();
            rig.Store.PushFront(SituationIds.Transient, new SituationEntry { FileName = "tmp.txt" }, "語音");
            rig.Store.PushFront("3", new SituationEntry { FileName = "game.txt", UserInput = "打遊戲" }, "打遊戲");
            var vm = rig.Build();

            vm.RefreshConfigOptions();

            Assert.Equal(new[] { "cmd_unbind", "cmd_rollback", "3" },
                         vm.ConfigOptions.Select(x => x.SituationId));
        }

        // --- 偏好 ---

        [Fact]
        public void 撥一個開關就存一次()
        {
            using var rig = new Rig();
            var vm = rig.Build();
            int before = rig.Prefs.Saves;

            vm.Preferences.AiMemories.Add("我喜歡重低音");

            Assert.True(rig.Prefs.Saves > before);
        }

        [Fact]
        public void RemoveAiMemory_刪掉不存在的不會白存一次()
        {
            using var rig = new Rig();
            var vm = rig.Build();
            int before = rig.Prefs.Saves;

            vm.RemoveAiMemory("沒有這條");

            Assert.Equal(before, rig.Prefs.Saves);
        }

        // --- 接線 ---

        [Fact]
        public void 相依沒有預設值_少接一條就當場炸()
        {
            using var rig = new Rig();

            // 有預設值的話，忘了接線在測試裡看起來會一切正常。
            Assert.Throws<ArgumentNullException>(() => new MainWindowViewModel(
                null!, null!, rig.Store, rig.Tts, rig.Notifier, rig.Backend, rig.Llm,
                rig.Prefs, rig.ApiKeys, rig.Preview, rig.Router, rig.Routes, rig.Dir.Path));
        }
    }
}
