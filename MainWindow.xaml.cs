using System;
using System.Windows;
using Gigasoft.ProEssentials;
using Gigasoft.ProEssentials.Enums;

namespace BitmapSymbolsJaggedData
{
    /// <summary>
    /// ProEssentials WPF — Bitmap Symbols &amp; Jagged Data
    ///
    /// Combines two PesgoWpf features:
    ///
    /// FEATURE 1 — BITMAP RESOURCE SYMBOLS (Example 140 pattern):
    ///   Custom PNG images are used as data point markers instead of the
    ///   built-in vector PointType shapes. Each subset gets its own bitmap.
    ///
    ///   Two bitmap strategies are shown side by side:
    ///
    ///   Colorize = false (Subset 0 — symbolBlueDot.png):
    ///     The bitmap renders with its own native colors. Used for pre-colored
    ///     images like a blue dot that should always look blue regardless of
    ///     the subset color. Best for rendered 3D spheres or complex icons
    ///     where tinting would look wrong.
    ///
    ///   Colorize = true (Subsets 1-3 — symbol01/02/03.png):
    ///     The bitmap is a dark silhouette on a transparent background.
    ///     ProEssentials tints it using SubsetColors, so one black-on-transparent
    ///     shape file can produce any color. Far more flexible — one PNG,
    ///     unlimited colors.
    ///
    ///   Bitmap slot assignment uses the formula:
    ///     SubsetPointTypes[s] = (PointType)(10001 + workingBitmapIndex)
    ///   Where 10001 = WorkingBitmap 0, 10002 = WorkingBitmap 1, etc.
    ///   Standard PointType enum values stay below 100; values >= 10001 are
    ///   interpreted as bitmap resource slot references.
    ///
    ///   WMF/EMF vector export is disabled because bitmap symbols cannot be
    ///   represented in pure vector format.
    ///
    /// FEATURE 2 — JAGGED DATA (Example 142 pattern):
    ///   JaggedData = true allows each subset to have a different number of
    ///   data points. This is the correct approach when series genuinely have
    ///   different densities rather than padding shorter series with nulls.
    ///
    ///   Point counts:
    ///     Subset 0  12,000 pts  (blue dot — dense scatter, pre-colored)
    ///     Subset 1   1,200 pts  (symbol01 — colorized cyan)
    ///     Subset 2     120 pts  (symbol02 — colorized gold)
    ///     Subset 3      12 pts  (symbol03 — colorized red)
    ///
    ///   Data is loaded using FastCopyFromJagged — each subset's float[]
    ///   array is block-copied in one call. This is significantly faster than
    ///   spoon-feeding Y[s,p] element by element for large point counts.
    ///
    ///   IMPORTANT: JaggedData requires RenderEngine = Direct2D.
    ///   Direct3D does not support JaggedData.
    ///
    ///   Pre-allocation: setting the last element of each subset before the
    ///   data loop guarantees internal arrays are fully sized before filling,
    ///   preventing incremental reallocations.
    ///
    /// Controls:
    ///   Left-click drag  — zoom box
    ///   Right-click      — context menu (export, print, customize)
    /// </summary>
    public partial class MainWindow : Window
    {
        // Subset point counts — each subset has its own density
        const int Pts0 = 12000;  // dense — symbolBlueDot, Colorize=false
        const int Pts1 =  1200;  // symbol01, Colorize=true, cyan
        const int Pts2 =   120;  // symbol02, Colorize=true, gold
        const int Pts3 =    12;  // symbol03, Colorize=true, red

        public MainWindow()
        {
            InitializeComponent();
        }

