using System.Text;
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
/// line separator, and so on) has a representation.
/// </remarks>
/// <remarks>
/// A collection the library requires to be non-empty rejects an empty value at
/// construction, with a message naming the actual problem, rather than letting
/// the caller build the spec and hear about it later from deep inside the
/// library: see <see cref="Content"/>, <see cref="PieChartSpec.Slices"/>,
/// <see cref="TableSpec.Rows"/> and <see cref="TableRowSpec.Cells"/>.
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
    public EdgeInsets Margins
    {
        get;
        init => field = SpecLimits.ValidateEdgeInsets(value, nameof(Margins));
    } = new(72);

    /// <summary>
    /// The style registered as the document's default through
    /// <c>Document.SetDefaultFont</c>. Per section 3.4.0 of the plan, that
    /// member is consulted only by the <c>Document.Add(string, TextStyle?)</c>
    /// overload, which a <see cref="PlainTextSpec"/> with no explicit
    /// <see cref="PlainTextSpec.Style"/> maps to.
    /// </summary>
    /// <remarks>
    /// NOTE: content items do not all behave alike here. <see cref="HeadingSpec"/>
    /// and <see cref="ParagraphSpec"/> resolve their own fallback style at
    /// construction and never read this value. <see cref="ListItemSpec"/> and
    /// <see cref="TableCellSpec"/> stay <see langword="null"/> when left
    /// unstyled and are resolved later by their container
    /// (<see cref="ListSpec.DefaultStyle"/> or <see cref="TableSpec.DefaultCellStyle"/>),
    /// which may itself be <see langword="null"/>; this property governs
    /// neither. Only an unstyled <see cref="PlainTextSpec"/> reads it.
    /// </remarks>
    /// <remarks>
    /// If this style's <see cref="TextStyleSpec.Font"/> is <see cref="FontKind.Embedded"/>,
    /// its index must name an actual entry of <see cref="EmbeddedFonts"/>; see
    /// <see cref="ValidateEmbeddedFontReferences"/> for why that cannot be
    /// checked here, at construction, the way every other cross-property
    /// check in this type is.
    /// </remarks>
    public required TextStyleSpec DefaultTextStyle
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(DefaultTextStyle));
    }

    /// <summary>
    /// Raw TrueType font bytes for every embedded face the document uses. A
    /// <see cref="FontSpec"/> of kind <see cref="FontKind.Embedded"/> refers to
    /// one of these by position. Each entry is registered with the underlying
    /// <c>Document</c> exactly once, regardless of how many styles reference it.
    /// </summary>
    /// <remarks>
    /// Per plan section 5.4, the list is capped at <see cref="SpecLimits.MaxEmbeddedFonts"/>
    /// entries, each entry is capped at <see cref="SpecLimits.MaxAssetBytes"/>,
    /// and both the list and each entry's own byte array are snapshotted at
    /// construction (<see cref="SpecLimits.ValidateAssetBytes"/> returns a
    /// defensive copy), so mutating either a list passed in, or a byte array
    /// already inside it, cannot change this value afterward.
    /// </remarks>
    public IReadOnlyList<byte[]> EmbeddedFonts
    {
        get;
        init => field = ValidateEmbeddedFonts(value);
    } = [];

    /// <summary>The document's content, laid out in the order given. A document must have at least one item.</summary>
    /// <remarks>
    /// Per plan section 5.4, the list is capped at <see cref="SpecLimits.MaxContentItems"/>
    /// and snapshotted with a collection expression at construction, so an
    /// aliased, later-mutated <c>List&lt;ContentItemSpec&gt;</c> cannot empty
    /// this property out from under a fully constructed <see cref="DocumentSpec"/>.
    /// Every <see cref="ImageSpec"/> in the list also has its declared
    /// <see cref="ImageSpec.Format"/> checked against the magic bytes of its
    /// own <see cref="ImageSpec.Bytes"/>, which requires both properties to be
    /// already set and so cannot be done inside <see cref="ImageSpec"/> itself.
    /// The total number of nodes walking this list would visit (counted
    /// exactly as <see cref="Generation.SpecRenderer"/> and
    /// <see cref="Generation.SpecCodeEmitter"/> walk it, once per position
    /// rather than once per distinct object) is checked against
    /// <see cref="SpecLimits.MaxWalkedNodes"/>, which is the only thing that
    /// stops a small number of objects sharing one deeply reused subtree from
    /// multiplying the work either side performs; every other cap in
    /// <see cref="SpecLimits"/> bounds a collection's own size and cannot, by
    /// itself, prevent that multiplication. The same walk sums every
    /// text-bearing node's own text length against <see cref="SpecLimits.MaxTotalTextLength"/>,
    /// which is what stops <see cref="SpecLimits.MaxWalkedNodes"/> and
    /// <see cref="SpecLimits.MaxTextLength"/> from being multiplied together
    /// into an unreasonably large total.
    /// <para>
    /// Cycle 7 audit of every string this record can hold, against what the
    /// walk actually counts: <see cref="HeadingSpec.Text"/>,
    /// <see cref="HeadingSpec.BookmarkTitle"/>, <see cref="PlainTextSpec.Text"/>,
    /// each <see cref="TextRunSpec.Text"/>, each <see cref="ListItemSpec.Text"/>
    /// (at every depth), each <see cref="TableCellSpec.Content"/>,
    /// <see cref="ImageSpec.AltText"/>, <see cref="PieChartSpec.AltText"/> and
    /// every <see cref="PieSlice.Label"/> are all reachable through THIS list
    /// and are all counted (the last two were NOT, before cycle 7: a
    /// specification of charts with maximal-length slice labels reached
    /// 490,220,429 characters through that gap, 24.5 times
    /// <see cref="SpecLimits.MaxTotalTextLength"/>, entirely inside a total
    /// this walk was already supposed to bound).
    /// </para>
    /// <para>
    /// Round nine audit, Medium 3: two FURTHER kinds of member reachable
    /// through this list were still missing, and are now counted too. Every
    /// <see cref="TextStyleSpec.LinkUri"/> a style reachable from
    /// <see cref="Content"/> can carry (a paragraph run's, a heading's, a
    /// list item's, a list's own <see cref="ListSpec.DefaultStyle"/>, a
    /// table cell's, and a table's own <see cref="TableSpec.DefaultCellStyle"/>),
    /// capped individually at <see cref="SpecLimits.MaxUriLength"/> (2,048)
    /// but, like every other member audited above, multipliable by shared
    /// structure; and <see cref="HeadingSpec.Language"/>,
    /// <see cref="ParagraphSpec.Language"/>, <see cref="ListItemSpec.Language"/>
    /// (at every depth) and <see cref="TableCellSpec.Language"/>. Measured
    /// directly before this fix: four paragraphs of 1,000 runs each, every
    /// run carrying a distinct maximal-length <see cref="TextStyleSpec.LinkUri"/>,
    /// counted only 4,000 characters (the runs' own text) while the emitted
    /// C# carried 8,608,755. <see cref="DocumentSpec.Language"/> itself, on
    /// this record rather than a member reachable THROUGH <see cref="Content"/>,
    /// remains outside this walk for the same reason the members below are.
    /// </para>
    /// <para>
    /// <see cref="RunningBandSpec.Template"/> on <see cref="Header"/> and
    /// <see cref="Footer"/> is DELIBERATELY outside this walk, but NOT for
    /// the reason once given here. It is a single top-level property that
    /// appears at most once per specification, so it cannot be multiplied by
    /// shared STRUCTURE the way a list, table or chart entry can; that part
    /// was always true. It misses that a running band is laid out once per
    /// PAGE, and page count is exactly the quantity
    /// <see cref="SpecLimits.MaxTotalTextLength"/> and
    /// <see cref="SpecLimits.MaxWalkedNodes"/> exist to bound, so a template
    /// this walk (or any cap on <see cref="Content"/>) cannot see IS
    /// multiplied, by pagination rather than by structure.
    /// <see cref="SpecLimits.MaxRunningBandTemplateLength"/> bounds it
    /// directly instead, at a value measured small enough that the
    /// multiplication it cannot avoid stays cheap. NOTE: this remark once
    /// carried its own figures here (page count, bytes, milliseconds); they
    /// were deleted, deliberately, because they were measured on a document
    /// 2.2 times shallower than this model actually admits and had drifted
    /// out of step with <see cref="SpecLimits.MaxRunningBandTemplateLength"/>'s
    /// own remark, which carries the correct, current measurement (the
    /// construction it is measured on, and the true page count, roughly
    /// 11,000, not the smaller figure once repeated here). Restating a
    /// number here would only give it a second place to go stale; see that
    /// constant's own remark for the figures instead.
    /// </para>
    /// <para>
    /// <see cref="RunningBandSpec.Style"/>'s own <see cref="TextStyleSpec.LinkUri"/>
    /// on <see cref="Header"/> and <see cref="Footer"/>, every
    /// <see cref="DocumentMetadataSpec"/> field, an output intent's own
    /// identifier and info string, and an <see cref="EncryptionSpec"/>
    /// password remain DELIBERATELY outside this walk, and for these the
    /// original reasoning holds: each is a single top-level property of this
    /// record (or of a record one of those properties holds), appearing at
    /// most once per specification, so none of them can be multiplied by
    /// shared structure the way a list, table or chart entry can, AND, unlike
    /// <see cref="RunningBandSpec.Template"/>, none of them is laid out once
    /// per page either. For the link specifically this was verified rather
    /// than assumed: a running band whose style carries a maximal-length
    /// (<see cref="SpecLimits.MaxUriLength"/>, 2,048)
    /// <see cref="TextStyleSpec.LinkUri"/> renders output of IDENTICAL
    /// LENGTH to the same band with none, across a footer-only specification
    /// that renders 2,475 pages, because the library does not turn it into a
    /// per-page link annotation. NOTE: this is a length comparison, not a
    /// byte-for-byte one; two renders of the same specification are never
    /// byte-identical, because the library writes a random document
    /// identifier on every render. See the remark on
    /// <see cref="SpecLimits.MaxRunningBandTemplateLength"/> for the figure
    /// and the test that guards it. <see cref="SpecLimits.MaxTextLength"/> (or
    /// <see cref="SpecLimits.MaxUriLength"/>, for a link) alone already
    /// bounds each of these individually, and that bound cannot be
    /// out-multiplied by anything reachable from a single occurrence or by
    /// pagination.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Every <see cref="TextStyleSpec"/> reachable from this list, from
    /// <see cref="Header"/> and <see cref="Footer"/>, and
    /// <see cref="DefaultTextStyle"/> itself, must eventually have its
    /// <see cref="FontSpec.EmbeddedFontIndex"/> checked against
    /// <see cref="EmbeddedFonts"/> whenever its <see cref="FontSpec.Kind"/> is
    /// <see cref="FontKind.Embedded"/>. See <see cref="ValidateEmbeddedFontReferences"/>
    /// for that check and for why it is NOT performed here, at construction,
    /// unlike every other cross-property check in this type.
    /// </remarks>
    public required IReadOnlyList<ContentItemSpec> Content
    {
        get;
        init => field = ValidateContent(value);
    }

    /// <summary>
    /// The running header repeated on every page, if any. See the remark on
    /// <see cref="DefaultTextStyle"/> about its style's embedded font index.
    /// </summary>
    public RunningBandSpec? Header { get; init; }

    /// <summary>
    /// The running footer repeated on every page, if any. See the remark on
    /// <see cref="DefaultTextStyle"/> about its style's embedded font index.
    /// </summary>
    public RunningBandSpec? Footer { get; init; }

    /// <summary>The conformance profile the document claims, or <see cref="DocumentConformance.None"/>.</summary>
    public DocumentConformance Conformance
    {
        get;
        init => field = SpecLimits.ValidateEnum(value, nameof(Conformance));
    } = DocumentConformance.None;

    /// <summary>Whether the document carries a tagged structure tree.</summary>
    public bool Tagged { get; init; }

    /// <summary>
    /// Whether <c>Document.Save</c> uses PDF 1.5+ object streams and a
    /// cross-reference stream, for smaller output, instead of the classic
    /// cross-reference table. Forwarded directly to <c>Document.UseObjectStreams</c>;
    /// the library's own default is <see langword="false"/>, matched here.
    /// </summary>
    /// <remarks>
    /// NOTE: the library throws <see cref="NotSupportedException"/> from
    /// <c>Document.Save</c> when this is combined with <see cref="Encryption"/>.
    /// This model does not guard that combination itself, consistent with
    /// every other cross-feature incompatibility the library enforces at
    /// save time rather than at construction, such as a PDF/A
    /// <see cref="Conformance"/> claim together with <see cref="Encryption"/>.
    /// </remarks>
    public bool UseObjectStreams { get; init; }

    /// <summary>The document's natural language, as a BCP 47 tag such as <c>"en"</c>.</summary>
    public string? Language
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxLanguageTagLength, nameof(Language));
    }

    /// <summary>Document information dictionary entries, if any are set.</summary>
    public DocumentMetadataSpec? Metadata { get; init; }

    /// <summary>The output intent to embed, if any.</summary>
    /// <remarks>
    /// Per plan section 5.4, a <see cref="PdfAOutputIntentSpec"/> has its
    /// <see cref="PdfAOutputIntentSpec.IccProfile"/> header validated here
    /// against its own <see cref="PdfAOutputIntentSpec.ComponentCount"/>,
    /// which requires both properties to already be set and so cannot be done
    /// inside <see cref="PdfAOutputIntentSpec"/> itself. Left unvalidated, a
    /// three-byte junk profile would embed silently into a document claiming
    /// PDF/A or PDF/UA conformance.
    /// </remarks>
    public OutputIntentSpec? OutputIntent
    {
        get;
        init => field = value switch
        {
            PdfAOutputIntentSpec pdfA => Validated(pdfA),
            _ => value,
        };
    }

    /// <summary>
    /// Encryption settings, if the document is to be encrypted.
    /// </summary>
    /// <remarks>
    /// Per plan section 5.4, a restricted <see cref="EncryptionSpec.Permissions"/>
    /// set requires an <see cref="EncryptionSpec.OwnerPassword"/> that is both
    /// non-empty AND distinct from <see cref="EncryptionSpec.UserPassword"/>.
    /// The library authenticates full owner access to whichever password
    /// actually opens the document: with <see cref="EncryptionSpec.OwnerPassword"/>
    /// left unset, that password is <see cref="EncryptionSpec.UserPassword"/>,
    /// and setting <see cref="EncryptionSpec.OwnerPassword"/> equal to
    /// <see cref="EncryptionSpec.UserPassword"/> reaches exactly the same
    /// outcome by a different route: there is still only one password, so it
    /// still authenticates as owner. Either way, a restricted permission set
    /// would bind nobody who can open the file, and the PDF is the one
    /// artefact that leaves the machine. This check requires both properties
    /// to already be set and so cannot be done inside <see cref="EncryptionSpec"/>
    /// itself.
    /// </remarks>
    public EncryptionSpec? Encryption
    {
        get;
        init => field = ValidateEncryption(value);
    }

    private static IReadOnlyList<byte[]> ValidateEmbeddedFonts(IReadOnlyList<byte[]> value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(EmbeddedFonts));

        if (value.Count > SpecLimits.MaxEmbeddedFonts)
        {
            throw new ArgumentException(
                $"A document must not have more than {SpecLimits.MaxEmbeddedFonts} embedded fonts; got {value.Count}.",
                nameof(EmbeddedFonts));
        }

        return [.. value.Select(font => SpecLimits.ValidateAssetBytes(font, nameof(EmbeddedFonts)))];
    }

    private static IReadOnlyList<ContentItemSpec> ValidateContent(IReadOnlyList<ContentItemSpec> value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(Content));

        if (value.Count == 0)
        {
            throw new ArgumentException("A document must have at least one item of content.", nameof(Content));
        }

        if (value.Count > SpecLimits.MaxContentItems)
        {
            throw new ArgumentException(
                $"A document must not have more than {SpecLimits.MaxContentItems} items of content; got {value.Count}.",
                nameof(Content));
        }

        var snapshot = (IReadOnlyList<ContentItemSpec>)[.. value];

        foreach (var item in snapshot)
        {
            if (item is null)
            {
                throw new ArgumentNullException(nameof(Content), "Content must not contain a null item.");
            }

            if (!IsRecognisedContentItemType(item))
            {
                throw new ArgumentException(
                    $"Content contains a {item.GetType()}, which neither SpecRenderer nor SpecCodeEmitter " +
                    "recognises. Only the eight ContentItemSpec subtypes both consumers switch over " +
                    "(PlainTextSpec, HeadingSpec, ParagraphSpec, ListSpec, TableSpec, ImageSpec, PieChartSpec, " +
                    "LineSeparatorSpec) may appear here; see the remark on ContentItemSpec.",
                    nameof(Content));
            }

            if (item is ImageSpec image && !ImageSignature.Matches(image.Format, image.Bytes))
            {
                throw new ArgumentException(
                    $"ImageSpec.Bytes does not match the declared ImageFormat.{image.Format}; the file's own signature says otherwise.",
                    nameof(Content));
            }
        }

        var walk = new ContentWalkState();

        foreach (var item in snapshot)
        {
            if (!walk.TryVisitNode(item))
            {
                break;
            }
        }

        if (walk.NodeLimitExceeded)
        {
            throw new ArgumentException(
                $"This specification's content would require walking more than {SpecLimits.MaxWalkedNodes} " +
                "nodes to render or emit, counting the walk itself rather than the number of distinct objects " +
                "constructed, so a small number of objects sharing one deeply reused subtree cannot multiply " +
                "the work performed. Reduce nesting, breadth, or the amount of shared structure.",
                nameof(Content));
        }

        if (walk.CharacterLimitExceeded)
        {
            throw new ArgumentException(
                $"This specification's content would carry more than {SpecLimits.MaxTotalTextLength} " +
                "characters across every text-bearing node, counted the same way as the node walk above so " +
                "that sharing cannot amplify it. Reduce the total amount of text.",
                nameof(Content));
        }

        return snapshot;
    }

    /// <summary>
    /// Whether <paramref name="item"/> is one of the eight concrete
    /// <see cref="ContentItemSpec"/> subtypes <see cref="Generation.SpecRenderer.AddContentItem"/>
    /// and <see cref="Generation.SpecCodeEmitter.EmitContentItem"/> both
    /// switch over. <see cref="ContentItemSpec"/> is a public, non-sealed
    /// record any assembly may extend, and neither consumer's switch carries
    /// a <see langword="default"/> arm any more (a <see langword="switch"/>
    /// STATEMENT does not require one, unlike a switch EXPRESSION): before
    /// this check existed, an unrecognised subtype passed construction,
    /// rendered as though it were absent (silently skipped by
    /// <see cref="Generation.SpecRenderer.AddContentItem"/>'s switch falling
    /// through with no arm to match), and made
    /// <see cref="Generation.SpecCodeEmitter.EmitContentItem"/> throw instead,
    /// which is exactly the divergence CLAUDE.md's round-trip invariant
    /// forbids. Rejecting it HERE, at construction, is what lets both
    /// consumers omit a matching defensive arm entirely rather than
    /// duplicating this membership check on both sides: a type that cannot
    /// reach either switch needs no arm to reject it there.
    /// </summary>
    private static bool IsRecognisedContentItemType(ContentItemSpec item) =>
        item is PlainTextSpec or HeadingSpec or ParagraphSpec or ListSpec or TableSpec or ImageSpec or PieChartSpec or LineSeparatorSpec;

    /// <summary>
    /// Threads two running totals through one walk of <see cref="Content"/>,
    /// visiting a shared reference once per position it occupies exactly as
    /// <see cref="Generation.SpecRenderer"/> and <see cref="Generation.SpecCodeEmitter"/>
    /// do, never deduplicated by object identity: the node count against
    /// <see cref="SpecLimits.MaxWalkedNodes"/> and the character count
    /// against <see cref="SpecLimits.MaxTotalTextLength"/>. Both stop the
    /// walk the instant the running total would exceed them, so a
    /// specification engineered to make either TRUE total astronomically
    /// large is rejected after doing only that many units of counting work,
    /// never after actually performing the astronomical amount of work the
    /// true total implies.
    /// </summary>
    private sealed class ContentWalkState
    {
        private int NodeCount { get; set; }

        /// <summary>
        /// The running character total, checked against
        /// <see cref="SpecLimits.MaxTotalTextLength"/> below. Used only by
        /// this class itself.
        /// </summary>
        private long CharacterCount { get; set; }

        public bool NodeLimitExceeded { get; private set; }

        public bool CharacterLimitExceeded { get; private set; }

        public bool TryVisitNode(ContentItemSpec item)
        {
            if (!TryVisit())
            {
                return false;
            }

            switch (item)
            {
                case PlainTextSpec plainText:
                    return TryAddCharacters(plainText.Text.Length) && TryAddStyleUri(plainText.Style);

                case HeadingSpec heading:
                    // BookmarkTitle is a second text-bearing member of this
                    // same node, not a separate position in the tree, so its
                    // length is added without a further TryVisit(); see the
                    // remark on this class for why every text-bearing member
                    // reachable from Content, not merely the two the cycle 7
                    // review named (PieSlice.Label and AltText), must be
                    // counted here. Round nine review: Style.LinkUri and
                    // Language are two further such members this walk missed
                    // before; see TryAddStyleUri's own remark.
                    return TryAddCharacters(heading.Text.Length) &&
                        TryAddCharacters(heading.BookmarkTitle?.Length ?? 0) &&
                        TryAddCharacters(heading.Language?.Length ?? 0) &&
                        TryAddStyleUri(heading.Style);

                case ParagraphSpec paragraph:
                    if (!TryAddCharacters(paragraph.Language?.Length ?? 0))
                    {
                        return false;
                    }

                    foreach (var run in paragraph.Runs)
                    {
                        // run.Style is never null: TextRunSpec's own
                        // construction rejects that (see its remark).
                        if (!TryVisit() || !TryAddCharacters(run.Text.Length) || !TryAddStyleUri(run.Style))
                        {
                            return false;
                        }
                    }

                    return true;

                case ListSpec list:
                    if (!TryAddStyleUri(list.DefaultStyle))
                    {
                        return false;
                    }

                    foreach (var listItem in list.Items)
                    {
                        if (!TryVisitListItem(listItem))
                        {
                            return false;
                        }
                    }

                    return true;

                case TableSpec table:
                    if (!TryAddStyleUri(table.DefaultCellStyle))
                    {
                        return false;
                    }

                    foreach (var row in table.Rows)
                    {
                        if (!TryVisit())
                        {
                            return false;
                        }

                        foreach (var cell in row.Cells)
                        {
                            if (!TryVisit() ||
                                !TryAddCharacters(cell.Content.Length) ||
                                !TryAddCharacters(cell.Language?.Length ?? 0) ||
                                !TryAddStyleUri(cell.Style))
                            {
                                return false;
                            }
                        }
                    }

                    return true;

                case PieChartSpec pieChart:
                    // AltText is this node's own text-bearing member, counted
                    // without a further TryVisit() for the same reason as
                    // HeadingSpec.BookmarkTitle above. Each slice IS already
                    // visited as its own node below; slice.Label is that
                    // node's own text and must be counted the same way every
                    // other node's text is, which the walk did not do before
                    // cycle 7: a document of charts with maximal-length slice
                    // labels reached 490,220,429 characters, 24.5 times
                    // MaxTotalTextLength, through this exact gap.
                    if (!TryAddCharacters(pieChart.AltText?.Length ?? 0))
                    {
                        return false;
                    }

                    foreach (var slice in pieChart.Slices)
                    {
                        if (!TryVisit() || !TryAddCharacters(slice.Label?.Length ?? 0))
                        {
                            return false;
                        }
                    }

                    return true;

                case ImageSpec image:
                    return TryAddCharacters(image.AltText?.Length ?? 0);

                default:
                    // LineSeparatorSpec: no text-bearing member.
                    return true;
            }
        }

        /// <summary>
        /// Adds <paramref name="style"/>'s own <see cref="TextStyleSpec.LinkUri"/>
        /// length, or nothing when <paramref name="style"/> or its
        /// <see cref="TextStyleSpec.LinkUri"/> is <see langword="null"/>.
        /// </summary>
        /// <remarks>
        /// Round nine review, Medium 3: <see cref="TextStyleSpec.LinkUri"/>,
        /// capped at <see cref="SpecLimits.MaxUriLength"/> (2,048) each, is
        /// reachable through <see cref="Content"/> at every position a
        /// <see cref="TextStyleSpec"/> can appear, and can therefore be
        /// multiplied by shared structure exactly as any other text-bearing
        /// member here can, but this walk never counted it. Measured
        /// directly: four paragraphs of 1,000 runs each, every run carrying a
        /// distinct maximal-length <see cref="TextStyleSpec.LinkUri"/>, counted
        /// only 4,000 characters (the runs' own <see cref="TextRunSpec.Text"/>)
        /// while the emitted C# carried 8,608,755 characters, entirely inside
        /// a total this walk was already supposed to bound.
        /// </remarks>
        private bool TryAddStyleUri(TextStyleSpec? style) => TryAddCharacters(style?.LinkUri?.Length ?? 0);

        private bool TryVisitListItem(ListItemSpec item)
        {
            // Language, like Style.LinkUri, is a text-bearing member of this
            // same node and reachable at every depth Children can multiply
            // it to; see the remark on TryAddStyleUri for the identical gap
            // this closes.
            if (!TryVisit() ||
                !TryAddCharacters(item.Text.Length) ||
                !TryAddCharacters(item.Language?.Length ?? 0) ||
                !TryAddStyleUri(item.Style))
            {
                return false;
            }

            foreach (var child in item.Children)
            {
                if (!TryVisitListItem(child))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryVisit()
        {
            if (++NodeCount <= SpecLimits.MaxWalkedNodes)
            {
                return true;
            }

            NodeLimitExceeded = true;
            return false;
        }

        private bool TryAddCharacters(int length)
        {
            CharacterCount += length;
            if (CharacterCount <= SpecLimits.MaxTotalTextLength)
            {
                return true;
            }

            CharacterLimitExceeded = true;
            return false;
        }
    }

    private static PdfAOutputIntentSpec Validated(PdfAOutputIntentSpec pdfA)
    {
        IccProfileHeader.Validate(pdfA.IccProfile, pdfA.ComponentCount);
        return pdfA;
    }

    /// <summary>
    /// Checks every <see cref="TextStyleSpec"/> reachable from <see cref="Content"/>,
    /// <see cref="Header"/>, <see cref="Footer"/> and <see cref="DefaultTextStyle"/>:
    /// whenever one's <see cref="FontSpec.Kind"/> is <see cref="FontKind.Embedded"/>,
    /// its <see cref="FontSpec.EmbeddedFontIndex"/> must name an actual entry
    /// of <see cref="EmbeddedFonts"/>. <see cref="Generation.SpecRenderer.Render"/>
    /// and <see cref="Generation.SpecCodeEmitter.Emit"/> both call this before
    /// doing anything else with a <see cref="DocumentSpec"/>, and it is the
    /// single place this check is stated.
    /// </summary>
    /// <remarks>
    /// This cannot be done at construction, unlike every other cross-property
    /// check in this type. Those checks (<see cref="OutputIntent"/> against a
    /// nested <see cref="PdfAOutputIntentSpec"/>'s own two properties,
    /// <see cref="Encryption"/> against a nested <see cref="EncryptionSpec"/>'s
    /// own two properties) validate a single ALREADY-FULLY-CONSTRUCTED nested
    /// object, which is safe regardless of the order its own properties were
    /// set in, because object-initializer syntax fully evaluates a nested
    /// <c>new PdfAOutputIntentSpec { ... }</c> expression before the result is
    /// ever assigned to <see cref="OutputIntent"/>. <see cref="EmbeddedFonts"/>,
    /// <see cref="Content"/>, <see cref="Header"/>, <see cref="Footer"/> and
    /// <see cref="DefaultTextStyle"/> are five independent top-level
    /// properties of THIS SAME record, with no such nesting relationship, and
    /// a caller may set them in any order inside <c>new DocumentSpec { ... }</c>:
    /// object-initializer member assignments run in the order written, so
    /// whichever of these five is assigned first would see the other four
    /// still at their construction-time defaults, not their final values.
    /// Measured directly: several samples in this repository set
    /// <see cref="DefaultTextStyle"/> to an embedded-font style before
    /// <see cref="EmbeddedFonts"/> itself, which is exactly the order that
    /// breaks a check made from <see cref="DefaultTextStyle"/>'s own <see langword="init"/>.
    /// Only after every property of a fully constructed <see cref="DocumentSpec"/>
    /// holds its final value can this run correctly regardless of the order
    /// its caller happened to write, which is why it runs once, explicitly,
    /// at the start of both real consumers instead.
    /// </remarks>
    public void ValidateEmbeddedFontReferences()
    {
        var embeddedFontCount = EmbeddedFonts.Count;

        ValidateFontReference(DefaultTextStyle, embeddedFontCount, nameof(DefaultTextStyle));
        ValidateBandFontReference(Header, embeddedFontCount, nameof(Header));
        ValidateBandFontReference(Footer, embeddedFontCount, nameof(Footer));

        foreach (var item in Content)
        {
            ValidateContentFontReferences(item, embeddedFontCount);
        }
    }

    private static void ValidateContentFontReferences(ContentItemSpec item, int embeddedFontCount)
    {
        switch (item)
        {
            case PlainTextSpec { Style: { } style }:
                ValidateFontReference(style, embeddedFontCount, nameof(Content));
                break;

            case HeadingSpec { Style: { } style }:
                ValidateFontReference(style, embeddedFontCount, nameof(Content));
                break;

            case ParagraphSpec paragraph:
                foreach (var run in paragraph.Runs)
                {
                    ValidateFontReference(run.Style, embeddedFontCount, nameof(Content));
                }

                break;

            case ListSpec list:
                if (list.DefaultStyle is { } listDefaultStyle)
                {
                    ValidateFontReference(listDefaultStyle, embeddedFontCount, nameof(Content));
                }

                foreach (var listItem in list.Items)
                {
                    ValidateListItemFontReferences(listItem, embeddedFontCount);
                }

                break;

            case TableSpec table:
                if (table.DefaultCellStyle is { } tableDefaultStyle)
                {
                    ValidateFontReference(tableDefaultStyle, embeddedFontCount, nameof(Content));
                }

                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        if (cell.Style is { } cellStyle)
                        {
                            ValidateFontReference(cellStyle, embeddedFontCount, nameof(Content));
                        }
                    }
                }

                break;
        }
    }

    private static void ValidateListItemFontReferences(ListItemSpec item, int embeddedFontCount)
    {
        if (item.Style is { } style)
        {
            ValidateFontReference(style, embeddedFontCount, nameof(Content));
        }

        foreach (var child in item.Children)
        {
            ValidateListItemFontReferences(child, embeddedFontCount);
        }
    }

    private static void ValidateFontReference(TextStyleSpec style, int embeddedFontCount, string paramName)
    {
        if (style.Font.Kind == FontKind.Embedded && style.Font.EmbeddedFontIndex >= embeddedFontCount)
        {
            throw new ArgumentException(
                $"{paramName} references FontSpec.FromEmbedded({style.Font.EmbeddedFontIndex}), but " +
                $"EmbeddedFonts has only {embeddedFontCount} " +
                (embeddedFontCount == 1 ? "entry." : "entries."),
                paramName);
        }
    }

    private static void ValidateBandFontReference(RunningBandSpec? value, int embeddedFontCount, string paramName)
    {
        if (value is not null)
        {
            ValidateFontReference(value.Style, embeddedFontCount, paramName);
        }
    }

    private static EncryptionSpec? ValidateEncryption(EncryptionSpec? value)
    {
        if (value is null || value.Permissions == PdfPermissions.All)
        {
            return value;
        }

        if (string.IsNullOrEmpty(value.OwnerPassword) || AuthenticatesIdentically(value.OwnerPassword, value.UserPassword))
        {
            throw new ArgumentException(
                "EncryptionSpec.OwnerPassword must be set to a value distinct from UserPassword whenever " +
                "Permissions restricts any permission; otherwise the displayed permission set binds nobody " +
                "who can open the file.",
                nameof(Encryption));
        }

        return value;
    }

    /// <summary>
    /// Whether the library's own AES-256 security handler would treat
    /// <paramref name="ownerPassword"/> and <paramref name="userPassword"/> as
    /// the SAME password. Comparing the two strings for exact equality is not
    /// enough: <c>VellumPdf.Encryption.StandardSecurityHandler.PasswordBytes</c>
    /// encodes a password as UTF-8 and truncates it to 127 bytes before
    /// deriving key material from it, so two strings that differ only after
    /// byte 127 authenticate identically even though a plain <c>==</c> on the
    /// strings says they differ. A 127-byte user password with a 128-byte
    /// owner password sharing the same
    /// first 127 bytes is therefore the SAME defeat of the owner-password rule
    /// as leaving <see cref="EncryptionSpec.OwnerPassword"/> unset, and must be
    /// rejected the same way.
    /// </summary>
    private static bool AuthenticatesIdentically(string? ownerPassword, string? userPassword) =>
        TruncatedPasswordBytes(ownerPassword).AsSpan().SequenceEqual(TruncatedPasswordBytes(userPassword));

    /// <summary>Mirrors <c>VellumPdf.Encryption.StandardSecurityHandler.PasswordBytes</c>: UTF-8, truncated to 127 bytes. A null password is the library's own empty-string fallback.</summary>
    private static byte[] TruncatedPasswordBytes(string? password)
    {
        var bytes = Encoding.UTF8.GetBytes(password ?? string.Empty);
        return bytes.Length > 127 ? bytes[..127] : bytes;
    }
}

