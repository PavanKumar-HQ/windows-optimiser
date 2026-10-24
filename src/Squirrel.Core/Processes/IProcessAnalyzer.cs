using System.Collections.Generic;
using System.Threading.Tasks;

namespace Squirrel.Core.Processes
{
    public interface IProcessAnalyzer
    {
        IReadOnlyList<ProcessInfo> GetRunningProcesses();
        Task<IReadOnlyList<ProcessInfo>> GetRunningProcessesAsync();
    }
}
