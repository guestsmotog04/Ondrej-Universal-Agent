using System.Globalization;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Skia;

namespace Thio_Universal_Agent.Logic;

public class CoordinatePrompter
{
    private const int RulerOffset = 100;
    private const int LabelBuffer = 60;
    private const int DefaultDivisions = 10;
    private const double DefaultConfidencePixels = 15.0;

    /// <summary>Tracks a rectangular crop window in original image pixel coordinates.</summary>
    public record ViewRegion(double X, double Y, double Width, double Height);

    /// <summary>A parsed grid coordinate returned by the LLM.</summary>
    public record GridCoordinate(double X, double Y);

    /// <summary>Builds the prompt text that asks the LLM to identify grid coordinates.</summary>
    public static string MakeCoordinatePrompt(string itemToIdentify, int divisions = DefaultDivisions)
    {
        return $"Identify the X and Y grid coordinates closest to: {itemToIdentify}.\n\n"
             + $"Only output the result as plain text comma separated. Each coordinate should be between 0 and {divisions}, and contain a single decimal.";
    }

    /// <summary>Creates a <see cref="ViewRegion"/> covering the full source image.</summary>
    public static ViewRegion CreateFullView(IImage source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ViewRegion(0, 0, source.Width, source.Height);
    }

    /// <summary>
    /// Creates a PNG image of the source cropped to <paramref name="view"/> with a grid
    /// overlay and axis labels drawn on top.
    /// </summary>
    public static byte[] CreateGridOverlayImage(
        IImage source,
        ViewRegion view,
        int cols = DefaultDivisions,
        int rows = DefaultDivisions,
        Color? gridColor = null,
        float? lineThickness = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(view);

        Color color = gridColor ?? Color.FromRgba(236, 72, 153, 102); // ~40% opacity pink
        int imageWidth = (int)source.Width;
        int imageHeight = (int)source.Height;
        int canvasWidth = imageWidth + RulerOffset + LabelBuffer;
        int canvasHeight = imageHeight + RulerOffset + LabelBuffer;

        using var context = new SkiaBitmapExportContext(canvasWidth, canvasHeight, 1.0f);
        ICanvas canvas = context.Canvas;

        // Black background
        canvas.FillColor = Colors.Black;
        canvas.FillRectangle(0, 0, canvasWidth, canvasHeight);

        // Draw the cropped view stretched to the full image area via clip + transform
        canvas.SaveState();
        canvas.ClipRectangle(RulerOffset, RulerOffset, imageWidth, imageHeight);
        canvas.Translate(RulerOffset, RulerOffset);
        canvas.Scale(imageWidth / (float)view.Width, imageHeight / (float)view.Height);
        canvas.Translate(-(float)view.X, -(float)view.Y);
        canvas.DrawImage(source, 0, 0, source.Width, source.Height);
        canvas.RestoreState();

        DrawRulerGrid(canvas, imageWidth, imageHeight, cols, rows, color, lineThickness);

        using var ms = new MemoryStream();
        context.WriteToStream(ms);
        return ms.ToArray();
    }

    /// <summary>Parses a comma-separated "X, Y" coordinate string from the LLM response.</summary>
    public static GridCoordinate? ParseCoordinates(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        string[] parts = response.Trim().Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return null;

        if (!double.TryParse(parts[0], CultureInfo.InvariantCulture, out double x) ||
            !double.TryParse(parts[1], CultureInfo.InvariantCulture, out double y))
            return null;

        return new GridCoordinate(x, y);
    }

    /// <summary>
    /// Calculates a contextual zoom region around the given coordinate and returns
    /// the zoomed <see cref="ViewRegion"/> for the next iteration.
    /// The zoom window extends from floor(val − 1) to ceil(val + 1) on each axis.
    /// </summary>
    public static ViewRegion CalculateZoomRegion(
        ViewRegion currentView,
        GridCoordinate coordinate,
        int cols = DefaultDivisions,
        int rows = DefaultDivisions)
    {
        ArgumentNullException.ThrowIfNull(currentView);
        ArgumentNullException.ThrowIfNull(coordinate);

        int xStart = Math.Max(0, (int)Math.Floor(coordinate.X - 1));
        int xEnd = Math.Min(cols, (int)Math.Ceiling(coordinate.X + 1));
        int yStart = Math.Max(0, (int)Math.Floor(coordinate.Y - 1));
        int yEnd = Math.Min(rows, (int)Math.Ceiling(coordinate.Y + 1));

        int spanX = xEnd - xStart;
        int spanY = yEnd - yStart;

        if (spanX <= 0 || spanY <= 0)
            return currentView;

        return ZoomToRegion(currentView, xStart, yStart, spanX, spanY, cols, rows);
    }

