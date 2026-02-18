using Azure;
using Azure.AI.OpenAI;
using OpenAI.Embeddings;
using System.ClientModel;

namespace KnowledgeAssistant.API.Services
{
    public class EmbeddingService
    {
        private readonly IConfiguration _configuration;

        public EmbeddingService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<ReadOnlyMemory<float>> GetEmbeddingAsync(string text)
        {
            // 1. Get Secrets
            string endpoint = _configuration["AzureOpenAI:Embeddings:Endpoint"];
            string key = _configuration["AzureOpenAI:Embeddings:ApiKey"];
            string deploymentName = _configuration["AzureOpenAI:Embeddings:DeploymentName"];

            // 2. Create Client
            AzureOpenAIClient client = new(new Uri(endpoint), new AzureKeyCredential(key));
            EmbeddingClient embeddingClient = client.GetEmbeddingClient(deploymentName);

            // 3. Generate Embedding
            EmbeddingGenerationOptions options = new() { Dimensions = 1536 };
            ClientResult<OpenAIEmbedding> embedding = await embeddingClient.GenerateEmbeddingAsync(text, options);

            // 4. Return the vector (1536 numbers)
            return embedding.Value.ToFloats();
        }
    }
}