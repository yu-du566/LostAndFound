using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LostAndFound.Models
{
    /// <summary>
    /// 系统日志表 - 记录关键操作 (对应 D6 系统日志)
    /// </summary>
    public class SystemLog
    {
        [Key]
        public int Id { get; set; }

        [Display(Name = "操作用户ID")]
        public int? UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        [Display(Name = "操作时间")]
        public DateTime OperatedAt { get; set; } = DateTime.Now;

        [Required]
        [StringLength(50)]
        [Display(Name = "操作类型")]
        public string ActionType { get; set; } = string.Empty;

        [StringLength(500)]
        [Display(Name = "操作内容")]
        public string? Content { get; set; }

        [StringLength(50)]
        [Display(Name = "请求IP")]
        public string? IpAddress { get; set; }
    }
}