using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LostAndFound.Data;
using LostAndFound.Models;
using QRCoder;

namespace LostAndFound.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db)
    {
        _db = db;
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
        return View();
    }

    /// <summary>
    /// 生成二维码，自动检测本机IP，手机扫描后直接访问
    /// </summary>
    [HttpGet]
    public IActionResult QrCode()
    {
        var host = Request.Host.Host;
        var port = Request.Host.Port ?? 5000;

        // 如果从localhost访问，用真实IP替换，手机才能扫码访问
        if (host.StartsWith("localhost") || host.StartsWith("127."))
        {
            var ip = GetLocalIP();
            if (!string.IsNullOrEmpty(ip))
                host = ip;
        }
        var url = $"{Request.Scheme}://{host}:{port}/";

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

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
