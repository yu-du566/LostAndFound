using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using LostAndFound.Data;
using LostAndFound.Hubs;
using LostAndFound.Models;
using LostAndFound.Services;

namespace LostAndFound.Controllers
{
    public class FoundItemsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AuthService _auth;
        private readonly SimilarityService _sim;
        private readonly ImageRecognitionService _aiImage;
        private readonly AiMatchingService _aiMatch;
        private readonly IHubContext<NotificationHub> _hub;

        public FoundItemsController(AppDbContext db, AuthService auth, SimilarityService sim,
            ImageRecognitionService aiImage, AiMatchingService aiMatch, IHubContext<NotificationHub> hub)
        {
            _db = db;
            _auth = auth;
            _sim = sim;
            _aiImage = aiImage;
            _aiMatch = aiMatch;
            _hub = hub;
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

        // GET: /FoundItems/Create?fromLostId=5
        [HttpGet]
        public async Task<IActionResult> Create(int? fromLostId)
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            ViewBag.Categories = new[] { "电子产品", "证件卡片", "衣物饰品", "书籍文具", "运动用品", "生活用品", "其他" };

            // 从失物列表点"我捡到了"过来的，预填信息
            if (fromLostId.HasValue)
            {
                var lostItem = await _db.LostItems.Include(i => i.Publisher).FirstOrDefaultAsync(i => i.Id == fromLostId);
                if (lostItem != null)
                {
                    ViewBag.FromLostId = fromLostId;
                    ViewBag.PreFillName = lostItem.ItemName;
                    ViewBag.PreFillCategory = lostItem.Category;
                    ViewBag.PreFillLocation = lostItem.LostLocation;
                    ViewBag.PreFillDescription = lostItem.Description;
                    ViewBag.LostItemPublisher = lostItem.Publisher?.RealName ?? "未知";
                }
            }

            return View();
        }

        // POST: /FoundItems/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(FoundItem item, IFormFile? imageFile, int? fromLostId)
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            ViewBag.Categories = new[] { "电子产品", "证件卡片", "衣物饰品", "书籍文具", "运动用品", "生活用品", "其他" };

            if (!ModelState.IsValid) return View(item);

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

            // 处理图片上传（重复检测之后）
            if (imageFile != null && imageFile.Length > 0)
            {
                item.ImagePath = await Services.ImageService.SaveImageAsync(
                    imageFile, Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));

                // AI 图片识别 — 上传后自动分类
                if (item.ImagePath != null)
                {
                    var aiCategory = await _aiImage.RecognizeCategoryAsync(
                        item.ImagePath, Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
                    if (aiCategory != null)
                    {
                        item.Category = aiCategory;
                        ViewBag.AiSuggestedCategory = aiCategory;
                    }
                }
            }

            _db.FoundItems.Add(item);
            await _db.SaveChangesAsync();

            // SignalR 广播新招领发布通知
            _ = NotificationHub.SendToAll(_hub, "new_found",
                "📌 新的招领信息", $"有人捡到了 {item.ItemName}（{item.Category}），地点：{item.FoundLocation}");

            _db.SystemLogs.Add(new SystemLog
            {
                UserId = item.UserId,
                ActionType = "发布招领",
                Content = $"发布招领: {item.ItemName} (ID:{item.Id})",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });

            // AI 智能匹配 — 自动匹配已有失物信息 + 给失主发通知
            await _db.SaveChangesAsync();

            var aiMatches = await _aiMatch.MatchFoundToLostAsync(item.Id);

            // 给匹配到的失主生成通知
            foreach (var match in aiMatches.Where(m => m.MatchScore >= 40))
            {
                var lostItem = await _db.LostItems.Include(l => l.Publisher).FirstOrDefaultAsync(l => l.Id == match.LostItemId);
                if (lostItem?.Publisher != null)
                {
                    _db.Notifications.Add(new Notification
                    {
                        UserId = lostItem.Publisher.Id,
                        Message = $"🎉 有人可能捡到了你的「{lostItem.ItemName}」！\n捡到地点：{item.FoundLocation}，匹配度{match.MatchScore:F0}%。\nAI分析：{match.Reason}",
                        RelatedUrl = $"/FoundItems/Details/{item.Id}",
                        CreatedAt = DateTime.Now
                    });
                }
            }

            // 如果是从失物页面点"我捡到了"过来的，也给那个失主发通知
            if (fromLostId.HasValue)
            {
                var sourceLostItem = await _db.LostItems.Include(l => l.Publisher).FirstOrDefaultAsync(l => l.Id == fromLostId);
                if (sourceLostItem?.Publisher != null)
                {
                    _db.Notifications.Add(new Notification
                    {
                        UserId = sourceLostItem.Publisher.Id,
                        Message = $"📦 有人捡到了你的「{sourceLostItem.ItemName}」并发布了招领信息！\n捡到地点：{item.FoundLocation}\n点击查看详情 →",
                        RelatedUrl = $"/FoundItems/Details/{item.Id}",
                        CreatedAt = DateTime.Now
                    });
                }
            }

            await _db.SaveChangesAsync();

            if (aiMatches.Any(m => m.MatchScore >= 40))
            {
                TempData["MatchMsg"] = $"🤖 AI 发现 {aiMatches.Count} 条可能匹配的失物信息！";
                return RedirectToAction(nameof(Matches), new { id = item.Id });
            }

            TempData["SuccessMsg"] = "招领信息发布成功，暂未发现匹配的失物信息";
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
        public async Task<IActionResult> Edit(int id, FoundItem model, IFormFile? imageFile)
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

            // 处理图片上传
            if (imageFile != null && imageFile.Length > 0)
            {
                item.ImagePath = await Services.ImageService.SaveImageAsync(
                    imageFile, Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
            }

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

        // GET: /FoundItems/Matches/5 - 查看 AI 匹配结果
        [HttpGet]
        public async Task<IActionResult> Matches(int id)
        {
            var item = await _db.FoundItems
                .Include(i => i.Publisher)
                .FirstOrDefaultAsync(i => i.Id == id);
            if (item == null) return NotFound();

            // AI 语义匹配
            var aiMatches = await _aiMatch.MatchFoundToLostAsync(id);
            ViewBag.Item = item;
            ViewBag.MatchMsg = TempData["MatchMsg"];
            return View(aiMatches);
        }
    }
}