using Avalonia.Platform.Storage;
using FluentAssertions;
using Moq;

namespace DropAndForget.Tests.UI;

public sealed class DropPathExtractorTests
{
    [Fact]
    public void ExtractFiles_prefers_storage_files()
    {
        var storageItem = new Mock<IStorageItem>();
        storageItem.SetupGet(item => item.Path).Returns(new Uri("file:///tmp/file-one.txt"));

        var paths = DropAndForget.UI.DropPathExtractor.ExtractFiles([storageItem.Object]);

        paths.Should().Equal("/tmp/file-one.txt");
    }

    [Fact]
    public void ExtractText_falls_back_to_uri_list_text()
    {
        var paths = DropAndForget.UI.DropPathExtractor.ExtractText("# comment\nfile:///tmp/a.txt\nfile:///tmp/folder\n");

        paths.Should().Equal("/tmp/a.txt", "/tmp/folder");
    }

    [Fact]
    public void ExtractText_accepts_plain_absolute_paths_from_text()
    {
        var paths = DropAndForget.UI.DropPathExtractor.ExtractText("/tmp/a.txt\nrelative.txt\n/tmp/b.txt");

        paths.Should().Equal("/tmp/a.txt", "/tmp/b.txt");
    }
}
