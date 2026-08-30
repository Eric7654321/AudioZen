namespace AudioUI
{
    /// <summary>
    /// 「錄音 → 轉文字 → 解析 → 寫檔 → 套用 → 詢問回退」整條主線。
    ///
    /// 住在 Core 而不是 WPF 專案：這裡只認介面，測試才餵得進假的後端與模型，
    /// 也才驗得到「模型回不出東西時到底有沒有把空檔套上去」這種光看編譯不會知道的事。
    /// </summary>
    public class SituationManager
    {
        private readonly IAudioBackend _backend;
        private readonly INotifier _notifier;
        private readonly ISpeechInput _speech;
        private readonly ILlmClient _llm;
        private readonly IConfigStore _store;
        private readonly IPreferencesStore _prefs;
        private readonly ITextToSpeech _tts;
        private readonly ISampleRecorder _recorder;
        private readonly IAppStateNotifier _appState;
        private readonly string _recordFolder;

        /// <summary>
        /// 相依全部由外面給、沒有預設值：預設值會讓「忘了接線」在測試裡看起來一切正常，
        /// 而真正接線的地方只該有一處（<c>AppConfig.CreateSituationManager</c>）。
        /// </summary>
        public SituationManager(IAudioBackend backend, INotifier notifier, ISpeechInput speech,
                                ILlmClient llm, IConfigStore store, ITextToSpeech tts,
                                IPreferencesStore prefs, ISampleRecorder recorder,
                                IAppStateNotifier appState, string? recordFolder = null)
        {
            _backend = backend;
            _notifier = notifier;
            _speech = speech;
            _llm = llm;
            _store = store;
            _tts = tts;
            _prefs = prefs;
            _recorder = recorder;
            _appState = appState;
            _recordFolder = recordFolder
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "record");
        }

        /// <summary>
        /// 樣本錄音失敗不算整輪失敗。它只被拿來當 preset 的附件，而 process loopback 要
        /// Windows 10 build 20348 以上——在舊機器上它每次都會丟，讓它把後面的回退詢問與
        /// 存檔一起帶走，等於設定套出去了卻收不了尾。
        ///
        /// 不通知使用者：這件事在不支援的機器上是每次都發生，講了就是每次都吵。
        /// </summary>
        private async Task<string> RecordSampleAsync()
        {
            try { return await _recorder.RecordActiveAppsAsync(_recordFolder, TimeSpan.FromSeconds(6)); }
            catch { return ""; }
        }

        /// <summary>把某個情境退回上一份設定。已經沒有更早的紀錄時只出聲，不動後端。</summary>
        public async Task ConfigRollback(string IdString)
        {
            _store.PopFront(IdString);

            var previous = _store.Front(IdString);
            if (previous == null)
            {
                await _tts.SpeakAsync("已經沒有更早的設定可以還原");
                return;
            }

            _backend.Apply(previous.FileName);
        }

        /// <summary>
        /// 完整的進行一次錄音、分析與寫入的過程 (goal 1)
        /// </summary>
        public async Task RecordAndProcessAsync(int situationId, string audioFilePath, string eqConfigPath, int recordMs = 5000)
        {
            Task<string> recordPathTask = RecordSampleAsync();

            // 0. 第一次回應
            _appState.ShowCurrentApps();

            await _tts.SpeakAsync("請問您想如何調整音訊設定");
            // 1. 錄音recordMs 毫秒
            string audioBase64 = await _speech.RecordAsync(audioFilePath, recordMs);

            // 2. 提示正在解析
            _notifier.Notify("解析中", "心頻氣和正在分析內容");
            await _tts.SpeakAsync("正在解析中");

            // 3. 呼叫 Gemini API
            string transcribedText = await _llm.TranscribeAsync(audioBase64); // STT
            AudioIntent? intent = await _llm.InterpretAsync(transcribedText, _prefs.Current.MemoriesForPrompt());

            if (intent == null) return;

            // 4. 解析回傳並寫入 Config
            string? ttsMessage = _backend.Write(intent, eqConfigPath);
            int retryCount = 0;

            // 模型偶爾會回出不成形的內容，重試幾次通常就過了
            while (ttsMessage == null && retryCount < 3)
            {
                intent = await _llm.InterpretAsync(transcribedText, _prefs.Current.MemoriesForPrompt());
                ttsMessage = _backend.Write(intent, eqConfigPath);
                retryCount++;
            }
            if (ttsMessage == null)
            {
                // 寫檔是先截斷再寫，所以失敗留下的是空檔或半截檔；套用它等於把設定清掉。
                await _tts.SpeakAsync("很抱歉，無法產生有效指令，請稍後再試");
                return;
            }

            string situationIdString = situationId.ToString();
            _notifier.Notify("已套用設定", "心頻氣和已完成解析並套用設定");

            _backend.Apply(eqConfigPath);

            // 5. 使用 TTS 播放回應訊息
            await _tts.SpeakAsync(ttsMessage);

            await _tts.SpeakAsync("是否需要調整回原本的內容");

            SituationEntry newCreateData = new SituationEntry
            {
                FileName = eqConfigPath,
                UserInput = transcribedText,
                AiResponse = ttsMessage
            };

            string recordPath = await recordPathTask;

            // 6. 詢問是否套用設定
            _store.PushFront(SituationIds.Transient, newCreateData);
            if (await _notifier.ConfirmAsync("回退確認", "是否要取消此設定"))
            {
                // rollback
                await ConfigRollback(SituationIds.Transient);
            }
            else
            {
                if (situationIdString != SituationIds.Transient)
                {
                    _store.PushFront(situationIdString, newCreateData);
                }

                // 7. 詢問是否需要儲存成preset
                await _tts.SpeakAsync("是否需要存為preset");

                if (await _notifier.ConfirmAsync("preset設定", "是否要存為preset"))
                {
                    _store.PushFront(_store.NextId(), newCreateData, transcribedText, recordPath);
                }
            }

            // Inserted wait for one second as requested (非阻塞)
            await Task.Delay(1000);

            _notifier.Notify("設定結束", "調整已結束，請享受更好的聲音");
            // 儲存 Chat
            _store.Save();
        }
    }
}
