using System;
using System.Drawing;
using System.IO;
using System.Windows;
using PropertyManagement.Client.Views;
using WinForms = System.Windows.Forms;

namespace PropertyManagement.Client.Services
{
    /// <summary>
    /// 系统托盘服务（CHG-v1.4.0-10，负责人 2026-10-08 裁定 A）：
    /// 客户端点「关闭」= 隐藏到 Windows 通知区域（托盘）；托盘左键单击恢复主界面；
    /// 右键菜单 =「打开主界面」「退出程序」，「退出程序」二次确认后真正结束进程。
    ///
    /// 实现说明：只用 .NET Framework 内置的 WinForms `NotifyIcon`（Win7 红线，不新增第三方依赖）；
    /// 图标取客户端 `Assets\app.ico`，取不到时回落系统默认图标；进程退出即销毁托盘图标。
    /// </summary>
    internal sealed class TrayService : IDisposable
    {
        private readonly WinForms.NotifyIcon _icon;
        private readonly Action _showMainWindow;
        private readonly Action _exitApplication;
        private bool _tipShown;
        private bool _disposed;

        public TrayService(Action showMainWindow, Action exitApplication)
        {
            _showMainWindow = showMainWindow;
            _exitApplication = exitApplication;

            var menu = new WinForms.ContextMenuStrip();
            var openItem = new WinForms.ToolStripMenuItem("打开主界面");
            openItem.Click += (s, e) => ShowMainWindow();
            var exitItem = new WinForms.ToolStripMenuItem("退出程序");
            exitItem.Click += (s, e) => RequestExit();
            menu.Items.Add(openItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            _icon = new WinForms.NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "安怡物业管理系统",
                Visible = false,
                ContextMenuStrip = menu
            };
            _icon.MouseClick += (s, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Left) { ShowMainWindow(); }
            };
            _icon.DoubleClick += (s, e) => ShowMainWindow();
        }

        /// <summary>显示托盘图标（首次隐藏时弹一次气泡提示）。</summary>
        public void Show()
        {
            if (_disposed) { return; }
            _icon.Visible = true;
            if (_tipShown) { return; }
            _tipShown = true;
            try
            {
                _icon.ShowBalloonTip(3000, "安怡物业管理系统",
                    "已最小化到系统托盘：左键图标可重新打开，右键可选择「退出程序」。",
                    WinForms.ToolTipIcon.Info);
            }
            catch (Exception)
            {
                // 系统禁用气泡提示时忽略（不影响托盘常驻）
            }
        }

        /// <summary>隐藏托盘图标（真正退出前调用）。</summary>
        public void Hide()
        {
            if (_disposed) { return; }
            _icon.Visible = false;
        }

        /// <summary>恢复并置前主界面（登录页或主窗口）。</summary>
        public void ShowMainWindow()
        {
            Window window = ResolveHostWindow();
            if (window == null) { return; }
            if (!window.IsVisible) { window.Show(); }
            if (window.WindowState == WindowState.Minimized) { window.WindowState = WindowState.Normal; }
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
            window.Focus();
        }

        private static Window ResolveHostWindow()
        {
            Application app = Application.Current;
            if (app == null) { return null; }

            // 登录页优先（未登录时主界面不存在）
            foreach (Window window in app.Windows)
            {
                if (window is LoginWindow) { return window; }
            }
            foreach (Window window in app.Windows)
            {
                if (window is MainWindow) { return window; }
            }
            return app.MainWindow;
        }

        private void RequestExit()
        {
            // CHG-v1.4.0-18（负责人 2026-10-08 第 2 轮裁定）：托盘「退出程序」**不再二次确认**，点了即退出
            if (_exitApplication != null) { _exitApplication(); }
        }

        private static Icon LoadIcon()
        {
            // 图标以 WPF 资源形式内嵌在程序集（csproj <Resource Include="Assets\app.ico" />），
            // 不依赖部署目录里是否存在 Assets 文件夹；取不到再回落文件/系统图标。
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
                System.Windows.Resources.StreamResourceInfo res = Application.GetResourceStream(uri);
                if (res != null && res.Stream != null)
                {
                    using (Stream stream = res.Stream)
                    {
                        return new Icon(stream);
                    }
                }
            }
            catch (Exception)
            {
                // 资源不可用时继续尝试文件路径
            }
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
                if (File.Exists(path)) { return new Icon(path); }
            }
            catch (Exception)
            {
                // 图标文件缺失/损坏时回落系统图标
            }
            return SystemIcons.Application;
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            try
            {
                _icon.Visible = false;
                _icon.Dispose();
            }
            catch (Exception)
            {
                // 进程退出阶段忽略清理异常
            }
        }
    }
}