        // -----------------------------------------------------------------------
        // Pesgo1_Loaded — chart initialization
        // -----------------------------------------------------------------------
        void Pesgo1_Loaded(object sender, RoutedEventArgs e)
        {
            // =======================================================================
            // Step 1 — JaggedData mode
            //
            // JaggedData = true allows each subset to have an independent point
            // count. Without it, all subsets share the same Points dimension and
            // shorter series would need null padding.
            //
            // IMPORTANT: JaggedData requires RenderEngine = Direct2D (set in Step 6).
            // Direct3D does not support JaggedData.
            //
            // Points = 1 is the recommended starting value for JaggedData.
            // On ReinitializeResetImage(), ProEssentials automatically adjusts
            // Points to the maximum across all subsets.
            // =======================================================================
            Pesgo1.PeData.JaggedData = true;
            Pesgo1.PeData.Subsets    = 4;
            Pesgo1.PeData.Points     = 1;  // auto-adjusts to max(Pts0..3) on reinitialize

            // =======================================================================
            // Step 2 — Pre-allocate jagged arrays
            //
            // Setting the last element of each subset before the data loop guarantees
            // that the internal per-subset arrays are fully sized upfront, preventing
            // incremental reallocations during data load.
            //
            // This is the validated .NET pre-allocation pattern for JaggedData —
            // SetJaggedPointsX/Y exist only in the OCX/VBA API, not in .NET.
            // =======================================================================
            Pesgo1.PeData.X[0, Pts0 - 1] = 0f;  Pesgo1.PeData.Y[0, Pts0 - 1] = 0f;
            Pesgo1.PeData.X[1, Pts1 - 1] = 0f;  Pesgo1.PeData.Y[1, Pts1 - 1] = 0f;
            Pesgo1.PeData.X[2, Pts2 - 1] = 0f;  Pesgo1.PeData.Y[2, Pts2 - 1] = 0f;
            Pesgo1.PeData.X[3, Pts3 - 1] = 0f;  Pesgo1.PeData.Y[3, Pts3 - 1] = 0f;

            // =======================================================================
            // Step 3 — Build data arrays and load via FastCopyFromJagged
            //
            // Each subset gets its own float[] pair built in a single loop, then
            // block-copied into the chart with FastCopyFromJagged. This is
            // significantly faster than spoon-feeding Y[s,p] for large datasets
            // and produces cleaner code — one array build per subset.
            //
            // FastCopyFromJagged(source, subsetIndex) copies the full array into
            // subset n and sets that subset's size from the source array length.
            //
            // X spacing differs per subset so the series are visually spread across
            // the same X range, making all four densities easy to compare.
            // =======================================================================
            var rand = new Random(42);

            LoadSubset(0, Pts0, stepX: 0.1f,   rand);
            LoadSubset(1, Pts1, stepX: 1.0f,   rand);
            LoadSubset(2, Pts2, stepX: 10.0f,  rand);
            LoadSubset(3, Pts3, stepX: 100.0f, rand);

            // =======================================================================
            // Step 4 — Subset colors
            //
            // SubsetColors MUST be set before Bitmaps.Colorize is configured.
            // The colorization lookup reads SubsetColors at the time the bitmap
            // slot is defined. Setting colors after Colorize = true may result
            // in tinted symbols not reflecting the intended color.
            //
            // Subset 0 uses a pre-colored bitmap (Colorize=false), so its
            // SubsetColor only affects the legend swatch.
            // Subsets 1-3 use Colorize=true — SubsetColors[s] becomes the tint.
            // =======================================================================
            Pesgo1.PeColor.SubsetColors[0] = System.Windows.Media.Color.FromArgb(255,   0, 150, 255); // blue  (legend swatch only — slot 0 is pre-colored)
            Pesgo1.PeColor.SubsetColors[1] = System.Windows.Media.Color.FromArgb(255,   0, 229, 229); // cyan  (tints symbol01)
            Pesgo1.PeColor.SubsetColors[2] = System.Windows.Media.Color.FromArgb(255, 255, 210,   0); // gold  (tints symbol02)
            Pesgo1.PeColor.SubsetColors[3] = System.Windows.Media.Color.FromArgb(255, 255,  48,  48); // red   (tints symbol03)

            // =======================================================================
            // Step 5 — Bitmap resource symbols
            //
            // Each WorkingBitmap slot holds one PNG file with its rendering style.
            // Slots are referenced by SubsetPointTypes via the formula:
            //   (PointType)(10001 + workingBitmapIndex)
            //
            // Slot 0 — symbolBlueDot.png, Colorize = false:
            //   Renders with the bitmap's own native colors. The blue dot is
            //   pre-colored and should always appear blue — tinting it with
            //   SubsetColors would change its appearance.
            //
            // Slots 1-3 — symbol01/02/03.png, Colorize = true:
            //   Each is a dark silhouette on a transparent background.
            //   ProEssentials tints it using SubsetColors[s] set in Step 4.
            //   One shape file → any color. The tint comes from SubsetColors[s].
            // =======================================================================
            var bitmaps = new (int slot, string file, Gigasoft.ProEssentials.Enums.ResourceBitmapColorizeMode colorize)[]
            {
                (0, "symbolBlueDot.png", Gigasoft.ProEssentials.Enums.ResourceBitmapColorizeMode.None),  // pre-colored, use as-is
                (1, "symbol01.png",      Gigasoft.ProEssentials.Enums.ResourceBitmapColorizeMode.Mask),   // silhouette, tinted by SubsetColors[1]
                (2, "symbol02.png",      Gigasoft.ProEssentials.Enums.ResourceBitmapColorizeMode.Mask),   // silhouette, tinted by SubsetColors[2]
                (3, "symbol03.png",      Gigasoft.ProEssentials.Enums.ResourceBitmapColorizeMode.Mask),   // silhouette, tinted by SubsetColors[3]
            };

            foreach (var (slot, file, colorize) in bitmaps)
            {
                Pesgo1.PePlot.Bitmaps.WorkingBitmap = slot;
                Pesgo1.PePlot.Bitmaps.Filename      = file;
                Pesgo1.PePlot.Bitmaps.ColorizeMode = colorize;
                Pesgo1.PePlot.Bitmaps.Style         = ResourceBitmapStyle.MediumCentered;
            }

            // Assign each subset to its bitmap slot.
            // The magic number base 10001 means WorkingBitmap 0;
            // 10002 = WorkingBitmap 1, etc.
            Pesgo1.PePlot.SubsetPointTypes[0] = (PointType)10001; // slot 0: symbolBlueDot
            Pesgo1.PePlot.SubsetPointTypes[1] = (PointType)10002; // slot 1: symbol01
            Pesgo1.PePlot.SubsetPointTypes[2] = (PointType)10003; // slot 2: symbol02
            Pesgo1.PePlot.SubsetPointTypes[3] = (PointType)10004; // slot 3: symbol03

            // Scale the symbols — denser subsets get smaller markers so individual
            // points remain distinguishable at full zoom; sparser subsets get larger
            // markers so they don't disappear against the dense background.
            Pesgo1.PePlot.Option.SubsetPointSizes[0] = 1.0F; // 12,000 pts — keep small
            Pesgo1.PePlot.Option.SubsetPointSizes[1] = 1.4F; //  1,200 pts
            Pesgo1.PePlot.Option.SubsetPointSizes[2] = 1.8F; //    120 pts
            Pesgo1.PePlot.Option.SubsetPointSizes[3] = 1.8F; //     12 pts

            // =======================================================================
            // Step 6 — Subset labels and appearance
            // =======================================================================
            Pesgo1.PeString.SubsetLabels[0] = "12,000 pts — Blue Dot";
            Pesgo1.PeString.SubsetLabels[1] =  "1,200 pts — Symbol 01";
            Pesgo1.PeString.SubsetLabels[2] =    "120 pts — Symbol 02";
            Pesgo1.PeString.SubsetLabels[3] =     "12 pts — Symbol 03";

            Pesgo1.PePlot.Method      = SGraphPlottingMethod.Point;
            Pesgo1.PePlot.DataShadows = DataShadows.None; // shadows add clutter at high point counts
            Pesgo1.PePlot.MarkDataPoints = false;

            // =======================================================================
            // Step 7 — Rendering and export
            //
            // Direct2D is required for JaggedData. GDI+ and Direct3D do not support
            // the per-subset variable point count architecture.
            //
            // WMF/EMF vector export is disabled because bitmap resource symbols
            // cannot be represented in pure vector format. PNG export works fine.
            // =======================================================================
            Pesgo1.PeConfigure.RenderEngine      = RenderEngine.Direct2D;
            Pesgo1.PeConfigure.PrepareImages      = true;
            Pesgo1.PeConfigure.AntiAliasGraphics  = true;
            Pesgo1.PeConfigure.ImageAdjustLeft    = 25;

            Pesgo1.PeUserInterface.Dialog.AllowWmfExport = false;
            Pesgo1.PeUserInterface.Dialog.AllowEmfExport = false;

            // =======================================================================
            // Step 8 — Zoom and interaction
            // =======================================================================
            Pesgo1.PeUserInterface.Allow.Zooming  = AllowZooming.HorzAndVert;
            Pesgo1.PeUserInterface.Allow.ZoomStyle = ZoomStyle.Ro2Not;

            Pesgo1.PeUserInterface.Scrollbar.ScrollingHorzZoom = true;
            Pesgo1.PeUserInterface.Scrollbar.ScrollingVertZoom = true;
            Pesgo1.PeUserInterface.Scrollbar.MouseWheelFunction =
                MouseWheelFunction.HorizontalVerticalZoom;
            Pesgo1.PeUserInterface.Scrollbar.MouseWheelZoomSmoothness = 8;

            Pesgo1.PeUserInterface.Cursor.PromptTracking = true;
            Pesgo1.PeUserInterface.Cursor.PromptLocation = CursorPromptLocation.ToolTip;
            Pesgo1.PeUserInterface.Cursor.PromptStyle    = CursorPromptStyle.XYValues;

            // =======================================================================
            // Step 9 — Style
            // =======================================================================
            Pesgo1.PeColor.BitmapGradientMode = true;
            Pesgo1.PeColor.QuickStyle         = QuickStyle.DarkInset;

            Pesgo1.PeGrid.InFront     = true;
            Pesgo1.PeGrid.LineControl = GridLineControl.Both;
            Pesgo1.PeGrid.Style       = GridStyle.Dot;

            Pesgo1.PeFont.FontSize       = Gigasoft.ProEssentials.Enums.FontSize.Large;
            Pesgo1.PeFont.Fixed          = true;
            Pesgo1.PeFont.MainTitle.Bold = true;

            // =======================================================================
            // Step 10 — Titles
            // =======================================================================
            Pesgo1.PeString.MainTitle  = "Bitmap Symbols & Jagged Data";
            Pesgo1.PeString.SubTitle   = "4 subsets · 4 bitmap symbols · variable point counts per subset";
            Pesgo1.PeString.XAxisLabel = "X";
            Pesgo1.PeString.YAxisLabel = "Y";

            // =======================================================================
            // Step 11 — ReinitializeResetImage
            //
            // On reinitialize, PeData.Points auto-adjusts to the maximum subset
            // point count (12,000 for JaggedData). Always call last.
            // =======================================================================
            Pesgo1.PeFunction.ReinitializeResetImage();
            Pesgo1.Invalidate();
        }

