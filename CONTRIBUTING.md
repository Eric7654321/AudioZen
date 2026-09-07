# 貢獻方式

這份是**給人與 agent 共用的操作規範**。agent（Claude Code / Codex 等）在這個 repo 動手之前先讀這份，
再開始寫東西。

## 一、不要 push 到 `main`

`main` 有 branch protection：**直推會被拒絕，只接受 merge 進來的 PR，而且 `build` 這個 check 要綠**。
限制對 admin 也生效，所以「我有權限」不是繞過的理由。任何改動——包含一行 typo、
包含「反正 CI 會綠」——都走同一條路：

```bash
git switch -c <type>/<短描述>        # 例：fix/voicemeeter-aux-collision
# ...改東西、commit...
git push -u origin HEAD
gh pr create --base main --assignee Eric7654321
gh pr merge --auto --rebase          # CI 綠了自己合，不用等人按
```

- **門檻是 CI，不是人。** `build` 綠就可以合，不需要誰 approve。
- **所以 PR 描述要寫「我哪裡不確定」**——沒有人會在合之前逐行看，你自己講的沒講到的地方，
  就是沒有人知道的地方。
- **不要求 approval 是有原因的**：agent 用 Eric 的帳號推 PR，而 GitHub 不讓作者 approve
  自己的 PR（review request 指向作者會被靜靜丟掉）。要求 approval 會讓 agent 的 PR 永遠合不了。
  jam 或其他人開的 PR 不受這個限制，該找 Eric 看就找。
- **CI 沒綠不要掛 auto-merge，也不要請人看**。先自己修到綠，或在 PR 裡寫清楚為什麼綠不了。
- **`--rebase`**：`main` 目前是線性的，維持它——出事時 `git log --oneline` 讀得動比什麼都重要。

## 二、CI 會跑什麼

| workflow | 觸發 | 內容 |
|---|---|---|
| `build` | push 到 `main`、對 `main` 的 PR、手動 | Windows 上 `dotnet build` + `dotnet test`；push 到 main 時另把摘要寫回 `ci-status` 分支 |
| `installer` | push 到 `main`、改到 `installer/` 或程式碼的 PR、手動 | 跑 `installer/Test-Setup.ps1`，用 Inno Setup 打包出 `AudioZen.Setup.exe` |

**這兩條都在 `windows-latest` 上跑，因為這個專案在 Linux 上編不起來**
（`net8.0-windows`、WPF、WASAPI、DPAPI）。所以：在沒有 Windows 的機器上改完，
**你沒有驗證過任何東西**——不要在 PR 裡寫「已測試」，寫「哪裡沒驗到」。

## 三、commit

Conventional Commits：`<type>(<scope>): <description>`，type 用 `feat` / `fix` / `refactor` / `test` / `docs` / `chore`。

**訊息只寫「當初在解什麼問題」，人話、短。不要寫機制**——機制寫進 code 註解，
因為讀 code 的人不會先去翻 git log。

## 四、PR 描述

固定四段，不要自創 section：

```
## Summary      這個 PR 在解什麼問題（不是「改了哪些檔案」）
## Changes      改了什麼，一行一項
## How to Test  第一句寫「我哪裡不確定」，再寫怎麼驗
## Checklist    自己勾
```

**`How to Test` 的第一句是「我哪裡不確定」，不是「都測過了」。** 沒有 Windows 就明講；
沒跑過安裝流程就明講。漏講一次，下一個人就得重新發現一次。

## 五、註解不要寫日記

註解回答的是「**現在為什麼長這樣**」，不是「這段 code 的歷史」。

```csharp
// ✗ 之前是取列舉到的第一台，換個插拔順序就換一條匯流排
// ✗ 這一輪要修的那個 bug
// ✗ 2026-09-07 改成補 GUID

// ○ 裝置的列舉順序會隨插拔改變，挑法不能跟著改變
// ○ APO 的比對只有 AND、沒有否定，所以短名稱分不開主匯流排與 AUX
```

判準：註解裡出現日期、「之前 / 原本 / 這次 / 已經不需要了」、第一人稱的施工紀錄，
八成就是日記。那些屬於 commit message 與 PR 描述，它們有日期、有作者、有 diff；
註解沒有，而且會活得比那次改動久。

## 六、不要進 repo 的東西

- `appsettings.json`（有 API key）、`config/`、錄音檔——已在 `.gitignore`，不要用 `-f` 繞過。
- API key 一律走環境變數或設定頁（DPAPI）。**發現 key 進了歷史就講，不要默默改掉。**
- 第三方安裝檔不打包進 repo 或發行檔；`installer/dependencies.json` 只放官方 URL 與釘死的 SHA-256，
  升級時要重新核對雜湊，不能改用不固定版本的 latest URL、也不能關掉驗證。
