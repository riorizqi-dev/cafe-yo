using cafe_yo.Models;

namespace cafe_yo.Services
{
    public interface IChatbotService
    {
        Task<ChatbotReply> AskAsync(string message, HttpContext httpContext);
    }
}

