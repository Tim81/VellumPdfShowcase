using VellumPdf.Encryption;
using VellumPdf.Fonts;
using VellumPdf.Layout.Core;
using VellumPdf.Layout.Elements;
using DocumentConformance = VellumPdf.Document.PdfConformance;

namespace VellumPdfShowcase.Web.Model;

/// <summary>
/// An immutable description of a document the visitor has selected in the
/// showcase. <see cref="Generation.SpecRenderer"/> turns a <see cref="DocumentSpec"/>
/// into PDF bytes by calling <c>VellumPdf.Layout</c> directly, and
/// <see cref="Generation.SpecCodeEmitter"/> turns the same instance into the C#
/// a developer would write to obtain the identical document. Neither type may
/// diverge from the other, because both read only this record.
/// </summary>
/// <remarks>
/// Scope is bounded deliberately: every member here corresponds to a capability
/// recorded as available in the library's 2.3.0 capability table. Nothing that
/// table lists as absent (explicit page breaks, nested tables, inline runs in
/// headings or list items, multi-string headers and footers, shapes beyond a
/// line separator, and so on) has a representation, so an invalid combination
/// simply cannot be constructed.
/// </remarks>
public sealed record DocumentSpec
{
    /// <summary>The page size, in PDF points.</summary>
    public required PageSizeSpec Page { get; init; }

    /// <summary>
    /// The page margins, applied on all four edges of every page. Defaults to
    /// 72 points (one inch) on every edge, matching <c>Document</c>'s own
    /// default exactly, for the same reason given on <see cref="PieChartSpec.StartAngle"/>.
    /// </summary>
    public EdgeInsets Margins { get; init; } = new(72);

    /// <summary>The style applied to elements that do not specify their own.</summary>
    public required TextStyleSpec DefaultTextStyle { get; init; }

    /// <summary>
    /// Raw TrueType font bytes for every embedded face the document uses. A
    /// <see cref="FontSpec"/> of kind <see cref="FontKind.Embedded"/> refers to
    /// one of these by position. Each entry is registered with the underlying
    /// <c>Document</c> exactly once, regardless of how many styles reference it.
    /// </summary>
    public IReadOnlyList<byte[]> EmbeddedFonts { get; init; } = [];

    /// <summary>The document's content, laid out in the order given.</summary>
    public IReadOnlyList<ContentItemSpec> Content { get; init; } = [];

    /// <summary>The running header repeated on every page, if any.</summary>
    public RunningBandSpec? Header { get; init; }

    /// <summary>The running footer repeated on every page, if any.</summary>
    public RunningBandSpec? Footer { get; init; }

    /// <summary>The conformance profile the document claims, or <see cref="DocumentConformance.None"/>.</summary>
    public DocumentConformance Conformance { get; init; } = DocumentConformance.None;

    /// <summary>Whether the document carries a tagged structure tree.</summary>
    public bool Tagged { get; init; }

    /// <summary>The document's natural language, as a BCP 47 tag such as <c>"en"</c>.</summary>
    public string? Language { get; init; }

    /// <summary>Document information dictionary entries, if any are set.</summary>
    public DocumentMetadataSpec? Metadata { get; init; }

    /// <summary>The output intent to embed, if any.</summary>
    public OutputIntentSpec? OutputIntent { get; init; }

    /// <summary>Encryption settings, if the document is to be encrypted.</summary>
    public EncryptionSpec? Encryption { get; init; }
}

/// <summary>A page size expressed directly in PDF points.</summary>
public sealed record PageSizeSpec(double WidthPoints, double HeightPoints)
{
    /// <summary>Builds a <see cref="PageSizeSpec"/> from a Kernel <c>PdfRectangle</c>, such as one of the <c>PageSize</c> presets.</summary>
    public static PageSizeSpec FromRectangle(VellumPdf.Document.PdfRectangle rectangle) =>
        new(rectangle.Width, rectangle.Height);
}

/// <summary>Which of the two font sources a <see cref="FontSpec"/> selects.</summary>
public enum FontKind
{
    /// <summary>One of the fourteen built-in faces. No embedding, no font file.</summary>
    Standard14,

    /// <summary>An embedded TrueType face, referenced by index into <see cref="DocumentSpec.EmbeddedFonts"/>.</summary>
    Embedded,
}

/// <summary>
/// A reference to a font, either a <see cref="Standard14"/> face or an embedded
/// TrueType face by position in <see cref="DocumentSpec.EmbeddedFonts"/>. Kept
/// separate from the library's own <c>FontReference</c>, which wraps an
/// <c>EmbeddedFontHandle</c> tied to one already-constructed <c>Document</c> and
/// so cannot be prepared ahead of rendering.
/// </summary>
public sealed record FontSpec
{
    public required FontKind Kind { get; init; }
    public Standard14 Standard14Face { get; init; }
    public int EmbeddedFontIndex { get; init; }

