using System;

namespace Squirrel.Core.Storage
{
    public class CleanupTarget
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;

        public int FileCount { get; set; }
        public long SizeBytes { get; set; }
        public double SizeMb => SizeBytes / (1024.0 * 1024.0);
        public double SizeGb => SizeBytes / (1024.0 * 1024.0 * 1024.0);

        /// <summary>
        /// Explicit review-first workflow: Defaults to false so the user must explicitly opt in.
        /// </summary>
        public bool IsSelectedForCleanup { get; set; } = false;

        public bool IsSafeForCleanup { get; set; } = true;
        public string SafetyNotes { get; set; } = string.Empty;
        public bool RequiresAdmin { get; set; } = false;

        public override string ToString()
        {
            return $"{Name}: {FileCount} items ({SizeMb:F1} MB) - Path: {DirectoryPath}";
        }
    }
}
