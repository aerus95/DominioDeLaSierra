using DominioDeLaSierra.Infrastructure.Media;
using DominioDeLaSierra.Infrastructure.Products;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace DominioDeLaSierra.IntegrationTests;

public class ImageProcessingTests
{
    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task Accepts_a_square_image_and_returns_a_1200_webp(string format)
    {
        var prepared = await PrepareAsync(Encode(800, 800, format));

        Assert.True(prepared.Length >= 12);
        Assert.Equal((byte)'W', prepared[8]);
        Assert.Equal((byte)'E', prepared[9]);
        Assert.Equal((byte)'B', prepared[10]);
        Assert.Equal((byte)'P', prepared[11]);
        var info = Image.Identify(prepared);
        Assert.Equal(ProductMediaOptions.CanvasEdge, info.Width);
        Assert.Equal(ProductMediaOptions.CanvasEdge, info.Height);
    }

    [Fact]
    public async Task Rejects_a_fake_jpeg()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            PrepareAsync(new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x11 }));

        Assert.Equal("La imagen no es válida.", exception.Message);
    }

    [Fact]
    public async Task Rejects_an_image_smaller_than_800()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            PrepareAsync(Encode(799, 799, "png")));

        Assert.Equal("La imagen debe medir al menos 800×800 px.", exception.Message);
    }

    [Fact]
    public async Task Rejects_an_image_with_more_than_16_million_pixels()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            PrepareAsync(Encode(4001, 4001, "jpeg")));

        Assert.Equal("La imagen tiene demasiados píxeles.", exception.Message);
    }

    [Fact]
    public async Task Rejects_an_animated_webp()
    {
        using var image = new Image<Rgba32>(800, 800, Color.Red);
        using var second = new Image<Rgba32>(800, 800, Color.Blue);
        image.Frames.AddFrame(second.Frames.RootFrame);
        using var stream = new MemoryStream();
        image.Save(stream, new WebpEncoder());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => PrepareAsync(stream.ToArray()));

        Assert.Equal("Las imágenes animadas no están permitidas.", exception.Message);
    }

    [Fact]
    public async Task Writes_the_webp_only_inside_the_temporary_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dominio-sierra-tests", "images-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "salida.webp");
        try
        {
            var prepared = await PrepareAsync(Encode(800, 800, "png"));
            await ProductImageContent.WriteAsync(path, prepared, CancellationToken.None);

            Assert.True(File.Exists(path));
            Assert.Empty(Directory.GetFiles(directory).Except([path]));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        Assert.False(Directory.Exists(directory));
    }

    private static async Task<byte[]> PrepareAsync(byte[] bytes)
    {
        await using var stream = new MemoryStream(bytes);
        return await ProductImageContent.PrepareAsync(stream, bytes.Length, CancellationToken.None);
    }

    private static byte[] Encode(int width, int height, string format)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(90, 30, 36));
        using var stream = new MemoryStream();
        switch (format)
        {
            case "jpeg":
                image.Save(stream, new JpegEncoder { Quality = 30 });
                break;
            case "webp":
                image.Save(stream, new WebpEncoder { Quality = 30 });
                break;
            default:
                image.Save(stream, new PngEncoder());
                break;
        }

        return stream.ToArray();
    }
}
