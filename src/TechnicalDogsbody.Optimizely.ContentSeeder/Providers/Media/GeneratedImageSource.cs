using System.IO.Compression;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

/// <summary>
/// Draws a plain placeholder PNG in code: a light grey panel with its dimensions ("600 × 400") in the middle and a
/// small credit in the bottom right, set in Noto Sans. The glyph outlines are built in (see
/// <see cref="NotoSansOutlines"/>), so it needs no font file, no native graphics library and no network, and looks
/// the same on Windows and Linux.
/// </summary>
public sealed class GeneratedImageSource : IImageSource, IDescribesProvider
{
    private const int MaxDimension = 4096;
    private const string Credit = "Created using TechnicalDogsbody ContentSeeder";

    // Vertical sub-samples per pixel row; horizontal coverage is exact.
    private const int SubSamples = 4;

    private static readonly byte[] Background = [238, 238, 238];
    private static readonly byte[] DimensionsColour = [45, 48, 58];
    private static readonly byte[] CreditColour = [150, 152, 160];

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <inheritdoc/>
    public ProviderInfo Describe() => new("Generated placeholders", "Grey placeholder images drawn in code");

    /// <inheritdoc/>
    public Task<GeneratedImage?> GetImageAsync(ImageRequest request, CancellationToken ct = default)
    {
        int width = Math.Clamp(request.Width, 1, MaxDimension);
        int height = Math.Clamp(request.Height, 1, MaxDimension);
        return Task.FromResult<GeneratedImage?>(new GeneratedImage(RenderPng(width, height, request), ".png"));
    }

    private static byte[] RenderPng(int width, int height, ImageRequest request)
    {
        // Raw scanlines: one filter byte (0 = none) then RGB per pixel.
        int stride = 1 + width * 3;
        var raw = new byte[height * stride];
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                int i = row + 1 + x * 3;
                raw[i] = Background[0];
                raw[i + 1] = Background[1];
                raw[i + 2] = Background[2];
            }
        }

        // Dimensions, centred; digits are about 14.5% of the image height.
        string label = $"{request.Width} × {request.Height}";
        double labelUnits = TextWidth(NotoSansOutlines.Bold, label);
        double scale = Math.Min(height * 0.145 / NotoSansOutlines.CapHeight, width * 0.7 / labelUnits);
        if (scale * NotoSansOutlines.CapHeight >= 4)
        {
            double x0 = (width - labelUnits * scale) / 2;
            double baseline = (height + NotoSansOutlines.CapHeight * scale) / 2;
            DrawText(raw, width, height, NotoSansOutlines.Bold, label, x0, baseline, scale, DimensionsColour);
        }

        // Credit, bottom right, only where it fits comfortably.
        double creditScale = Math.Max(8, Math.Min(width, height) * 0.028) / NotoSansOutlines.CapHeight;
        double creditWidth = TextWidth(NotoSansOutlines.Regular, Credit) * creditScale;
        double margin = Math.Max(8, creditScale * 600);
        if (width >= 240 && height >= 120 && creditWidth <= width - margin * 2)
        {
            double x0 = width - margin - creditWidth;
            double baseline = height - margin - 200 * creditScale; // leaves room for descenders
            DrawText(raw, width, height, NotoSansOutlines.Regular, Credit, x0, baseline, creditScale, CreditColour);
        }

        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8; // bit depth
        header[9] = 2; // colour type: RGB
        WriteChunk(output, "IHDR", header);

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw);
            WriteChunk(output, "IDAT", compressed.ToArray());
        }

        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    // ── text drawing ─────────────────────────────────────────────────────────

    private static double TextWidth(IReadOnlyDictionary<char, NotoSansOutlines.Glyph> font, string text)
    {
        double total = 0;
        foreach (char c in text)
            total += font.TryGetValue(c, out var glyph) ? glyph.Advance : 0;
        return total;
    }

    /// <summary>Fills the glyph outlines (non-zero rule) onto the image, anti-aliased.</summary>
    private static void DrawText(byte[] raw, int width, int height, IReadOnlyDictionary<char, NotoSansOutlines.Glyph> font,
        string text, double originX, double baseline, double scale, byte[] colour)
    {
        // Edges in pixel space: (x0, y0, x1, y1), y down.
        var edges = new List<(double X0, double Y0, double X1, double Y1)>();
        double cursor = originX;
        foreach (char c in text)
        {
            if (!font.TryGetValue(c, out var glyph))
                continue;

            foreach (var contour in glyph.Contours)
            {
                int count = contour.Length / 2;
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;
                    edges.Add((cursor + contour[2 * i] * scale, baseline - contour[2 * i + 1] * scale,
                        cursor + contour[2 * j] * scale, baseline - contour[2 * j + 1] * scale));
                }
            }

            cursor += glyph.Advance * scale;
        }

        if (edges.Count == 0)
            return;

        int top = Math.Max(0, (int)Math.Floor(edges.Min(e => Math.Min(e.Y0, e.Y1))));
        int bottom = Math.Min(height - 1, (int)Math.Ceiling(edges.Max(e => Math.Max(e.Y0, e.Y1))));

        var coverage = new double[width];
        var crossings = new List<(double X, int Direction)>();
        for (int y = top; y <= bottom; y++)
        {
            Array.Clear(coverage);
            for (int s = 0; s < SubSamples; s++)
            {
                double sy = y + (s + 0.5) / SubSamples;
                crossings.Clear();
                foreach (var e in edges)
                {
                    if (e.Y0 == e.Y1) continue;
                    bool down = e.Y1 > e.Y0;
                    double yMin = down ? e.Y0 : e.Y1, yMax = down ? e.Y1 : e.Y0;
                    if (sy < yMin || sy >= yMax) continue;
                    double x = e.X0 + (sy - e.Y0) / (e.Y1 - e.Y0) * (e.X1 - e.X0);
                    crossings.Add((x, down ? 1 : -1));
                }

                crossings.Sort((a, b) => a.X.CompareTo(b.X));
                int winding = 0;
                for (int k = 0; k + 1 < crossings.Count; k++)
                {
                    winding += crossings[k].Direction;
                    if (winding != 0)
                        AddSpan(coverage, width, crossings[k].X, crossings[k + 1].X);
                }
            }

            int row = y * (1 + width * 3) + 1;
            for (int x = 0; x < width; x++)
            {
                int a = (int)Math.Min(255, coverage[x] * 255 / SubSamples + 0.5);
                if (a == 0) continue;

                int i = row + x * 3;
                for (int k = 0; k < 3; k++)
                    raw[i + k] = (byte)((raw[i + k] * (255 - a) + colour[k] * a + 127) / 255);
            }
        }
    }

    /// <summary>Adds the horizontal span [from, to) to a row of per-pixel coverage, with exact partial pixels.</summary>
    private static void AddSpan(double[] coverage, int width, double from, double to)
    {
        from = Math.Max(from, 0);
        to = Math.Min(to, width);
        if (to <= from) return;

        int first = (int)from, last = (int)to;
        if (first == last)
        {
            coverage[first] += to - from;
            return;
        }

        coverage[first] += first + 1 - from;
        for (int x = first + 1; x < last; x++)
            coverage[x] += 1;
        if (last < width)
            coverage[last] += to - last;
    }

    // ── PNG encoding ─────────────────────────────────────────────────────────

    private static void WriteBigEndian(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var number = new byte[4];
        WriteBigEndian(number, 0, (uint)data.Length);
        output.Write(number);

        var body = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) body[i] = (byte)type[i];
        data.CopyTo(body, 4);
        output.Write(body);

        WriteBigEndian(number, 0, Crc32(body));
        output.Write(number);
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
