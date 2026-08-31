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
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            RegisterCrashHandlers();

            try
            {
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
