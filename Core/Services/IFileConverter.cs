using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RadialConvert.Core.Models;

namespace RadialConvert.Core.Services;

public interface IFileConverter
{
    bool CanHandle(string filePath);
    bool CanHandleAll(IReadOnlyList<string> filePaths);
    IReadOnlyList<ConversionOption> GetOptions(IReadOnlyList<string> filePaths);
    Task<string> ConvertAsync(string sourcePath, string targetExtension, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ConvertBatchAsync(IReadOnlyList<string> sourcePaths, string targetOptionId, CancellationToken cancellationToken = default);
}
