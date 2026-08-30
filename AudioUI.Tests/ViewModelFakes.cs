namespace AudioUI.Tests
{
    /// <summary>不列舉音效卡，清單由測試直接給。</summary>
    public sealed class FakeAudioSessions : IAudioSessions
    {
        public List<AudioAppInfo> Apps { get; } = new();

        public List<string> Devices { get; } = new();

        public int Listed { get; private set; }

        public IReadOnlyList<AudioAppInfo> List()
        {
            Listed++;
            return Apps;
        }

        public IReadOnlyList<string> RenderDeviceIdentities() => Devices;
    }

    /// <summary>key 放在記憶體，不碰 DPAPI。可以叫它在儲存時失敗。</summary>
    public sealed class FakeApiKeyManager : IApiKeyManager
    {
        public List<string?> Saved { get; } = new();

        public Exception? ThrowsOnSave { get; set; }

        public string Masked { get; set; } = "…abcd";

        public bool IsConfigured { get; set; } = true;

        public void Save(string? apiKey)
        {
            if (ThrowsOnSave != null) throw ThrowsOnSave;
            Saved.Add(apiKey);
        }
    }

    /// <summary>不跑取樣管線。回 null 代表這段錄音產不出試聽檔。</summary>
    public sealed class FakeAudioPreview : IAudioPreview
    {
        public string? Result { get; set; }

        public List<(string Input, string Config)> Calls { get; } = new();

        public string? Generate(string inputWavPath, string configPath)
        {
            Calls.Add((inputWavPath, configPath));
            return Result;
        }
    }

    /// <summary>不碰系統的路由介面。<see cref="IsSupported"/> 關掉就是取不到介面的機器。</summary>
    public sealed class FakeAppAudioRouter : IAppAudioRouter
    {
        public bool IsSupported { get; set; } = true;

        /// <summary>指定哪些 process 會失敗，其餘成功。</summary>
        public HashSet<int> Fails { get; } = new();

        public List<(int ProcessId, string? TargetId)> Routed { get; } = new();

        public string Message { get; set; } = "取不到系統的音訊路由介面。";

        public RouteResult Route(int processId, string? targetId)
        {
            if (!IsSupported) return RouteResult.Failure(Message);

            Routed.Add((processId, targetId));
            return Fails.Contains(processId)
                ? RouteResult.Failure("指定失敗")
                : RouteResult.Success("已指定");
        }

        public RouteResult ResetToSystemDefault(int processId) => RouteResult.Success("已還原");

        public string? CurrentDeviceId(int processId) => null;
    }
}
