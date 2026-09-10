using VellumPdf.Encryption;
using VellumPdf.Fonts;
using VellumPdf.Layout.Core;
using VellumPdf.Layout.Elements;
using VellumPdfShowcase.Web.Generation;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// Cycle 6 review, part 1: every collection and every string in the model was
/// capped, but not one number was, except <see cref="TableSpec.ColumnWidths"/>.
/// These tests cover the numeric caps that closed that gap.
/// </summary>
/// <remarks>
/// NOTE: the page, font-size and leading caps were once derived from a defect
/// in <c>VellumPdf.Layout</c> 2.3.0, whose <c>DocumentRenderer</c> recursed
/// once per page continuation and overflowed the CLR stack past roughly 4,250
/// of them. 2.3.1 converts both of its pagination passes to loops, so those
/// three caps are now sanity ceilings rather than measured boundaries, and the
/// reproductions that once had to be REJECTED are pinned in
/// <see cref="DeepPaginationTests"/> as documents that must RENDER.
/// </remarks>
public class PageSizeValidationTests
{
    [Theory]
    [InlineData(0, 300)]
    [InlineData(300, 0)]
    [InlineData(-1, 300)]
    [InlineData(double.NaN, 300)]
    [InlineData(300, double.NaN)]
    [InlineData(double.PositiveInfinity, 300)]
    [InlineData(300, double.NegativeInfinity)]
    [InlineData(20_001, 300)]
    [InlineData(300, 20_001)]
    public void OutOfRangeDimension_ThrowsAtConstruction(double width, double height)
    {
        Assert.Throws<ArgumentException>(() => new PageSizeSpec(width, height));
    }

    [Fact]
    public void MinimumAndMaximumDimensions_Construct()
    {
        var min = new PageSizeSpec(SpecLimits.MinPageDimensionPoints, SpecLimits.MinPageDimensionPoints);
        var max = new PageSizeSpec(SpecLimits.MaxPageDimensionPoints, SpecLimits.MaxPageDimensionPoints);

        Assert.Equal(SpecLimits.MinPageDimensionPoints, min.WidthPoints);
        Assert.Equal(SpecLimits.MaxPageDimensionPoints, max.WidthPoints);
    }

    /// <summary>
    /// A 36 by 36 point page is a legal PDF page and the library accepts it.
    /// The model rejected it while <see cref="SpecLimits.MinPageDimensionPoints"/>
    /// stood at 200, which made the catalogue understate what the library does.
    /// It now constructs and renders.
    /// </summary>
    [Fact]
    public void TinyPage_ConstructsAndRenders()
    {
        var style = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica) };

        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(36, 36),
            Margins = new EdgeInsets(0),
            DefaultTextStyle = style,
            Content = [ParagraphSpec.FromText("W", style)],
        };

        Assert.NotEmpty(SpecRenderer.Render(spec));
    }

    /// <summary>
    /// The replacement for the deleted geometry bound, and the reason relaxing
    /// <see cref="SpecLimits.MinPageDimensionPoints"/> to 1 is safe rather than
    /// merely permissive. A content box too short to hold one line is refused
    /// by the library, not by this model, and refused with a CATCHABLE
    /// exception rather than a hang, a crash, or fifty thousand wasted
    /// iterations. Measured directly against 2.3.1: about 10 ms, on this
    /// geometry and on a 1 by 1 point page alike.
    /// </summary>
    [Fact]
    public void PageTooSmallToHoldOneLine_RenderThrowsCatchably()
    {
        var style = new TextStyleSpec
        {
            Font = FontSpec.FromStandard14(Standard14.Helvetica),
            FontSize = 36,
            Leading = 50,
        };

        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 40),
            Margins = new EdgeInsets(0),
            DefaultTextStyle = style,
            Content = [ParagraphSpec.FromText(new string('a', 500), style)],
        };

        var exception = Assert.Throws<InvalidOperationException>(() => SpecRenderer.Render(spec));
        Assert.Contains("too tall", exception.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// The worst specification the caps in <see cref="SpecLimits"/> together
/// permitted while <see cref="SpecLimits.MinPageDimensionPoints"/> was 200 and
/// <see cref="SpecLimits.MaxFontSize"/> was 36: that page, zero margins, that
/// font size, and exactly <see cref="SpecLimits.MaxTotalTextLength"/>
/// characters in one run. Both configurations must render.
/// </summary>
/// <remarks>
/// NOTE: 200 and 36 are written here as LITERALS on purpose. They were read
/// from the constants until those constants were relaxed, at which point this
/// test silently became a different test: a 1 by 1 point page at 1,000-point
/// type holds no line at all, so it would assert the library's refusal rather
/// than the render it exists to guard. A test whose subject moves when an
/// unrelated constant moves is not a regression guard. The genuinely worst
/// case the relaxed model admits is measured and recorded on
/// <see cref="SpecLimits.MaxTotalTextLength"/> instead.
/// <para>
/// There are TWO tests below, not one, because there is no single answer to
/// which of "<see cref="TextStyleSpec.Leading"/> left unset" and
/// "<see cref="TextStyleSpec.Leading"/> set explicitly" is the worse
/// configuration: an unset leading reaches the library as a literal zero,
/// which it reads as a request to compute a line height from the font. See the
/// remark on <see cref="SpecLimits.MaxLeadingPoints"/>.
/// </para>
/// </remarks>
public class WorstPermittedSpecificationTests
{
    private const double PageSide = 200;
    private const double WorstFontSize = 36;
    private const double WorstLeading = 50;

    [Fact]
    public void SmallestPageLargestFontAndMaxTotalTextWithLeadingUnset_RendersSuccessfully()
    {
        var style = new TextStyleSpec
        {
            Font = FontSpec.FromStandard14(Standard14.Helvetica),
            FontSize = WorstFontSize,
        };

        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(PageSide, PageSide),
            Margins = new EdgeInsets(0),
            DefaultTextStyle = style,
            Content = [ParagraphSpec.FromText(new string('a', SpecLimits.MaxTotalTextLength), style)],
        };

        var bytes = SpecRenderer.Render(spec);

        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void SmallestPageLargestFontAndMaxTotalTextWithMaxLeadingExplicit_RendersSuccessfully()
    {
        var style = new TextStyleSpec
        {
            Font = FontSpec.FromStandard14(Standard14.Helvetica),
            FontSize = WorstFontSize,
            Leading = WorstLeading,
        };

        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(PageSide, PageSide),
            Margins = new EdgeInsets(0),
            DefaultTextStyle = style,
            Content = [ParagraphSpec.FromText(new string('a', SpecLimits.MaxTotalTextLength), style)],
        };

        var bytes = SpecRenderer.Render(spec);

        Assert.NotEmpty(bytes);
    }
}

