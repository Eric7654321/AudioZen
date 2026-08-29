using AudioUI;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AudioUI   
{
    public class PerProcessAudioRecorder : IDisposable
    {
        
        // 定義 COM GUIDs
        private static  Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        private static  Guid IID_IAudioCaptureClient = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
        private const string VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK = "VAD\\Process_Loopback";

        private IWavePlayer _outputDevice;
        private MixingSampleProvider _mixer;
        private List<AudioFileReader> _readers;

        /// <summary>
        /// [新功能] 建立 Timestamp 資料夾，並對所有有視窗的應用程式同時進行錄音。
        /// </summary>
        /// <param name="baseOutputFolder">基礎輸出路徑 (例如 C:\Recordings)</param>
        /// <param name="duration">錄音時間長度</param>
        public static async Task<string> RecordAllActiveAppsAsync(string baseOutputFolder, TimeSpan duration)
        {
            if (!IsProcessLoopbackSupported())
            {
                throw new NotSupportedException("此功能需要 Windows 10 Build 20348 或 Windows 11 以上版本。");
            }
            

            // 1. 建立 Timestamp 資料夾
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string sessionFolder = Path.Combine(baseOutputFolder, timestamp);
            if (!Directory.Exists(sessionFolder)) Directory.CreateDirectory(sessionFolder);

            // 2. 透過AudioSessionService的函式來篩選目標 Process
            AudioSessionService _AudioSessionService = new AudioSessionService();
            var activeApps = _AudioSessionService.GetAppsWithConfig();
            var pidsToRecord = activeApps.Select(app => app.ProcessId).ToList();
            var targetProcesses = new List<Process>();
            foreach (var pid in pidsToRecord) 
            {
                var p = Process.GetProcessById(pid);
                targetProcesses.Add(p);
            }

            Debug.WriteLine($"找到 {targetProcesses.Count} 個潛在錄音目標。");

            // 3. 建立並行任務 (Parallel Tasks)
            var recordingTasks = new List<Task>();

            foreach (var p in targetProcesses)
            {
                // 為每個 Process 啟動一個獨立的 Task
                var task = Task.Run(() =>
                {
                    try
                    {
                        // 產生檔名：時間_ProcessName_PID.wav
                        string safeName = SanitizeFileName(p.ProcessName);
                        string fileName = $"{timestamp}_{safeName}_{p.Id}.wav";
                        string fullPath = Path.Combine(sessionFolder, fileName);

                        // 呼叫錄音核心邏輯
                        RecordProcessLoopbackInternal(p, fullPath, duration);
                    }
                    catch (Exception ex)
                    {
                        // 某些程式可能無法錄音 (權限不足或已關閉)，這裡捕捉例外避免整個批次崩潰
                        Debug.WriteLine($"無法錄製 {p.ProcessName} (PID: {p.Id}): {ex.Message}");
                    }
                });

                recordingTasks.Add(task);
            }

            // 4. 等待所有錄音任務完成
            if (recordingTasks.Count > 0)
            {
                await Task.WhenAll(recordingTasks);
            }

            return sessionFolder;
        }

        /// <summary>
        /// 播放指定資料夾內的所有 WAV 檔案
        /// </summary>
        /// <param name="folderPath">包含音檔的資料夾路徑</param>
        public void PlayAllInFolder(string folderPath)
        {
            // 1. 先停止並清理之前的播放 (避免重複播放)
            Stop();

            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"找無此資料夾: {folderPath}");

            var files = Directory.GetFiles(folderPath, "*.wav");
            if (files.Length == 0)
                throw new FileNotFoundException("資料夾內沒有 WAV 檔案");

            _readers = new List<AudioFileReader>();

            // 2. 建立 Mixer (設定基準格式：48kHz, Stereo, IEEE Float)
            // 必須設定與我們錄音時一致或相容的格式
            var mixerFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
            _mixer = new MixingSampleProvider(mixerFormat)
            {
                ReadFully = true // 設為 true 避免沒有輸入時自動停止，或者設為 false 讓它自動結束(視需求)
            };

            // 3. 讀取所有檔案並加入 Mixer
            foreach (var file in files)
            {
                try
                {
                    var reader = new AudioFileReader(file);

                    // 如果檔案格式跟 Mixer 不一樣 (例如錄音是 44.1k)，NAudio 通常會報錯或變快變慢
                    // 為了保險，我們可以加一個自動重取樣 (Resampler)
                    ISampleProvider input = reader;
                    if (reader.WaveFormat.SampleRate != mixerFormat.SampleRate)
                    {
                        input = new WdlResamplingSampleProvider(reader, mixerFormat.SampleRate);
                    }

                    // 確保聲道數一致 (Mono -> Stereo)
                    if (input.WaveFormat.Channels == 1 && mixerFormat.Channels == 2)
                    {
                        input = new MonoToStereoSampleProvider(input);
                    }

                    _readers.Add(reader); // 存起來以便之後 Dispose
                    _mixer.AddMixerInput(input);
                }
                catch (Exception ex)
                {
                    // 略過損壞的檔案
                    System.Diagnostics.Debug.WriteLine($"無法讀取檔案 {file}: {ex.Message}");
                }
            }

            if (_readers.Count == 0) return; // 沒有有效的檔案

            // 4. 初始化輸出裝置並播放
            _outputDevice = new WaveOutEvent(); // 或使用 WasapiOut
            _outputDevice.Init(_mixer);
            _outputDevice.Play();
        }


        /// <summary>單一 Process 錄音 (核心邏輯)</summary>
        public static void RecordProcessToWave(Process process, string outputFilePath, TimeSpan duration)
        {
            // 這是給外部單獨呼叫用的接口，批次功能使用的是 RecordAllActiveAppsAsync
            if (process == null) throw new ArgumentNullException(nameof(process));

            // GetDirectoryName 對純檔名回空字串、對根路徑回 null，兩種都不能直接餵給 CreateDirectory。
            string? folder = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);

            RecordProcessLoopbackInternal(process, outputFilePath, duration);
        }

        // --- 核心錄製實作 ---

        private static void RecordProcessLoopbackInternal(Process process, string filePath, TimeSpan duration)
        {
            IAudioClient audioClient = null;
            IAudioCaptureClient captureClient = null;

            try
            {
                // 1. 取得 Process Loopback 音訊介面
                audioClient = ActivateAudioClientForProcess((uint)process.Id);

                // 2. 設定 WASAPI 來源格式 (必須是 32-bit Float, 48kHz)
                WaveFormatEx inputFormat = CreateLoopbackFormat();

                // 3. 設定 WAV 存檔格式 (轉換為 16-bit PCM)
                WaveFormatEx fileFormat = CreateOutputPcmFormat(inputFormat);

                // 4. 初始化 (關鍵：AudioClientStreamFlags.Loopback)
                long hnsBufferDuration = 1000000; // 100ms buffer
                CheckHr(audioClient.Initialize(
                        AudioClientShareMode.Shared,
                        AudioClientStreamFlags.Loopback, // 必備旗標
                        hnsBufferDuration,
                        0,
                        ref inputFormat,
                        IntPtr.Zero),
                    "IAudioClient.Initialize failed.");

                // 5. 取得服務
                IntPtr captureClientPtr;
                CheckHr(audioClient.GetService(ref IID_IAudioCaptureClient, out captureClientPtr), "GetService failed.");
                captureClient = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(captureClientPtr);

                // 6. 開始錄音與轉碼
                CaptureToWave(audioClient, captureClient, inputFormat, fileFormat, duration, filePath);
            }
            catch (Exception ex)
            {
                // 拋出例外讓外層 Task 捕捉 (例如 Process 已經結束或拒絕存取)
                throw new InvalidOperationException($"錄音失敗 [{process.ProcessName}]: {ex.Message}", ex);
            }
            finally
            {
                if (captureClient != null) Marshal.ReleaseComObject(captureClient);
                if (audioClient != null) Marshal.ReleaseComObject(audioClient);
            }
        }

        // --- 錄音迴圈與轉碼 (Float -> PCM) ---

        private static void CaptureToWave(
            IAudioClient audioClient,
            IAudioCaptureClient captureClient,
            WaveFormatEx inputFormat,
            WaveFormatEx outputFormat,
            TimeSpan duration,
            string filePath)
        {
            int inputBytesPerFrame = inputFormat.nBlockAlign;
            int outputBytesPerFrame = outputFormat.nBlockAlign;

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
            {
                long headerPos = fs.Position;
                WriteWaveHeader(fs, outputFormat, 0); // 寫入標頭 (0 data size)

                CheckHr(audioClient.Start(), "Start failed.");

                var sw = Stopwatch.StartNew();
                long totalOutputBytes = 0;

                // 緩衝區重用
                byte[] inputBuffer = new byte[8192];
                float[] floatBuffer = new float[2048];
                byte[] outputBuffer = new byte[4096];

                try
                {
                    while (sw.Elapsed < duration)
                    {
                        // 簡單的流速控制，避免 CPU 100%
                        Thread.Sleep(5);

                        uint nextPacketSize;
                        CheckHr(captureClient.GetNextPacketSize(out nextPacketSize), "GetNextPacketSize failed.");

                        while (nextPacketSize > 0)
                        {
                            IntPtr pData;
                            uint framesAvailable;
                            AudioClientBufferFlags flags;
                            ulong devPos, qpcPos;

                            CheckHr(captureClient.GetBuffer(out pData, out framesAvailable, out flags, out devPos, out qpcPos), "GetBuffer failed.");

                            int bytesToRead = (int)(framesAvailable * inputBytesPerFrame);
                            int bytesToWrite = (int)(framesAvailable * outputBytesPerFrame);

                            // 擴大緩衝區檢查
                            if (inputBuffer.Length < bytesToRead) inputBuffer = new byte[bytesToRead];
                            int floatCount = bytesToRead / 4;
                            if (floatBuffer.Length < floatCount) floatBuffer = new float[floatCount];
                            if (outputBuffer.Length < bytesToWrite) outputBuffer = new byte[bytesToWrite];

                            if ((flags & AudioClientBufferFlags.Silent) != 0)
                            {
                                // 靜音處理：寫入 0
                                Array.Clear(outputBuffer, 0, bytesToWrite);
                                fs.Write(outputBuffer, 0, bytesToWrite);
                            }
                            else
                            {
                                // 1. 複製原始資料 (Float Bytes)
                                Marshal.Copy(pData, inputBuffer, 0, bytesToRead);

                                // 2. Bytes -> Float[]
                                Buffer.BlockCopy(inputBuffer, 0, floatBuffer, 0, bytesToRead);

                                // 3. Float -> Short (PCM)
                                int outIndex = 0;
                                for (int i = 0; i < floatCount; i++)
                                {
                                    float s = floatBuffer[i];
                                    // Clipping
                                    if (s > 1.0f) s = 1.0f;
                                    else if (s < -1.0f) s = -1.0f;

                                    short pcm = (short)(s * 32767);
                                    outputBuffer[outIndex++] = (byte)(pcm & 0xFF);
                                    outputBuffer[outIndex++] = (byte)((pcm >> 8) & 0xFF);
                                }

                                fs.Write(outputBuffer, 0, bytesToWrite);
                            }

                            totalOutputBytes += bytesToWrite;
                            CheckHr(captureClient.ReleaseBuffer(framesAvailable), "ReleaseBuffer failed.");
                            CheckHr(captureClient.GetNextPacketSize(out nextPacketSize), "Loop GetNextPacketSize failed.");
                        }
                    }
                }
                finally
                {
                    audioClient.Stop();
                }

                // 修正檔頭大小
                long endPos = fs.Position;
                fs.Position = headerPos;
                WriteWaveHeader(fs, outputFormat, totalOutputBytes);
                fs.Position = endPos;
            }
        }

        // --- 輔助方法 ---

        private static bool IsProcessLoopbackSupported()
        {
#if NET5_0_OR_GREATER
            return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348);
