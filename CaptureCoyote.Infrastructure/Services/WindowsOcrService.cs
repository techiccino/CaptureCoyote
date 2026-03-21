using CaptureCoyote.Services.Abstractions;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Infrastructure.Helpers;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Int32Rect = System.Windows.Int32Rect;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class WindowsOcrService : IOcrService
{
    private enum OcrPreparationMode
    {
        Standard,
        HighContrast
    }

    public async Task<string> ExtractTextAsync(
        byte[] pngBytes,
        string? preferredLanguageTag = null,
        CancellationToken cancellationToken = default)
    {
        if (pngBytes.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            var engine = CreateEngine(preferredLanguageTag);
            if (engine is null)
            {
                return string.Empty;
            }

            var source = ImageHelper.ToBitmapSource(pngBytes);
            var primaryText = await RecognizeBitmapSourceAsync(engine, source, preferredLongestEdge: 2600, cancellationToken).ConfigureAwait(false);
            var bestText = primaryText;

            if (NeedsFocusedTextFallback(bestText, source))
            {
                try
                {
                    var focusedText = await RecognizeFocusedTextRegionAsync(engine, source, cancellationToken).ConfigureAwait(false);
                    bestText = ChooseBest(bestText, focusedText);
                }
                catch
                {
                }
            }

            if (ShouldTryDocumentPass(source))
            {
                try
                {
                    var documentText = await RecognizeDocumentSlicesAsync(engine, source, cancellationToken).ConfigureAwait(false);
                    bestText = ChooseBest(bestText, documentText);
                }
                catch
                {
                }
            }

            if (NeedsGenericTileFallback(bestText, source))
            {
                try
                {
                    var tileText = await RecognizeGenericTilesAsync(engine, source, cancellationToken).ConfigureAwait(false);
                    bestText = ChooseBest(bestText, tileText);
                }
                catch
                {
                }
            }

            if (NeedsAggressiveTextFallback(bestText, source))
            {
                try
                {
                    var aggressiveText = await RecognizeAggressiveTextTilesAsync(engine, source, cancellationToken).ConfigureAwait(false);
                    bestText = ChooseBest(bestText, aggressiveText);
                }
                catch
                {
                }
            }

            return bestText;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static OcrEngine? CreateEngine(string? preferredLanguageTag)
    {
        if (!string.IsNullOrWhiteSpace(preferredLanguageTag))
        {
            try
            {
                var language = new Language(preferredLanguageTag);
                var languageEngine = OcrEngine.TryCreateFromLanguage(language);
                if (languageEngine is not null)
                {
                    return languageEngine;
                }
            }
            catch
            {
            }
        }

        return OcrEngine.TryCreateFromUserProfileLanguages();
    }

    private static string Normalize(OcrResult result)
    {
        if (result.Lines.Count == 0)
        {
            return string.Empty;
        }

        var orderedLines = result.Lines
            .OrderBy(line => GetLineBounds(line).Y)
            .ThenBy(line => GetLineBounds(line).X)
            .ToList();

        var outputLines = new List<string>();
        Rect? previousRect = null;

        foreach (var line in orderedLines)
        {
            var lineBounds = GetLineBounds(line);
            var text = BuildLine(line);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (previousRect is not null)
            {
                var gap = lineBounds.Y - (previousRect.Value.Y + previousRect.Value.Height);
                var threshold = Math.Max(previousRect.Value.Height, lineBounds.Height) * 0.9;
                if (gap > threshold)
                {
                    outputLines.Add(string.Empty);
                }
            }

            if (outputLines.Count > 0 && ShouldMergeWithPrevious(outputLines[^1], text))
            {
                outputLines[^1] += text.TrimStart();
            }
            else
            {
                outputLines.Add(text);
            }

            previousRect = lineBounds;
        }

        return string.Join(
            Environment.NewLine,
            outputLines
                .Where((line, index) => index == 0 || !string.IsNullOrWhiteSpace(line) || !string.IsNullOrWhiteSpace(outputLines[index - 1]))
                .Select(line => line.TrimEnd()));
    }

    private static string BuildLine(OcrLine line)
    {
        var words = line.Words
            .OrderBy(word => word.BoundingRect.X)
            .ToList();

        if (words.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        OcrWord? previousWord = null;

        foreach (var word in words)
        {
            if (previousWord is not null)
            {
                var gap = word.BoundingRect.X - (previousWord.BoundingRect.X + previousWord.BoundingRect.Width);
                var averageCharWidth = previousWord.BoundingRect.Width / Math.Max(1, previousWord.Text.Length);

                if (gap > averageCharWidth * 6)
                {
                    builder.Append("    ");
                }
                else if (gap > averageCharWidth * 3.2)
                {
                    builder.Append("  ");
                }
                else
                {
                    builder.Append(' ');
                }
            }

            builder.Append(word.Text);
            previousWord = word;
        }

        return CleanupInlineText(builder.ToString());
    }

    private static bool ShouldMergeWithPrevious(string previous, string current)
    {
        if (string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(current))
        {
            return false;
        }

        var previousLast = previous.TrimEnd().LastOrDefault();
        var currentFirst = current.TrimStart().FirstOrDefault();

        if (!char.IsLetter(previousLast) || !char.IsLower(currentFirst))
        {
            return false;
        }

        return previousLast is not '.' and not ':' and not ';' and not '?' and not '!';
    }

    private static string CleanupInlineText(string text)
    {
        return text
            .Replace(" | ", "  ")
            .Replace("  |", "  ")
            .Replace("|  ", "  ")
            .Trim();
    }

    private static async Task<string> RecognizeBitmapSourceAsync(
        OcrEngine engine,
        BitmapSource source,
        double preferredLongestEdge,
        CancellationToken cancellationToken)
    {
        return await RecognizeBitmapSourceAsync(
                engine,
                source,
                preferredLongestEdge,
                OcrPreparationMode.Standard,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<string> RecognizeBitmapSourceAsync(
        OcrEngine engine,
        BitmapSource source,
        double preferredLongestEdge,
        OcrPreparationMode preparationMode,
        CancellationToken cancellationToken)
    {
        var preparedBytes = PrepareForOcr(source, preferredLongestEdge, preparationMode);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(preparedBytes);
            await writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
        }

        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
        using var bitmap = await decoder
            .GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
        return Normalize(result);
    }

    private static async Task<string> RecognizeDocumentSlicesAsync(
        OcrEngine engine,
        BitmapSource source,
        CancellationToken cancellationToken)
    {
        var sections = new List<string>();
        foreach (var slice in BuildDocumentSlices(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await RecognizeBitmapSourceAsync(engine, slice, preferredLongestEdge: 3400, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(text))
            {
                sections.Add(text);
            }
        }

        return CombineSections(sections);
    }

    private static async Task<string> RecognizeGenericTilesAsync(
        OcrEngine engine,
        BitmapSource source,
        CancellationToken cancellationToken)
    {
        var sections = new List<string>();
        foreach (var slice in BuildGenericTiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await RecognizeBitmapSourceAsync(engine, slice, preferredLongestEdge: 3600, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(text))
            {
                sections.Add(text);
            }
        }

        return CombineSections(sections);
    }

    private static async Task<string> RecognizeAggressiveTextTilesAsync(
        OcrEngine engine,
        BitmapSource source,
        CancellationToken cancellationToken)
    {
        var sections = new List<string>();
        foreach (var slice in BuildAggressiveTextTiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await RecognizeBitmapSourceAsync(
                    engine,
                    slice,
                    preferredLongestEdge: 5200,
                    OcrPreparationMode.HighContrast,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(text))
            {
                sections.Add(text);
            }
        }

        return CombineSections(sections);
    }

    private static async Task<string> RecognizeFocusedTextRegionAsync(
        OcrEngine engine,
        BitmapSource source,
        CancellationToken cancellationToken)
    {
        if (!TryFindFocusedTextRegion(source, out var region))
        {
            return string.Empty;
        }

        var working = EnsureBgra32(source);
        var focusedRegion = Crop(working, region);
        var standard = await RecognizeBitmapSourceAsync(engine, focusedRegion, preferredLongestEdge: 3400, cancellationToken).ConfigureAwait(false);
        var highContrast = await RecognizeBitmapSourceAsync(
                engine,
                focusedRegion,
                preferredLongestEdge: 3800,
                OcrPreparationMode.HighContrast,
                cancellationToken)
            .ConfigureAwait(false);

        return ChooseBest(standard, highContrast);
    }

    private static IEnumerable<BitmapSource> BuildDocumentSlices(BitmapSource source)
    {
        var analysisSource = EnsureBgra32(source);
        var pixelBuffer = CopyPixels(analysisSource, out var stride);
        var contentRegion = FindDocumentContentRegion(pixelBuffer, stride, analysisSource.PixelWidth, analysisSource.PixelHeight);
        var pageColumns = FindPageColumns(pixelBuffer, stride, analysisSource.PixelWidth, analysisSource.PixelHeight, contentRegion);
        var tileRegions = new List<Int32Rect>();

        if (pageColumns.Count > 0)
        {
            foreach (var column in pageColumns)
            {
                tileRegions.AddRange(BuildTileRegions(column, maxTileHeight: 1450, tileOverlap: 140));
            }
        }
        else if (analysisSource.PixelWidth > analysisSource.PixelHeight * 1.08)
        {
            var halfWidth = Math.Max(1, contentRegion.Width / 2);
            var gutterOverlap = Math.Min(90, Math.Max(30, halfWidth / 10));
            var leftRegion = NormalizeCrop(
                new Int32Rect(contentRegion.X, contentRegion.Y, Math.Min(contentRegion.Width, halfWidth + gutterOverlap), contentRegion.Height),
                analysisSource.PixelWidth,
                analysisSource.PixelHeight);
            var rightRegion = NormalizeCrop(
                new Int32Rect(
                    Math.Max(contentRegion.X, contentRegion.X + contentRegion.Width - halfWidth - gutterOverlap),
                    contentRegion.Y,
                    Math.Min(contentRegion.Width, halfWidth + gutterOverlap),
                    contentRegion.Height),
                analysisSource.PixelWidth,
                analysisSource.PixelHeight);

            tileRegions.AddRange(BuildTileRegions(leftRegion, maxTileHeight: 1450, tileOverlap: 140));
            tileRegions.AddRange(BuildTileRegions(rightRegion, maxTileHeight: 1450, tileOverlap: 140));
        }
        else
        {
            tileRegions.AddRange(BuildTileRegions(contentRegion, maxTileHeight: 1500, tileOverlap: 140));
        }

        foreach (var tileRegion in tileRegions
                     .OrderBy(region => region.Y)
                     .ThenBy(region => region.X))
        {
            yield return Crop(analysisSource, tileRegion);
        }
    }

    private static IEnumerable<BitmapSource> BuildGenericTiles(BitmapSource source)
    {
        var analysisSource = EnsureBgra32(source);
        var marginX = Math.Min(36, Math.Max(0, (int)Math.Round(analysisSource.PixelWidth * 0.015)));
        var marginY = Math.Min(28, Math.Max(0, (int)Math.Round(analysisSource.PixelHeight * 0.01)));
        var contentRegion = NormalizeCrop(
            new Int32Rect(
                marginX,
                marginY,
                Math.Max(1, analysisSource.PixelWidth - (marginX * 2)),
                Math.Max(1, analysisSource.PixelHeight - (marginY * 2))),
            analysisSource.PixelWidth,
            analysisSource.PixelHeight);

        var tileRegions = new List<Int32Rect>();
        if (contentRegion.Width > contentRegion.Height * 1.05)
        {
            var halfWidth = Math.Max(1, contentRegion.Width / 2);
            var overlap = Math.Min(120, Math.Max(30, halfWidth / 8));
            var leftRegion = NormalizeCrop(
                new Int32Rect(contentRegion.X, contentRegion.Y, Math.Min(contentRegion.Width, halfWidth + overlap), contentRegion.Height),
                analysisSource.PixelWidth,
                analysisSource.PixelHeight);
            var rightRegion = NormalizeCrop(
                new Int32Rect(
                    Math.Max(contentRegion.X, contentRegion.X + contentRegion.Width - halfWidth - overlap),
                    contentRegion.Y,
                    Math.Min(contentRegion.Width, halfWidth + overlap),
                    contentRegion.Height),
                analysisSource.PixelWidth,
                analysisSource.PixelHeight);

            tileRegions.AddRange(BuildTileRegions(leftRegion, maxTileHeight: 1350, tileOverlap: 120));
            tileRegions.AddRange(BuildTileRegions(rightRegion, maxTileHeight: 1350, tileOverlap: 120));
        }
        else
        {
            tileRegions.AddRange(BuildTileRegions(contentRegion, maxTileHeight: 1350, tileOverlap: 120));
        }

        foreach (var region in tileRegions
                     .OrderBy(tile => tile.Y)
                     .ThenBy(tile => tile.X))
        {
            yield return Crop(analysisSource, region);
        }
    }

    private static IEnumerable<BitmapSource> BuildAggressiveTextTiles(BitmapSource source)
    {
        var analysisSource = EnsureBgra32(source);
        var marginX = Math.Min(28, Math.Max(0, (int)Math.Round(analysisSource.PixelWidth * 0.012)));
        var marginY = Math.Min(20, Math.Max(0, (int)Math.Round(analysisSource.PixelHeight * 0.008)));
        var contentRegion = NormalizeCrop(
            new Int32Rect(
                marginX,
                marginY,
                Math.Max(1, analysisSource.PixelWidth - (marginX * 2)),
                Math.Max(1, analysisSource.PixelHeight - (marginY * 2))),
            analysisSource.PixelWidth,
            analysisSource.PixelHeight);

        var tileRegions = new List<Int32Rect>();
        if (contentRegion.Width > contentRegion.Height * 1.05)
        {
            var halfWidth = Math.Max(1, contentRegion.Width / 2);
            var overlap = Math.Min(90, Math.Max(24, halfWidth / 10));
            var leftRegion = NormalizeCrop(
                new Int32Rect(contentRegion.X, contentRegion.Y, Math.Min(contentRegion.Width, halfWidth + overlap), contentRegion.Height),
                analysisSource.PixelWidth,
                analysisSource.PixelHeight);
            var rightRegion = NormalizeCrop(
                new Int32Rect(
                    Math.Max(contentRegion.X, contentRegion.X + contentRegion.Width - halfWidth - overlap),
                    contentRegion.Y,
                    Math.Min(contentRegion.Width, halfWidth + overlap),
                    contentRegion.Height),
                analysisSource.PixelWidth,
                analysisSource.PixelHeight);

            tileRegions.AddRange(BuildTileRegions(leftRegion, maxTileHeight: 960, tileOverlap: 80));
            tileRegions.AddRange(BuildTileRegions(rightRegion, maxTileHeight: 960, tileOverlap: 80));
        }
        else
        {
            tileRegions.AddRange(BuildTileRegions(contentRegion, maxTileHeight: 960, tileOverlap: 80));
        }

        foreach (var region in tileRegions
                     .OrderBy(tile => tile.Y)
                     .ThenBy(tile => tile.X))
        {
            yield return Crop(analysisSource, region);
        }
    }

    private static IEnumerable<Int32Rect> BuildTileRegions(Int32Rect region, int maxTileHeight, int tileOverlap)
    {
        if (region.Height <= maxTileHeight)
        {
            yield return region;
            yield break;
        }

        var y = region.Y;
        while (y < region.Y + region.Height)
        {
            var remaining = (region.Y + region.Height) - y;
            var height = Math.Min(maxTileHeight, remaining);
            yield return new Int32Rect(region.X, y, region.Width, height);

            if (y + height >= region.Y + region.Height)
            {
                break;
            }

            y += maxTileHeight - tileOverlap;
        }
    }

    private static Int32Rect FindDocumentContentRegion(byte[] pixels, int stride, int width, int height)
    {
        var leftTrim = Math.Min(80, Math.Max(0, (int)Math.Round(width * 0.035)));
        var rightTrim = leftTrim;
        var rowRatios = MeasureRowPageRatios(pixels, stride, width, height, leftTrim, width - rightTrim);
        var top = FindLeadingActiveIndex(rowRatios, threshold: 0.16, requiredRun: 18);
        var bottom = FindTrailingActiveIndex(rowRatios, threshold: 0.16, requiredRun: 18);

        if (bottom <= top)
        {
            var fallbackTopTrim = Math.Min(220, Math.Max(0, (int)Math.Round(height * 0.12)));
            var fallbackBottomTrim = Math.Min(120, Math.Max(0, (int)Math.Round(height * 0.05)));
            return NormalizeCrop(
                new Int32Rect(leftTrim, fallbackTopTrim, Math.Max(1, width - (leftTrim + rightTrim)), Math.Max(1, height - fallbackTopTrim - fallbackBottomTrim)),
                width,
                height);
        }

        top = Math.Max(0, top - 20);
        bottom = Math.Min(height - 1, bottom + 18);

        return NormalizeCrop(
            new Int32Rect(leftTrim, top, Math.Max(1, width - (leftTrim + rightTrim)), Math.Max(1, bottom - top + 1)),
            width,
            height);
    }

    private static List<Int32Rect> FindPageColumns(byte[] pixels, int stride, int width, int height, Int32Rect contentRegion)
    {
        var columnRatios = MeasureColumnPageRatios(pixels, stride, width, height, contentRegion);
        var segments = FindActiveSegments(
            columnRatios,
            contentRegion.X,
            threshold: 0.44,
            minimumLength: Math.Max(220, (int)Math.Round(contentRegion.Width * 0.12)),
            maximumGap: Math.Max(18, (int)Math.Round(contentRegion.Width * 0.015)));

        if (segments.Count == 0)
        {
            return [];
        }

        var paddedSegments = segments
            .OrderByDescending(segment => segment.Length)
            .Take(3)
            .Select(segment =>
            {
                var padding = Math.Min(24, Math.Max(8, segment.Length / 18));
                return NormalizeCrop(
                    new Int32Rect(segment.Start - padding, contentRegion.Y, segment.Length + (padding * 2), contentRegion.Height),
                    width,
                    height);
            })
            .OrderBy(segment => segment.X)
            .ToList();

        return paddedSegments;
    }

    private static double[] MeasureRowPageRatios(byte[] pixels, int stride, int width, int height, int xStart, int xEnd)
    {
        var safeXStart = Math.Clamp(xStart, 0, Math.Max(0, width - 1));
        var safeXEnd = Math.Clamp(xEnd, safeXStart + 1, width);
        var stepX = Math.Max(2, (safeXEnd - safeXStart) / 320);
        var ratios = new double[height];

        for (var y = 0; y < height; y++)
        {
            var pagePixelCount = 0;
            var sampleCount = 0;
            var rowOffset = y * stride;
            for (var x = safeXStart; x < safeXEnd; x += stepX)
            {
                sampleCount++;
                if (IsPagePixel(pixels, rowOffset + (x * 4)))
                {
                    pagePixelCount++;
                }
            }

            ratios[y] = sampleCount == 0 ? 0 : (double)pagePixelCount / sampleCount;
        }

        return ratios;
    }

    private static double[] MeasureColumnPageRatios(byte[] pixels, int stride, int width, int height, Int32Rect region)
    {
        var safeRegion = NormalizeCrop(region, width, height);
        var stepY = Math.Max(2, safeRegion.Height / 320);
        var ratios = new double[safeRegion.Width];

        for (var x = 0; x < safeRegion.Width; x++)
        {
            var pagePixelCount = 0;
            var sampleCount = 0;
            var absoluteX = safeRegion.X + x;
            for (var y = safeRegion.Y; y < safeRegion.Y + safeRegion.Height; y += stepY)
            {
                sampleCount++;
                if (IsPagePixel(pixels, (y * stride) + (absoluteX * 4)))
                {
                    pagePixelCount++;
                }
            }

            ratios[x] = sampleCount == 0 ? 0 : (double)pagePixelCount / sampleCount;
        }

        return ratios;
    }

    private static int FindLeadingActiveIndex(double[] values, double threshold, int requiredRun)
    {
        for (var index = 0; index <= values.Length - requiredRun; index++)
        {
            var average = values.Skip(index).Take(requiredRun).Average();
            if (average >= threshold)
            {
                return index;
            }
        }

        return 0;
    }

    private static int FindTrailingActiveIndex(double[] values, double threshold, int requiredRun)
    {
        for (var index = values.Length - requiredRun; index >= 0; index--)
        {
            var average = values.Skip(index).Take(requiredRun).Average();
            if (average >= threshold)
            {
                return index + requiredRun - 1;
            }
        }

        return values.Length - 1;
    }

    private static List<(int Start, int Length)> FindActiveSegments(
        double[] values,
        int offset,
        double threshold,
        int minimumLength,
        int maximumGap)
    {
        var segments = new List<(int Start, int Length)>();
        var segmentStart = -1;
        var gapCount = 0;

        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] >= threshold)
            {
                if (segmentStart < 0)
                {
                    segmentStart = index;
                }

                gapCount = 0;
                continue;
            }

            if (segmentStart < 0)
            {
                continue;
            }

            gapCount++;
            if (gapCount <= maximumGap)
            {
                continue;
            }

            var end = index - gapCount;
            var length = end - segmentStart + 1;
            if (length >= minimumLength)
            {
                segments.Add((offset + segmentStart, length));
            }

            segmentStart = -1;
            gapCount = 0;
        }

        if (segmentStart >= 0)
        {
            var end = values.Length - 1;
            var length = end - segmentStart + 1;
            if (length >= minimumLength)
            {
                segments.Add((offset + segmentStart, length));
            }
        }

        return segments;
    }

    private static BitmapSource Crop(BitmapSource source, Int32Rect region)
    {
        var cropped = new CroppedBitmap(source, NormalizeCrop(region, source.PixelWidth, source.PixelHeight));
        cropped.Freeze();
        return cropped;
    }

    private static Int32Rect NormalizeCrop(Int32Rect region, int maxWidth, int maxHeight)
    {
        var x = Math.Clamp(region.X, 0, Math.Max(0, maxWidth - 1));
        var y = Math.Clamp(region.Y, 0, Math.Max(0, maxHeight - 1));
        var width = Math.Clamp(region.Width, 1, maxWidth - x);
        var height = Math.Clamp(region.Height, 1, maxHeight - y);
        return new Int32Rect(x, y, width, height);
    }

    private static string CombineSections(IEnumerable<string> sections)
    {
        var output = new List<string>();
        foreach (var section in sections)
        {
            foreach (var rawLine in section.Split(Environment.NewLine))
            {
                var line = rawLine.TrimEnd();
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
                    {
                        output.Add(string.Empty);
                    }

                    continue;
                }

                if (output.TakeLast(8).Any(existing => string.Equals(existing, line, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                output.Add(line);
            }
        }

        return string.Join(Environment.NewLine, output).Trim();
    }

    private static string ChooseBest(string primaryText, string documentText)
    {
        if (string.IsNullOrWhiteSpace(primaryText))
        {
            return documentText;
        }

        if (string.IsNullOrWhiteSpace(documentText))
        {
            return primaryText;
        }

        if (HasDocumentBody(documentText) && !HasDocumentBody(primaryText))
        {
            return documentText;
        }

        var primaryScore = ScoreText(primaryText);
        var documentScore = ScoreText(documentText);

        return documentScore > primaryScore * 1.05
            ? documentText
            : primaryText;
    }

    private static double ScoreText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var alphabeticCharacters = text.Count(char.IsLetter);
        var digitCharacters = text.Count(char.IsDigit);
        var likelyWords = lines
            .SelectMany(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Count(word => word.Count(char.IsLetterOrDigit) >= 3);
        var longLines = lines.Count(line =>
        {
            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length >= 5 && line.Count(char.IsLetter) >= 20;
        });
        var sentenceLikeLines = lines.Count(line =>
            line.Contains('.') || line.Contains(',') || line.Contains(';') || line.Contains(':'));
        var uiLikeShortLines = lines.Count(line =>
        {
            var trimmed = line.Trim();
            var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length is > 0 and <= 3
                   && trimmed.Length <= 28
                   && !trimmed.Any(character => character is '.' or ',' or ';' or ':');
        });
        var noisyLines = lines.Count(line =>
        {
            var trimmed = line.Trim();
            return trimmed.Length <= 2 || trimmed.All(character => !char.IsLetter(character));
        });

        return alphabeticCharacters
               + (digitCharacters * 0.15)
               + (likelyWords * 4.5)
               + (longLines * 18)
               + (sentenceLikeLines * 8)
               + (lines.Length * 1.8)
               - (uiLikeShortLines * 7)
               - (noisyLines * 9);
    }

    private static bool HasDocumentBody(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lines = text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToList();

        var substantialLines = lines.Count(line =>
        {
            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length >= 5 && line.Count(char.IsLetter) >= 20;
        });

        return substantialLines >= 3;
    }

    private static bool NeedsGenericTileFallback(string text, BitmapSource source)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (ScoreText(text) < 120 && (source.PixelHeight >= 1700 || source.PixelWidth >= 2200))
        {
            return true;
        }

        return !HasDocumentBody(text) && source.PixelHeight >= 2200;
    }

    private static bool NeedsFocusedTextFallback(string text, BitmapSource source)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return ScoreText(text) < 105 &&
               (source.PixelWidth <= 1500 || source.PixelHeight <= 1100);
    }

    private static bool NeedsAggressiveTextFallback(string text, BitmapSource source)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return ScoreText(text) < 150 &&
               (source.PixelHeight >= 1500 || source.PixelWidth >= 1800);
    }

    private static bool ShouldTryDocumentPass(BitmapSource source)
    {
        return source.PixelHeight >= 1800 || source.PixelWidth > source.PixelHeight * 1.08;
    }

    private static bool TryFindFocusedTextRegion(BitmapSource source, out Int32Rect region)
    {
        var working = EnsureBgra32(source);
        var pixels = CopyPixels(working, out var stride);
        var width = working.PixelWidth;
        var height = working.PixelHeight;
        var step = Math.Max(1, Math.Min(width, height) < 900 ? 1 : 2);

        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        var inkSamples = 0;

        for (var y = 0; y < height; y += step)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < width; x += step)
            {
                if (!IsInkPixel(pixels, rowOffset + (x * 4)))
                {
                    continue;
                }

                inkSamples++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (inkSamples < 24 || maxX <= minX || maxY <= minY)
        {
            region = default;
            return false;
        }

        var rawWidth = Math.Max(1, maxX - minX + 1);
        var rawHeight = Math.Max(1, maxY - minY + 1);
        var paddingX = Math.Clamp((int)Math.Round(rawWidth * 0.08), 10, 48);
        var paddingY = Math.Clamp((int)Math.Round(rawHeight * 0.1), 10, 44);
        region = NormalizeCrop(
            new Int32Rect(
                minX - paddingX,
                minY - paddingY,
                rawWidth + (paddingX * 2),
                rawHeight + (paddingY * 2)),
            width,
            height);

        var focusedArea = Math.Max(1d, region.Width * region.Height);
        var sourceArea = Math.Max(1d, width * height);
        return focusedArea / sourceArea <= 0.97;
    }

    private static byte[] PrepareForOcr(BitmapSource source, double preferredLongestEdge, OcrPreparationMode preparationMode)
    {
        var longestEdge = Math.Max(1d, Math.Max(source.PixelWidth, source.PixelHeight));
        var maxImageDimension = Math.Max(512d, OcrEngine.MaxImageDimension - 8d);
        var cappedPreferredLongestEdge = Math.Min(preferredLongestEdge, maxImageDimension);
        var targetScale = CalculateScale(source.PixelWidth, source.PixelHeight);
        var adjustedPreferredEdgeScale = cappedPreferredLongestEdge / longestEdge;
        var maxAllowedScale = maxImageDimension / longestEdge;

        BitmapSource working = FlattenToOpaqueBackground(EnsureBgra32(source));
        var effectiveScale = Math.Clamp(Math.Max(targetScale, adjustedPreferredEdgeScale), 1, Math.Max(1, maxAllowedScale));
        if (effectiveScale > 1.01)
        {
            var scaled = new TransformedBitmap(working, new ScaleTransform(effectiveScale, effectiveScale));
            scaled.Freeze();
            working = scaled;
        }

        if (preparationMode == OcrPreparationMode.HighContrast)
        {
            working = ApplyHighContrastTextEnhancement(working);
        }

        return ImageHelper.Encode(working, ImageFileFormat.Png);
    }

    private static double CalculateScale(int width, int height)
    {
        var longestEdge = Math.Max(width, height);
        if (longestEdge <= 0)
        {
            return 1;
        }

        var preferredLongestEdge = 2600d;
        var scale = preferredLongestEdge / longestEdge;
        return Math.Clamp(scale, 1, 2.25);
    }

    private static Rect GetLineBounds(OcrLine line)
    {
        if (line.Words.Count == 0)
        {
            return new Rect(0, 0, 0, 0);
        }

        var left = line.Words.Min(word => word.BoundingRect.X);
        var top = line.Words.Min(word => word.BoundingRect.Y);
        var right = line.Words.Max(word => word.BoundingRect.X + word.BoundingRect.Width);
        var bottom = line.Words.Max(word => word.BoundingRect.Y + word.BoundingRect.Height);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32)
        {
            return source;
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static BitmapSource FlattenToOpaqueBackground(BitmapSource source)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(System.Windows.Media.Brushes.White, null, new System.Windows.Rect(0, 0, source.PixelWidth, source.PixelHeight));
            context.DrawImage(source, new System.Windows.Rect(0, 0, source.PixelWidth, source.PixelHeight));
        }

        var bitmap = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource ApplyHighContrastTextEnhancement(BitmapSource source)
    {
        var working = EnsureBgra32(source);
        var stride = ((working.PixelWidth * working.Format.BitsPerPixel) + 7) / 8;
        var pixels = new byte[stride * working.PixelHeight];
        working.CopyPixels(pixels, stride, 0);

        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var blue = pixels[offset];
            var green = pixels[offset + 1];
            var red = pixels[offset + 2];
            var luminance = (red * 77 + green * 150 + blue * 29) >> 8;

            var contrasted = ((luminance - 128) * 2) + 128;
            contrasted = Math.Clamp(contrasted, 0, 255);

            if (contrasted >= 228)
            {
                contrasted = 255;
            }
            else if (contrasted <= 40)
            {
                contrasted = 0;
            }

            var output = (byte)contrasted;
            pixels[offset] = output;
            pixels[offset + 1] = output;
            pixels[offset + 2] = output;
            pixels[offset + 3] = 255;
        }

        var enhanced = BitmapSource.Create(
            working.PixelWidth,
            working.PixelHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        enhanced.Freeze();
        return enhanced;
    }

    private static byte[] CopyPixels(BitmapSource source, out int stride)
    {
        var working = EnsureBgra32(source);
        stride = ((working.PixelWidth * working.Format.BitsPerPixel) + 7) / 8;
        var buffer = new byte[stride * working.PixelHeight];
        working.CopyPixels(buffer, stride, 0);
        return buffer;
    }

    private static bool IsPagePixel(byte[] pixels, int offset)
    {
        var blue = pixels[offset];
        var green = pixels[offset + 1];
        var red = pixels[offset + 2];
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));

        return max >= 232 && (max - min) <= 26;
    }

    private static bool IsInkPixel(byte[] pixels, int offset)
    {
        var blue = pixels[offset];
        var green = pixels[offset + 1];
        var red = pixels[offset + 2];
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var luminance = (red * 77 + green * 150 + blue * 29) >> 8;

        if (luminance <= 214)
        {
            return true;
        }

        return luminance <= 235 && (max - min) >= 24;
    }
}
