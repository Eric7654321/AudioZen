using System;
using System.Globalization;
using System.IO;
using System.Speech.Recognition; // 引用語音識別命名空間
using System.Windows;
using System.Windows.Threading;

namespace AudioUI
{
    public class WakeWordTrigger
    {
        private SpeechRecognitionEngine _recognizer;
        private GeminiServices _geminiServices = new GeminiServices();
        private MainWindow _currentMainWindow;

        public WakeWordTrigger(MainWindow mainWindow)
        {
            _currentMainWindow = mainWindow;
        }

        public void InitializeSpeechRecognition()
        {
            try
            {
                // 1. 初始化語音識別引擎
                // 注意：這裡設定為中文 (zh-TW)，如果你的系統是英文版或要識別英文，請改為 new CultureInfo("en-US")
                _recognizer = new SpeechRecognitionEngine(new CultureInfo("zh-TW"));

                // 2. 定義要監聽的關鍵字 (Choices)
                Choices commands = new Choices();
                commands.Add(new string[] { "心平氣和" });

                // 3. 建立語法構建器並加載關鍵字
                GrammarBuilder gb = new GrammarBuilder();
                gb.Culture = new CultureInfo("zh-TW"); // 確保語法文化與引擎一致
                gb.Append(commands);

                // 4. 建立 Grammar 物件並載入引擎
                Grammar g = new Grammar(gb);
                _recognizer.LoadGrammar(g);

                // 5. 註冊事件：當語音被識別時觸發
                _recognizer.SpeechRecognized += Recognizer_SpeechRecognizedAsync;

                // 6. 設定輸入來源為預設麥克風
                _recognizer.SetInputToDefaultAudioDevice();

                // 7. 開始非同步識別 (RecognizeMode.Multiple 代表持續監聽，不會聽一次就停)
                _recognizer.RecognizeAsync(RecognizeMode.Multiple);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"語音引擎初始化失敗: {ex.Message}\n請確認電腦已安裝對應語言的語音識別套件。");
            }
        }

        // 當識別到語音時執行的事件
        private async void Recognizer_SpeechRecognizedAsync(object sender, SpeechRecognizedEventArgs e)
        {
            // 建議：暫停識別，避免在處理過程中因為背景聲音再次觸發
            _recognizer.RecognizeAsyncStop();

            try
            {
                // 信心指數過濾
                if (e.Result.Confidence < _currentMainWindow.recognitionConfidience)
                {
                    // 如果過濾掉，記得要恢復識別
                    _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                    return;
                }

                string command = e.Result.Text;

                // 設定路徑變數
                string audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "command.wav");
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string configFileName = $"config_{timestamp}.txt";
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", configFileName);

                switch (command)
                {
                    case "心平氣和":
                        // 這裡可以安全地使用 await
                        await _geminiServices.RecordAndProcessAsync(-1, audioPath, configPath, _currentMainWindow._ChatManager, 5000);
                        break;
                }
            }
            catch (Exception ex)
            {
                // 處理錯誤，避免 async void 導致程式閃退
                System.Windows.MessageBox.Show($"執行錯誤: {ex.Message}");
            }
            finally
            {
                // 處理完畢後，重新開始監聽語音
                // 確保無論成功或失敗，語音識別都能繼續工作
                _recognizer.RecognizeAsync(RecognizeMode.Multiple);
            }
        }


        // 視窗關閉時釋放資源
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_recognizer != null)
            {
                _recognizer.Dispose();
            }
        }
    }
}