using System;

namespace Squirrel.Core.Audit
{
    public class AuditEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string ActionType { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
        public string TargetIdentifier { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public string PreviousState { get; set; } = string.Empty;
        public string NewState { get; set; } = string.Empty;

        public bool IsReversible { get; set; }
        public string RollbackDataJson { get; set; } = string.Empty;
        public string Status { get; set; } = "Completed";

        public override string ToString()
        {
            return $"[{TimestampUtc:yyyy-MM-dd HH:mm:ss}] {ActionType} on '{TargetName}': {Description} ({Status})";
        }
    }
}
