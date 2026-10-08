using System;
using System.Threading;
using System.Windows;
using PropertyManagement.Client.Views;

namespace PropertyManagement.Client.Services
{
    /// <summary>
    /// 客户端单实例守卫（CHG-v1.4.0-11，负责人 2026-10-08 反馈「可以重复双击运行多个客户端窗口」）。
    ///
    /// 口径：第一个实例持有命名互斥体并监听「唤起事件」；后续实例启动时
    /// ① 触发唤起事件 → 已有实例把自己的窗口恢复并置前；② 本次进程立即静默退出（不再出现第二个窗口）。
    ///
    /// 说明：后端本来就有单实例互斥（Program.AcquireSingleInstance，按端口作用域），本类只约束客户端；
    /// 与托盘（TrayService）联动 —— 窗口藏在托盘时同样能被唤起。
    /// </summary>
    internal static class SingleInstanceGuard
    {
        private const string MutexName = @"Local\PropertyManagement.Client.SingleInstance";
        private const string ActivateEventName = @"Local\PropertyManagement.Client.ActivateEvent";

        private static Mutex _mutex;
        private static volatile bool _shutdown;

        /// <summary>尝试成为唯一实例。返回 false 表示已有实例在运行（本次应退出）。</summary>
        public static bool TryAcquire()
        {
            bool createdNew;
            try
            {
                _mutex = new Mutex(true, MutexName, out createdNew);
            }
            catch (UnauthorizedAccessException)
            {
                // 互斥体不可用时放行（不因守卫本身阻断启动）
                return true;
            }

            if (createdNew)
            {
                StartActivationListener();
                return true;
            }

            SignalExistingInstance();
            return false;
        }

        private static void SignalExistingInstance()
        {
            try
            {
                using (EventWaitHandle handle = EventWaitHandle.OpenExisting(ActivateEventName))
                {
                    handle.Set();
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // 已有实例尚未起监听（启动竞争）→ 直接退出即可，用户再双击一次会成功唤起
            }
            catch (UnauthorizedAccessException)
            {
                // 同上
            }
        }

        private static void StartActivationListener()
        {
            var listener = new Thread(() =>
            {
                try
                {
                    using (var handle = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName))
                    {
                        while (!_shutdown)
                        {
                            if (!handle.WaitOne(TimeSpan.FromMilliseconds(500))) { continue; }
                            Application app = Application.Current;
                            if (app == null) { continue; }
                            app.Dispatcher.BeginInvoke(new Action(ActivateExistingWindow));
                        }
                    }
                }
                catch (Exception)
                {
                    // 监听线程异常不阻断客户端主流程
                }
            });
            listener.IsBackground = true;
            listener.Start();
        }

        /// <summary>把已有实例的窗口从托盘/最小化状态恢复并置前。</summary>
        private static void ActivateExistingWindow()
        {
            Application app = Application.Current;
            if (app == null) { return; }

            Window target = null;
            foreach (Window window in app.Windows)
            {
                if (window is LoginWindow) { target = window; break; }
            }
            if (target == null)
            {
                foreach (Window window in app.Windows)
                {
                    if (window is MainWindow) { target = window; break; }
                }
            }
            if (target == null) { return; }

            if (!target.IsVisible) { target.Show(); }
            if (target.WindowState == WindowState.Minimized) { target.WindowState = WindowState.Normal; }
            target.Activate();
            target.Topmost = true;
            target.Topmost = false;
            target.Focus();
        }

        /// <summary>进程退出时释放互斥体（CHG-v1.4.0-11）。</summary>
        public static void Release()
        {
            _shutdown = true;
            if (_mutex == null) { return; }
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 非持有线程释放时忽略
            }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
