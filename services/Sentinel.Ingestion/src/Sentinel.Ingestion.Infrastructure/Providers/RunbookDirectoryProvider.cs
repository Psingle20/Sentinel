using Microsoft.Extensions.Configuration;
using Sentinel.Ingestion.Application.Knowledge;

namespace Sentinel.Ingestion.Infrastructure.Providers;

/// <summary>
/// Runbooks as Markdown files in a directory (<c>Ingestion:Providers:Runbooks:Directory</c>). The
/// first heading is the title; a <c>service:</c> line in the front matter names the owning service.
/// </summary>
internal sealed class RunbookDirectoryProvider(IConfiguration configuration) : IKnowledgeProvider
{
    public const string ProviderName = "runbooks";

    public string Name => ProviderName;

    public async Task<IReadOnlyList<KnowledgeEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        string? directory = configuration["Ingestion:Providers:Runbooks:Directory"];
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        var entries = new List<KnowledgeEntry>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.md").Order(StringComparer.Ordinal))
        {
            string text = (await File.ReadAllTextAsync(path, cancellationToken)).Replace("\r\n", "\n", StringComparison.Ordinal);
            string? service = null;
            if (text.StartsWith("---\n", StringComparison.Ordinal) && text.IndexOf("\n---\n", 4, StringComparison.Ordinal) is var end and > 0)
            {
                service = text[4..end].Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.StartsWith("service:", StringComparison.Ordinal))
                    .Select(line => line["service:".Length..].Trim())
                    .FirstOrDefault();
                text = text[(end + 5)..];
            }

            string title = text.Split('\n').FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim() ?? Path.GetFileNameWithoutExtension(path);
            entries.Add(new KnowledgeEntry(Path.GetFileNameWithoutExtension(path), title, service, text, $"runbooks/{Path.GetFileName(path)}", File.GetLastWriteTimeUtc(path)));
        }

        return entries;
    }
}
