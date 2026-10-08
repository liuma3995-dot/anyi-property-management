using System;
using System.Windows;
using System.Windows.Threading;
using PropertyManagement.Client.Services;
using PropertyManagement.Client.Views;

namespace PropertyManagement.Client
{
    /// <summary>应用入口（M3）：按会话状态选择登录页或主窗口；启动异常记录崩溃日志。</summary>
    public partial class App : Application
    {
        /// <summary>
        /// CHG-v1.4.0-10：托盘服务（关闭窗口 → 隐藏到 Windows 通知区域，右键可退出程序）。
        /// 进程级唯一实例，窗口关闭处理与托盘菜单共用。
        /// </summary>
        internal static TrayService Tray { get; private set; }

        /// <summary>CHG-v1.4.0-10：是否正在「真正退出」——退出时窗口关闭不再拦截。</summary>
        internal static bool IsExiting { get; private set; }

        /// <summary>CHG-v1.4.0-10：托盘菜单「退出程序」→ 二次确认后收尾退出。</summary>
        internal static void ExitApplication()
        {
            IsExiting = true;
            if (Tray != null) { Tray.Hide(); }
            SingleInstanceGuard.Release();
            Current.Shutdown();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            RegisterCrashHandlers();

            try
            {
                // CHG-v1.4.0-11：客户端单实例 —— 已有实例则唤起它并结束本次启动（不再多开窗口）
                if (!SingleInstanceGuard.TryAcquire())
                {
                    Shutdown(0);
                    return;
                }

                // CHG-v1.4.0-10：托盘常驻 —— 显式退出才结束进程（关窗口只隐藏到托盘）
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Tray = new TrayService(
                    () => Tray.ShowMainWindow(),
                    ExitApplication);

                SessionManager.Instance.Load();

                if (SessionManager.Instance.HasValidSession)
                {
                    var main = new MainWindow();
                    MainWindow = main;
                    main.Show();
                }
                else
                {
                    var login = new LoginWindow();
                    MainWindow = login;
                    login.Show();
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                throw;
            }
        }

        private void RegisterCrashHandlers()
        {
            DispatcherUnhandledException += (sender, args) =>
            {
                CrashLog.Write(args.Exception);
                IsExiting = true;
                if (Tray != null) { Tray.Dispose(); }
                SingleInstanceGuard.Release();
                Shutdown(1);
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                if (ex != null)
                {
                    CrashLog.Write(ex);
                }
            };
        }
    }
}
