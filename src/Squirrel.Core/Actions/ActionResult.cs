using System;

namespace Squirrel.Core.Actions
{
    public enum ActionRiskLevel
    {
        Safe,
        Low,
        Moderate,
        High
    }

    public class ActionResult
    {
        public bool Succeeded { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ErrorDetails { get; set; } = string.Empty;
        public DateTime ExecutedAtUtc { get; set; } = DateTime.UtcNow;

        public static ActionResult Success(string message) => new ActionResult { Succeeded = true, Message = message };
        public static ActionResult Failure(string message, string errorDetails = "") => new ActionResult { Succeeded = false, Message = message, ErrorDetails = errorDetails };
    }
}
