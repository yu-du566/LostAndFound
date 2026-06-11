using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using LostAndFound.Data;
using LostAndFound.Models;

namespace LostAndFound.Services
{
    /// <summary>
    /// AI 智能匹配服务 — 使用 DeepSeek 语义分析，将失物与招领信息进行智能匹配
    /// </summary>
    public class AiMatchingService
    {
        private readonly AppDbContext _db;
        private readonly SimilarityService _sim;
        private readonly IConfiguration _config;
        private readonly ILogger<AiMatchingService> _logger;

        public AiMatchingService(AppDbContext db, SimilarityService sim,
            IConfiguration config, ILogger<AiMatchingService> logger)
        {
            _db = db;
            _sim = sim;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// AI 匹配结果
        /// </summary>
        public class MatchResult
        {
            public int FoundItemId { get; set; }
            public double MatchScore { get; set; }  // 0-100
            public string Reason { get; set; } = "";
            public FoundItem? FoundItem { get; set; }
        }

        public class LostMatchResult
        {
            public int LostItemId { get; set; }
            public double MatchScore { get; set; }
            public string Reason { get; set; } = "";
            public LostItem? LostItem { get; set; }
        }

        /// <summary>
        /// 为新发布的失物匹配已有的招领信息
        /// </summary>
        public async Task<List<MatchResult>> MatchLostToFoundAsync(int lostItemId)
        {
            var lostItem = await _db.LostItems.Include(i => i.Publisher).FirstOrDefaultAsync(i => i.Id == lostItemId);
            if (lostItem == null) return [];

            // 第一步：用 Jaccard 相似度粗筛前 15 条候选
            var candidates = await _sim.CheckFoundItemDuplicateAsync(
                lostItem.ItemName, lostItem.Category, lostItem.Description, lostItem.LostLocation);

            if (candidates.Count == 0)
            {
                _logger.LogInformation("AI 匹配：未找到 Jaccard 候选，无需 DeepSeek 介入");
                return [];
            }

            // 转换为带 ID 的列表供 AI 评估
            var candidateList = candidates.Select(c => c.Item).ToList();

            _logger.LogInformation("AI 匹配：{Count} 条候选招领 → 调用 DeepSeek 语义匹配", candidateList.Count);

            // 第二步：调用 DeepSeek 进行语义匹配
            return await DeepSeekMatchLostAsync(lostItem, candidateList);
        }

        /// <summary>
        /// 为新发布的招领匹配已有的失物信息
        /// </summary>
        public async Task<List<LostMatchResult>> MatchFoundToLostAsync(int foundItemId)
        {
            var foundItem = await _db.FoundItems.Include(i => i.Publisher).FirstOrDefaultAsync(i => i.Id == foundItemId);
            if (foundItem == null) return [];

            // Jaccard 粗筛
            var candidates = await _sim.CheckLostItemDuplicateAsync(
                foundItem.ItemName, foundItem.Category, foundItem.Description, foundItem.FoundLocation);

            if (candidates.Count == 0)
            {
                _logger.LogInformation("AI 匹配：未找到 Jaccard 候选，无需 DeepSeek 介入");
                return [];
            }

            var candidateList = candidates.Select(c => c.Item).ToList();
            _logger.LogInformation("AI 匹配：{Count} 条候选失物 → 调用 DeepSeek 语义匹配", candidateList.Count);

            return await DeepSeekMatchFoundAsync(foundItem, candidateList);
        }

        /// <summary>
        /// 调用 DeepSeek 进行语义匹配 — 失物 vs 招领列表
        /// </summary>
        private async Task<List<MatchResult>> DeepSeekMatchLostAsync(LostItem lostItem, List<FoundItem> candidates)
        {
            var apiKey = _config["OpenAI:ApiKey"];
            var model = _config["OpenAI:Model"] ?? "deepseek-chat";
            var endpoint = _config["OpenAI:Endpoint"] ?? "https://api.deepseek.com/v1/chat/completions";

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("DeepSeek ApiKey 未配置，降级为 Jaccard 匹配");
                return candidates.Select(c => new MatchResult
                {
                    FoundItemId = c.Id,
                    MatchScore = 0,
                    Reason = "基于文本相似度匹配（未启用AI）",
                    FoundItem = c
                }).ToList();
            }

            try
            {
                // 构建提示：将招领列表描述给 AI
                var candidateDesc = new List<string>();
                for (int i = 0; i < candidates.Count; i++)
                {
                    var c = candidates[i];
                    candidateDesc.Add(
                        $"[ID:{c.Id}] 物品:{c.ItemName} | 类别:{c.Category} | 地点:{c.FoundLocation} | 时间:{c.FoundTime:yyyy-MM-dd} | 描述:{c.Description ?? "无"}");
                }

                var prompt = $$"""
你是一个失物招领匹配助手。请根据以下失物信息，从招领列表中找出最可能匹配的物品。

【失物信息】
物品名称：{{lostItem.ItemName}}
类别：{{lostItem.Category}}
丢失地点：{{lostItem.LostLocation}}
丢失时间：{{lostItem.LostTime:yyyy-MM-dd}}
描述：{{lostItem.Description ?? "无"}}

【招领候选列表】
{{string.Join("\n", candidateDesc)}}

请分析每个招领物品与失物的匹配程度（考虑物品名称相似度、类别一致性、地点距离、时间接近度、描述语义关联），选出匹配度 >= 40（满分100）的物品。

严格按以下JSON数组格式返回，不要包含任何其他文字：
[{"id":招领ID,"score":匹配度0-100,"reason":"中文匹配理由(20字以内)"}]

如果没有任何物品匹配度达到40分，返回空数组：[]
""";

                var result = await CallDeepSeekAsync(apiKey, model, endpoint, prompt);
                return ParseLostMatchResults(result, candidates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeepSeek AI 匹配失败，降级为 Jaccard 匹配");
                return candidates.Select(c => new MatchResult
                {
                    FoundItemId = c.Id,
                    MatchScore = 0,
                    Reason = "基于文本相似度匹配（AI暂时不可用）",
                    FoundItem = c
                }).ToList();
            }
        }

        /// <summary>
        /// 调用 DeepSeek 进行语义匹配 — 招领 vs 失物列表
        /// </summary>
        private async Task<List<LostMatchResult>> DeepSeekMatchFoundAsync(FoundItem foundItem, List<LostItem> candidates)
        {
            var apiKey = _config["OpenAI:ApiKey"];
            var model = _config["OpenAI:Model"] ?? "deepseek-chat";
            var endpoint = _config["OpenAI:Endpoint"] ?? "https://api.deepseek.com/v1/chat/completions";

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return candidates.Select(c => new LostMatchResult
                {
                    LostItemId = c.Id,
                    MatchScore = 0,
                    Reason = "基于文本相似度匹配（未启用AI）",
                    LostItem = c
                }).ToList();
            }

            try
            {
                var candidateDesc = new List<string>();
                for (int i = 0; i < candidates.Count; i++)
                {
                    var c = candidates[i];
                    candidateDesc.Add(
                        $"[ID:{c.Id}] 物品:{c.ItemName} | 类别:{c.Category} | 地点:{c.LostLocation} | 时间:{c.LostTime:yyyy-MM-dd} | 描述:{c.Description ?? "无"}");
                }

                var prompt = $$"""
你是一个失物招领匹配助手。请根据以下招领信息，从失物列表中找出最可能匹配的物品。

【招领信息】
物品名称：{{foundItem.ItemName}}
类别：{{foundItem.Category}}
捡到地点：{{foundItem.FoundLocation}}
捡到时间：{{foundItem.FoundTime:yyyy-MM-dd}}
描述：{{foundItem.Description ?? "无"}}

【失物候选列表】
{{string.Join("\n", candidateDesc)}}

请分析每个失物与招领的匹配程度，选出匹配度 >= 40 的物品。

严格按以下JSON数组格式返回，不要包含任何其他文字：
[{"id":失物ID,"score":匹配度0-100,"reason":"中文匹配理由(20字以内)"}]

如果没有任何物品匹配度达到40分，返回空数组：[]
""";

                var result = await CallDeepSeekAsync(apiKey, model, endpoint, prompt);
                return ParseLostMatchResultsForFound(result, candidates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeepSeek AI 匹配失败，降级为 Jaccard 匹配");
                return candidates.Select(c => new LostMatchResult
                {
                    LostItemId = c.Id,
                    MatchScore = 0,
                    Reason = "基于文本相似度匹配（AI暂时不可用）",
                    LostItem = c
                }).ToList();
            }
        }

        /// <summary>
        /// 调用 DeepSeek API
        /// </summary>
        private static async Task<string> CallDeepSeekAsync(string apiKey, string model, string endpoint, string prompt)
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var body = new { model, max_tokens = 800, messages = new[] { new { role = "user", content = prompt } } };
            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await http.PostAsync(endpoint, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "[]";
        }

        /// <summary>
        /// 解析 DeepSeek 返回的 JSON 匹配结果 → MatchResult 列表
        /// </summary>
        private List<MatchResult> ParseLostMatchResults(string aiResponse, List<FoundItem> candidates)
        {
            var results = new List<MatchResult>();
            try
            {
                // 清理 AI 可能返回的 markdown ```json ``` 包裹
                var cleanJson = aiResponse.Trim();
                if (cleanJson.StartsWith("```"))
                {
                    var endIdx = cleanJson.LastIndexOf("```", StringComparison.Ordinal);
                    cleanJson = cleanJson[cleanJson.IndexOf('\n')..endIdx].Trim();
                }

                using var doc = JsonDocument.Parse(cleanJson);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var id = item.GetProperty("id").GetInt32();
                    var score = item.GetProperty("score").GetDouble();
                    var reason = item.GetProperty("reason").GetString() ?? "";

                    var foundItem = candidates.FirstOrDefault(c => c.Id == id);
                    results.Add(new MatchResult
                    {
                        FoundItemId = id,
                        MatchScore = score,
                        Reason = reason,
                        FoundItem = foundItem
                    });
                }

                _logger.LogInformation("AI 匹配结果: {Count} 条, 最高分={MaxScore}",
                    results.Count, results.Any() ? results.Max(r => r.MatchScore) : 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析 AI 匹配 JSON 失败\nRaw: {Raw}", aiResponse);
            }
            return results.OrderByDescending(r => r.MatchScore).ToList();
        }

        /// <summary>
        /// 解析 DeepSeek 返回的 JSON 匹配结果 → LostMatchResult 列表
        /// </summary>
        private List<LostMatchResult> ParseLostMatchResultsForFound(string aiResponse, List<LostItem> candidates)
        {
            var results = new List<LostMatchResult>();
            try
            {
                var cleanJson = aiResponse.Trim();
                if (cleanJson.StartsWith("```"))
                {
                    var endIdx = cleanJson.LastIndexOf("```", StringComparison.Ordinal);
                    cleanJson = cleanJson[cleanJson.IndexOf('\n')..endIdx].Trim();
                }

                using var doc = JsonDocument.Parse(cleanJson);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var id = item.GetProperty("id").GetInt32();
                    var score = item.GetProperty("score").GetDouble();
                    var reason = item.GetProperty("reason").GetString() ?? "";

                    var lostItem = candidates.FirstOrDefault(c => c.Id == id);
                    results.Add(new LostMatchResult
                    {
                        LostItemId = id,
                        MatchScore = score,
                        Reason = reason,
                        LostItem = lostItem
                    });
                }

                _logger.LogInformation("AI 匹配结果: {Count} 条, 最高分={MaxScore}",
                    results.Count, results.Any() ? results.Max(r => r.MatchScore) : 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析 AI 匹配 JSON 失败\nRaw: {Raw}", aiResponse);
            }
            return results.OrderByDescending(r => r.MatchScore).ToList();
        }
    }
}