/// <summary>
/// A page size expressed directly in PDF points. Both dimensions must be finite
/// and lie between <see cref="SpecLimits.MinPageDimensionPoints"/> and
/// <see cref="SpecLimits.MaxPageDimensionPoints"/>. NOTE: a page large enough
/// to construct is not necessarily a page the library can lay content out on.
/// A page too small to hold one line is refused by the library itself, with a
/// catchable exception; see the remark on
/// <see cref="SpecLimits.MinPageDimensionPoints"/>.
/// </summary>
public sealed record PageSizeSpec(double WidthPoints, double HeightPoints)
{
    public double WidthPoints
    {
        get;
        init => field = SpecLimits.ValidatePageDimension(value, nameof(WidthPoints));
    } = SpecLimits.ValidatePageDimension(WidthPoints, nameof(WidthPoints));

    public double HeightPoints
    {
        get;
        init => field = SpecLimits.ValidatePageDimension(value, nameof(HeightPoints));
    } = SpecLimits.ValidatePageDimension(HeightPoints, nameof(HeightPoints));

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
    /// <summary>
    /// Which of <see cref="FontKind"/>'s two named members this reference
    /// selects. Validated here, at construction, the same way
    /// <see cref="EmbeddedFontIndex"/> below rejects a negative value:
    /// <see cref="FontKind"/> is a public enumeration, and <see cref="FromStandard14"/>
    /// and <see cref="FromEmbedded"/> are convenience factories, not the only
    /// way to set this property. A caller may always write
    /// <c>new FontSpec { Kind = (FontKind)99 }</c> directly through the
    /// object-initializer syntax the two factories themselves use, so
    /// claiming this member unreachable outside the two factories was false;
    /// measured directly, the repository's own test suite constructs exactly
    /// that value. Rejecting it here, rather than downstream, is what lets
    /// both <see cref="Generation.SpecRenderer"/> and
    /// <see cref="Generation.SpecCodeEmitter"/> treat <see cref="FontKind.Embedded"/>
    /// against everything else as a two-way, exhaustive comparison with no
    /// unreachable arm to keep in step.
    /// </summary>
    public required FontKind Kind
    {
        get;
        init => field = SpecLimits.ValidateEnum(value, nameof(Kind));
    }

