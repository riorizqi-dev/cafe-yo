using Microsoft.AspNetCore.Mvc;
using cafe_yo.Models;
using cafe_yo.Services;

namespace cafe_yo.Controllers
{
    [ApiController]
    [Route("api/chatbot")]
    public sealed class ChatbotApiController : ControllerBase
    {
        private readonly IChatbotService _chatbotService;

        public ChatbotApiController(IChatbotService chatbotService)
        {
            _chatbotService = chatbotService;
        }

        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] ChatbotAskRequest request)
        {
            var message = request?.Message?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(message))
            {
                return BadRequest(new { ok = false, message = "Pesan tidak boleh kosong." });
            }

            if (message.Length > 500)
            {
                return BadRequest(new { ok = false, message = "Pesan terlalu panjang. Maksimal 500 karakter." });
            }

            var result = await _chatbotService.AskAsync(message, HttpContext);
            return Ok(new { ok = true, data = result });
        }
    }
}

