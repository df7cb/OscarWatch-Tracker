using OscarWatch.Core.Dxcc;
using OscarWatch.Core.HamQth;
using OscarWatch.Core.Models;
using OscarWatch.Core.Qrz;
using OscarWatch.Core.Services;
using OscarWatch.Localization;
using OscarWatch.ViewModels;

namespace OscarWatch.Tests;

public class QsoLogbookCorrespondentLookupTests
{
    [Fact]
    public async Task New_contact_replaces_name_and_grid_from_the_latest_qso()
    {
        var repo = new FakeLogbookRepository();
        repo.LatestByCall["G0ABC"] = Qso("G0ABC", "Bob", "IO91");
        using var vm = CreateViewModel(repo);
        await SelectLogbookAsync(vm);

        vm.Name = "Alice";
        vm.Grid = "JO62";
        vm.Comment = "keep me";
        vm.Call = "G0ABC";

        Assert.Equal("Bob", vm.Name);
        Assert.Equal("IO91", vm.Grid);
        Assert.Equal("keep me", vm.Comment);
    }

    [Fact]
    public async Task New_contact_clears_name_and_grid_when_the_callsign_is_cleared()
    {
        using var vm = CreateViewModel(new FakeLogbookRepository());
        await SelectLogbookAsync(vm);

        vm.Name = "Alice";
        vm.Grid = "JO62";
        vm.Comment = "keep me";
        vm.Call = "G0";

        Assert.Equal("", vm.Name);
        Assert.Equal("", vm.Grid);
        Assert.Equal("keep me", vm.Comment);
    }

    [Fact]
    public async Task New_contact_clears_name_and_grid_when_lookup_finds_nothing()
    {
        using var vm = CreateViewModel(new FakeLogbookRepository());
        await SelectLogbookAsync(vm);

        vm.Name = "Alice";
        vm.Grid = "JO62";
        vm.Comment = "keep me";
        vm.Call = "M0XYZ";

        Assert.Equal("", vm.Name);
        Assert.Equal("", vm.Grid);
        Assert.Equal("keep me", vm.Comment);
    }

    [Fact]
    public async Task Editing_a_qso_keeps_the_saved_name_and_grid()
    {
        var saved = Qso("G0ABC", "Alice", "JO62", comment: "note");
        var repo = new FakeLogbookRepository();
        repo.LatestByCall["G0ABC"] = saved;
        repo.LatestByCall["M0XYZ"] = Qso("M0XYZ", "Bob", "IO91");
        using var vm = CreateViewModel(repo);
        await SelectLogbookAsync(vm);

        vm.SelectedQso = QsoRowViewModel.From(saved, use24Hour: true, LocalizationService.Instance);
        vm.BeginEditSelectedQsoCommand.Execute(null);
        vm.Call = "M0XYZ";

        Assert.Equal("Alice", vm.Name);
        Assert.Equal("JO62", vm.Grid);
        Assert.Equal("note", vm.Comment);
    }

    private static QsoLogbookViewModel CreateViewModel(FakeLogbookRepository repo) =>
        new(
            repo,
            new FakeTracker(),
            new FakeSettings(),
            new FakeCloudlogUpload(),
            new FakeSatelliteLink(),
            new FakeSatelliteStatus(),
            new FakeDxcc(),
            new FakeQrz(),
            new FakeHamQth(),
            LocalizationService.Instance);

    private static async Task SelectLogbookAsync(QsoLogbookViewModel vm)
    {
        vm.SelectedLogbook = new QsoLogbook { Id = 1, Name = "Main" };
        await Task.Delay(50);
    }

    private static QsoRecord Qso(string call, string name, string grid, string comment = "") => new()
    {
        Id = 1,
        LogbookId = 1,
        Call = call,
        Name = name,
        GridSquare = grid,
        Comment = comment,
        QsoUtc = DateTime.UtcNow,
        CreatedUtc = DateTime.UtcNow
    };

    private sealed class FakeLogbookRepository : IQsoLogbookRepository
    {
        public Dictionary<string, QsoRecord> LatestByCall { get; } = new(StringComparer.OrdinalIgnoreCase);

        public event Action<long>? QsosChanged { add { } remove { } }
        public string DatabasePath => "";

        public Task<QsoRecord?> FindLatestQsoForCallAsync(
            long logbookId, string call, CancellationToken cancellationToken = default) =>
            Task.FromResult(LatestByCall.TryGetValue(call, out var qso) ? qso : null);

