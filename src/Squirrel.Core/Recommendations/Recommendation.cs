using System;
using Squirrel.Core.Actions;

namespace Squirrel.Core.Recommendations
{
    public enum RecommendationCategory
    {
        CpuBottleneck,
        MemoryOptimization,
        StartupOptimization,
        SafeStorageCleanup,
        Responsiveness
    }

    public class Recommendation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = string.Empty;
        public RecommendationCategory Category { get; set; }
        public string Explanation { get; set; } = string.Empty;
        public string Evidence { get; set; } = string.Empty;
        public string ExpectedBenefit { get; set; } = string.Empty;
        public ActionRiskLevel RiskLevel { get; set; } = ActionRiskLevel.Low;
        public bool RequiresUserApproval { get; set; } = true;

        public ISystemAction? Action { get; set; }
        public bool IsActionAvailable => Action != null;

        public override string ToString()
        {
            return $"[{Category}] {Title} - Evidence: {Evidence}";
        }
    }
}