/// <summary>
/// The documents that killed the process on <c>VellumPdf.Layout</c> 2.3.0,
/// kept as evidence that 2.3.1 paginates them instead. Each was measured
/// directly against both packages, as its own process with the exit code read
/// unpiped: every one exits 127 with "Stack overflow." on 2.3.0, and renders
/// on 2.3.1 in the page count asserted below.
/// </summary>
/// <remarks>
/// These shapes are here because they are DIFFERENT mechanisms, and the model
/// bounded each with a separate estimate before that bound came out. The first
/// is driven by wrapped text in a content box the document's own default
/// margins shrink; a caller who never touches <see cref="DocumentSpec.Margins"/>
/// reaches it. The second is driven by NODE count rather than character count:
/// 4,950 list items carrying 4,950 characters between them, which every
/// character-based estimate in this repository waved through while the library
/// still needed one rendered line per item. The third adds a running band,
/// which makes the library count pages in a separate pass before it draws
/// them; that pass carried its own copy of the recursion, so a document with a
/// band reached the crash one pass earlier than the original report's figures
/// suggested, and no measurement in that report covered it.
/// <para>
/// NOTE: every geometry here is written as literals rather than read from
/// <see cref="SpecLimits"/>, for the reason given on
/// <see cref="WorstPermittedSpecificationTests"/>: a reproduction whose shape
/// follows a constant stops being the document that was measured. The font
/// size and leading matter as much as the page does. At the model's default
/// 12-point style the list below occupies six lines per page and only 825
/// continuations, which 2.3.0 rendered without complaint; at 36 points with
/// 50-point leading it occupies one line per page and 4,950, which killed it.
/// </para>
/// <para>
/// NOTE: the page counts are asserted as lower bounds, not exact figures. What
/// these tests guard is that the documents remain DEEP, since a shallow one
/// would pass an emptiness check while proving nothing. An exact count would
/// pin the library's line-breaking decisions, which are not this repository's
/// to fix.
/// </para>
/// </remarks>
public class DeepPaginationTests
{
    private static TextStyleSpec OneLinePerPageStyle() => new()
    {
        Font = FontSpec.FromStandard14(Standard14.Helvetica),
        FontSize = 36,
        Leading = 50,
    };

    /// <summary>
    /// Measured at a <see cref="SpecLimits.MaxTotalTextLength"/> of 20,000:
    /// 15,000 pages on 2.3.1, exit 127 on 2.3.0 at roughly 4,353
    /// <c>DocumentRenderer.PlaceRenderer</c> frames. This reproduction is the
    /// worst text volume the model admits, so its page count follows that cap.
    /// </summary>
    private static DocumentSpec DefaultMarginsWideGlyphRepro()
    {
        var style = new TextStyleSpec
        {
            Font = FontSpec.FromStandard14(Standard14.Helvetica),
            FontSize = 36,
        };

        var text = string.Concat(Enumerable.Repeat("WWW ", SpecLimits.MaxTotalTextLength / 4));
        Assert.Equal(SpecLimits.MaxTotalTextLength, text.Length);

        return new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = style,
            Content = [ParagraphSpec.FromText(text, style)],
        };
    }

    /// <summary>
    /// Measured: 4,950 pages on 2.3.1, exit 127 on 2.3.0. The walk sees 4,951
    /// nodes and 4,950 characters, comfortably inside
    /// <see cref="SpecLimits.MaxWalkedNodes"/> and
    /// <see cref="SpecLimits.MaxTotalTextLength"/>, which is the whole point:
    /// no cap on volume can see this document coming.
    /// </summary>
    private static DocumentSpec ManyShortListItemsRepro()
    {
        var style = OneLinePerPageStyle();

        List<ListItemSpec> topLevelItems = [];
        for (var i = 0; i < 1_650; i++)
        {
            topLevelItems.Add(new ListItemSpec
            {
                Text = "W",
                Children = [new ListItemSpec { Text = "W" }, new ListItemSpec { Text = "W" }],
            });
        }

        return new DocumentSpec
        {
            Page = new PageSizeSpec(20_000, 200),
            Margins = new EdgeInsets(55),
            DefaultTextStyle = style,
            Content = [new ListSpec { Style = ListStyle.Unordered, DefaultStyle = style, Items = topLevelItems }],
        };
    }

    /// <summary>
    /// Counts page objects by scanning for the <c>/Type /Page</c> key the
    /// library writes, rejecting the <c>/Type /Pages</c> tree nodes that share
    /// its prefix. <c>VellumPdf.Reader</c> 2.3.1 exposes no page collection of
    /// its own, and these documents leave <see cref="DocumentSpec.UseObjectStreams"/>
    /// unset, so every page dictionary appears in the file uncompressed.
    /// </summary>
    private static int PageCount(byte[] pdf)
    {
        const string Key = "/Type /Page";
        var text = System.Text.Encoding.Latin1.GetString(pdf);

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(Key, index, StringComparison.Ordinal)) >= 0)
        {
            index += Key.Length;
            if (index >= text.Length || text[index] != 's')
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The reproductions below only mean something while the model still admits
    /// a document deep enough to have reached the defect. 2.3.0 died past
    /// roughly 4,250 page continuations, and at THIS geometry one character is
    /// NOT one page: <see cref="DefaultMarginsWideGlyphRepro"/> fills with the
    /// four-character string <c>"WWW "</c>, and the trailing space does not
    /// start a page, so three of every four characters produce a page and the
    /// fourth does not. MEASURED: 4,500 characters gives 3,375 pages, and
    /// 5,336 gives 4,002; both match a ratio of exactly 0.75 pages per
    /// character, not 1. A text budget under the floor this ratio implies
    /// would leave these tests passing on a document that does not render
    /// deeply enough to mean anything.
    /// </summary>
    [Fact]
    public void TextAndNodeBudgets_StayAboveTheCrashThreshold()
    {
        Assert.True(
            SpecLimits.MaxTotalTextLength >= 5_336,
            $"MaxTotalTextLength is {SpecLimits.MaxTotalTextLength}. DefaultMarginsWideGlyphRepro renders at a " +
            "measured 0.75 pages per character, not the 1 an earlier version of this sentinel assumed, so " +
            "DefaultMarginsWideGlyphRepro_RendersDeeply's own PageCount > 4,000 assertion needs at least 5,336 " +
            "characters (floor(chars / 4) * 3 > 4,000) to pass; anything from 4,500 up to 5,335 satisfied the " +
            "old, wrong floor here while still failing that render assertion, with an opaque Assert.True(false) " +
            "rather than this message.");

        Assert.True(
            SpecLimits.MaxWalkedNodes >= 4_951,
            $"MaxWalkedNodes is {SpecLimits.MaxWalkedNodes}, too few for the 4,951-node list reproduction below.");
    }

    [Fact]
    public void DefaultMarginsWideGlyphRepro_RendersDeeply() =>
        Assert.True(PageCount(SpecRenderer.Render(DefaultMarginsWideGlyphRepro())) > 4_000);

    [Fact]
    public void ManyShortListItemsRepro_RendersDeeply() =>
        Assert.True(PageCount(SpecRenderer.Render(ManyShortListItemsRepro())) > 4_000);

    /// <summary>
    /// The same list document carrying a footer, which exercises the library's
    /// page-counting pass. The band's <see cref="RunningBandSpec.Height"/> is
    /// set explicitly to 30 points: an unset band reserves enough of this
    /// 200-point page that no line fits beneath it at all, and the library
    /// then refuses the element as too tall rather than paginating it, which
    /// would test nothing. Measured: 4,950 pages on 2.3.1, exit 127 on 2.3.0.
    /// </summary>
    [Fact]
    public void ManyShortListItemsWithFooter_RendersDeeply()
    {
        var withFooter = ManyShortListItemsRepro() with
        {
            Footer = new RunningBandSpec
            {
                Template = "Page {page} of {pages}",
                Style = OneLinePerPageStyle(),
                Height = 30,
            },
        };

        Assert.True(PageCount(SpecRenderer.Render(withFooter)) > 4_000);
    }
}

