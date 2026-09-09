using System.Buffers.Binary;
using System.Text;
using VellumPdf.Encryption;
using VellumPdf.Layout.Core;

namespace VellumPdfShowcase.Web.Model;

/// <summary>
/// The size and length caps <see cref="DocumentSpec"/> and its nested records
/// enforce at construction, per plan section 5.4. Generation runs synchronously
/// on the visitor's own tab, so an unbounded specification is a denial of
/// service the visitor inflicts on themselves; every limit here is generous
/// enough that no sample or real showcase document comes close to it.
/// </summary>
/// <remarks>
/// Two different kinds of cap work together here, and neither is sufficient
/// alone. Every cap other than <see cref="MaxWalkedNodes"/> bounds how many
/// DISTINCT objects a specification may hold in one collection: at most this
/// many table rows, this many list items, this many characters in one string.
/// None of those caps, individually or together, stops the same
/// already-capped object graph from being referenced repeatedly. A handful of
/// distinct objects sharing one deeply nested subtree can still make
/// <see cref="Generation.SpecRenderer"/> and <see cref="Generation.SpecCodeEmitter"/>,
/// both of which walk content by position rather than by object identity,
/// perform work many orders of magnitude larger than the object count alone
/// suggests. <see cref="MaxWalkedNodes"/> is what actually closes that gap: it
/// bounds the total number of nodes the walk itself visits, so no
/// specification, however its objects are shared or arranged, can make either
/// side perform unbounded work.
/// </remarks>
public static class SpecLimits
{
    /// <summary>
    /// Caps <see cref="Model.ImageSpec.Bytes"/>, each entry of
    /// <see cref="Model.DocumentSpec.EmbeddedFonts"/>, and
    /// <see cref="Model.PdfAOutputIntentSpec.IccProfile"/>. 20 MB comfortably
    /// exceeds every asset this application ships (the largest bundled font is
    /// under 2 MB) while keeping a single specification's byte-array footprint
    /// bounded. This does NOT bound decode time: a well-formed file far
    /// smaller than this cap can still declare an enormous pixel grid.
    /// Decode time is bounded instead by the decoding library's own
    /// decoded-pixel-count guard, which <see cref="Generation.SpecRenderer.BuildImage"/>'s
    /// try/catch surfaces as a legible message rather than an unhandled
    /// exception.
    /// </summary>
    public const int MaxAssetBytes = 20 * 1024 * 1024;

    /// <summary>
    /// Caps every plain-text string a specification carries: heading text and
    /// bookmark title, paragraph run text, plain text, list item and cell
    /// content, alternative text on images and charts, running header and
    /// footer templates, document metadata fields, output intent identifiers
    /// and info strings, and encryption passwords. 100,000 characters is far
    /// beyond anything a person would type into a demonstration document, but
    /// nowhere near large enough on its own to let a specification's emitted
    /// C# snippet or rendered PDF grow pathologically; <see cref="MaxWalkedNodes"/>
    /// is what bounds how many such strings one specification can multiply
    /// together through shared structure.
    /// </summary>
    /// <remarks>
    /// NOTE: this remains a distinct, load-bearing cap even now that
    /// <see cref="MaxTotalTextLength"/> (20,000, a fifth of this value) is
    /// substantially smaller than this value.
    /// <see cref="MaxTotalTextLength"/> only bounds the strings the
    /// <see cref="Model.DocumentSpec.Content"/> walk visits, which, as of
    /// cycle 7, is every text-bearing member reachable through that list:
    /// heading text and bookmark title, plain text, paragraph run text, list
    /// item text at every depth, table cell content, image and pie-chart
    /// alternative text, AND a pie slice's own label (an earlier version of
    /// this remark wrongly listed the label as outside the walk; it counted
    /// the SLICE as a node but never its label's own length, which let
    /// 490,220,429 characters through a total this walk exists to bound; see
    /// the remark on <see cref="Model.DocumentSpec.Content"/>). A running
    /// header or footer <see cref="Model.RunningBandSpec.Template"/>, every
    /// <see cref="Model.DocumentMetadataSpec"/> field, an output intent's
    /// identifier and info string, and an encryption password remain outside
    /// that walk and carry no other length bound than this cap, because each
    /// is a single top-level property that can appear at most once per
    /// specification and so cannot be multiplied by shared structure the way
    /// a list, table or chart entry can.
    /// </remarks>
    public const int MaxTextLength = 100_000;

    /// <summary>
    /// Caps a BCP 47 language tag. RFC 5646's own ABNF sets no length ceiling
    /// on a conforming tag: the grammar permits arbitrarily many variant,
    /// extension and private-use subtags. 35 characters comfortably covers an
    /// ordinary tag (a primary language, script, region and a variant or two)
    /// while remaining short enough that a value here still reads as a
    /// language tag rather than free text; it is a practical showcase limit,
    /// not a bound the standard supplies.
    /// </summary>
    public const int MaxLanguageTagLength = 35;

