using System.Collections.Generic;
using System.Threading.Tasks;

namespace Squirrel.Core.Processes
{
    public interface IStartupAnalyzer
    {
        IReadOnlyList<StartupItem> GetStartupItems();
        Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync();
    }
}