/// <summary>
/// <see cref="SpecLimits.MaxTotalTextLength"/> bounds the TOTAL characters a
/// specification carries, which no per-string or per-collection cap can do on
/// its own: two runs of 60,000 characters each sit under
/// <see cref="SpecLimits.MaxTextLength"/> individually and under every
/// breadth cap, but sum to more than <see cref="SpecLimits.MaxTotalTextLength"/>.
/// </summary>
public class TotalTextLengthLimitTests
{
    private static TextStyleSpec Style() => new() { Font = FontSpec.FromStandard14(Standard14.Helvetica) };

    [Fact]
    public void TwoRunsIndividuallyUnderMaxTextLength_ButOverTotal_ThrowsAtConstruction()
    {
        var half = SpecLimits.MaxTotalTextLength / 2 + 100;
        Assert.True(half < SpecLimits.MaxTextLength, "Test setup: each run must stay under the per-string cap.");

        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content =
                [
                    ParagraphSpec.FromText(new string('a', half), Style()),
                    ParagraphSpec.FromText(new string('b', half), Style()),
                ],
            });

        Assert.Contains("characters", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExactlyMaxTotalTextLengthInOneRun_Constructs()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [ParagraphSpec.FromText(new string('a', SpecLimits.MaxTotalTextLength), Style())],
        };

        Assert.NotNull(spec);
    }

    /// <summary>
    /// Round nine review, Medium 3: <see cref="TextStyleSpec.LinkUri"/> is
    /// reachable through <see cref="DocumentSpec.Content"/> at every run of a
    /// paragraph but was never counted toward <see cref="SpecLimits.MaxTotalTextLength"/>.
    /// Ten runs, each carrying only a one-character <see cref="TextRunSpec.Text"/>
    /// but a distinct, maximal-length (<see cref="SpecLimits.MaxUriLength"/>,
    /// 2,048) <see cref="TextStyleSpec.LinkUri"/>, sum to 10 characters of
    /// TEXT but 20,490 characters once the links are counted too, over the
    /// limit; before this fix, only the 10 was counted and this constructed
    /// successfully.
    /// </summary>
    [Fact]
    public void ManyRunsWithMaximalLinkUri_ThrowsAtConstruction()
    {
        var uri = "https://example.com/" + new string('a', SpecLimits.MaxUriLength - "https://example.com/".Length);
        Assert.Equal(SpecLimits.MaxUriLength, uri.Length);

        List<TextRunSpec> runs = [.. Enumerable.Range(0, 10).Select(_ =>
            new TextRunSpec("x", new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica), LinkUri = uri }))];

        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content = [new ParagraphSpec { Runs = runs }],
            });

        Assert.Contains("characters", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Round nine review, Medium 3: <see cref="ListItemSpec.Language"/> is
    /// reachable through <see cref="DocumentSpec.Content"/> at every depth a
    /// list can nest to but was never counted toward
    /// <see cref="SpecLimits.MaxTotalTextLength"/>. 600 list items, each with
    /// empty <see cref="ListItemSpec.Text"/> but a maximal-length
    /// (<see cref="SpecLimits.MaxLanguageTagLength"/>, 35) <see cref="ListItemSpec.Language"/>,
    /// sum to zero characters of TEXT but 21,000 once the language tags are
    /// counted too, over the limit; before this fix, only the (zero) text
    /// length was counted and this constructed successfully.
    /// </summary>
    [Fact]
    public void ManyListItemsWithMaximalLanguage_ThrowsAtConstruction()
    {
        var language = new string('a', SpecLimits.MaxLanguageTagLength);
        List<ListItemSpec> items = [.. Enumerable.Range(0, 600).Select(_ => new ListItemSpec { Text = "", Language = language })];

        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content = [new ListSpec { Style = ListStyle.Unordered, Items = items }],
            });

        Assert.Contains("characters", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Measured directly: <c>HeadingRenderer.HeadingStructType</c> clamps every level outside this range to the same structure type an H6 heading gets, so the model rejects them instead of letting the library silently clamp.</summary>
public class HeadingLevelValidationTests
{
    [Theory]
    [InlineData(-5)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void OutOfRangeLevel_ThrowsAtConstruction(int level)
    {
        Assert.Throws<ArgumentException>(() => new HeadingSpec { Text = "x", Level = level });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void BoundaryLevels_Construct(int level)
    {
        var heading = new HeadingSpec { Text = "x", Level = level };
        Assert.Equal(level, heading.Level);
    }
}

public class TextStyleNumericValidationTests
{
    private static FontSpec Font() => FontSpec.FromStandard14(Standard14.Helvetica);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidFontSize_ThrowsAtConstruction(double fontSize)
    {
        Assert.Throws<ArgumentException>(() => new TextStyleSpec { Font = Font(), FontSize = fontSize });
    }

    [Fact]
    public void FontSizeAboveMax_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new TextStyleSpec { Font = Font(), FontSize = SpecLimits.MaxFontSize + 1 });
    }

    [Fact]
    public void FontSizeAtMax_Constructs()
    {
        var style = new TextStyleSpec { Font = Font(), FontSize = SpecLimits.MaxFontSize };
        Assert.Equal(SpecLimits.MaxFontSize, style.FontSize);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidLeading_ThrowsAtConstruction(double leading)
    {
        Assert.Throws<ArgumentException>(() => new TextStyleSpec { Font = Font(), Leading = leading });
    }

    [Fact]
    public void LeadingAboveMax_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new TextStyleSpec { Font = Font(), Leading = SpecLimits.MaxLeadingPoints + 1 });
    }

    [Theory]
    [InlineData(-0.1, 0, 0)]
    [InlineData(0, 1.1, 0)]
    [InlineData(0, 0, double.NaN)]
    public void InvalidColor_ThrowsAtConstruction(double r, double g, double b)
    {
        Assert.Throws<ArgumentException>(() => new TextStyleSpec { Font = Font(), Color = new ColorRgb(r, g, b) });
    }
}