        // -----------------------------------------------------------------------
        // LoadSubset — build float[] arrays and block-copy into the chart
        //
        // Builds X and Y float arrays for one subset in a tight loop, then
        // copies them into the chart with FastCopyFromJagged. Compared to
        // spoon-feeding Y[s,p] element by element, this approach:
        //   - Minimizes interop calls (one PEvsetEx call vs nPoints calls)
        //   - Keeps data-building code clean and readable
        //   - Scales well to large point counts (12,000+)
        //
        // FastCopyFromJagged(source, subsetIndex) performs a memcpy of the
        // entire array into that subset and sets the subset's size.
        //
        // stepX controls X spacing so all four subsets span a comparable
        // X range regardless of point count.
        // -----------------------------------------------------------------------
        void LoadSubset(int subset, int count, float stepX, Random rand)
        {
            float[] xData = new float[count];
            float[] yData = new float[count];

            int offset = (int)(rand.NextDouble() * 250);

            for (int p = 0; p < count; p++)
            {
                xData[p] = (p + 1) * stepX;
                yData[p] = (float)(
                    (p + 1) * 1
                    + (rand.NextDouble() * 250)
                    + Math.Sin(((double)(offset + p)) * 0.03) * 700.0
                    - subset * 140.0);
            }

            Pesgo1.PeData.X.FastCopyFromJagged(xData, subset);
            Pesgo1.PeData.Y.FastCopyFromJagged(yData, subset);
        }

        // -----------------------------------------------------------------------
        // Window_Closing
        // -----------------------------------------------------------------------
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
        }
    }
}
