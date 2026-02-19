using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using KnowledgeAssistant.API.Services;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using Azure;
using System.Text.Json;

namespace KnowledgeAssistant.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly EmbeddingService _embeddingService;

        public ChatController(IConfiguration configuration, EmbeddingService embeddingService)
        {
            _configuration = configuration;
            _embeddingService = embeddingService;
        }

        [HttpPost("ask")]
        public async Task<IActionResult> AskAI([FromBody] string userQuestion)
        {
            // 1. GET THE "CONTEXT" (Search the Database)
            // Convert user's question to a vector
            var questionVector = await _embeddingService.GetEmbeddingAsync(userQuestion);
            string vectorJson = JsonSerializer.Serialize(questionVector.ToArray());

            string contextText = "";
            string connString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new SqlConnection(connString))
            {
                // This is the Vector Search Query
                // It finds the TOP 1 most similar piece of info from your SQL table
                string sql = @"
                    SELECT TOP 1 Content 
                    FROM Documents 
                    ORDER BY VECTOR_DISTANCE('cosine', Embedding, CAST(@VectorJson AS VECTOR(1536)))";

                contextText = await conn.QueryFirstOrDefaultAsync<string>(sql, new { VectorJson = vectorJson });
            }

            // 2. TALK TO THE AI (The "RAG" part)
            string endpoint = _configuration["AzureOpenAI:Chat:Endpoint"];
            string key = _configuration["AzureOpenAI:Chat:ApiKey"];
            string deploymentName = _configuration["AzureOpenAI:Chat:DeploymentName"];

            AzureOpenAIClient azureClient = new(new Uri(endpoint), new AzureKeyCredential(key));
            ChatClient chatClient = azureClient.GetChatClient(deploymentName);

            // We give the AI the "Context" we found in the database
            ChatCompletion completion = await chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage($@"You are a Knowledge Assistant. 
                        Use the following context to answer the user's question. 
                        If the answer is not in the context, say 'I don't know based on my data.'
                        
                        CONTEXT: {contextText}"),
                    new UserChatMessage(userQuestion)
                ]);

            return Ok(new { Answer = completion.Content[0].Text, SourceUsed = contextText });
        }
    }
}
