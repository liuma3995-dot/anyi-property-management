namespace PropertyManagement.Server.Infrastructure.Security
{
    /// <summary>密码哈希（BCrypt 加盐，DM-07）。</summary>
    public static class PasswordHasher
    {
        private const int WorkFactor = 10;

        public static string Hash(string plainPassword)
        {
            return BCrypt.Net.BCrypt.HashPassword(plainPassword, WorkFactor);
        }

        public static bool Verify(string plainPassword, string storedHash)
        {
            if (string.IsNullOrWhiteSpace(plainPassword) || string.IsNullOrEmpty(storedHash))
            {
                return false;
            }

            try
            {
                return BCrypt.Net.BCrypt.Verify(plainPassword, storedHash);
            }
            catch
            {
                return false;
            }
        }
    }
}
