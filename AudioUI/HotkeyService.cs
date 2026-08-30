using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AudioUI
{
    public class HotkeyService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private IntPtr _windowHandle;
        private HwndSource _source;

        // 事件：當某個 ID 的快捷鍵被按下
        public event Action<int> OnHotkeyPressed;

        public void Init(Window window)
        {
            _windowHandle = new WindowInteropHelper(window).Handle;
            _source = HwndSource.FromHwnd(_windowHandle);
            _source.AddHook(HwndHook);
        }

        // 註冊快捷鍵
        // modifier: Alt=1, Ctrl=2, Shift=4, None=0
        // vk: 虛擬鍵碼 (例如 Numpad1 = 97)
        //
        // 回傳註冊成功與否。Windows 會在別的程式已經佔走同一組鍵時直接拒絕，
        // 而原本這裡把回傳值丟掉——熱鍵按下去沒反應時，連「有沒有註冊成功」都無從得知。
        public bool Register(int id, uint modifier, uint vk)
        {
            if (_windowHandle == IntPtr.Zero) return false;
            return RegisterHotKey(_windowHandle, id, modifier, vk);
        }

        public void Unregister(int id)
        {
            UnregisterHotKey(_windowHandle, id);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                OnHotkeyPressed?.Invoke(id);
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            _source?.RemoveHook(HwndHook);
            // 這裡理論上要 Unregister 所有按鍵，簡化版先略過
        }
    }
}