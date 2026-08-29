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
            return Task.FromResult(ConfirmResult);
        }

        public bool ConfirmResult { get; set; }
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
