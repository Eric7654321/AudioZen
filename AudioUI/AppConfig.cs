using System;
using System.IO;
using System.Text.Json;

namespace AudioUI
{
    /// <summary>
    /// 設定的單一入口。key 只在這裡出現一次：多一個地方持有它，就多一個會漂移、
    /// 也多一個會跟著版控走的地方。刻意不用 const —— const 的值一定寫在原始碼裡。
    /// </summary>
    public static class AppConfig
    {
        private static readonly Lazy<AppSettings> _settings = new Lazy<AppSettings>(Load);

        private static readonly Lazy<RouteTable> _routes =
            new Lazy<RouteTable>(() => Settings.Routes is { Count: > 0 } r ? new RouteTable(r) : RouteTable.Default());

        public static AppSettings Settings => _settings.Value;

        /// <summary>app 與虛擬裝置的對應。所有需要這份知識的地方都從這裡拿，不各自持有一份。</summary>
        public static RouteTable Routes => _routes.Value;

        /// <summary>套用設定的後端。唯一知道設定該寫到哪裡的地方。</summary>
        public static IAudioBackend AudioBackend => _backend.Value;

        private static readonly Lazy<IAudioBackend> _backend =
            new Lazy<IAudioBackend>(() => new EqualizerApoBackend(Settings.Apo, Routes));

        /// <summary>對使用者說話的管道。</summary>
        public static INotifier Notifier => _notifier.Value;

        private static readonly Lazy<INotifier> _notifier = new Lazy<INotifier>(() => new ToastNotifier());

        /// <summary>語音回覆。</summary>
        public static ITextToSpeech TextToSpeech => _tts.Value;

        private static readonly Lazy<ITextToSpeech> _tts = new Lazy<ITextToSpeech>(() => new TtsService());

        /// <summary>麥克風輸入。</summary>
        public static ISpeechInput SpeechInput => _speech.Value;

        private static readonly Lazy<ISpeechInput> _speech = new Lazy<ISpeechInput>(() => new NAudioSpeechInput());

        /// <summary>設定是否可用。UI 想在送出前先擋掉可以看這個，不必接例外。</summary>
        public static bool IsConfigured => Settings.Gemini.IsConfigured;

        public static string GeminiUrl => Settings.Gemini.BuildGenerateContentUrl();

        /// <summary>情境與調整紀錄的存放處。開檔時載入一次，之後大家共用同一份。</summary>
        public static IConfigStore ConfigStore => _store.Value;

        private static readonly Lazy<IConfigStore> _store = new Lazy<IConfigStore>(() =>
        {
            var store = new JsonConfigStore(Notifier);
            store.Load();
            return store;
        });

        /// <summary>把個別程式的音訊輸出指到指定裝置。取不到系統介面時 IsSupported 是 false。</summary>
        public static IAppAudioRouter AppRouter => _router.Value;

        private static readonly Lazy<IAppAudioRouter> _router =
            new Lazy<IAppAudioRouter>(() => new AudioPolicyConfigRouter(Routes));

        /// <summary>加密後的 API key 儲存。設定頁寫、載入時讀。</summary>
        public static IApiKeyStore ApiKeys => _apiKeys.Value;

        private static readonly Lazy<IApiKeyStore> _apiKeys = new Lazy<IApiKeyStore>(() => new DpapiApiKeyStore());

        /// <summary>
        /// 存下使用者在設定頁輸入的 key，並讓這次執行立刻改用它。
        /// <see cref="GeminiClient"/> 持有的是同一個 <see cref="GeminiSettings"/> 物件、
        /// 而且每次呼叫才組網址，所以不必重啟。
        /// </summary>
        public static void SaveApiKey(string? apiKey)
        {
            ApiKeys.Save(apiKey);
            Settings.Gemini.ApiKey = ResolveApiKey();
        }

        /// <summary>使用者偏好。開檔時載入一次，設定頁改完自己呼叫 Save。</summary>
        public static IPreferencesStore Preferences => _prefs.Value;

