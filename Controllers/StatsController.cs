using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LostAndFound.Data;
using LostAndFound.Models;
using LostAndFound.Services;

namespace LostAndFound.Controllers
{
    public class StatsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AuthService _auth;

        public StatsController(AppDbContext db, AuthService auth) { _db = db; _auth = auth; }

        private IActionResult? EnsureAdmin()
        {
            if (!_auth.IsAdmin()) { TempData["ErrorMsg"] = "需要管理员权限"; return RedirectToAction("Index", "Home"); }
            return null;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var adminCheck = EnsureAdmin();
            if (adminCheck != null) return adminCheck;

            var now = DateTime.Now;
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var yearStart = new DateTime(now.Year, 1, 1);
            var monthNames = new[] { "1月","2月","3月","4月","5月","6月","7月","8月","9月","10月","11月","12月" };

            // === 基础统计 ===
            ViewBag.TotalLost = await _db.LostItems.CountAsync();
            ViewBag.TotalFound = await _db.FoundItems.CountAsync();
            ViewBag.TotalClaimed = await _db.LostItems.CountAsync(i => i.Status == "已认领")
                                + await _db.FoundItems.CountAsync(i => i.Status == "已认领");
            ViewBag.MonthLost = await _db.LostItems.CountAsync(i => i.CreatedAt >= monthStart);
            ViewBag.MonthFound = await _db.FoundItems.CountAsync(i => i.CreatedAt >= monthStart);
            ViewBag.YearLost = await _db.LostItems.CountAsync(i => i.CreatedAt >= yearStart);
            ViewBag.YearFound = await _db.FoundItems.CountAsync(i => i.CreatedAt >= yearStart);

            // === 归档 ===
            var cutoff = DateTime.Now.AddDays(-30);
            var oldLost = await _db.LostItems.Where(i => i.Status != "已认领" && !i.IsArchived && i.CreatedAt <= cutoff).ToListAsync();
            var oldFound = await _db.FoundItems.Where(i => i.Status != "已认领" && !i.IsArchived && i.CreatedAt <= cutoff).ToListAsync();
            foreach (var i in oldLost) i.IsArchived = true;
            foreach (var i in oldFound) i.IsArchived = true;
            await _db.SaveChangesAsync();
            ViewBag.NewlyArchived = oldLost.Count + oldFound.Count;
            ViewBag.ArchivedLost = await _db.LostItems.CountAsync(i => i.IsArchived);
            ViewBag.ArchivedFound = await _db.FoundItems.CountAsync(i => i.IsArchived);

            // === 认领时长 ===
            var lostClaimed = await _db.Claims.Where(c => c.LostItemId != null && c.Status == "通过")
                .Select(c => (c.AppliedAt - c.LostItem!.CreatedAt).TotalDays).ToListAsync();
            var foundClaimed = await _db.Claims.Where(c => c.FoundItemId != null && c.Status == "通过")
                .Select(c => (c.AppliedAt - c.FoundItem!.CreatedAt).TotalDays).ToListAsync();
            ViewBag.AvgLostDays = lostClaimed.Any() ? Math.Round(lostClaimed.Average(), 1) : 0;
            ViewBag.AvgFoundDays = foundClaimed.Any() ? Math.Round(foundClaimed.Average(), 1) : 0;

            // === 类别分布（环状图数据）===
            var categories = new[] { "电子产品","证件卡片","衣物饰品","书籍文具","运动用品","生活用品","其他" };
            var lostCat = await _db.LostItems.GroupBy(i => i.Category).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            var foundCat = await _db.FoundItems.GroupBy(i => i.Category).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            ViewBag.Categories = categories.ToList();
            ViewBag.LostCatData = categories.Select(c => lostCat.FirstOrDefault(x => x.Key == c)?.Count ?? 0).ToList();
            ViewBag.FoundCatData = categories.Select(c => foundCat.FirstOrDefault(x => x.Key == c)?.Count ?? 0).ToList();

            // === 各分类已认领数量（供成功归还柱状图）===
            var lostClaimedByCat = await _db.LostItems.Where(i => i.Status == "已认领").GroupBy(i => i.Category)
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            var foundClaimedByCat = await _db.FoundItems.Where(i => i.Status == "已认领").GroupBy(i => i.Category)
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            ViewBag.LostClaimedData = categories.Select(c => lostClaimedByCat.FirstOrDefault(x => x.Key == c)?.Count ?? 0).ToList();
            ViewBag.FoundClaimedData = categories.Select(c => foundClaimedByCat.FirstOrDefault(x => x.Key == c)?.Count ?? 0).ToList();

            // === 类别详细分析表格 ===
            ViewBag.CategoryAnalysis = categories.Select(cat =>
            {
                int lCount = lostCat.FirstOrDefault(x => x.Key == cat)?.Count ?? 0;
                int fCount = foundCat.FirstOrDefault(x => x.Key == cat)?.Count ?? 0;
                int total = lCount + fCount;
                return new
                {
                    Category = cat,
                    LostCount = lCount,
                    FoundCount = fCount,
                    Total = total
                };
            }).OrderByDescending(x => x.Total).ToList();

            // === 月度趋势（折线图数据）===
            var sixMonthsAgo = new DateTime(now.Year, now.Month, 1).AddMonths(-5);
            var monthlyLost = new List<int>();
            var monthlyFound = new List<int>();
            var monthlyLabels = new List<string>();
            for (int m = 0; m < 6; m++)
            {
                var d = sixMonthsAgo.AddMonths(m);
                var next = d.AddMonths(1);
                monthlyLost.Add(await _db.LostItems.CountAsync(i => i.CreatedAt >= d && i.CreatedAt < next));
                monthlyFound.Add(await _db.FoundItems.CountAsync(i => i.CreatedAt >= d && i.CreatedAt < next));
                monthlyLabels.Add($"{d.Month}月");
            }
            ViewBag.MonthlyLabels = monthlyLabels;
            ViewBag.MonthlyLost = monthlyLost;
            ViewBag.MonthlyFound = monthlyFound;

            // === 地点分析（柱状图）===
            var locationStats = (await _db.LostItems
                .GroupBy(i => i.LostLocation.Trim())
                .Select(g => new { Location = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync())
                .Concat((await _db.FoundItems
                .GroupBy(i => i.FoundLocation.Trim())
                .Select(g => new { Location = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync()))
                .GroupBy(x => x.Location)
                .Select(g => new { Location = g.Key, Count = g.Sum(x => x.Count) })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToList();

            ViewBag.LocLabels = locationStats.Select(x => x.Location.Length > 8 ? x.Location[..8] + "..." : x.Location).ToList();
            ViewBag.LocData = locationStats.Select(x => x.Count).ToList();

            // === 黑名单 ===
            ViewBag.Blacklisted = await _db.Users.Where(u => u.IsBlacklisted).ToListAsync();

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Archived()
        {
            var adminCheck = EnsureAdmin();
            if (adminCheck != null) return adminCheck;
            ViewBag.ArchivedLost = await _db.LostItems.Where(i => i.IsArchived).Include(i => i.Publisher).OrderByDescending(i => i.CreatedAt).ToListAsync();
            ViewBag.ArchivedFound = await _db.FoundItems.Where(i => i.IsArchived).Include(i => i.Publisher).OrderByDescending(i => i.CreatedAt).ToListAsync();
            return View();
        }
    }
}