#else
            var v = Environment.OSVersion.Version;
            return v.Major > 10 || (v.Major == 10 && v.Build >= 20348);
#endif
        }

        private static WaveFormatEx CreateLoopbackFormat()
        {
            return new WaveFormatEx
            {
                wFormatTag = 3, // IEEE_FLOAT
                nChannels = 2,
                nSamplesPerSec = 48000,
                wBitsPerSample = 32,
                nBlockAlign = 8,
                nAvgBytesPerSec = 48000 * 8,
                cbSize = 0
            };
        }

        private static WaveFormatEx CreateOutputPcmFormat(WaveFormatEx src)
        {
            ushort bits = 16;
            ushort align = (ushort)(src.nChannels * 2);
            return new WaveFormatEx
            {
                wFormatTag = 1, // PCM
                nChannels = src.nChannels,
                nSamplesPerSec = src.nSamplesPerSec,
                wBitsPerSample = bits,
                nBlockAlign = align,
                nAvgBytesPerSec = src.nSamplesPerSec * align,
                cbSize = 0
            };
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
        }

        private static void WriteWaveHeader(FileStream stream, WaveFormatEx format, long dataLen)
        {
            using (var bw = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
            {
                bw.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                bw.Write((uint)(36 + dataLen));
                bw.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                bw.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                bw.Write((uint)16);
                bw.Write(format.wFormatTag);
                bw.Write(format.nChannels);
                bw.Write(format.nSamplesPerSec);
                bw.Write(format.nAvgBytesPerSec);
                bw.Write(format.nBlockAlign);
                bw.Write(format.wBitsPerSample);
                bw.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                bw.Write((uint)dataLen);
            }
        }

        // --- Activate Audio Client Async Logic ---

        private static IAudioClient ActivateAudioClientForProcess(uint processId)
        {
            var tcs = new TaskCompletionSource<IAudioClient>();
            var handler = new ActivateAudioInterfaceCompletionHandler(tcs);

            var p = new AUDIOCLIENT_ACTIVATION_PARAMS
            {
                ActivationType = AUDIOCLIENT_ACTIVATION_TYPE.ProcessLoopback,
                ProcessLoopbackParams = new AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
                {
                    TargetProcessId = processId,
                    ProcessLoopbackMode = PROCESS_LOOPBACK_MODE.IncludeTargetProcessTree
                }
            };

            // Marshaling params
            int size = Marshal.SizeOf(typeof(AUDIOCLIENT_ACTIVATION_PARAMS));
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(p, ptr, false);

            PROPVARIANT prop = default;
            prop.vt = (ushort)VarEnum.VT_BLOB;
            prop.blob.cbSize = (uint)size;
            prop.blob.pBlobData = ptr;

            try
            {
                IActivateAudioInterfaceAsyncOperation op;
                ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, ref IID_IAudioClient, ref prop, handler, out op);
                return tcs.Task.GetAwaiter().GetResult();
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        // --- 必要的 CheckHr ---
        private static void CheckHr(int hr, string msg)
        {
            if (hr < 0) throw new COMException(msg, hr);
        }

        // --- COM Interop Definitions (精簡版，整合修正) ---

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true, CharSet = CharSet.Unicode)]
        private static extern int ActivateAudioInterfaceAsync(
            string deviceInterfacePath,
            ref Guid riid,
            ref PROPVARIANT activationParams,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);

        [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceCompletionHandler
        {
            void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
        }

        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceAsyncOperation
        {
            void GetActivateResult(out int activateResult, [Out] out IntPtr activatedInterface);
        }

        private class ActivateAudioInterfaceCompletionHandler : IActivateAudioInterfaceCompletionHandler
        {
            private readonly TaskCompletionSource<IAudioClient> _tcs;
            public ActivateAudioInterfaceCompletionHandler(TaskCompletionSource<IAudioClient> tcs) => _tcs = tcs;

            public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
            {
                operation.GetActivateResult(out int hr, out IntPtr ptr);
                if (hr < 0) { _tcs.TrySetException(new COMException("Activate failed", hr)); return; }

                var client = (IAudioClient)Marshal.GetObjectForIUnknown(ptr);
                Marshal.Release(ptr);
                _tcs.TrySetResult(client);
            }
        }

        // 定義 WaveFormatEx, IAudioClient (加了 PreserveSig), IAudioCaptureClient, Enums...
        // 請將之前修正過的介面定義放在這裡 (省略以節省篇幅，與之前討論的一致)
        // 必須包含 AudioClientStreamFlags.Loopback 定義

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig] int Initialize(AudioClientShareMode shareMode, AudioClientStreamFlags streamFlags, long hnsBufferDuration, long hnsPeriodicity, ref WaveFormatEx pFormat, IntPtr audioSessionGuid);
            [PreserveSig] int GetBufferSize(out uint bufferSize);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint currentPadding);
            [PreserveSig] int IsFormatSupported(AudioClientShareMode shareMode, ref WaveFormatEx pFormat, out IntPtr closestMatch);
            [PreserveSig] int GetMixFormat(out IntPtr deviceFormat);
            [PreserveSig] int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr eventHandle);
            [PreserveSig] int GetService(ref Guid riid, out IntPtr service);
        }

        [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint numFramesToRead, out AudioClientBufferFlags bufferFlags, out ulong devicePosition, out ulong qpcPosition);
            [PreserveSig] int ReleaseBuffer(uint numFramesRead);
            [PreserveSig] int GetNextPacketSize(out uint numFramesInNextPacket);
        }

        [Flags] private enum AudioClientStreamFlags : uint { None = 0, CrossProcess = 0x10000, Loopback = 0x20000, EventCallback = 0x40000, NoPersist = 0x80000 }
        private enum AudioClientShareMode { Shared = 0, Exclusive = 1 }
        [Flags] private enum AudioClientBufferFlags : uint { None = 0, DataDiscontinuity = 1, Silent = 2, TimestampError = 4 }
        private enum AUDIOCLIENT_ACTIVATION_TYPE { Default = 0, ProcessLoopback = 1 }
        private enum PROCESS_LOOPBACK_MODE { IncludeTargetProcessTree = 0, ExcludeTargetProcessTree = 1 }

        [StructLayout(LayoutKind.Sequential)] private struct AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS { public uint TargetProcessId; public PROCESS_LOOPBACK_MODE ProcessLoopbackMode; }
        [StructLayout(LayoutKind.Sequential)] private struct AUDIOCLIENT_ACTIVATION_PARAMS { public AUDIOCLIENT_ACTIVATION_TYPE ActivationType; public AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS ProcessLoopbackParams; }
        [StructLayout(LayoutKind.Sequential)] private struct PROPVARIANT { public ushort vt; public ushort wReserved1; public ushort wReserved2; public ushort wReserved3; public PROPVARIANT_BLOB blob; }
        [StructLayout(LayoutKind.Sequential)] private struct PROPVARIANT_BLOB { public uint cbSize; public IntPtr pBlobData; }

        /// <summary>
        /// 停止播放並釋放資源
        /// </summary>
        public void Stop()
        {
            if (_outputDevice != null)
            {
                _outputDevice.Stop();
                _outputDevice.Dispose();
                _outputDevice = null;
            }

            if (_readers != null)
            {
                foreach (var reader in _readers)
                {
                    reader.Dispose();
                }
                _readers.Clear();
                _readers = null;
            }

            // 清空 Mixer 引用
            if (_mixer != null)
            {
                _mixer.RemoveAllMixerInputs();
                _mixer = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }

    }

}