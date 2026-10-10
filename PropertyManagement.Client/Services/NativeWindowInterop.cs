using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace PropertyManagement.Client.Services
{
    /// <summary>
    /// 原生窗口互操作：用于无边框主窗口最大化时按工作区（不含任务栏）约束尺寸，
    /// 修复 HandyControl 仅在任务栏自动隐藏时才约束 WmGetMinMaxInfo 的问题。
    /// </summary>
    internal static class NativeWindowInterop
    {
        public const int WM_GETMINMAXINFO = 0x0024;
        public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        // CHG-v1.4.1-01：浮层去置顶用到的 SetWindowPos 参数
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        /// <summary>
        /// CHG-v1.4.1-01（负责人 2026-10-10 反馈「搜索框遮挡输入法」）：
        /// WPF 的 <see cref="Popup"/>（AllowsTransparency=True）创建的是**顶层（WS_EX_TOPMOST）**独立窗口
        /// ——实测扩展样式 0x08080088 = TOPMOST ｜ LAYERED ｜ TOOLWINDOW ｜ NOACTIVATE。
        /// 第三方输入法（搜狗/QQ 等）候选窗也是顶层窗但排在置顶带后面 → 被浮层压住；微软拼音候选窗
        /// 每次按键重新置顶所以不受影响（与负责人观察一致）。
        ///
        /// 处置：浮层打开时用 SetWindowPos(HWND_NOTOPMOST) 把它降出置顶带，仍保持在本软件窗口之上、
        /// 仍能点浮层以外收起。注意 **不能用 SetWindowLong 改 GWL_EXSTYLE**（实测读回仍带 TOPMOST）。
        /// </summary>
        public static bool DemotePopupFromTopmost(Popup popup)
        {
            if (popup == null || popup.Child == null)
            {
                return false;
            }

            var source = PresentationSource.FromVisual(popup.Child) as HwndSource;
            if (source == null || source.Handle == IntPtr.Zero)
            {
                return false;
            }

            // 失败时静默返回：只影响置顶观感，不影响功能
            return SetWindowPos(source.Handle, HWND_NOTOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>
        /// 将窗口最大化尺寸/位置约束到所在显示器的工作区（避开任务栏）。
        /// 坐标系与 MINMAXINFO 一致（相对显示器左上角）。
        /// </summary>
        public static bool ConstrainMaxSizeToWorkArea(IntPtr hwnd, IntPtr lParam)
        {
            if (lParam == IntPtr.Zero)
            {
                return false;
            }

            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return false;
            }

            // 任务栏自动隐藏时 rcWork == rcMonitor，两种场景均正确
            mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
            mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
            mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
            mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;

            Marshal.StructureToPtr(mmi, lParam, true);
            return true;
        }
    }
}