    /// <summary>
    /// Validated unconditionally, not only when <see cref="Kind"/> selects a
    /// standard face. The two are independent members of the same record and an
    /// object initializer may set them in either order, so an accessor that
    /// consulted <see cref="Kind"/> here would pass or fail by the order the
    /// caller happened to write.
    /// </summary>
    public Standard14 Standard14Face
    {
        get;
        init => field = SpecLimits.ValidateEnum(value, nameof(Standard14Face));
    }

    /// <summary>
    /// Rejected here when negative, which is meaningless regardless of how
    /// many embedded fonts a document ends up with. Whether it names an
    /// actual entry of <see cref="DocumentSpec.EmbeddedFonts"/> can only be
    /// checked once that list is known, so <see cref="DocumentSpec.Content"/>,
    /// <see cref="DocumentSpec.Header"/>, <see cref="DocumentSpec.Footer"/> and
    /// <see cref="DocumentSpec.DefaultTextStyle"/> each check it there.
    /// </summary>
    public int EmbeddedFontIndex
    {
        get;
        init => field = value >= 0
            ? value
            : throw new ArgumentException($"EmbeddedFontIndex must not be negative; got {value}.", nameof(EmbeddedFontIndex));
    }

    public static FontSpec FromStandard14(Standard14 face) =>
        new() { Kind = FontKind.Standard14, Standard14Face = face };

