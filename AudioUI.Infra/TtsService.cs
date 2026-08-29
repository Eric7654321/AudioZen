using System.Speech.Synthesis; 

namespace AudioUI
{
    /// <summary>用 Windows 內建語音合成實作 <see cref="ITextToSpeech"/>。</summary>
    public sealed class TtsService : ITextToSpeech
    {
        private SpeechSynthesizer _synthesizer;

        public TtsService()
        {
            _synthesizer = new SpeechSynthesizer();

            // 設定音量 (0-100)
            _synthesizer.Volume = 100;
            _synthesizer.Rate = 1; // 語速 (-10 到 10)

            // 可以嘗試選擇語音 (如果系統有安裝中文語音的話)
            // _synthesizer.SelectVoiceByHints(VoiceGender.Female);
        }

        public Task SpeakAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return Task.CompletedTask;

            // 使用 Task.Run 避免卡住 UI 介面
            return Task.Run(() =>
            {
                _synthesizer.Speak(text);
            });
        }

        // 停止目前的發音
        public void Stop()
        {
            _synthesizer.SpeakAsyncCancelAll();
        }
    }
}
