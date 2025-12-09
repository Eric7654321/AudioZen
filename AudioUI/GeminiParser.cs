using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions; // 建議加入這個處理 JSON 雜訊

namespace WpfApp1
{
    public class GeminiParser
    {
        // --- 功能 4: 解析回傳並寫入 Config ---
        // 新增 deviceName 參數，預設為 null (不寫入)
        public string ParseAndWriteConfig(string rawResponse, string outputPath, string deviceName = null)
        {
            // 1. 解析 Gemini 的外層 JSON
            // 使用 CaseInsensitive 設定以防萬一
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(rawResponse, options);

            if (geminiResponse?.Candidates == null || geminiResponse.Candidates.Count == 0)
                throw new Exception("No candidates found in Gemini response.");

            // 2. 取出內層的文字
            string innerJsonText = geminiResponse.Candidates[0].Content.Parts[0].Text;

            // [重要] 清理 Markdown 標記
            // 有時候 AI 會回傳 ```json ... ```，這會導致解析失敗，這裡做簡單的清理
            innerJsonText = innerJsonText.Replace("```json", "").Replace("```", "").Trim();

            // 3. 解析內層 JSON (對應新的 EqConfig 結構)
            var eqConfig = JsonSerializer.Deserialize<EqConfig>(innerJsonText, options);

            // 4. 寫入檔案
            using (StreamWriter sw = new StreamWriter(outputPath, false))
            {
                // 寫入 Preamp (注意：Equalizer APO 接受 "Preamp: -3 dB" 格式)
                // 這裡我們確保寫入格式為小數點後 1 位或 2 位，視需求而定
                sw.WriteLine($"Preamp: {eqConfig.PreampDb} dB");

                // 如果有指定 Device 名稱，則寫入
                if (!string.IsNullOrWhiteSpace(deviceName))
                {
                    sw.WriteLine($"Device: {deviceName}");
                }

                // 寫入 GraphicEQ
                // AI 回傳的 graphic_eq_string 已經是 "25 2.2; 40 1.6; ..." 的格式
                if (!string.IsNullOrEmpty(eqConfig.GraphicEqString))
                {
                    sw.WriteLine($"GraphicEQ: {eqConfig.GraphicEqString}");
                }
            }

            // 回傳給 TTS 的訊息
            return eqConfig.MessageForUser;
        }
    }
}