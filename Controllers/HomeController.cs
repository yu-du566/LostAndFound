using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LostAndFound.Data;
using LostAndFound.Models;
using LostAndFound.Services;
using QRCoder;

namespace LostAndFound.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;
    private readonly AuthService _auth;
    private readonly IConfiguration _config;

    public HomeController(AppDbContext db, AuthService auth, IConfiguration config)
    {
        _db = db;
        _auth = auth;
        _config = config;
    }

    public IActionResult Index()
    {
        ViewBag.TotalLost = _db.LostItems.Count(i => i.Status == "未认领" && !i.IsArchived);
        ViewBag.TotalFound = _db.FoundItems.Count(i => i.Status == "未认领" && !i.IsArchived);
        ViewBag.TotalClaimed = _db.LostItems.Count(i => i.Status == "已认领") + _db.FoundItems.Count(i => i.Status == "已认领");
        ViewBag.Announcements = _db.Announcements
            .OrderByDescending(a => a.IsTop)
            .ThenByDescending(a => a.PublishedAt)
            .Take(5)
            .ToList();

        // 当前用户的未读通知
        var userId = _auth.GetCurrentUserId();
        ViewBag.Notifications = userId != null
            ? _db.Notifications.Where(n => n.UserId == userId && !n.IsRead)
                .OrderByDescending(n => n.CreatedAt).Take(10).ToList()
            : new List<Notification>();
        ViewBag.UnreadCount = userId != null
            ? _db.Notifications.Count(n => n.UserId == userId && !n.IsRead)
            : 0;

        // 局域网访问URL
        var lanIp = GetLocalIP();
        var port = Request.Host.Port ?? 5171;
        ViewBag.LanUrl = lanIp != null ? $"http://{lanIp}:{port}" : $"http://localhost:{port}";

        // ngrok 远程访问URL（从配置读取，空则不显示）
        ViewBag.ExternalUrl = _config["ExternalUrl"] ?? "";

        return View();
    }

    /// <summary>
    /// 生成二维码。支持参数 url 指定地址，否则自动检测本机IP
    /// </summary>
    [HttpGet]
    public IActionResult QrCode(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            var host = Request.Host.Host;
            var port = Request.Host.Port ?? 5171;

            if (host.StartsWith("localhost") || host.StartsWith("127."))
            {
                var ip = GetLocalIP();
                if (!string.IsNullOrEmpty(ip))
                    host = ip;
            }
            url = $"{Request.Scheme}://{host}:{port}/";
        }

        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrData);
        var qrBytes = qrCode.GetGraphic(10);

        return File(qrBytes, "image/png");
    }

    /// <summary>
    /// 获取本机第一个非回环IPv4地址
    /// </summary>
    private static string? GetLocalIP()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    var s = ip.ToString();
                    // 优先返回192.168.x.x（最常见的内网IP段）
                    if (s.StartsWith("192.168.")) return s;
                }
            }
            // 没有192.168则返回第一个IPv4
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return ip.ToString();
            }
        }
        catch { }
        return null;
    }

    public IActionResult Privacy()
    {
        return View();
    }

    // POST: /Home/MarkAllRead
    [HttpPost]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = _auth.GetCurrentUserId();
        if (userId == null) return RedirectToAction("Login", "Account");
        var unread = await _db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync();
        foreach (var n in unread) n.IsRead = true;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
