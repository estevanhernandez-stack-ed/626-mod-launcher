using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ModManager.App.Services;

/// <summary>
/// Manages the user's avatar PNG + derived .ico under %APPDATA%\ModManagerBuilder\profile\. Uses
/// built-in WinRT BitmapDecoder for decode (no System.Drawing dep). The .ico writer wraps a PNG
/// payload — modern Windows reads PNG-in-ICO directly, no BMP conversion needed.
/// </summary>
public sealed class AvatarService
{
    public string ProfileDir { get; }
    public string AvatarPngPath { get; }
    public string AvatarIcoPath { get; }

    public AvatarService()
    {
        ProfileDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModManagerBuilder", "profile");
        AvatarPngPath = Path.Combine(ProfileDir, "avatar.png");
        AvatarIcoPath = Path.Combine(ProfileDir, "avatar.ico");
    }

    public bool HasAvatar => File.Exists(AvatarPngPath);

    /// <summary>Decode a source image (any format BitmapDecoder supports), re-encode as a square
    /// 256×256 PNG, and persist as the avatar. Also generates a matching 64×64 .ico.</summary>
    public async Task ImportAsync(string sourceImagePath)
    {
        Directory.CreateDirectory(ProfileDir);

        // Decode source → resized RGBA pixel buffer (256×256, square-cropped center).
        var (png256, _) = await ResizeToSquareAsync(sourceImagePath, 256);
        File.WriteAllBytes(AvatarPngPath, png256);

        // Re-encode again at 64×64 for the .ico payload.
        var (png64, _) = await ResizeToSquareAsync(sourceImagePath, 64);
        WritePngInIco(AvatarIcoPath, png64, 64);
    }

    public void Delete()
    {
        if (File.Exists(AvatarPngPath)) File.Delete(AvatarPngPath);
        if (File.Exists(AvatarIcoPath)) File.Delete(AvatarIcoPath);
    }

    /// <summary>Decode → resize to square → encode PNG. Returns (pngBytes, rgbaPixels). Public so the
    /// dialog can sample pixels for palette extraction without a second file read.</summary>
    public static async Task<(byte[] PngBytes, byte[] RgbaPixels)> ResizeToSquareAsync(string path, uint side)
    {
        using var src = File.OpenRead(path);
        var randomAccess = src.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccess);
        var transform = new BitmapTransform { ScaledWidth = side, ScaledHeight = side, InterpolationMode = BitmapInterpolationMode.Fant };
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var rgba = pixels.DetachPixelData();

        var ms = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ms);
        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied, side, side, 96, 96, rgba);
        await encoder.FlushAsync();
        ms.Seek(0);
        var pngBytes = new byte[ms.Size];
        await ms.ReadAsync(pngBytes.AsBuffer(), (uint)ms.Size, InputStreamOptions.None);
        return (pngBytes, rgba);
    }

    /// <summary>Write a PNG-wrapping .ico file. Header (6 bytes) + 1 directory entry (16 bytes) +
    /// the PNG payload. Width/height stored as 0 mean "256 or larger" — we use the literal byte
    /// for sub-256 sizes.</summary>
    private static void WritePngInIco(string path, byte[] pngBytes, int side)
    {
        using var fs = File.Create(path);
        // ICONDIR
        fs.Write(new byte[] { 0, 0, 1, 0, 1, 0 }); // reserved, type=1 icon, count=1
        // ICONDIRENTRY (16 bytes)
        var sideByte = side >= 256 ? (byte)0 : (byte)side;
        fs.WriteByte(sideByte); // width
        fs.WriteByte(sideByte); // height
        fs.WriteByte(0);        // color count (0 for >256 colors)
        fs.WriteByte(0);        // reserved
        fs.Write(BitConverter.GetBytes((ushort)1));    // color planes
        fs.Write(BitConverter.GetBytes((ushort)32));   // bits per pixel
        fs.Write(BitConverter.GetBytes((uint)pngBytes.Length)); // image size
        fs.Write(BitConverter.GetBytes((uint)22));     // offset (6 + 16)
        fs.Write(pngBytes);
    }
}