/// <summary><see cref="EdgeInsets"/> is accepted directly, or optionally, on nine different members. A handful of representative sites, plus the document's own <see cref="DocumentSpec.Margins"/>, cover the shared validator.</summary>
public class EdgeInsetsValidationTests
{
    private static TextStyleSpec Style() => new() { Font = FontSpec.FromStandard14(Standard14.Helvetica) };

    [Fact]
    public void DocumentMargins_NaNComponent_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                Margins = new EdgeInsets(double.NaN, 0, 0, 0),
                DefaultTextStyle = Style(),
                Content = [new PlainTextSpec { Text = "x" }],
            });
    }

    [Fact]
    public void DocumentMargins_NegativeComponent_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                Margins = new EdgeInsets(-1, 0, 0, 0),
                DefaultTextStyle = Style(),
                Content = [new PlainTextSpec { Text = "x" }],
            });
    }

    [Fact]
    public void DocumentMargins_ComponentAboveMax_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                Margins = new EdgeInsets(SpecLimits.MaxEdgeInsetPoints + 1),
                DefaultTextStyle = Style(),
                Content = [new PlainTextSpec { Text = "x" }],
            });
    }

    [Fact]
    public void HeadingMargins_InfiniteComponent_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new HeadingSpec { Text = "x", Level = 0, Margins = new EdgeInsets(double.PositiveInfinity, 0, 0, 0) });
    }

    [Fact]
    public void CellPadding_NegativeComponent_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new TableCellSpec { Content = "x", Padding = new EdgeInsets(0, -1, 0, 0) });
    }

    [Fact]
    public void MaximumEdgeInset_Constructs()
    {
        var heading = new HeadingSpec { Text = "x", Level = 0, Margins = new EdgeInsets(SpecLimits.MaxEdgeInsetPoints) };
        Assert.NotNull(heading.Margins);
    }
}

public class PieChartNumericValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void InvalidDiameter_ThrowsAtConstruction(double diameter)
    {
        Assert.Throws<ArgumentException>(() => new PieChartSpec { Diameter = diameter, Slices = [new PieSlice(1, ColorRgb.Black)] });
    }

    [Fact]
    public void DiameterAboveMax_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new PieChartSpec { Diameter = SpecLimits.MaxPieChartDiameterPoints + 1, Slices = [new PieSlice(1, ColorRgb.Black)] });
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void InvalidStrokeWidth_ThrowsAtConstruction(double strokeWidth)
    {
        Assert.Throws<ArgumentException>(() =>
            new PieChartSpec { Diameter = 100, StrokeWidth = strokeWidth, Slices = [new PieSlice(1, ColorRgb.Black)] });
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidStartAngle_ThrowsAtConstruction(double startAngle)
    {
        Assert.Throws<ArgumentException>(() =>
            new PieChartSpec { Diameter = 100, StartAngle = startAngle, Slices = [new PieSlice(1, ColorRgb.Black)] });
    }

    [Fact]
    public void StartAngleBeyondMagnitude_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new PieChartSpec { Diameter = 100, StartAngle = SpecLimits.MaxAngleMagnitudeRadians + 1, Slices = [new PieSlice(1, ColorRgb.Black)] });
    }

    [Fact]
    public void NegativeSliceValue_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new PieChartSpec { Diameter = 100, Slices = [new PieSlice(-1, ColorRgb.Black)] });
    }

    [Fact]
    public void SliceColorOutOfRange_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new PieChartSpec { Diameter = 100, Slices = [new PieSlice(1, new ColorRgb(1.5, 0, 0))] });
    }

    [Fact]
    public void SliceLabelAboveMaxTextLength_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new PieChartSpec { Diameter = 100, Slices = [new PieSlice(1, ColorRgb.Black, new string('a', SpecLimits.MaxTextLength + 1))] });
    }
}