        public Task<IReadOnlyList<QsoRecord>> ListQsosAsync(
            long logbookId, DateTime? fromUtcInclusive = null, DateTime? toUtcExclusive = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QsoRecord>>([]);

        public Task<IReadOnlyList<QsoRecord>> SearchQsosByCallAsync(
            long logbookId, string callPrefix, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QsoRecord>>([]);

        public Task<IReadOnlyList<QsoRecord>> ListQsosMissingDxccAsync(
            long logbookId, int limit = 500, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QsoRecord>>([]);

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<QsoLogbook>> ListLogbooksAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QsoLogbook>>([]);
        public Task<QsoLogbook> GetOrCreateLogbookAsync(QsoLogbookCreateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<QsoLogbook> CreateLogbookAsync(QsoLogbookCreateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<QsoLogbook> UpdateLogbookAsync(QsoLogbookUpdateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteLogbookAsync(long logbookId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<int> CountQsosAsync(long logbookId, DateTime? fromUtcInclusive = null, DateTime? toUtcExclusive = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
        public Task<QsoRecord?> FindLatestQsoForDxccAsync(long logbookId, int dxcc, CancellationToken cancellationToken = default) =>
            Task.FromResult<QsoRecord?>(null);
        public Task<QsoRecord> AddQsoAsync(QsoRecordCreateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<QsoRecord> UpdateQsoAsync(QsoRecordUpdateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteQsoAsync(long qsoId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<QsoLogbook?> GetLogbookByIdAsync(long logbookId, CancellationToken cancellationToken = default) =>
            Task.FromResult<QsoLogbook?>(null);
        public Task<QsoRecord?> GetQsoByIdAsync(long qsoId, CancellationToken cancellationToken = default) =>
            Task.FromResult<QsoRecord?>(null);
        public Task<QsoLogbook> UpdateLogbookCloudlogSettingsAsync(QsoLogbookCloudlogSettingsRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task UpdateQsoCloudlogUploadStateAsync(long qsoId, CloudlogUploadStatus status, int attempts, string? lastError, DateTime? sentUtc, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<IReadOnlyList<QsoRecord>> ListQsosPendingCloudlogUploadAsync(int limit = 25, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QsoRecord>>([]);
        public Task ResetFailedCloudlogUploadsAsync(long logbookId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<(IReadOnlySet<string> Calls, IReadOnlySet<string> GridFields)> LoadWorkedCallAndGridFieldsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlySet<string>, IReadOnlySet<string>)>(
                (new HashSet<string>(), new HashSet<string>()));
        public Task<IReadOnlyList<string>> ListRecentNonStandardCallsAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public string SettingsPath => "";
        public string? LoadError => null;
        public bool CanPersist => true;
        public string SerializeCurrent() => "";
        public Task ReplaceAndSaveAsync(AppSettings imported, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Load() { }
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void RequestSave() { }
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void SyncGridFromLatLon() { }
        public void SyncLatLonFromGrid() { }
        public void EnsureSavedStations() { }
        public void ApplyActiveStation() { }
        public void SyncActiveStationFromGroundStation() { }
    }

    private sealed class FakeTracker : ILiveTrackerSnapshotProvider
    {
        public string? FocusedNoradId => null;
        public LiveTrackerSnapshot GetCurrent() => LiveTrackerSnapshot.Empty;
    }

    private sealed class FakeCloudlogUpload : ICloudlogQsoUploadService
    {
        public event Action<long>? UploadStateChanged { add { } remove { } }
        public Task QueueUploadIfEnabledAsync(long qsoId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ProcessRetryQueueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetFailedUploadsForRetryAsync(long logbookId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSatelliteLink : ISatelliteLinkBroadcastService
    {
        public event Action? StateChanged { add { } remove { } }
        public bool IsListening => false;
        public int ClientCount => 0;
        public string? LastError => null;
        public void ApplySettings(SatelliteLinkSettings settings) { }
        public void Publish(SatelliteTrackState? track, RigTrackingContext? context, bool force = false) { }
        public void PublishQso(QsoRecord record, QsoLogbook logbook, SatelliteLinkQsoEventKind kind, string? noradId = null) { }
        public void PublishPassAlert(PassInfo pass, DateTime utcNow) { }
        public Task<bool> TestBindAsync(SatelliteLinkSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task StopAsync() => Task.CompletedTask;
    }

    private sealed class FakeSatelliteStatus : ISatelliteStatusReportService
    {
        public Task<SatelliteStatusTokenTestResult> TestTokenAsync(SatelliteStatusSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SatelliteStatusTokenTestResult(true, "", 200));
        public Task<SatelliteStatusReportResult> SubmitReportAsync(SatelliteStatusSettings settings, SatelliteStatusReportRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SatelliteStatusReportResult(true, true, "", 200));
        public Task<SatelliteStatusFetchResult> FetchCommunityAsync(SatelliteStatusSettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDxcc : IDxccLookupService
    {
        public string ActiveCountryFilePath => "";
        public DateTime? CountryFileLastWriteUtc => null;
        public bool TryResolve(string? callsign, out DxccMatch match)
        {
            match = default;
            return false;
        }
        public void EnsureLoaded() { }
        public void Reload() { }
        public Task<DxccCountryFileUpdateResult> UpdateCountryFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DxccCountryFileUpdateResult());
    }

    private sealed class FakeQrz : IQrzCallbookService
    {
        public bool CanLookup(QrzSettings? settings) => false;
        public Task<QrzConnectionTestResult> TestConnectionAsync(QrzSettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<QrzCallbookEntry?> LookupAsync(QrzSettings settings, string callsign, CancellationToken cancellationToken = default) =>
            Task.FromResult<QrzCallbookEntry?>(null);
    }

    private sealed class FakeHamQth : IHamQthCallbookService
    {
        public bool CanLookup(HamQthSettings? settings) => false;
        public Task<HamQthConnectionTestResult> TestConnectionAsync(HamQthSettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<QrzCallbookEntry?> LookupAsync(HamQthSettings settings, string callsign, CancellationToken cancellationToken = default) =>
            Task.FromResult<QrzCallbookEntry?>(null);
    }
}