    /// <summary>
    /// Caps <see cref="Model.TextStyleSpec.LinkUri"/>. 2048 is a conventional
    /// practical ceiling for a URL, historically the Internet Explorer
    /// address-bar limit, carried over here as a generous round number rather
    /// than because it matches any constraint this specific destination
    /// imposes: the value ends up in a PDF <c>/URI</c> action, not a browser
    /// address bar, so no address-bar rule actually applies to it. It remains
    /// far larger than any link a showcase sample uses.
    /// </summary>
    public const int MaxUriLength = 2048;

    /// <summary>
    /// Caps <see cref="Model.DocumentSpec.Content"/>. A demonstration document
    /// with thousands of top-level items is already far beyond anything the
    /// showcase samples use, but a specification of unbounded size is not.
    /// </summary>
    public const int MaxContentItems = 2_000;

    /// <summary>Caps <see cref="Model.TableSpec.Rows"/>.</summary>
    public const int MaxTableRows = 2_000;

    /// <summary>Caps <see cref="Model.TableRowSpec.Cells"/>.</summary>
    public const int MaxTableCellsPerRow = 100;

    /// <summary>Caps <see cref="Model.TableSpec.ColumnWidths"/>.</summary>
    public const int MaxTableColumnWidths = 100;

    /// <summary>Caps <see cref="Model.PieChartSpec.Slices"/>.</summary>
    public const int MaxChartSlices = 100;

    /// <summary>Caps <see cref="Model.ParagraphSpec.Runs"/>.</summary>
    public const int MaxParagraphRuns = 1_000;

    /// <summary>Caps <see cref="Model.ListSpec.Items"/>.</summary>
    public const int MaxListItems = 2_000;

    /// <summary>
    /// Caps how many direct <see cref="Model.ListItemSpec.Children"/> one
    /// list item may hold. Distinct from <see cref="MaxListNestingDepth"/>,
    /// which caps depth rather than breadth at any one level.
    /// </summary>
    public const int MaxListItemChildren = 100;

    /// <summary>Caps the COUNT of <see cref="Model.DocumentSpec.EmbeddedFonts"/>; each entry's own size is capped separately by <see cref="MaxAssetBytes"/>.</summary>
    public const int MaxEmbeddedFonts = 100;

    /// <summary>
    /// Caps how deeply a <see cref="Model.ListItemSpec"/> tree may nest through
    /// <see cref="Model.ListItemSpec.Children"/>. Measured directly: unbounded
    /// nesting overflows the CLR stack at roughly depth 4000 on the desktop,
    /// lower in the browser, and a stack overflow cannot be caught. 64 levels
    /// is far beyond any real outline while leaving a wide safety margin below
    /// that threshold.
    /// </summary>
    public const int MaxListNestingDepth = 64;

    /// <summary>
    /// Caps the total number of nodes a walk of <see cref="Model.DocumentSpec.Content"/>
    /// visits, counted by <see cref="Model.DocumentSpec"/> exactly as
    /// <see cref="Generation.SpecRenderer"/> and <see cref="Generation.SpecCodeEmitter"/>
    /// walk it: once per position in the tree, not once per distinct object.
    /// Every other cap in this file bounds how many distinct objects one
    /// collection may hold; none of them, alone or combined, stops the same
    /// already-capped object graph from being referenced repeatedly. Five
    /// hundred list-item slots that all point at the one shared, sixty-three
    /// level list-item chain construct only a few hundred distinct objects,
    /// well inside every per-collection cap, but a walk that visits a shared
    /// reference once per slot performs the work of five hundred distinct
    /// chains. This limit is what actually bounds that work: counting stops
    /// the instant the running total would exceed it, so a specification
    /// engineered to make the true total astronomically large is rejected
    /// after doing only this many units of counting work, never after
    /// actually doing the astronomical amount of work itself.
    /// </summary>
    public const int MaxWalkedNodes = 5_000;

