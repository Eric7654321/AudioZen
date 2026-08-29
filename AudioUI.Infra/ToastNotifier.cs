using Microsoft.Toolkit.Uwp.Notifications;

namespace AudioUI
{
    /// <summary>用 Windows 的 toast 通知實作 <see cref="INotifier"/>。</summary>
    public sealed class ToastNotifier : INotifier
    {
        public void Notify(string title, string context)
        {
            new ToastContentBuilder()
            .AddText(title)   // 標題
            .AddText(context) // 內文
            .Show(toast =>
            {
                toast.ExpirationTime = DateTimeOffset.Now.AddSeconds(5);
            });
        }

        public async Task<bool> ConfirmAsync(string title, string context)
        {
            // 1. 建立一個任務完成來源，用來當作暫停點
            var tcs = new TaskCompletionSource<bool>();

            // 2. 定義臨時的事件處理邏輯
            // 這裡我們用 lambda 抓取回傳結果
            void handler(ToastNotificationActivatedEventArgsCompat e)
            {
                var args = ToastArguments.Parse(e.Argument);

                // 檢查是否有 action 參數
                if (args.TryGetValue("action", out string action))
                {
                    // 根據使用者的選擇設定結果
                    if (action == "yes")
                    {
                        tcs.TrySetResult(true);
                    }
                    else
                    {
                        tcs.TrySetResult(false);
                    }
                }
            };

            // 3. 註冊事件監聽
            ToastNotificationManagerCompat.OnActivated += handler;

            try
            {
                // 4. 建構並發送通知
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(context)
                    .AddButton(new ToastButton("是", "action=yes"))
                    .AddButton(new ToastButton("否", "action=no"))
                    .Show();

                // 5. 等待使用者回應，或是等待 10 秒超時 (避免程式永遠卡住)
                // Task.WhenAny 會回傳最先完成的那個 Task
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(10000));

                if (completedTask == tcs.Task)
                {
                    // 使用者在時間內回應了
                    return await tcs.Task;
                }
                else
                {
                    // 超時
                    // 這裡可以選擇清除通知，避免過期點擊
                    ToastNotificationManagerCompat.History.Clear();
                    return false; // 預設回傳 false
                }
            }
            finally
            {
                // 6. 重要：無論結果如何，一定要取消註冊事件，避免記憶體洩漏或重複觸發
                ToastNotificationManagerCompat.OnActivated -= handler;
            }
        }
    }
}
