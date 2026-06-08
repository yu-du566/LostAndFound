using Microsoft.EntityFrameworkCore;
using LostAndFound.Data;
using LostAndFound.Models;

namespace LostAndFound.Services
{
    /// <summary>
    /// 文本相似度检测服务 - 用于防重复发布和智能匹配
    /// 使用Jaccard相似度算法（基于字符bigram）
    /// </summary>
    public class SimilarityService
    {
        private readonly AppDbContext _db;

        public SimilarityService(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// 计算两个字符串的Jaccard相似度（基于字符bigram）
        /// </summary>
        public double CalculateSimilarity(string text1, string text2)
        {
            if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
                return 0;

            var set1 = GetBigramSet(text1.ToLower());
            var set2 = GetBigramSet(text2.ToLower());

            if (set1.Count == 0 && set2.Count == 0) return 1.0;

            var intersection = set1.Intersect(set2).Count();
            var union = set1.Union(set2).Count();

            return union == 0 ? 0 : (double)intersection / union;
        }

        /// <summary>
        /// 构建文本用于相似度计算的字符串（物品名称+类别+描述）
        /// </summary>
        private static string BuildText(string itemName, string category, string? description, string location)
        {
            return $"{itemName} {category} {description ?? ""} {location}".Trim();
        }

        private static HashSet<string> GetBigramSet(string text)
        {
            var set = new HashSet<string>();
            for (int i = 0; i < text.Length - 1; i++)
            {
                set.Add(text.Substring(i, 2));
            }
            return set;
        }

        /// <summary>
        /// 发布失物前的重复检测 - 返回相似度最高的前5条已有记录
        /// </summary>
        public async Task<List<(double Similarity, LostItem Item)>> CheckLostItemDuplicateAsync(
            string itemName, string category, string? description, string location, int? excludeId = null)
        {
            var newText = BuildText(itemName, category, description, location);
            var existing = await _db.LostItems
                .Where(i => i.Status != "已认领")
                .Where(i => excludeId == null || i.Id != excludeId.Value)
                .ToListAsync();

            var results = new List<(double, LostItem)>();
            foreach (var item in existing)
            {
                var oldText = BuildText(item.ItemName, item.Category, item.Description, item.LostLocation);
                var sim = CalculateSimilarity(newText, oldText);
                if (sim >= 0.3) // 阈值30%
                {
                    results.Add((sim, item));
                }
            }
            return results.OrderByDescending(r => r.Item1).Take(5).ToList();
        }

        /// <summary>
        /// 发布招领前的重复检测
        /// </summary>
        public async Task<List<(double Similarity, FoundItem Item)>> CheckFoundItemDuplicateAsync(
            string itemName, string category, string? description, string location, int? excludeId = null)
        {
            var newText = BuildText(itemName, category, description, location);
            var existing = await _db.FoundItems
                .Where(i => i.Status != "已认领")
                .Where(i => excludeId == null || i.Id != excludeId.Value)
                .ToListAsync();

            var results = new List<(double, FoundItem)>();
            foreach (var item in existing)
            {
                var oldText = BuildText(item.ItemName, item.Category, item.Description, item.FoundLocation);
                var sim = CalculateSimilarity(newText, oldText);
                if (sim >= 0.3)
                {
                    results.Add((sim, item));
                }
            }
            return results.OrderByDescending(r => r.Item1).Take(5).ToList();
        }

        /// <summary>
        /// 智能匹配：失物与招领信息的交叉匹配
        /// </summary>
        public async Task<List<(double Similarity, LostItem LostItem, FoundItem FoundItem)>> CrossMatchAsync(
            string itemName, string category, string? description, string location, bool isLost)
        {
            var newText = BuildText(itemName, category, description, location);
            var results = new List<(double, LostItem, FoundItem)>();

            if (isLost)
            {
                // 新发布的是失物，匹配已有招领
                var foundItems = await _db.FoundItems
                    .Where(i => i.Status == "未认领")
                    .Include(i => i.Publisher)
                    .ToListAsync();

                foreach (var fi in foundItems)
                {
                    var oldText = BuildText(fi.ItemName, fi.Category, fi.Description, fi.FoundLocation);
                    var sim = CalculateSimilarity(newText, oldText);
                    if (sim >= 0.3)
                    {
                        var lostItem = new LostItem
                        {
                            ItemName = itemName, Category = category,
                            Description = description, LostLocation = location
                        };
                        results.Add((sim, lostItem, fi));
                    }
                }
            }
            else
            {
                // 新发布的是招领，匹配已有失物
                var lostItems = await _db.LostItems
                    .Where(i => i.Status == "未认领")
                    .Include(i => i.Publisher)
                    .ToListAsync();

                foreach (var li in lostItems)
                {
                    var oldText = BuildText(li.ItemName, li.Category, li.Description, li.LostLocation);
                    var sim = CalculateSimilarity(newText, oldText);
                    if (sim >= 0.3)
                    {
                        var foundItem = new FoundItem
                        {
                            ItemName = itemName, Category = category,
                            Description = description, FoundLocation = location
                        };
                        results.Add((sim, li, foundItem));
                    }
                }
            }

            return results.OrderByDescending(r => r.Item1).Take(10).ToList();
        }
    }
}