using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: GenerateIcon <source.png> <output.ico>");
    return 1;
}

var sourcePath = Path.GetFullPath(args[0]);
var icoPath = Path.GetFullPath(args[1]);
int[] sizes = [16, 32, 48, 64, 128, 256];

using var source = new Bitmap(sourcePath);
var pngImages = new List<byte[]>(sizes.Length);

foreach (var size in sizes)
{
    using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.Clear(Color.Transparent);

        const float padding = 0.08f;
        float available = size * (1f - padding * 2f);
        float scale = Math.Min(available / source.Width, available / source.Height);
        int w = Math.Max(1, (int)Math.Round(source.Width * scale));
        int h = Math.Max(1, (int)Math.Round(source.Height * scale));
        int x = (size - w) / 2;
        int y = (size - h) / 2;
        g.DrawImage(source, x, y, w, h);
    }

    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    pngImages.Add(ms.ToArray());
}

WriteIco(icoPath, sizes, pngImages);
Console.WriteLine($"Created {icoPath}");
return 0;

static void WriteIco(string path, int[] sizes, List<byte[]> pngImages)
{
    using var fs = File.Create(path);
    using var bw = new BinaryWriter(fs);

    bw.Write((ushort)0);
    bw.Write((ushort)1);
    bw.Write((ushort)pngImages.Count);

    int offset = 6 + pngImages.Count * 16;
    var entries = new List<(byte width, byte height, int length, int offset)>();

    for (int i = 0; i < pngImages.Count; i++)
    {
        int size = sizes[i];
        entries.Add((
            (byte)(size >= 256 ? 0 : size),
            (byte)(size >= 256 ? 0 : size),
            pngImages[i].Length,
            offset));
        offset += pngImages[i].Length;
    }

    foreach (var entry in entries)
    {
        bw.Write(entry.width);
        bw.Write(entry.height);
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(entry.length);
        bw.Write(entry.offset);
    }

    foreach (var png in pngImages)
        bw.Write(png);
}