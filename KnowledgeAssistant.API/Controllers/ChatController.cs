using Azure;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Mvc;
using OpenAI.Chat;

namespace KnowledgeAssistant.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : Controller
    {
        private readonly IConfiguration _configuration;

        public ChatController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpPost("ask")]
        public async Task<IActionResult> AskAI([FromBody] string userQuestion)
        {
            // 1. Get secrets
            string endpoint = _configuration["AzureOpenAI:Endpoint"];
            string key = _configuration["AzureOpenAI:ApiKey"];
            string deploymentName = _configuration["AzureOpenAI:DeploymentName"];

            // 2. Create Client
            AzureOpenAIClient azureClient = new(new Uri(endpoint), new AzureKeyCredential(key));
            ChatClient chatClient = azureClient.GetChatClient(deploymentName);

            // 3. Call AI
            ChatCompletion completion = await chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage("You are a helpful assistant."),
                    new UserChatMessage(userQuestion)
                ]);

            // 4. Return the text
            return Ok(new { Answer = completion.Content[0].Text });
        }
    }
}
