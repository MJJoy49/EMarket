using System.Text;
using EMarket.BLL.Common;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace EMarket.Tests.Common;

public sealed class FileStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "emarket-files-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage storage;

    public FileStorageTests() =>
        storage = new LocalFileStorage(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Root"] = root }).Build());

    // A valid 1x1 PNG.
    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static UploadFile Upload(byte[] bytes) => new(new MemoryStream(bytes), bytes.Length);

    [Fact]
    public async Task A_real_image_is_saved_with_a_thumbnail_under_a_random_name()
    {
        var saved = await storage.SaveImageWithThumbnailAsync(Upload(Png()), "products");

        saved.Path.ShouldStartWith("products/");
        saved.Path.ShouldEndWith(".png");
        File.Exists(Path.Combine(root, saved.ThumbPath)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_renamed_executable_is_refused() =>
        (await Should.ThrowAsync<AppException>(() => storage.SaveImageAsync(Upload(Encoding.ASCII.GetBytes("MZ fake exe")), "products")))
            .Message.ShouldBe(EMarket.Shared.Constants.Messages.BadFile);

    [Fact]
    public async Task A_file_with_an_image_header_but_broken_body_is_refused()
    {
        byte[] fake = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        await Should.ThrowAsync<AppException>(() => storage.SaveImageAsync(Upload(fake), "products"));
    }

    [Fact]
    public async Task A_file_over_two_megabytes_is_refused() =>
        await Should.ThrowAsync<AppException>(() => storage.SaveImageAsync(new UploadFile(new MemoryStream(), 3 * 1024 * 1024), "products"));

    [Fact]
    public void A_path_outside_the_store_is_refused() =>
        Should.Throw<AppException>(() => storage.Open("../../windows/win.ini"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
