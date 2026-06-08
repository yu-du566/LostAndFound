using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LostAndFound.Models
{
    /// <summary>
    /// 公告表 - 存储系统公告 (对应 D5 公告通知记录)
    /// </summary>
    public class Announcement
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        [Display(Name = "标题")]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(2000)]
        [Display(Name = "内容")]
        public string Content { get; set; } = string.Empty;

        [Display(Name = "发布时间")]
        public DateTime PublishedAt { get; set; } = DateTime.Now;

        [Display(Name = "发布人ID")]
        public int PublisherId { get; set; }

        [ForeignKey("PublisherId")]
        public User? Publisher { get; set; }

        [Display(Name = "置顶标志")]
        public bool IsTop { get; set; } = false;
    }
}