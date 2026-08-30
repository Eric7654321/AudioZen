using System;
using System.Threading.Tasks;

namespace AudioUI
{
    /// <summary>
    /// <see cref="ISampleRecorder"/> 的真實實作：Windows 的 process loopback 擷取。
    ///
    /// 只是把靜態方法包成介面。留在 WPF 專案而不是 Infra，因為
    /// <see cref="PerProcessAudioRecorder"/> 要靠 <see cref="AudioSessionService"/> 決定錄哪些程式，
    /// 而那個型別回傳的模型帶著 WPF 的圖示。
    /// </summary>
    public sealed class ProcessLoopbackSampleRecorder : ISampleRecorder
    {
        public Task<string> RecordActiveAppsAsync(string baseFolder, TimeSpan duration) =>
            PerProcessAudioRecorder.RecordAllActiveAppsAsync(baseFolder, duration);
    }
}