    /// <summary>
    /// Caps the total number of characters a specification may carry across
    /// every text-bearing node the same walk of <see cref="Model.DocumentSpec.Content"/>
    /// that enforces <see cref="MaxWalkedNodes"/> visits, counted the same way:
    /// by position during the walk, so that a shared subtree cannot amplify
    /// its own text length any more than it can amplify its own node count.
    /// </summary>
    /// <remarks>
    /// This closes the gap <see cref="MaxWalkedNodes"/> and <see cref="MaxTextLength"/>
    /// leave when multiplied together: 5,000 nodes at 100,000 characters each is
    /// 500,000,000 characters, about 954 MB of UTF-16. Measured on the way there,
    /// a specification carrying 200,062,485 characters emitted its C# in 1.3 s
    /// and rendered a 40.6 MB PDF in 8,972 ms on desktop x64; WebAssembly is
    /// single-threaded and materially slower. This limit targets rendering well
    /// under 250 ms on desktop x64, which measurement puts comfortably above
    /// 100,000 characters in every geometry this file also bounds.
    /// <para>
    /// Text volume is not only a CPU-time hazard. Measured directly:
    /// <c>VellumPdf.Layout.Rendering.DocumentRenderer.PlaceRenderer</c> recurses once per page continuation, so a
    /// specification that forces enough pages overflows the CLR stack, which
    /// cannot be caught. <see cref="MinPageDimensionPoints"/>, <see cref="MaxFontSize"/>
    /// and <see cref="MaxLeadingPoints"/> bound how few characters one page can
    /// hold; this bounds how many characters there are to place.
    /// </para>
    /// <para>
    /// The worst specification every cap in this file together still permits
    /// is the smallest permitted page, the largest permitted font, and exactly
    /// this many characters in one run. At 72-point font (this value's
    /// previous ceiling), <see cref="Model.TextStyleSpec.Leading"/> left UNSET
    /// was found to be substantially more dangerous than
    /// <see cref="Model.TextStyleSpec.Leading"/> set explicitly to
    /// <see cref="MaxLeadingPoints"/>: an unset <see cref="Model.TextStyleSpec.Leading"/>
    /// reaches the library as a literal <c>0</c> (see the remark on
    /// <see cref="MaxLeadingPoints"/>), which the library treats as a request
    /// to compute its own line height from the font, and at 72 points that
    /// computed leading exceeded <see cref="MaxLeadingPoints"/> itself. At
    /// <see cref="MaxFontSize"/>'s CURRENT value of 36, re-measuring both
    /// configurations found them within roughly one percent of each other,
    /// not the roughly twofold gap seen at 72 points; the auto-computed
    /// leading at 36 points evidently sits close to, rather than well above,
    /// the 50-point cap. NEITHER configuration is safely ignorable: both were
    /// measured, and the smaller of the two boundaries found was used below.
    /// This is stated explicitly because it does not hold in general and must
    /// be re-checked, not assumed, whenever <see cref="MaxFontSize"/> or
    /// <see cref="MaxLeadingPoints"/> changes again.
    /// </para>
    /// <para>
    /// Measured directly at that exact geometry (200 &#215; 200 points, zero
    /// margins, 36-point font, one run), under both
    /// <see cref="Model.TextStyleSpec.Leading"/> left unset and
    /// <see cref="Model.TextStyleSpec.Leading"/> set explicitly to
    /// <see cref="MaxLeadingPoints"/>: rendering succeeded reliably (repeated
    /// trials, no failures) up to at least 156,000 characters under both
    /// configurations, and overflowed the CLR stack reliably (repeated
    /// trials, no successes) from 170,000 characters onward, at roughly 4,348
    /// <c>DocumentRenderer.PlaceRenderer</c> frames, consistent with the
    /// roughly 3,659 to 4,354-frame depth measured elsewhere at this geometry
    /// on this machine. Single trials in the 156,000 to 170,000 range were
    /// inconsistent from one process launch to the next, by as much as a few
    /// thousand characters either side, which reads as ordinary run-to-run
    /// stack-layout variance this close to the true boundary rather than as a
    /// property of the content itself; this is itself a reason to keep the
    /// margin wide rather than shave it to the exact figure. 20,000 was chosen
    /// from the reliably-safe figure of 150,000 with roughly a sevenfold
    /// margin below it; WebAssembly's stack is smaller still, which is why the
    /// margin is wide rather than exact.
    /// </para>
    /// <para>
    /// NOTE: the measurement above holds <see cref="Model.DocumentSpec.Margins"/>
    /// at ZERO and uses a NARROW glyph, both of which minimise page count and
    /// so understate the danger; this value alone does not bound the crash for
    /// a specification whose margins or running-band heights shrink the
    /// content box below what this measurement assumed. <see cref="MaxSafePageContinuations"/>
    /// is the companion cap that bounds THAT case, measured with margins and
    /// glyph width at their actual worst permitted values; see its own remark
    /// for the geometry-dependent figures. This value remains the correct
    /// bound for the geometry it was measured at; it was never a bound on
    /// every geometry a specification may declare.
    /// </para>
    /// </remarks>
    public const int MaxTotalTextLength = 20_000;

