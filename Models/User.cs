using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LostAndFound.Models
{
    /// <summary>
    /// 用户表 - 存储系统用户信息
    /// </summary>
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "用户名")]
        public string Username { get; set; } = string.Empty;

        [Required]
        [StringLength(256)]
        [Display(Name = "密码哈希")]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        [Display(Name = "姓名")]
        public string RealName { get; set; } = string.Empty;

        [StringLength(20)]
        [Display(Name = "手机号")]
        public string? Phone { get; set; }

        [StringLength(100)]
        [Display(Name = "邮箱")]
        public string? Email { get; set; }

        [Required]
        [StringLength(10)]
        [Display(Name = "角色")]
        public string Role { get; set; } = "User"; // "User" 或 "Admin"

        [Display(Name = "警告次数")]
        public int WarningCount { get; set; } = 0;

        [Display(Name = "是否被拉黑")]
        public bool IsBlacklisted { get; set; } = false;

        [Display(Name = "黑名单截止日期")]
        public DateTime? BlacklistUntil { get; set; }

        [Display(Name = "创建时间")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // 导航属性
        public ICollection<LostItem> LostItems { get; set; } = new List<LostItem>();
        public ICollection<FoundItem> FoundItems { get; set; } = new List<FoundItem>();
        public ICollection<Claim> Claims { get; set; } = new List<Claim>();
    }
}