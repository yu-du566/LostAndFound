using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using LostAndFound.Data;
using LostAndFound.Hubs;
using LostAndFound.Models;
using LostAndFound.Services;

namespace LostAndFound.Controllers
{
    public class ClaimsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AuthService _auth;
        private readonly EmailService _email;
        private readonly IHubContext<NotificationHub> _hub;

        public ClaimsController(AppDbContext db, AuthService auth, EmailService email, IHubContext<NotificationHub> hub)
        {
            _db = db;
            _auth = auth;
            _email = email;
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

        /// <summary>
        /// 将物品信息加载到 ViewBag（不抛出异常）
        /// </summary>
        private bool LoadItemToViewBag(string type, int itemId)
        {
            ViewBag.Type = type;
            ViewBag.ItemId = itemId;

            if (type == "Lost")
            {
                var item = _db.LostItems.Include(i => i.Publisher).FirstOrDefault(i => i.Id == itemId);
                if (item == null) return false;
                ViewBag.Item = item;
            }
            else
            {
                var item = _db.FoundItems.Include(i => i.Publisher).FirstOrDefault(i => i.Id == itemId);
                if (item == null) return false;
                ViewBag.Item = item;
            }
            return true;
        }

        // GET: /Claims/Apply
        [HttpGet]
        public IActionResult Apply(string type, int id)
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            if (!LoadItemToViewBag(type, id))
                return NotFound();

            return View();
        }

        // POST: /Claims/Apply
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Apply(string type, int itemId, string reason)
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            var userId = _auth.GetCurrentUserId()!.Value;

            // 始终加载物品信息（验证失败返回 View 时不会崩溃）
            if (!LoadItemToViewBag(type, itemId))
                return NotFound();

            // 验证申请理由长度
            var charCount = reason?.Trim().Length ?? 0;
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
            {
                ViewBag.Error = $"申请理由不能少于10个字（当前 {charCount} 个字），请补充更多描述。";
                ViewBag.ReasonText = reason; // 保留输入的文字
                return View();
            }

            if (type == "Lost")
            {
                var item = await _db.LostItems.Include(i => i.Publisher).FirstOrDefaultAsync(i => i.Id == itemId);
                if (item == null) return NotFound();
                if (item.UserId == userId)
                {
                    ViewBag.Error = "不能认领自己发布的失物";
                    ViewBag.ReasonText = reason;
                    return View();
                }
                if (item.Status != "未认领")
                {
                    TempData["ErrorMsg"] = "该物品目前不在可认领状态";
                    return RedirectToAction("Details", "LostItems", new { id = itemId });
                }

                item.Status = "认领中";
                _db.Claims.Add(new Claim
                {
                    LostItemId = itemId,
                    ApplicantId = userId,
                    Reason = reason.Trim(),
                    Status = "待审核",
                    AppliedAt = DateTime.Now
                });
            }
            else
            {
                var item = await _db.FoundItems.Include(i => i.Publisher).FirstOrDefaultAsync(i => i.Id == itemId);
                if (item == null) return NotFound();
                if (item.UserId == userId)
                {
                    ViewBag.Error = "不能认领自己发布的招领";
                    ViewBag.ReasonText = reason;
                    return View();
                }
                if (item.Status != "未认领")
                {
                    TempData["ErrorMsg"] = "该物品目前不在可认领状态";
                    return RedirectToAction("Details", "FoundItems", new { id = itemId });
                }

                item.Status = "认领中";
                _db.Claims.Add(new Claim
                {
                    FoundItemId = itemId,
                    ApplicantId = userId,
                    Reason = reason.Trim(),
                    Status = "待审核",
                    AppliedAt = DateTime.Now
                });
            }

            _db.SystemLogs.Add(new SystemLog
            {
                UserId = userId,
                ActionType = "发起认领",
                Content = $"用户对 {(type == "Lost" ? "失物" : "招领")} ID:{itemId} 发起认领申请",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });
            await _db.SaveChangesAsync();

