using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using LostAndFound.Data;
using LostAndFound.Hubs;
using LostAndFound.Models;
using LostAndFound.Services;

namespace LostAndFound.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AuthService _auth;
        private readonly EmailService _email;
        private readonly IHubContext<NotificationHub> _hub;

        public AccountController(AppDbContext db, AuthService auth, EmailService email, IHubContext<NotificationHub> hub)
        {
            _db = db;
            _auth = auth;
            _email = email;
            _hub = hub;
        }

        // GET: /Account/Login
        [HttpGet]
        public IActionResult Login(string? returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        // POST: /Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password, string? returnUrl)
        {
            var (success, message, user) = await _auth.LoginAsync(username, password);
            if (!success)
            {
                ViewBag.Error = message;
                return View();
            }

            // 记录日志
            _db.SystemLogs.Add(new SystemLog
            {
                UserId = user!.Id,
                ActionType = "用户登录",
                Content = $"用户 {user.Username} 登录系统",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });
            await _db.SaveChangesAsync();

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Home");
        }

        // GET: /Account/Register
        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        // POST: /Account/Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string username, string password, string confirmPassword,
            string realName, string? phone, string? email)
        {
            if (password != confirmPassword)
            {
                ViewBag.Error = "两次输入的密码不一致";
                return View();
            }
            if (password.Length < 6)
            {
                ViewBag.Error = "密码长度不能少于6位";
                return View();
            }
            if (string.IsNullOrWhiteSpace(phone) && string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Error = "手机号和邮箱至少需要填写一项";
                return View();
            }

            var (success, message) = await _auth.RegisterAsync(username, password, realName, phone, email);
            if (!success)
            {
                ViewBag.Error = message;
                return View();
            }

            TempData["SuccessMsg"] = "注册成功，请登录";

            // 发送欢迎邮件
            _ = _email.SendAsync(email ?? "", "欢迎注册校园失物招领系统",
                $"<h3>🎉 欢迎，{realName}！</h3><p>您已成功注册校园失物招领系统。</p><p>现在您可以发布失物/招领信息，帮助他人找回遗失物品。</p>");

            // SignalR 广播新用户注册通知
            _ = NotificationHub.SendToAll(_hub, "new_user",
                "新用户注册", $"欢迎新用户 {realName} 加入校园失物招领系统！");

            return RedirectToAction("Login");
        }

        // GET: /Account/Logout
        public IActionResult Logout()
        {
            _auth.Logout();
            return RedirectToAction("Index", "Home");
        }

        // GET: /Account/Profile - 个人信息
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var user = await _auth.GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login");
            return View(user);
        }

        // POST: /Account/Profile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(string realName, string? phone, string? email)
        {
            var user = await _auth.GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login");

            user.RealName = realName;
            user.Phone = phone;
            user.Email = email;
            await _db.SaveChangesAsync();

            HttpContext.Session.SetString("RealName", user.RealName);
            TempData["SuccessMsg"] = "个人信息修改成功";
            return RedirectToAction("Profile");
        }

        // GET: /Account/ChangePassword
        [HttpGet]
        public IActionResult ChangePassword()
        {
            if (_auth.GetCurrentUserId() == null) return RedirectToAction("Login");
            return View();
        }

        // POST: /Account/ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string oldPassword, string newPassword, string confirmPassword)
        {
            var user = await _auth.GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login");

            if (user.PasswordHash != AuthService.HashPassword(oldPassword))
            {
                ViewBag.Error = "原密码错误";
                return View();
            }
            if (newPassword.Length < 6)
            {
                ViewBag.Error = "新密码长度不能少于6位";
                return View();
            }
            if (newPassword != confirmPassword)
            {
                ViewBag.Error = "两次输入的新密码不一致";
                return View();
            }

            user.PasswordHash = AuthService.HashPassword(newPassword);
            await _db.SaveChangesAsync();

            TempData["SuccessMsg"] = "密码修改成功";
            return RedirectToAction("Profile");
        }
    }
}