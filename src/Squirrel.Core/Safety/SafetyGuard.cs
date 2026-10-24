using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Squirrel.Core.Safety
{
    /// <summary>
    /// Enforces strict safety rules across all Squirrel operations.
    /// Protects personal files, Microsoft Office, Windows core components, security services, and hardware drivers.
    /// </summary>
    public static class SafetyGuard
    {
        // Protected Microsoft Office executable and service names
        public static readonly HashSet<string> ProtectedOfficeEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "OfficeClickToRun.exe",
            "WINWORD.EXE",
            "EXCEL.EXE",
            "POWERPNT.EXE",
            "OUTLOOK.EXE",
            "ONENOTE.EXE",
            "MSOSYNC.EXE",
            "GROOVE.EXE",
            "MSACCESS.EXE",
            "MSPUB.EXE",
            "lync.exe",
            "teams.exe",
            "ClickToRunSvc",
            "OfficeSvc"
        };

        // Critical Windows OS executables and system processes that must never be terminated or modified
        public static readonly HashSet<string> ProtectedSystemEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System",
            "Registry",
            "smss.exe",
            "csrss.exe",
            "wininit.exe",
            "services.exe",
            "lsass.exe",
            "svchost.exe",
            "fontdrvhost.exe",
            "winlogon.exe",
            "dwm.exe",
            "explorer.exe",
            "spoolsv.exe",
            "taskhostw.exe",
            "sihost.exe",
            "ctfmon.exe",
            "RuntimeBroker.exe",
            "SearchIndexer.exe",
            "conhost.exe"
        };

        // Security protections: Windows Defender and Core Security Services
        public static readonly HashSet<string> ProtectedSecurityEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MsMpEng.exe",
            "NisSrv.exe",
            "SecurityHealthService.exe",
            "SecurityHealthSystray.exe",
            "smartscreen.exe",
            "Sense.exe",
            "WinDefend",
            "SecurityHealthService",
            "wscsvc"
        };

        // Hardware driver and essential input processes for AMD / Touchpad / Audio
        public static readonly HashSet<string> ProtectedHardwareEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RadeonSoftware.exe",
            "AMDRSServ.exe",
            "atiesrxx.exe",
            "atieclxx.exe",
            "amdacpusrsvc.exe",
            "RtkNGUI64.exe",
            "RAVBg64.exe",
            "SynTPEnh.exe",
            "SynTPHelper.exe",
            "ETDCtrl.exe"
        };

        /// <summary>
        /// Checks if an entity is protected from being terminated, disabled, or modified.
        /// </summary>
        public static bool IsProtectedEntity(string entityName, out string reason)
        {
            if (string.IsNullOrWhiteSpace(entityName))
            {
                reason = "Invalid or empty entity name.";
                return true;
            }

            var cleanName = Path.GetFileName(entityName).Trim();

            if (ProtectedOfficeEntities.Contains(cleanName) ||
                ProtectedOfficeEntities.Any(p => string.Equals(Path.GetFileNameWithoutExtension(cleanName), Path.GetFileNameWithoutExtension(p), StringComparison.OrdinalIgnoreCase)))
            {
                reason = "Microsoft Office component — required to remain fully functional.";
                return true;
            }

            if (ProtectedSecurityEntities.Contains(cleanName) ||
                ProtectedSecurityEntities.Any(p => string.Equals(Path.GetFileNameWithoutExtension(cleanName), Path.GetFileNameWithoutExtension(p), StringComparison.OrdinalIgnoreCase)))
            {
                reason = "Windows Security protection — disabling security is strictly prohibited.";
                return true;
            }

            if (ProtectedSystemEntities.Contains(cleanName) ||
                ProtectedSystemEntities.Any(p => string.Equals(Path.GetFileNameWithoutExtension(cleanName), Path.GetFileNameWithoutExtension(p), StringComparison.OrdinalIgnoreCase)))
            {
                reason = "Critical Windows operating system process — termination causes system instability or crash.";
                return true;
            }

            if (ProtectedHardwareEntities.Contains(cleanName) ||
                ProtectedHardwareEntities.Any(p => string.Equals(Path.GetFileNameWithoutExtension(cleanName), Path.GetFileNameWithoutExtension(p), StringComparison.OrdinalIgnoreCase)))
            {
                reason = "Essential hardware driver (AMD graphics, audio, or touchpad).";
                return true;
            }

            reason = string.Empty;
            return false;
        }

        /// <summary>
        /// Validates whether a file directory is permitted for safe cleanup.
        /// Strictly denies personal folders (Documents, Desktop, Downloads, Pictures, Music, OneDrive, etc.).
        /// </summary>
        public static bool IsPathSafeForCleanup(string path, out string rejectionReason)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                rejectionReason = "Path is null or empty.";
                return false;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                rejectionReason = $"Invalid path format: {ex.Message}";
                return false;
            }

            // Check against user profile root or root drive
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.Equals(fullPath, userProfile, StringComparison.OrdinalIgnoreCase))
            {
                rejectionReason = "Cannot target user profile root folder.";
                return false;
            }

            // Explicitly protect personal user folders
            var protectedPersonalFolders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Path.Combine(userProfile, "Downloads"),
                Path.Combine(userProfile, "OneDrive"),
                Path.Combine(userProfile, "Dropbox"),
                Path.Combine(userProfile, "Google Drive")
            };

            foreach (var personalFolder in protectedPersonalFolders)
            {
                if (!string.IsNullOrWhiteSpace(personalFolder))
                {
                    var cleanPersonal = Path.GetFullPath(personalFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (string.Equals(fullPath, cleanPersonal, StringComparison.OrdinalIgnoreCase) ||
                        fullPath.StartsWith(cleanPersonal + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        rejectionReason = $"Path resides inside personal user folder ({Path.GetFileName(cleanPersonal)}), which is strictly protected.";
                        return false;
                    }
                }
            }

            // Windows System Root protection (e.g. C:\Windows or C:\Windows\System32)
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(winDir))
            {
                var cleanWinDir = Path.GetFullPath(winDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(fullPath, cleanWinDir, StringComparison.OrdinalIgnoreCase))
                {
                    rejectionReason = "Cannot target Windows directory root.";
                    return false;
                }

                var system32 = Path.Combine(cleanWinDir, "System32");
                if (fullPath.StartsWith(system32, StringComparison.OrdinalIgnoreCase))
                {
                    rejectionReason = "Windows System32 directory is strictly protected.";
                    return false;
                }
            }

            rejectionReason = string.Empty;
            return true;
        }
    }
}
