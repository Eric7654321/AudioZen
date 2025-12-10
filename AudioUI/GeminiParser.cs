using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions; // 建議加入這個處理 JSON 雜訊

namespace AudioUI
{
    public class GeminiParser
    {
        // --- 功能 4: 解析回傳並寫入 Config ---
        // 新增 deviceName 參數，預設為 null (不寫入)
        public string ParseAndWriteConfig(string rawResponse, string outputPath, Dictionary<string, string> deviceMap)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(rawResponse, options);

            if (geminiResponse?.Candidates == null || geminiResponse.Candidates.Count == 0)
                throw new Exception("No candidates found.");

            string innerJsonText = geminiResponse.Candidates[0].Content.Parts[0].Text;

            // 清理 Markdown
            innerJsonText = innerJsonText.Replace("```json", "").Replace("```", "").Trim();

            // 解析新的結構
            var eqResponse = JsonSerializer.Deserialize<EqConfig>(innerJsonText, options);

            using (StreamWriter sw = new StreamWriter(outputPath, false)) // false 表示覆寫檔案
            {
                if (eqResponse.Configs != null)
                {
                    foreach (var config in eqResponse.Configs)
                    {
                        // 1. 處理 Device 行
                        string targetKey = config.Target?.ToLower();

                        if (targetKey == "all")
                        {
                            // 如果是 all，通常不需要指定 Device，或者您可以根據需求決定是否要重置 Device 選擇
                            // 這裡示範：寫入一行註解，或者什麼都不寫代表全域
                             sw.WriteLine("# Global Setting");
                        }
                        else if (!string.IsNullOrEmpty(targetKey) && deviceMap.ContainsKey(targetKey))
                        {
                            // 根據 map 寫入實際裝置名稱
                            sw.WriteLine($"Device: {deviceMap[targetKey]}");
                        }
                        else
                        {
                            // 如果 AI 回傳了 first 但 map 裡沒有，可以選擇跳過或記錄錯誤
                            // 這裡選擇寫入一個預設註解以供除錯
                            sw.WriteLine($"# Unknown Target: {targetKey}");
                        }

                        // 2. 寫入 Preamp
                        sw.WriteLine($"Preamp: {config.PreampDb} dB");

                        // 3. 寫入 GraphicEQ
                        if (!string.IsNullOrEmpty(config.GraphicEqString))
                        {
                            sw.WriteLine($"GraphicEQ: {config.GraphicEqString}");
                        }

                        // 4. 加入一個空行分隔不同裝置的設定 (可選)
                        sw.WriteLine();
                    }
                }
            }

            return eqResponse.MessageForUser;
        }
    }
}