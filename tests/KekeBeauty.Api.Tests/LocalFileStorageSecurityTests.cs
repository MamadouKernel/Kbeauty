using KekeBeauty.Infrastructure.Onboarding;
using Microsoft.Extensions.Configuration;

namespace KekeBeauty.Api.Tests;

public sealed class LocalFileStorageSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kekebeauty-tests", Guid.NewGuid().ToString("N"));
    private LocalFileStorage Storage() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Storage:KycRootPath"] = _root }).Build());

    [Fact]
    public async Task Rejects_unsafe_extension()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Storage().SaveAsync(Guid.NewGuid(), "document-recto", "../exe", new MemoryStream([1, 2, 3]), CancellationToken.None));
    }

    [Fact]
    public async Task Refuses_path_outside_storage()
    {
        var stream = await Storage().OpenAsync("../../secret.txt", CancellationToken.None);
        Assert.Null(stream);
    }

    [Fact]
    public async Task Saved_file_can_be_opened_and_deleted()
    {
        var id = Guid.NewGuid();
        var storage = Storage();
        var path = await storage.SaveAsync(id, "document-recto", "png", new MemoryStream([1, 2, 3]), CancellationToken.None);
        await using (var stream = await storage.OpenAsync(path, CancellationToken.None))
        {
            Assert.NotNull(stream);
            Assert.Equal(3, stream!.Length);
        }
        await storage.DeleteApplicationFilesAsync(id, CancellationToken.None);
        Assert.Null(await storage.OpenAsync(path, CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}