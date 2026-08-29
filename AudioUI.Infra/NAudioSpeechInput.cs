using NAudio.Wave;

namespace AudioUI
{
    /// <summary>用 NAudio 從預設輸入裝置錄音。</summary>
    public sealed class NAudioSpeechInput : ISpeechInput
    {
        public Task<string> RecordAsync(string filePath, int durationMs)
        {
            // 1. 改成 TaskCompletionSource<string> 以便回傳字串
            var tcs = new TaskCompletionSource<string>();

            var waveFormat = new WaveFormat(44100, 16, 1);
            var waveIn = new WaveInEvent();
            waveIn.WaveFormat = waveFormat;

            var writer = new WaveFileWriter(filePath, waveIn.WaveFormat);

            waveIn.DataAvailable += (s, a) =>
            {
                writer.Write(a.Buffer, 0, a.BytesRecorded);
            };

            // 2. 將讀取檔案與回傳結果的邏輯移到 RecordingStopped 事件中
            waveIn.RecordingStopped += (s, a) =>
            {
                try
                {
                    // 必須先 Dispose 釋放檔案鎖定 (File Lock)
                    writer.Dispose();
                    waveIn.Dispose();

                    // 檢查錄音過程是否有錯誤
                    if (a.Exception != null)
                    {
                        tcs.SetException(a.Exception);
                        return;
                    }

                    // 此時檔案已經存檔完畢且解除鎖定，可以安全讀取
                    byte[] bytes = File.ReadAllBytes(filePath);
                    string returnString = Convert.ToBase64String(bytes);
                    //string configPath = Path.Combine(".", "fixedCommand.wav");
                    //byte[] bytes = File.ReadAllBytes(configPath);

                    // 設定 Task 完成並回傳字串
                    tcs.SetResult(returnString);
                }
                catch (Exception ex)
                {
                    // 捕捉讀檔過程可能發生的錯誤
                    tcs.SetException(ex);
                }
            };

            waveIn.StartRecording();

            // 設定計時器，時間到停止錄音 (這會觸發上面的 RecordingStopped 事件)
            Task.Delay(durationMs).ContinueWith(_ =>
            {
                waveIn.StopRecording();
            });

            // 回傳 Task，等待 RecordingStopped 裡的 SetResult 被呼叫
            return tcs.Task;
        }
    }
}
