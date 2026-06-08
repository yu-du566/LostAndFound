using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LostAndFound.Data;
using LostAndFound.Models;
using LostAndFound.Services;

namespace LostAndFound.Controllers
{
    /// <summary>
    /// 排行榜 + 个人统计 + 异常检测
    /// </summary>
    public class LeaderboardController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AuthService _auth;

        public LeaderboardController(AppDbContext db, AuthService auth)
        {
            _db = db;
            _auth = auth;
        }

        // GET: /Leaderboard — 招领发布排行榜（所有人可见）
        [HttpGet]
        public async Task<IActionResult> Index(string period = "month")
        {
            var now = DateTime.Now;
            DateTime startDate;
            if (period == "year")
                startDate = new DateTime(now.Year, 1, 1);
            else if (period == "all")
                startDate = DateTime.MinValue;
            else
                startDate = new DateTime(now.Year, now.Month, 1);

            // 查询排行榜（带排名，使用强类型 RankItem）
            var raw = await _db.FoundItems
                .Where(f => f.CreatedAt >= startDate && !f.Publisher!.IsBlacklisted)
                .GroupBy(f => new { f.UserId, f.Publisher!.Username, f.Publisher.RealName })
                .Select(g => new RankItem
                {
                    UserId = g.Key.UserId,
                    Username = g.Key.Username,
                    RealName = g.Key.RealName,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .Take(20)
                .ToListAsync();

            var ranking = raw.Select((item, index) => {
                item.Rank = index + 1;
                return item;
            }).ToList();

            // 异常检测 + 黑名单检查
            var violations = new List<(string Name, int Count, string Reason)>();
            if (ranking.Any())
            {
                var top = ranking.First();
                var second = ranking.Skip(1).FirstOrDefault();

                // 规则1：最高不超过150
                if (top.Count > 150)
                {
                    await IssueWarning(top.UserId, $"招领发布次数异常（{top.Count}次），超过上限150次");
                    violations.Add((top.RealName, top.Count, $"超过上限150次"));
                }

                // 规则2：不高于第二名+50
                if (second != null && top.Count > second.Count + 50)
                {
                    await IssueWarning(top.UserId, $"招领发布次数（{top.Count}次）远超第二名（{second.Count}次），疑似刷数据");
                    violations.Add((top.RealName, top.Count, $"远超第二名（{second.Count}次）多达{top.Count - second.Count}次"));
                }

                // 检查所有用户是否超过第二名的次数+50
                foreach (var user in ranking.Skip(1))
                {
                    if (user.Count > 150)
                    {
                        await IssueWarning(user.UserId, $"招领发布次数（{user.Count}次），超过上限150次");
                        violations.Add((user.RealName, user.Count, $"超过上限150次"));
                    }
                }
            }

            ViewBag.Period = period;
            ViewBag.Ranking = ranking; // List<RankItem>
            ViewBag.Violations = violations.Select(v => $"{v.Name}（{v.Count}次）：{v.Reason}").ToList(); // 预格式化为 List<string>

            return View();
        }

        /// <summary>
        /// 给用户发出警告，3次后拉黑30天
        /// </summary>
        private async Task IssueWarning(int userId, string reason)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return;

            user.WarningCount++;

            if (user.WarningCount >= 3)
            {
                user.IsBlacklisted = true;
                user.BlacklistUntil = DateTime.Now.AddDays(30);
                _db.SystemLogs.Add(new SystemLog
                {
                    UserId = _auth.GetCurrentUserId(),
                    ActionType = "拉黑用户",
                    Content = $"用户 {user.Username} 被自动拉黑30天（警告达3次）",
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
                });
            }
            else
            {
                _db.SystemLogs.Add(new SystemLog
                {
                    UserId = _auth.GetCurrentUserId(),
                    ActionType = "警告用户",
                    Content = $"警告 {user.Username}：{reason}（第{user.WarningCount}次警告）",
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
                });
            }

            await _db.SaveChangesAsync();
        }

        // GET: /Leaderboard/MyStats — 个人统计（所有人可见，需登录）
        [HttpGet]
        public async Task<IActionResult> MyStats()
        {
            var userId = _auth.GetCurrentUserId();
            if (userId == null) return RedirectToAction("Login", "Account", new { returnUrl = "/Leaderboard/MyStats" });

            var now = DateTime.Now;
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var yearStart = new DateTime(now.Year, 1, 1);

            // 招领发布数
            ViewBag.MonthFound = await _db.FoundItems.CountAsync(f => f.UserId == userId && f.CreatedAt >= monthStart);
            ViewBag.YearFound = await _db.FoundItems.CountAsync(f => f.UserId == userId && f.CreatedAt >= yearStart);
            ViewBag.TotalFound = await _db.FoundItems.CountAsync(f => f.UserId == userId);

            // 失物发布数
            ViewBag.MonthLost = await _db.LostItems.CountAsync(l => l.UserId == userId && l.CreatedAt >= monthStart);
            ViewBag.YearLost = await _db.LostItems.CountAsync(l => l.UserId == userId && l.CreatedAt >= yearStart);
            ViewBag.TotalLost = await _db.LostItems.CountAsync(l => l.UserId == userId);

            // 认领成功次数（审核通过的认领）
            ViewBag.MonthClaimed = await _db.Claims.CountAsync(c => c.ApplicantId == userId && c.Status == "通过" && c.ReviewedAt >= monthStart);
            ViewBag.YearClaimed = await _db.Claims.CountAsync(c => c.ApplicantId == userId && c.Status == "通过" && c.ReviewedAt >= yearStart);
            ViewBag.TotalClaimed = await _db.Claims.CountAsync(c => c.ApplicantId == userId && c.Status == "通过");

            // 警告信息
            var user = await _db.Users.FindAsync(userId.Value);
            ViewBag.User = user;

            return View();
        }

        // GET: /Leaderboard/Warnings — 管理员查看所有警告记录（管理员）
        [HttpGet]
        public async Task<IActionResult> Warnings()
        {
            if (!_auth.IsAdmin()) return RedirectToAction("Index", "Home");

            var logs = await _db.SystemLogs
                .Where(l => l.ActionType == "警告用户" || l.ActionType == "拉黑用户")
                .Include(l => l.User)
                .OrderByDescending(l => l.OperatedAt)
                .Take(100)
                .ToListAsync();

            var blacklisted = await _db.Users
                .Where(u => u.IsBlacklisted)
                .ToListAsync();

            ViewBag.Logs = logs;
            ViewBag.Blacklisted = blacklisted;

            return View();
        }

        // POST: /Leaderboard/Unban/5 — 管理员手动解除黑名单
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unban(int userId)
        {
            if (!_auth.IsAdmin()) return RedirectToAction("Index", "Home");

            var user = await _db.Users.FindAsync(userId);
            if (user != null)
            {
                user.IsBlacklisted = false;
                user.BlacklistUntil = null;
                user.WarningCount = 0;
                _db.SystemLogs.Add(new SystemLog
                {
                    UserId = _auth.GetCurrentUserId(),
                    ActionType = "解除拉黑",
                    Content = $"管理员手动解除 {user.Username} 的黑名单",
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
                });
                await _db.SaveChangesAsync();
            }

            TempData["SuccessMsg"] = "已解除黑名单";
            return RedirectToAction(nameof(Warnings));
        }
    }
}