using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using NLog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PropertyManagement.Server.Infrastructure.Data;

namespace PropertyManagement.Server.Infrastructure.Security
{
    /// <summary>
    /// 轻量签名 token（M2-D2）：JWT 形态 header.payload.signature，HMAC-SHA256。
    /// 密钥首次启动随机生成落盘 %ProgramData%\PropertyManagement\config\token.key，
    /// 仅本机服务使用，避免引入 JWT 第三方依赖。
    /// </summary>
    public static class TokenService
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private const string HeaderJson = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";

        private static readonly byte[] Key = LoadOrCreateKey();

        public static string Issue(string username, DateTime expiresAt)
        {
            var payload = new JObject
            {
                ["sub"] = username,
                ["iat"] = ToUnixTime(DateTime.Now),
                ["exp"] = ToUnixTime(expiresAt)
            };

            string signingInput = Base64UrlEncode(HeaderJson) + "." + Base64UrlEncode(payload.ToString(Formatting.None));
            return signingInput + "." + Sign(signingInput);
        }

        /// <summary>校验 token 签名与有效期；通过则输出用户名。</summary>
        public static bool TryValidate(string token, out string username)
        {
            username = null;

            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string[] parts = token.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            string signingInput = parts[0] + "." + parts[1];
            if (!FixedTimeEquals(Sign(signingInput), parts[2]))
            {
                return false;
            }

            try
            {
                string payloadJson = Base64UrlDecode(parts[1]);
                var payload = JObject.Parse(payloadJson);

                string sub = payload.Value<string>("sub");
                long exp = payload.Value<long?>("exp") ?? 0L;

                if (string.IsNullOrWhiteSpace(sub))
                {
                    return false;
                }

                if (ToUnixTime(DateTime.Now) >= exp)
                {
                    return false;
                }

                username = sub;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string Sign(string input)
        {
            using (var hmac = new HMACSHA256(Key))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
                return Base64UrlEncode(hash);
            }
        }

        private static byte[] LoadOrCreateKey()
        {
            string file = DbConfig.TokenKeyFile;

            if (File.Exists(file))
            {
                string text = File.ReadAllText(file).Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    EnsureKeyFileAcl(file);
                    return Convert.FromBase64String(text);
                }
            }

            var key = new byte[32];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(key);
            }

            DbConfig.EnsureDirectories();
            File.WriteAllText(file, Convert.ToBase64String(key));
            EnsureKeyFileAcl(file);
            return key;
        }

        /// <summary>
        /// 收紧密钥文件 ACL（审查 P2）：仅当前用户/SYSTEM/Administrators 可访问，
        /// 防止本机标准用户读取密钥伪造 token（DM-07 §五 目录权限限制建议）。
        /// 对已存在文件同样幂等执行；失败仅记日志，不阻断服务启动。
        /// </summary>
        private static void EnsureKeyFileAcl(string file)
        {
            try
            {
                var security = new FileSecurity();
                var currentUser = WindowsIdentity.GetCurrent().User;

                // 关闭继承，仅保留显式规则
                security.SetAccessRuleProtection(true, false);

                security.AddAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.Read | FileSystemRights.Write,
                    AccessControlType.Allow));

                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));

                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));

                File.SetAccessControl(file, security);
                Log.Info("token.key 访问权限已收紧：{0}", file);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "收紧 token.key 访问权限失败：{0}", file);
            }
        }

        private static string Base64UrlEncode(string text)
        {
            return Base64UrlEncode(Encoding.UTF8.GetBytes(text));
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string Base64UrlDecode(string text)
        {
            string padded = text.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }

        private static long ToUnixTime(DateTime value)
        {
            return new DateTimeOffset(value.ToLocalTime()).ToUnixTimeSeconds();
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            int diff = 0;
            for (int i = 0; i < left.Length; i++)
            {
                diff |= left[i] ^ right[i];
            }

            return diff == 0;
        }
    }
}