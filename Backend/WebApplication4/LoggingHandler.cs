namespace WebApplication4
{
    public class LoggingHandler : DelegatingHandler
    {
        public LoggingHandler(HttpMessageHandler inner) : base(inner) { }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[GEMINI REQUEST] {request.Method} {request.RequestUri}");
            var response = await base.SendAsync(request, cancellationToken);
            Console.WriteLine($"[GEMINI RESPONSE] {(int)response.StatusCode} {response.StatusCode}");
            return response;
        }
    }
}
