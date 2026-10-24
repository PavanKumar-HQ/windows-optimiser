using System;
using System.Threading.Tasks;

namespace Squirrel.Core.Diagnostics
{
    public interface ISystemMonitor : IDisposable
    {
        /// <summary>
        /// Captures a live snapshot of system resource usage and Squirrel's own resource footprint.
        /// </summary>
        SystemMetrics GetSnapshot();

        /// <summary>
        /// Asynchronously captures a live snapshot.
        /// </summary>
        Task<SystemMetrics> GetSnapshotAsync();
    }
}
