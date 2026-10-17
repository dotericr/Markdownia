using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;

// Usage: IconGen [repo-root]
// Draws the Markdownia icon (a page with the Markdown mark on a blue tile) and writes:
//   packaging/linux/icons/hicolor/<size>/apps/markdownia.png and scalable/apps/markdownia.svg
//   src/Markdownia/Assets/Icon/markdownia.png (window icon) and src/Markdownia/markdownia.ico (Windows)
// The SVG and the bitmaps are produced from the same path data, so they always match.

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
AppBuilder.Configure<Application>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

// Design on a 256-unit grid.
const string GradientStart = "#4F86F7", GradientEnd = "#2443B5", Fold = "#C7D5F7", Shadow = "#0B1E5B";
const string Page = "M76,40 H158 L198,80 V200 Q198,216 182,216 H76 Q60,216 60,200 V56 Q60,40 76,40 Z";
const string Corner = "M158,40 V66 Q158,80 172,80 H198 Z";
const string Mark = "M82,166 V110 L104,138 L126,110 V166 M166,108 V164 M148,146 L166,166 L184,146";
const double MarkStroke = 15;

void Draw(DrawingContext ctx)
{
    var tile = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse(GradientStart), 0), new GradientStop(Color.Parse(GradientEnd), 1) },
    };
    ctx.DrawRectangle(tile, null, new RoundedRect(new Rect(16, 16, 224, 224), 52));
    using (ctx.PushTransform(Matrix.CreateTranslation(0, 6)))
        ctx.DrawGeometry(new SolidColorBrush(Color.Parse(Shadow), 0.25), null, Geometry.Parse(Page));
    ctx.DrawGeometry(Brushes.White, null, Geometry.Parse(Page));
    ctx.DrawGeometry(new SolidColorBrush(Color.Parse(Fold)), null, Geometry.Parse(Corner));
    var pen = new Pen(new SolidColorBrush(Color.Parse(GradientEnd)), MarkStroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    ctx.DrawGeometry(null, pen, Geometry.Parse(Mark));
}

byte[] Png(int size)
{
    using var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
    using (var ctx = bitmap.CreateDrawingContext())
    using (ctx.PushTransform(Matrix.CreateScale(size / 256.0, size / 256.0)))
        Draw(ctx);
    using var stream = new MemoryStream();
#pragma warning disable CS0618 // The replacement overload's options type is abstract; the PNG default is what we want.
    bitmap.Save(stream);
#pragma warning restore CS0618
    return stream.ToArray();
}

var svg = $"""
    <svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
      <defs>
        <linearGradient id="tile" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="{GradientStart}"/>
          <stop offset="1" stop-color="{GradientEnd}"/>
        </linearGradient>
      </defs>
      <rect x="16" y="16" width="224" height="224" rx="52" fill="url(#tile)"/>
      <path d="{Page}" fill="{Shadow}" fill-opacity="0.25" transform="translate(0 6)"/>
      <path d="{Page}" fill="#FFFFFF"/>
      <path d="{Corner}" fill="{Fold}"/>
      <path d="{Mark}" fill="none" stroke="{GradientEnd}" stroke-width="{MarkStroke}" stroke-linecap="round" stroke-linejoin="round"/>
    </svg>

    """;

var hicolor = Path.Combine(root, "packaging", "linux", "icons", "hicolor");
Write(Path.Combine(hicolor, "scalable", "apps", "markdownia.svg"), System.Text.Encoding.UTF8.GetBytes(svg));
foreach (var size in new[] { 16, 22, 24, 32, 48, 64, 128, 256, 512 })
    Write(Path.Combine(hicolor, $"{size}x{size}", "apps", "markdownia.png"), Png(size));
Write(Path.Combine(root, "src", "Markdownia", "Assets", "Icon", "markdownia.png"), Png(256));
Write(Path.Combine(root, "src", "Markdownia", "markdownia.ico"), Ico([16, 24, 32, 48, 64, 128, 256]));

// An .ico file holding PNG images (supported since Windows Vista).
byte[] Ico(int[] sizes)
{
    var images = sizes.Select(Png).ToArray();
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write((short)0);
    writer.Write((short)1);
    writer.Write((short)sizes.Length);
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)
    {
        writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(images[i].Length);
        writer.Write(offset);
        offset += images[i].Length;
    }
    foreach (var image in images)
        writer.Write(image);
    return stream.ToArray();
}

static void Write(string path, byte[] data)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, data);
    Console.WriteLine($"wrote {path}");
}
