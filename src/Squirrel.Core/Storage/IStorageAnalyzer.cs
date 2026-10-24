using System;
using System.Threading;
using System.Threading.Tasks;

namespace Squirrel.Core.Storage
{
    public interface IStorageAnalyzer
    {
        StorageReport AnalyzeStorage(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
        Task<StorageReport> AnalyzeStorageAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    }
}
