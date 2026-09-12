using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using System.ClientModel;
using WebApplication4;
using WebApplication4.Data;
using WebApplication4.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Database Setup
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("SupabaseConnection"),
        npgsqlOptions =>
        {
            npgsqlOptions.UseVector();
            npgsqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
            npgsqlOptions.CommandTimeout(60);
        }
    ));

var startupLogger = LoggerFactory.Create(cfg => cfg.AddConsole()).CreateLogger("Startup");

var ollamaBaseUri = new Uri("http://localhost:11434/v1");
var ollamaDummyCredential = new ApiKeyCredential("dummy");

// ============================================
// 2. EMBEDDING — config se switch (Ollama / Cloud)
// ============================================
var embeddingProvider = builder.Configuration["EmbeddingProvider"] ?? "Ollama";
startupLogger.LogInformation("Embedding provider selected: {Provider}", embeddingProvider);

var geminiToken = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                      ?? builder.Configuration["GEMINI_API_KEY"];
var geminiCredential = new ApiKeyCredential(geminiToken);

// 2. Google Gemini ka OpenAI-Compatible Endpoint use karein
var geminiOptions = new OpenAIClientOptions
{
    Endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/")
};

// 3. Gemini ka Embedding Model ("text-embedding-004") load karein
IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator = new GeminiDirectGenerator(geminiToken);



// ============================================
// 3. CHAT — config se switch (Ollama / Groq / OpenAI)
// ============================================
var aiProvider = builder.Configuration["AiProvider"] ?? "Ollama";
startupLogger.LogInformation("Chat provider selected: {Provider}", aiProvider);

IChatClient chatClient;
bool ollamaNeededForChat = false;

//switch (aiProvider.ToLowerInvariant())
//{
//    case "ollama":
//        chatClient = new ChatClient(
//            "llama3.2",
//            ollamaDummyCredential,
//            new OpenAIClientOptions { Endpoint = ollamaBaseUri }
//        ).AsIChatClient();

//        ollamaNeededForChat = true;
//        startupLogger.LogInformation("Chat: local Ollama (llama3.2).");
//        break;

//    case "groq":
//        var groqKey = Environment.GetEnvironmentVariable("GROQ_API_KEY")
//                      ?? builder.Configuration["GROQ_API_KEY"];

//        if (string.IsNullOrEmpty(groqKey))
//            startupLogger.LogWarning("GROQ_API_KEY not set! Chat calls will fail.");

//        chatClient = new ChatClient(
//            "llama-3.3-70b-versatile",
//            new ApiKeyCredential(groqKey ?? "dummy-groq-key"),
//            new OpenAIClientOptions { Endpoint = new Uri("https://api.groq.com/openai/v1") }
//        ).AsIChatClient();

//        startupLogger.LogInformation("Chat: Groq (llama-3.3-70b-versatile).");
//        break;

//    case "gemini":
      
//       //var geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
//       //                  ?? builder.Configuration["GEMINI_API_KEY"];

//        if (string.IsNullOrEmpty(geminiToken))
//            startupLogger.LogWarning("GEMINI_API_KEY not set!");

//        // Google Gemini ka OpenAI-compatible endpoint configure karein
      
//        var httpClient = new HttpClient(new LoggingHandler(new HttpClientHandler()));

//        var geminiChatOptions = new OpenAIClientOptions
//        {
//            Endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/openai"),
//            Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(httpClient)
//        };

//        chatClient = new OpenAIClient(
//        new ApiKeyCredential(geminiToken ?? "dummy-gemini-key"),
//        geminiChatOptions
//    // YAHAN CHANGE HAI: Sirf "gemini-1.5-flash" use karein, "models/" hata dein
//    ).GetChatClient("gemini-3.6-flash")
//     .AsIChatClient();

//        startupLogger.LogInformation("Chat: Google Gemini (gemini-3.6-flash).");
//        break;

//    default:
//        throw new InvalidOperationException(
//            $"Unknown AiProvider '{aiProvider}'. Valid values: Ollama, Groq, OpenAI.");
//}

// Purana (hatao):
// chatClient = new ChatClient(...).AsIChatClient();
// builder.Services.AddSingleton(chatClient); // IChatClient type se register ho raha tha

// Naya:
ChatClient nativeChatClient = aiProvider.ToLowerInvariant() switch
{
    "ollama" => new ChatClient(
        "llama3.2",
        ollamaDummyCredential,
        new OpenAIClientOptions { Endpoint = ollamaBaseUri }),

    "groq" => new ChatClient(
        "llama-3.3-70b-versatile",
        new ApiKeyCredential(Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "dummy-groq-key"),
        new OpenAIClientOptions { Endpoint = new Uri("https://api.groq.com/openai/v1") }),

    "gemini" => new ChatClient(
        "gemini-3.6-flash",
        new ApiKeyCredential(geminiToken ?? "dummy-gemini-key"),
        new OpenAIClientOptions { Endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/openai") }),

    _ => throw new InvalidOperationException($"Unknown AiProvider '{aiProvider}'.")
};

/*builder.Services.AddSingleton(nativeChatClient);*/ // ab ChatClient type se register hoga

// ============================================
// 4. Warmup service — sirf tab jab Ollama actually use ho raha ho
// (embedding ke liye ya chat ke liye, ya dono)
// ============================================
bool ollamaNeededForEmbedding = embeddingProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase);

if (ollamaNeededForEmbedding || ollamaNeededForChat)
{
    builder.Services.AddHostedService<OllamaWarmupService>();
}

// 5. Register Services
builder.Services.AddSingleton(nativeChatClient);
builder.Services.AddSingleton(embeddingGenerator);
builder.Services.AddSingleton<PdfService>();
builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();
app.UseCors("AllowAll");
app.MapControllers();
app.Run();