    /// <summary>
    /// The maximum number of page continuations
    /// <see cref="Model.DocumentSpec.ValidateContentFitsPageArea"/> permits a
    /// specification's own page geometry to require for its own total text
    /// volume. <c>VellumPdf.Layout.Rendering.DocumentRenderer.PlaceRenderer</c>
    /// recurses once per page continuation and that recursion cannot be
    /// caught, so this is a second, independent bound on the same hazard
    /// <see cref="MaxTotalTextLength"/> bounds, needed because
    /// <see cref="MaxTotalTextLength"/> alone assumes a content box large
    /// enough to place many characters per page; a specification with large
    /// <see cref="Model.DocumentSpec.Margins"/>, a tall <see cref="Model.RunningBandSpec"/>,
    /// or both, can shrink that box far below what <see cref="MaxTotalTextLength"/>'s
    /// own measurement assumed without violating any other cap in this file.
    /// </summary>
    /// <remarks>
    /// Measured directly against the shipped library, holding <see cref="MaxFontSize"/>
    /// fixed and varying the three levers the ORIGINAL <see cref="MaxTotalTextLength"/>
    /// measurement held fixed at their least dangerous values: margins (0, not
    /// the 72-point default), glyph width (the narrow character <c>'a'</c>,
    /// not a wide one), and running-band height (no header or footer at all).
    /// Re-measured with each lever moved toward its actual worst case:
    /// <list type="bullet">
    /// <item><description>
    /// At a 200 x 200 page with ZERO margins, 36-point Helvetica, and the wide
    /// character <c>'W'</c>: rendering succeeded up to 85,000 characters and
    /// overflowed the stack at 90,000, roughly half of the 156,000/170,000
    /// boundary <see cref="MaxTotalTextLength"/>'s own remark records for the
    /// narrow character 'a' at the same geometry, confirming glyph width alone
    /// roughly doubles the danger.
    /// </description></item>
    /// <item><description>
    /// At the SAME page with its DEFAULT 72-point margins on every edge
    /// (leaving a 56 x 56 content box) and the wide character, 20,000
    /// characters, the exact figure <see cref="MaxTotalTextLength"/> permits,
    /// overflowed the stack reliably (reproduced three times out of three,
    /// exit code 127, "Stack overflow.", roughly 4,353
    /// <c>DocumentRenderer.PlaceRenderer</c> frames), and the same failure
    /// reproduced with margins of 60, 72 and 77 points, and separately with a
    /// <see cref="Model.RunningBandSpec.Height"/> of 77 on an otherwise
    /// zero-margin page. <see cref="MaxTotalTextLength"/>'s own 20,000-character
    /// figure is therefore NOT safe in general: it is safe only at content
    /// boxes at or above roughly the one this cap's own worst-permitted
    /// regression test uses (200 x 200, zero margins), which a caller-supplied
    /// <see cref="Model.DocumentSpec.Margins"/> or <see cref="Model.RunningBandSpec.Height"/>
    /// is free to shrink far below.
    /// </description></item>
    /// <item><description>
    /// Across every geometry tried, the page count at which the stack
    /// overflowed stayed within a narrow band (roughly 4,250 to 4,500 pages),
    /// consistent with the stack overflowing at a roughly fixed RECURSION
    /// DEPTH regardless of how that depth was reached; this is what makes a
    /// single page-count ceiling, rather than a character-count ceiling,
    /// the correct bound to add.
    /// </description></item>
    /// </list>
    /// 2,000 is chosen with headroom below that measured 4,250-to-4,500-page
    /// boundary: it comfortably passes the existing worst-permitted-specification
    /// regression tests (a 200 x 200, zero-margin page at <see cref="MaxFontSize"/>
    /// and exactly <see cref="MaxTotalTextLength"/> characters computes to
    /// roughly 1,000 page continuations under this cap's own, deliberately
    /// generous, glyph-width assumption), while rejecting every reproduction
    /// above, each of which computes to several thousand. WebAssembly's
    /// smaller stack is not assumed to raise this boundary; the margin is kept
    /// wide rather than tuned tight for the same reason <see cref="MaxTotalTextLength"/>'s
    /// own margin is.
    /// </remarks>
    public const int MaxSafePageContinuations = 2_000;

    /// <summary>
    /// The lower bound on <see cref="Model.PageSizeSpec.WidthPoints"/> and
    /// <see cref="Model.PageSizeSpec.HeightPoints"/>. Measured directly: a
    /// <see cref="Model.PageSizeSpec"/> of (36, 36), zero margins, and a single
    /// run of exactly <see cref="MaxTextLength"/> characters overflows the CLR
    /// stack, because <c>VellumPdf.Layout.Rendering.DocumentRenderer.PlaceRenderer</c>
    /// recurses once per page continuation and page count is content volume
    /// divided by page area. The same content at 200 x 200 renders successfully
    /// with a wide margin below the point measurement found the stack to
    /// overflow at this font size and text volume; see <see cref="MaxFontSize"/>
    /// and <see cref="MaxTotalTextLength"/> for the other two levers this
    /// figure was chosen together with. 200 is also small enough that no
    /// existing specification in this repository, several of which use a
    /// 200 x 200 page for a minimal test document, needed to change.
    /// NOTE: when <see cref="MaxFontSize"/> was later raised and then lowered
    /// again, this figure was not re-derived on its own each time; it was
    /// re-verified as part of the same combined measurement described on
    /// <see cref="MaxTotalTextLength"/>. NOTE: this figure also cannot rise
    /// above 297 (A4's shorter edge in points) without excluding A6, which one
    /// shipped sample uses; that ceiling was not tested against, since every
    /// measurement here has only ever found reason to keep this figure at 200
    /// or lower it, never to raise it.
    /// </summary>
    public const double MinPageDimensionPoints = 200;

