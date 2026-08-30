using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace AudioUI
{
    /// <summary>
    /// 用 Windows 內部的 AudioPolicyConfig 介面指定某個程式的輸出裝置。
    ///
    /// 這個介面沒有官方文件，也沒有型別庫，而且 Windows 11 前後的介面識別碼不一樣
    /// （vtable 佈局相同）。EarTrumpet 用的是 .NET Framework 的內建 WinRT 封送，
    /// 但那套支援在 .NET 5 就被移除了，所以這裡直接照 vtable 位置呼叫。
    ///
    /// 因為整段都無法離線驗證，失敗時一律把 HRESULT 帶出來——這種東西最糟的形式
    /// 是「呼叫了、回傳了、什麼也沒發生」，而那正是這個專案已經踩過的坑。
    /// </summary>
    public sealed unsafe class AudioPolicyConfigRouter : IAppAudioRouter, IDisposable
    {
        private const string ActivatableClassId = "Windows.Media.Internal.AudioPolicyConfig";

        // Windows 11（21H2 起）與更早的版本各有一個識別碼，方法排列一樣。
        private static readonly Guid Iid21H2 = new Guid("ab3d4648-e242-459f-b02f-541c70306324");
        private static readonly Guid IidDownlevel = new Guid("2a59116d-6c4f-45e0-a74f-707e3fef9258");

        // IUnknown 三個 + IInspectable 三個 + 這個介面自己的 19 個未使用方法，
        // 所以要呼叫的方法從第 25 格開始。數錯一格就是呼叫到別的東西。
        private const int SlotSet = 25;
        private const int SlotGet = 26;

        private const int DataFlowRender = 0;
        private const int RoleConsole = 0;
        private const int RoleMultimedia = 1;

        private readonly RouteTable _routes;
        private readonly IntPtr _factory;
        private readonly string? _initError;

        public AudioPolicyConfigRouter(RouteTable? routes = null)
        {
            _routes = routes ?? RouteTable.Default();
            _factory = Activate(out _initError);
        }

        public bool IsSupported => _factory != IntPtr.Zero;

        public RouteResult Route(int processId, string? targetId)
        {
            if (!IsSupported) return RouteResult.Failure(_initError ?? "這台機器上取不到系統的音訊路由介面。");

            string? pattern = _routes.ResolveDevicePattern(targetId);
            if (pattern == null) return RouteResult.Failure($"路由表裡沒有「{targetId}」。");

            string? deviceId = FindDeviceId(pattern);
            if (deviceId == null)
                return RouteResult.Failure($"找不到符合「{pattern}」的音訊裝置，虛擬音效卡可能沒有安裝。");

            return SetEndpoint(processId, MmDeviceIds.ToPolicyId(deviceId),
                               $"已把 PID {processId} 指到 {pattern}");
        }

        public RouteResult ResetToSystemDefault(int processId)
        {
            if (!IsSupported) return RouteResult.Failure(_initError ?? "這台機器上取不到系統的音訊路由介面。");

            // 空字串代表「不指定」，系統會讓它回到預設裝置。
            return SetEndpoint(processId, "", $"已讓 PID {processId} 回到系統預設裝置");
        }

        public string? CurrentDeviceId(int processId)
        {
            if (!IsSupported) return null;

            IntPtr hstring = IntPtr.Zero;
            try
            {
                var vtbl = *(IntPtr**)_factory;
                var get = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int, IntPtr*, int>)vtbl[SlotGet];
                IntPtr result;
                int hr = get(_factory, (uint)processId, DataFlowRender, RoleMultimedia, &result);
                if (hr < 0) return null;

                hstring = result;
                if (hstring == IntPtr.Zero) return null;

                IntPtr buffer = WindowsGetStringRawBuffer(hstring, out uint length);
                string raw = buffer == IntPtr.Zero ? "" : Marshal.PtrToStringUni(buffer, (int)length) ?? "";
                return string.IsNullOrEmpty(raw) ? null : MmDeviceIds.FromPolicyId(raw);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hstring != IntPtr.Zero) WindowsDeleteString(hstring);
            }
        }

        /// <summary>指定端點。兩個 role 都要設，否則有些程式會走另一個而看起來沒生效。</summary>
        private RouteResult SetEndpoint(int processId, string policyId, string successMessage)
        {
            IntPtr hstring = IntPtr.Zero;
            try
            {
                if (policyId.Length > 0)
                {
                    int created = WindowsCreateString(policyId, policyId.Length, out hstring);
                    if (created < 0) return RouteResult.Failure($"建立裝置字串失敗（HRESULT 0x{created:X8}）。");
                }

                var vtbl = *(IntPtr**)_factory;
                var set = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int, IntPtr, int>)vtbl[SlotSet];

                foreach (int role in new[] { RoleMultimedia, RoleConsole })
                {
                    int hr = set(_factory, (uint)processId, DataFlowRender, role, hstring);
                    if (hr < 0)
                        return RouteResult.Failure($"設定失敗（role {role}，HRESULT 0x{hr:X8}）。");
                }

                return RouteResult.Success(successMessage);
            }
            catch (Exception ex)
            {
                return RouteResult.Failure($"設定時發生例外：{ex.Message}");
            }
            finally
            {
                if (hstring != IntPtr.Zero) WindowsDeleteString(hstring);
            }
        }

        /// <summary>用路由的樣式找出實際裝置的 id。比對語意跟體檢報告共用一份。</summary>
        private static string? FindDeviceId(string pattern)
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    try
                    {
                        if (DependencyChecker.DeviceMatches(pattern, $"{device.FriendlyName} {device.ID}"))
                            return device.ID;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 取得工廠物件。兩個識別碼都試：先照系統版本猜一個，失敗再試另一個——
        /// 版本判斷猜錯的代價是整個功能不能用，而多試一次幾乎沒有成本。
        /// </summary>
        private static IntPtr Activate(out string? error)
        {
            error = null;
            IntPtr classId = IntPtr.Zero;
            try
            {
                RoInitialize(1); // RO_INIT_MULTITHREADED；已經初始化過會回 RPC_E_CHANGED_MODE，忽略。

                int created = WindowsCreateString(ActivatableClassId, ActivatableClassId.Length, out classId);
                if (created < 0)
                {
                    error = $"建立類別名稱字串失敗（HRESULT 0x{created:X8}）。";
                    return IntPtr.Zero;
                }

                bool newer = Environment.OSVersion.Version.Build >= 21390;
                Guid[] candidates = newer
                    ? new[] { Iid21H2, IidDownlevel }
                    : new[] { IidDownlevel, Iid21H2 };

                int lastHr = 0;
                foreach (Guid candidate in candidates)
                {
                    Guid iid = candidate;
                    lastHr = RoGetActivationFactory(classId, ref iid, out IntPtr factory);
                    if (lastHr >= 0 && factory != IntPtr.Zero) return factory;
                }

                error = $"取不到系統的音訊路由介面（HRESULT 0x{lastHr:X8}）。" +
                        $"這個介面沒有官方文件，Windows {Environment.OSVersion.Version} 可能不支援。";
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                error = $"初始化音訊路由介面時發生例外：{ex.Message}";
                return IntPtr.Zero;
            }
            finally
            {
                if (classId != IntPtr.Zero) WindowsDeleteString(classId);
            }
        }

        public void Dispose()
        {
            if (_factory != IntPtr.Zero) Marshal.Release(_factory);
        }

        [DllImport("combase.dll")]
        private static extern int RoInitialize(int initType);

        [DllImport("combase.dll")]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

        [DllImport("combase.dll", CharSet = CharSet.Unicode)]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString,
                                                      int length, out IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);
    }
}
