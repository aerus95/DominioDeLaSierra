using DominioDeLaSierra.Infrastructure.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DominioDeLaSierra.Infrastructure.Products;

internal static class ProductImageContent
{
    public const string StorageFailureMessage = "No se ha podido guardar la imagen.";

    public static async Task<byte[]> PrepareAsync(Stream content, long length, CancellationToken cancellationToken)
    {
        var bytes = await ReadContentAsync(content, length, cancellationToken);
        EnsureSupportedFormat(bytes);
        return Normalize(bytes);
    }

    public static async Task WriteAsync(string fullPath, byte[] bytes, CancellationToken cancellationToken)
    {
        try
        {
            await using var file = new FileStream(
                fullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            await file.WriteAsync(bytes, cancellationToken);
        }
        catch
        {
            ManagedProductImageFiles.DeleteIfExists(fullPath);
            throw;
        }
    }

    private static byte[] Normalize(byte[] bytes)
    {
        var info = ReadImage(() => Image.Identify(bytes) ?? throw new ArgumentException("La imagen no es válida."));

        if (info.Width <= 0 || info.Height <= 0)
        {
            throw new ArgumentException("La imagen no es válida.");
        }

        if (info.FrameMetadataCollection.Count > 1)
        {
            throw new ArgumentException("Las imágenes animadas no están permitidas.");
        }

        if ((long)info.Width * info.Height > ProductMediaOptions.MaxImagePixels)
        {
            throw new ArgumentException("La imagen tiene demasiados píxeles.");
        }

        return ReadImage(() =>
        {
            using var image = Image.Load<Rgba32>(bytes);
            image.Mutate(operation => operation.AutoOrient());
            if (image.Width < ProductMediaOptions.MinEdge || image.Height < ProductMediaOptions.MinEdge)
            {
                throw new ArgumentException("La imagen debe medir al menos 800×800 px.");
            }

            image.Mutate(operation => operation.Resize(new ResizeOptions
            {
                Size = new Size(ProductMediaOptions.CanvasEdge, ProductMediaOptions.CanvasEdge),
                Mode = ResizeMode.Pad,
                PadColor = Color.Transparent,
                Sampler = KnownResamplers.Lanczos3,
                Position = AnchorPositionMode.Center
            }));

            using var output = new MemoryStream();
            image.Save(output, new WebpEncoder
            {
                Quality = ProductMediaOptions.WebpQuality,
                FileFormat = WebpFileFormatType.Lossy
            });
            return output.ToArray();
        });
    }

    private static T ReadImage<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (ArgumentException exception) when (exception is not ArgumentOutOfRangeException)
        {
            throw;
        }
        catch (Exception exception) when (IsCorruptImageData(exception))
        {
            throw new ArgumentException("La imagen no es válida.");
        }
    }

    private static bool IsCorruptImageData(Exception exception) =>
        exception is ImageFormatException
            or ImageProcessingException
            or NotSupportedException
            or InvalidOperationException
            or FormatException
            or EndOfStreamException
            or IOException
            or ArgumentOutOfRangeException;

    private static async Task<byte[]> ReadContentAsync(Stream content, long length, CancellationToken cancellationToken)
    {
        if (length <= 0)
        {
            throw new ArgumentException("La imagen es obligatoria.");
        }

        if (length > ProductMediaOptions.MaxImageBytes)
        {
            throw new ArgumentException("La imagen no puede superar 5 MB.");
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        long total = 0;
        while (true)
        {
            var read = await content.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > ProductMediaOptions.MaxImageBytes)
            {
                throw new ArgumentException("La imagen no puede superar 5 MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        if (total == 0)
        {
            throw new ArgumentException("La imagen es obligatoria.");
        }

        return buffer.ToArray();
    }

    private static void EnsureSupportedFormat(ReadOnlySpan<byte> content)
    {
        if (IsPng(content) || IsWebp(content) || IsJpeg(content))
        {
            return;
        }

        throw new ArgumentException("La imagen debe ser JPEG, PNG o WEBP.");
    }

    private static bool IsJpeg(ReadOnlySpan<byte> content) =>
        content.Length >= 3
        && content[0] == 0xFF
        && content[1] == 0xD8
        && content[2] == 0xFF;

    private static bool IsPng(ReadOnlySpan<byte> content) =>
        content.Length >= 8
        && content[0] == 0x89
        && content[1] == 0x50
        && content[2] == 0x4E
        && content[3] == 0x47
        && content[4] == 0x0D
        && content[5] == 0x0A
        && content[6] == 0x1A
        && content[7] == 0x0A;

    private static bool IsWebp(ReadOnlySpan<byte> content) =>
        content.Length >= 12
        && content[0] == (byte)'R'
        && content[1] == (byte)'I'
        && content[2] == (byte)'F'
        && content[3] == (byte)'F'
        && content[8] == (byte)'W'
        && content[9] == (byte)'E'
        && content[10] == (byte)'B'
        && content[11] == (byte)'P';
}
