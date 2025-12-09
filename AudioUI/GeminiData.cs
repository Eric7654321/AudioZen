using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WpfApp1
{
    // --- 1. Gemini API 回傳的結構 (外層) ---

    public class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<Candidate> Candidates { get; set; }
    }

    public class Candidate
    {
        [JsonPropertyName("content")]
        public Content Content { get; set; }
    }

    public class Content
    {
        [JsonPropertyName("parts")]
        public List<Part> Parts { get; set; }
    }

    public class Part
    {
        [JsonPropertyName("text")]
        public string Text { get; set; }
    }

    // --- 2. 你的 Prompt 要求生成的 EQ 設定結構 (內層) ---
    public class EqConfig
    {
        [JsonPropertyName("message_for_user")]
        public string MessageForUser { get; set; }

        [JsonPropertyName("preamp_db")]
        public double PreampDb { get; set; }

        // 這裡改為接收字串，因為現在是用 GraphicEQ 格式
        [JsonPropertyName("graphic_eq_string")]
        public string GraphicEqString { get; set; }
    }

}