public class TableCellSpanValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ColSpanBelowOne_ThrowsAtConstruction(int colSpan)
    {
        Assert.Throws<ArgumentException>(() => new TableCellSpec { Content = "x", ColSpan = colSpan });
    }

    [Fact]
    public void ColSpanAboveMaxCellsPerRow_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new TableCellSpec { Content = "x", ColSpan = SpecLimits.MaxTableCellsPerRow + 1 });
    }

    [Fact]
    public void RowSpanAboveMaxTableRows_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new TableCellSpec { Content = "x", RowSpan = SpecLimits.MaxTableRows + 1 });
    }

    [Fact]
    public void MaximumSpans_Construct()
    {
        var cell = new TableCellSpec { Content = "x", ColSpan = SpecLimits.MaxTableCellsPerRow, RowSpan = SpecLimits.MaxTableRows };
        Assert.Equal(SpecLimits.MaxTableCellsPerRow, cell.ColSpan);
    }
}

public class ImageDimensionValidationTests
{
    private static byte[] OnePixelPng { get; } = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void InvalidWidth_ThrowsAtConstruction(double width)
    {
        Assert.Throws<ArgumentException>(() => new ImageSpec { Format = ImageFormat.Png, Bytes = OnePixelPng, Width = width });
    }

    [Fact]
    public void HeightAboveMax_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new ImageSpec { Format = ImageFormat.Png, Bytes = OnePixelPng, Height = SpecLimits.MaxImageDimensionPoints + 1 });
    }
}

public class ListIndentValidationTests
{
    [Fact]
    public void NegativeIndent_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new ListSpec { Style = ListStyle.Unordered, Items = [], Indent = -1 });
    }

    [Fact]
    public void IndentAboveMax_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new ListSpec { Style = ListStyle.Unordered, Items = [], Indent = SpecLimits.MaxIndentPoints + 1 });
    }
}

/// <summary>Shared width cap: <see cref="LineSeparatorSpec.LineWidth"/>, <see cref="TableSpec.BorderWidth"/> and <see cref="PieChartSpec.StrokeWidth"/> all measure a stroke in points.</summary>
public class StrokeWidthValidationTests
{
    [Fact]
    public void LineSeparatorLineWidth_Negative_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new LineSeparatorSpec { LineWidth = -1 });
    }

    [Fact]
    public void TableBorderWidth_AboveMax_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new TableSpec { Rows = [new TableRowSpec { Cells = [new TableCellSpec { Content = "x" }] }], BorderWidth = SpecLimits.MaxStrokeWidthPoints + 1 });
    }
}

/// <summary>
/// Cycle 6 review, part 2: <see cref="FontSpec.EmbeddedFontIndex"/> is
/// meaningful only relative to <see cref="DocumentSpec.EmbeddedFonts"/>, and
/// an out-of-range index was previously accepted at construction: measured
/// directly, <see cref="SpecRenderer.Render"/> then threw a bare
/// <see cref="ArgumentOutOfRangeException"/> naming only <c>index</c>, while
/// <see cref="SpecCodeEmitter.Emit"/> returned code referencing a local it
/// never declared. Both consumers now call
/// <see cref="DocumentSpec.ValidateEmbeddedFontReferences"/> first, so the
/// same specification fails the same way on both sides.
/// </summary>
public class EmbeddedFontIndexValidationTests
{
    [Fact]
    public void NegativeIndex_ThrowsAtFontSpecConstruction()
    {
        Assert.Throws<ArgumentException>(() => FontSpec.FromEmbedded(-1));
    }

