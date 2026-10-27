using System.Text.Json;
using Lip.Core.Entities;

namespace Lip.Core.Mcmod;

public class ArchiveReader(IDocumentParser documentParser) : IArchiveReader {
  private readonly IDocumentParser _documentParser = documentParser;

  public async Task<ArchiveDocument> Read(ArchiveHandle archive) {
    HashSet<string> keys = [.. archive.Source.Keys];

    try {
      if (keys.Contains(ManifestFormat.ToothFileName)) {
        byte[] bytes = await ReadAll(archive, ManifestFormat.ToothFileName);
        PackageManifest manifest = await PackageManifest.FromStream(new MemoryStream(bytes));
        return new ToothArchiveDocument(manifest, JsonDocument.Parse(bytes).RootElement.Clone());
      }

      if (keys.Contains(ManifestFormat.ManifestFileName)) {
        byte[] bytes = await ReadAll(archive, ManifestFormat.ManifestFileName);
        return _documentParser.Parse(bytes) switch {
          RoutingDocument routing => new RoutingArchiveDocument(routing),
          ConcreteDocument concrete => new ConcreteArchiveDocument(
              concrete, JsonDocument.Parse(bytes).RootElement.Clone()),
          _ => throw new InvalidOperationException("Unknown document type."),
        };
      }
    }
    catch (Exception ex) when (ex is not ArchiveUnavailableException and not NotImplementedException) {
      throw new ArchiveUnavailableException($"Invalid root document in {archive.Key}: {ex.Message}", ex);
    }

    throw new ArchiveUnavailableException(
        $"{archive.Key} has neither {ManifestFormat.ToothFileName} nor {ManifestFormat.ManifestFileName}.");
  }

  private static async Task<byte[]> ReadAll(ArchiveHandle archive, string key) {
    using Stream stream = await archive.Source.OpenRead(key);
    using MemoryStream memory = new();
    await stream.CopyToAsync(memory);
    return memory.ToArray();
  }
}
