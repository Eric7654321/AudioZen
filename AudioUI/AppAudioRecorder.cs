// PerProcessAudioRecorder.cs
// 只負責錄音邏輯，不含任何 WPF UI。
// - 在支援 Application Loopback 的系統 (Windows 10 build 20348+/Windows 11) 上，使用 per-process loopback 只錄指定程式。
// - 在較舊系統上，會退回錄整個預設播放裝置的 loopback（所有程式混在一起）。

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;

namespace AudioUI
{
    /// <summary>
    /// 針對單一 Process 錄製輸出音訊到 WAV 檔。
    /// </summary>
    public static class PerProcessAudioRecorder
    {
        private static Guid IID_IAudioClient =
            new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

        private static Guid IID_IAudioCaptureClient =
            new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

        private const string VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK = "VAD\\Process_Loopback";

        public static void RecordProcessToWave(
            Process process,
            string outputFolder,
            TimeSpan duration,
            bool allowDeviceLoopbackFallback = true)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentNullException(nameof(outputFolder));
            if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

            string safeName = SanitizeFileName(process.ProcessName);
            string fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeName}.wav";
            string filePath = Path.Combine(outputFolder, fileName);

            if (IsProcessLoopbackSupported())
            {
                RecordProcessLoopbackInternal(process, filePath, duration);
            }
            else
            {
                if (!allowDeviceLoopbackFallback)
                {
                    throw new NotSupportedException(
                        "Per-process loopback 錄音需要 Windows 10 build 20348 以上或 Windows 11。");
                }

                RecordDeviceLoopbackInternal(process, filePath, duration);
            }
        }

        /// <summary>判斷系統是否支援 per-process loopback。</summary>
        private static bool IsProcessLoopbackSupported()
        {
#if NET5_0_OR_GREATER
            // Windows 10 build 20348 (Server 2022) 開始支援 per-process loopback。
            return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348);
#else
            var v = Environment.OSVersion.Version;
            return v.Major > 10 || (v.Major == 10 && v.Build >= 20348);
