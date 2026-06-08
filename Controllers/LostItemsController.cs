using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LostAndFound.Data;
using LostAndFound.Models;
using LostAndFound.Services;

namespace LostAndFound.Controllers
{
    public class LostItemsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AuthService _auth;
        private readonly SimilarityService _sim;

        public LostItemsController(AppDbContext db, AuthService auth, SimilarityService sim)
        {
            _db = db;
            _auth = auth;
            _sim = sim;
        }

        // 确保登录
        private IActionResult? EnsureLogin()
        {
            if (_auth.GetCurrentUserId() == null)
            {
                TempData["ErrorMsg"] = "请先登录";
                return RedirectToAction("Login", "Account", new { returnUrl = Request.Path });
            }
            return null;
        }

        // GET: /LostItems - 失物查询列表
        [HttpGet]
        public async Task<IActionResult> Index(string? keyword, string? category, string? status, DateTime? startDate, DateTime? endDate)
        {
            var query = _db.LostItems.Include(i => i.Publisher)
                .Where(i => !i.IsArchived).AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
                query = query.Where(i => i.ItemName.Contains(keyword) || (i.Description ?? "").Contains(keyword));
            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(i => i.Category == category);
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(i => i.Status == status);
            if (startDate.HasValue)
                query = query.Where(i => i.LostTime >= startDate.Value);
            if (endDate.HasValue)
                query = query.Where(i => i.LostTime <= endDate.Value);

            ViewBag.Keyword = keyword;
            ViewBag.Category = category;
            ViewBag.Status = status;
            ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd");
            ViewBag.Categories = new[] { "电子产品", "证件卡片", "衣物饰品", "书籍文具", "运动用品", "生活用品", "其他" };

            var items = await query.OrderByDescending(i => i.CreatedAt).ToListAsync();
            return View(items);
        }

        // GET: /LostItems/Create - 发布失物
        [HttpGet]
        public IActionResult Create()
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            ViewBag.Categories = new[] { "电子产品", "证件卡片", "衣物饰品", "书籍文具", "运动用品", "生活用品", "其他" };
            return View();
        }

        // POST: /LostItems/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(LostItem item, IFormFile? imageFile)
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            ViewBag.Categories = new[] { "电子产品", "证件卡片", "衣物饰品", "书籍文具", "运动用品", "生活用品", "其他" };

            if (!ModelState.IsValid) return View(item);

            // 防重复检测
            var duplicates = await _sim.CheckLostItemDuplicateAsync(
                item.ItemName, item.Category, item.Description, item.LostLocation);
            if (duplicates.Any(d => d.Similarity >= 0.7))
            {
                ViewBag.DuplicateWarning = $"存在高度相似（{duplicates[0].Similarity:P0}）的已有失物信息，请确认是否为同一物品";
                ViewBag.Duplicates = duplicates;
                return View(item);
            }

            // 处理图片上传
            if (imageFile != null && imageFile.Length > 0)
            {
                item.ImagePath = await Services.ImageService.SaveImageAsync(
                    imageFile, Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
            }

            item.UserId = _auth.GetCurrentUserId()!.Value;
            item.Status = "未认领";
            item.CreatedAt = DateTime.Now;

            _db.LostItems.Add(item);
            await _db.SaveChangesAsync();

            // 记录日志
            _db.SystemLogs.Add(new SystemLog
            {
                UserId = item.UserId,
                ActionType = "发布失物",
                Content = $"发布失物: {item.ItemName} (ID:{item.Id})",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });

            // 智能匹配
            var matches = await _sim.CrossMatchAsync(item.ItemName, item.Category, item.Description, item.LostLocation, true);
            if (matches.Any(m => m.Similarity >= 0.5))
            {
                TempData["MatchMsg"] = $"发现 {matches.Count} 条可能匹配的招领信息，请查看详情";
                HttpContext.Session.SetString("LastMatchType", "Lost");
                HttpContext.Session.SetInt32("LastItemId", item.Id);
            }

            await _db.SaveChangesAsync();
            TempData["SuccessMsg"] = "失物信息发布成功";
            return RedirectToAction(nameof(Index));
        }

        // GET: /LostItems/MyLostItems - 我的失物
        [HttpGet]
        public async Task<IActionResult> MyLostItems()
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            var userId = _auth.GetCurrentUserId()!.Value;
            var items = await _db.LostItems
                .Where(i => i.UserId == userId)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();
            return View(items);
        }

        // GET: /LostItems/Details/5
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var item = await _db.LostItems
                .Include(i => i.Publisher)
                .Include(i => i.Claims).ThenInclude(c => c.Applicant)
                .FirstOrDefaultAsync(i => i.Id == id);
            if (item == null) return NotFound();
            return View(item);
        }

        // GET: /LostItems/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            var item = await _db.LostItems.FindAsync(id);
            if (item == null) return NotFound();
            if (item.UserId != _auth.GetCurrentUserId()!.Value && !_auth.IsAdmin())
                return Forbid();

            ViewBag.Categories = new[] { "电子产品", "证件卡片", "衣物饰品", "书籍文具", "运动用品", "生活用品", "其他" };
            return View(item);
        }

        // POST: /LostItems/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, LostItem model)
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            var item = await _db.LostItems.FindAsync(id);
            if (item == null) return NotFound();
            if (item.UserId != _auth.GetCurrentUserId()!.Value && !_auth.IsAdmin())
                return Forbid();

            ViewBag.Categories = new[] { "电子产品", "证件卡片", "衣物饰品", "书籍文具", "运动用品", "生活用品", "其他" };

            if (!ModelState.IsValid) return View(model);

            item.ItemName = model.ItemName;
            item.Category = model.Category;
            item.LostTime = model.LostTime;
            item.LostLocation = model.LostLocation;
            item.Description = model.Description;

            await _db.SaveChangesAsync();

            _db.SystemLogs.Add(new SystemLog
            {
                UserId = _auth.GetCurrentUserId(),
                ActionType = "编辑失物",
                Content = $"编辑失物 ID:{id} - {item.ItemName}",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });
            await _db.SaveChangesAsync();

            TempData["SuccessMsg"] = "失物信息修改成功";
            return RedirectToAction(nameof(MyLostItems));
        }

        // POST: /LostItems/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            var item = await _db.LostItems.FindAsync(id);
            if (item == null) return NotFound();
            if (item.UserId != _auth.GetCurrentUserId()!.Value && !_auth.IsAdmin())
                return Forbid();

            _db.LostItems.Remove(item);

            _db.SystemLogs.Add(new SystemLog
            {
                UserId = _auth.GetCurrentUserId(),
                ActionType = "删除失物",
                Content = $"删除失物 ID:{id} - {item.ItemName}",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });
            await _db.SaveChangesAsync();

            TempData["SuccessMsg"] = "失物信息已删除";
            return RedirectToAction(nameof(MyLostItems));
        }

        // GET: /LostItems/Matches/5 - 查看匹配结果
        [HttpGet]
        public async Task<IActionResult> Matches(int id)
        {
            var item = await _db.LostItems.FindAsync(id);
            if (item == null) return NotFound();

            var matches = await _sim.CrossMatchAsync(item.ItemName, item.Category, item.Description, item.LostLocation, true);
            ViewBag.Item = item;
            return View(matches);
        }
    }
}