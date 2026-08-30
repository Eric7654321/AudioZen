using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Globalization;
using System.IO;
using System.Speech.Recognition;
using System.Windows;
using System.Windows.Threading;

namespace AudioUI
{
    public class WakeWordTrigger
    {
        private SpeechRecognitionEngine _recognizer;
        private SituationManager _situations = AppConfig.CreateSituationManager();

        // 這是主視窗的參考 (Pointer)，不要在裡面 new 新的！
        private MainWindow _currentMainWindow;

        public WakeWordTrigger(MainWindow mainWindow)
        {
            _currentMainWindow = mainWindow;
        }

        public void InitializeSpeechRecognition()
        {
            try
            {
                _recognizer = new SpeechRecognitionEngine(new CultureInfo("zh-TW"));

                // 喚醒詞是偏好，不是常數：hi-fi 的個人化頁要讓使用者自己錄一個。
                Choices commands = new Choices();
                commands.Add(new string[] { AppConfig.Preferences.Current.EffectiveWakeWord });

                GrammarBuilder gb = new GrammarBuilder();
                gb.Culture = new CultureInfo("zh-TW");
                gb.Append(commands);

                Grammar g = new Grammar(gb);
                _recognizer.LoadGrammar(g);

                _recognizer.SpeechRecognized += Recognizer_SpeechRecognizedAsync;
                _recognizer.SetInputToDefaultAudioDevice();
                _recognizer.RecognizeAsync(RecognizeMode.Multiple);
            }
            catch (Exception ex)
            {
                // 使用完整命名空間避免衝突
                System.Windows.MessageBox.Show($"語音引擎初始化失敗: {ex.Message}\n請確認電腦已安裝對應語言的語音識別套件。");
            }
        }

        private bool _isProcessing = false;

        private async void Recognizer_SpeechRecognizedAsync(object? sender, SpeechRecognizedEventArgs e)
        {
            // 暫停識別，避免處理中重複觸發
            //_recognizer.RecognizeAsyncStop();
            if (_isProcessing) return;
            _isProcessing = true;

            try
            {
                // ★★★ 修正：使用傳進來的 _currentMainWindow，而不是 new 一個新的 ★★★
                // 信心指數過濾
                if (e.Result.Confidence < _currentMainWindow.recognitionConfidience)
                {
                    _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                    return;
                }

                string command = e.Result.Text;

                string audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "command.wav");
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string configFileName = $"config_{timestamp}.txt";
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", configFileName);

                switch (command)
                {
                    case "心平氣和":
                        // 1. 播放提示音或通知 (選用)
                        // SendNotification("語音喚醒", "正在聆聽您的指令...");

                        // 2. 執行錄音與 AI 分析
                        await _situations.RecordAndProcessAsync(-1, audioPath, configPath, 5000);

                        // 3. ★★★ 補上這行：將生成的 Config 套用到 APO ★★★
                        ApplyConfigToAPO(configPath);

                        // 4. (選用) 嘗試刷新主視窗 UI (需要用 Dispatcher 回到 UI 執行緒)
                        // 雖然主要是在背景執行，但如果視窗開著，最好刷新一下列表
                        _currentMainWindow.Dispatcher.Invoke(() =>
                        {
                            // 這裡假設你有把 Refresh 方法設為 public，或是單純觸發 UI 更新
                            // _currentMainWindow.RefreshConfigOptions(); 
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"執行錯誤: {ex.Message}");
            }
            finally
            {
                // 恢復監聽
                //_recognizer.RecognizeAsync(RecognizeMode.Multiple);
                _isProcessing = false;
            }
        }

        // 視窗關閉時釋放資源
        public void Dispose()
        {
            if (_recognizer != null)
            {
                _recognizer.Dispose();
            }
        }

        private void ApplyConfigToAPO(string sourcePath)
        {
            try
            {
                AppConfig.AudioBackend.Apply(sourcePath);
            }
            catch (Exception ex)
            {
                SendNotification("套用失敗", ex.Message);
            }
        }

        private void SendNotification(string title, string content) => AppConfig.Notifier.Notify(title, content);
    }
}