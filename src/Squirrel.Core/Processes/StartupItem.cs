using System;

namespace Squirrel.Core.Processes
{
    public enum StartupLocationType
    {
        RegistryCurrentUser,
        RegistryLocalMachine,
        UserStartupFolder,
        CommonStartupFolder,
        ScheduledTask
    }

    public enum StartupImpact
    {
        Unknown,
        Low,
        Medium,
        High
    }

    public class StartupItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string CommandLine { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public StartupLocationType LocationType { get; set; }
        public string LocationPath { get; set; } = string.Empty;
        public string SubKeyPath { get; set; } = string.Empty;
        public int ValueKind { get; set; } = 1; // 1 = RegistryValueKind.String (REG_SZ), 2 = REG_EXPAND_SZ

        public bool IsEnabled { get; set; } = true;
        public StartupImpact Impact { get; set; } = StartupImpact.Unknown;
        public string Category { get; set; } = "General";

        public bool CanSafelyDisable { get; set; } = true;
        public string SafetyExplanation { get; set; } = string.Empty;

        public bool IsRunOnce => LocationPath.IndexOf("RunOnce", StringComparison.OrdinalIgnoreCase) >= 0;

        public string ActionText => IsEnabled ? "Disable" : "Enable";
        public string StatusText => IsEnabled ? "ENABLED" : "DISABLED";

        public override string ToString()
        {
            return $"{Name} ({(IsEnabled ? "Enabled" : "Disabled")}) - {LocationType}: {CommandLine}";
        }
    }
}
