using DropAndForget.Models;
using DropAndForget.Services.Sync;
using DropAndForget.Tests.TestSupport;
using FluentAssertions;

namespace DropAndForget.Tests.Sync;

public sealed class SyncStateStoreTests
{
    [Fact]
    public void SaveAndLoad_ShouldRoundTripStateForOneConfigIdentity()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new SyncStateStore(temporaryDirectory.Path);
        var config = TestAppConfigFactory.Create(storageMode: StorageMode.Sync, syncFolderPath: "/tmp/sync");
        var state = new Dictionary<string, SyncItemState>
        {
            ["docs/report.txt"] = new()
            {
                RelativePath = "docs/report.txt",
                LastKnownLocalSize = 42,
                RemoteETag = "etag-1"
            }
        };

        store.Save(config, state);
        var loaded = store.Load(config);

        loaded.Should().ContainKey("docs/report.txt");
        loaded["docs/report.txt"].RemoteETag.Should().Be("etag-1");
    }

    [Fact]
    public void Load_ShouldReturnEmptyStateWhenJsonIsCorruptedOrEntriesAreInvalid()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new SyncStateStore(temporaryDirectory.Path);
        var config = TestAppConfigFactory.Create(storageMode: StorageMode.Sync, syncFolderPath: "/tmp/sync");

        store.Save(config, new Dictionary<string, SyncItemState>
        {
            ["valid.txt"] = new() { RelativePath = "valid.txt", RemoteETag = "etag" }
        });

        var stateFile = Directory.GetFiles(temporaryDirectory.Path, "*.json", SearchOption.AllDirectories).Single();
        File.WriteAllText(stateFile, "[{\"relativePath\":\"\"}, {not-json}]");

        var loaded = store.Load(config);

        loaded.Should().BeEmpty();
    }
}