#endif
        }

        // --- per-process loopback path (需要新系統) ---

        private static void RecordProcessLoopbackInternal(Process process, string filePath, TimeSpan duration)
        {
            // 1. 啟用 per-process loopback 對應的 IAudioClient
            IAudioClient audioClient = ActivateAudioClientForProcess((uint)process.Id);

            // 官方 ApplicationLoopback 範例使用固定 PCM 格式
            WaveFormatEx format = CreateDefaultPcmFormat();
            int bytesPerFrame = format.nBlockAlign;

            long hnsBufferDuration = 0;
            long hnsPeriodicity = 0;

            CheckHr(audioClient.Initialize(
                    AudioClientShareMode.Shared,
                    AudioClientStreamFlags.None,
                    hnsBufferDuration,
                    hnsPeriodicity,
                    ref format,
                    IntPtr.Zero),
                "IAudioClient.Initialize (process loopback) failed.");

            uint bufferFrameCount;
            CheckHr(audioClient.GetBufferSize(out bufferFrameCount), "GetBufferSize failed.");

            IntPtr captureClientPtr;
            CheckHr(audioClient.GetService(ref IID_IAudioCaptureClient, out captureClientPtr),
                "GetService(IAudioCaptureClient) failed.");

            var captureClient = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(captureClientPtr);

            try
            {
                CaptureToWave(audioClient, captureClient, format, bytesPerFrame, duration, filePath);
            }
            finally
            {
                Marshal.ReleaseComObject(captureClient);
                Marshal.ReleaseComObject(audioClient);
            }
        }

        // --- device loopback fallback (所有程式混在一起) ---

        private static void RecordDeviceLoopbackInternal(Process process, string filePath, TimeSpan duration)
        {
            // 取得預設播放裝置
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice device = null;

            try
            {
                CheckHr(enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device),
                    "GetDefaultAudioEndpoint failed.");

                // 由裝置產生 IAudioClient
                IntPtr audioClientPtr;
                CheckHr(device.Activate(ref IID_IAudioClient, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out audioClientPtr),
                    "IMMDevice.Activate(IAudioClient) failed.");

                var audioClient = (IAudioClient)Marshal.GetObjectForIUnknown(audioClientPtr);

                IntPtr mixFmtPtr;
                WaveFormatEx format;

                int hrMix = audioClient.GetMixFormat(out mixFmtPtr);
                if (hrMix >= 0 && mixFmtPtr != IntPtr.Zero)
                {
                    format = Marshal.PtrToStructure<WaveFormatEx>(mixFmtPtr);
                    Marshal.FreeCoTaskMem(mixFmtPtr);
                }
                else
                {
                    format = CreateDefaultPcmFormat();
                }

                int bytesPerFrame = (format.wBitsPerSample / 8) * format.nChannels;
                long hnsBufferDuration = 0;
                long hnsPeriodicity = 0;

                CheckHr(audioClient.Initialize(
                        AudioClientShareMode.Shared,
                        AudioClientStreamFlags.Loopback,
                        hnsBufferDuration,
                        hnsPeriodicity,
                        ref format,
                        IntPtr.Zero),
                    "IAudioClient.Initialize (device loopback) failed.");

                uint bufferFrameCount;
                CheckHr(audioClient.GetBufferSize(out bufferFrameCount), "GetBufferSize failed.");

                IntPtr captureClientPtr;
                CheckHr(audioClient.GetService(ref IID_IAudioCaptureClient, out captureClientPtr),
                    "GetService(IAudioCaptureClient) failed.");

                var captureClient = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(captureClientPtr);

                try
                {
                    CaptureToWave(audioClient, captureClient, format, bytesPerFrame, duration, filePath);
                }
                finally
                {
                    Marshal.ReleaseComObject(captureClient);
                    Marshal.ReleaseComObject(audioClient);
                }
            }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }

        // --- 共用的錄製主迴圈 ---

        private static void CaptureToWave(
            IAudioClient audioClient,
            IAudioCaptureClient captureClient,
            WaveFormatEx format,
            int bytesPerFrame,
            TimeSpan duration,
            string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
            {
                long headerPos = fs.Position;
                WriteWaveHeader(fs, format, 0);

                CheckHr(audioClient.Start(), "IAudioClient.Start failed.");
                var sw = Stopwatch.StartNew();
                long totalBytesWritten = 0;

                try
                {
                    while (sw.Elapsed < duration)
                    {
                        Thread.Sleep(10);

                        uint nextPacketSize;
                        CheckHr(captureClient.GetNextPacketSize(out nextPacketSize),
                            "GetNextPacketSize failed.");

                        while (nextPacketSize > 0)
                        {
                            IntPtr buffer;
                            uint framesAvailable;
                            AudioClientBufferFlags flags;
                            ulong devicePosition;
                            ulong qpcPosition;

                            CheckHr(
                                captureClient.GetBuffer(
                                    out buffer,
                                    out framesAvailable,
                                    out flags,
                                    out devicePosition,
                                    out qpcPosition),
                                "GetBuffer failed.");

                            int bytesToCopy = checked((int)(framesAvailable * bytesPerFrame));

                            if ((flags & AudioClientBufferFlags.Silent) != 0)
                            {
                                byte[] silence = new byte[bytesToCopy];
                                fs.Write(silence, 0, silence.Length);
                                totalBytesWritten += silence.Length;
                            }
                            else
                            {
                                byte[] managed = new byte[bytesToCopy];
                                Marshal.Copy(buffer, managed, 0, bytesToCopy);
                                fs.Write(managed, 0, managed.Length);
                                totalBytesWritten += managed.Length;
                            }

                            CheckHr(captureClient.ReleaseBuffer(framesAvailable),
                                "ReleaseBuffer failed.");

                            CheckHr(captureClient.GetNextPacketSize(out nextPacketSize),
                                "GetNextPacketSize failed (loop).");
                        }
                    }
                }
                finally
                {
                    audioClient.Stop();
                }

                long endPos = fs.Position;
                fs.Position = headerPos;
                WriteWaveHeader(fs, format, totalBytesWritten);
                fs.Position = endPos;
            }
        }

        private static WaveFormatEx CreateDefaultPcmFormat()
        {
            // 2ch, 44.1kHz, 16bit PCM
            ushort channels = 2;
            uint sampleRate = 44100;
            ushort bits = 16;
            ushort blockAlign = (ushort)(channels * (bits / 8));
            uint avgBytes = sampleRate * blockAlign;

            return new WaveFormatEx
            {
                wFormatTag = 1, // WAVE_FORMAT_PCM
                nChannels = channels,
                nSamplesPerSec = sampleRate,
                wBitsPerSample = bits,
                nBlockAlign = blockAlign,
                nAvgBytesPerSec = avgBytes,
                cbSize = 0
            };
        }

        private static void CheckHr(int hr, string message)
        {
            if (hr < 0) throw new COMException(message, hr);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "UnknownProcess" : name;
        }

        // --- per-process loopback：ActivateAudioInterfaceAsync ---

        private static IAudioClient ActivateAudioClientForProcess(uint processId)
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<IAudioClient>();
            var handler = new ActivateAudioInterfaceCompletionHandler(tcs);

            AUDIOCLIENT_ACTIVATION_PARAMS activationParams = new AUDIOCLIENT_ACTIVATION_PARAMS
            {
                ActivationType = AUDIOCLIENT_ACTIVATION_TYPE.AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK,
                ProcessLoopbackParams = new AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
                {
                    TargetProcessId = processId,
                    ProcessLoopbackMode =
                        PROCESS_LOOPBACK_MODE.PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE
                }
            };

            IntPtr activationParamsPtr = IntPtr.Zero;
            PROPVARIANT prop = default(PROPVARIANT);

            try
            {
                int size = Marshal.SizeOf(typeof(AUDIOCLIENT_ACTIVATION_PARAMS));
                activationParamsPtr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(activationParams, activationParamsPtr, false);

                prop.vt = (ushort)VarEnum.VT_BLOB;
                prop.wReserved1 = 0;
                prop.wReserved2 = 0;
                prop.wReserved3 = 0;
                prop.blob.cbSize = (uint)size;
                prop.blob.pBlobData = activationParamsPtr;

                IActivateAudioInterfaceAsyncOperation asyncOp;

                int hr = ActivateAudioInterfaceAsync(
                    VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
                    ref IID_IAudioClient,
                    ref prop,
                    handler,
                    out asyncOp);

                CheckHr(hr, "ActivateAudioInterfaceAsync failed.");

                // 等待 callback 把 IAudioClient 塞進 TaskCompletionSource
                IAudioClient client = tcs.Task.GetAwaiter().GetResult();
                return client;
            }
            finally
            {
                if (activationParamsPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(activationParamsPtr);
            }
        }

        // --- WAV header ---

        private static void WriteWaveHeader(FileStream stream, WaveFormatEx format, long dataLengthBytes)
        {
            using (var bw = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
            {
                uint fmtChunkSize = 16;
                uint riffChunkSize =
                    (uint)(4 + 8 + fmtChunkSize + 8 + dataLengthBytes);

                bw.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                bw.Write(riffChunkSize);
                bw.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });

                bw.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                bw.Write(fmtChunkSize);

                bw.Write(format.wFormatTag);
                bw.Write(format.nChannels);
                bw.Write(format.nSamplesPerSec);
                bw.Write(format.nAvgBytesPerSec);
                bw.Write(format.nBlockAlign);
                bw.Write(format.wBitsPerSample);
                bw.Write(format.cbSize);

                bw.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                bw.Write((uint)dataLengthBytes);
            }
        }

        // --- interop types ---

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true, CharSet = CharSet.Unicode)]
        private static extern int ActivateAudioInterfaceAsync(
            string deviceInterfacePath,
            ref Guid riid,
            ref PROPVARIANT activationParams,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);

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

        [Flags]
        private enum AudioClientStreamFlags : uint
        {
            None = 0x00000000,
            CrossProcess = 0x00010000,
            Loopback = 0x00020000,
            EventCallback = 0x00040000,
            NoPersist = 0x00080000,
        }

        private enum AudioClientShareMode
        {
            Shared = 0,
            Exclusive = 1
        }

        [Flags]
        private enum AudioClientBufferFlags : uint
        {
            None = 0x0,
            DataDiscontinuity = 0x1,
            Silent = 0x2,
            TimestampError = 0x4
        }

        private enum AUDIOCLIENT_ACTIVATION_TYPE
        {
            AUDIOCLIENT_ACTIVATION_TYPE_DEFAULT = 0,
            AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK = 1
        }

        private enum PROCESS_LOOPBACK_MODE
        {
            PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE = 0,
            PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE = 1
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
        {
            public uint TargetProcessId;
            public PROCESS_LOOPBACK_MODE ProcessLoopbackMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AUDIOCLIENT_ACTIVATION_PARAMS
        {
            public AUDIOCLIENT_ACTIVATION_TYPE ActivationType;
            public AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS ProcessLoopbackParams;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPVARIANT_BLOB
        {
            public uint cbSize;
            public IntPtr pBlobData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPVARIANT
        {
            public ushort vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public PROPVARIANT_BLOB blob;
        }

        [ComImport]
        [Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceCompletionHandler
        {
            void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
        }

        [ComImport]
        [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceAsyncOperation
        {
            void GetActivateResult(
                out int activateResult,
                [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
        }

        [ComVisible(true)]
        [ClassInterface(ClassInterfaceType.None)]
        private sealed class ActivateAudioInterfaceCompletionHandler
            : IActivateAudioInterfaceCompletionHandler
        {
            private readonly System.Threading.Tasks.TaskCompletionSource<IAudioClient> _tcs;

            public ActivateAudioInterfaceCompletionHandler(
                System.Threading.Tasks.TaskCompletionSource<IAudioClient> tcs)
            {
                _tcs = tcs;
            }

            public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
            {
                try
                {
                    operation.GetActivateResult(out int hr, out object activated);

                    if (hr < 0)
                    {
                        _tcs.TrySetException(new COMException(
                            "ActivateAudioInterfaceAsync failed in callback.", hr));
                        return;
                    }

                    if (activated is not IAudioClient client)
                    {
                        _tcs.TrySetException(new InvalidCastException(
                            "Activated interface is not IAudioClient."));
                        return;
                    }

                    _tcs.TrySetResult(client);
                }
                catch (Exception ex)
                {
                    _tcs.TrySetException(ex);
                }
            }
        }

        [ComImport]
        [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            int Initialize(
                AudioClientShareMode shareMode,
                AudioClientStreamFlags streamFlags,
                long hnsBufferDuration,
                long hnsPeriodicity,
                ref WaveFormatEx pFormat,
                IntPtr audioSessionGuid);

            int GetBufferSize(out uint bufferSize);
            int GetStreamLatency(out long latency);
            int GetCurrentPadding(out uint currentPadding);

            int IsFormatSupported(
                AudioClientShareMode shareMode,
                ref WaveFormatEx pFormat,
                out IntPtr closestMatch);

            int GetMixFormat(out IntPtr deviceFormat);
            int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
            int Start();
            int Stop();
            int Reset();
            int SetEventHandle(IntPtr eventHandle);
            int GetService(ref Guid riid, out IntPtr service);
        }

        [ComImport]
        [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            int GetBuffer(
                out IntPtr data,
                out uint numFramesToRead,
                out AudioClientBufferFlags bufferFlags,
                out ulong devicePosition,
                out ulong qpcPosition);

            int ReleaseBuffer(uint numFramesRead);

            int GetNextPacketSize(out uint numFramesInNextPacket);
        }

        // --- MMDevice API (device loopback fallback) ---

        private enum EDataFlow
        {
            eRender,
            eCapture,
            eAll,
            EDataFlow_enum_count
        }

        private enum ERole
        {
            eConsole,
            eMultimedia,
            eCommunications,
            ERole_enum_count
        }

        [Flags]
        private enum DeviceState : uint
        {
            ACTIVE = 0x00000001,
            DISABLED = 0x00000002,
            NOTPRESENT = 0x00000004,
            UNPLUGGED = 0x00000008,
            MASK_ALL = 0x0000000F
        }

        [Flags]
        private enum CLSCTX : uint
        {
            CLSCTX_INPROC_SERVER = 0x1,
            CLSCTX_INPROC_HANDLER = 0x2,
            CLSCTX_LOCAL_SERVER = 0x4,
            CLSCTX_REMOTE_SERVER = 0x10,
            CLSCTX_ALL = CLSCTX_INPROC_SERVER | CLSCTX_INPROC_HANDLER | CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [ComImport]
        [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            int GetCount(out uint cProps);
            int GetAt(uint iProp, out PROPERTYKEY pkey);
            int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
            int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
            int Commit();
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState dwStateMask, out IMMDeviceCollection ppDevices);
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
            int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
            int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);
            int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
        }

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject
        {
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, CLSCTX dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
            int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
            int GetState(out DeviceState pdwState);
        }

        [ComImport]
        [Guid("0BD7A1BE-7A1A-44DB-8397-C0EC0BA2980A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            int GetCount(out uint pcDevices);
            int Item(uint nDevice, out IMMDevice ppDevice);
        }

        [ComImport]
        [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMNotificationClient
        {
            void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, DeviceState dwNewState);
            void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
            void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
            void OnDefaultDeviceChanged(EDataFlow flow, ERole role,
                [MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId);
            void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId,
                ref PROPERTYKEY key);
        }
    }
}
