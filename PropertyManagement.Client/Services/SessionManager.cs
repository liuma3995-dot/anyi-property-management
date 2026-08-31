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
    }
}
