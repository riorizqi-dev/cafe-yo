namespace cafe_yo.Models
{
    public sealed class ChatbotAskRequest
    {
        public string Message { get; set; } = string.Empty;
    }

    public sealed class ChatbotReply
    {
        public string Intent { get; set; } = "fallback";
        public string Answer { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
        public int? MatchedMenuItemId { get; set; }
        public int? MatchedFaqId { get; set; }
    }
}

