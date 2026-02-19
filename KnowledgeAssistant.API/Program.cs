using KnowledgeAssistant.API.Services;

var builder = WebApplication.CreateBuilder(args);

// Debug: Verify secrets are loading (Only in Development)
if (builder.Environment.IsDevelopment())
{
    var chatKey = builder.Configuration["AzureOpenAI:Chat:ApiKey"];
    var dbConn = builder.Configuration.GetConnectionString("DefaultConnection");

    if (string.IsNullOrEmpty(chatKey) || chatKey.Contains("PLACEHOLDER"))
    {
        Console.WriteLine("⚠️ WARNING: Secrets not loaded correctly!");
    }
    else
    {
        Console.WriteLine("✅ SUCCESS: User Secrets loaded.");
    }
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular",
        policy => policy.WithOrigins("http://localhost:4200")
                        .AllowAnyMethod()
                        .AllowAnyHeader());
});

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<EmbeddingService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowAngular");

app.UseAuthorization();

app.MapControllers();

app.Run();
