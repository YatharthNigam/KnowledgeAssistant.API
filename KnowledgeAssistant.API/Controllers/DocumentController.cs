using Dapper;
using KnowledgeAssistant.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using UglyToad.PdfPig;

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

        private List<string> ChunkText(string text, int chunkSize = 1000)
        {
            var chunks = new List<string>();
            // Split the text into words
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            string currentChunk = "";
            foreach (var word in words)
            {
                if ((currentChunk.Length + word.Length) > chunkSize)
                {
                    chunks.Add(currentChunk.Trim());
                    currentChunk = "";
                }
                currentChunk += word + " ";
            }

            if (!string.IsNullOrWhiteSpace(currentChunk))
            {
                chunks.Add(currentChunk.Trim());
            }

            return chunks;
        }

        [HttpPost("ingest")]
        public async Task<IActionResult> IngestDocument([FromBody] string textContent, [FromHeader(Name = "X-Session-ID")] string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return BadRequest("Session ID is missing.");
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
                    INSERT INTO Documents (FileName, Content, Embedding, SessionId)
                    VALUES (@FileName, @Content, CAST(@VectorJson AS VECTOR(1536)), @SessionId)";

                await conn.ExecuteAsync(sql, new
                {
                    FileName = "Demo_Upload.txt",
                    Content = textContent,
                    VectorJson = vectorJson,
                    SessionId = sessionId
                });
            }

            return Ok("Document chunked and saved to Vector DB!");
        }

        [HttpPost("upload-pdf")]
        public async Task<IActionResult> UploadPdf(IFormFile file, [FromHeader(Name = "X-Session-ID")] string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return BadRequest("Session ID is missing.");
            if (file == null || file.Length == 0)
                return BadRequest("Please upload a valid PDF file.");

            // 1. Extract Text from the PDF
            string fullText = "";
            using (var stream = file.OpenReadStream())
            using (var document = PdfDocument.Open(stream))
            {
                foreach (var page in document.GetPages())
                {
                    fullText += page.Text + " ";
                }
            }

            // 2. Slicing the text into chunks
            var chunks = ChunkText(fullText, 1000);
            string connString = _configuration.GetConnectionString("DefaultConnection");
            int savedChunks = 0;

            // 3. Process and Save each chunk
            using (var conn = new SqlConnection(connString))
            {
                await conn.OpenAsync();
                foreach (var chunk in chunks)
                {
                    // Convert chunk to vector
                    var vector = await _embeddingService.GetEmbeddingAsync(chunk);
                    string vectorJson = JsonSerializer.Serialize(vector.ToArray());

                    // Save to SQL (vector goes to Embedding column, session id to SessionId column)
                    string sql = @"
                    INSERT INTO Documents (FileName, Content, Embedding, SessionId) 
                    VALUES (@FileName, @Content, CAST(@VectorJson AS VECTOR(1536)), @SessionId)";

                    await conn.ExecuteAsync(sql, new
                    {
                        FileName = file.FileName,
                        Content = chunk,
                        VectorJson = vectorJson,
                        SessionId = sessionId
                    });

                    savedChunks++;
                }
            }

            return Ok(new { Message = $"Successfully processed and saved {savedChunks} chunks from {file.FileName}." });
        }
    }
}