            TempData["SuccessMsg"] = "认领申请已提交，请等待管理员审核";
            return RedirectToAction("MyClaims");
        }

        // GET: /Claims/MyClaims
        [HttpGet]
        public async Task<IActionResult> MyClaims()
        {
            var loginCheck = EnsureLogin();
            if (loginCheck != null) return loginCheck;

            var userId = _auth.GetCurrentUserId()!.Value;
            var claims = await _db.Claims
                .Include(c => c.LostItem)
                .Include(c => c.FoundItem)
                .Include(c => c.Reviewer)
                .Where(c => c.ApplicantId == userId)
                .OrderByDescending(c => c.AppliedAt)
                .ToListAsync();
            return View(claims);
        }

        // GET: /Claims/Pending
        [HttpGet]
        public async Task<IActionResult> Pending()
        {
            if (!_auth.IsAdmin())
            {
                TempData["ErrorMsg"] = "无权限访问";
                return RedirectToAction("Index", "Home");
            }
            var claims = await _db.Claims
                .Include(c => c.LostItem).ThenInclude(i => i!.Publisher)
                .Include(c => c.FoundItem).ThenInclude(i => i!.Publisher)
                .Include(c => c.Applicant)
                .Where(c => c.Status == "待审核")
                .OrderByDescending(c => c.AppliedAt)
                .ToListAsync();
            return View(claims);
        }

        // GET: /Claims/Review/5
        [HttpGet]
        public async Task<IActionResult> Review(int id)
        {
            if (!_auth.IsAdmin()) { TempData["ErrorMsg"] = "无权限访问"; return RedirectToAction("Index", "Home"); }
            var claim = await _db.Claims
                .Include(c => c.LostItem).ThenInclude(i => i!.Publisher)
                .Include(c => c.FoundItem).ThenInclude(i => i!.Publisher)
                .Include(c => c.Applicant)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (claim == null) return NotFound();
            return View(claim);
        }

        // POST: /Claims/Review/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Review(int id, string action, string? remark)
        {
            if (!_auth.IsAdmin()) { TempData["ErrorMsg"] = "无权限访问"; return RedirectToAction("Index", "Home"); }
            var claim = await _db.Claims.Include(c => c.LostItem).Include(c => c.FoundItem).FirstOrDefaultAsync(c => c.Id == id);
            if (claim == null) return NotFound();

            if (action == "approve")
            {
                claim.Status = "通过"; claim.Remark = remark;
                claim.ReviewerId = _auth.GetCurrentUserId(); claim.ReviewedAt = DateTime.Now;
                if (claim.LostItem != null) claim.LostItem.Status = "已认领";
                if (claim.FoundItem != null) claim.FoundItem.Status = "已认领";
                _db.SystemLogs.Add(new SystemLog { UserId = _auth.GetCurrentUserId(), ActionType = "审核通过", Content = $"认领申请 ID:{id} 审核通过", IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() });

                // 发送邮件通知申请人
                var applicant = await _db.Users.FindAsync(claim.ApplicantId);
                if (applicant?.Email != null)
                {
                    var itemName = claim.LostItem?.ItemName ?? claim.FoundItem?.ItemName ?? "未知物品";
                    _ = _email.SendAsync(applicant.Email, "认领申请已通过 ✅",
                        $"<h3>📦 认领申请已通过</h3><p>您对 <b>{itemName}</b> 的认领申请已通过审核。</p><p>请联系管理员领取物品。</p>{(remark != null ? $"<p><b>审核备注：</b>{remark}</p>" : "")}");
                }

                // SignalR 通知申请人
                _ = NotificationHub.SendToUser(_hub, claim.ApplicantId, "claim_approved",
                    "认领申请已通过", $"您对 {(claim.LostItem?.ItemName ?? claim.FoundItem?.ItemName ?? "物品")} 的认领申请已通过审核！");
            }
            else if (action == "reject")
            {
                claim.Status = "拒绝"; claim.Remark = remark;
                claim.ReviewerId = _auth.GetCurrentUserId(); claim.ReviewedAt = DateTime.Now;
                if (claim.LostItem != null) claim.LostItem.Status = "未认领";
                if (claim.FoundItem != null) claim.FoundItem.Status = "未认领";
                _db.SystemLogs.Add(new SystemLog { UserId = _auth.GetCurrentUserId(), ActionType = "审核拒绝", Content = $"认领申请 ID:{id} 被拒绝，原因: {remark}", IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() });

                // 发送邮件通知申请人
                var applicant = await _db.Users.FindAsync(claim.ApplicantId);
                if (applicant?.Email != null)
                {
                    var itemName = claim.LostItem?.ItemName ?? claim.FoundItem?.ItemName ?? "未知物品";
                    _ = _email.SendAsync(applicant.Email, "认领申请未通过",
                        $"<h3>📦 认领申请未通过</h3><p>很遗憾，您对 <b>{itemName}</b> 的认领申请未通过审核。</p>{(remark != null ? $"<p><b>审核备注：</b>{remark}</p>" : "")}<p>如有疑问请联系管理员。</p>");
                }

                // SignalR 通知申请人
                _ = NotificationHub.SendToUser(_hub, claim.ApplicantId, "claim_rejected",
                    "认领申请未通过", $"您对 {(claim.LostItem?.ItemName ?? claim.FoundItem?.ItemName ?? "物品")} 的认领申请未通过审核。");
            }
            await _db.SaveChangesAsync();
            TempData["SuccessMsg"] = $"认领申请已{(action == "approve" ? "通过" : "拒绝")}";
            return RedirectToAction(nameof(Pending));
        }
    }
}