    public static FontSpec FromEmbedded(int embeddedFontIndex) =>
        new() { Kind = FontKind.Embedded, EmbeddedFontIndex = embeddedFontIndex };
}

/// <summary>Mirrors the settable members of the library's <c>TextStyle</c>.</summary>
/// <remarks>
/// Per plan section 3.4.0.1, every member of this record must implement value
/// equality. <see cref="Generation.SpecRenderer"/>'s style cache and
/// <see cref="Generation.SpecCodeEmitter"/>'s style hoisting both key on this
/// record's own equality, and they agree about when a style is shared only
/// because both keys behave identically. C# record equality falls back to
/// reference equality for any member whose type does not implement value
/// equality, so adding a member of such a type here (an array or list, for
/// instance) would silently make two value-equal styles compare unequal and
/// reintroduce the divergence between the renderer and the emitter that took
/// two review cycles to find and fix. If a future member cannot implement
/// value equality, give this record an explicit <c>Equals</c> and
/// <c>GetHashCode</c> covering it.
/// </remarks>
public sealed record TextStyleSpec
{
    public required FontSpec Font
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(Font));
    }

    /// <summary>
    /// Defaults to 12, matching <c>TextStyle</c>'s own default exactly, for the
    /// same reason given on <see cref="PieChartSpec.StartAngle"/>. Capped at
    /// <see cref="SpecLimits.MaxFontSize"/>, which is a sanity ceiling rather
    /// than a measured boundary: a larger font size fits fewer characters on a
    /// page, but the number of pages one element can demand is bounded by
    /// <see cref="SpecLimits.MaxTotalTextLength"/> and
    /// <see cref="SpecLimits.MaxWalkedNodes"/> whatever the font size is. See
    /// the remark on <see cref="SpecLimits.MaxFontSize"/>.
    /// </summary>
    public double FontSize
    {
        get;
        init => field = SpecLimits.ValidateFontSize(value, nameof(FontSize));
    } = 12;

    /// <summary>
    /// Capped at <see cref="SpecLimits.MaxLeadingPoints"/> when set, for the
    /// same reason as <see cref="FontSize"/>: a large leading enlarges every
    /// line exactly as a large font size does, and neither bounds the worst
    /// case. NOTE: leaving this unset does not mean a small line height. An
    /// unset leading reaches the library as a literal zero, which it reads as
    /// a request to compute a line height from the font, and that computed
    /// value may exceed this cap. See the remark on
    /// <see cref="SpecLimits.MaxLeadingPoints"/>.
    /// </summary>
    public double? Leading
    {
        get;
        init => field = SpecLimits.ValidateOptionalLeading(value, nameof(Leading));
    }

    public ColorRgb Color
    {
        get;
        init => field = SpecLimits.ValidateColor(value, nameof(Color));
    } = ColorRgb.Black;

    /// <summary>
    /// A URI a run of this style links to, or <see langword="null"/> for none.
    /// Restricted to the <c>http</c> and <c>https</c> schemes: this value ends
    /// up in a downloadable PDF's <c>/URI</c> action, and a <c>javascript:</c>
    /// or <c>data:</c> scheme is not a hyperlink there.
    /// </summary>
    /// <remarks>
    /// Cycle 6 left open, and cycle 7 closes: a C0 control character (0x00
    /// through 0x1F) or DEL (0x7F) embedded in an otherwise well-formed
    /// <c>http</c>/<c>https</c> URI. Measured directly: <see cref="Uri.TryCreate(string?, UriKind, out Uri?)"/>
    /// accepts every one tried (a NUL byte, a tab, an escape character, a
    /// bare CR and a bare LF among them) and reports <see cref="Uri.Scheme"/>
    /// unchanged, so the scheme check above does not see them; the STORED
    /// value is <paramref name="value"/> itself, not <see cref="Uri.AbsoluteUri"/>,
    /// so whatever percent-encoding <see cref="Uri"/> would apply on ITS OWN
    /// normalised form never actually reaches this property. No valid
    /// <c>http</c> or <c>https</c> URI, per RFC 3986, contains a literal
    /// control character at all: one is only ever expressed there
    /// percent-encoded. This rejects the raw byte outright, rather than
    /// silently percent-encoding it in place, consistently with every other
    /// validator in this file rejecting an out-of-bounds value instead of
    /// silently repairing it.
    /// </remarks>
    public string? LinkUri
    {
        get;
        init => field = value is null || (HasAllowedScheme(value) && HasNoControlCharacters(value))
            ? SpecLimits.ValidateOptionalString(value, SpecLimits.MaxUriLength, nameof(LinkUri))
            : throw new ArgumentException(
                HasAllowedScheme(value)
                    ? $"LinkUri must not contain a raw control character; got one in {value}."
                    : $"LinkUri must use the http or https scheme; got {value}.",
                nameof(LinkUri));
    }

    private static bool HasAllowedScheme(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) &&
        (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    private static bool HasNoControlCharacters(string uri) =>
        !uri.Any(static c => c <= '\u001F' || c == '\u007F');
}

