using System.Net;
using System.Text;
using System.Text.Json;

namespace LostAndFound.Services
{
    /// <summary>
    /// AI 图片识别服务 — 使用百度图像识别 API 对上传的图片进行物品分类
    /// </summary>
    public class ImageRecognitionService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ImageRecognitionService> _logger;
        private readonly HttpClient _http;

        // 缓存 access_token（有效期30天）
        private static string? _cachedToken;
        private static DateTime _tokenExpiry = DateTime.MinValue;

        private static readonly string[] Categories =
            ["电子产品", "证件卡片", "衣物饰品", "书籍文具", "运动用品", "生活用品", "其他"];

        // 百度识别结果关键词 → 系统类别映射
        private static readonly Dictionary<string, string> KeywordCategoryMap = new()
        {
            // 电子产品
            ["手机"] = "电子产品", ["电脑"] = "电子产品", ["笔记本"] = "电子产品", ["耳机"] = "电子产品",
            ["平板"] = "电子产品", ["充电器"] = "电子产品", ["u盘"] = "电子产品", ["硬盘"] = "电子产品",
            ["数据线"] = "电子产品", ["鼠标"] = "电子产品", ["键盘"] = "电子产品", ["相机"] = "电子产品",
            ["手表"] = "电子产品", ["手环"] = "电子产品", ["音箱"] = "电子产品", ["电子"] = "电子产品",

            // 证件卡片
            ["身份证"] = "证件卡片", ["银行卡"] = "证件卡片", ["校园卡"] = "证件卡片", ["学生证"] = "证件卡片",
            ["驾驶证"] = "证件卡片", ["护照"] = "证件卡片", ["工作证"] = "证件卡片", ["票据"] = "证件卡片",
            ["文件袋"] = "证件卡片", ["卡包"] = "证件卡片", ["名片"] = "证件卡片",

            // 衣物饰品
            ["衣服"] = "衣物饰品", ["外套"] = "衣物饰品", ["裤子"] = "衣物饰品", ["裙子"] = "衣物饰品",
            ["鞋"] = "衣物饰品", ["帽子"] = "衣物饰品", ["围巾"] = "衣物饰品", ["手套"] = "衣物饰品",
            ["包"] = "衣物饰品", ["钱包"] = "衣物饰品", ["背包"] = "衣物饰品", ["饰品"] = "衣物饰品",
            ["眼镜"] = "衣物饰品", ["首饰"] = "衣物饰品", ["戒指"] = "衣物饰品", ["项链"] = "衣物饰品",

            // 书籍文具
            ["书"] = "书籍文具", ["课本"] = "书籍文具", ["笔记本"] = "书籍文具", ["笔"] = "书籍文具",
            ["文具"] = "书籍文具", ["文件夹"] = "书籍文具", ["纸张"] = "书籍文具",

            // 运动用品
            ["球"] = "运动用品", ["运动"] = "运动用品", ["健身"] = "运动用品", ["瑜伽"] = "运动用品",
            ["跳绳"] = "运动用品", ["护具"] = "运动用品",

            // 生活用品
            ["杯"] = "生活用品", ["伞"] = "生活用品", ["钥匙"] = "生活用品", ["水壶"] = "生活用品",
            ["箱"] = "生活用品", ["袋子"] = "生活用品", ["毛巾"] = "生活用品", ["化妆品"] = "生活用品",
            ["食品"] = "生活用品", ["餐具"] = "生活用品", ["日用品"] = "生活用品", ["工具"] = "生活用品",
        };

        public ImageRecognitionService(IConfiguration config, ILogger<ImageRecognitionService> logger, HttpClient http)
        {
            _config = config;
            _logger = logger;
            _http = http;
        }

