using System;
using System.Runtime.InteropServices;

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