/// <summary>One inline run of a <see cref="ParagraphSpec"/>, matching the library's <c>TextRun</c>.</summary>
/// <remarks>
/// Cycle 7 review: this was the one bare positional record anywhere in this
/// model, with neither member validated at all, which is exactly why
/// <see cref="SpecRenderer.Render"/>'s documented exception contract was
/// false. A <see cref="Style"/> of <see langword="null"/>, in particular,
/// passed <see cref="ParagraphSpec"/>'s own construction untouched (that
/// type's <c>ValidateRuns</c> checked only <see cref="Text"/>) and reached
/// <see cref="NullReferenceException"/> from several different places
/// downstream instead: <see cref="DocumentSpec.ValidateEmbeddedFontReferences"/>
/// dereferences <c>run.Style.Font</c> directly, and both
/// <see cref="SpecRenderer"/> and <see cref="Generation.SpecCodeEmitter"/>
/// dereference a run's <see cref="Style"/> while building the library's own
/// <c>TextRun</c> or the matching emitted expression. Both members are now
/// validated here, at THIS record's own construction, the same way every
/// other required member in this model is: <see cref="Style"/> can no
/// longer be <see langword="null"/> by the time a <see cref="TextRunSpec"/>
/// exists at all, which closes every one of those paths at once rather than
/// requiring each downstream dereference to be found and guarded
/// individually.
/// </remarks>
public sealed record TextRunSpec(string Text, TextStyleSpec Style)
{
    public string Text
    {
        get;
        init => field = SpecLimits.ValidateString(value, SpecLimits.MaxTextLength, nameof(Text));
    } = SpecLimits.ValidateString(Text, SpecLimits.MaxTextLength, nameof(Text));

    public TextStyleSpec Style
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(Style));
    } = Style ?? throw new ArgumentNullException(nameof(Style));
}