    /// <summary>
    /// The upper bound on <see cref="Model.PageSizeSpec.WidthPoints"/> and
    /// <see cref="Model.PageSizeSpec.HeightPoints"/>. A generous sanity ceiling
    /// (about 278 inches) rather than a measured one: an oversized page is not
    /// part of the stack-overflow mechanism <see cref="MinPageDimensionPoints"/>
    /// guards against, since more area means fewer pages, not more.
    /// </summary>
    public const double MaxPageDimensionPoints = 20_000;

    /// <summary>
    /// Caps <see cref="Model.TextStyleSpec.FontSize"/>. 36 points is
    /// unambiguously a display size, twice the largest font size any shipped
    /// sample in this repository uses today (18 points), chosen so the site
    /// can demonstrate typography at display scale per plan section 6.2
    /// without narrowing <see cref="MaxTotalTextLength"/> so far that plan
    /// section 6.2's OTHER requirement, demonstrating automatic pagination by
    /// letting a table and a list visibly divide across pages, becomes only
    /// barely possible.
    /// </summary>
    /// <remarks>
    /// A large font size shrinks how much text fits on one page as sharply as
    /// a small page does, and the two compound; this is why this value is not
    /// chosen freely. Rather than finding a font-size ceiling that is itself
    /// safe at a fixed text volume, the ceiling is fixed here for typographic
    /// purpose and <see cref="MaxTotalTextLength"/> is chosen to compensate.
    /// This value was previously 72; it was measured, together with
    /// <see cref="MaxTotalTextLength"/>, and lowered to 36 to buy back text
    /// budget, since 72 forced <see cref="MaxTotalTextLength"/> down to 5,000
    /// characters for an entire document, too little to demonstrate the
    /// pagination requirement above. See <see cref="MaxTotalTextLength"/> for
    /// the measurement performed at this exact font size, which is what makes
    /// 36 safe together with <see cref="MinPageDimensionPoints"/> and
    /// <see cref="MaxLeadingPoints"/>.
    /// </remarks>
    public const double MaxFontSize = 36;

    /// <summary>
    /// Caps <see cref="Model.TextStyleSpec.Leading"/> when set. Leading
    /// enlarges each line's height exactly as a larger font does, so it was
    /// measured against the same worst-case page and font-size combination as
    /// <see cref="MaxFontSize"/>; 50 points left a wide margin below the point
    /// measurement found dangerous there.
    /// </summary>
    /// <remarks>
    /// NOTE: this cap does not necessarily bound the most dangerous leading a
    /// specification can carry. <see cref="Generation.SpecRenderer"/> passes an
    /// UNSET <see cref="Model.TextStyleSpec.Leading"/> to the library as a
    /// literal <c>0</c>, and <see cref="Generation.SpecCodeEmitter"/> emits no
    /// <c>Leading</c> property at all in that case, which leaves the library's
    /// own <c>TextStyle.Leading</c> at its own default of <c>0</c>; both paths
    /// agree, so the round trip does not diverge. The library treats a <c>0</c>
    /// leading as a request to compute its own line height from the font
    /// rather than as a literal zero, and WHETHER that computed value is larger
    /// or smaller than this cap depends on <see cref="MaxFontSize"/>: measured
    /// at a previous <see cref="MaxFontSize"/> of 72, the computed value was
    /// substantially larger than this cap, making an unset
    /// <see cref="Model.TextStyleSpec.Leading"/> the more dangerous
    /// configuration; re-measured at the current <see cref="MaxFontSize"/> of
    /// 36, an unset <see cref="Model.TextStyleSpec.Leading"/> and one set
    /// explicitly to this maximum were found within roughly one percent of
    /// each other. Both configurations must be measured together whenever
    /// <see cref="MaxFontSize"/> or this cap changes; see the remark on
    /// <see cref="MaxTotalTextLength"/> for the current measurement.
    /// </remarks>
    public const double MaxLeadingPoints = 50;

