using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Squirrel.Core.Actions;
using Squirrel.Core.Audit;
using Squirrel.Core.Diagnostics;
using Squirrel.Core.Processes;
using Squirrel.Core.Recommendations;
using Squirrel.Core.Storage;

namespace Squirrel.Wpf.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ISystemMonitor _monitor;
        private readonly IAuditLedger _ledger;
        private readonly IStartupAnalyzer _startupAnalyzer;
        private readonly IStorageAnalyzer _storageAnalyzer;
        private readonly IRecommendationEngine _recommendationEngine;
        private readonly BottleneckDetector _bottleneckDetector;
        private readonly SafeStorageCleaner _storageCleaner;
        private readonly DispatcherTimer _liveTimer;

        // Navigation
        private string _selectedTab = "Dashboard";
        public string SelectedTab
        {
            get => _selectedTab;
            set { _selectedTab = value; OnPropertyChanged(); }
        }

        // Live Dashboard Metrics
        private SystemMetrics _currentMetrics = new SystemMetrics();
        public SystemMetrics CurrentMetrics
        {
            get => _currentMetrics;
            set { _currentMetrics = value; OnPropertyChanged(); }
        }

        // Diagnostic State
        private bool _isDiagnosing;
        public bool IsDiagnosing
        {
            get => _isDiagnosing;
            set { _isDiagnosing = value; OnPropertyChanged(); }
        }

        private string _diagnosticProgressMessage = "Ready to run multi-sample diagnosis.";
        public string DiagnosticProgressMessage
        {
            get => _diagnosticProgressMessage;
            set { _diagnosticProgressMessage = value; OnPropertyChanged(); }
        }

        private BottleneckReport? _bottleneckReport;
        public BottleneckReport? BottleneckReport
        {
            get => _bottleneckReport;
            set { _bottleneckReport = value; OnPropertyChanged(); }
        }

        // Storage State
        private bool _isScanningStorage;
        public bool IsScanningStorage
        {
            get => _isScanningStorage;
            set { _isScanningStorage = value; OnPropertyChanged(); }
        }

        private StorageReport? _storageReport;
        public StorageReport? StorageReport
        {
            get => _storageReport;
            set { _storageReport = value; OnPropertyChanged(); }
        }

        private string _statusMessage = "Squirrel active. Strict safety enabled.";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        // Collections
        public ObservableCollection<StartupItem> StartupItems { get; } = new ObservableCollection<StartupItem>();
        public ObservableCollection<CleanupTarget> CleanupTargets { get; } = new ObservableCollection<CleanupTarget>();
        public ObservableCollection<Recommendation> Recommendations { get; } = new ObservableCollection<Recommendation>();
        public ObservableCollection<AuditEntry> AuditEntries { get; } = new ObservableCollection<AuditEntry>();

        // Commands
        public ICommand SelectTabCommand { get; }
        public ICommand RunBottleneckScanCommand { get; }
        public ICommand RefreshStartupCommand { get; }
        public ICommand ToggleStartupItemCommand { get; }
        public ICommand ScanStorageCommand { get; }
        public ICommand DryRunStorageCommand { get; }
        public ICommand CleanSelectedStorageCommand { get; }
        public ICommand RefreshRecommendationsCommand { get; }
        public ICommand ExecuteRecommendationCommand { get; }
        public ICommand RefreshAuditHistoryCommand { get; }
        public ICommand RollbackAuditEntryCommand { get; }

        public MainViewModel()
        {
            _monitor = new WindowsSystemMonitor();
            _ledger = new LocalJsonAuditLedger();
            _startupAnalyzer = new WindowsStartupAnalyzer();
            _storageAnalyzer = new WindowsStorageAnalyzer();
            _recommendationEngine = new WindowsRecommendationEngine(_ledger);
            _bottleneckDetector = new BottleneckDetector(_monitor);
            _storageCleaner = new SafeStorageCleaner(_ledger);

            // Command Wiring
            SelectTabCommand = new RelayCommand(p => SelectedTab = p?.ToString() ?? "Dashboard");
            RunBottleneckScanCommand = new RelayCommand(async () => await RunBottleneckScanAsync());
            RefreshStartupCommand = new RelayCommand(async () => await LoadStartupItemsAsync());
            ToggleStartupItemCommand = new RelayCommand(async p => await ToggleStartupItemAsync(p as StartupItem));
            ScanStorageCommand = new RelayCommand(async () => await LoadStorageTargetsAsync());
            DryRunStorageCommand = new RelayCommand(async () => await RunStorageDryRunAsync());
            CleanSelectedStorageCommand = new RelayCommand(async () => await CleanSelectedStorageAsync());
            RefreshRecommendationsCommand = new RelayCommand(async () => await LoadRecommendationsAsync());
            ExecuteRecommendationCommand = new RelayCommand(async p => await ExecuteRecommendationAsync(p as Recommendation));
            RefreshAuditHistoryCommand = new RelayCommand(async () => await LoadAuditHistoryAsync());
            RollbackAuditEntryCommand = new RelayCommand(async p => await RollbackAuditEntryAsync(p as AuditEntry));

            // Start Live Monitor Timer
            _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _liveTimer.Tick += (s, e) => UpdateLiveMetrics();
            _liveTimer.Start();

            // Initial snapshot
            UpdateLiveMetrics();

            // Load background data
            _ = LoadStartupItemsAsync();
            _ = LoadStorageTargetsAsync();
            _ = LoadAuditHistoryAsync();
        }

        private void UpdateLiveMetrics()
        {
            try
            {
                CurrentMetrics = _monitor.GetSnapshot();
            }
            catch { }
        }

        public async Task RunBottleneckScanAsync()
        {
            if (IsDiagnosing) return;

            IsDiagnosing = true;
            DiagnosticProgressMessage = "Sampling system and process load across 5 intervals...";
            StatusMessage = "Diagnosing sustained bottlenecks...";

            var progress = new Progress<string>(msg => DiagnosticProgressMessage = msg);

            try
            {
                var report = await _bottleneckDetector.RunDiagnosticAsync(sampleCount: 5, intervalMs: 1500, progress: progress);
                BottleneckReport = report;
                DiagnosticProgressMessage = $"Completed. Average CPU: {report.AverageSystemCpuPercent:F1}%. Found {report.SustainedCpuOffenders.Count} sustained offenders.";
                StatusMessage = "Bottleneck diagnosis completed.";
                await LoadRecommendationsAsync();
            }
            catch (Exception ex)
            {
                DiagnosticProgressMessage = $"Diagnosis error: {ex.Message}";
                StatusMessage = "Diagnosis failed.";
            }
            finally
            {
                IsDiagnosing = false;
            }
        }

        public async Task LoadStartupItemsAsync()
        {
            try
            {
                var items = await _startupAnalyzer.GetStartupItemsAsync();
                StartupItems.Clear();
                foreach (var item in items)
                {
                    StartupItems.Add(item);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load startup items: {ex.Message}";
            }
        }

        public async Task ToggleStartupItemAsync(StartupItem? item)
        {
            if (item == null) return;

            if (!item.CanSafelyDisable)
            {
                MessageBox.Show($"'{item.Name}' is protected:\n{item.SafetyExplanation}", "Protected Entity", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var action = new DisableStartupItemAction(item, _ledger);
            if (item.IsEnabled)
            {
                var confirm = MessageBox.Show(
                    $"Do you want to disable startup for '{item.Name}'?\n\nImpact: {item.Impact}\nDetails: {item.SafetyExplanation}\n\nThis is completely reversible and will not delete any files.",
                    "Confirm Disable Startup", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirm == MessageBoxResult.Yes)
                {
                    var result = await action.ExecuteAsync(userConfirmed: true);
                    MessageBox.Show(result.Message, result.Succeeded ? "Success" : "Failed", MessageBoxButton.OK,
                        result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
                    await LoadStartupItemsAsync();
                    await LoadAuditHistoryAsync();
                }
            }
            else
            {
                var result = await action.RollbackAsync();
                MessageBox.Show(result.Message, result.Succeeded ? "Success" : "Failed", MessageBoxButton.OK,
                    result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
                await LoadStartupItemsAsync();
                await LoadAuditHistoryAsync();
            }
        }

        public async Task LoadStorageTargetsAsync()
        {
            if (IsScanningStorage) return;
            IsScanningStorage = true;
            StatusMessage = "Scanning safe storage cleanup targets...";

            try
            {
                var report = await _storageAnalyzer.AnalyzeStorageAsync();
                StorageReport = report;
                CleanupTargets.Clear();
                foreach (var target in report.Targets)
                {
                    CleanupTargets.Add(target);
                }
                StatusMessage = $"Storage scan complete. Reclaimable: {report.TotalReclaimableMb:F1} MB.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Storage scan error: {ex.Message}";
            }
            finally
            {
                IsScanningStorage = false;
            }
        }

        public async Task RunStorageDryRunAsync()
        {
            var selected = CleanupTargets.Where(t => t.IsSelectedForCleanup).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Please select at least one cleanup target checkbox to simulate.", "No Targets Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int simulatedFiles = 0;
            double simulatedMb = 0.0;

            foreach (var t in selected)
            {
                var res = await _storageCleaner.CleanTargetAsync(t, userConfirmed: true, dryRun: true);
                simulatedFiles += res.DeletedFilesCount;
                simulatedMb += res.DeletedMb;
            }

            MessageBox.Show(
                $"[Dry Run Simulation Complete]\n\nTargets evaluated: {selected.Count}\nFiles that would be removed: {simulatedFiles}\nSpace that would be reclaimed: {simulatedMb:F1} MB\n\nNo files were deleted from your computer.",
                "Dry Run Result", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public async Task CleanSelectedStorageAsync()
        {
            var selected = CleanupTargets.Where(t => t.IsSelectedForCleanup).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Please check at least one target in the list to clean.", "No Targets Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var totalMb = selected.Sum(s => s.SizeMb);
            var confirm = MessageBox.Show(
                $"Review-First Approval Required:\n\nYou have selected {selected.Count} cache targets ({totalMb:F1} MB).\nPersonal files and Microsoft Office are strictly protected.\n\nProceed with cleanup?",
                "Confirm Safe Cleanup", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            int cleanedFiles = 0;
            double reclaimedMb = 0.0;

            foreach (var t in selected)
            {
                var res = await _storageCleaner.CleanTargetAsync(t, userConfirmed: true, dryRun: false);
                cleanedFiles += res.DeletedFilesCount;
                reclaimedMb += res.DeletedMb;
            }

            MessageBox.Show($"Cleanup Completed:\nReclaimed {reclaimedMb:F1} MB across {cleanedFiles} files.", "Cleanup Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadStorageTargetsAsync();
            await LoadAuditHistoryAsync();
        }

        public async Task LoadRecommendationsAsync()
        {
            try
            {
                var currentMetrics = CurrentMetrics;
                var bottleneckReport = BottleneckReport;
                var items = StartupItems.ToList();
                var storageReport = StorageReport;

                var recs = await Task.Run(() => _recommendationEngine.Evaluate(currentMetrics, bottleneckReport, items, storageReport));

                Recommendations.Clear();
                foreach (var r in recs)
                {
                    Recommendations.Add(r);
                }
            }
            catch { }
        }

        public async Task ExecuteRecommendationAsync(Recommendation? rec)
        {
            if (rec?.Action == null) return;

            var confirm = MessageBox.Show(
                $"{rec.Title}\n\nEvidence: {rec.Evidence}\nExpected Benefit: {rec.ExpectedBenefit}\n\nExecute this safe action now?",
                "Confirm Recommended Action", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                var res = await rec.Action.ExecuteAsync(userConfirmed: true);
                MessageBox.Show(res.Message, res.Succeeded ? "Success" : "Failed", MessageBoxButton.OK,
                    res.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
                await LoadStartupItemsAsync();
                await LoadRecommendationsAsync();
                await LoadAuditHistoryAsync();
            }
        }

        public async Task LoadAuditHistoryAsync()
        {
            try
            {
                var entries = await _ledger.GetEntriesAsync();
                AuditEntries.Clear();
                foreach (var entry in entries.OrderByDescending(e => e.TimestampUtc))
                {
                    AuditEntries.Add(entry);
                }
            }
            catch { }
        }

        public async Task RollbackAuditEntryAsync(AuditEntry? entry)
        {
            if (entry == null || !entry.IsReversible || entry.Status == "Reverted") return;

            var confirm = MessageBox.Show(
                $"Revert the following previous action?\n\nAction: {entry.ActionType}\nTarget: {entry.TargetName}\nPrevious State: {entry.PreviousState}",
                "Confirm Rollback", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                // Revert via ledger
                await _ledger.MarkRevertedAsync(entry.Id);
                MessageBox.Show($"Action for '{entry.TargetName}' was marked reverted. Refreshing system state.", "Rollback Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadStartupItemsAsync();
                await LoadAuditHistoryAsync();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
