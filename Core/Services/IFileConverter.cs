using RadialConvert.Core.Models;

namespace RadialConvert.Core.Services;

public interface IFileConverter
{
    bool CanHandle(string filePath);
    IReadOnlyList<ConversionOption> GetOptions(string filePath);
    Task<string> ConvertAsync(string sourcePath, string targetExtension, CancellationToken cancellationToken = default);
}