    /// <summary>
    /// The lowest value <see cref="Model.HeadingSpec.Level"/> accepts. Zero is
    /// top-level, matching the library's own <c>Heading.Level</c> convention.
    /// </summary>
    public const int MinHeadingLevel = 0;

    /// <summary>
    /// The highest value <see cref="Model.HeadingSpec.Level"/> accepts. Measured
    /// directly against the library: <c>HeadingRenderer.HeadingStructType</c>
    /// clamps every level above this to the same PDF structure type an H6
    /// heading gets, so <c>-5</c>, <c>7</c>, <c>100</c> and <c>int.MaxValue</c>
    /// all currently produce identical output. Rejecting them at construction,
    /// rather than letting the library silently clamp them, is what keeps the
    /// displayed level and the rendered structure type in agreement.
    /// </summary>
    public const int MaxHeadingLevel = 5;

    /// <summary>
    /// Caps every <c>EdgeInsets</c> component (<c>Top</c>, <c>Right</c>,
    /// <c>Bottom</c>, <c>Left</c>) wherever this specification accepts one, on
    /// <see cref="Model.DocumentSpec.Margins"/> and every element's own
    /// <c>Margins</c> or <c>Padding</c>. A generous sanity ceiling: the library
    /// itself rejects a page whose margins leave no content area, at render
    /// time; this bound exists so a NaN or a wildly disproportionate value is
    /// rejected at construction instead, with a legible message, rather than
    /// constructing successfully and failing only when rendered.
    /// </summary>
    public const double MaxEdgeInsetPoints = 10_000;

    /// <summary>Caps <see cref="Model.PieChartSpec.Diameter"/>. A generous sanity ceiling, comfortably above the library's own 200-point default.</summary>
    public const double MaxPieChartDiameterPoints = 10_000;

    /// <summary>
    /// Caps a stroke or border width in points: <see cref="Model.PieChartSpec.StrokeWidth"/>,
    /// <see cref="Model.TableSpec.BorderWidth"/> and <see cref="Model.LineSeparatorSpec.LineWidth"/>.
    /// A generous sanity ceiling, far beyond any line a page this size could
    /// usefully show.
    /// </summary>
    public const double MaxStrokeWidthPoints = 1_000;

    /// <summary>Caps <see cref="Model.ListSpec.Indent"/>. A generous sanity ceiling, comfortably above the library's own 20-point default.</summary>
    public const double MaxIndentPoints = 10_000;

    /// <summary>Caps <see cref="Model.ImageSpec.Width"/> and <see cref="Model.ImageSpec.Height"/>. A generous sanity ceiling matching the order of magnitude of <see cref="MaxPageDimensionPoints"/>.</summary>
    public const double MaxImageDimensionPoints = 10_000;

    /// <summary>
    /// Caps the magnitude of <see cref="Model.PieChartSpec.StartAngle"/>, in
    /// radians. A generous sanity ceiling of roughly 159 full turns either
    /// way, far more than any legitimate value, that exists only to reject a
    /// NaN, an infinity, or a value so large it signals a caller error rather
    /// than an intended rotation.
    /// </summary>
    public const double MaxAngleMagnitudeRadians = 1_000;

    /// <summary>Throws when <paramref name="value"/> is not a finite number (rejects <see cref="double.NaN"/> and both infinities); returns it otherwise.</summary>
    public static double ValidateFinite(double value, string paramName) =>
        double.IsFinite(value)
            ? value
            : throw new ArgumentException($"{paramName} must be a finite number; got {value}.", paramName);

    /// <summary>Throws when <paramref name="value"/> is not finite or falls outside <c>[minInclusive, maxInclusive]</c>; returns it otherwise.</summary>
    public static double ValidateRange(double value, double minInclusive, double maxInclusive, string paramName)
    {
        ValidateFinite(value, paramName);
        return value < minInclusive || value > maxInclusive
            ? throw new ArgumentException(
                $"{paramName} must be between {minInclusive} and {maxInclusive}; got {value}.",
                paramName)
            : value;
    }

    /// <summary>Validates a page dimension against <see cref="MinPageDimensionPoints"/> and <see cref="MaxPageDimensionPoints"/>.</summary>
    public static double ValidatePageDimension(double value, string paramName) =>
        ValidateRange(value, MinPageDimensionPoints, MaxPageDimensionPoints, paramName);

    /// <summary>Validates <see cref="Model.TextStyleSpec.FontSize"/>: finite, strictly positive, and no larger than <see cref="MaxFontSize"/>.</summary>
    public static double ValidateFontSize(double value, string paramName)
    {
        ValidateFinite(value, paramName);
        return value switch
        {
            <= 0 => throw new ArgumentException($"{paramName} must be greater than zero; got {value}.", paramName),
            > MaxFontSize => throw new ArgumentException($"{paramName} must not exceed {MaxFontSize} points; got {value}.", paramName),
            _ => value,
        };
    }