    private static DocumentSpec SpecWithOutOfRangeContentReference() => new()
    {
        Page = new PageSizeSpec(200, 200),
        DefaultTextStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica) },
        Content = [ParagraphSpec.FromText("x", new TextStyleSpec { Font = FontSpec.FromEmbedded(0) })],
    };

    [Fact]
    public void OutOfRangeContentReference_ConstructsButRenderThrows()
    {
        var spec = SpecWithOutOfRangeContentReference();
        var exception = Assert.Throws<ArgumentException>(() => SpecRenderer.Render(spec));
        Assert.Contains("FromEmbedded", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OutOfRangeContentReference_ConstructsButEmitThrows()
    {
        var spec = SpecWithOutOfRangeContentReference();
        var exception = Assert.Throws<ArgumentException>(() => SpecCodeEmitter.Emit(spec));
        Assert.Contains("FromEmbedded", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OutOfRangeDefaultTextStyleReference_ThrowsOnRender()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = new TextStyleSpec { Font = FontSpec.FromEmbedded(0) },
            Content = [new PlainTextSpec { Text = "x" }],
        };

        Assert.Throws<ArgumentException>(() => SpecRenderer.Render(spec));
    }

    [Fact]
    public void OutOfRangeHeaderReference_ThrowsOnRender()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica) },
            Content = [new PlainTextSpec { Text = "x" }],
            Header = new RunningBandSpec { Template = "h", Style = new TextStyleSpec { Font = FontSpec.FromEmbedded(0) } },
        };

        Assert.Throws<ArgumentException>(() => SpecRenderer.Render(spec));
    }

    /// <summary>
    /// Cycle 7 review: deleting <see cref="DocumentSpec.ValidateBandFontReference"/>'s
    /// call for <see cref="DocumentSpec.Footer"/> left the suite green,
    /// because nothing exercised it; only <see cref="DocumentSpec.Header"/>
    /// and <see cref="DocumentSpec.DefaultTextStyle"/> had a regression guard
    /// of their own. This closes that gap.
    /// </summary>
    [Fact]
    public void OutOfRangeFooterReference_ThrowsOnRender()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica) },
            Content = [new PlainTextSpec { Text = "x" }],
            Footer = new RunningBandSpec { Template = "f", Style = new TextStyleSpec { Font = FontSpec.FromEmbedded(0) } },
        };

        var exception = Assert.Throws<ArgumentException>(() => SpecRenderer.Render(spec));
        Assert.Contains("Footer", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The <see cref="Generation.SpecCodeEmitter.Emit"/> counterpart to <see cref="OutOfRangeFooterReference_ThrowsOnRender"/>, for the same symmetry <see cref="OutOfRangeContentReference_ConstructsButEmitThrows"/> already covers.</summary>
    [Fact]
    public void OutOfRangeFooterReference_ThrowsOnEmit()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica) },
            Content = [new PlainTextSpec { Text = "x" }],
            Footer = new RunningBandSpec { Template = "f", Style = new TextStyleSpec { Font = FontSpec.FromEmbedded(0) } },
        };

        var exception = Assert.Throws<ArgumentException>(() => SpecCodeEmitter.Emit(spec));
        Assert.Contains("Footer", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A style set BEFORE <see cref="DocumentSpec.EmbeddedFonts"/> in the
    /// object initializer must still be checked correctly once
    /// <see cref="DocumentSpec.EmbeddedFonts"/> is later removed or shrunk:
    /// this is exactly the ordering several samples in this repository use
    /// (<see cref="DocumentSpecSamples.PdfA2bWithOutputIntent"/> among them),
    /// which is why the check cannot live in any one property's own
    /// <see langword="init"/> accessor. See the remark on
    /// <see cref="DocumentSpec.ValidateEmbeddedFontReferences"/>.
    /// </summary>
    [Fact]
    public void DefaultTextStyleSetBeforeEmbeddedFonts_StillValidatedCorrectly()
    {
        var style = new TextStyleSpec { Font = FontSpec.FromEmbedded(0) };

        // DefaultTextStyle is written before EmbeddedFonts here, matching the
        // real-world sample this regression guards.
        var validSpec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = style,
            EmbeddedFonts = [[0x00, 0x01, 0x00, 0x00]],
            Content = [new PlainTextSpec { Text = "x", Style = style }],
        };

        // Constructs fine: EmbeddedFonts has one entry, index 0 is in range.
        Assert.NotNull(validSpec);

        var invalidSpec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = style,
            Content = [new PlainTextSpec { Text = "x", Style = style }],
        };

        // No EmbeddedFonts at all this time: index 0 is now out of range, and
        // must still be caught, even though DefaultTextStyle was written
        // first in both initializers.
        Assert.Throws<ArgumentException>(() => SpecRenderer.Render(invalidSpec));
    }
}

/// <summary>
/// Cycle 6 review, part 2: <see cref="EncryptionSpec.Permissions"/> accepted
/// any raw value, not only a union of the library's named flags. Measured
/// directly: a raw value with an undefined bit set made
/// <c>SpecCodeEmitter.EmitPermissions</c> either emit invalid C# (a lone
/// comma, for a value naming no flag at all) or silently drop that bit from
/// the displayed code while the renderer still applied it.
/// </summary>
public class PermissionsUnionValidationTests
{
    [Fact]
    public void RawValueWithUndefinedBit_ThrowsAtConstruction()
    {
        // Bit 1 (raw value 2) is not any named PdfPermissions flag.
        var undefined = (PdfPermissions)2;
        Assert.Throws<ArgumentException>(() => new EncryptionSpec { Permissions = undefined });
    }

    [Fact]
    public void NamedFlagCombinedWithUndefinedBit_ThrowsAtConstruction()
    {
        // Print (4) combined with the same undefined bit 1 (2) = raw 6.
        var mixed = (PdfPermissions)6;
        Assert.Throws<ArgumentException>(() => new EncryptionSpec { Permissions = mixed });
    }

    [Fact]
    public void UnionOfNamedFlags_Constructs()
    {
        var spec = new EncryptionSpec { Permissions = PdfPermissions.Print | PdfPermissions.Copy };
        Assert.Equal(PdfPermissions.Print | PdfPermissions.Copy, spec.Permissions);
    }

    /// <summary>Verified by measurement against the shipped package (see <c>permcheck</c> probe): the union of every named flag other than <c>None</c> and <c>All</c> equals <c>All</c> exactly.</summary>
    [Fact]
    public void AllNamedFlags_EqualsPdfPermissionsAll()
    {
        var union = Enum.GetValues<PdfPermissions>()
            .Where(flag => flag is not (PdfPermissions.None or PdfPermissions.All))
            .Aggregate(PdfPermissions.None, (acc, flag) => acc | flag);

        Assert.Equal(PdfPermissions.All, union);
    }
}

/// <summary>
/// Cycle 6 review, part 3: the owner-password rule compared the two passwords
/// as STRINGS, but the library truncates a password to 127 UTF-8 bytes before
/// deriving key material from it (<c>StandardSecurityHandler.PasswordBytes</c>).
/// Measured directly against the shipped package: a 127-byte password and a
/// 128-byte password sharing the same first 127 bytes authenticate
/// IDENTICALLY, so a user password of 127 characters with an owner password
/// of 128 was accepted by the model while being byte-identical to the
/// library, reproducing the exact defect the rule exists to prevent.
/// </summary>
public class OwnerPasswordByteTruncationTests
{
    private static TextStyleSpec Style() => new() { Font = FontSpec.FromStandard14(Standard14.Helvetica) };

    private static DocumentSpec DocumentWithEncryption(EncryptionSpec encryption) => new()
    {
        Page = new PageSizeSpec(200, 200),
        DefaultTextStyle = Style(),
        Content = [new PlainTextSpec { Text = "x" }],
        Encryption = encryption,
    };

