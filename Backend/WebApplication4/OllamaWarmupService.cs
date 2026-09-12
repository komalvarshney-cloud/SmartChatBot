using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using OpenAI.Chat;

namespace WebApplication4.Services
{
    public class OllamaWarmupService : IHostedService
    {
        private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
        private readonly ChatClient _chatClient; // <-- IChatClient ki jagah native ChatClient
        private readonly IConfiguration _configuration;
        private readonly IHostApplicationLifetime _lifetime;

        public OllamaWarmupService(
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
            ChatClient chatClient, // <-- ab native ChatClient inject hoga
            IConfiguration configuration,
            IHostApplicationLifetime lifetime)
        {
            _embeddingGenerator = embeddingGenerator;
            _chatClient = chatClient;
            _configuration = configuration;
            _lifetime = lifetime;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _lifetime.ApplicationStarted.Register(() =>
            {
                _ = Task.Run(async () =>
                {
                    var embeddingProvider = _configuration["EmbeddingProvider"] ?? "Ollama";
                    var chatProvider = _configuration["AiProvider"] ?? "Ollama";

                    // Embedding warmup — sirf agar Ollama embedding use ho raha hai
                    if (embeddingProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            Console.WriteLine("[INFO] Warming up Ollama embedding model...");
                            await _embeddingGenerator.GenerateAsync(new[] { "warmup" }, cancellationToken: cancellationToken);
                            Console.WriteLine("[SUCCESS] Embedding model warmed up.");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERROR] Embedding warm-up failed. Is Ollama running? {ex.Message}");
                        }
                    }

                    // Chat warmup — sirf agar Ollama chat use ho raha hai
                    if (chatProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            Console.WriteLine("[INFO] Warming up Ollama chat model...");

                            var chatMessages = new List<OpenAI.Chat.ChatMessage>
                            {
                                new UserChatMessage("Hello")
                            };

                            var warmupOptions = new ChatCompletionOptions
                            {
                                MaxOutputTokenCount = 1
                            };

                            await _chatClient.CompleteChatAsync(chatMessages, warmupOptions, cancellationToken);

                            Console.WriteLine("[SUCCESS] Chat model warmed up.");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERROR] Chat warm-up failed. {ex.Message}");
                        }
                    }
                });
            });

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}