    /// <summary>Validates <see cref="Model.TextStyleSpec.Leading"/> when set: finite and within <c>[0, MaxLeadingPoints]</c>.</summary>
    public static double ValidateLeading(double value, string paramName) =>
        ValidateRange(value, 0, MaxLeadingPoints, paramName);

    /// <summary>Same as <see cref="ValidateLeading(double, string)"/>, but passes a null value through unchanged.</summary>
    public static double? ValidateOptionalLeading(double? value, string paramName) =>
        value is null ? null : ValidateLeading(value.Value, paramName);

    /// <summary>Validates <see cref="Model.HeadingSpec.Level"/> against <see cref="MinHeadingLevel"/> and <see cref="MaxHeadingLevel"/>.</summary>
    public static int ValidateHeadingLevel(int value, string paramName) =>
        value is >= MinHeadingLevel and <= MaxHeadingLevel
            ? value
            : throw new ArgumentException(
                $"{paramName} must be between {MinHeadingLevel} and {MaxHeadingLevel}; got {value}.",
                paramName);

    /// <summary>Validates every component of an <c>EdgeInsets</c>: each of <c>Top</c>, <c>Right</c>, <c>Bottom</c> and <c>Left</c> finite and within <c>[0, MaxEdgeInsetPoints]</c>.</summary>
    public static EdgeInsets ValidateEdgeInsets(EdgeInsets value, string paramName)
    {
        ValidateRange(value.Top, 0, MaxEdgeInsetPoints, paramName);
        ValidateRange(value.Right, 0, MaxEdgeInsetPoints, paramName);
        ValidateRange(value.Bottom, 0, MaxEdgeInsetPoints, paramName);
        ValidateRange(value.Left, 0, MaxEdgeInsetPoints, paramName);
        return value;
    }

    /// <summary>Same as <see cref="ValidateEdgeInsets(EdgeInsets, string)"/>, but passes a null value through unchanged.</summary>
    public static EdgeInsets? ValidateOptionalEdgeInsets(EdgeInsets? value, string paramName) =>
        value is null ? null : ValidateEdgeInsets(value.Value, paramName);

    /// <summary>Validates every component of a <c>ColorRgb</c>: each of <c>R</c>, <c>G</c> and <c>B</c> finite and within <c>[0, 1]</c>, matching the library's own normalised-colour contract.</summary>
    public static ColorRgb ValidateColor(ColorRgb value, string paramName)
    {
        ValidateRange(value.R, 0, 1, paramName);
        ValidateRange(value.G, 0, 1, paramName);
        ValidateRange(value.B, 0, 1, paramName);
        return value;
    }

    /// <summary>Same as <see cref="ValidateColor(ColorRgb, string)"/>, but passes a null value through unchanged.</summary>
    public static ColorRgb? ValidateOptionalColor(ColorRgb? value, string paramName) =>
        value is null ? null : ValidateColor(value.Value, paramName);

    /// <summary>
    /// Validates <see cref="Model.EncryptionSpec.Permissions"/>: every bit set
    /// must belong to one of the library's named <see cref="PdfPermissions"/>
    /// flags. <see cref="Generation.SpecCodeEmitter"/>'s <c>EmitPermissions</c>
    /// reconstructs a value from exactly those named flags, so a raw value
    /// carrying an undefined bit would either emit invalid C# (a lone comma,
    /// for a value with no named flag at all) or silently drop that bit from
    /// the displayed code while the renderer still applied it, letting the
    /// document the code produces diverge from the one it is shown beside.
    /// </summary>
    public static PdfPermissions ValidatePermissions(PdfPermissions value, string paramName) =>
        (value & ~AllNamedPermissionFlags) == 0
            ? value
            : throw new ArgumentException(
                $"{paramName} must be a union of named PdfPermissions flags; got a raw value with undefined bits set (0x{(int)value:x}).",
                paramName);

    /// <summary>
    /// The bitwise union of every named <see cref="PdfPermissions"/> flag other
    /// than <see cref="PdfPermissions.None"/> and <see cref="PdfPermissions.All"/>.
    /// Verified by test to equal <see cref="PdfPermissions.All"/> exactly, which
    /// is what makes <see cref="ValidatePermissions"/> accept <c>All</c> without
    /// naming it as a special case.
    /// </summary>
    internal static readonly PdfPermissions AllNamedPermissionFlags = Enum.GetValues<PdfPermissions>()
        .Where(flag => flag is not (PdfPermissions.None or PdfPermissions.All))
        .Aggregate(PdfPermissions.None, (acc, flag) => acc | flag);