    public static FontSpec FromStandard14(Standard14 face) =>
        new() { Kind = FontKind.Standard14, Standard14Face = face };

    public static FontSpec FromEmbedded(int embeddedFontIndex) =>
        new() { Kind = FontKind.Embedded, EmbeddedFontIndex = embeddedFontIndex };
}

/// <summary>Mirrors the settable members of the library's <c>TextStyle</c>.</summary>
public sealed record TextStyleSpec
{
    public required FontSpec Font { get; init; }
    public double FontSize { get; init; } = 11;
    public double? Leading { get; init; }
    public ColorRgb Color { get; init; } = ColorRgb.Black;
    public string? LinkUri { get; init; }
}

/// <summary>One inline run of a <see cref="ParagraphSpec"/>, matching the library's <c>TextRun</c>.</summary>
public sealed record TextRunSpec(string Text, TextStyleSpec Style);

/// <summary>The base type for one item of ordered document content.</summary>
public abstract record ContentItemSpec;

/// <summary>A <c>Heading</c>. A <see langword="null"/> <see cref="Style"/> lets the library apply automatic styling for the level.</summary>
public sealed record HeadingSpec : ContentItemSpec
{
    public required string Text { get; init; }
    public required int Level { get; init; }
    public TextStyleSpec? Style { get; init; }
    public HorizontalAlignment Alignment { get; init; } = HorizontalAlignment.Left;
    public EdgeInsets? Margins { get; init; }
    public string? BookmarkTitle { get; init; }
    public string? Language { get; init; }
}

/// <summary>
/// A <c>Paragraph</c>. A single-entry <see cref="Runs"/> list is the common
/// case of one style throughout; more entries produce the mixed-style inline
/// runs the library's <c>Paragraph(IEnumerable&lt;TextRun&gt;)</c> constructor accepts.
/// </summary>
public sealed record ParagraphSpec : ContentItemSpec
{
    public required IReadOnlyList<TextRunSpec> Runs { get; init; }
    public HorizontalAlignment Alignment { get; init; } = HorizontalAlignment.Left;
    public EdgeInsets? Margins { get; init; }
    public string? Language { get; init; }

    public static ParagraphSpec FromText(string text, TextStyleSpec style) =>
        new() { Runs = [new TextRunSpec(text, style)] };
}

/// <summary>A <c>ListElement</c>, unordered or one of the three ordered forms.</summary>
public sealed record ListSpec : ContentItemSpec
{
    public required ListStyle Style { get; init; }
    public required IReadOnlyList<ListItemSpec> Items { get; init; }
    public double? Indent { get; init; }
    public EdgeInsets? Margins { get; init; }
    public TextStyleSpec? DefaultStyle { get; init; }
}

/// <summary>One <c>ListItem</c>. Nesting is expressed through <see cref="Children"/>, matching <c>ListItem.AddChild</c>.</summary>
public sealed record ListItemSpec
{
    public required string Text { get; init; }
    public TextStyleSpec? Style { get; init; }
    public string? Language { get; init; }
    public IReadOnlyList<ListItemSpec> Children { get; init; } = [];
}

/// <summary>A <c>TableElement</c>.</summary>
public sealed record TableSpec : ContentItemSpec
{
    public required IReadOnlyList<TableRowSpec> Rows { get; init; }
    public IReadOnlyList<double>? ColumnWidths { get; init; }
    public TextStyleSpec? DefaultCellStyle { get; init; }
    public double? BorderWidth { get; init; }
    public ColorRgb? BorderColor { get; init; }
    public EdgeInsets? Margins { get; init; }
}

/// <summary>
/// One <c>Row</c> of a <see cref="TableSpec"/>. The library declares
/// <c>Row.Background</c>, but it is unreachable from the published API: a
/// <c>Row</c> is obtained only from <c>TableElement.AddRow</c> or
/// <c>AddHeaderRow</c>, both of which return an already-constructed instance,
/// and <c>Background</c> is an <see langword="init"/> property, which the
/// language only allows to be set inside the object-initializer expression
/// that constructs the instance. There is no overload that accepts a
/// caller-built <c>Row</c>, so no external caller can ever set it. Per-row
/// background is therefore not modelled here; per-cell background, set on a
/// <see cref="TableCellSpec"/> the caller constructs directly, is unaffected.
/// </summary>
public sealed record TableRowSpec
{
    public required IReadOnlyList<TableCellSpec> Cells { get; init; }
    public bool IsHeader { get; init; }
}

