using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace AudioUI
{
    /// <summary>
    /// 主視窗背後的狀態與流程。放這裡而不是 code-behind，是為了讓「按下去會發生什麼」
    /// 跟「按鈕長什麼樣」分開——前者會被改很多次，後者幾乎不動。
    ///
    /// 視窗仍然以自己當 DataContext，集合由 MainWindow 轉發過去，所以 XAML 的繫結路徑不變。
    /// </summary>
    public sealed class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly AudioSessionService _AudioService = new AudioSessionService();
        private readonly SituationManager _situations = AppConfig.CreateSituationManager();
        private readonly IConfigStore _store = AppConfig.ConfigStore;
        private readonly ITextToSpeech _TtsService = new TtsService();

        public ObservableCollection<AudioAppModel> AppList { get; } = new ObservableCollection<AudioAppModel>();
        public ObservableCollection<AudioAppModel> RecentAppList { get; } = new ObservableCollection<AudioAppModel>();
        public ObservableCollection<DeviceInfoModel> DeviceList { get; } = new ObservableCollection<DeviceInfoModel>();
        public ObservableCollection<ConfigOptionItem> ConfigOptions { get; } = new ObservableCollection<ConfigOptionItem>();
        public ObservableCollection<MacroKeyModel> MacroKeys { get; } = new ObservableCollection<MacroKeyModel>();
        public ObservableCollection<SituationSummary> ChatList { get; } = new ObservableCollection<SituationSummary>();
        public ObservableCollection<ChatMessageModel> ChatMessages { get; } = new ObservableCollection<ChatMessageModel>();

        /// <summary>手動調參面板的狀態。</summary>
        public TuningViewModel Tuning { get; } = new TuningViewModel();

        /// <summary>設定頁直接綁這一份；改完自動存，使用者不必再按一次儲存。</summary>
        public UserPreferences Preferences => AppConfig.Preferences.Current;

        private string _apiKeyStatus = "";

        /// <summary>測試連線的結果。空字串代表還沒測過。</summary>
        public string ApiKeyStatus
        {
            get => _apiKeyStatus;
            private set { _apiKeyStatus = value; Raise(); }
        }

        private DependencyReport _dependencies = DependencyChecker.Check(false, null, null);

        /// <summary>執行環境的體檢結果：APO 與虛擬裝置在不在。</summary>
        public DependencyReport Dependencies
        {
            get => _dependencies;
            private set { _dependencies = value; Raise(); Raise(nameof(DependencySummary)); }
        }

        public string DependencySummary => _dependencies.Summary;

        /// <summary>重新體檢。裝置會被插拔，所以這是隨時可以再跑一次的東西，不是啟動時算一次。</summary>
        public void RefreshDependencies() =>
            Dependencies = DependencyChecker.Check(
                AppConfig.AudioBackend.IsAvailable,
                _AudioService.GetRenderDeviceIdentities(),
                AppConfig.Routes);

        private string _routingStatus = "";

        /// <summary>自動接線的結果。空字串代表還沒接過。</summary>
        public string RoutingStatus
        {
            get => _routingStatus;
            private set { _routingStatus = value; Raise(); }
        }

        /// <summary>
        /// 把目前在播音訊、而且路由表認得的程式，各自指到該走的虛擬裝置。
        ///
        /// 這是「使用者只開一個 app」的第一步：原本要人去 Voicemeeter 與系統音量設定裡逐個點。
        /// 用到的是沒有文件的系統介面，所以每一條都把結果留下來——安靜地成功與安靜地失敗
        /// 看起來一模一樣。
        /// </summary>
        public void WireRouting()
        {
            var router = AppConfig.AppRouter;
            if (!router.IsSupported)
            {
                RoutingStatus = router.Route(0, null).Message;
                return;
            }

            var lines = new List<string>();
            int done = 0;

            foreach (var app in _AudioService.GetAppsWithConfig())
            {
                var route = AppConfig.Routes.ByProcess(app.Name);
                if (route == null || app.ProcessId <= 0) continue;

                var result = router.Route(app.ProcessId, route.Id);
                if (result.Ok) done++;
                else lines.Add($"{app.Name}：{result.Message}");
            }

            RoutingStatus = lines.Count == 0
                ? (done == 0 ? "沒有找到路由表認得、而且正在播放的程式。" : $"已接好 {done} 個程式。")
                : $"接好 {done} 個，其餘：{string.Join("　", lines)}";
        }

        /// <summary>目前這把 key 的樣子，只露尾四碼。</summary>
        public string ApiKeyMasked => AppConfig.Settings.Gemini.Masked;

        public MainWindowViewModel()
        {
            // 撥一個開關就存一次。TextBox 預設是失焦才寫回來源，所以不會每打一個字存一次。
            Preferences.PropertyChanged += (_, _) => AppConfig.Preferences.Save();
            Preferences.AiMemories.CollectionChanged += (_, _) => AppConfig.Preferences.Save();
        }

        /// <summary>存下設定頁輸入的 key。</summary>
        public void SaveApiKey(string? apiKey)
        {
            try
            {
                AppConfig.SaveApiKey(apiKey);
                Raise(nameof(ApiKeyMasked));
                ApiKeyStatus = string.IsNullOrWhiteSpace(apiKey) ? "已清除" : "已儲存，建議按一次測試連線";
            }
            catch (Exception ex)
            {
                ApiKeyStatus = $"儲存失敗：{ex.Message}";
            }
        }

        /// <summary>
        /// 實際打一次 API。key 對不對只有 Google 說了算——存得起來不代表能用，
        /// 而「存好了」卻在下一次語音指令才炸掉是最難查的那種。
        /// </summary>
        public async Task TestApiKeyAsync()
        {
            if (!AppConfig.IsConfigured)
            {
                ApiKeyStatus = "還沒有 key";
                return;
            }

            ApiKeyStatus = "測試中…";
            try
            {
                var intent = await AppConfig.LlmClient.InterpretAsync("測試連線");
                ApiKeyStatus = intent != null ? "連線正常" : "連得上，但回應看不懂";
            }
            catch (Exception ex)
            {
                ApiKeyStatus = $"連線失敗：{GeminiSettings.Redact(ex.Message)}";
            }
        }

        public void RemoveAiMemory(string? memory)
        {
            if (Preferences.RemoveAiMemory(memory)) AppConfig.Preferences.Save();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>目前選中的情境；-1 代表語音指令用的暫存情境。</summary>
        public int CurrentSituationId { get; set; } = -1;

        public SortMode CurrentSortMode { get; set; } = SortMode.NameAsc;

        public void Load() => _store.Load();

        /// <summary>錄一段語音指令、解析、套用，然後把畫面刷新到新狀態。</summary>
        public async Task RecordAndProcessCurrentAsync()
        {
            string configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            string audioPath = Path.Combine(configDir, "command.wav");
            string configPath = Path.Combine(configDir, $"config_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            try
            {
                if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);

                _TtsService.Stop();
                await _situations.RecordAndProcessAsync(CurrentSituationId, audioPath, configPath, 5000);

                RefreshConfigOptions();
                RefreshChatList();
                if (CurrentSituationId != -1) LoadChatHistory(CurrentSituationId.ToString());

                ApplyConfigToAPO(configPath);
            }
            catch (Exception ex)
            {
                AppConfig.Notifier.Notify("錄音處理失敗", GeminiSettings.Redact(ex.Message));
            }
        }

        public void RefreshChatList()
        {
            ChatList.Clear();
            var sessions = _store.Summaries();
            foreach (var session in sessions)
            {
                ChatList.Add(session);
            }
        }

        public void LoadChatHistory(string id)
        {
            ChatMessages.Clear();

            var mapItem = _store.ById(id);

            if (mapItem == null)
            {
                AppConfig.Notifier.Notify("找不到情境", $"沒有 ID 為 {id} 的資料");
                return;
            }

            string recordPath = mapItem.RecordPath ?? "";
            var history = _store.History(id, 20);

            // 填入 ChatMessages
            foreach (var msg in history)
            {
                if (!string.IsNullOrEmpty(msg.UserInput))
                {
                    ChatMessages.Add(new ChatMessageModel
                    {
                        IsUser = true,
                        Message = msg.UserInput
                    });
                }

                if (!string.IsNullOrEmpty(msg.AiResponse))
                {
                    ChatMessages.Add(new ChatMessageModel
                    {
                        IsUser = false,
                        Message = msg.AiResponse,
                        AudioFolderPath = recordPath
                    });
                }
            }
        }

        public string GetBtnIdByKeyCode(int code)
        {
            if (code == 103) return "btn01"; if (code == 104) return "btn02"; if (code == 105) return "btn03";
            if (code == 100) return "btn04"; if (code == 101) return "btn05"; if (code == 102) return "btn06";
            if (code == 97) return "btn07"; if (code == 98) return "btn08"; if (code == 99) return "btn09";
            if (code == 96) return "btn10"; if (code == 110) return "btn11"; if (code == 13) return "btn12";
            return "";
        }

        public async Task ExecuteConfig(string configId)
        {
            if (configId == "cmd_rollback")
            {
                try { await _situations.ConfigRollback(SituationIds.Transient); AppConfig.Notifier.Notify("快捷鍵觸發", "↩️ 已回復上一個設定"); RefreshConfigOptions(); }
                catch { AppConfig.Notifier.Notify("無法回復", "沒有歷史紀錄可供還原。"); }
                return;
            }
            else if (configId == "cmd_mute" || configId == SituationIds.Mute)
            {
                var muteItem = _store.ById(SituationIds.Mute);
                if (muteItem != null && muteItem.FileDatas.Count > 0) { ApplyConfigToAPO(muteItem.FileDatas[0].FileName); AppConfig.Notifier.Notify("快捷鍵觸發", "🔇 已全域靜音"); }
                else AppConfig.Notifier.Notify("錯誤", "找不到靜音設定檔");
                return;
            }
            else
            {
                var mapItem = _store.ById(configId);
                if (mapItem == null) { _store.Load(); mapItem = _store.ById(configId); }

                if (mapItem != null && mapItem.FileDatas.Count > 0)
                {
                    ApplyConfigToAPO(mapItem.FileDatas[0].FileName);
                    string name = string.IsNullOrEmpty(mapItem.ChatName) ? $"情境 {configId}" : mapItem.ChatName;
                    AppConfig.Notifier.Notify("設定已套用", $"⚡ 已切換至：{name}");
                }
                else AppConfig.Notifier.Notify("設定失敗", $"找不到情境 ID: {configId}");
            }
        }

        /// <summary>
        /// 把面板上的設定寫成檔案並套用。走的是跟語音指令同一條路
        /// （<see cref="IAudioBackend"/> 的 Write 再 Apply），差別只在意圖不是模型產生的。
        /// </summary>
        public void ApplyTuning()
        {
            string configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            string configPath = Path.Combine(configDir, $"tuning_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            try
            {
                if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);

                string? message = AppConfig.AudioBackend.Write(Tuning.BuildIntent(), configPath);
                if (message == null)
                {
                    AppConfig.Notifier.Notify("套用失敗", "產生設定檔失敗");
                    return;
                }

                ApplyConfigToAPO(configPath);
                RefreshAudioApps();
                AppConfig.Notifier.Notify("已套用", $"{Tuning.TargetName}：{Tuning.ToneText}");
            }
            catch (Exception ex)
            {
                AppConfig.Notifier.Notify("套用失敗", ex.Message);
            }
        }

        /// <summary>
        /// 打開面板前把現況填進去。不讀的話面板是一排零，而按下套用就會把
        /// 目前生效的設定整個洗掉——使用者不會知道自己剛剛清掉了什麼。
        /// </summary>
        public void BeginTuning(string targetId, string targetName)
        {
            Tuning.TargetId = targetId;
            Tuning.TargetName = targetName;
            Tuning.LoadFrom(AppConfig.AudioBackend.ReadCurrent(targetId));
        }

        public void ApplyConfigToAPO(string sourcePath)
        {
            try { AppConfig.AudioBackend.Apply(sourcePath); }
            catch (Exception ex) { AppConfig.Notifier.Notify("套用失敗", ex.Message); }
        }

        // --- 4. UI 互動與初始化 ---
        public void InitDevices()
        {
            DeviceList.Clear();
            DeviceList.Add(new DeviceInfoModel { Name = "自定義宏鍵盤", Description = "交大創客特供版", ImagePath = "keyboard.png" });
            DeviceList.Add(new DeviceInfoModel { Name = "g304", Description = "Logitech G304 Lightspeed", ImagePath = "mouse.png" });
            DeviceList.Add(new DeviceInfoModel { Name = "Mouse", Description = "Standard Pointing Device", ImagePath = "hamster.png" });

            AppConfig.Preferences.Current.ApplyDeviceImages(DeviceList);
        }

        /// <summary>換掉某台裝置的卡片圖。傳空路徑還原成內建的圖。</summary>
        public void SetDeviceImage(string deviceName, string? imagePath)
        {
            AppConfig.Preferences.Current.SetDeviceImage(deviceName, imagePath);
            AppConfig.Preferences.Save();
            InitDevices();
        }

        public void InitMuteConfig()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string configDir = Path.Combine(baseDir, "config");
            if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);

            string muteFilePath = Path.Combine(configDir, "mute.txt");
            if (!File.Exists(muteFilePath)) File.WriteAllText(muteFilePath, "Device: all\r\nPreamp: -100 dB\r\n# System Mute Config");

            var existing = _store.ById(SituationIds.Mute);
            if (existing == null)
            {
                var muteData = new SituationEntry { FileName = muteFilePath, UserInput = "系統強制靜音", AiResponse = "已將前級擴大 (Preamp) 設為 -100dB。" };
                _store.PushFront(SituationIds.Mute, muteData, "全域靜音", "");
                _store.Save();
            }
        }

        public void RefreshConfigOptions()
        {
            ConfigOptions.Clear(); _store.Load();
            ConfigOptions.Add(new ConfigOptionItem { SituationId = "cmd_unbind", DisplayName = "解除綁定 (Unbind)", Description = "清除按鍵設定" });
            ConfigOptions.Add(new ConfigOptionItem { SituationId = "cmd_rollback", DisplayName = "Rollback", Description = "回復上一個操作" });

            foreach (var map in _store.Situations.Where(x => x.Id != SituationIds.Transient))
            {
                var latest = map.FileDatas.FirstOrDefault();
                if (latest != null) ConfigOptions.Add(new ConfigOptionItem { SituationId = map.Id, DisplayName = string.IsNullOrEmpty(map.ChatName) ? $"情境 {map.Id}" : map.ChatName, Description = latest.UserInput ?? "AI 設定", FilePath = latest.FileName });
            }
        }

        public void RefreshAudioApps()
        {
            AppList.Clear(); RecentAppList.Clear();
            var globalApp = new AudioAppModel { Name = "整體調整", SystemVolume = 100, Config = new AppConfigData { TargetDevice = "System" } };
            RecentAppList.Add(globalApp); AppList.Add(globalApp);
            try { var sessions = _AudioService.GetAppsWithConfig(); var sessionList = new List<AudioAppModel>(sessions); foreach (var app in sessionList.Take(3)) RecentAppList.Add(app); var sorted = CurrentSortMode switch { SortMode.NameDesc => sessionList.OrderByDescending(x => x.Name), SortMode.VolumeDesc => sessionList.OrderByDescending(x => x.SystemVolume), _ => sessionList.OrderBy(x => x.Name) }; foreach (var app in sorted) AppList.Add(app); } catch { }
        }

        /// <summary>
        /// 送出一句文字指令：問模型、寫設定、產生預覽音檔、留下紀錄。
        /// 刻意不直接套用——使用者要先聽過預覽再決定。
        /// </summary>
        public async Task SendAdjustmentAsync(string userText)
        {
            ChatMessages.Add(new ChatMessageModel { IsUser = true, Message = userText });

            var thinkingMsg = new ChatMessageModel { IsUser = false, Message = "思考中..." };
            ChatMessages.Add(thinkingMsg);

            try
            {
                AudioIntent? intent = await AppConfig.LlmClient.InterpretAsync(
                    userText, AppConfig.Preferences.Current.MemoriesForPrompt());

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string configFileName = $"config_{timestamp}.txt";
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", configFileName);

                string aiMessage = AppConfig.AudioBackend.Write(intent, configPath) ?? "抱歉，我無法理解您的調整需求。";

                var currentChat = _store.ById(CurrentSituationId.ToString());
                string recordFolder = currentChat?.RecordPath ?? "";

                // 預覽音檔是拿該情境的第一段原始錄音去套新設定算出來的。
                string originalWav = Directory.Exists(recordFolder)
                    ? Directory.GetFiles(recordFolder, "*.wav").FirstOrDefault() ?? ""
                    : "";
                string previewWavPath = (!string.IsNullOrEmpty(originalWav)
                    ? AudioProcessor.GeneratePreview(originalWav, configPath)
                    : "") ?? "";

                var newData = new SituationEntry
                {
                    FileName = configPath,
                    UserInput = userText,
                    AiResponse = aiMessage
                };
                _store.PushFront(CurrentSituationId.ToString(), newData, userText, recordFolder);
                _store.Save();

                ChatMessages.Remove(thinkingMsg);
                ChatMessages.Add(new ChatMessageModel
                {
                    IsUser = false,
                    Message = aiMessage,
                    AudioFolderPath = !string.IsNullOrEmpty(previewWavPath) ? previewWavPath : recordFolder,
                    ConfigPath = configPath
                });

                RefreshChatList();
            }
            catch (Exception ex)
            {
                ChatMessages.Remove(thinkingMsg);
                // 例外訊息可能夾著帶 key 的網址，而這一行會留在聊天紀錄裡。
                ChatMessages.Add(new ChatMessageModel { IsUser = false, Message = $"發生錯誤: {GeminiSettings.Redact(ex.Message)}" });
            }
        }
    }
}
