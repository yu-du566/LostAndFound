using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LostAndFound.Models
{
    /// <summary>
    /// 失物信息表 - 存储失主发布的丢失物品信息 (对应 D1 报失信息表)
    /// </summary>
    public class LostItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "物品名称")]
        public string ItemName { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        [Display(Name = "物品类别")]
        public string Category { get; set; } = string.Empty;
        // 类别：电子产品、证件卡片、衣物饰品、书籍文具、运动用品、生活用品、其他

        [Required]
        [Display(Name = "丢失时间")]
        public DateTime LostTime { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "丢失地点")]
        public string LostLocation { get; set; } = string.Empty;

        [StringLength(500)]
        [Display(Name = "特征描述")]
        public string? Description { get; set; }

        [StringLength(200)]
        [Display(Name = "图片路径")]
        public string? ImagePath { get; set; }

        [Required]
        [StringLength(20)]
        [Display(Name = "状态")]
        public string Status { get; set; } = "未认领"; // "未认领" / "认领中" / "已认领"

        [Display(Name = "是否已归档")]
        public bool IsArchived { get; set; } = false;

        [Display(Name = "登记时间")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // 外键
        [Display(Name = "发布者ID")]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User? Publisher { get; set; }

        public ICollection<Claim> Claims { get; set; } = new List<Claim>();
    }
}