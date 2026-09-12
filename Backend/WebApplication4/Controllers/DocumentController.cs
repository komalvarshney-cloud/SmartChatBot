using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Pgvector;
using WebApplication4.Data;
using WebApplication4.Services;

namespace WebApplication4.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly PdfService _pdfService;
    private readonly ILogger<DocumentController> _logger;
    public DocumentController(
        AppDbContext db,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        PdfService pdfService,
        ILogger<DocumentController> logger)
    {
        _db = db;
        _embeddingGenerator = embeddingGenerator;
        _pdfService = pdfService;
        _logger = logger;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadPdf(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest("PDF file is required.");

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only PDF files are supported.");

        var tempPath = Path.GetTempFileName();
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            var fullText = _pdfService.ExtractText(tempPath);
            var chunks = _pdfService.ChunkText(fullText);

            if (chunks.Count == 0)
                return BadRequest("No extractable text found in PDF.");

            int savedCount = 0;
            foreach (var chunk in chunks)
            {
                var embeddingResult = await _embeddingGenerator.GenerateAsync(
                    new[] { chunk }, cancellationToken: cancellationToken);

                var vector = new Vector(embeddingResult[0].Vector.ToArray());

                _db.Documents.Add(new Document
                {
                    Content = chunk,
                    Embedding = vector
                });

                savedCount++;

                // Rate limit se bachne ke liye chhota delay
                await Task.Delay(150, cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new { message = "PDF processed successfully", chunksSaved = savedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PDF upload");
            return StatusCode(500, "Failed to process PDF.");
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
                System.IO.File.Delete(tempPath);
        }
    }
}