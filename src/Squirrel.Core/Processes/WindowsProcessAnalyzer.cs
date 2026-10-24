using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Squirrel.Core.Safety;

namespace Squirrel.Core.Processes
{
    public class WindowsProcessAnalyzer : IProcessAnalyzer
    {
        public IReadOnlyList<ProcessInfo> GetRunningProcesses()
        {
            var result = new List<ProcessInfo>();
            var processes = Process.GetProcesses();

            try
            {
                foreach (var proc in processes)
                {
                    try
                    {
                        var info = new ProcessInfo
                        {
                            Id = proc.Id,
                            Name = proc.ProcessName
                        };

                        try { info.WorkingSetMb = proc.WorkingSet64 / (1024.0 * 1024.0); } catch { }
                        try { info.PrivateMemoryMb = proc.PrivateMemorySize64 / (1024.0 * 1024.0); } catch { }
                        try { info.ThreadCount = proc.Threads.Count; } catch { }
                        try { info.TotalProcessorTime = proc.TotalProcessorTime; } catch { }
                        try { info.MainWindowTitle = proc.MainWindowTitle; } catch { }
                        try { info.StartTime = proc.StartTime; } catch { }

                        try
                        {
                            var module = proc.MainModule;
                            if (module != null)
                            {
                                info.ExecutablePath = module.FileName ?? string.Empty;
                                var fileVersion = module.FileVersionInfo;
                                info.Description = fileVersion.FileDescription ?? string.Empty;
                                info.Publisher = fileVersion.CompanyName ?? string.Empty;
                            }
                        }
                        catch
                        {
                            // Expected for protected system processes or architecture mismatches
                        }

                        // Determine safety classifications
                        info.IsProtected = SafetyGuard.IsProtectedEntity(info.Name, out var reason);
                        info.ProtectionReason = reason;

                        info.IsMicrosoftOffice = SafetyGuard.ProtectedOfficeEntities.Contains(info.Name + ".exe") ||
                                                 SafetyGuard.ProtectedOfficeEntities.Contains(info.Name) ||
                                                 (!string.IsNullOrEmpty(info.Publisher) && info.Publisher.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                  !string.IsNullOrEmpty(info.Description) && info.Description.IndexOf("Office", StringComparison.OrdinalIgnoreCase) >= 0);

                        result.Add(info);
                    }
                    catch
                    {
                        // Process may have exited during enumeration
                    }
                }
            }
            finally
            {
                foreach (var p in processes)
                {
                    try { p.Dispose(); } catch { }
                }
            }

            return result.OrderByDescending(p => p.WorkingSetMb).ToList();
        }

        public Task<IReadOnlyList<ProcessInfo>> GetRunningProcessesAsync()
        {
            return Task.Run(() => GetRunningProcesses());
        }
    }
}
