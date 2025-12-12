using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AudioUI
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

        [JsonPropertyName("configs")]
        public List<EqConfigItem> Configs { get; set; }
    }

    public class EqConfigItem
    {
        [JsonPropertyName("target")]
        public string Target { get; set; }

        [JsonPropertyName("preamp_db")]
        public double PreampDb { get; set; }

        [JsonPropertyName("graphic_eq_string")]
        public string GraphicEqString { get; set; }

        [JsonPropertyName("comp_json")] 
        public List<MeldaEntry> CompJson { get; set; } 

        [JsonPropertyName("reverb_json")]
        public List<MeldaEntry> ReverbJson { get; set; }
    }

    public class MeldaEntry{
        [JsonPropertyName("raw_key")] 
        public string RawKey { get; set; }

        [JsonConverter(typeof(ObjectToNativeConverter))]
        public object Value { get; set; }
    }
}