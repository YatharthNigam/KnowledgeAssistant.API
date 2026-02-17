using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using KnowledgeAssistant.API.Services;
using System.Text.Json;

namespace KnowledgeAssistant.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentController : ControllerBase
    {
        private readonly EmbeddingService _embeddingService;
        private readonly IConfiguration _configuration;

        public DocumentController(EmbeddingService embeddingService, IConfiguration configuration)
        {
            _embeddingService = embeddingService;
            _configuration = configuration;
        }

        [HttpPost("ingest")]
        public async Task<IActionResult> IngestDocument([FromBody] string textContent)
        {
            // 1. Convert Text -> Numbers (Vector)
            var vector = await _embeddingService.GetEmbeddingAsync(textContent);

            // 2. Convert Numbers -> JSON String (for SQL)
            // SQL's VECTOR type accepts a JSON array string like "[0.12, 0.33, ...]"
            string vectorJson = JsonSerializer.Serialize(vector.ToArray());

            // 3. Save to SQL Database
            // Note: We use CAST(... AS VECTOR(1536)) to tell SQL this string is actually a vector
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new SqlConnection(connectionString))
            {
                string sql = @"
                    INSERT INTO Documents (FileName, Content, Embedding)
                    VALUES (@FileName, @Content, CAST(@VectorJson AS VECTOR(1536)))";

                await conn.ExecuteAsync(sql, new
                {
                    FileName = "Demo_Upload.txt",
                    Content = textContent,
                    VectorJson = vectorJson
                });
            }

            return Ok("Document chunked and saved to Vector DB!");
        }
    }
}