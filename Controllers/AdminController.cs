using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using LostAndFound.Data;
using LostAndFound.Hubs;
using LostAndFound.Models;
using LostAndFound.Services;

namespace LostAndFound.Controllers
{
    public class AdminController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AuthService _auth;
        private readonly EmailService _email;
        private readonly IHubContext<NotificationHub> _hub;

        public AdminController(AppDbContext db, AuthService auth, EmailService email, IHubContext<NotificationHub> hub)
        {
            _db = db;
            _auth = auth;
            _email = email;
            _hub = hub;
        }

        private IActionResult? EnsureAdmin()
        {
            if (!_auth.IsAdmin())
            {
                TempData["ErrorMsg"] = "需要管理员权限";
                return RedirectToAction("Index", "Home");
            }
            return null;
        }

        // ===== 用户管理 =====

        // GET: /Admin/Users
        [HttpGet]
        public async Task<IActionResult> Users(string? keyword)
        {
            var adminCheck = EnsureAdmin();
            if (adminCheck != null) return adminCheck;

            var query = _db.Users.AsQueryable();
            if (!string.IsNullOrWhiteSpace(keyword))
                query = query.Where(u => u.Username.Contains(keyword) || u.RealName.Contains(keyword));

            ViewBag.Keyword = keyword;
            var users = await query.OrderByDescending(u => u.CreatedAt).ToListAsync();
            return View(users);
        }

        // GET: /Admin/EditUser/5
        [HttpGet]
        public async Task<IActionResult> EditUser(int id)
        {
            var adminCheck = EnsureAdmin();
            if (adminCheck != null) return adminCheck;

            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();
            return View(user);
        }

        // POST: /Admin/EditUser/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(int id, string realName, string role, string? phone, string? email)
        {
            var adminCheck = EnsureAdmin();
            if (adminCheck != null) return adminCheck;

            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();

            user.RealName = realName;
            user.Role = role;
            user.Phone = phone;
            user.Email = email;
            await _db.SaveChangesAsync();

            _db.SystemLogs.Add(new SystemLog
            {
                UserId = _auth.GetCurrentUserId(),
                ActionType = "编辑用户",
                Content = $"管理员编辑用户 ID:{id} - {user.Username}",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });
            await _db.SaveChangesAsync();

            TempData["SuccessMsg"] = "用户信息修改成功";
            return RedirectToAction(nameof(Users));
        }

        // POST: /Admin/ResetPassword/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(int id, string newPassword)
        {
            var adminCheck = EnsureAdmin();
            if (adminCheck != null) return adminCheck;

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            {
                TempData["ErrorMsg"] = "新密码长度不能少于6位";
                return RedirectToAction(nameof(EditUser), new { id });
            }

            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();

            user.PasswordHash = AuthService.HashPassword(newPassword);
            await _db.SaveChangesAsync();

            _db.SystemLogs.Add(new SystemLog
            {
                UserId = _auth.GetCurrentUserId(),
                ActionType = "重置密码",
                Content = $"管理员重置用户 ID:{id} - {user.Username} 的密码",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });
            await _db.SaveChangesAsync();

            TempData["SuccessMsg"] = "密码重置成功";
            return RedirectToAction(nameof(Users));
        }

        // POST: /Admin/DeleteUser/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var adminCheck = EnsureAdmin();
            if (adminCheck != null) return adminCheck;

            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();
            if (user.Id == _auth.GetCurrentUserId())
            {
                TempData["ErrorMsg"] = "不能删除自己";
                return RedirectToAction(nameof(Users));
            }

            _db.Users.Remove(user);

            _db.SystemLogs.Add(new SystemLog
            {
                UserId = _auth.GetCurrentUserId(),
                ActionType = "删除用户",
                Content = $"管理员删除用户 ID:{id} - {user.Username}",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });
            await _db.SaveChangesAsync();

            TempData["SuccessMsg"] = "用户已删除";
            return RedirectToAction(nameof(Users));
        }

        // ===== 公告管理 =====

        // GET: /Admin/Announcements
        [HttpGet]
        public async Task<IActionResult> Announcements()
        {
            var adminCheck = EnsureAdmin();
            if (adminCheck != null) return adminCheck;

            var list = await _db.Announcements
                .Include(a => a.Publisher)
                .OrderByDescending(a => a.IsTop)
                .ThenByDescending(a => a.PublishedAt)
                .ToListAsync();
            return View(list);
        }

        // GET: /Admin/CreateAnnouncement
        [HttpGet]
        public IActionResult CreateAnnouncement()
        {
            var adminCheck = EnsureAdmin();
            if (adminCheck != null) return adminCheck;
            return View();
        }

        // POST: /Admin/CreateAnnouncement
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAnnouncement(string title, string content, bool isTop)
        {
            var adminCheck = EnsureAdmin();
            if (adminCheck != null) return adminCheck;

            var ann = new Announcement
            {
                Title = title,
                Content = content,
                IsTop = isTop,
                PublisherId = _auth.GetCurrentUserId()!.Value,
                PublishedAt = DateTime.Now
            };
            _db.Announcements.Add(ann);

            _db.SystemLogs.Add(new SystemLog
            {
                UserId = _auth.GetCurrentUserId(),
                ActionType = "发布公告",
                Content = $"管理员发布公告: {title}",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });
            await _db.SaveChangesAsync();

            // SignalR 广播新公告通知
            _ = NotificationHub.SendToAll(_hub, "new_announcement",
                "📢 新公告", title);

            // 群发邮件给所有填写了邮箱的用户
            var recipients = await _db.Users
                .Where(u => u.Email != null && u.Email != "")
                .Select(u => new { u.Email, u.RealName })
                .ToListAsync();
            var emailList = recipients.Select(r => (r.Email!, r.RealName)).ToList();
            _ = _email.SendToAllAsync(emailList, $"📢 校园失物招领公告: {title}",
                $"<h3>{title}</h3><p>{content}</p><p style='color:#999;'>发布时间：{DateTime.Now:yyyy-MM-dd HH:mm}</p>");

            TempData["SuccessMsg"] = "公告发布成功";
            return RedirectToAction(nameof(Announcements));
        }

        // POST: /Admin/DeleteAnnouncement/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAnnouncement(int id)
        {
            var adminCheck = EnsureAdmin();
            if (adminCheck != null) return adminCheck;

            var ann = await _db.Announcements.FindAsync(id);
            if (ann == null) return NotFound();
            _db.Announcements.Remove(ann);
            await _db.SaveChangesAsync();

            TempData["SuccessMsg"] = "公告已删除";
            return RedirectToAction(nameof(Announcements));
        }

        // ===== 日志管理 =====

        // GET: /Admin/Logs
        [HttpGet]
        public async Task<IActionResult> Logs(DateTime? startDate, DateTime? endDate, string? actionType, int page = 1)
        {
            var adminCheck = EnsureAdmin();
            if (adminCheck != null) return adminCheck;

            var query = _db.SystemLogs.Include(l => l.User).AsQueryable();

            if (startDate.HasValue)
                query = query.Where(l => l.OperatedAt >= startDate.Value);
            if (endDate.HasValue)
                query = query.Where(l => l.OperatedAt <= endDate.Value.AddDays(1));
            if (!string.IsNullOrWhiteSpace(actionType))
                query = query.Where(l => l.ActionType == actionType);

            ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd");
            ViewBag.ActionType = actionType;

            const int pageSize = 20;
            var total = await query.CountAsync();
            var logs = await query
                .OrderByDescending(l => l.OperatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Page = page;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.Total = total;

            return View(logs);
        }
    }
}