namespace LostAndFound.Models
{
    public class RankItem
    {
        public int Rank { get; set; }
        public int UserId { get; set; }
        public string Username { get; set; } = "";
        public string RealName { get; set; } = "";
        public int Count { get; set; }
    }
}