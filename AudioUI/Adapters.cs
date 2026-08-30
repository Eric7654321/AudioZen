using System;

namespace AudioUI
{
    /// <summary>
    /// key 管理的實際接線。來源優先序（環境變數 → 設定頁 → appsettings.json）
    /// 住在 <see cref="AppConfig"/>。
    /// </summary>
    public sealed class AppConfigApiKeyManager : IApiKeyManager
    {
        public void Save(string? apiKey) => AppConfig.SaveApiKey(apiKey);

        public string Masked => AppConfig.Settings.Gemini.Masked;

        public bool IsConfigured => AppConfig.IsConfigured;
    }

    /// <summary>試聽音檔的實際產生器，包著 <see cref="AudioProcessor"/> 的取樣管線。</summary>
    public sealed class AudioProcessorPreview : IAudioPreview
    {
        public string? Generate(string inputWavPath, string configPath) =>
            AudioProcessor.GeneratePreview(inputWavPath, configPath);
    }
}
