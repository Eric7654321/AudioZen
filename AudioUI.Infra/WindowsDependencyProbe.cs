using System.Speech.Recognition;

namespace AudioUI
{
    /// <summary>只收集 Windows、分析磁碟現況及確認API key是否存在；缺項的產品判斷留在可測的 Core。</summary>
    public sealed class WindowsDependencyProbe : IDependencyProbe
    {
        private readonly IAudioBackend _backend;
        private readonly IAudioSessions _sessions;
        private readonly IApiKeyManager _apiKeys;
        private readonly ApoSettings _apo;

        public WindowsDependencyProbe(IAudioBackend backend, IAudioSessions sessions,
                                      IApiKeyManager apiKeys, ApoSettings apo)
        {
            _backend = backend;
            _sessions = sessions;
            _apiKeys = apiKeys;
            _apo = apo;
        }

        public DependencySnapshot Inspect()
        {
            IReadOnlyList<string> devices;
            try { devices = _sessions.RenderDeviceIdentities(); }
            catch { devices = Array.Empty<string>(); }

            bool speech = false;
            try
            {
                speech = SpeechRecognitionEngine.InstalledRecognizers()
                    .Any(r => string.Equals(r.Culture.Name, "zh-TW", StringComparison.OrdinalIgnoreCase));
            }
            catch { }

            return new DependencySnapshot
            {
                ApoInstalled = _backend.IsAvailable,
                CompressorInstalled = File.Exists(Path.Combine(_apo.VstDirectory, "Dynamics", "MCompressor.dll")),
                ReverbInstalled = File.Exists(Path.Combine(_apo.VstDirectory, "Reverb", "MCharmVerb.dll")),
                ZhTwSpeechInstalled = speech,
                ApiKeyConfigured = _apiKeys.IsConfigured,
                RenderDevices = devices,
            };
        }
    }
}
