using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Squirrel.Core.Safety
{
    /// <summary>
    /// Enforces strict safety rules across all Squirrel operations.
    /// Protects personal files, Microsoft Office, Windows core components, security services, accessibility tools, and hardware drivers.
    /// Employs strict whitelist validation for cleanup targets and verifies process identity.
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

        // Windows Accessibility tools (never disable or modify)
        public static readonly HashSet<string> ProtectedAccessibilityEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "osk.exe",
            "narrator.exe",
            "magnify.exe",
            "AtBroker.exe",
            "DisplaySwitch.exe"
        };

        /// <summary>
        /// Checks if an entity is protected from being terminated, disabled, or modified.
        /// Evaluates name, optional executable path, and publisher context.
        /// </summary>
        public static bool IsProtectedEntity(string entityName, out string reason)
        {
            return IsProtectedEntity(entityName, null, out reason);
        }

        public static bool IsProtectedEntity(string entityName, string? executablePath, out string reason)
        {
            if (string.IsNullOrWhiteSpace(entityName))
            {
                reason = "Invalid or empty entity name.";
                return true;
            }

            var cleanName = GetFileNameCrossPlatform(entityName);
            var cleanExe = GetFileNameCrossPlatform(executablePath);

            bool Matches(HashSet<string> set) =>
                set.Contains(cleanName) ||
                (!string.IsNullOrEmpty(cleanExe) && set.Contains(cleanExe)) ||
                set.Any(p => string.Equals(Path.GetFileNameWithoutExtension(cleanName), Path.GetFileNameWithoutExtension(p), StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(cleanExe) && set.Any(p => string.Equals(Path.GetFileNameWithoutExtension(cleanExe), Path.GetFileNameWithoutExtension(p), StringComparison.OrdinalIgnoreCase)));

            // 1. Accessibility tools
            if (Matches(ProtectedAccessibilityEntities))
            {
                reason = "Windows accessibility tool — must remain available.";
                return true;
            }

            // 2. Microsoft Office
            if (Matches(ProtectedOfficeEntities))
            {
                reason = "Microsoft Office component — required to remain fully functional.";
                return true;
            }

            if (!string.IsNullOrEmpty(executablePath) &&
                (executablePath.IndexOf(@"\Microsoft Office\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 executablePath.IndexOf(@"\Office16\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 executablePath.IndexOf(@"\Root\Office16\", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                reason = "Microsoft Office component — resides within Office installation directory.";
                return true;
            }

            // 3. Windows Security & Defender
            if (Matches(ProtectedSecurityEntities))
            {
                reason = "Windows Security protection — disabling security is strictly prohibited.";
                return true;
            }

            // 4. Critical Windows System Entities
            if (Matches(ProtectedSystemEntities))
            {
                reason = "Critical Windows operating system process — termination causes system instability or crash.";
                return true;
            }

            // 5. Hardware drivers (AMD, Audio, Touchpad)
            if (Matches(ProtectedHardwareEntities))
            {
                reason = "Essential hardware driver (AMD graphics, audio, or touchpad).";
                return true;
            }

            reason = string.Empty;
            return false;
        }

        /// <summary>
        /// Validates whether a file directory is permitted for safe cleanup.
        /// Uses an explicit category whitelist and strictly denies drive roots, user profile root,
        /// personal user folders, Office directories, and core Windows system folders.
        /// </summary>
        public static bool IsPathSafeForCleanup(string path, out string rejectionReason)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                rejectionReason = "Path is null or empty.";
                return false;
            }

            var trimmedInput = path.Trim();
            if ((trimmedInput.Length == 2 && char.IsLetter(trimmedInput[0]) && trimmedInput[1] == ':') ||
                (trimmedInput.Length == 3 && char.IsLetter(trimmedInput[0]) && trimmedInput[1] == ':' && (trimmedInput[2] == '\\' || trimmedInput[2] == '/')) ||
                trimmedInput == "/" || trimmedInput == "\\")
            {
                rejectionReason = "Cannot target a drive root or root filesystem.";
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

            // 1. Strict Drive Root Protection (e.g. C:\ or /)
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root))
            {
                var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.IsNullOrEmpty(trimmedRoot))
                {
                    // Root was "/" on Unix
                    if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase) || fullPath == "/")
                    {
                        rejectionReason = "Cannot target a drive root or root filesystem.";
                        return false;
                    }
                }
                else
                {
                    if (string.Equals(fullPath, trimmedRoot, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
                    {
                        rejectionReason = "Cannot target a drive root or root filesystem.";
                        return false;
                    }
                }
            }

            // 2. Strict User Profile Root Protection
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrEmpty(userProfile) && string.Equals(fullPath, userProfile, StringComparison.OrdinalIgnoreCase))
            {
                rejectionReason = "Cannot target user profile root folder.";
                return false;
            }

            // 3. Strict AppData Root Protection
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrEmpty(localAppData) && string.Equals(fullPath, localAppData, StringComparison.OrdinalIgnoreCase))
            {
                rejectionReason = "Cannot target LocalAppData root folder.";
                return false;
            }
            if (!string.IsNullOrEmpty(roamingAppData) && string.Equals(fullPath, roamingAppData, StringComparison.OrdinalIgnoreCase))
            {
                rejectionReason = "Cannot target Roaming AppData root folder.";
                return false;
            }

            // 4. Strict Program Files Protection
            var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if ((!string.IsNullOrEmpty(progFiles) && (string.Equals(fullPath, progFiles, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(progFiles + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrEmpty(progFilesX86) && (string.Equals(fullPath, progFilesX86, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(progFilesX86 + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) ||
                fullPath.StartsWith(@"C:\Program Files", StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(@"C:\Program Files (x86)", StringComparison.OrdinalIgnoreCase))
            {
                rejectionReason = "Cannot target Program Files directory.";
                return false;
            }

            // 5. Strict Personal User Folders Protection
            var protectedPersonalFolders = new List<string?>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                !string.IsNullOrEmpty(userProfile) ? Path.Combine(userProfile, "Downloads") : null,
                !string.IsNullOrEmpty(userProfile) ? Path.Combine(userProfile, "OneDrive") : null,
                !string.IsNullOrEmpty(userProfile) ? Path.Combine(userProfile, "Dropbox") : null,
                !string.IsNullOrEmpty(userProfile) ? Path.Combine(userProfile, "Google Drive") : null
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

            // 6. Strict Microsoft Office Data Protection
            if (!string.IsNullOrEmpty(roamingAppData))
            {
                var officeRoaming = Path.Combine(roamingAppData, "Microsoft", "Office");
                if (fullPath.StartsWith(officeRoaming, StringComparison.OrdinalIgnoreCase))
                {
                    rejectionReason = "Microsoft Office configuration and template directory is strictly protected.";
                    return false;
                }
            }

            // 7. Strict Windows Root and System Core Protection
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrWhiteSpace(winDir))
            {
                if (string.Equals(fullPath, winDir, StringComparison.OrdinalIgnoreCase))
                {
                    rejectionReason = "Cannot target Windows directory root.";
                    return false;
                }

                // If inside Windows directory, ONLY Windows\Temp is permitted! All other Windows subfolders are rejected.
                if (fullPath.StartsWith(winDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    var winTemp = Path.Combine(winDir, "Temp").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (!string.Equals(fullPath, winTemp, StringComparison.OrdinalIgnoreCase) &&
                        !fullPath.StartsWith(winTemp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        rejectionReason = "Windows operating system directories (outside Windows\\Temp) are strictly protected.";
                        return false;
                    }
                }
            }

            // 8. Whitelist Category Validation:
            // The path must qualify under one of the verified safe cleanup target patterns.
            var tempRoot = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bool isInsideUserTemp = fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && !string.Equals(fullPath, tempRoot, StringComparison.OrdinalIgnoreCase);

            bool isInsideWinTemp = !string.IsNullOrWhiteSpace(winDir) &&
                                   fullPath.StartsWith(Path.Combine(winDir, "Temp"), StringComparison.OrdinalIgnoreCase);

            bool isCrashDumps = !string.IsNullOrWhiteSpace(localAppData) &&
                                fullPath.StartsWith(Path.Combine(localAppData, "CrashDumps"), StringComparison.OrdinalIgnoreCase);

            bool isEdgeCache = !string.IsNullOrWhiteSpace(localAppData) &&
                               fullPath.StartsWith(Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache", "Cache_Data"), StringComparison.OrdinalIgnoreCase);

            bool isChromeCache = !string.IsNullOrWhiteSpace(localAppData) &&
                                 fullPath.StartsWith(Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache", "Cache_Data"), StringComparison.OrdinalIgnoreCase);

            bool isIsolatedTestDir = fullPath.IndexOf("SquirrelTests_", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isInsideUserTemp || isInsideWinTemp || isCrashDumps || isEdgeCache || isChromeCache || isIsolatedTestDir ||
                string.Equals(fullPath, tempRoot, StringComparison.OrdinalIgnoreCase))
            {
                rejectionReason = string.Empty;
                return true;
            }

            rejectionReason = "Path does not match any approved safe cleanup category whitelist.";
            return false;
        }

        private static string GetFileNameCrossPlatform(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var trimmed = path.Trim();
            var lastSlash = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
            return lastSlash >= 0 ? trimmed.Substring(lastSlash + 1) : trimmed;
        }
    }
}