    /// <summary>Throws when <paramref name="value"/> is null or longer than <paramref name="maxLength"/>; returns it otherwise.</summary>
    public static string ValidateString(string value, int maxLength, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        return value.Length > maxLength
            ? throw new ArgumentException($"{paramName} must not exceed {maxLength} characters; got {value.Length}.", paramName)
            : value;
    }

    /// <summary>Same as <see cref="ValidateString(string, int, string)"/>, but passes a null value through unchanged.</summary>
    public static string? ValidateOptionalString(string? value, int maxLength, string paramName) =>
        value is null ? null : ValidateString(value, maxLength, paramName);

    /// <summary>
    /// Throws when <paramref name="value"/> is null or longer than
    /// <see cref="MaxAssetBytes"/>; otherwise returns a defensive copy, so the
    /// caller's own array cannot be mutated afterward to change what a fully
    /// constructed record holds. Every one of the three byte-array members
    /// this validates (<see cref="Model.ImageSpec.Bytes"/>,
    /// <see cref="Model.PdfAOutputIntentSpec.IccProfile"/>, and each entry of
    /// <see cref="Model.DocumentSpec.EmbeddedFonts"/>) is validated once
    /// against bytes the caller could still hold a reference to; without this
    /// copy, overwriting a validated array after construction would bypass
    /// that validation entirely, for instance replacing a validated PNG's
    /// bytes with BMP bytes after <see cref="Model.DocumentSpec.Content"/>'s
    /// magic-byte check has already passed.
    /// </summary>
    public static byte[] ValidateAssetBytes(byte[] value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        return value.Length > MaxAssetBytes
            ? throw new ArgumentException(
                $"{paramName} must not exceed {MaxAssetBytes:N0} bytes ({MaxAssetBytes / (1024 * 1024)} MB); got {value.Length:N0} bytes.",
                paramName)
            : (byte[])value.Clone();
    }
}

/// <summary>
/// Sniffs the leading bytes of an image against the five formats the Kernel
/// image loaders accept, per plan section 5.4 control 2: the declared
/// <see cref="ImageFormat"/> must agree with the bytes actually supplied,
/// rather than being trusted outright and used to select which clean-room
/// parser attacker-controlled bytes are handed to.
/// </summary>
public static class ImageSignature
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Gif87a = "GIF87a"u8.ToArray();
    private static readonly byte[] Gif89a = "GIF89a"u8.ToArray();

    public static bool Matches(ImageFormat format, byte[] bytes) => format switch
    {
        ImageFormat.Png => bytes.AsSpan().StartsWith(PngMagic),
        ImageFormat.Jpeg => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        ImageFormat.Bmp => bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D,
        ImageFormat.Gif => bytes.AsSpan().StartsWith(Gif87a) || bytes.AsSpan().StartsWith(Gif89a),
        ImageFormat.Tiff => bytes.Length >= 4 &&
            ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
             (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unrecognised image format."),
    };
}

/// <summary>
/// Validates the fixed 128-byte ICC profile header (ICC.1:2010 section 7.2)
/// against a value the profile itself is claimed to have, per plan section
/// 5.4: a three-byte junk value must not silently become an output intent on
/// a document claiming PDF/A or PDF/UA conformance.
/// </summary>
public static class IccProfileHeader
{
    /// <summary>The ICC header is 128 bytes; this validates only the three fields the showcase's own claims depend on.</summary>
    private const int MinimumHeaderLength = 128;

    public static void Validate(byte[] profile, int componentCount)
    {
        if (profile.Length < MinimumHeaderLength)
        {
            throw new ArgumentException(
                $"IccProfile is {profile.Length} bytes, too small to hold a valid ICC profile header ({MinimumHeaderLength} bytes).",
                nameof(profile));
        }

        var declaredSize = BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(0, 4));
        if (declaredSize != (uint)profile.Length)
        {
            throw new ArgumentException(
                $"IccProfile's own size field says {declaredSize} bytes, but the array is {profile.Length} bytes.",
                nameof(profile));
        }

        var magic = Encoding.ASCII.GetString(profile, 36, 4);
        if (magic != "acsp")
        {
            throw new ArgumentException(
                $"IccProfile is missing the required 'acsp' signature at byte offset 36; found \"{magic}\".",
                nameof(profile));
        }

        var colorSpace = Encoding.ASCII.GetString(profile, 16, 4).TrimEnd();
        var expectedComponentCount = colorSpace switch
        {
            "GRAY" => 1,
            "RGB" => 3,
            "Lab" => 3,
            "CMYK" => 4,
            _ => (int?)null,
        };

        if (expectedComponentCount is { } expected && expected != componentCount)
        {
            throw new ArgumentException(
                $"IccProfile declares a {colorSpace} data colour space, which requires ComponentCount {expected}; got {componentCount}.",
                nameof(componentCount));
        }
    }
}