/// <summary>The base type for one item of ordered document content.</summary>
/// <remarks>
/// Declared <see langword="abstract"/> and public, so any assembly may write
/// a further subtype, but <see cref="DocumentSpec.Content"/>'s own
/// construction-time validation (<c>IsRecognisedContentItemType</c>) rejects
/// every value that is not one of the eight subtypes below (or is
/// <see langword="null"/>): a fully constructed <see cref="DocumentSpec"/> is
/// therefore guaranteed to hold only members of that closed set, and neither
/// <see cref="Generation.SpecRenderer"/> nor <see cref="Generation.SpecCodeEmitter"/>
/// needs a matching defensive arm of its own to stay in step with the other.
/// </remarks>
public abstract record ContentItemSpec;

/// <summary>A <c>Heading</c>. A <see langword="null"/> <see cref="Style"/> lets the library apply automatic styling for the level.</summary>
public sealed record HeadingSpec : ContentItemSpec
{
    public required string Text
    {
        get;
        init => field = SpecLimits.ValidateString(value, SpecLimits.MaxTextLength, nameof(Text));
    }

    /// <summary>
    /// Zero-based: 0 is top-level, matching the library's own <c>Heading.Level</c>
    /// convention. Capped at <see cref="SpecLimits.MaxHeadingLevel"/>: measured
    /// directly against the library, every level above that is silently
    /// clamped to the same PDF structure type an H6 heading gets, so rejecting
    /// an out-of-range level here is what keeps the displayed level and the
    /// rendered structure type from disagreeing.
    /// </summary>
    public required int Level
    {
        get;
        init => field = SpecLimits.ValidateHeadingLevel(value, nameof(Level));
    }

    public TextStyleSpec? Style { get; init; }
    public HorizontalAlignment Alignment
    {
        get;
        init => field = SpecLimits.ValidateEnum(value, nameof(Alignment));
    } = HorizontalAlignment.Left;

    public EdgeInsets? Margins
    {
        get;
        init => field = SpecLimits.ValidateOptionalEdgeInsets(value, nameof(Margins));
    }

    public string? BookmarkTitle
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(BookmarkTitle));
    }

    public string? Language
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxLanguageTagLength, nameof(Language));
    }
}

/// <summary>
/// A <c>Paragraph</c>. A single-entry <see cref="Runs"/> list is the common
/// case of one style throughout; more entries produce the mixed-style inline
/// runs the library's <c>Paragraph(IEnumerable&lt;TextRun&gt;)</c> constructor accepts.
/// </summary>
public sealed record ParagraphSpec : ContentItemSpec
{
    /// <summary>
    /// Per plan section 5.4, the list is capped at <see cref="SpecLimits.MaxParagraphRuns"/>
    /// and snapshotted with a collection expression at construction. Each
    /// run's own <see cref="TextRunSpec.Text"/> and <see cref="TextRunSpec.Style"/>
    /// are validated by <see cref="TextRunSpec"/> itself, at its own
    /// construction, so nothing further needs checking about an individual
    /// run here; see the remark on <see cref="TextRunSpec"/>.
    /// </summary>
    public required IReadOnlyList<TextRunSpec> Runs
    {
        get;
        init => field = ValidateRuns(value);
    }

    public HorizontalAlignment Alignment
    {
        get;
        init => field = SpecLimits.ValidateEnum(value, nameof(Alignment));
    } = HorizontalAlignment.Left;

    public EdgeInsets? Margins
    {
        get;
        init => field = SpecLimits.ValidateOptionalEdgeInsets(value, nameof(Margins));
    }

    public string? Language
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxLanguageTagLength, nameof(Language));
    }

    public static ParagraphSpec FromText(string text, TextStyleSpec style) =>
        new() { Runs = [new TextRunSpec(text, style)] };

    private static IReadOnlyList<TextRunSpec> ValidateRuns(IReadOnlyList<TextRunSpec> value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(Runs));

        if (value.Count > SpecLimits.MaxParagraphRuns)
        {
            throw new ArgumentException($"A paragraph must not have more than {SpecLimits.MaxParagraphRuns} runs; got {value.Count}.", nameof(Runs));
        }

        return [.. value];
    }
}

/// <summary>
/// A plain string added through the library's <c>Document.Add(string, TextStyle?)</c>
/// overload: the last of that method's overloads this model had not already
/// covered when this type was added. Plan section 3.1 lists two further
/// members that remain unexpressed, for different reasons: <c>Document.Add(IRenderer)</c>,
/// a developer-supplied drawing hook with nothing for a structured form to
/// represent, and <c>Document.TextEncodingWarnings</c>, which is get-only and
/// so was never a candidate to begin with. <c>Document.UseObjectStreams</c>,
/// once also on that list, is now expressed directly as
/// <see cref="DocumentSpec.UseObjectStreams"/>.
/// </summary>
/// <remarks>
/// When <see cref="Style"/> is <see langword="null"/>, the rendered text uses
/// whatever style <see cref="DocumentSpec.DefaultTextStyle"/> registered
/// through <c>Document.SetDefaultFont</c>. <see cref="HeadingSpec"/> and
/// <see cref="ParagraphSpec"/> resolve their own fallback directly instead and
/// never consult that value; see the remark on <see cref="DocumentSpec.DefaultTextStyle"/>
/// for the two content items that are neither.
/// </remarks>
public sealed record PlainTextSpec : ContentItemSpec
{
    public required string Text
    {
        get;
        init => field = SpecLimits.ValidateString(value, SpecLimits.MaxTextLength, nameof(Text));
    }

    public TextStyleSpec? Style { get; init; }
}

/// <summary>A <c>ListElement</c>, unordered or one of the three ordered forms.</summary>
public sealed record ListSpec : ContentItemSpec
{
    public required ListStyle Style
    {
        get;
        init => field = SpecLimits.ValidateEnum(value, nameof(Style));
    }

    /// <summary>
    /// Capped at <see cref="SpecLimits.MaxListItems"/> and snapshotted with a
    /// collection expression at construction, per plan section 5.4.
    /// </summary>
    public required IReadOnlyList<ListItemSpec> Items
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(Items));

            if (value.Count > SpecLimits.MaxListItems)
            {
                throw new ArgumentException($"A list must not have more than {SpecLimits.MaxListItems} items; got {value.Count}.", nameof(Items));
            }

            field = [.. value];
        }
    }

    public double? Indent
    {
        get;
        init => field = value is null ? null : SpecLimits.ValidateRange(value.Value, 0, SpecLimits.MaxIndentPoints, nameof(Indent));
    }

    public EdgeInsets? Margins
    {
        get;
        init => field = SpecLimits.ValidateOptionalEdgeInsets(value, nameof(Margins));
    }

    public TextStyleSpec? DefaultStyle { get; init; }
}

/// <summary>One <c>ListItem</c>. Nesting is expressed through <see cref="Children"/>, matching <c>ListItem.AddChild</c>.</summary>
public sealed record ListItemSpec
{
    public required string Text
    {
        get;
        init => field = SpecLimits.ValidateString(value, SpecLimits.MaxTextLength, nameof(Text));
    }

    public TextStyleSpec? Style { get; init; }

    public string? Language
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxLanguageTagLength, nameof(Language));
    }

    /// <summary>
    /// Snapshotted with a collection expression at construction, per plan
    /// section 5.4. Capped in two independent dimensions: breadth, at
    /// <see cref="SpecLimits.MaxListItemChildren"/> direct children, and
    /// depth, at <see cref="SpecLimits.MaxListNestingDepth"/> levels reachable
    /// through this item. Unbounded nesting recurses without a bound in both
    /// the renderer and the emitter and overflows the CLR stack; a stack
    /// overflow cannot be caught, so the depth cap is the one control in
    /// section 5.4 that a wrapped parser call cannot rescue.
    /// </summary>
    public IReadOnlyList<ListItemSpec> Children
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(Children));

            if (value.Count > SpecLimits.MaxListItemChildren)
            {
                throw new ArgumentException(
                    $"A list item must not have more than {SpecLimits.MaxListItemChildren} children; got {value.Count}.",
                    nameof(Children));
            }

            var snapshot = value.Count == 0 ? (IReadOnlyList<ListItemSpec>)[] : [.. value];
            var depth = snapshot.Count == 0 ? 1 : 1 + snapshot.Max(child => child.Depth);

            if (depth > SpecLimits.MaxListNestingDepth)
            {
                throw new ArgumentException(
                    $"List nesting must not exceed {SpecLimits.MaxListNestingDepth} levels; this item would reach depth {depth}.",
                    nameof(Children));
            }

            field = snapshot;
            Depth = depth;
        }
    } = [];

    /// <summary>The greatest nesting depth reachable from this item, counting itself as depth 1. Not part of the library's own shape; used only to enforce <see cref="SpecLimits.MaxListNestingDepth"/> as each level is constructed.</summary>
    private int Depth { get; set; } = 1;
}

