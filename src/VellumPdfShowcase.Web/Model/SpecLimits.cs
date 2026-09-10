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
    /// exceeds every asset this application ships (the one bundled font,
    /// Liberation Sans Regular, is 410,712 bytes, about 401 KiB) while
    /// keeping a single specification's byte-array footprint
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
    /// content, alternative text on images and charts, document metadata
    /// fields, output intent identifiers and info strings, and encryption
    /// passwords. 100,000 characters is far beyond anything a person would
    /// type into a demonstration document, but nowhere near large enough on
    /// its own to let a specification's emitted C# snippet or rendered PDF
    /// grow pathologically; <see cref="MaxWalkedNodes"/> is what bounds how
    /// many such strings one specification can multiply together through
    /// shared structure. NOTE: a running header or footer's own
    /// <see cref="Model.RunningBandSpec.Template"/> is NOT among these; it is
    /// bounded by the much smaller <see cref="MaxRunningBandTemplateLength"/>
    /// instead, for the reason given on that constant.
    /// </summary>
    /// <remarks>
    /// NOTE: this remains a distinct, load-bearing cap even now that
    /// <see cref="MaxTotalTextLength"/> (6,000, six per cent of this value) is
    /// substantially smaller than this value.
    /// <see cref="MaxTotalTextLength"/> only bounds the strings the
    /// <see cref="Model.DocumentSpec.Content"/> walk visits, which is
    /// currently every text-bearing member reachable through that list:
    /// heading text and bookmark title, plain text, paragraph run text, list
    /// item text at every depth, table cell content, image and pie-chart
    /// alternative text, AND a pie slice's own label (an earlier version of
    /// this remark wrongly listed the label as outside the walk; it counted
    /// the SLICE as a node but never its label's own length, which let
    /// 490,220,429 characters through a total this walk exists to bound; see
    /// the remark on <see cref="Model.DocumentSpec.Content"/>). Every
    /// <see cref="Model.DocumentMetadataSpec"/> field, an output intent's
    /// identifier and info string, and an encryption password remain outside
    /// that walk and carry no other length bound than this cap, because each
    /// is a single top-level property that can appear at most once per
    /// specification and, unlike a running band's <see cref="Model.RunningBandSpec.Template"/>,
    /// is never laid out once per page either, so it cannot be multiplied by
    /// shared structure OR by pagination the way a list, table or chart entry,
    /// or a running band, can.
    /// </remarks>
    public const int MaxTextLength = 100_000;

    /// <summary>
    /// Caps <see cref="Model.RunningBandSpec.Template"/>. A running band
    /// holds one line of text substituting <c>{page}</c> and <c>{pages}</c>,
    /// so <see cref="MaxTextLength"/> (100,000 characters) is meaningless
    /// here: a template is laid out once PER PAGE, not once per
    /// specification, and page count is exactly the quantity
    /// <see cref="MaxTotalTextLength"/> and <see cref="MaxWalkedNodes"/>
    /// exist to bound. Neither of those two caps, nor
    /// <see cref="Model.DocumentSpec.Content"/>'s own walk, sees a
    /// <see cref="Model.DocumentSpec.Header"/> or
    /// <see cref="Model.DocumentSpec.Footer"/> at all, so nothing else in
    /// this file stops the product of template length and page count from
    /// growing without bound; this cap is the only thing that does.
    /// </summary>
    /// <remarks>
    /// MEASURED against the true worst case this model admits, not the
    /// smaller document once recorded here, which understated the cost this
    /// cap bounds by a factor of 2.2. Start from the maximal construction
    /// described on <see cref="MaxTotalTextLength"/> (one <see cref="Model.ListSpec"/>
    /// of 1,667 top-level <see cref="Model.ListItemSpec"/>, 1,666 of them
    /// each carrying two empty children and the last carrying none, for
    /// 4,999 items in total; the first item alone carries the full
    /// 6,000-character text budget as solid <c>'W'</c> and every other item
    /// is empty, in 36-point Helvetica at this model's own default 72-point
    /// margins; 10,998 pages on its own) and heighten the page from 200 by
    /// 200 to 200 by 280 points, the width unchanged, to leave room for a
    /// <see cref="Model.RunningBandSpec.Height"/> of 40 on both a header and
    /// a footer, each holding a template at this cap's own value, 200
    /// characters. This renders 10,998 pages, identical to the unbanded
    /// construction it started from, because the 80 points added to the
    /// page's height are exactly the 80 points the two 40-point bands
    /// reserve, leaving the content box unchanged at 56 points; that
    /// identity is what makes the two page counts comparable. It takes 601
    /// ms on desktop x64 in Release. Varying the template length on this
    /// same construction gives:
    /// <list type="table">
    /// <item><term>template length 1</term><description>556 ms</description></item>
    /// <item><term>template length 200</term><description>601 ms</description></item>
    /// <item><term>template length 2,000</term><description>965 ms</description></item>
    /// <item><term>template length 100,000</term><description>18,718 ms</description></item>
    /// </list>
    /// This reports time, not bytes; an earlier version of this remark
    /// reported bytes without stating what drives them. Both the band's own
    /// <see cref="Model.RunningBandSpec.Style"/>, here 36-point Helvetica
    /// with 50-point leading, matching the body style, and the template text
    /// itself, here the ASCII character <c>'a'</c> repeated to the given
    /// length, drive that byte cost: a wider template of that character in
    /// that style costs more bytes because the glyph run drawn into each
    /// page's band grows, not because of anything this cap tracks beyond
    /// length. Time, not bytes, is the quantity this cap exists to bound,
    /// which is why the table above reports only time. NOTE what these
    /// figures show: the 200-character cap itself is worth only about 45 ms
    /// over the one-character floor at this geometry, a small fraction of
    /// the 601 ms total. The cost is page count and the second pagination
    /// pass a running band forces the library into, not the template text;
    /// this cap must not be described as what bounds the freeze this
    /// construction produces. It remains justified because it still removes
    /// a 31-fold cost at 100,000 characters, 18,718 ms against 601 ms. See
    /// the remark on <see cref="MaxTotalTextLength"/> for the freeze itself,
    /// its full figure with both bands at this cap, and the decision to
    /// accept it rather than tune further.
    /// <para>
    /// <see cref="Model.RunningBandSpec.Style"/>'s own
    /// <see cref="Model.TextStyleSpec.LinkUri"/> was checked for the same
    /// multiplication and found NOT to have it: a footer whose style carries
    /// a maximal-length (<see cref="MaxUriLength"/>, 2,048)
    /// <see cref="Model.TextStyleSpec.LinkUri"/> renders output of IDENTICAL
    /// LENGTH, 1,027,765 bytes, to the same footer with no
    /// <see cref="Model.TextStyleSpec.LinkUri"/> at all, across a
    /// footer-only variant of the construction above: the same 20,000 by
    /// 260 point page, 55-point margins, 36-point/50-point-leading style and
    /// 1,650-item list, with only a footer, at
    /// <see cref="Model.RunningBandSpec.Height"/> 30, and no header. That
    /// specification renders 2,475 pages, half the 4,950 the same list
    /// renders under both a header and a footer, because removing one band
    /// frees that much more of each page for content. NOTE: this is a
    /// length comparison, not a byte-for-byte one. Two renders of the same
    /// specification are never byte-identical, because the library writes a
    /// random document identifier on every render; a byte-for-byte claim
    /// would be false here regardless of what <see cref="Model.TextStyleSpec.LinkUri"/>
    /// does. The library does not turn a running band's link into a per-page
    /// annotation, so <see cref="MaxUriLength"/> alone already bounds it and
    /// no dedicated cap is needed here. <c>RunningBandTemplateCapTests.MaximalLinkUriOnFooterStyle_AddsNothingAcrossManyPages</c>
    /// guards the same exemption on a smaller, cheaper document instead of
    /// the 2,475-page one above: 80 unordered list items on a 200 by 200
    /// point page at 20-point margins, with only a footer, which renders 40
    /// pages, not 2,475. That is enough pages for the comparison to mean
    /// something without paying for the larger construction on every test
    /// run, and it remains the only test in the repository that sets
    /// <see cref="Model.TextStyleSpec.LinkUri"/> on a header or footer style
    /// at all.
    /// </para>
    /// </remarks>
    public const int MaxRunningBandTemplateLength = 200;

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
    /// Caps the SUM, in bytes, of every distinct asset byte array one
    /// specification carries: each distinct <see cref="Model.ImageSpec.Bytes"/>
    /// reachable from <see cref="Model.DocumentSpec.Content"/>, every entry of
    /// <see cref="Model.DocumentSpec.EmbeddedFonts"/>, and an output intent's
    /// own <see cref="Model.PdfAOutputIntentSpec.IccProfile"/>. Enforced by
    /// <see cref="Model.DocumentSpec.ValidateAggregateAssetBytes"/>, which both
    /// <see cref="Generation.SpecRenderer.Render"/> and
    /// <see cref="Generation.SpecCodeEmitter.Emit"/> call; see that method's
    /// own remark for why this cannot be checked at construction the way
    /// <see cref="MaxAssetBytes"/> itself is.
    /// </summary>
    /// <remarks>
    /// THE GAP THIS CLOSES. <see cref="MaxAssetBytes"/> bounds one byte array.
    /// Nothing bounded their SUM: a specification could hold hundreds of
    /// DISTINCT images, each individually under <see cref="MaxAssetBytes"/>,
    /// or every one of <see cref="MaxEmbeddedFonts"/>'s 100 slots filled at
    /// <see cref="MaxAssetBytes"/> each. MEASURED by the coordinator through
    /// <see cref="Generation.SpecRenderer.Render"/>, every specification below
    /// was inside every existing cap: 500 DISTINCT 4096 by 4096 PNGs, each 2.5
    /// MB of source, rendered 1.30 GB of output in 86,859 ms on desktop; 500
    /// DISTINCT 2048 by 2048 PNGs rendered 596 MB in 22,666 ms; the SAME 500
    /// images as one SHARED instance, after the per-render image cache fix
    /// (see <see cref="Generation.SpecRenderer.RenderContext"/>'s own
    /// <c>ImageCache</c>), rendered 2.79 MB in 109 ms. <see cref="Model.SpecLimits.MaxContentItems"/>
    /// permits 2,000 occurrences, so the first figure extrapolates to roughly
    /// 5 GB: memory exhaustion in the visitor's own tab, not merely a freeze.
    /// </remarks>
    /// <remarks>
    /// COUNTED BY DISTINCT REFERENCE, deliberately the opposite rule from
    /// <see cref="MaxWalkedNodes"/> and <see cref="MaxTotalTextLength"/>, which
    /// count by POSITION so that shared structure cannot amplify a total.
    /// Both rules are correct for what each one bounds. A walked node or a
    /// character is laid out again, in full, at every position that reaches
    /// it, because neither <see cref="Generation.SpecRenderer"/> nor
    /// <see cref="Generation.SpecCodeEmitter"/> caches layout by identity, so
    /// counting by position is the only rule that cannot be defeated by
    /// sharing. An asset byte array is the opposite: since the shared-image
    /// caching fix, <see cref="Generation.SpecRenderer.RenderContext.ImageCache"/>
    /// decodes and embeds a repeated <see cref="Model.ImageSpec"/> INSTANCE
    /// once, and <see cref="Generation.SpecCodeEmitter.DistinctContentImagesByReference"/>
    /// hoists the identical reference into one shared decode in the emitted
    /// C#, so a shared instance genuinely costs once on both sides, measured
    /// directly above (2.79 MB against 596 MB to 1.30 GB for the identical
    /// pixels as distinct instances). Counting by position here would charge
    /// twice for something the cache already made free, penalising exactly
    /// the sharing pattern the caching fix exists to reward, and counting by
    /// value equality would be wrong for the same reason
    /// <see cref="Generation.SpecRenderer.RenderContext.ImageCache"/>'s own
    /// remark gives for keying the cache by reference rather than by
    /// <see cref="Model.ImageSpec"/>'s record equality: two value-equal but
    /// DISTINCT instances are not the same decode, and must not be charged
    /// once between them. <see cref="Generation.SpecCodeEmitter.DistinctContentImagesByReference"/>
    /// is reused directly, by reference-identity <see cref="HashSet{T}"/>,
    /// rather than restating the same rule a second time, so this cap and the
    /// cache it mirrors cannot silently drift apart about which occurrence is
    /// "the same image".
    /// </remarks>
    /// <remarks>
    /// EVERY ASSET MEMBER IS COVERED, not only images. Every entry of
    /// <see cref="Model.DocumentSpec.EmbeddedFonts"/> is loaded by
    /// <see cref="Generation.SpecRenderer.Render"/> unconditionally, via
    /// <c>Document.UseTrueTypeFont</c>, regardless of whether any
    /// <see cref="Model.TextStyleSpec"/> in the specification ever references
    /// it by index, so <see cref="MaxEmbeddedFonts"/> (100) times
    /// <see cref="MaxAssetBytes"/> (20 MB) is 2 GB the model already admitted
    /// before this cap. MEASURED rather than assumed: 100 DISTINCT 410,712
    /// byte font arrays (the shipped Liberation Sans face, cloned), none
    /// referenced by any content style, registered in 34 ms on desktop and
    /// added nothing to the 53,444 byte output; an unreferenced embedded font
    /// is parsed but never actually embedded into the saved bytes. This does
    /// NOT make the 2 GB figure safe to leave uncapped: <c>Document.UseTrueTypeFont</c>
    /// still parses, and <see cref="Model.SpecLimits.ValidateAssetBytes"/>
    /// still clones, every one of those bytes regardless of whether the
    /// library later embeds them, which is memory pressure in the visitor's
    /// own tab per CLAUDE.md control 1 even when the OUTPUT stays small; this
    /// cap bounds that memory directly rather than relying on an output-size
    /// side effect that a future library change could remove. An output
    /// intent's <see cref="Model.PdfAOutputIntentSpec.IccProfile"/> is a single
    /// optional byte array, so it can contribute at most one
    /// <see cref="MaxAssetBytes"/> share; it is summed in for completeness, not
    /// because it was independently found to be a large contributor.
    /// </remarks>
    /// <remarks>
    /// THE VALUE, MEASURED rather than guessed, at 32 MiB (33,554,432 bytes).
    /// Every shipped asset fits with wide headroom: the one bundled font is
    /// 410,712 bytes and the bundled sRGB ICC profile is 3,024 bytes, about
    /// 414 KB together, roughly 80 times under this cap; every
    /// <c>DocumentSpecSamples</c> image is a one-pixel placeholder of a few
    /// dozen bytes. MEASURED AT THIS VALUE, Release configuration, desktop x64:
    /// 18 DISTINCT 2048 by 2048 synthetic PNGs (<c>SyntheticPng.CreateVaryingRgb</c>,
    /// the same generator <c>SharedImageCacheTests</c> uses), totalling
    /// 32,136,696 bytes of source just under this cap, rendered 30,315,242
    /// bytes (28.91 MB) of output in 737 ms. This is the SAME order of
    /// magnitude as the worst case this model already accepts elsewhere: the
    /// <see cref="MaxRunningBandTemplateLength"/> construction's 10,998
    /// pages render in 601 ms on desktop and 35,148 ms in the browser this
    /// application ships to; this cap's own worst case, extrapolated by the
    /// same roughly seventeen to fifty-nine times desktop-to-browser factor
    /// that construction and <see cref="MaxTotalTextLength"/>'s own remark
    /// both measure directly, is on the order of tens of seconds, not the
    /// tens of MINUTES the uncapped
    /// 1.30 GB figure above would extrapolate to at 500 occurrences. Embedded
    /// fonts and an ICC profile were measured and found far cheaper per byte
    /// than an image (100 fonts totalling 39.2 MB registered in 34 ms, against
    /// 30.65 MB of images costing 737 ms), so images are the figure this value
    /// is calibrated against; a specification that spends its whole budget on
    /// fonts instead costs less than the figure above, not more.
    /// </remarks>
    /// <remarks>
    /// NOTE what this cap does NOT bound. Exactly as <see cref="MaxAssetBytes"/>'s
    /// own remark states, this bounds SOURCE bytes, not decoded pixel count: a
    /// well-formed file far smaller than its own <see cref="MaxAssetBytes"/>
    /// share can still declare an enormous pixel grid, and that remains the
    /// decoding library's own decoded-pixel-count guard's responsibility, not
    /// this cap's. This cap answers a different question: given assets the
    /// per-image guard already accepts, how many of them, and how large in
    /// total, may one specification hold at once.
    /// </remarks>
    public const int MaxTotalAssetBytes = 32 * 1024 * 1024;

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
    /// already-capped object graph from being referenced repeatedly. One
    /// hundred list-item slots (<see cref="MaxListItemChildren"/>)
    /// that all point at the one shared, sixty-three-level list-item chain
    /// construct only about SIXTY-FOUR distinct objects in total (the
    /// chain's own 63 levels, plus the one top-level item holding all 100
    /// references to it), well inside every per-collection cap, but a walk
    /// that visits a shared reference once per slot performs the work of one
    /// hundred distinct chains, roughly 6,300 node visits. This limit is
    /// what actually bounds that work: counting stops
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
    /// leave when multiplied together: at their current values, 5,000 and
    /// 100,000 respectively, unchanged since each was introduced, that
    /// product is 500,000,000 characters, about 954 MB of UTF-16. MEASURED
    /// at those same two constants, a specification carrying 200,062,485
    /// characters on the way to that product emitted its C# in 1.3 s and
    /// rendered a 40.6 MB PDF in 8,972 ms on desktop x64. Nothing in this
    /// file promises a specific generation time, only that generation
    /// completes, by yielding to the browser's own macrotask queue rather
    /// than blocking it unboundedly (see <see cref="Generation.SpecRenderer"/>
    /// and <see cref="Generation.SpecCodeEmitter"/>); the generation-time
    /// figures this remark exists to bound follow below, measured at THIS
    /// value's own boundary rather than at that unbounded product.
    /// <para>
    /// This value was originally derived from a stack boundary, because
    /// <c>VellumPdf.Layout</c> 2.3.0 recursed once per page continuation and a
    /// specification forcing enough pages overflowed the CLR stack, which
    /// cannot be caught. 2.3.1 converts both of that library's pagination
    /// passes to loops, so no text volume this file admits can crash a caller
    /// any longer, and that derivation is gone. What remains is a bound on
    /// generation time and on output size. Measured directly against 2.3.1,
    /// through <see cref="Generation.SpecRenderer.Render"/> at this value's
    /// own cap: 6,000 characters of the widest glyph at 36 points on a 200
    /// by 200 point page with this model's own DEFAULT margins renders 6,000
    /// pages and 2,253,004 bytes in 309 ms on desktop x64. NOTE: 36 points is
    /// not this file's font-size ceiling; <see cref="MaxFontSize"/> now
    /// permits up to 1,000 points, and per the remark there, a larger font
    /// does not change this worst case, only makes it easier to reach.
    /// </para>
    /// <para>
    /// Before this value was lowered to 6,000, at its previous setting of
    /// 20,000, the same construction (the widest glyph at 36 points on a 200
    /// by 200 point page at default margins) rendered 20,000 pages and
    /// 7.5 MB in 1,578 ms on desktop x64, and took 47,963 ms in the browser
    /// this application ships to, measured through the site's own
    /// elapsed-time display in a published Release build. The same text at
    /// ZERO margins, which was 1,000 pages rather than 20,000 and was the
    /// worst case this model admitted while it still carried a page-geometry
    /// bound, took 1,955 ms in the same browser against 88 ms on desktop. The
    /// factor between desktop and browser is not one fixed number: the
    /// 20,000-character pair gives 47,963 over 1,578, about thirty; the
    /// zero-margin pair gives 1,955 over 88, about twenty-two; the current
    /// 6,000-character construction, measured further below, gives 5,205
    /// over 309, about seventeen; and the running-band construction, also
    /// measured further below, gives 35,148 over 601, about fifty-nine.
    /// Across these four pairs the factor ranges from about seventeen to
    /// about fifty-nine, a RANGE rather than a single number to multiply by,
    /// and every point in it is far above the three to ten a reader
    /// extrapolating from any one pair alone might assume. Forty-eight
    /// seconds is a tab that looks dead rather than busy, so this value was
    /// lowered to bound it. The lever is deliberately this cap and
    /// <see cref="MaxWalkedNodes"/> rather than a return of the page-geometry
    /// bound: the geometry bound refused page sizes and font sizes the
    /// library itself accepts, which made the catalogue understate the
    /// library, and it was defeated five times by levers inside the
    /// library's own layout. These two caps bound the same worst case from
    /// the other side, using only quantities the model can see exactly.
    /// </para>
    /// <para>
    /// WHERE THE FLOOR IS, measured rather than chosen. The worst case is one
    /// rendered line per character on a content box too narrow for two, so
    /// PAGE COUNT is exactly linear in this value: 20,000 characters is
    /// 20,000 pages. Browser TIME is not linear in it; it is superlinear, as
    /// the table below shows. The floor is set by the regression guards.
    /// <c>DeepPaginationTests</c>' list reproduction
    /// (<c>ManyShortListItemsRepro</c>) needs 4,950 characters across 4,951
    /// nodes; <see cref="MaxWalkedNodes"/> can go as low as 4,951 before
    /// that same reproduction breaks, verified directly by setting the
    /// constant to 4,951 and confirming <c>DeepPaginationTests</c> stays
    /// green. Every sample shipped in <c>DocumentSpecSamples</c> fits under
    /// 4,000 characters, verified by lowering this cap and running the
    /// suite. Both figures are satisfied by a value well below 6,000, so
    /// neither sets this value's floor. <c>DeepPaginationTests</c>' OTHER
    /// reproduction, <c>DefaultMarginsWideGlyphRepro</c>, does: it fills its
    /// content box with the four-character string <c>"WWW "</c>, and at that
    /// geometry three of every four characters produce a page, not one: 0.75
    /// pages per character, not the 1 an earlier version of this remark
    /// assumed. Its own render assertion needs page count over 4,000 to mean
    /// anything, which that ratio needs at least 5,336 characters to reach;
    /// <c>TextAndNodeBudgets_StayAboveTheCrashThreshold</c> asserts exactly
    /// this floor. A cap lowered to 5,000 would still satisfy the
    /// 4,950- and 4,000-character constraints named above while leaving that
    /// render assertion failing on a document too shallow to mean anything.
    /// 6,000 is therefore set with headroom over the true floor, 5,336, not
    /// over either of the other two figures.
    /// </para>
    /// <para>
    /// MEASURED AT THIS VALUE, on desktop x64 and, where noted, in the same
    /// browser and the same way as the 47,963 ms figure above:
    /// <list type="table">
    /// <item><term>Text budget alone (6,000 characters, the construction above)</term><description>6,000 pages, 309 ms desktop</description></item>
    /// <item><term>Node budget only (1,665 list items, each with two empty children: 4,996 of 5,000 nodes, not the full budget)</term><description>4,995 pages, 139 ms desktop</description></item>
    /// <item><term>Both budgets, as first recorded (a 6,000-character paragraph of solid 'W' plus a separate list of 1,664 top-level items, each with two empty children, on this same 200 by 200 page at default margins)</term><description>10,992 pages, 387 ms desktop, 11,985 ms browser</description></item>
    /// <item><term>Both budgets, the maximal construction below</term><description>10,998 pages, 342 ms desktop</description></item>
    /// </list>
    /// A list item with empty text is a line-producing node that costs
    /// nothing against the text budget, so a specification can spend both
    /// budgets at once; the last two rows do exactly that. The maximal
    /// construction, the deepest document this model admits, is one
    /// <see cref="Model.ListSpec"/> of 1,667 top-level
    /// <see cref="Model.ListItemSpec"/>: 1,666 of them each carry two empty
    /// children and the last carries none, for 1,667 + 1,666 &#215; 2 = 4,999
    /// items, plus the one node the list itself costs as a
    /// <see cref="Model.DocumentSpec.Content"/> entry, exactly
    /// <see cref="MaxWalkedNodes"/>. The first item alone carries the full
    /// 6,000-character text budget as solid <c>'W'</c>; every other item, at
    /// both levels, is empty. The page is 200 by 200 points, at this model's
    /// own default 72-point margins, in 36-point Helvetica. NOTE the
    /// relationship is not linear in this value: in the browser, a third of
    /// the characters (6,000 against the previous setting's 20,000) cost a
    /// ninth of the time (5,205 ms against 47,963 ms); on desktop the same
    /// pair costs about a fifth (309 ms against 1,578 ms), not a third
    /// either way, because output size falls with it too.
    /// </para>
    /// <para>
    /// Adding a header and a footer, each with a
    /// <see cref="Model.RunningBandSpec.Height"/> of 40 and a
    /// <see cref="Model.RunningBandSpec.Template"/> at
    /// <see cref="MaxRunningBandTemplateLength"/>'s own cap, on a page
    /// heightened to 200 by 280 points, the width unchanged, to leave room
    /// for both bands, costs more than the deepest document alone: see the
    /// remark on <see cref="MaxRunningBandTemplateLength"/> for that
    /// construction in full and what it costs by template length. That
    /// construction renders 10,998 pages, the same count as the maximal
    /// construction above, because the 80 points added to the page's height
    /// are exactly what the two 40-point bands reserve, at 601 ms on desktop
    /// x64 and at 35,148 ms in the browser this application ships to,
    /// measured through the site's own elapsed-time display in a published
    /// Release build. THAT figure, not the 11,985 ms of the recorded
    /// both-budgets document above, is the true ceiling this file's caps
    /// together buy. A running band is laid out once per page and forces a
    /// second pagination pass; neither cost is visible to
    /// <see cref="MaxWalkedNodes"/> or to this value, which is why
    /// <see cref="MaxRunningBandTemplateLength"/> exists as a third,
    /// independent cap rather than being folded into either of these two.
    /// </para>
    /// <para>
    /// Thirty-five seconds is a known property of a browser-hosted,
    /// single-threaded renderer pushed to paginate an already-deep document
    /// twice, not a defect this file can tune away. Bounding it further
    /// means giving up the ability to express a document that reaches the
    /// 2.3.0 crash threshold at all, which would mean deleting the
    /// regression guards for the defect this model spent ten review rounds
    /// on; that trade was not taken. This value and <see cref="MaxWalkedNodes"/>
    /// stay where they are, deliberately, and tuning them further has
    /// stopped. Together with <see cref="MaxRunningBandTemplateLength"/>,
    /// these caps reduce the freeze well below the 47,963 ms the previous,
    /// 20,000-character setting cost with no band at all; they do not, and
    /// are not intended to, remove it.
    /// </para>
    /// </remarks>
    public const int MaxTotalTextLength = 6_000;

    /// <summary>
    /// The lower bound on <see cref="Model.PageSizeSpec.WidthPoints"/> and
    /// <see cref="Model.PageSizeSpec.HeightPoints"/>. A dimension must be a
    /// positive, finite number of points; this bound exists to reject zero and
    /// negative values with a legible message at construction, and to do so
    /// where every other numeric member of this model is bounded.
    /// </summary>
    /// <remarks>
    /// This figure was 200 while <c>VellumPdf.Layout</c> 2.3.0 recursed once
    /// per page continuation, because a page small enough forced enough
    /// continuations to overflow the CLR stack. 2.3.1 removed that recursion,
    /// and with it the only reason to refuse a page the library itself
    /// accepts. NOTE: a page too small to hold one line is now answered by the
    /// library rather than by this model, and answered well. Measured directly
    /// against 2.3.1: a 1 by 1 point page, and a 3 by 3 one, each raise a
    /// catchable <see cref="InvalidOperationException"/> reading "An element is
    /// too tall to fit on a single page" within about 10 ms. Neither hangs,
    /// neither iterates to the library's continuation ceiling, and neither
    /// crashes. That measurement is what makes a bound of 1 defensible rather
    /// than merely permissive.
    /// </remarks>
    public const double MinPageDimensionPoints = 1;

    /// <summary>
    /// The upper bound on <see cref="Model.PageSizeSpec.WidthPoints"/> and
    /// <see cref="Model.PageSizeSpec.HeightPoints"/>. A generous sanity ceiling
    /// of about 278 inches rather than a measured one. A larger page means
    /// fewer pages, so nothing about output size or generation time argues for
    /// a tighter figure.
    /// </summary>
    public const double MaxPageDimensionPoints = 20_000;

    /// <summary>
    /// Caps <see cref="Model.TextStyleSpec.FontSize"/>. A generous sanity
    /// ceiling, in the same register as <see cref="MaxStrokeWidthPoints"/> and
    /// <see cref="MaxIndentPoints"/>: its job is to reject a value no
    /// typography could intend, with a legible message, alongside the NaN and
    /// non-positive checks beside it.
    /// </summary>
    /// <remarks>
    /// This figure was 36, and 72 before that, while a large font size was a
    /// route to the stack overflow described on
    /// <see cref="MinPageDimensionPoints"/>: fewer characters per page meant
    /// more pages, and enough pages killed the tab. That is no longer true of
    /// <c>VellumPdf.Layout</c> 2.3.1. NOTE: font size no longer changes the
    /// worst case this file admits at all. A rendered line holds at least one
    /// character however large the type, so the worst case is bounded by
    /// <see cref="MaxTotalTextLength"/> and <see cref="MaxWalkedNodes"/>
    /// whatever this value is; a larger font makes that worst case easier to
    /// reach and no larger. The showcase must demonstrate typography at
    /// display scale, per plan section 6.2, and a ceiling derived from a
    /// defect the library has fixed would make the catalogue understate what
    /// the library does.
    /// </remarks>
    public const double MaxFontSize = 1_000;

    /// <summary>
    /// Caps <see cref="Model.TextStyleSpec.Leading"/> when set. A generous
    /// sanity ceiling on the same footing as <see cref="MaxFontSize"/>, and
    /// for the same reason: leading enlarges a line's height exactly as a
    /// larger font does, and neither changes the worst case this file admits.
    /// </summary>
    /// <remarks>
    /// NOTE: this cap does not bound the most dangerous leading a
    /// specification can carry, and never did. <see cref="Generation.SpecRenderer"/>
    /// passes an UNSET <see cref="Model.TextStyleSpec.Leading"/> to the library
    /// as a literal <c>0</c>, and <see cref="Generation.SpecCodeEmitter"/>
    /// emits no <c>Leading</c> property at all in that case, which leaves the
    /// library's own <c>TextStyle.Leading</c> at its own default of <c>0</c>.
    /// Both paths agree, so the round trip does not diverge. The library reads
    /// a <c>0</c> leading as a request to compute its own line height from the
    /// font, and that computed value may exceed this cap. It remains bounded
    /// by the font size that produces it.
    /// </remarks>
    public const double MaxLeadingPoints = 1_000;

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

    /// <summary>
    /// Caps <see cref="Model.PdfAOutputIntentSpec.ComponentCount"/>. This was
    /// the one numeric member in the model with no cap of its own. <see cref="IccProfileHeader.Validate"/> only cross-checks it
    /// against the profile's OWN declared colour space when that colour
    /// space is one of the four <see cref="IccProfileHeader.Validate"/>
    /// recognises (GRAY, RGB, Lab, CMYK); a profile declaring any other
    /// colour space left <see cref="Model.PdfAOutputIntentSpec.ComponentCount"/>
    /// completely unchecked, so a negative or absurdly large value would
    /// construct successfully. A generous sanity ceiling: ICC.1:2010's own
    /// channel-count field is wide enough to carry values far larger than
    /// this, but every colour space the standard names by acronym (up to
    /// 9CLR) fits comfortably under 15, and every sample in this repository
    /// uses 2, 3 or 4.
    /// </summary>
    public const int MaxIccComponentCount = 15;

    /// <summary>The lower bound on <see cref="Model.PdfAOutputIntentSpec.ComponentCount"/>: a colour space has at least one channel.</summary>
    public const int MinIccComponentCount = 1;

    /// <summary>Validates <see cref="Model.PdfAOutputIntentSpec.ComponentCount"/> against <see cref="MinIccComponentCount"/> and <see cref="MaxIccComponentCount"/>.</summary>
    public static int ValidateIccComponentCount(int value, string paramName) =>
        value is >= MinIccComponentCount and <= MaxIccComponentCount
            ? value
            : throw new ArgumentException(
                $"{paramName} must be between {MinIccComponentCount} and {MaxIccComponentCount}; got {value}.",
                paramName);

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
    /// Throws unless <paramref name="value"/> is one of <typeparamref name="TEnum"/>'s
    /// own named members; returns it otherwise.
    /// </summary>
    /// <remarks>
    /// Every non-flags enum a specification carries reaches
    /// <see cref="Generation.SpecCodeEmitter"/>, which writes it into the
    /// displayed C# by name. A value outside the enumeration emits text such as
    /// <c>ListStyle.99</c>, which does not compile, while
    /// <see cref="Generation.SpecRenderer"/> renders the same specification
    /// without complaint. That is the divergence the round trip exists to
    /// prevent, and it is invisible to a guard that only compares whether the
    /// two consumers agree, because they both succeed.
    /// <para>
    /// Measured directly before this check existed: <c>(ListStyle)99</c> and
    /// <c>(HorizontalAlignment)99</c> both rendered, and <c>(Standard14)99</c>
    /// made <see cref="Generation.SpecRenderer.Render"/> throw a raw
    /// <see cref="IndexOutOfRangeException"/>, outside its own documented
    /// exception contract.
    /// </para>
    /// <para>
    /// NOTE: this is for NON-FLAGS enumerations only.
    /// <see cref="PdfPermissions"/> keeps <see cref="ValidatePermissions"/>,
    /// because a legitimate union of two flags is not itself a named member and
    /// <see cref="Enum.IsDefined{TEnum}(TEnum)"/> would reject it. Do not
    /// unify the two.
    /// </para>
    /// </remarks>
    public static TEnum ValidateEnum<TEnum>(TEnum value, string paramName)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value)
            ? value
            : throw new ArgumentException(
                $"{paramName} must be one of {typeof(TEnum).Name}'s named members; got {(object)value}.",
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

    public static bool Matches(ImageFormat format, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return format switch
        {
            ImageFormat.Png => bytes.AsSpan().StartsWith(PngMagic),
            ImageFormat.Jpeg => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            ImageFormat.Bmp => bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D,
            ImageFormat.Gif => bytes.AsSpan().StartsWith(Gif87a) || bytes.AsSpan().StartsWith(Gif89a),
            ImageFormat.Tiff => bytes.Length >= 4 &&
                ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
                 (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)),
            // Unreachable through any DocumentSpec today, because ImageSpec.Format validates against this same
            // enum at construction; kept anyway as a defensive boundary against a future ImageFormat member the
            // switch above has not been taught to sniff, so an unrecognised value fails loudly here rather than
            // silently falling through to "does not match" and being treated as a rejected, well-formed image.
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unrecognised image format."),
        };
    }
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
        ArgumentNullException.ThrowIfNull(profile);
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
