using System;
using System.IO;
using Newtonsoft.Json;

namespace PropertyManagement.Client.Services
{
    /// <summary>客户端本地偏好（M3 修版：登录页"记住账号"）。</summary>
    public class ClientPrefs
    {
        public string SavedUserName { get; set; }

        public bool RememberAccount { get; set; }

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PropertyManagement");
                return Path.Combine(dir, "prefs.json");
            }
        }

        public static ClientPrefs Current { get; private set; } = new ClientPrefs();

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    Current = JsonConvert.DeserializeObject<ClientPrefs>(File.ReadAllText(FilePath)) ?? new ClientPrefs();
                }
            }
            catch
            {
                Current = new ClientPrefs();
            }
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(this));
            }
            catch
            {
                // 忽略偏好写失败
            }
        }
    }
}