/// <summary>A <c>TableElement</c>.</summary>
public sealed record TableSpec : ContentItemSpec
{
    /// <summary>
    /// Per plan section 5.4, capped at <see cref="SpecLimits.MaxTableRows"/>
    /// and snapshotted with a collection expression at construction.
    /// </summary>
    public required IReadOnlyList<TableRowSpec> Rows
    {
        get;
        init => field = ValidateRows(value);
    }

    /// <summary>Capped at <see cref="SpecLimits.MaxTableColumnWidths"/> entries and snapshotted at construction, per plan section 5.4.</summary>
    public IReadOnlyList<double>? ColumnWidths
    {
        get;
        init => field = ValidateColumnWidths(value);
    }

    public TextStyleSpec? DefaultCellStyle { get; init; }

    public double? BorderWidth
    {
        get;
        init => field = value is null ? null : SpecLimits.ValidateRange(value.Value, 0, SpecLimits.MaxStrokeWidthPoints, nameof(BorderWidth));
    }

    public ColorRgb? BorderColor
    {
        get;
        init => field = SpecLimits.ValidateOptionalColor(value, nameof(BorderColor));
    }

    public EdgeInsets? Margins
    {
        get;
        init => field = SpecLimits.ValidateOptionalEdgeInsets(value, nameof(Margins));
    }

    private static IReadOnlyList<double>? ValidateColumnWidths(IReadOnlyList<double>? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Count > SpecLimits.MaxTableColumnWidths)
        {
            throw new ArgumentException(
                $"A table must not have more than {SpecLimits.MaxTableColumnWidths} column widths; got {value.Count}.",
                nameof(ColumnWidths));
        }

        foreach (var width in value)
        {
            SpecLimits.ValidateRange(width, 0, SpecLimits.MaxPageDimensionPoints, nameof(ColumnWidths));
        }

        return [.. value];
    }

    private static IReadOnlyList<TableRowSpec> ValidateRows(IReadOnlyList<TableRowSpec> value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(Rows));

        if (value.Count == 0)
        {
            throw new ArgumentException("A table must have at least one row.", nameof(Rows));
        }

        if (value.Count > SpecLimits.MaxTableRows)
        {
            throw new ArgumentException($"A table must not have more than {SpecLimits.MaxTableRows} rows; got {value.Count}.", nameof(Rows));
        }

        return [.. value];
    }
}

/// <summary>
/// One <c>Row</c> of a <see cref="TableSpec"/>. The library declares
/// <c>Row.Background</c> as an ordinary settable property with a public
/// parameterless constructor, so <c>new Row { Background = ..., IsHeader = true }</c>
/// compiles on its own. The obstacle is containment, not initialisation:
/// a <c>Row</c> reaches a <c>TableElement</c> only through
/// <c>TableElement.AddRow</c> or <c>AddHeaderRow</c>, both of which construct
/// and return their own instance, and <c>TableElement.Rows</c> is a get-only
/// list with no <c>AddRow(Row)</c> overload to attach a caller-built one. No
/// external caller can therefore get a <c>Row</c> it built itself into a
/// table. Per-row background is thus not modelled here; per-cell background,
/// set on a <see cref="TableCellSpec"/> the caller constructs directly, is
/// unaffected.
/// </summary>
public sealed record TableRowSpec
{
    /// <summary>
    /// Per plan section 5.4, capped at <see cref="SpecLimits.MaxTableCellsPerRow"/>
    /// and snapshotted with a collection expression at construction.
    /// </summary>
    public required IReadOnlyList<TableCellSpec> Cells
    {
        get;
        init => field = ValidateCells(value);
    }

    public bool IsHeader { get; init; }

    private static IReadOnlyList<TableCellSpec> ValidateCells(IReadOnlyList<TableCellSpec> value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(Cells));

        if (value.Count == 0)
        {
            throw new ArgumentException("A table row must have at least one cell.", nameof(Cells));
        }

        if (value.Count > SpecLimits.MaxTableCellsPerRow)
        {
            throw new ArgumentException(
                $"A table row must not have more than {SpecLimits.MaxTableCellsPerRow} cells; got {value.Count}.",
                nameof(Cells));
        }

        return [.. value];
    }
}

/// <summary>One <c>Cell</c> of a <see cref="TableRowSpec"/>. Content is plain text; the library does not support nested elements in a cell.</summary>
public sealed record TableCellSpec
{
    public required string Content
    {
        get;
        init => field = SpecLimits.ValidateString(value, SpecLimits.MaxTextLength, nameof(Content));
    }

    /// <summary>Capped at <see cref="SpecLimits.MaxTableCellsPerRow"/>: a cell cannot usefully span more columns than a row may ever have.</summary>
    public int ColSpan
    {
        get;
        init => field = value is >= 1 and <= SpecLimits.MaxTableCellsPerRow
            ? value
            : throw new ArgumentException($"ColSpan must be between 1 and {SpecLimits.MaxTableCellsPerRow}; got {value}.", nameof(ColSpan));
    } = 1;

    /// <summary>Capped at <see cref="SpecLimits.MaxTableRows"/>: a cell cannot usefully span more rows than a table may ever have.</summary>
    public int RowSpan
    {
        get;
        init => field = value is >= 1 and <= SpecLimits.MaxTableRows
            ? value
            : throw new ArgumentException($"RowSpan must be between 1 and {SpecLimits.MaxTableRows}; got {value}.", nameof(RowSpan));
    } = 1;

    public TextStyleSpec? Style { get; init; }

    public EdgeInsets? Padding
    {
        get;
        init => field = SpecLimits.ValidateOptionalEdgeInsets(value, nameof(Padding));
    }

    public ColorRgb? Background
    {
        get;
        init => field = SpecLimits.ValidateOptionalColor(value, nameof(Background));
    }

    public HorizontalAlignment Alignment
    {
        get;
        init => field = SpecLimits.ValidateEnum(value, nameof(Alignment));
    } = HorizontalAlignment.Left;

    public string? Language
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxLanguageTagLength, nameof(Language));
    }
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
/// <remarks>
/// Per plan section 5.4, <see cref="Bytes"/> is capped at
/// <see cref="SpecLimits.MaxAssetBytes"/> and defensively copied here
/// (<see cref="SpecLimits.ValidateAssetBytes"/> returns a clone), so mutating
/// the caller's own array afterward cannot change what a fully constructed
/// record holds; whether it actually matches <see cref="Format"/>'s magic
/// bytes is checked by <see cref="DocumentSpec.Content"/>, which is the only
/// property that ever sees both this record's properties fully set, and
/// which reads this already-copied array rather than the caller's.
/// </remarks>
public sealed record ImageSpec : ContentItemSpec
{
    public required ImageFormat Format
    {
        get;
        init => field = SpecLimits.ValidateEnum(value, nameof(Format));
    }

    public required byte[] Bytes
    {
        get;
        init => field = SpecLimits.ValidateAssetBytes(value, nameof(Bytes));
    }

    public double? Width
    {
        get;
        init => field = value is null ? null : SpecLimits.ValidateRange(value.Value, 0, SpecLimits.MaxImageDimensionPoints, nameof(Width));
    }

    public double? Height
    {
        get;
        init => field = value is null ? null : SpecLimits.ValidateRange(value.Value, 0, SpecLimits.MaxImageDimensionPoints, nameof(Height));
    }

    public HorizontalAlignment Alignment
    {
        get;
        init => field = SpecLimits.ValidateEnum(value, nameof(Alignment));
    } = HorizontalAlignment.Left;

    public EdgeInsets? Margins
    {
        get;
        init => field = SpecLimits.ValidateOptionalEdgeInsets(value, nameof(Margins));
    }

    public string? AltText
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(AltText));
    }
}

/// <summary>A <c>PieChart</c>, using the library's own <c>PieSlice</c> value for each slice.</summary>
public sealed record PieChartSpec : ContentItemSpec
{
    /// <summary>
    /// Per plan section 5.4, capped at <see cref="SpecLimits.MaxChartSlices"/>
    /// and snapshotted with a collection expression at construction.
    /// </summary>
    public required IReadOnlyList<PieSlice> Slices
    {
        get;
        init => field = ValidateSlices(value);
    }

    public required double Diameter
    {
        get;
        init => field = value is > 0 and <= SpecLimits.MaxPieChartDiameterPoints
            ? value
            : throw new ArgumentException(
                $"Diameter must be greater than zero and no more than {SpecLimits.MaxPieChartDiameterPoints}; got {value}.",
                nameof(Diameter));
    }

    public EdgeInsets? Margins
    {
        get;
        init => field = SpecLimits.ValidateOptionalEdgeInsets(value, nameof(Margins));
    }

    public ColorRgb? StrokeColor
    {
        get;
        init => field = SpecLimits.ValidateOptionalColor(value, nameof(StrokeColor));
    }

    public double StrokeWidth
    {
        get;
        init => field = SpecLimits.ValidateRange(value, 0, SpecLimits.MaxStrokeWidthPoints, nameof(StrokeWidth));
    } = 0.5;

