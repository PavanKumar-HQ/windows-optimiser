using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using Squirrel.Core.Audit;
using Squirrel.Core.Processes;
using Squirrel.Core.Safety;

namespace Squirrel.Core.Actions
{
    public class DisableStartupItemAction : ISystemAction
    {
        private readonly StartupItem _startupItem;
        private readonly IAuditLedger? _auditLedger;

        public string Id => _startupItem.Id;
        public string Title => $"Disable Startup: {_startupItem.Name}";
        public string Description => $"Stops '{_startupItem.Name}' from launching automatically on Windows boot. {_startupItem.SafetyExplanation}";
        public ActionRiskLevel RiskLevel => ActionRiskLevel.Low;
        public bool IsReversible => true;
        public bool RequiresAdmin => _startupItem.LocationType == StartupLocationType.RegistryLocalMachine || _startupItem.LocationType == StartupLocationType.CommonStartupFolder;

        public DisableStartupItemAction(StartupItem startupItem, IAuditLedger? auditLedger = null)
        {
            _startupItem = startupItem ?? throw new ArgumentNullException(nameof(startupItem));
            _auditLedger = auditLedger;
        }

        public async Task<ActionResult> ExecuteAsync(bool userConfirmed)
        {
            if (!userConfirmed)
            {
                return ActionResult.Failure("Action aborted: User confirmation is required to disable startup items.");
            }

            if (!_startupItem.CanSafelyDisable)
            {
                return ActionResult.Failure($"Cannot disable '{_startupItem.Name}': {_startupItem.SafetyExplanation}");
            }

            if (SafetyGuard.IsProtectedEntity(_startupItem.Name, out var reason))
            {
                return ActionResult.Failure($"Cannot disable protected entity '{_startupItem.Name}': {reason}");
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return ActionResult.Success($"[Cross-platform Simulated] Disabled startup item {_startupItem.Name}");
            }

            try
            {
                if (_startupItem.LocationType == StartupLocationType.RegistryCurrentUser ||
                    _startupItem.LocationType == StartupLocationType.RegistryLocalMachine)
                {
                    var isHklm = _startupItem.LocationType == StartupLocationType.RegistryLocalMachine;
                    var baseKey = isHklm ? Registry.LocalMachine : Registry.CurrentUser;
                    var subKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

                    using var sourceKey = baseKey.OpenSubKey(subKeyPath, true);
                    if (sourceKey == null)
                    {
                        return ActionResult.Failure($"Registry key not found: {subKeyPath}");
                    }

                    var val = sourceKey.GetValue(_startupItem.Name)?.ToString();
                    if (val == null)
                    {
                        return ActionResult.Failure($"Startup entry '{_startupItem.Name}' not found in registry.");
                    }

                    // Backup into Squirrel disabled registry branch
                    using (var backupKey = Registry.CurrentUser.CreateSubKey($@"Software\Squirrel\DisabledStartup\{(isHklm ? "HKLM" : "HKCU")}"))
                    {
                        backupKey?.SetValue(_startupItem.Name, val);
                    }

                    // Remove from active Run key
                    sourceKey.DeleteValue(_startupItem.Name, false);
                    _startupItem.IsEnabled = false;

                    if (_auditLedger != null)
                    {
                        var rollbackPayload = JsonSerializer.Serialize(new
                        {
                            IsHklm = isHklm,
                            Name = _startupItem.Name,
                            CommandLine = val
                        });

                        await _auditLedger.RecordEntryAsync(new AuditEntry
                        {
                            Id = Id,
                            ActionType = "DisableStartupItem",
                            TargetName = _startupItem.Name,
                            TargetIdentifier = _startupItem.LocationPath,
                            Description = $"Disabled startup entry '{_startupItem.Name}'",
                            PreviousState = $"Enabled ({val})",
                            NewState = "Disabled",
                            IsReversible = true,
                            RollbackDataJson = rollbackPayload,
                            Status = "Completed"
                        });
                    }

                    return ActionResult.Success($"Successfully disabled startup item '{_startupItem.Name}'. It has been backed up and can be re-enabled at any time.");
                }
                else if (_startupItem.LocationType == StartupLocationType.UserStartupFolder ||
                         _startupItem.LocationType == StartupLocationType.CommonStartupFolder)
                {
                    var path = _startupItem.CommandLine;
                    if (File.Exists(path))
                    {
                        var disabledPath = path + ".disabled";
                        File.Move(path, disabledPath, true);
                        _startupItem.IsEnabled = false;

                        if (_auditLedger != null)
                        {
                            var rollbackPayload = JsonSerializer.Serialize(new
                            {
                                OriginalPath = path,
                                DisabledPath = disabledPath
                            });

                            await _auditLedger.RecordEntryAsync(new AuditEntry
                            {
                                Id = Id,
                                ActionType = "DisableStartupFolderItem",
                                TargetName = _startupItem.Name,
                                TargetIdentifier = path,
                                Description = $"Renamed startup shortcut to .disabled",
                                PreviousState = "Enabled",
                                NewState = "Disabled",
                                IsReversible = true,
                                RollbackDataJson = rollbackPayload,
                                Status = "Completed"
                            });
                        }

                        return ActionResult.Success($"Successfully disabled shortcut '{_startupItem.Name}' in startup folder.");
                    }

                    return ActionResult.Failure($"File '{path}' does not exist.");
                }

                return ActionResult.Failure($"Unsupported location type: {_startupItem.LocationType}");
            }
            catch (Exception ex)
            {
                return ActionResult.Failure($"Failed to disable startup item: {ex.Message}", ex.ToString());
            }
        }

