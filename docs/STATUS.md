# AudioZen 目前狀態與待辦

## 待處理

按建議順序，前面的不做後面的做了也難驗。

**方案 A 剩下的**（讓使用者只需要開這一個 app）

- [ ] **Voicemeeter Remote API**：目前只做到「app → 虛擬裝置」，
      **虛擬裝置 → 實體喇叭仍要人手動在 Voicemeeter 裡接**。這塊不做，方案 A 不算完成
- [ ] 驗證 `AudioPolicyConfigRouter` 真的生效。顯示「已接好」也可能是假的——
      要去 Windows「系統 → 音效 → 音量合成器」確認該 app 的輸出裝置真的變了

**已知缺口**

- [ ] 卡片上的「音色調整」永遠顯示「無」。設定檔的回讀解析（`ConfigService.LoadConfig`）
      沒有任何地方呼叫，所以 `AudioAppInfo.Config` 對真實的 app 一律是 null。
      通知列走的是另一條路（`ApoConfigSummary`），那條是對的
- [ ] 「控制」分頁的「調整錄音檔」按鈕沒有接任何動作
- [ ] `DspPresets` 的數值是照參數範圍推的起點，**沒有實際試聽調過**

**刻意不做**

- 情境 id `"114514"` 維持字串常數。它同時是 `config/file_mapping.json` 的鍵，
  改成 enum 會讓既有存檔讀不回來
- 剩下的宣告層 nullable 警告。逐一修需要判斷每個欄位「真的可以是 null 嗎」，
  改錯會把 null 悄悄變成空字串
