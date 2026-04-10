using Microsoft.AspNetCore.Mvc;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AiController : ControllerBase
{
    private readonly IAiAssistantService _ai;

    public AiController(IAiAssistantService ai)
    {
        _ai = ai;
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { message = "Message is required" });

        // Check if user is Admin/Staff for Admin Context
        bool isAdmin = User.IsInRole("Admin") || User.IsInRole("Manager");

        var response = await _ai.ChatAsync(request.Message, request.ConversationId, isAdmin);
        return Ok(new { response, conversationId = request.ConversationId ?? Guid.NewGuid().ToString() });
    }
}

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
}