    /// <summary>
    /// Converts grid coordinates on the current (possibly zoomed) view back to
    /// absolute screen pixel coordinates, assuming the original image matches screen resolution.
    /// </summary>
    public static (double ScreenX, double ScreenY) CalculateScreenCoordinates(
        ViewRegion currentView,
        GridCoordinate coordinate,
        int cols = DefaultDivisions,
        int rows = DefaultDivisions)
    {
        ArgumentNullException.ThrowIfNull(currentView);
        ArgumentNullException.ThrowIfNull(coordinate);

        double screenX = currentView.X + (coordinate.X / cols) * currentView.Width;
        double screenY = currentView.Y + (coordinate.Y / rows) * currentView.Height;

        return (screenX, screenY);
    }

    /// <summary>
    /// Determines whether another zoom iteration is warranted based on the current
    /// pixel precision per grid cell compared to a confidence threshold.
    /// </summary>
    public static bool ShouldContinueZooming(
        ViewRegion currentView,
        int cols = DefaultDivisions,
        int rows = DefaultDivisions,
        double confidencePixels = DefaultConfidencePixels)
    {
        ArgumentNullException.ThrowIfNull(currentView);

        double cellPixelW = currentView.Width / cols;
        double cellPixelH = currentView.Height / rows;

        return cellPixelW > confidencePixels || cellPixelH > confidencePixels;
    }

    /// <summary>Zooms the current view to a sub-region defined by grid-cell start and span.</summary>
    private static ViewRegion ZoomToRegion(
        ViewRegion currentView,
        double startX,
        double startY,
        double spanX,
        double spanY,
        int cols,
        int rows)
    {
        double cellPixelW = currentView.Width / cols;
        double cellPixelH = currentView.Height / rows;

        return new ViewRegion(
            currentView.X + startX * cellPixelW,
            currentView.Y + startY * cellPixelH,
            spanX * cellPixelW,
            spanY * cellPixelH);
    }

    /// <summary>Draws the grid lines, axis labels and tick marks onto the canvas.</summary>
    private static void DrawRulerGrid(
        ICanvas canvas,
        int imageWidth,
        int imageHeight,
        int cols,
        int rows,
        Color color,
        float? lineThickness)
    {
        float thickness = lineThickness ?? Math.Max(1f, imageWidth / 1200f);
        float labelSize = Math.Max(16f, imageWidth / 60f);

        IFont labelFont = new Font("Consolas", FontWeights.Bold);

        canvas.StrokeColor = color;
        canvas.StrokeSize = thickness;
        canvas.Antialias = true;
        canvas.FontColor = Colors.White;
        canvas.FontSize = labelSize;
        canvas.Font = labelFont;

        float cellW = (float)imageWidth / cols;
        float cellH = (float)imageHeight / rows;

        // Vertical lines + X axis labels
        for (int c = 0; c <= cols; c++)
        {
            float x = RulerOffset + c * cellW;

            canvas.DrawLine(x, RulerOffset, x, RulerOffset + imageHeight);

            string label = c.ToString(CultureInfo.InvariantCulture);
            SizeF sz = canvas.GetStringSize(label, labelFont, labelSize);
            canvas.DrawString(label,
                x - sz.Width / 2, RulerOffset - 15 - sz.Height, sz.Width, sz.Height,
                HorizontalAlignment.Center, VerticalAlignment.Bottom);

            canvas.DrawLine(x, RulerOffset - 8, x, RulerOffset);
        }

        // Horizontal lines + Y axis labels
        for (int r = 0; r <= rows; r++)
        {
            float y = RulerOffset + r * cellH;

            canvas.DrawLine(RulerOffset, y, RulerOffset + imageWidth, y);

            string label = r.ToString(CultureInfo.InvariantCulture);
            SizeF sz = canvas.GetStringSize(label, labelFont, labelSize);
            canvas.DrawString(label,
                RulerOffset - 25 - sz.Width, y - sz.Height / 2, sz.Width, sz.Height,
                HorizontalAlignment.Right, VerticalAlignment.Center);

            canvas.DrawLine(RulerOffset - 8, y, RulerOffset, y);
        }
    }

} // End CoordinatePrompter class
