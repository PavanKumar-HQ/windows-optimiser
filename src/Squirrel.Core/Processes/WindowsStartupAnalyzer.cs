using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using Squirrel.Core.Safety;

namespace Squirrel.Core.Processes
{
    public class WindowsStartupAnalyzer : IStartupAnalyzer
    {
        public IReadOnlyList<StartupItem> GetStartupItems()
        {
            var items = new List<StartupItem>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // 1. Current User Registry Run keys
                ScanRegistryKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", StartupLocationType.RegistryCurrentUser, items);
                ScanRegistryKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", StartupLocationType.RegistryCurrentUser, items);

                // 2. Local Machine Registry Run keys (64-bit and 32-bit wow64)
                ScanRegistryKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", StartupLocationType.RegistryLocalMachine, items);
                ScanRegistryKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", StartupLocationType.RegistryLocalMachine, items);
                ScanRegistryKey(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", StartupLocationType.RegistryLocalMachine, items);

                // 3. User Startup Folder
                var userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                ScanFolder(userStartup, StartupLocationType.UserStartupFolder, items);

                // 4. Common (All Users) Startup Folder
                var commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
                ScanFolder(commonStartup, StartupLocationType.CommonStartupFolder, items);
            }

            return items;
        }

        public Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync()
        {
            return Task.Run(() => GetStartupItems());
        }

        private void ScanRegistryKey(RegistryKey rootKey, string subKeyPath, StartupLocationType locationType, List<StartupItem> items)
        {
            try
            {
                using var key = rootKey.OpenSubKey(subKeyPath, false);
                if (key == null) return;

                var valueNames = key.GetValueNames();
                foreach (var valueName in valueNames)
                {
                    try
                    {
                        var command = key.GetValue(valueName)?.ToString() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(command)) continue;

                        var item = CreateStartupItem(valueName, command, locationType, $@"{rootKey.Name}\{subKeyPath}");
                        items.Add(item);
                    }
                    catch
                    {
                        // Ignore individual value read errors
                    }
                }
            }
            catch
            {
                // Access denied or key not found
            }
        }

        private void ScanFolder(string folderPath, StartupLocationType locationType, List<StartupItem> items)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

                var files = Directory.GetFiles(folderPath);
                foreach (var file in files)
                {
                    try
                    {
                        var fileName = Path.GetFileName(file);
                        if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                        var item = CreateStartupItem(Path.GetFileNameWithoutExtension(fileName), file, locationType, folderPath);
                        items.Add(item);
                    }
                    catch
                    {
                        // Ignore individual file errors
                    }
                }
            }
            catch
            {
                // Access denied or directory error
            }
        }

        private StartupItem CreateStartupItem(string name, string commandLine, StartupLocationType locationType, string locationPath)
        {
            var item = new StartupItem
            {
                Name = name,
                CommandLine = commandLine,
                LocationType = locationType,
                LocationPath = locationPath,
                IsEnabled = true
            };

            var exePath = ExtractExecutablePath(commandLine);
            item.ExecutablePath = exePath;

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                try
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                    item.Publisher = versionInfo.CompanyName ?? string.Empty;
                }
                catch { }
            }

            // Categorize and assess impact & safety
            AssessSafetyAndImpact(item);

            return item;
        }

        private void AssessSafetyAndImpact(StartupItem item)
        {
            var combined = (item.Name + " " + item.CommandLine + " " + item.Publisher).ToLowerInvariant();

            // 1. Office Check - strictly protected
            if (SafetyGuard.ProtectedOfficeEntities.Any(p => combined.Contains(p.ToLowerInvariant())) ||
                combined.Contains("officeclicktorun") || combined.Contains("onenotem") || combined.Contains("groove.exe"))
            {
                item.Category = "Microsoft Office";
                item.CanSafelyDisable = false;
                item.SafetyExplanation = "Required for Microsoft Office functionality and background synchronization.";
                item.Impact = StartupImpact.Medium;
                return;
            }

            // 2. Hardware Drivers (AMD Radeon, Realtek Audio, Touchpad)
            if (combined.Contains("amd") || combined.Contains("radeon") || combined.Contains("realtek") ||
                combined.Contains("rtkngui") || combined.Contains("syntp") || combined.Contains("etdctrl"))
            {
                item.Category = "Hardware Driver";
                item.CanSafelyDisable = false;
                item.SafetyExplanation = "Essential hardware driver component for display, audio, or touchpad.";
                item.Impact = StartupImpact.Low;
                return;
            }

            // 3. Windows Security / Defender
            if (combined.Contains("msmpeng") || combined.Contains("securityhealth") || combined.Contains("smartscreen"))
            {
                item.Category = "Windows Security";
                item.CanSafelyDisable = false;
                item.SafetyExplanation = "Windows Core Security protection — must not be disabled.";
                item.Impact = StartupImpact.Low;
                return;
            }

            // 4. Cloud Storage
            if (combined.Contains("onedrive") || combined.Contains("dropbox") || combined.Contains("googledrive") || combined.Contains("box sync"))
            {
                item.Category = "Cloud Storage";
                item.CanSafelyDisable = true;
                item.SafetyExplanation = "Disabling stops background cloud syncing on boot, saving RAM and CPU. Can be launched manually when needed.";
                item.Impact = StartupImpact.High;
                return;
            }

            // 5. Communication & Heavy Apps
            if (combined.Contains("discord") || combined.Contains("teams") || combined.Contains("slack") ||
                combined.Contains("spotify") || combined.Contains("steam") || combined.Contains("skype"))
            {
                item.Category = "Messaging / Media";
                item.CanSafelyDisable = true;
                item.SafetyExplanation = "Heavy third-party application. Safe to disable on boot to improve dual-core laptop startup responsiveness.";
                item.Impact = StartupImpact.High;
                return;
            }

            // 6. Updaters & Utilities
            if (combined.Contains("update") || combined.Contains("helper") || combined.Contains("launcher"))
            {
                item.Category = "Application Updater";
                item.CanSafelyDisable = true;
                item.SafetyExplanation = "Background updater. Disabling prevents background checks on boot; applications will still update when opened.";
                item.Impact = StartupImpact.Medium;
                return;
            }

            item.Category = "General Utility";
            item.CanSafelyDisable = true;
            item.SafetyExplanation = "Non-essential startup entry. Safe to disable with your confirmation.";
            item.Impact = StartupImpact.Low;
        }

        private static string ExtractExecutablePath(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return string.Empty;

            var trimmed = commandLine.Trim();
            if (trimmed.StartsWith("\""))
            {
                var nextQuote = trimmed.IndexOf('\"', 1);
                if (nextQuote > 1)
                {
                    return trimmed.Substring(1, nextQuote - 1);
                }
            }

            var spaceIndex = trimmed.IndexOf(' ');
            return spaceIndex > 0 ? trimmed.Substring(0, spaceIndex) : trimmed;
        }
    }
}
