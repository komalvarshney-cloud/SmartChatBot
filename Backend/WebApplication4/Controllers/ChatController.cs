using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using OpenAI.Chat;
using Pgvector;
using System.Diagnostics;
using System.Text;
using WebApplication4.Data;


[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ChatClient _chatClient; // <-- IChatClient ki jagah native ChatClient
    private readonly ILogger<ChatController> _logger;
    public record ChatRequest(string Message);
    private static readonly MemoryCache _embeddingCache = new(new MemoryCacheOptions
    {
        SizeLimit = 500
    });

    private static readonly HashSet<string> _greetings = new()
    {
        "hi", "hello", "hey", "hii", "hiii", "hlo",
        "good morning", "good afternoon", "good evening", "namaste"
    };

    private static readonly HashSet<string> _courtesies = new()
    {
        "thanks", "thank you", "ok", "okay", "bye", "goodbye"
    };

    public ChatController(
        AppDbContext db,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ChatClient chatClient, // <-- ab native ChatClient inject hoga
        ILogger<ChatController> logger)
    {
        _db = db;
        _embeddingGenerator = embeddingGenerator;
        _chatClient = chatClient;
        _logger = logger;
    }

    [HttpPost("stream")]
    public async Task StreamChat([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Message))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("Message cannot be empty.", cancellationToken);
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        // ---- 0. GREETING / SMALL-TALK SHORTCUT ----
        var trimmedMsg = request.Message.Trim().ToLowerInvariant().TrimEnd('!', '.', '?');

        if (_greetings.Contains(trimmedMsg))
        {
            var reply = "Hello! How can I help you today?";
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(reply), cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            return;
        }

        if (_courtesies.Contains(trimmedMsg))
        {
            var reply = trimmedMsg.Contains("bye")
                ? "Goodbye! Have a great day."
                : "You're welcome! Let me know if you need anything else.";
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(reply), cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            return;
        }

        var sw = Stopwatch.StartNew();
        var cacheKey = $"emb:{trimmedMsg}";

        try
        {
            // ---- 1. EMBEDDING (with cache) ----
            Vector queryVector;

            if (_embeddingCache.TryGetValue(cacheKey, out float[]? cachedVec) && cachedVec is not null)
            {
                queryVector = new Vector(cachedVec);
            }
            else
            {
                var embeddings = await _embeddingGenerator.GenerateAsync(
                    new[] { request.Message }, cancellationToken: cancellationToken);

                var vecArray = embeddings[0].Vector.ToArray();
                queryVector = new Vector(vecArray);

                _embeddingCache.Set(cacheKey, vecArray, new MemoryCacheEntryOptions
                {
                    Size = 1,
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6)
                });
            }

            _logger.LogInformation("Embedding done in {ms} ms", sw.ElapsedMilliseconds);
            sw.Restart();

            // ---- 2. DB FETCH ----
            var matches = await _db.Documents
                .FromSqlInterpolated($"SELECT * FROM match_documents({queryVector}, 0.1, 3)")
                .AsNoTracking()
                .Select(d => d.Content)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("DB fetch done in {ms} ms", sw.ElapsedMilliseconds);
            sw.Restart();

            // ---- 3. NO CONTEXT CASE ----
            if (matches.Count == 0)
            {
                var noContextMsg = "Maaf kijiye, is sawaal ka jawab mere paas available documents mein nahi hai.";
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(noContextMsg), cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                return;
            }

            var context = string.Join("\n\n", matches);

            var systemPrompt = $@"You are a STRICT and professional business assistant. Your primary directive is to answer questions based EXCLUSIVELY on the provided context.

CRITICAL RULES YOU MUST FOLLOW:
1. GROUNDING: Use ONLY the information explicitly stated in the CONTEXT block below.
2. NO HALLUCINATION: Do NOT add facts, numbers, assumptions, or external knowledge. Do not invent stories or make guesses.
3. STRICT FALLBACK: If the CONTEXT does not contain the answer to the user's question, you must reply EXACTLY with: ""I am sorry, but I do not have that information.""
4. GREETINGS & SMALL TALK: If the user's message is only a greeting or simple courtesy, respond naturally and briefly. Do NOT apply the strict fallback rule to greetings.
5. LANGUAGE: Always respond in clear, professional, and grammatically correct English. Always reply in Correct English.
6. CONCISENESS: Keep your answers direct, short, and to the point.

CONTEXT:
{context}

Remember: Do not say anything outside of the CONTEXT, except for greetings and courtesy replies as per Rule 4.";

            // ---- 4. NATIVE OpenAI SDK message format ----
            var messages = new List<OpenAI.Chat.ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(request.Message)
            };

            var chatOptions = new ChatCompletionOptions
            {
                Temperature = 0.2f
            };

            // ---- 5. NATIVE STREAMING — direct SDK method, no IChatClient wrapper ----
            var buffer = new StringBuilder();
            const int flushThreshold = 20;

            await foreach (var update in _chatClient.CompleteChatStreamingAsync(messages, chatOptions, cancellationToken))
            {
                foreach (var part in update.ContentUpdate)
                {
                    if (string.IsNullOrEmpty(part.Text)) continue;

                    buffer.Append(part.Text);

                    if (buffer.Length >= flushThreshold)
                    {
                        var bytes = Encoding.UTF8.GetBytes(buffer.ToString());
                        await Response.Body.WriteAsync(bytes, cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);
                        buffer.Clear();
                    }
                }
            }

            if (buffer.Length > 0)
            {
                var bytes = Encoding.UTF8.GetBytes(buffer.ToString());
                await Response.Body.WriteAsync(bytes, cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            _logger.LogInformation("LLM streaming done in {ms} ms", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            // Client disconnect
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while streaming chat response");

            if (!Response.HasStarted)
                Response.StatusCode = StatusCodes.Status500InternalServerError;

            var errorBytes = Encoding.UTF8.GetBytes("\n[Error: Server busy, please try again.]");
            await Response.Body.WriteAsync(errorBytes, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}