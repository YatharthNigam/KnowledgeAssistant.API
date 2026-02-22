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
        public async Task<IActionResult> AskAI([FromBody] string userQuestion, [FromHeader(Name = "X-Session-ID")] string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return BadRequest("Session ID is missing.");

            // 1. GET THE "CONTEXT" (Search the Database)
            // Convert user's question to a vector
            var questionVector = await _embeddingService.GetEmbeddingAsync(userQuestion);
            string vectorJson = JsonSerializer.Serialize(questionVector.ToArray());

            string contextText = "";
            string fileName = "Unknown";
            string connString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new SqlConnection(connString))
            {
                string sql = @"
        SELECT TOP 3 FileName, Content 
        FROM Documents 
        WHERE SessionId = @SessionId 
        ORDER BY VECTOR_DISTANCE('cosine', Embedding, CAST(@VectorJson AS VECTOR(1536)))";

                // 1. Changed to QueryAsync to get a LIST of results
                var results = await conn.QueryAsync(sql, new
                {
                    VectorJson = vectorJson,
                    SessionId = sessionId
                });

                // 2. Loop through all 3 chunks and combine them
                if (results != null && results.Any())
                {
                    List<string> sourceFiles = new List<string>();

                    foreach (var row in results)
                    {
                        // Add each chunk of text to the context
                        contextText += row.Content + "\n\n";

                        // Collect the file names, avoiding duplicates
                        string currentFileName = (string)row.FileName;
                        if (!sourceFiles.Contains(currentFileName))
                        {
                            sourceFiles.Add(currentFileName);
                        }
                    }

                    // Combine names like: "Resume.pdf, Boeing.pdf"
                    fileName = string.Join(", ", sourceFiles);
                }
            }

            // 2. TALK TO THE AI (The "RAG" part)
            string endpoint = _configuration["AzureOpenAI:Chat:Endpoint"];
            string key = _configuration["AzureOpenAI:Chat:ApiKey"];
            string deploymentName = _configuration["AzureOpenAI:Chat:DeploymentName"];

            AzureOpenAIClient azureClient = new(new Uri(endpoint), new AzureKeyCredential(key));
            ChatClient chatClient = azureClient.GetChatClient(deploymentName);

            // 1. Handle an empty database or missing context
            if (string.IsNullOrWhiteSpace(contextText))
            {
                contextText = "NO DOCUMENTS FOUND IN DATABASE.";
            }

            // 2. The Strict System Prompt
            ChatCompletion completion = await chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage($@"You are a strict internal Knowledge Assistant. 
            You MUST answer the user's question using ONLY the information provided in the CONTEXT below.
            Under no circumstances should you use your pre-trained knowledge.
            If the CONTEXT is 'NO DOCUMENTS FOUND IN DATABASE', or if the CONTEXT does not contain the exact answer, you MUST reply exactly with: 'I don't know based on my data.'
            
            CONTEXT: {contextText}"),
        new UserChatMessage(userQuestion)
                ]);

            return Ok(new
            {
                Answer = completion.Content[0].Text,
                SourceFile = fileName
            });
        }
    }
}
