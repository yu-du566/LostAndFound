using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LostAndFound.Models
{
    /// <summary>
    /// 用户通知表 — 存储系统推送给用户的消息（如匹配通知等）
    /// </summary>
    public class Notification
    {
        [Key]
        public int Id { get; set; }

        [Display(Name = "接收用户ID")]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        [Required]
        [StringLength(500)]
        [Display(Name = "通知内容")]
        public string Message { get; set; } = string.Empty;

        [StringLength(200)]
        [Display(Name = "跳转链接")]
        public string? RelatedUrl { get; set; }

        [Display(Name = "是否已读")]
        public bool IsRead { get; set; } = false;

        [Display(Name = "创建时间")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