        /// <summary>
        /// 获取百度 API access_token（缓存30天）
        /// </summary>
        private async Task<string?> GetAccessTokenAsync()
        {
            if (_cachedToken != null && DateTime.Now < _tokenExpiry)
                return _cachedToken;

            var apiKey = _config["BaiduAI:ApiKey"];
            var secretKey = _config["BaiduAI:SecretKey"];

            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secretKey))
            {
                _logger.LogWarning("百度AI ApiKey/SecretKey 未配置");
                return null;
            }

            try
            {
                var url = $"https://aip.baidubce.com/oauth/2.0/token?grant_type=client_credentials&client_id={apiKey}&client_secret={secretKey}";
                var response = await _http.PostAsync(url, null);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var token = doc.RootElement.GetProperty("access_token").GetString();
                var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();

                _cachedToken = token;
                _tokenExpiry = DateTime.Now.AddSeconds(expiresIn - 3600); // 提前1小时刷新
                _logger.LogInformation("百度AI access_token 获取成功，有效期至 {Expiry}", _tokenExpiry);

                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取百度AI access_token 失败");
                return null;
            }
        }

        /// <summary>
        /// 识别图片中的物品类别。返回类别名称，失败返回 null。
        /// </summary>
        public async Task<string?> RecognizeCategoryAsync(string imagePath, string wwwrootPath)
        {
            var apiKey = _config["BaiduAI:ApiKey"];

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("百度AI ApiKey 未配置，跳过图片识别");
                return null;
            }

            try
            {
                // 1. 获取 access_token
                var token = await GetAccessTokenAsync();
                if (token == null) return null;

                // 2. 读取图片并 base64 编码
                var fullPath = Path.Combine(wwwrootPath, imagePath.TrimStart('/'));
                if (!File.Exists(fullPath))
                {
                    _logger.LogWarning("图片文件不存在: {Path}", fullPath);
                    return null;
                }

                var imageBytes = await File.ReadAllBytesAsync(fullPath);
                var base64 = Convert.ToBase64String(imageBytes);
                _logger.LogInformation("图片已编码: {Path}, 大小={Size}KB", fullPath, imageBytes.Length / 1024);

                // 3. 调用百度通用物体识别 API
                var apiUrl = $"https://aip.baidubce.com/rest/2.0/image-classify/v2/advanced_general?access_token={token}";
                var postData = new Dictionary<string, string>
                {
                    ["image"] = base64,
                    ["top_num"] = "5",
                    ["baike_num"] = "0"
                };
                var content = new FormUrlEncodedContent(postData);

                var response = await _http.PostAsync(apiUrl, content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);

                // 检查错误
                if (doc.RootElement.TryGetProperty("error_code", out var errorCode))
                {
                    var errorMsg = doc.RootElement.GetProperty("error_msg").GetString();
                    _logger.LogError("百度AI 返回错误: {Code} - {Msg}", errorCode.GetInt32(), errorMsg);
                    return null;
                }

                // 4. 解析结果
                var results = doc.RootElement.GetProperty("result");
                foreach (var item in results.EnumerateArray())
                {
                    var keyword = item.GetProperty("keyword").GetString()?.ToLower() ?? "";
                    var score = item.GetProperty("score").GetDouble();
                    var root = item.TryGetProperty("root", out var r) ? r.GetString()?.ToLower() ?? "" : "";

                    _logger.LogInformation("百度识别: keyword={Keyword}, score={Score}, root={Root}", keyword, score, root);

                    // 先尝试关键词映射
                    foreach (var (k, cat) in KeywordCategoryMap)
                    {
                        if (keyword.Contains(k.ToLower()) || root.Contains(k.ToLower()))
                        {
                            _logger.LogInformation("AI 分类匹配成功: {Keyword} → {Category}", keyword, cat);
                            return cat;
                        }
                    }

                    // 再根据百度 root 层级推断
                    if (root.Contains("电子") || root.Contains("数码") || root.Contains("手机") || root.Contains("电脑"))
                        return "电子产品";
                    if (root.Contains("服饰") || root.Contains("鞋") || root.Contains("包") || root.Contains("眼镜"))
                        return "衣物饰品";
                    if (root.Contains("书籍") || root.Contains("文具") || root.Contains("办公"))
                        return "书籍文具";
                    if (root.Contains("运动") || root.Contains("健身") || root.Contains("体育"))
                        return "运动用品";
                    if (root.Contains("日用") || root.Contains("家居") || root.Contains("食品"))
                        return "生活用品";
                    if (root.Contains("证件") || root.Contains("卡片") || root.Contains("票据"))
                        return "证件卡片";
                }

                _logger.LogInformation("AI 分类未匹配到具体类别，返回\"其他\"");
                return "其他";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "百度AI 图片识别失败");
                return null; // 静默降级
            }
        }
    }
}
