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

            // 環境變數優先於檔案，讓 CI / 臨時測試不必在磁碟上留 key。
            string? fromEnv = Environment.GetEnvironmentVariable(GeminiSettings.ApiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnv))
                settings.Gemini.ApiKey = fromEnv;

            return settings;
        }
    }
}
