// 一次性工具：把 logo.png 转成多尺寸 app.ico
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var src = args[0];
var dst = args[1];
int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };

using var original = new Bitmap(src);

// 先合成到正方形白底，避免非正方形被拉伸变形
int side = Math.Max(original.Width, original.Height);
using var square = new Bitmap(side, side, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(square))
{
    g.Clear(Color.Transparent);
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.DrawImage(original, (side - original.Width) / 2, (side - original.Height) / 2, original.Width, original.Height);
}

var pngs = new List<byte[]>();
foreach (var s in sizes)
{
    using var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);
        g.DrawImage(square, 0, 0, s, s);
    }
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    pngs.Add(ms.ToArray());
}

// 写 ICO 容器（PNG 压缩的 Vista+ 格式）
using var fs = new FileStream(dst, FileMode.Create);
using var bw = new BinaryWriter(fs);
bw.Write((ushort)0);          // reserved
bw.Write((ushort)1);          // type = icon
bw.Write((ushort)sizes.Length);

int offset = 6 + 16 * sizes.Length;
for (int i = 0; i < sizes.Length; i++)
{
    int s = sizes[i];
    bw.Write((byte)(s >= 256 ? 0 : s));   // width
    bw.Write((byte)(s >= 256 ? 0 : s));   // height
    bw.Write((byte)0);                    // palette
    bw.Write((byte)0);                    // reserved
    bw.Write((ushort)1);                  // planes
    bw.Write((ushort)32);                 // bpp
    bw.Write(pngs[i].Length);
    bw.Write(offset);
    offset += pngs[i].Length;
}
foreach (var png in pngs) bw.Write(png);

Console.WriteLine($"OK -> {dst}  sizes={string.Join(",", sizes)}  bytes={new FileInfo(dst).Length}");
