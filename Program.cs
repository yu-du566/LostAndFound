using Microsoft.EntityFrameworkCore;
using LostAndFound.Data;
using LostAndFound.Hubs;
using LostAndFound.Services;

namespace LostAndFound
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 添加 DbContext - SQLite
            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

            // Session
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromHours(4);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<AuthService>();
            builder.Services.AddScoped<SimilarityService>();
            builder.Services.AddScoped<EmailService>();
            builder.Services.AddScoped<AiMatchingService>();

            // ImageRecognitionService 需要 HttpClient 调用 OpenAI API
            builder.Services.AddHttpClient<ImageRecognitionService>();

            // SignalR 实时消息
            builder.Services.AddSignalR();

            builder.Services.AddControllersWithViews()
                .AddRazorRuntimeCompilation();

            var app = builder.Build();

            // 初始化数据库
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
                if (!db.Users.Any(u => u.Username == "admin"))
                {
                    db.Users.Add(new LostAndFound.Models.User
                    {
                        Username = "admin",
                        PasswordHash = AuthService.HashPassword("admin123"),
                        RealName = "系统管理员",
                        Role = "Admin",
                        CreatedAt = DateTime.Now
                    });
                    db.SaveChanges();
                }

                // ====== 种子数据（如无数据则插入示例数据供图表展示）======
                if (!db.LostItems.Any())
                {
                    var admin = db.Users.FirstOrDefault(u => u.Username == "admin");
                    var rng = new Random();
                    var categories = new[] { "电子产品", "证件卡片", "衣物饰品", "书籍文具", "运动用品", "生活用品", "其他" };
                    var locations = new[] { "图书馆二楼自习室", "思源楼301教室", "食堂一楼", "图书馆一楼大厅",
                        "逸夫教学楼", "操场跑道", "学生活动中心", "体育馆篮球场", "思源楼门口", "食堂二楼" };

                    // 插入 50 条失物
                    for (int i = 0; i < 50; i++)
                    {
                        db.LostItems.Add(new LostAndFound.Models.LostItem
                        {
                            ItemName = new[] { "手机", "校园卡", "钱包", "雨伞", "钥匙", "耳机", "笔记本", "水杯", "书包", "眼镜" }[rng.Next(10)],
                            Category = categories[rng.Next(categories.Length)],
                            LostTime = DateTime.Now.AddDays(-rng.Next(1, 180)),
                            LostLocation = locations[rng.Next(locations.Length)],
                            Description = "示例数据，用于测试图表展示",
                            Status = rng.Next(3) == 0 ? "已认领" : "未认领",
                            CreatedAt = DateTime.Now.AddDays(-rng.Next(1, 180)),
                            UserId = admin!.Id
                        });
                    }

                    // 插入 40 条招领
                    for (int i = 0; i < 40; i++)
                    {
                        db.FoundItems.Add(new LostAndFound.Models.FoundItem
                        {
                            ItemName = new[] { "校园卡", "钥匙", "U盘", "耳机", "手表", "课本", "水杯", "帽子" }[rng.Next(8)],
                            Category = categories[rng.Next(categories.Length)],
                            FoundTime = DateTime.Now.AddDays(-rng.Next(1, 180)),
                            FoundLocation = locations[rng.Next(locations.Length)],
                            Description = "示例数据，用于测试图表展示",
                            Status = rng.Next(4) == 0 ? "已认领" : "未认领",
                            CreatedAt = DateTime.Now.AddDays(-rng.Next(1, 180)),
                            UserId = admin!.Id
                        });
                    }

                    // 插入一些认领记录
                    var claimedLost = db.LostItems.Where(i => i.Status == "已认领").Take(3).ToList();
                    foreach (var item in claimedLost)
                    {
                        db.Claims.Add(new LostAndFound.Models.Claim
                        {
                            LostItemId = item.Id,
                            ApplicantId = admin!.Id,
                            Reason = "这是我的物品，特征完全匹配",
                            Status = "通过",
                            ReviewerId = admin.Id,
                            ReviewedAt = item.CreatedAt.AddDays(rng.Next(1, 14)),
                            AppliedAt = item.CreatedAt.AddDays(rng.Next(1, 10))
                        });
                    }

                    db.SaveChanges();
                }
            }

            if (!app.Environment.IsDevelopment())
                app.UseExceptionHandler("/Home/Error");

            // 跳过 ngrok 免费版的拦截警告页
            app.Use(async (context, next) =>
            {
                context.Response.Headers["ngrok-skip-browser-warning"] = "1";
                await next();
            });

            app.UseStaticFiles();
            app.UseRouting();
            app.UseSession();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.MapHub<NotificationHub>("/notificationHub");

            app.Run();
        }
    }
}