/// <summary>One <c>Cell</c> of a <see cref="TableRowSpec"/>. Content is plain text; the library does not support nested elements in a cell.</summary>
public sealed record TableCellSpec
{
    public required string Content { get; init; }
    public int ColSpan { get; init; } = 1;
    public int RowSpan { get; init; } = 1;
    public TextStyleSpec? Style { get; init; }
    public EdgeInsets? Padding { get; init; }
    public ColorRgb? Background { get; init; }
    public HorizontalAlignment Alignment { get; init; } = HorizontalAlignment.Left;
    public string? Language { get; init; }
}

/// <summary>The five raster formats the Kernel image loaders accept.</summary>
public enum ImageFormat
{
    Png,
    Jpeg,
    Bmp,
    Gif,
    Tiff,
}

/// <summary>A <c>LayoutImage</c>, decoded through the matching Kernel loader for <see cref="Format"/>.</summary>
public sealed record ImageSpec : ContentItemSpec
{
    public required ImageFormat Format { get; init; }
    public required byte[] Bytes { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public HorizontalAlignment Alignment { get; init; } = HorizontalAlignment.Left;
    public EdgeInsets? Margins { get; init; }
    public string? AltText { get; init; }
}

/// <summary>A <c>PieChart</c>, using the library's own <c>PieSlice</c> value for each slice.</summary>
public sealed record PieChartSpec : ContentItemSpec
{
    public required IReadOnlyList<PieSlice> Slices { get; init; }
    public required double Diameter { get; init; }
    public EdgeInsets? Margins { get; init; }
    public ColorRgb? StrokeColor { get; init; }
    public double StrokeWidth { get; init; } = 0.5;
    public HorizontalAlignment Alignment { get; init; } = HorizontalAlignment.Center;

    /// <summary>
    /// Defaults to <c>π/2</c> (12 o'clock), matching <c>PieChart</c>'s own
    /// default exactly. Every default in this record matches the library's so
    /// that <see cref="Generation.SpecCodeEmitter"/> can safely omit an
    /// unset property from the code it emits, relying on the library to apply
    /// the identical default that <see cref="Generation.SpecRenderer"/> set explicitly.
    /// </summary>
    public double StartAngle { get; init; } = double.Pi / 2;

    public bool Clockwise { get; init; } = true;
    public string? AltText { get; init; }
    public bool Decorative { get; init; }
}

/// <summary>A <c>LineSeparator</c>, the library's only vector primitive in the Layout API.</summary>
public sealed record LineSeparatorSpec : ContentItemSpec
{
    public double LineWidth { get; init; } = 1;
    public ColorRgb Color { get; init; } = ColorRgb.Black;
    public EdgeInsets? Margins { get; init; }
}

/// <summary>
/// A running header or footer. The template is one string substituting
/// <c>{page}</c> and <c>{pages}</c>; the library accepts nothing richer here.
/// </summary>
public sealed record RunningBandSpec
{
    public required string Template { get; init; }
    public required TextStyleSpec Style { get; init; }
    public HorizontalAlignment Alignment { get; init; } = HorizontalAlignment.Center;
    public double? Height { get; init; }
}

/// <summary>Entries for the document's <c>PdfDocumentInfo</c>.</summary>
public sealed record DocumentMetadataSpec
{
    public string? Title { get; init; }
    public string? Author { get; init; }
    public string? Subject { get; init; }
    public string? Keywords { get; init; }
    public string? Creator { get; init; }
    public string? Producer { get; init; }
}

/// <summary>The base type for the two output intents <c>Document</c> can embed.</summary>
public abstract record OutputIntentSpec;

/// <summary>An ICC-based output intent for a PDF/A or PDF/UA claim, matching <c>Document.SetPdfAOutputIntent</c>.</summary>
public sealed record PdfAOutputIntentSpec : OutputIntentSpec
{
    public required byte[] IccProfile { get; init; }
    public required int ComponentCount { get; init; }
    public required string OutputConditionIdentifier { get; init; }
    public string? Info { get; init; }
}

/// <summary>A device CMYK output intent, matching <c>Document.UseCmykOutputIntent</c>.</summary>
public sealed record CmykOutputIntentSpec : OutputIntentSpec
{
    public required string OutputConditionIdentifier { get; init; }
}

/// <summary>Encryption settings, matching <c>PdfEncryptionSettings</c>.</summary>
public sealed record EncryptionSpec
{
    public string? UserPassword { get; init; }
    public string? OwnerPassword { get; init; }
    public PdfPermissions Permissions { get; init; } = PdfPermissions.All;
    public bool EncryptMetadata { get; init; } = true;
}
