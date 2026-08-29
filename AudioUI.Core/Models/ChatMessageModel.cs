using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioUI
{
    // UI 用對話模型 (純資料版)
    public class ChatMessageModel
    {
        public bool IsUser { get; set; }
        public string Message { get; set; }
        public string AudioFolderPath { get; set; } // 這是預覽音檔路徑
        public string ConfigPath { get; set; }      // ★★★ 新增：這是該次生成的 Config 路徑 ★★★

        public bool HasAudio => !string.IsNullOrEmpty(AudioFolderPath);
        // 如果有 ConfigPath，代表這是 AI 的回應，可以顯示「套用」按鈕
        public bool CanApply => !IsUser && !string.IsNullOrEmpty(ConfigPath);
    }
}