        private static readonly Lazy<IPreferencesStore> _prefs = new Lazy<IPreferencesStore>(() =>
        {
            var store = new JsonPreferencesStore(Notifier);
            store.Load();
            return store;
        });

        /// <summary>列出目前在出聲的程式。</summary>
        public static IAppStateNotifier AppStateNotifier => _appState.Value;

        private static readonly Lazy<IAppStateNotifier> _appState =
            new Lazy<IAppStateNotifier>(() => new AppListNotifier());

        /// <summary>錄下各程式的輸出當樣本。</summary>
        public static ISampleRecorder SampleRecorder => _recorder.Value;

        private static readonly Lazy<ISampleRecorder> _recorder =
            new Lazy<ISampleRecorder>(() => new ProcessLoopbackSampleRecorder());

        /// <summary>
        /// 語音調整主流程的唯一接線處。<see cref="SituationManager"/> 住在 Core、相依沒有預設值，
        /// 所以「用哪個實作」這個決定只在這裡出現一次。
        /// </summary>
        public static SituationManager CreateSituationManager() =>
            new SituationManager(AudioBackend, Notifier, SpeechInput, LlmClient, ConfigStore,
                                 TextToSpeech, Preferences, SampleRecorder, AppStateNotifier);

        /// <summary>key 管理。</summary>
        public static IApiKeyManager ApiKeyManager => _keyManager.Value;

        private static readonly Lazy<IApiKeyManager> _keyManager =
            new Lazy<IApiKeyManager>(() => new AppConfigApiKeyManager());

        /// <summary>試聽音檔的產生。</summary>
        public static IAudioPreview AudioPreview => _preview.Value;

        private static readonly Lazy<IAudioPreview> _preview =
            new Lazy<IAudioPreview>(() => new AudioProcessorPreview());

        /// <summary>設定檔與錄音的落腳處。</summary>
        public static string ConfigDirectory =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");

        /// <summary>
        /// 主視窗狀態的唯一接線處。<see cref="MainWindowViewModel"/> 的相依沒有預設值，
        /// 所以「用哪個實作」這個決定只在這裡出現一次。
        /// </summary>
        public static MainWindowViewModel CreateMainWindowViewModel() =>
            new MainWindowViewModel(
                new AudioSessionService(),
                CreateSituationManager(),
                ConfigStore,
                TextToSpeech,
                Notifier,
                AudioBackend,
                LlmClient,
                Preferences,
                ApiKeyManager,
                AudioPreview,
                AppRouter,
                Routes,
                ConfigDirectory);

        /// <summary>把人話翻成音訊意圖的模型。</summary>
        public static ILlmClient LlmClient => _llm.Value;

        private static readonly Lazy<ILlmClient> _llm =
            new Lazy<ILlmClient>(() => new GeminiClient(Settings.Gemini, Routes));

        private static AppSettings Load()
        {
            var settings = new AppSettings();

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (File.Exists(path))
            {
                try
                {
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), AppSettings.JsonOptions)
                               ?? new AppSettings();
                }
                catch (JsonException ex)
                {
                    // 設定檔壞掉要講清楚是哪個檔，否則會被誤判成「key 沒填」。
                    throw new InvalidOperationException($"appsettings.json 格式錯誤：{ex.Message}", ex);
                }
            }

            _keyFromSettingsFile = settings.Gemini.ApiKey;
            settings.Gemini.ApiKey = ResolveApiKey();

            return settings;
        }

        /// <summary>appsettings.json 裡寫的 key。留著當最後的後備，免得既有的設定突然失效。</summary>
        private static string _keyFromSettingsFile = "";

        /// <summary>
        /// key 的來源優先序：環境變數 → 設定頁存的（加密） → appsettings.json。
        ///
        /// 環境變數最優先，讓 CI 與臨時測試不必在磁碟上留東西；設定頁排第二，
        /// 因為那是使用者最近一次明確表達的意思。
        /// </summary>
        private static string ResolveApiKey()
        {
            string? fromEnv = Environment.GetEnvironmentVariable(GeminiSettings.ApiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

            return ApiKeys.Read() ?? _keyFromSettingsFile;
        }
    }
}
