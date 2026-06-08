using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using LostAndFound.Data;
using LostAndFound.Models;

namespace LostAndFound.Services
{
    /// <summary>
    /// 认证与用户服务 - 处理注册、登录、密码哈希
    /// </summary>
    public class AuthService
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _http;

        public AuthService(AppDbContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        /// <summary>
        /// 使用SHA256进行密码哈希
        /// </summary>
        public static string HashPassword(string password)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            return Convert.ToHexStringLower(bytes);
        }

        /// <summary>
        /// 用户注册
        /// </summary>
        public async Task<(bool Success, string Message)> RegisterAsync(string username, string password,
            string realName, string? phone, string? email)
        {
            if (await _db.Users.AnyAsync(u => u.Username == username))
                return (false, "用户名已存在");

            var user = new User
            {
                Username = username,
                PasswordHash = HashPassword(password),
                RealName = realName,
                Phone = phone,
                Email = email,
                Role = "User",
                CreatedAt = DateTime.Now
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            return (true, "注册成功");
        }

        /// <summary>
        /// 用户登录
        /// </summary>
        public async Task<(bool Success, string Message, User? User)> LoginAsync(string username, string password)
        {
            var hash = HashPassword(password);
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username && u.PasswordHash == hash);
            if (user == null)
                return (false, "用户名或密码错误", null);

            // 检查黑名单
            if (user.IsBlacklisted)
            {
                if (user.BlacklistUntil.HasValue && user.BlacklistUntil.Value > DateTime.Now)
                    return (false, $"您的账号已被限制登录，解禁时间：{user.BlacklistUntil.Value:yyyy-MM-dd HH:mm}", null);
                if (!user.BlacklistUntil.HasValue || user.BlacklistUntil.Value <= DateTime.Now)
                {
                    // 黑名单已过期，自动解除
                    user.IsBlacklisted = false;
                    user.BlacklistUntil = null;
                    user.WarningCount = 0;
                    await _db.SaveChangesAsync();
                }
                else
                {
                    return (false, "您的账号已被限制登录", null);
                }
            }

            // 写入Session
            var session = _http.HttpContext?.Session;
            session?.SetInt32("UserId", user.Id);
            session?.SetString("Username", user.Username);
            session?.SetString("RealName", user.RealName);
            session?.SetString("Role", user.Role);

            return (true, "登录成功", user);
        }

        /// <summary>
        /// 登出
        /// </summary>
        public void Logout()
        {
            _http.HttpContext?.Session.Clear();
        }

        /// <summary>
        /// 获取当前登录用户
        /// </summary>
        public int? GetCurrentUserId()
        {
            return _http.HttpContext?.Session.GetInt32("UserId");
        }

        public string? GetCurrentUserRole()
        {
            return _http.HttpContext?.Session.GetString("Role");
        }

        public bool IsAdmin()
        {
            return GetCurrentUserRole() == "Admin";
        }

        public async Task<User?> GetCurrentUserAsync()
        {
            var id = GetCurrentUserId();
            if (id == null) return null;
            return await _db.Users.FindAsync(id.Value);
        }
    }
}