    [Fact]
    public void OwnerPasswordSharingFirst127BytesWithUserPassword_ThrowsAtConstruction()
    {
        var userPassword = new string('a', 127);
        var ownerPassword = new string('a', 128); // Same first 127 bytes, one byte longer.

        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithEncryption(new EncryptionSpec { UserPassword = userPassword, OwnerPassword = ownerPassword, Permissions = PdfPermissions.Print }));
        Assert.Contains("OwnerPassword", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnerPasswordDifferingWithinFirst127Bytes_Constructs()
    {
        var userPassword = new string('a', 126);
        var ownerPassword = new string('a', 126) + "c"; // Differs at byte 127, which is not truncated away.

        var spec = DocumentWithEncryption(new EncryptionSpec { UserPassword = userPassword, OwnerPassword = ownerPassword, Permissions = PdfPermissions.Print });

        Assert.NotNull(spec.Encryption);
    }

    [Fact]
    public void OwnerPasswordAndUserPasswordBothLongAndIdenticalPast127Bytes_ThrowsAtConstruction()
    {
        var userPassword = new string('a', 200);
        var ownerPassword = new string('a', 200); // Identical outright, well past the truncation point.

        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithEncryption(new EncryptionSpec { UserPassword = userPassword, OwnerPassword = ownerPassword, Permissions = PdfPermissions.Print }));
        Assert.Contains("OwnerPassword", exception.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// Cycle 6 review, part 3: <see cref="SpecAssets.FromSpec"/> handed back the
/// very arrays <see cref="SpecLimits.ValidateAssetBytes"/> already cloned at
/// <see cref="DocumentSpec"/> construction, reopening the immutability hole
/// from the other direction: a caller holding a <see cref="SpecAssets"/>
/// array could mutate it in place and corrupt the bytes
/// <see cref="Generation.SpecRenderer"/> goes on to render.
/// </summary>
public class SpecAssetsImmutabilityTests
{
    private static byte[] OnePixelPng { get; } = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public void EmbeddedFontsEntry_IsNotTheSameArrayInstanceAsTheSpec()
    {
        var fontBytes = new byte[] { 1, 2, 3, 4 };
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica) },
            Content = [new PlainTextSpec { Text = "x" }],
            EmbeddedFonts = [fontBytes],
        };

        var assets = SpecAssets.FromSpec(spec);

        Assert.NotSame(spec.EmbeddedFonts[0], assets.EmbeddedFonts[0]);
        assets.EmbeddedFonts[0][0] = 99;
        Assert.Equal(1, spec.EmbeddedFonts[0][0]);
    }

    [Fact]
    public void ImagesEntry_IsNotTheSameArrayInstanceAsTheSpec()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica) },
            Content = [new ImageSpec { Format = ImageFormat.Png, Bytes = OnePixelPng }],
        };

        var assets = SpecAssets.FromSpec(spec);
        var storedImage = (ImageSpec)spec.Content[0];

        Assert.NotSame(storedImage.Bytes, assets.Images[0]);
        var originalFirstByte = storedImage.Bytes[0];
        assets.Images[0][0] = unchecked((byte)(assets.Images[0][0] + 1));
        Assert.Equal(originalFirstByte, storedImage.Bytes[0]);
    }
}

/// <summary>
/// Cycle 6 review, part 3: <c>Document.Encrypt</c> and <c>Document.Save</c>
/// were called unwrapped, so a bad combination of settings could throw any of
/// four different exception types depending on which rule it broke, while
/// every OTHER failure path in <see cref="SpecRenderer.Render"/> already
/// normalised to <see cref="InvalidOperationException"/>. This is now
/// uniform: see the remark on <see cref="SpecRenderer.Render"/> for the full
/// contract.
/// </summary>
public class SpecRendererExceptionUniformityTests
{
    private static TextStyleSpec Style() => new() { Font = FontSpec.FromStandard14(Standard14.Helvetica) };

    /// <summary>
    /// Measured directly against the shipped library: <c>Document.Save</c>
    /// throws a raw <see cref="NotSupportedException"/>, not
    /// <see cref="InvalidOperationException"/>, when object streams and
    /// encryption are combined. The model deliberately does not guard this
    /// combination at construction (see the remark on
    /// <see cref="DocumentSpec.UseObjectStreams"/>), so it is exactly the
    /// case this wrapping exists for.
    /// </summary>
    [Fact]
    public void ObjectStreamsCombinedWithEncryption_ThrowsInvalidOperationException()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new PlainTextSpec { Text = "x" }],
            UseObjectStreams = true,
            Encryption = new EncryptionSpec { UserPassword = "secret" },
        };

        Assert.Throws<InvalidOperationException>(() => SpecRenderer.Render(spec));
    }
}

/// <summary>
/// A running band is laid out once per page, so
/// <see cref="RunningBandSpec.Template"/>'s length is multiplied by page
/// count, a multiplication <see cref="SpecLimits.MaxTotalTextLength"/> and
/// <see cref="SpecLimits.MaxWalkedNodes"/> cannot see because
/// <see cref="DocumentSpec.Header"/> and <see cref="DocumentSpec.Footer"/>
/// sit outside <see cref="DocumentSpec.Content"/>'s walk. See the remark on
/// <see cref="SpecLimits.MaxRunningBandTemplateLength"/> for the measurements
/// this class checks against.
/// </summary>
public class RunningBandTemplateCapTests
{
    private static TextStyleSpec Style() => new()
    {
        Font = FontSpec.FromStandard14(Standard14.Helvetica),
        FontSize = 36,
        Leading = 50,
    };

    [Fact]
    public void TemplateAtCap_Constructs()
    {
        var band = new RunningBandSpec
        {
            Template = new string('a', SpecLimits.MaxRunningBandTemplateLength),
            Style = Style(),
        };

        Assert.Equal(SpecLimits.MaxRunningBandTemplateLength, band.Template.Length);
    }

