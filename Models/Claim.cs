using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LostAndFound.Models
{
    /// <summary>
    /// 认领记录表 - 记录每一次认领申请及审核结果 (对应 D3 认领记录表)
    /// </summary>
    public class Claim
    {
        [Key]
        public int Id { get; set; }

        [Display(Name = "关联失物ID（可为空）")]
        public int? LostItemId { get; set; }

        [ForeignKey("LostItemId")]
        public LostItem? LostItem { get; set; }

        [Display(Name = "关联招领ID（可为空）")]
        public int? FoundItemId { get; set; }

        [ForeignKey("FoundItemId")]
        public FoundItem? FoundItem { get; set; }

        [Required]
        [Display(Name = "申请者ID")]
        public int ApplicantId { get; set; }

        [ForeignKey("ApplicantId")]
        public User? Applicant { get; set; }

        [Required]
        [StringLength(500)]
        [Display(Name = "申请理由")]
        public string Reason { get; set; } = string.Empty;

        [Display(Name = "申请时间")]
        public DateTime AppliedAt { get; set; } = DateTime.Now;

        [Required]
        [StringLength(10)]
        [Display(Name = "审核状态")]
        public string Status { get; set; } = "待审核"; // "待审核" / "通过" / "拒绝"

        [Display(Name = "审核人ID")]
        public int? ReviewerId { get; set; }

        [ForeignKey("ReviewerId")]
        public User? Reviewer { get; set; }

        [Display(Name = "审核时间")]
        public DateTime? ReviewedAt { get; set; }

        [StringLength(300)]
        [Display(Name = "审核备注")]
        public string? Remark { get; set; }
    }
}