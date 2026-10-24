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
                return ActionResult.Failure("Action aborted: Explicit user confirmation is required to disable startup items.");
            }

            if (_startupItem.IsRunOnce)
            {
                return ActionResult.Failure($"Cannot disable '{_startupItem.Name}': RunOnce entries are temporary post-install triggers. Modifying RunOnce entries is unsafe.");
            }

            if (!_startupItem.CanSafelyDisable)
            {
                var explanation = string.IsNullOrWhiteSpace(_startupItem.SafetyExplanation)
                    ? "Item is marked as unsafe to disable."
                    : _startupItem.SafetyExplanation;
                return ActionResult.Failure($"Cannot disable '{_startupItem.Name}': {explanation}");
            }

            if (SafetyGuard.IsProtectedEntity(_startupItem.Name, _startupItem.ExecutablePath, out var reason))
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
                    var subKeyPath = !string.IsNullOrEmpty(_startupItem.SubKeyPath)
                        ? _startupItem.SubKeyPath
                        : @"Software\Microsoft\Windows\CurrentVersion\Run";

                    RegistryKey? sourceKey;
                    try
                    {
                        sourceKey = baseKey.OpenSubKey(subKeyPath, true);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        var failMsg = isHklm
                            ? "Administrator permissions are required to modify system-wide (HKLM) startup entries. Please run Squirrel as Administrator."
                            : $"Access denied to registry key: {subKeyPath}";

                        await LogFailureAsync("DisableStartupItem", failMsg);
                        return ActionResult.Failure(failMsg);
                    }

                    if (sourceKey == null)
                    {
                        var failMsg = $"Registry key not found: {subKeyPath}";
                        await LogFailureAsync("DisableStartupItem", failMsg);
                        return ActionResult.Failure(failMsg);
                    }

                    using (sourceKey)
                    {
                        var val = sourceKey.GetValue(_startupItem.Name)?.ToString();
                        if (val == null)
                        {
                            var failMsg = $"Startup entry '{_startupItem.Name}' not found in registry.";
                            await LogFailureAsync("DisableStartupItem", failMsg);
                            return ActionResult.Failure(failMsg);
                        }

                        var kind = sourceKey.GetValueKind(_startupItem.Name);
                        var backupHive = isHklm ? "HKLM" : "HKCU";
                        var backupSubPath = $@"Software\Squirrel\DisabledStartup\{backupHive}";

                        // Backup into dedicated user branch with exact value kind
                        using (var backupKey = Registry.CurrentUser.CreateSubKey(backupSubPath))
                        {
                            if (backupKey == null)
                            {
                                var failMsg = "Failed to create local Squirrel backup registry key.";
                                await LogFailureAsync("DisableStartupItem", failMsg);
                                return ActionResult.Failure(failMsg);
                            }

                            // Check if already backed up to avoid accidental overwrite of older original
                            if (backupKey.GetValue(_startupItem.Name) == null)
                            {
                                backupKey.SetValue(_startupItem.Name, val, kind);
                                backupKey.SetValue(_startupItem.Name + "_SubKeyPath", subKeyPath, RegistryValueKind.String);
                                backupKey.SetValue(_startupItem.Name + "_Kind", (int)kind, RegistryValueKind.DWord);
                            }
                        }

                        // Remove from active Run key
                        sourceKey.DeleteValue(_startupItem.Name, false);
                        _startupItem.IsEnabled = false;

                        if (_auditLedger != null)
                        {
                            var rollbackPayload = JsonSerializer.Serialize(new
                            {
                                IsHklm = isHklm,
                                SubKeyPath = subKeyPath,
                                Name = _startupItem.Name,
                                CommandLine = val,
                                ValueKind = (int)kind
                            });

                            await _auditLedger.RecordEntryAsync(new AuditEntry
                            {
                                Id = Id,
                                ActionType = "DisableStartupItem",
                                TargetName = _startupItem.Name,
                                TargetIdentifier = $@"{baseKey.Name}\{subKeyPath}",
                                Description = $"Disabled startup entry '{_startupItem.Name}' (preserved as {kind})",
                                PreviousState = $"Enabled ({val})",
                                NewState = "Disabled",
                                IsReversible = true,
                                RollbackDataJson = rollbackPayload,
                                Status = "Completed"
                            });
                        }

                        return ActionResult.Success($"Successfully disabled startup item '{_startupItem.Name}'. It has been backed up in Squirrel's local configuration and can be restored at any time.");
                    }
                }
                else if (_startupItem.LocationType == StartupLocationType.UserStartupFolder ||
                         _startupItem.LocationType == StartupLocationType.CommonStartupFolder)
                {
                    var path = _startupItem.CommandLine;
                    if (!File.Exists(path))
                    {
                        var failMsg = $"Startup shortcut file '{path}' does not exist on disk.";
                        await LogFailureAsync("DisableStartupFolderItem", failMsg);
                        return ActionResult.Failure(failMsg);
                    }

                    var disabledPath = path + ".disabled";
                    if (File.Exists(disabledPath))
                    {
                        var failMsg = $"A disabled backup '{Path.GetFileName(disabledPath)}' already exists in the startup folder.";
                        await LogFailureAsync("DisableStartupFolderItem", failMsg);
                        return ActionResult.Failure(failMsg);
                    }

                    File.Move(path, disabledPath, false);
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

                return ActionResult.Failure($"Unsupported location type: {_startupItem.LocationType}");
            }
            catch (Exception ex)
            {
                var failMsg = $"Failed to disable startup item: {ex.Message}";
                await LogFailureAsync("DisableStartupItem", failMsg);
                return ActionResult.Failure(failMsg, ex.ToString());
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
                    var backupHive = isHklm ? "HKLM" : "HKCU";
                    var backupSubPath = $@"Software\Squirrel\DisabledStartup\{backupHive}";

                    using var backupKey = Registry.CurrentUser.OpenSubKey(backupSubPath, true);
                    if (backupKey == null)
                    {
                        return ActionResult.Failure("Backup registry location not found.");
                    }

                    var val = backupKey.GetValue(_startupItem.Name)?.ToString();
                    if (string.IsNullOrEmpty(val))
                    {
                        val = _startupItem.CommandLine;
                    }

                    var subKeyPath = backupKey.GetValue(_startupItem.Name + "_SubKeyPath")?.ToString() ??
                                     (!string.IsNullOrEmpty(_startupItem.SubKeyPath) ? _startupItem.SubKeyPath : @"Software\Microsoft\Windows\CurrentVersion\Run");

                    var kindObj = backupKey.GetValue(_startupItem.Name + "_Kind");
                    var kind = kindObj != null && Enum.IsDefined(typeof(RegistryValueKind), kindObj)
                        ? (RegistryValueKind)Convert.ToInt32(kindObj)
                        : RegistryValueKind.String;

                    var baseKey = isHklm ? Registry.LocalMachine : Registry.CurrentUser;
                    RegistryKey? targetKey;
                    try
                    {
                        targetKey = baseKey.OpenSubKey(subKeyPath, true);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return ActionResult.Failure(isHklm
                            ? "Administrator permissions are required to restore system-wide (HKLM) startup entries. Please run Squirrel as Administrator."
                            : $"Access denied to registry key: {subKeyPath}");
                    }

                    if (targetKey == null)
                    {
                        return ActionResult.Failure($"Target registry key '{subKeyPath}' not found.");
                    }

                    using (targetKey)
                    {
                        // Conflict Detection: Prevent silently overwriting an existing startup entry
                        if (targetKey.GetValue(_startupItem.Name) != null)
                        {
                            return ActionResult.Failure($"Conflict detected: An active startup entry named '{_startupItem.Name}' already exists in {subKeyPath}. Restoration was aborted to prevent overwriting.");
                        }

                        targetKey.SetValue(_startupItem.Name, val, kind);
                    }

                    // Clean up backup keys
                    backupKey.DeleteValue(_startupItem.Name, false);
                    backupKey.DeleteValue(_startupItem.Name + "_SubKeyPath", false);
                    backupKey.DeleteValue(_startupItem.Name + "_Kind", false);

                    _startupItem.IsEnabled = true;

                    if (_auditLedger != null)
                    {
                        await _auditLedger.MarkRevertedAsync(Id);
                    }

                    return ActionResult.Success($"Successfully re-enabled startup item '{_startupItem.Name}' in {subKeyPath}.");
                }
                else if (_startupItem.LocationType == StartupLocationType.UserStartupFolder ||
                         _startupItem.LocationType == StartupLocationType.CommonStartupFolder)
                {
                    var disabledPath = _startupItem.CommandLine.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                        ? _startupItem.CommandLine
                        : _startupItem.CommandLine + ".disabled";

                    var originalPath = _startupItem.CommandLine.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                        ? _startupItem.CommandLine.Substring(0, _startupItem.CommandLine.Length - ".disabled".Length)
                        : _startupItem.CommandLine;

                    if (!File.Exists(disabledPath))
                    {
                        return ActionResult.Failure($"Disabled backup file '{Path.GetFileName(disabledPath)}' was not found to restore.");
                    }

                    // Conflict Detection: Do not overwrite an existing shortcut
                    if (File.Exists(originalPath))
                    {
                        return ActionResult.Failure($"Conflict detected: A file named '{Path.GetFileName(originalPath)}' already exists in the startup folder. Restoration aborted.");
                    }

                    File.Move(disabledPath, originalPath, false);
                    _startupItem.IsEnabled = true;

                    if (_auditLedger != null)
                    {
                        await _auditLedger.MarkRevertedAsync(Id);
                    }

                    return ActionResult.Success($"Successfully restored startup shortcut '{_startupItem.Name}'.");
                }

                return ActionResult.Failure("Unsupported location type for rollback.");
            }
            catch (Exception ex)
            {
                return ActionResult.Failure($"Rollback failed: {ex.Message}", ex.ToString());
            }
        }

        private async Task LogFailureAsync(string actionType, string errorReason)
        {
            if (_auditLedger == null) return;
            try
            {
                await _auditLedger.RecordEntryAsync(new AuditEntry
                {
                    Id = Id,
                    ActionType = actionType,
                    TargetName = _startupItem.Name,
                    TargetIdentifier = _startupItem.LocationPath,
                    Description = $"Attempted to disable '{_startupItem.Name}' but failed: {errorReason}",
                    PreviousState = "Enabled",
                    NewState = "Unchanged",
                    IsReversible = false,
                    Status = "Failed"
                });
            }
            catch { }
        }
    }
}