    [Fact]
    public void TemplateOverCap_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() => new RunningBandSpec
        {
            Template = new string('a', SpecLimits.MaxRunningBandTemplateLength + 1),
            Style = Style(),
        });

        Assert.Contains("characters", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The exact shape used to measure the running band template cap's cost: a 20,000
    /// by 260 point page, 55-point margins, the 36-point/50-point-leading
    /// style, one list of 1,650 items each with two children, and both a
    /// header and a footer with <see cref="RunningBandSpec.Height"/> set to
    /// 30, all rendering 4,950 pages. The template on both bands is set to
    /// exactly <see cref="SpecLimits.MaxRunningBandTemplateLength"/>
    /// characters, the largest this model now admits. MEASURED at that cap's
    /// current value of 200: 226 ms. If the cap were removed (falling back to
    /// <see cref="SpecLimits.MaxTextLength"/>, 100,000) or widened toward it,
    /// this same shape measured 5,518 ms; the assertion below catches either
    /// change by timing out long before that. Under the fix, this test itself
    /// stays fast: the point is the cap holding, not a long render.
    /// </summary>
    [Fact]
    public void MaximalTemplateOnDeepPagination_RendersWithinBudget()
    {
        var style = Style();

        List<ListItemSpec> topLevelItems = [];
        for (var i = 0; i < 1_650; i++)
        {
            topLevelItems.Add(new ListItemSpec
            {
                Text = "W",
                Children = [new ListItemSpec { Text = "W" }, new ListItemSpec { Text = "W" }],
            });
        }

        var band = new RunningBandSpec
        {
            Template = new string('a', SpecLimits.MaxRunningBandTemplateLength),
            Style = style,
            Height = 30,
        };

        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(20_000, 260),
            Margins = new EdgeInsets(55),
            DefaultTextStyle = style,
            Content = [new ListSpec { Style = ListStyle.Unordered, DefaultStyle = style, Items = topLevelItems }],
            Header = band,
            Footer = band,
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var bytes = SpecRenderer.Render(spec);
        sw.Stop();

        Assert.NotEmpty(bytes);
        Assert.True(
            sw.ElapsedMilliseconds < 2_000,
            $"Rendering took {sw.ElapsedMilliseconds} ms. Measured at MaxRunningBandTemplateLength=200 this takes " +
            "about 226 ms; a template anywhere near MaxTextLength (100,000) on this same shape takes about " +
            "5,518 ms, so a budget of 2,000 ms catches a removed or substantially widened cap.");
    }

    /// <summary>
    /// No test elsewhere in this repository sets <see cref="TextStyleSpec.LinkUri"/>
    /// on a <see cref="RunningBandSpec.Style"/> at all, yet the remark on
    /// <see cref="SpecLimits.MaxRunningBandTemplateLength"/> depends entirely
    /// on the library never turning such a link into a per-page annotation.
    /// If it ever did, a maximal <see cref="TextStyleSpec.LinkUri"/>
    /// (<see cref="SpecLimits.MaxUriLength"/>, 2,048 characters) on a header
    /// or footer style would be multiplied by page count exactly as an
    /// unbounded <see cref="RunningBandSpec.Template"/> once was, admitting
    /// up to 2,048 characters times roughly eleven thousand pages with
    /// nothing in this file positioned to catch it.
    /// </summary>
    /// <remarks>
    /// This renders the same multi-page document with and without a maximal
    /// <see cref="TextStyleSpec.LinkUri"/> on the footer style and asserts
    /// the two outputs normalize to identical text. A raw byte comparison
    /// cannot be used: two renders are never byte-identical, because the
    /// library writes a random document identifier on every render (see
    /// <see cref="PdfNormalization"/>), so the comparison below goes through
    /// that normalization rather than through the raw bytes. If the library
    /// ever starts emitting a per-page link annotation, the two normalized
    /// outputs stop matching and this turns red, rather than the exemption
    /// remaining an untested assumption.
    /// </remarks>
    [Fact]
    public void MaximalLinkUriOnFooterStyle_AddsNothingAcrossManyPages()
    {
        var bodyStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica), FontSize = 36, Leading = 50 };

        List<ListItemSpec> items = [];
        for (var i = 0; i < 80; i++)
        {
            items.Add(new ListItemSpec { Text = "W" });
        }

        var linkUri = "https://example.com/" + new string('a', SpecLimits.MaxUriLength - "https://example.com/".Length);
        Assert.Equal(SpecLimits.MaxUriLength, linkUri.Length);

        DocumentSpec Build(string? footerLinkUri)
        {
            var footerStyle = new TextStyleSpec
            {
                Font = FontSpec.FromStandard14(Standard14.Helvetica),
                FontSize = 36,
                Leading = 50,
                LinkUri = footerLinkUri,
            };

            return new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                Margins = new EdgeInsets(20),
                DefaultTextStyle = bodyStyle,
                Content = [new ListSpec { Style = ListStyle.Unordered, DefaultStyle = bodyStyle, Items = items }],
                Footer = new RunningBandSpec
                {
                    Template = "Page {page} of {pages}",
                    Style = footerStyle,
                    Height = 30,
                },
            };
        }

        var withoutLink = PdfNormalization.Normalize(SpecRenderer.Render(Build(null)));
        var withLink = PdfNormalization.Normalize(SpecRenderer.Render(Build(linkUri)));

        Assert.Equal(withoutLink, withLink);
    }
}

/// <summary>
/// <see cref="ImageSignature.Matches"/>'s defensive default arm is
/// unreachable through any <see cref="DocumentSpec"/>, because
/// <see cref="ImageSpec.Format"/> validates against the same
/// <see cref="ImageFormat"/> enumeration at construction, so no
/// specification this model admits can carry an undefined value there. That
/// also puts it outside the branch coverage gate in
/// <c>eng/check-emitter-branch-coverage.ps1</c>, which instruments only the
/// <c>VellumPdfShowcase.Web.Generation</c> namespace, while
/// <see cref="ImageSignature"/> lives in <c>VellumPdfShowcase.Web.Model</c>.
/// <see cref="ImageSignature.Matches"/> is public, so it can be, and is,
/// exercised directly here rather than left as the one branch in the
/// repository that no test reaches and no gate measures.
/// </summary>
public class ImageSignatureTests
{
    [Fact]
    public void UndefinedFormat_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageSignature.Matches((ImageFormat)99, [0x00]));
    }
}
