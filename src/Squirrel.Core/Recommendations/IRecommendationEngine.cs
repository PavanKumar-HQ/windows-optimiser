using System.Collections.Generic;
using Squirrel.Core.Diagnostics;
using Squirrel.Core.Processes;
using Squirrel.Core.Storage;

namespace Squirrel.Core.Recommendations
{
    public interface IRecommendationEngine
    {
        IReadOnlyList<Recommendation> Evaluate(
            SystemMetrics currentMetrics,
            BottleneckReport? bottleneckReport,
            IReadOnlyList<StartupItem> startupItems,
            StorageReport? storageReport);
    }
}