    public HorizontalAlignment Alignment
    {
        get;
        init => field = SpecLimits.ValidateEnum(value, nameof(Alignment));
    } = HorizontalAlignment.Center;

    /// <summary>
    /// Defaults to <c>π/2</c> (12 o'clock), matching <c>PieChart</c>'s own
    /// default exactly. Every default in this record matches the library's so
    /// that <see cref="Generation.SpecCodeEmitter"/> can safely omit an
    /// unset property from the code it emits, relying on the library to apply
    /// the identical default that <see cref="Generation.SpecRenderer"/> set explicitly.
    /// Capped at <see cref="SpecLimits.MaxAngleMagnitudeRadians"/> in magnitude,
    /// which exists only to reject a non-finite or wildly out-of-range value.
    /// </summary>
    public double StartAngle
    {
        get;
        init => field = SpecLimits.ValidateRange(value, -SpecLimits.MaxAngleMagnitudeRadians, SpecLimits.MaxAngleMagnitudeRadians, nameof(StartAngle));
    } = double.Pi / 2;

    public bool Clockwise { get; init; } = true;

    public string? AltText
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(AltText));
    }

    public bool Decorative { get; init; }

    private static IReadOnlyList<PieSlice> ValidateSlices(IReadOnlyList<PieSlice> value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(Slices));

        if (value.Count == 0)
        {
            throw new ArgumentException("A pie chart must have at least one slice.", nameof(Slices));
        }

        if (value.Count > SpecLimits.MaxChartSlices)
        {
            throw new ArgumentException($"A pie chart must not have more than {SpecLimits.MaxChartSlices} slices; got {value.Count}.", nameof(Slices));
        }

        foreach (var slice in value)
        {
            // Matches the library's own contract on PieSlice.Value ("must be
            // finite and non-negative"), checked here rather than left to
            // PieChart's own construction, since PieSlice is the library's
            // type and offers nowhere for this model to hook in.
            SpecLimits.ValidateRange(slice.Value, 0, double.MaxValue, nameof(Slices));
            SpecLimits.ValidateColor(slice.Color, nameof(Slices));

            if (slice.Label is not null)
            {
                SpecLimits.ValidateString(slice.Label, SpecLimits.MaxTextLength, nameof(Slices));
            }
        }

        return [.. value];
    }
}

/// <summary>A <c>LineSeparator</c>, the library's only vector primitive in the Layout API.</summary>
/// <remarks>
/// NOTE: measured directly against the library, <see cref="Margins"/>'s
/// <c>Left</c> and <c>Right</c> components have no effect on the rendered or
/// saved bytes: the drawn line always spans the full content width regardless
/// of either value, in every combination tried (sole content item, followed
/// by further content, larger or smaller than the vertical components). Only
/// <c>Top</c> is always observable, and <c>Bottom</c> only when a following
/// content item exists to be pushed down by it. This is a genuine library
/// behaviour, not a gap in this model or in <see cref="Generation.SpecRenderer"/>
/// or <see cref="Generation.SpecCodeEmitter"/>: a round-trip sample cannot make
/// the horizontal components observable by construction, since nothing in the
/// output depends on them, and the showcase samples are written accordingly.
/// </remarks>
public sealed record LineSeparatorSpec : ContentItemSpec
{
    public double LineWidth
    {
        get;
        init => field = SpecLimits.ValidateRange(value, 0, SpecLimits.MaxStrokeWidthPoints, nameof(LineWidth));
    } = 1;

    public ColorRgb Color
    {
        get;
        init => field = SpecLimits.ValidateColor(value, nameof(Color));
    } = ColorRgb.Black;

    public EdgeInsets? Margins
    {
        get;
        init => field = SpecLimits.ValidateOptionalEdgeInsets(value, nameof(Margins));
    }
}

/// <summary>
/// A running header or footer. The template is one string substituting
/// <c>{page}</c> and <c>{pages}</c>; the library accepts nothing richer here.
/// </summary>
public sealed record RunningBandSpec
{
    /// <remarks>
    /// Capped at <see cref="SpecLimits.MaxRunningBandTemplateLength"/>, NOT
    /// <see cref="SpecLimits.MaxTextLength"/>: a band is laid out once per
    /// page, so its template length is multiplied by page count in a way no
    /// other single top-level string in this model is. See the remark on
    /// <see cref="SpecLimits.MaxRunningBandTemplateLength"/> for the
    /// measurement behind the value.
    /// </remarks>
    public required string Template
    {
        get;
        init => field = SpecLimits.ValidateString(value, SpecLimits.MaxRunningBandTemplateLength, nameof(Template));
    }

    public required TextStyleSpec Style
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(Style));
    }

    public HorizontalAlignment Alignment
    {
        get;
        init => field = SpecLimits.ValidateEnum(value, nameof(Alignment));
    } = HorizontalAlignment.Center;

    public double? Height
    {
        get;
        init => field = value is null ? null : SpecLimits.ValidateRange(value.Value, 0, SpecLimits.MaxEdgeInsetPoints, nameof(Height));
    }
}

/// <summary>Entries for the document's <c>PdfDocumentInfo</c>.</summary>
public sealed record DocumentMetadataSpec
{
    public string? Title
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(Title));
    }

    public string? Author
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(Author));
    }

    public string? Subject
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(Subject));
    }

    public string? Keywords
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(Keywords));
    }

    public string? Creator
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(Creator));
    }

    public string? Producer
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(Producer));
    }
}

/// <summary>The base type for the two output intents <c>Document</c> can embed.</summary>
public abstract record OutputIntentSpec;

/// <summary>
/// An ICC-based output intent for a PDF/A or PDF/UA claim, matching
/// <c>Document.SetPdfAOutputIntent</c>.
/// </summary>
/// <remarks>
/// Per plan section 5.4, <see cref="IccProfile"/> is capped at
/// <see cref="SpecLimits.MaxAssetBytes"/> and defensively copied here
/// (<see cref="SpecLimits.ValidateAssetBytes"/> returns a clone), so mutating
/// the caller's own array afterward cannot change what a fully constructed
/// record holds; whether its header is internally consistent with
/// <see cref="ComponentCount"/> is checked by <see cref="DocumentSpec.OutputIntent"/>,
/// which is the only property that ever sees both this record's properties
/// fully set, and which reads this already-copied array rather than the
/// caller's.
/// </remarks>
public sealed record PdfAOutputIntentSpec : OutputIntentSpec
{
    public required byte[] IccProfile
    {
        get;
        init => field = SpecLimits.ValidateAssetBytes(value, nameof(IccProfile));
    }

    public required int ComponentCount
    {
        get;
        init => field = SpecLimits.ValidateIccComponentCount(value, nameof(ComponentCount));
    }

    public required string OutputConditionIdentifier
    {
        get;
        init => field = SpecLimits.ValidateString(value, SpecLimits.MaxTextLength, nameof(OutputConditionIdentifier));
    }

    public string? Info
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(Info));
    }
}

/// <summary>
/// A device CMYK output intent, matching <c>Document.UseCmykOutputIntent</c>.
/// Measured directly against the library: the call writes nothing into the
/// saved bytes unless <see cref="DocumentSpec.Conformance"/> is set to
/// something other than <see cref="DocumentConformance.None"/>. A sample
/// using this type without a conformance claim would round-trip correctly
/// while exercising nothing.
/// </summary>
public sealed record CmykOutputIntentSpec : OutputIntentSpec
{
    public required string OutputConditionIdentifier
    {
        get;
        init => field = SpecLimits.ValidateString(value, SpecLimits.MaxTextLength, nameof(OutputConditionIdentifier));
    }
}

/// <summary>
/// Encryption settings, matching <c>PdfEncryptionSettings</c>.
/// </summary>
/// <remarks>
/// Per plan section 5.4: whether <see cref="OwnerPassword"/> may be
/// left unset depends on <see cref="Permissions"/>, so that rule is enforced
/// by <see cref="DocumentSpec.Encryption"/>, the only property that ever sees
/// both fully set. Measured directly against the library: with
/// <see cref="OwnerPassword"/> unset, the password that actually authenticates
/// full (owner) access is <see cref="UserPassword"/>, so a restricted
/// <see cref="Permissions"/> value would otherwise bind nobody who can open
/// the file.
/// </remarks>
public sealed record EncryptionSpec
{
    public string? UserPassword
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(UserPassword));
    }

    public string? OwnerPassword
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(OwnerPassword));
    }

    /// <summary>
    /// Capped to a union of the library's own named flags. See the remark on
    /// <see cref="SpecLimits.ValidatePermissions"/>: an undefined bit here
    /// would make <c>SpecCodeEmitter.EmitPermissions</c> either produce
    /// invalid C# or emit code that grants fewer permissions than the
    /// rendered document actually carries.
    /// </summary>
    public PdfPermissions Permissions
    {
        get;
        init => field = SpecLimits.ValidatePermissions(value, nameof(Permissions));
    } = PdfPermissions.All;

    public bool EncryptMetadata { get; init; } = true;
}
