using System;
using System.IO;
using Newtonsoft.Json;

namespace PropertyManagement.Client.Services
{
    /// <summary>登录会话信息。</summary>
    public class SessionInfo
    {
        public string Token { get; set; }

        public string DisplayName { get; set; }

        public DateTime ExpiresAt { get; set; }

        /// <summary>M6 PG-COM-04：首登强制改密标记（登录后锁定至修改密码页；R16 已下线 90 天强制更换）。</summary>
        public bool MustChangePassword { get; set; }
    }

    /// <summary>
    /// 会话管理器（M3-D5）：token 持久化到 %LocalAppData%\PropertyManagement\session.json，
    /// 重启保持登录；退出登录时清空。
    /// </summary>
    public class SessionManager
    {
        public static SessionManager Instance { get; } = new SessionManager();

        private readonly string _filePath;

        public SessionInfo Current { get; private set; }

        /// <summary>
        /// 会话失效通知（服务端返回 40100：token 无效 / 过期 / 改密后旧 token 失效）。
        /// 参数为给用户的提示文案；由主窗口订阅后强制回到登录页（M7 修复：
        /// 此前仅弹「token 无效或过期」提示，界面停留在主窗口且不清除本地会话，用户无法自愈）。
        /// </summary>
        public event Action<string> SessionExpired;

        /// <summary>防止并发请求（如仪表盘 + 提醒同时 401）重复触发登录页切换。</summary>
        private bool _expiredNotified;

        public bool HasValidSession
        {
            get { return Current != null && Current.ExpiresAt > DateTime.Now; }
        }

        private SessionManager()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PropertyManagement");
            _filePath = Path.Combine(dir, "session.json");
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    Current = JsonConvert.DeserializeObject<SessionInfo>(File.ReadAllText(_filePath));
                    if (!HasValidSession)
                    {
                        Current = null;
                    }
                }
            }
            catch
            {
                Current = null;
            }
        }

        public void Save(SessionInfo session)
        {
            _expiredNotified = false;
            Current = session;
            try
            {
                string dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(_filePath, JsonConvert.SerializeObject(session));
            }
            catch
            {
                // 会话文件写失败不阻断使用（本次登录仍然有效）
            }
        }

        public void Clear()
        {
            Current = null;
            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch
            {
                // 忽略清理失败
            }
        }

        /// <summary>
        /// 服务端判定 token 失效时调用：清空本地会话并通知 UI 回落登录页（幂等）。
        /// 与「登录失败」区分：登录接口自身的 40100 属业务错误，不调用本方法。
        /// </summary>
        public void NotifyExpired(string message)
        {
            if (_expiredNotified)
            {
                return;
            }

            _expiredNotified = true;
            Clear();

            Action<string> handler = SessionExpired;
            if (handler == null)
            {
                return;
            }

            var dispatcher = System.Windows.Application.Current != null
                ? System.Windows.Application.Current.Dispatcher
                : null;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => handler(message)));
            }
            else
            {
                handler(message);
            }
        }
    }
}
