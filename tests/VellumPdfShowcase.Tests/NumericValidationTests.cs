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
/// That single gap let a <see cref="PageSizeSpec"/> of (36, 36) with one
/// maximal-length run overflow the CLR stack: recursion depth in the library
/// tracks page count, page count is content divided by page area, and
/// <see cref="PageSizeSpec"/> had no validation at all. These tests cover the
/// numeric caps that close that gap, and pin the worst specification the new
/// caps still permit as a regression guard: without it, a future change could
/// silently reopen the same crash.
/// </summary>
public class PageSizeValidationTests
{
    [Theory]
    [InlineData(199, 300)]
    [InlineData(300, 199)]
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
    /// The exact scenario measured against the shipped library: a 36 x 36
    /// point page, zero margins, and one run of exactly <c>MaxTextLength</c>
    /// characters overflowed the CLR stack with exit code 127, "Stack
    /// overflow.", roughly 3,659 <c>DocumentRenderer.PlaceRenderer</c> frames.
    /// A <see cref="StackOverflowException"/> cannot be caught, so the only
    /// available fix is making the specification impossible to construct in
    /// the first place. This is that regression guard: it must keep throwing
    /// even if every other cap in this file is loosened.
    /// </summary>
    [Fact]
    public void OriginalStackOverflowRepro_PageSize_IsRejectedAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() => new PageSizeSpec(36, 36));
        Assert.Contains("WidthPoints", exception.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// The worst specification every cap in this file together still permits:
/// the smallest allowed page, zero margins, the largest allowed font size,
/// and exactly <see cref="SpecLimits.MaxTotalTextLength"/> characters in one
/// run. Measured directly against the shipped library: this renders
/// successfully, comfortably below the roughly 4,350-frame depth at which
/// this exact geometry overflowed the stack on this machine. This is the
/// regression guard plan section 3.4.0.2 calls a vacuous test if it is
/// missing: without it, the caps above could be verified only by construction
/// succeeding, never by the worst case they still allow actually rendering.
/// </summary>
/// <remarks>
/// <see cref="TextStyleSpec.Leading"/> is deliberately left UNSET below, not
/// set to <see cref="SpecLimits.MaxLeadingPoints"/>. That is the actual worst
/// case, not the intuitive one: see the remark on
/// <see cref="SpecLimits.MaxLeadingPoints"/> and on
/// <see cref="SpecLimits.MaxTotalTextLength"/>. Reintroducing an explicit
/// <c>Leading = SpecLimits.MaxLeadingPoints</c> here would make this test
/// construct a SAFER specification than the actual worst case the caps
/// permit, silently losing the coverage this guard exists for.
/// </remarks>
public class WorstPermittedSpecificationTests
{
    [Fact]
    public void SmallestPageLargestFontAndMaxTotalText_RendersSuccessfully()
    {
        var style = new TextStyleSpec
        {
            Font = FontSpec.FromStandard14(Standard14.Helvetica),
            FontSize = SpecLimits.MaxFontSize,
        };

        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(SpecLimits.MinPageDimensionPoints, SpecLimits.MinPageDimensionPoints),
            Margins = new EdgeInsets(0),
            DefaultTextStyle = style,
            Content = [ParagraphSpec.FromText(new string('a', SpecLimits.MaxTotalTextLength), style)],
        };

        var bytes = SpecRenderer.Render(spec);

        Assert.NotEmpty(bytes);
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
