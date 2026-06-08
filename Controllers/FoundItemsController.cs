using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LostAndFound.Data;
using LostAndFound.Models;
using LostAndFound.Services;

namespace LostAndFound.Controllers
{
    public class FoundItemsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AuthService _auth;
        private readonly SimilarityService _sim;

        public FoundItemsController(AppDbContext db, AuthService auth, SimilarityService sim)
        {
            _db = db;
            _auth = auth;
            _sim = sim;
        }

        private IActionResult? EnsureLogin()
        {
            if (_auth.GetCurrentUserId() == null)
            {
                TempData["ErrorMsg"] = "请先登录";
                return RedirectToAction("Login", "Account", new { returnUrl = Request.Path });
            }
            return null;
        }

        // GET: /FoundItems - 招领查询列表
        [HttpGet]
        public async Task<IActionResult> Index(string? keyword, string? category, string? status, DateTime? startDate, DateTime? endDate)
        {
            var query = _db.FoundItems.Include(i => i.Publisher)
                .Where(i => !i.IsArchived).AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
                query = query.Where(i => i.ItemName.Contains(keyword) || (i.Description ?? "").Contains(keyword));
            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(i => i.Category == category);
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(i => i.Status == status);
            if (startDate.HasValue)
                query = query.Where(i => i.FoundTime >= startDate.Value);
            if (endDate.HasValue)
                query = query.Where(i => i.FoundTime <= endDate.Value);

            ViewBag.Keyword = keyword;
            ViewBag.Category = category;
            ViewBag.Status = status;
            ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd");
            ViewBag.Categories = new[] { "电子产品", "证件卡片", "衣物饰品", "书籍文具", "运动用品", "生活用品", "其他" };

            var items = await query.OrderByDescending(i => i.CreatedAt).ToListAsync();
            return View(items);
        }

        // GET: /FoundItems/Create
        [HttpGet]
        public IActionResult Create()
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            ViewBag.Categories = new[] { "电子产品", "证件卡片", "衣物饰品", "书籍文具", "运动用品", "生活用品", "其他" };
            return View();
        }

        // POST: /FoundItems/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(FoundItem item, IFormFile? imageFile)
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            ViewBag.Categories = new[] { "电子产品", "证件卡片", "衣物饰品", "书籍文具", "运动用品", "生活用品", "其他" };

            if (!ModelState.IsValid) return View(item);

            // 处理图片上传
            if (imageFile != null && imageFile.Length > 0)
            {
                item.ImagePath = await Services.ImageService.SaveImageAsync(
                    imageFile, Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
            }

            // 防重复检测
            var duplicates = await _sim.CheckFoundItemDuplicateAsync(
                item.ItemName, item.Category, item.Description, item.FoundLocation);
            if (duplicates.Any(d => d.Similarity >= 0.7))
            {
                ViewBag.DuplicateWarning = $"存在高度相似（{duplicates[0].Similarity:P0}）的已有招领信息，请确认是否为同一物品";
                ViewBag.Duplicates = duplicates;
                return View(item);
            }

            item.UserId = _auth.GetCurrentUserId()!.Value;
            item.Status = "未认领";
            item.CreatedAt = DateTime.Now;

            _db.FoundItems.Add(item);
            await _db.SaveChangesAsync();

            _db.SystemLogs.Add(new SystemLog
            {
                UserId = item.UserId,
                ActionType = "发布招领",
                Content = $"发布招领: {item.ItemName} (ID:{item.Id})",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });

            // 智能匹配
            var matches = await _sim.CrossMatchAsync(item.ItemName, item.Category, item.Description, item.FoundLocation, false);
            if (matches.Any(m => m.Similarity >= 0.5))
            {
                TempData["MatchMsg"] = $"发现 {matches.Count} 条可能匹配的失物信息，请查看详情";
                HttpContext.Session.SetString("LastMatchType", "Found");
                HttpContext.Session.SetInt32("LastItemId", item.Id);
            }

            await _db.SaveChangesAsync();
            TempData["SuccessMsg"] = "招领信息发布成功";
            return RedirectToAction(nameof(Index));
        }

        // GET: /FoundItems/MyFoundItems
        [HttpGet]
        public async Task<IActionResult> MyFoundItems()
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            var userId = _auth.GetCurrentUserId()!.Value;
            var items = await _db.FoundItems
                .Where(i => i.UserId == userId)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();
            return View(items);
        }

        // GET: /FoundItems/Details/5
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var item = await _db.FoundItems
                .Include(i => i.Publisher)
                .Include(i => i.Claims).ThenInclude(c => c.Applicant)
                .FirstOrDefaultAsync(i => i.Id == id);
            if (item == null) return NotFound();
            return View(item);
        }

        // GET: /FoundItems/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            var item = await _db.FoundItems.FindAsync(id);
            if (item == null) return NotFound();
            if (item.UserId != _auth.GetCurrentUserId()!.Value && !_auth.IsAdmin())
                return Forbid();

            ViewBag.Categories = new[] { "电子产品", "证件卡片", "衣物饰品", "书籍文具", "运动用品", "生活用品", "其他" };
            return View(item);
        }

        // POST: /FoundItems/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, FoundItem model)
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            var item = await _db.FoundItems.FindAsync(id);
            if (item == null) return NotFound();
            if (item.UserId != _auth.GetCurrentUserId()!.Value && !_auth.IsAdmin())
                return Forbid();

            ViewBag.Categories = new[] { "电子产品", "证件卡片", "衣物饰品", "书籍文具", "运动用品", "生活用品", "其他" };

            if (!ModelState.IsValid) return View(model);

            item.ItemName = model.ItemName;
            item.Category = model.Category;
            item.FoundTime = model.FoundTime;
            item.FoundLocation = model.FoundLocation;
            item.Description = model.Description;

            await _db.SaveChangesAsync();

            _db.SystemLogs.Add(new SystemLog
            {
                UserId = _auth.GetCurrentUserId(),
                ActionType = "编辑招领",
                Content = $"编辑招领 ID:{id} - {item.ItemName}",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });
            await _db.SaveChangesAsync();

            TempData["SuccessMsg"] = "招领信息修改成功";
            return RedirectToAction(nameof(MyFoundItems));
        }

        // POST: /FoundItems/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            var item = await _db.FoundItems.FindAsync(id);
            if (item == null) return NotFound();
            if (item.UserId != _auth.GetCurrentUserId()!.Value && !_auth.IsAdmin())
                return Forbid();

            _db.FoundItems.Remove(item);

            _db.SystemLogs.Add(new SystemLog
            {
                UserId = _auth.GetCurrentUserId(),
                ActionType = "删除招领",
                Content = $"删除招领 ID:{id} - {item.ItemName}",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });
            await _db.SaveChangesAsync();

            TempData["SuccessMsg"] = "招领信息已删除";
            return RedirectToAction(nameof(MyFoundItems));
        }

        // GET: /FoundItems/Matches/5
        [HttpGet]
        public async Task<IActionResult> Matches(int id)
        {
            var item = await _db.FoundItems.FindAsync(id);
            if (item == null) return NotFound();

            var matches = await _sim.CrossMatchAsync(item.ItemName, item.Category, item.Description, item.FoundLocation, false);
            ViewBag.Item = item;
            return View(matches);
        }
    }
}