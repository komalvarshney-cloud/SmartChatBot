using UglyToad.PdfPig;

namespace WebApplication4.Services;

public class PdfService
{
    public string ExtractText(string filePath)
    {
        var text = new System.Text.StringBuilder();
        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
        {
            text.AppendLine(page.Text);
        }
        return text.ToString();
    }

    public List<string> ChunkText(string text, int chunkSize = 500, int overlap = 50)
    {
        var chunks = new List<string>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0) return chunks;

        for (int i = 0; i < words.Length; i += (chunkSize - overlap))
        {
            var chunk = string.Join(' ', words.Skip(i).Take(chunkSize));
            if (!string.IsNullOrWhiteSpace(chunk))
                chunks.Add(chunk);

            if (i + chunkSize >= words.Length) break;
        }
        return chunks;
    }
}