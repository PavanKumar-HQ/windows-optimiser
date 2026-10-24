using System.Collections.Generic;
using System.Threading.Tasks;

namespace Squirrel.Core.Audit
{
    public interface IAuditLedger
    {
        Task RecordEntryAsync(AuditEntry entry);
        Task<IReadOnlyList<AuditEntry>> GetEntriesAsync();
        Task<bool> MarkRevertedAsync(string entryId);
    }
}
