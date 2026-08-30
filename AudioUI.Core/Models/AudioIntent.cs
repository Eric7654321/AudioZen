using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioUI
{
    // 模型回覆的音訊調整意圖。刻意不叫 EqConfig：裡面除了 EQ 還有 preamp、壓縮器與殘響，
    // 而且它描述的是「要達成什麼」，不是任何一種後端的設定檔格式。

    public class AudioIntent
    {
        [JsonPropertyName("message_for_user")]
        public string MessageForUser { get; set; }

        [JsonPropertyName("configs")]
        public List<AudioTargetConfig> Configs { get; set; }
    }

    public class AudioTargetConfig
    {
        [JsonPropertyName("target")]
        public string Target { get; set; }

        [JsonPropertyName("preamp_db")]
        public double PreampDb { get; set; }

        [JsonPropertyName("graphic_eq_string")]
        public string GraphicEqString { get; set; }

        // 可以是 null，而且 null 有意義：這個目標不要壓縮器 / 殘響。
        // 後端本來就在檢查，宣告成不可為 null 只是讓型別跟事實對不上。
        [JsonPropertyName("comp_json")]
        public List<MeldaEntry>? CompJson { get; set; }

        [JsonPropertyName("reverb_json")]
        public List<MeldaEntry>? ReverbJson { get; set; }

        // --- 以下不進 JSON：模型不會回這些，它們是設定檔與面板之間的往返資訊。 ---

        /// <summary>產生 <see cref="CompJson"/> 的那個 preset 代號。模型自由生成的沒有代號，所以是 null。</summary>
        [JsonIgnore]
        public string? CompPresetId { get; set; }

        [JsonIgnore]
        public string? ReverbPresetId { get; set; }

        /// <summary>
        /// 從既有設定檔讀回來的壓縮器參數，原封不動的 base64。
        ///
        /// 認不回是哪個 preset 時，這是唯一能把它原樣寫回去的東西——沒有它，面板按一次套用
        /// 就會把模型調出來的壓縮器靜靜清掉。
        /// </summary>
        [JsonIgnore]
        public string? CompChunk { get; set; }

        [JsonIgnore]
        public string? ReverbChunk { get; set; }
    }

    public class MeldaEntry{
        [JsonPropertyName("raw_key")] 
        public string RawKey { get; set; }

        [JsonConverter(typeof(ObjectToNativeConverter))]
        public object Value { get; set; }
    }

    /// <summary>把 JSON 的純量原樣還原成 object。Melda 的參數表混雜數字、布林與字串，
    /// 而編碼時要寫回原本的型別，所以不能一律當字串收。</summary>
public class ObjectToNativeConverter : JsonConverter<object?>{
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options){
            switch (reader.TokenType){
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.Number:
                    if(reader.TryGetInt64(out long l))
                        return l;
                    return reader.GetDouble();
                case JsonTokenType.String:
                    return reader.GetString();
                case JsonTokenType.Null:
                    return null;
                default:
                    return JsonDocument.ParseValue(ref reader).RootElement.Clone();
            }
        }

        public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options){
            // 靠 value.GetType() 決定要怎麼寫，所以 null 得先擋下來，否則是 NullReferenceException。
            if (value is null){
                writer.WriteNullValue();
                return;
            }
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
