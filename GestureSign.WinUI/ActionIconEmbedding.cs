using System;
using System.IO;
using System.Threading.Tasks;
using GestureSign.Foundation.RadialMenus;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace GestureSign.WinUI;

/// <summary>
/// Turns a picture file into an icon the action carries itself: the bytes go into
/// the action as a data URI, so moving or deleting the file later leaves the icon
/// in place, and exporting or sharing the action takes the picture along.
/// </summary>
internal static class ActionIconEmbedding
{
    /// <summary>Pictures up to this size are embedded as they are, in their own format.</summary>
    private const int KeepAsIs = 64 * 1024;

    /// <summary>A slot draws at 22–48 px, so 128 px is already generous.</summary>
    private const int LongestEdge = 128;

    /// <summary>Anything bigger is not worth carrying around; the caller keeps the path instead.</summary>
    private const int Largest = 512 * 1024;

    /// <summary>The data URI for a picture, or null when it cannot be read or is too large.</summary>
    public static async Task<string?> ReadAsync(string path)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (bytes.Length == 0)
                return null;
            if (bytes.Length > KeepAsIs)
                bytes = await ScaleAsync(path) ?? bytes;
            if (bytes.Length > Largest)
                return null;
            return ActionIcons.EncodeInlineImage(bytes, path);
        }
        catch (Exception)
        {
            // Unreadable or unsupported: the caller keeps the path, which still works
            // for as long as the file exists.
            return null;
        }
    }

    /// <summary>Re-encodes a large picture as a small PNG with its aspect ratio kept.</summary>
    private static async Task<byte[]?> ScaleAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var input = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(input);
            var longest = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
            if (longest == 0)
                return null;

            var scale = Math.Min(1d, (double)LongestEdge / longest);
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
                ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
                InterpolationMode = BitmapInterpolationMode.Fant
            };
            using var pixels = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);

            var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(pixels);
            await encoder.FlushAsync();

            output.Seek(0);
            var reader = new DataReader(output.GetInputStreamAt(0));
            await reader.LoadAsync((uint)output.Size);
            var bytes = new byte[output.Size];
            reader.ReadBytes(bytes);
            reader.Dispose();
            return bytes;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
