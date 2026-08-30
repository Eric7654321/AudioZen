namespace AudioUI.Tests
{
    /// <summary>把通知收下來供斷言，不真的跳 toast。</summary>
    public sealed class FakeNotifier : INotifier
    {
        public List<(string Title, string Message)> Messages { get; } = new();

        public void Notify(string title, string message) => Messages.Add((title, message));

        public Task<bool> ConfirmAsync(string title, string message)
        {
            Messages.Add((title, message));
            Confirms.Add(title);
            return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : ConfirmResult);
        }

        /// <summary>問過的是非題，依序。流程一次問兩題，光看 Messages 分不出誰是誰。</summary>
        public List<string> Confirms { get; } = new();

        /// <summary>依序回答；用完之後回 <see cref="ConfirmResult"/>。</summary>
        public Queue<bool> Answers { get; } = new();

        public bool ConfirmResult { get; set; }
    }

    /// <summary>不寫任何檔案的後端。<c>Write</c> 的回傳值可以排隊，用來重現模型連續回不成形內容。</summary>
    public sealed class FakeAudioBackend : IAudioBackend
    {
        public bool IsAvailable { get; set; } = true;

        /// <summary>依序回傳；用完之後一律回 <see cref="WriteResult"/>。</summary>
        public Queue<string?> WriteResults { get; } = new();

        public string? WriteResult { get; set; } = "已把遊戲音量調小";

        public List<(AudioIntent? Intent, string Path)> Writes { get; } = new();

        public List<string> Applied { get; } = new();

        public AudioTargetConfig? Current { get; set; }

        public string? Write(AudioIntent? intent, string outputPath)
        {
            Writes.Add((intent, outputPath));
            return WriteResults.Count > 0 ? WriteResults.Dequeue() : WriteResult;
        }

        public void Apply(string configFilePath) => Applied.Add(configFilePath);

        public AudioTargetConfig? ReadCurrent(string? targetId) => Current;
    }

    /// <summary>不打網路的模型。</summary>
    public sealed class FakeLlmClient : ILlmClient
    {
        public string Transcript { get; set; } = "遊戲太吵";

        public AudioIntent? Intent { get; set; } = new AudioIntent { MessageForUser = "好了" };

        public List<(string Text, IReadOnlyList<string>? Memories)> Interpretations { get; } = new();

        public List<string> Transcriptions { get; } = new();

        /// <summary>叫它失敗。網路錯誤的訊息常常夾著帶 key 的網址，那條路徑要驗得到。</summary>
        public Exception? Throws { get; set; }

        public Task<AudioIntent?> InterpretAsync(string userText, IReadOnlyList<string>? memories = null)
        {
            Interpretations.Add((userText, memories));
            if (Throws != null) return Task.FromException<AudioIntent?>(Throws);
            return Task.FromResult(Intent);
        }

        public Task<string> TranscribeAsync(string base64Wav)
        {
            Transcriptions.Add(base64Wav);
            return Task.FromResult(Transcript);
        }
    }

    /// <summary>不開麥克風。</summary>
    public sealed class FakeSpeechInput : ISpeechInput
    {
        public string Base64 { get; set; } = "UklGRg==";

        public List<(string Path, int DurationMs)> Recordings { get; } = new();

        public Task<string> RecordAsync(string filePath, int durationMs)
        {
            Recordings.Add((filePath, durationMs));
            return Task.FromResult(Base64);
        }
    }

    /// <summary>不出聲，把該講的話收下來供斷言。</summary>
    public sealed class FakeTextToSpeech : ITextToSpeech
    {
        public List<string> Spoken { get; } = new();

        public int Stops { get; private set; }

        public Task SpeakAsync(string text)
        {
            Spoken.Add(text);
            return Task.CompletedTask;
        }

        public void Stop() => Stops++;
    }

    /// <summary>記憶體版的情境存放處。不碰磁碟，也記下每一次寫入好斷言順序。</summary>
    public sealed class FakeConfigStore : IConfigStore
    {
        private readonly List<Situation> _situations = new();

        public List<(string Id, SituationEntry Entry, string DisplayName, string RecordPath)> Pushes { get; } = new();

        public List<string> Pops { get; } = new();

        public int Saves { get; private set; }

        public string NextIdValue { get; set; } = "7";

        public IReadOnlyList<Situation> Situations => _situations;

        public Situation? ById(string? id) => _situations.FirstOrDefault(x => x.Id == id);

        public IReadOnlyList<SituationSummary> Summaries() =>
            _situations.Select(s => new SituationSummary { Id = s.Id, DisplayName = s.ChatName }).ToList();

        public IReadOnlyList<SituationEntry> History(string id, int limit = 20) =>
            ById(id)?.FileDatas.Take(limit).Reverse().ToList() ?? new List<SituationEntry>();

        public string NextId() => NextIdValue;

        public void PushFront(string id, SituationEntry entry, string displayName = "", string recordPath = "")
        {
            Pushes.Add((id, entry, displayName, recordPath));

            var situation = ById(id);
            if (situation == null)
            {
                situation = new Situation { Id = id, ChatName = displayName, RecordPath = recordPath };
                _situations.Add(situation);
            }
            situation.FileDatas.Insert(0, entry);
        }

        public SituationEntry? PopFront(string id)
        {
            Pops.Add(id);

            var situation = ById(id);
            if (situation == null || situation.FileDatas.Count == 0) return null;

            var entry = situation.FileDatas[0];
            situation.FileDatas.RemoveAt(0);
            return entry;
        }

        public SituationEntry? Front(string id)
        {
            var situation = ById(id);
            return situation != null && situation.FileDatas.Count > 0 ? situation.FileDatas[0] : null;
        }

        public void Load() { }

        public void Save() => Saves++;
    }

    /// <summary>偏好放在記憶體，測試想改什麼直接改 <see cref="Current"/>。</summary>
    public sealed class FakePreferencesStore : IPreferencesStore
    {
        public UserPreferences Current { get; set; } = new UserPreferences();

        public int Saves { get; private set; }

        public void Load() { }

        public void Save() => Saves++;
    }

    /// <summary>不錄音。可以叫它失敗，重現 Windows 版本不支援 process loopback 的機器。</summary>
    public sealed class FakeSampleRecorder : ISampleRecorder
    {
        public string Folder { get; set; } = @"C:\audiozen\record\20260830";

        public Exception? Throws { get; set; }

        public List<(string BaseFolder, TimeSpan Duration)> Calls { get; } = new();

        public Task<string> RecordActiveAppsAsync(string baseFolder, TimeSpan duration)
        {
            Calls.Add((baseFolder, duration));
            return Throws != null ? Task.FromException<string>(Throws) : Task.FromResult(Folder);
        }
    }

    /// <summary>不跳 toast，只數有沒有被叫到。</summary>
    public sealed class FakeAppStateNotifier : IAppStateNotifier
    {
        public int Shown { get; private set; }

        public void ShowCurrentApps() => Shown++;
    }

    /// <summary>每個測試一個獨立的暫存目錄，結束時整個刪掉。</summary>
    public sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "audiozen-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
