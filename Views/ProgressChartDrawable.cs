namespace LearnJP.Views;

/// <summary>
/// Draws a compact proficiency bar-chart for the Progress page.
/// X-axis: vocabulary terms ordered from most to least proficient.
/// Y-axis: proficiency percentage (0–100 %).
/// Each bar is colour-coded with the same palette used by the row text.
/// </summary>
public sealed class ProgressChartDrawable : IDrawable
{
    public IReadOnlyList<double> Values { get; set; } = Array.Empty<double>();

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Values.Count == 0) return;

        float w = dirtyRect.Width;
        float h = dirtyRect.Height;

        // Leave a small 1 px gap between bars when there are fewer terms,
        // but collapse to solid fills for large vocabularies.
        float barPitch = w / Values.Count;
        float gap = barPitch >= 4f ? 1f : 0f;
        float barW = Math.Max(1f, barPitch - gap);

        for (int i = 0; i < Values.Count; i++)
        {
            double v = Values[i];
            float barH = (float)(v / 100.0 * h);
            float x = i * barPitch;
            float y = h - barH;

            canvas.FillColor = ColorFor(v);
            canvas.FillRectangle(x, y, barW, barH);
        }
    }

    private static Color ColorFor(double v)
    {
        if (v >= 85) return Color.FromArgb("#4CAF7A");
        if (v >= 60) return Color.FromArgb("#7BC47F");
        if (v >= 35) return Color.FromArgb("#FFB86B");
        if (v > 0)   return Color.FromArgb("#E5556B");
        return Color.FromArgb("#7A7A8C");
    }
}
