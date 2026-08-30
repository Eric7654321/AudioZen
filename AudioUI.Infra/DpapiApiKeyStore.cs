using System.Security.Cryptography;
using System.Text;

namespace AudioUI
{
    /// <summary>
    /// 用 Windows DPAPI 把 API key 加密後存在 <c>config/apikey.dat</c>。
    ///
    /// 純文字擺在 exe 旁邊說不過去——這個專案的歷史正好是被硬編在原始碼裡的 key 咬過一次。
    /// DPAPI 綁在目前的 Windows 使用者帳號上：換帳號、換機器都解不開，而程式碼只有兩行。
    /// 再往上是 Credential Manager，但那要 P/Invoke，這裡的投報率不划算。
    /// </summary>
    public sealed class DpapiApiKeyStore : IApiKeyStore
    {
        /// <summary>額外的熵。跟 key 一起參與加解密，讓這份密文只有這個程式解得開。</summary>
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AudioZen.ApiKey.v1");

        private readonly string _filePath;

        public DpapiApiKeyStore(string? filePath = null)
        {
            _filePath = filePath
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "apikey.dat");
        }

        public bool HasKey => !string.IsNullOrEmpty(Read());

        public string? Read()
        {
            try
            {
                if (!File.Exists(_filePath)) return null;

                byte[] cipher = Convert.FromBase64String(File.ReadAllText(_filePath).Trim());
                byte[] plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
                string key = Encoding.UTF8.GetString(plain);

                return string.IsNullOrWhiteSpace(key) ? null : key;
            }
            catch
            {
                // 檔案壞掉、或是從別台機器/別的帳號複製過來的，都解不開。
                // 那跟「沒有 key」是同一件事，設定頁會顯示未設定並請使用者重新輸入。
                return null;
            }
        }

        public void Save(string? apiKey)
        {
            string trimmed = (apiKey ?? "").Trim();

            if (trimmed.Length == 0)
            {
                if (File.Exists(_filePath)) File.Delete(_filePath);
                return;
            }

            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            byte[] cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(trimmed), Entropy, DataProtectionScope.CurrentUser);

            File.WriteAllText(_filePath, Convert.ToBase64String(cipher));
        }
    }
}