        public async Task<ActionResult> RollbackAsync()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return ActionResult.Success($"[Cross-platform Simulated] Restored startup item {_startupItem.Name}");
            }

            try
            {
                if (_startupItem.LocationType == StartupLocationType.RegistryCurrentUser ||
                    _startupItem.LocationType == StartupLocationType.RegistryLocalMachine)
                {
                    var isHklm = _startupItem.LocationType == StartupLocationType.RegistryLocalMachine;
                    var backupPath = $@"Software\Squirrel\DisabledStartup\{(isHklm ? "HKLM" : "HKCU")}";

                    using var backupKey = Registry.CurrentUser.OpenSubKey(backupPath, true);
                    var val = backupKey?.GetValue(_startupItem.Name)?.ToString();

                    if (val == null)
                    {
                        val = _startupItem.CommandLine;
                    }

                    var baseKey = isHklm ? Registry.LocalMachine : Registry.CurrentUser;
                    using (var targetKey = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (targetKey != null && !string.IsNullOrEmpty(val))
                        {
                            targetKey.SetValue(_startupItem.Name, val);
                        }
                    }

                    backupKey?.DeleteValue(_startupItem.Name, false);
                    _startupItem.IsEnabled = true;

                    if (_auditLedger != null)
                    {
                        await _auditLedger.MarkRevertedAsync(Id);
                    }

                    return ActionResult.Success($"Successfully re-enabled startup item '{_startupItem.Name}'.");
                }
                else if (_startupItem.LocationType == StartupLocationType.UserStartupFolder ||
                         _startupItem.LocationType == StartupLocationType.CommonStartupFolder)
                {
                    var disabledPath = _startupItem.CommandLine + ".disabled";
                    var originalPath = _startupItem.CommandLine;

                    if (File.Exists(disabledPath))
                    {
                        File.Move(disabledPath, originalPath, true);
                        _startupItem.IsEnabled = true;

                        if (_auditLedger != null)
                        {
                            await _auditLedger.MarkRevertedAsync(Id);
                        }

                        return ActionResult.Success($"Successfully restored startup shortcut '{_startupItem.Name}'.");
                    }

                    return ActionResult.Failure($"Disabled file '{disabledPath}' was not found to restore.");
                }

                return ActionResult.Failure("Unsupported location type for rollback.");
            }
            catch (Exception ex)
            {
                return ActionResult.Failure($"Rollback failed: {ex.Message}", ex.ToString());
            }
        }
    }
}
