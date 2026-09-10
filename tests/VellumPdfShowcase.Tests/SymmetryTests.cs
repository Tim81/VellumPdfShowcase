using System.Reflection;
using VellumPdf.Encryption;
using VellumPdf.Layout.Elements;
using VellumPdfShowcase.Web.Generation;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// The product's invariant is that
/// <see cref="SpecRenderer.Render"/> and <see cref="SpecCodeEmitter.Emit"/>
/// cannot disagree about a <see cref="DocumentSpec"/>. That invariant had been
/// approximated by branch coverage, and the approximation actively caused
/// defects: pressure toward 100% coverage deleted defensive arms from
/// <see cref="SpecRenderer"/> while <see cref="SpecCodeEmitter"/>'s
/// equivalents survived because some test happened to reach them, producing
/// two live divergences (the ContentItemSpec divergence: an unrecognised
/// <see cref="ContentItemSpec"/> rendered as though absent while
/// <see cref="SpecCodeEmitter"/> threw; the FontKind divergence: an
/// out-of-range <see cref="FontKind"/> rendered as Helvetica while
/// <see cref="SpecCodeEmitter"/> threw) with the branch-coverage gate fully
/// green throughout. This file asserts the invariant DIRECTLY, over a corpus
/// of every existing <see cref="DocumentSpecSamples"/> entry plus adversarial
/// specifications that pass construction, rather than relying on coverage as
/// a proxy for it. It is the PRIMARY guard for this property; the
/// branch-coverage gate remains a secondary check that every line is
/// reachable, not that the two consumers agree about what it produces.
/// </summary>
/// <remarks>
/// The invariant asserted here is narrower than "both succeed or both
/// reject", and deliberately so. <see cref="SpecCodeEmitter.Emit"/>'s own
/// documented contract (see its remark) promises it never throws
/// <see cref="InvalidOperationException"/>: it emits TEXT referencing
/// <see cref="DocumentSpec"/>'s own asset bytes and other values by position,
/// and never decodes, parses, or executes any of them, so a specification
/// that constructs cleanly but trips a library-level refusal only
/// <see cref="SpecRenderer.Render"/> can discover (a well-signed but
/// structurally malformed image or font, an output intent the library itself
/// rejects, <see cref="DocumentSpec.UseObjectStreams"/> combined with
/// <see cref="DocumentSpec.Encryption"/>, a PDF/A <see cref="DocumentSpec.Conformance"/>
/// claim combined with <see cref="DocumentSpec.Encryption"/>) is EXPECTED to
/// make <see cref="SpecRenderer.Render"/> throw <see cref="InvalidOperationException"/>
/// while <see cref="SpecCodeEmitter.Emit"/> succeeds. That asymmetry is
/// documented and deliberate, proven directly by
/// <see cref="SpecRendererErrorHandlingTests"/> and
/// <see cref="SpecRendererOutputIntentErrorHandlingTests"/>, not by this
/// guard.
/// <para>
/// What this guard DOES assert, unconditionally, over every specification in
/// its corpus: if EITHER consumer rejects a specification with an
/// <see cref="ArgumentException"/>-family exception ("a malformed spec the
/// MODEL failed to reject", per both methods' own documented contract), the
/// OTHER must reject it too. A specification that constructs successfully is,
/// by both contracts, supposed to be well-formed enough that neither
/// consumer's own per-content-type dispatch can be surprised by it; an
/// ArgumentException from one side and silent success (or an unrelated
/// exception) from the other means one consumer's switch tolerates a shape
/// the other's cannot handle, which is EXACTLY the shape of the two
/// divergences above. This is asserted by exception FAMILY (does it derive from
/// ArgumentException), not exact subtype: both contracts only promise the
/// family, and the two consumers are not required to throw the identical
/// ArgumentException subtype for the identical reason.
/// </para>
/// <para>
/// PROOF this guard actually protects the property, rather than passing
/// vacuously: with <c>DocumentSpec.IsRecognisedContentItemType</c>'s call
/// site in <c>ValidateContent</c> commented out (reintroducing the
/// ContentItemSpec divergence) and, separately, with <c>FontSpec.Kind</c>'s
/// validation reverted to a bare auto-property (reintroducing the FontKind
/// divergence), each of the two shapes below
/// constructs successfully again, and feeding the result to
/// <see cref="RenderAndEmitAgreeOnArgumentRejection"/> fails immediately,
/// naming the exact disagreement (Render succeeds silently; Emit throws
/// ArgumentOutOfRangeException). Both reproductions were run by hand during
/// this fix and are not committed as failing tests, since the fix they
/// prove is already in place; <see cref="UnrecognisedContentItem_CannotBeConstructed_SoRenderAndEmitCannotDisagreeAboutIt"/>
/// and <see cref="OutOfRangeFontKind_CannotBeConstructed_SoRenderAndEmitCannotDisagreeAboutIt"/>
/// below are the permanent regression guard: as long as construction itself
/// rejects both shapes, neither can ever reach <see cref="SpecRenderer.Render"/>
/// or <see cref="SpecCodeEmitter.Emit"/> for the two to disagree about.
/// </para>
/// </remarks>
public class SymmetryTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    /// <summary>
    /// Runs <paramref name="action"/>, reporting whether it threw and, if so,
    /// what it threw, without letting the exception propagate.
    /// </summary>
    private static (bool Threw, Exception? Exception) TryRun(Action action)
    {
        try
        {
            action();
            return (false, null);
        }
        catch (Exception ex)
        {
            return (true, ex);
        }
    }

    /// <summary>
    /// The primary assertion this file exists to make. See the class remark
    /// for exactly what is, and is not, required to agree.
    /// </summary>
    internal static void RenderAndEmitAgreeOnArgumentRejection(DocumentSpec spec)
    {
        var (renderThrew, renderException) = TryRun(() => SpecRenderer.Render(spec));
        var (emitThrew, emitException) = TryRun(() => SpecCodeEmitter.Emit(spec));

        var violation = AgreementViolation(renderThrew, renderException, emitThrew, emitException);

        Assert.True(violation is null, violation);
    }

    /// <summary>
    /// The decision this file exists to make, extracted from the assertion so
    /// that it can be driven directly with all four combinations of outcomes.
    /// Returns <see langword="null"/> when the two consumers agree, and the
    /// failure message otherwise.
    /// </summary>
    /// <remarks>
    /// Extracted for the reason <see cref="SpecRoundTripTests"/> extracted its
    /// own heading-hierarchy rule. Every specification in this file's corpus
    /// makes the comparison below evaluate <c>false == false</c>: the samples
    /// all succeed on both sides, and every adversarial specification is one of
    /// the documented asymmetries, where Render throws
    /// <see cref="InvalidOperationException"/>, which is not an
    /// <see cref="ArgumentException"/> and so is not a rejection AS MALFORMED
    /// on either side. A guard whose central comparison has only ever seen one
    /// pair of inputs is not known to discriminate. The corpus now carries
    /// specifications both consumers reject, and
    /// <see cref="SymmetryGuardRuleTests"/> drives this method with the two
    /// disagreeing combinations directly.
    /// </remarks>
    internal static string? AgreementViolation(bool renderThrew, Exception? renderException, bool emitThrew, Exception? emitException)
    {
        var renderRejectedAsMalformed = renderThrew && renderException is ArgumentException;
        var emitRejectedAsMalformed = emitThrew && emitException is ArgumentException;

        if (renderRejectedAsMalformed != emitRejectedAsMalformed)
        {
            return "SpecRenderer.Render and SpecCodeEmitter.Emit disagree about whether this specification is " +
                $"malformed. Render {Describe(renderThrew, renderException)}; Emit {Describe(emitThrew, emitException)}. " +
                "A specification the model let through construction must not look well-formed to one consumer's " +
                "own dispatch and malformed to the other's.";
        }

        // Emit's own documented contract: it may throw ArgumentException, but
        // never InvalidOperationException, since it never executes anything.
        if (emitThrew && emitException is InvalidOperationException)
        {
            return $"SpecCodeEmitter.Emit threw InvalidOperationException ({emitException.Message}), which its own " +
                "documented exception contract promises never happens.";
        }

        return null;
    }

    private static string Describe(bool threw, Exception? exception) =>
        threw ? $"threw {exception!.GetType().Name}: {exception.Message}" : "succeeded";

    /// <summary>
    /// Every public, parameterless, <see cref="DocumentSpec"/>-returning
    /// factory on <see cref="DocumentSpecSamples"/>, the same reflection
    /// <see cref="SpecRoundTripTests"/> uses, reused here rather than
    /// duplicated so a new sample is automatically added to this corpus too.
    /// </summary>
    public static TheoryData<string> AllSampleNames() => SampleCorpus.AllSampleNames();

    [Theory]
    [MemberData(nameof(AllSampleNames))]
    public void Sample_RenderAndEmitAgree(string sampleName)
    {
        RenderAndEmitAgreeOnArgumentRejection(SampleCorpus.Invoke(sampleName));
    }

    /// <summary>
    /// Adversarial specifications: each constructs successfully (passes
    /// every model-level check) but is chosen specifically to be a shape a
    /// per-content-type or per-feature dispatch could plausibly handle
    /// differently on the two sides. Named rather than anonymous, so a
    /// failure names the scenario directly.
    /// </summary>
    public static TheoryData<string> AdversarialSpecNames()
    {
        TheoryData<string> names = [];
        foreach (var name in AdversarialSpecificationNames())
        {
            names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// The fixed roster of every adversarial specification factory this file
    /// declares, named explicitly with <see langword="nameof"/> rather than
    /// derived by scanning for a name suffix. The corpus was previously
    /// discovered by a rule with no floor: any static, private,
    /// <see cref="DocumentSpec"/>-returning method whose name happened to end
    /// in <c>Specification</c> enrolled itself, so renaming a factory off
    /// that suffix removed it from every theory drawing on this method
    /// silently, and those theories then passed with fewer cases. Demonstrated
    /// directly: renaming seven of these ten factories off the suffix left the
    /// suite green at a smaller case count, with the content-shaped
    /// dangling-reference walk, <see cref="SharedStyleAcrossNestedStructureSpecification"/>,
    /// and three of the documented asymmetries silently gone. A rename or
    /// deletion of a listed factory is now a build error, since
    /// <see langword="nameof"/> stops compiling the moment the name it names
    /// no longer exists; a factory renamed off the suffix while its
    /// <see langword="nameof"/> reference is updated to match (so this method
    /// still compiles) is instead caught by
    /// <see cref="AdversarialCorpusTests.Corpus_ContainsExactlyTheExpectedRoster"/>,
    /// which compares this fixed roster against
    /// <see cref="DiscoverAdversarialSpecificationNamesBySuffix"/>, the
    /// original discovery rule, kept only for that comparison.
    /// <para>
    /// NOTE: adding a legitimate new adversarial factory means adding its name
    /// here too. That is deliberate, not an oversight, for the same reason
    /// <see cref="SampleCorpus"/>'s own fixed roster gives: naming every
    /// member explicitly, rather than deriving the roster from any predicate,
    /// is the only way this list can notice one going missing without also
    /// being blind to it going missing for the same reason.
    /// </para>
    /// </summary>
    internal static IEnumerable<string> AdversarialSpecificationNames() =>
    [
        nameof(DanglingDefaultStyleFontIndexSpecification),
        nameof(DanglingContentFontIndexSpecification),
        nameof(DanglingHeaderFontIndexSpecification),
        nameof(DanglingNestedListItemFontIndexSpecification),
        nameof(AggregateAssetBytesOverLimitSpecification),
        nameof(SharedStyleAcrossNestedStructureSpecification),
        nameof(MalformedButWellSignedImageSpecification),
        nameof(MalformedEmbeddedFontSpecification),
        nameof(EmptyOwnerPasswordSpecification),
        nameof(OutputIntentRejectedByLibrarySpecification),
        nameof(ObjectStreamsWithEncryptionSpecification),
    ];

    /// <summary>
    /// The discovery rule <see cref="AdversarialSpecificationNames"/> used
    /// before the fixed roster replaced it, kept only so
    /// <see cref="AdversarialCorpusTests.Corpus_ContainsExactlyTheExpectedRoster"/>,
    /// which lives in a different class and so has no access to these private
    /// factories for a direct <see langword="nameof"/> reference of its own,
    /// can detect a factory that still matches this suffix rule but is
    /// missing from the fixed roster, or one that no longer matches it.
    /// </summary>
    internal static IEnumerable<string> DiscoverAdversarialSpecificationNamesBySuffix() =>
        typeof(SymmetryTests)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(DocumentSpec) && method.Name.EndsWith("Specification", StringComparison.Ordinal))
            .Select(method => method.Name);

    [Theory]
    [MemberData(nameof(AdversarialSpecNames))]
    public void AdversarialSpecification_RenderAndEmitAgree(string specName)
    {
        var method = typeof(SymmetryTests).GetMethod(specName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var spec = (DocumentSpec)method.Invoke(null, null)!;

        RenderAndEmitAgreeOnArgumentRejection(spec);
    }

    /// <summary>
    /// A specification BOTH consumers reject, which the corpus had none of.
    /// <see cref="DocumentSpec.DefaultTextStyle"/> names an embedded font by
    /// index while <see cref="DocumentSpec.EmbeddedFonts"/> is empty. That
    /// cannot be checked by either member's own accessor, since neither can
    /// see the other at its final value, so it survives construction and is
    /// rejected by <c>ValidateEmbeddedFontReferences</c>, which Render and Emit
    /// each call first.
    /// </summary>
    private static DocumentSpec DanglingDefaultStyleFontIndexSpecification() => new()
    {
        Page = new PageSizeSpec(200, 200),
        DefaultTextStyle = new TextStyleSpec { Font = FontSpec.FromEmbedded(0) },
        Content = [new PlainTextSpec { Text = "x" }],
    };

    /// <summary>
    /// The same dangling reference reached through <see cref="DocumentSpec.Content"/>
    /// rather than through the document's own default style, so the walk that
    /// validates content font references is exercised too.
    /// </summary>
    private static DocumentSpec DanglingContentFontIndexSpecification() => new()
    {
        Page = new PageSizeSpec(200, 200),
        DefaultTextStyle = Style(),
        Content = [new ParagraphSpec { Runs = [new TextRunSpec("x", new TextStyleSpec { Font = FontSpec.FromEmbedded(2) })] }],
    };

    /// <summary>
    /// The same dangling reference on a running band's own style, which is a
    /// third distinct validation path.
    /// </summary>
    private static DocumentSpec DanglingHeaderFontIndexSpecification() => new()
    {
        Page = new PageSizeSpec(200, 200),
        DefaultTextStyle = Style(),
        Content = [new PlainTextSpec { Text = "x" }],
        Header = new RunningBandSpec { Template = "{page}", Style = new TextStyleSpec { Font = FontSpec.FromEmbedded(0) } },
    };

    /// <summary>
    /// The same dangling reference on a list item nested two levels deep,
    /// which is the recursive arm of the content walk. This is also the
    /// corpus's only CONTENT-shaped adversarial specification: every other one
    /// is asset-, encryption- or output-intent-shaped, and none of them
    /// exercises the per-content-type dispatch where the two consumers most
    /// plausibly diverge.
    /// </summary>
    private static DocumentSpec DanglingNestedListItemFontIndexSpecification() => new()
    {
        Page = new PageSizeSpec(200, 200),
        DefaultTextStyle = Style(),
        Content =
        [
            new ListSpec
            {
                Style = ListStyle.Unordered,
                Items =
                [
                    new ListItemSpec
                    {
                        Text = "outer",
                        Children =
                        [
                            new ListItemSpec
                            {
                                Text = "inner",
                                Style = new TextStyleSpec { Font = FontSpec.FromEmbedded(0) },
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// A specification whose aggregate asset bytes exceed
    /// <see cref="SpecLimits.MaxTotalAssetBytes"/>: two arbitrary 17 MB
    /// <see cref="DocumentSpec.EmbeddedFonts"/> entries, each individually
    /// under <see cref="SpecLimits.MaxAssetBytes"/> (20 MB) but together over
    /// the 33,554,432 byte aggregate cap. Passes every construction-time
    /// check (each entry's own length, the embedded-font COUNT, and, since
    /// neither entry is ever referenced by index,
    /// <see cref="DocumentSpec.ValidateEmbeddedFontReferences"/> too) and is
    /// rejected only by <see cref="DocumentSpec.ValidateAggregateAssetBytes"/>,
    /// which <see cref="SpecRenderer.Render"/> and
    /// <see cref="SpecCodeEmitter.Emit"/> each call immediately after
    /// <see cref="DocumentSpec.ValidateEmbeddedFontReferences"/>. Arbitrary
    /// bytes are enough here, unlike <see cref="MalformedEmbeddedFontSpecification"/>:
    /// this check runs before either consumer ever parses a font, so no real
    /// TrueType structure is needed for the two to agree.
    /// </summary>
    private static DocumentSpec AggregateAssetBytesOverLimitSpecification()
    {
        var oneFont = new byte[17 * 1024 * 1024];
        return new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            EmbeddedFonts = [oneFont, (byte[])oneFont.Clone()],
            Content = [new PlainTextSpec { Text = "x" }],
        };
    }

    /// <summary>
    /// Accepted by both consumers, and shaped for the agreement that actually
    /// matters rather than for a rejection: one value-equal
    /// <see cref="TextStyleSpec"/> reused across a nested list item, a spanning
    /// table cell and a paragraph run. The renderer caches styles and the
    /// emitter hoists them, both keyed on that record equality, so this is
    /// where a divergence about whether a style object is shared would show.
    /// </summary>
    private static DocumentSpec SharedStyleAcrossNestedStructureSpecification()
    {
        var shared = new TextStyleSpec
        {
            Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica),
            FontSize = 11,
        };

        var equalButDistinct = new TextStyleSpec
        {
            Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica),
            FontSize = 11,
        };

        return new DocumentSpec
        {
            Page = new PageSizeSpec(300, 300),
            DefaultTextStyle = Style(),
            Content =
            [
                new ParagraphSpec { Runs = [new TextRunSpec("first", shared), new TextRunSpec("second", equalButDistinct)] },
                new ListSpec
                {
                    Style = ListStyle.OrderedDecimal,
                    DefaultStyle = shared,
                    Items = [new ListItemSpec { Text = "outer", Style = equalButDistinct, Children = [new ListItemSpec { Text = "inner", Style = shared }] }],
                },
                new TableSpec
                {
                    DefaultCellStyle = equalButDistinct,
                    Rows =
                    [
                        new TableRowSpec { Cells = [new TableCellSpec { Content = "spanning", ColSpan = 2, Style = shared }] },
                        new TableRowSpec { Cells = [new TableCellSpec { Content = "a" }, new TableCellSpec { Content = "b" }] },
                    ],
                },
            ],
        };
    }

    /// <summary>
    /// A well-signed but structurally malformed PNG. Deliberately asymmetric
    /// (Render throws InvalidOperationException; Emit succeeds): included
    /// here anyway, to prove the guard does NOT misfire on the documented
    /// asymmetry it explicitly carves out.
    /// </summary>
    private static DocumentSpec MalformedButWellSignedImageSpecification()
    {
        byte[] truncatedPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xFF, 0xFF, 0xFF];
        return new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new ImageSpec { Format = ImageFormat.Png, Bytes = truncatedPng }],
        };
    }

    /// <summary>Same shape as <see cref="MalformedButWellSignedImageSpecification"/>, for an embedded font instead of an image.</summary>
    private static DocumentSpec MalformedEmbeddedFontSpecification()
    {
        byte[] tooShortToBeAFont = [0x00, 0x01, 0x00, 0x00];
        return new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            EmbeddedFonts = [tooShortToBeAFont],
            Content = [new PlainTextSpec { Text = "x", Style = Style() }],
        };
    }

    /// <summary>
    /// An empty (not null) OwnerPassword beside a non-empty UserPassword and
    /// unrestricted Permissions: passes DocumentSpec.Encryption's own
    /// construction-time check (which only fires when Permissions restricts
    /// anything), but Document.Encrypt itself refuses it unconditionally.
    /// Deliberately asymmetric for the same reason as the two above.
    /// </summary>
    private static DocumentSpec EmptyOwnerPasswordSpecification() => new()
    {
        Page = new PageSizeSpec(200, 200),
        DefaultTextStyle = Style(),
        Content = [new PlainTextSpec { Text = "x", Style = Style() }],
        Encryption = new EncryptionSpec { UserPassword = "user-secret", OwnerPassword = "", Permissions = PdfPermissions.All },
    };

    /// <summary>
    /// An ICC output intent declaring a colour space <see cref="IccProfileHeader"/>
    /// does not recognise, with a ComponentCount the library itself rejects.
    /// Passes DocumentSpec.OutputIntent's own construction-time check (which
    /// only cross-validates a recognised colour space) but
    /// Document.SetPdfAOutputIntent refuses it. Deliberately asymmetric for
    /// the same reason as the three above.
    /// </summary>
    private static DocumentSpec OutputIntentRejectedByLibrarySpecification()
    {
        var profile = (byte[])DocumentSpecSamples.SrgbIccProfileBytes().Clone();
        profile[16] = (byte)'2';
        profile[17] = (byte)'C';
        profile[18] = (byte)'L';
        profile[19] = (byte)'R';

        return new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Conformance = VellumPdf.Document.PdfConformance.PdfA2b,
            Content = [new PlainTextSpec { Text = "x", Style = Style() }],
            OutputIntent = new PdfAOutputIntentSpec { IccProfile = profile, ComponentCount = 2, OutputConditionIdentifier = "x" },
        };
    }

    /// <summary>
    /// UseObjectStreams combined with Encryption: DocumentSpec does not guard
    /// this combination at construction (see its own remark), and
    /// Document.Save refuses it with NotSupportedException, wrapped by
    /// SpecRenderer into InvalidOperationException. Deliberately asymmetric
    /// for the same reason as the specifications above: Emit never calls
    /// Document.Save, so it cannot discover this.
    /// </summary>
    private static DocumentSpec ObjectStreamsWithEncryptionSpecification() => new()
    {
        Page = new PageSizeSpec(200, 200),
        DefaultTextStyle = Style(),
        UseObjectStreams = true,
        Content = [new PlainTextSpec { Text = "x", Style = Style() }],
        Encryption = new EncryptionSpec { UserPassword = "user-secret" },
    };

    /// <summary>An unrecognised <see cref="ContentItemSpec"/> subtype, private to this file, fed directly to <see cref="DocumentSpec.Content"/> below rather than reaching into another test file's own nested type.</summary>
    private sealed record UnrecognisedContentItemSpecForSymmetryProof : ContentItemSpec;

    /// <summary>
    /// The ContentItemSpec divergence's exact shape, proven here rather than left implicit: an
    /// unrecognised <see cref="ContentItemSpec"/> subtype cannot even be
    /// CONSTRUCTED into a <see cref="DocumentSpec"/> any more
    /// (<see cref="DocumentSpec.Content"/> rejects it), which is what
    /// guarantees <see cref="SpecRenderer.Render"/> and
    /// <see cref="SpecCodeEmitter.Emit"/> agree about it: neither can ever be
    /// called with it at all. Before the fix, this assertion failed (the
    /// specification constructed successfully), and feeding the resulting
    /// spec to <see cref="RenderAndEmitAgreeOnArgumentRejection"/> failed
    /// too: Render succeeded silently while Emit threw
    /// <see cref="ArgumentOutOfRangeException"/>, exactly the disagreement
    /// this guard exists to catch. Both failures were reproduced by hand
    /// during this fix (see the class remark's PROOF paragraph) and are not
    /// committed as failing tests, since the fix is already in place.
    /// </summary>
    [Fact]
    public void UnrecognisedContentItem_CannotBeConstructed_SoRenderAndEmitCannotDisagreeAboutIt()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content = [new UnrecognisedContentItemSpecForSymmetryProof()],
            });

        Assert.Contains("neither SpecRenderer nor SpecCodeEmitter recognises", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Mirrors <see cref="UnrecognisedContentItem_CannotBeConstructed_SoRenderAndEmitCannotDisagreeAboutIt"/> for the FontKind divergence: an out-of-range <see cref="FontKind"/>, also now rejected at construction, for the identical reason.</summary>
    [Fact]
    public void OutOfRangeFontKind_CannotBeConstructed_SoRenderAndEmitCannotDisagreeAboutIt()
    {
        var exception = Assert.Throws<ArgumentException>(() => new FontSpec { Kind = (FontKind)99 });
        Assert.Contains("Kind", exception.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// Drives <see cref="SymmetryTests.AgreementViolation"/> with all four
/// combinations of outcomes directly, because the corpus alone cannot.
/// </summary>
/// <remarks>
/// Every specification in that corpus makes the rule evaluate the same pair of
/// inputs, so a weakened comparison would keep passing. Measured before this
/// was written: all 24 corpus members produced "neither consumer rejected this
/// as malformed", and the arm that checks Emit's own contract had never been
/// reached at all. These four cases are what make the rule's own logic a
/// tested thing rather than an assumed one.
/// </remarks>
public class SymmetryGuardRuleTests
{
    [Fact]
    public void NeitherRejects_IsAgreement() =>
        Assert.Null(SymmetryTests.AgreementViolation(false, null, false, null));

    [Fact]
    public void BothRejectAsMalformed_IsAgreement() =>
        Assert.Null(SymmetryTests.AgreementViolation(true, new ArgumentException("render"), true, new ArgumentException("emit")));

    [Fact]
    public void OnlyRenderRejectsAsMalformed_IsAViolation()
    {
        var violation = SymmetryTests.AgreementViolation(true, new ArgumentException("render"), false, null);

        Assert.NotNull(violation);
        Assert.Contains("disagree", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyEmitRejectsAsMalformed_IsAViolation()
    {
        var violation = SymmetryTests.AgreementViolation(false, null, true, new ArgumentException("emit"));

        Assert.NotNull(violation);
        Assert.Contains("disagree", violation, StringComparison.Ordinal);
    }

    /// <summary>
    /// The documented asymmetry, which must NOT be reported: the library
    /// refuses something only execution can discover, so Render throws
    /// <see cref="InvalidOperationException"/> while Emit returns the correct
    /// code for it.
    /// </summary>
    [Fact]
    public void RenderThrowsInvalidOperationWhileEmitSucceeds_IsAgreement() =>
        Assert.Null(SymmetryTests.AgreementViolation(true, new InvalidOperationException("library refused"), false, null));

    /// <summary>
    /// Emit promises never to throw <see cref="InvalidOperationException"/>,
    /// since it executes nothing. This arm of the rule was unreachable from
    /// the corpus.
    /// </summary>
    [Fact]
    public void EmitThrowsInvalidOperation_IsAViolation()
    {
        var violation = SymmetryTests.AgreementViolation(true, new InvalidOperationException("render"), true, new InvalidOperationException("emit"));

        Assert.NotNull(violation);
        Assert.Contains("promises never happens", violation, StringComparison.Ordinal);
    }
}

/// <summary>
/// The adversarial corpus is now a fixed roster, named explicitly with
/// <see langword="nameof"/> on <see cref="SymmetryTests.AdversarialSpecificationNames"/>;
/// see that member's remark for what replaced the previous no-floor
/// discovery-by-suffix rule and why. This class pins what the corpus must
/// CONTAIN, in terms of the verdicts it produces rather than a count, which is
/// the property that actually matters, and separately pins the fixed roster
/// itself against the original discovery rule.
/// </summary>
public class AdversarialCorpusTests
{
    /// <summary>
    /// The four outcomes a consumer can produce for a specification that
    /// itself constructed successfully. Collapsing "accepted" and "refused at
    /// run time" into one bucket is exactly what let this corpus's original
    /// verdict helper pass on a specification neither consumer rejected,
    /// while claiming to have found the documented asymmetry. Separating
    /// <see cref="RefusedAtRuntime"/> from <see cref="Crashed"/> closes a
    /// narrower version of the same defect: a bare <c>catch (Exception)</c>
    /// previously assigned <see cref="RefusedAtRuntime"/>, the verdict this
    /// file documents as the LIBRARY refusing at execution time, to anything
    /// that was not an <see cref="ArgumentException"/>, including a
    /// <see cref="NullReferenceException"/> from a genuine bug. A crash could
    /// therefore be reported as the documented asymmetry
    /// <see cref="Corpus_ContainsADocumentedAsymmetry"/> exists to pin.
    /// </summary>
    private enum ConsumerVerdict
    {
        /// <summary>The consumer produced output for this specification.</summary>
        Accepted,

        /// <summary>The consumer's own per-content-type or per-feature dispatch refused this specification as malformed, with an <see cref="ArgumentException"/>.</summary>
        RejectedAsMalformed,

        /// <summary>The specification constructed and passed dispatch, but the library itself refused it only at execution time, with an <see cref="InvalidOperationException"/>: the documented, deliberate asymmetry this file's class remark names.</summary>
        RefusedAtRuntime,

        /// <summary>The consumer threw something that is neither an <see cref="ArgumentException"/> nor an <see cref="InvalidOperationException"/>: not a documented rejection of any kind, and never to be reported as one.</summary>
        Crashed,
    }

    private static ConsumerVerdict Verdict(Action action)
    {
        try
        {
            action();
            return ConsumerVerdict.Accepted;
        }
        catch (ArgumentException)
        {
            return ConsumerVerdict.RejectedAsMalformed;
        }
        catch (InvalidOperationException)
        {
            return ConsumerVerdict.RefusedAtRuntime;
        }
        catch (Exception)
        {
            return ConsumerVerdict.Crashed;
        }
    }

    /// <summary>
    /// Drives <see cref="Verdict"/> directly with all four outcomes a
    /// consumer action can produce, for the reason
    /// <see cref="SymmetryGuardRuleTests"/> drives
    /// <see cref="SymmetryTests.AgreementViolation"/> directly rather than
    /// leaving it to whatever the corpus happens to exercise: the corpus
    /// alone only ever produces the outcomes its own members happen to
    /// trigger today, so a classification rule with a blind spot in it could
    /// regress silently. Closes the Low directly: before the fix, a bare
    /// <c>catch (Exception)</c> classified a
    /// <see cref="NullReferenceException"/>, the signature of a genuine bug
    /// rather than a documented library refusal, identically to a genuine
    /// <see cref="InvalidOperationException"/>, so <see cref="Verdict"/>
    /// could not tell a crash from the documented asymmetry
    /// <see cref="Corpus_ContainsADocumentedAsymmetry"/> exists to pin, and a
    /// spec that crashed one consumer while the other succeeded would have
    /// been reported as exactly that asymmetry.
    /// </summary>
    [Fact]
    public void Verdict_Accepts_WhenTheActionSucceeds() =>
        Assert.Equal(ConsumerVerdict.Accepted, Verdict(() => { }));

    [Fact]
    public void Verdict_RejectsAsMalformed_OnArgumentException() =>
        Assert.Equal(ConsumerVerdict.RejectedAsMalformed, Verdict(() => throw new ArgumentException("malformed")));

    [Fact]
    public void Verdict_RefusedAtRuntime_OnInvalidOperationException() =>
        Assert.Equal(ConsumerVerdict.RefusedAtRuntime, Verdict(() => throw new InvalidOperationException("library refused")));

    /// <summary>
    /// The exact shape the fix closes: a crash unrelated to either
    /// documented exception contract must not be classified the same as the
    /// documented, deliberate asymmetry.
    /// </summary>
    [Fact]
    public void Verdict_Crashed_OnAnyOtherException() =>
        Assert.Equal(ConsumerVerdict.Crashed, Verdict(() => throw new NullReferenceException("genuine bug")));

    private static (ConsumerVerdict Render, ConsumerVerdict Emit) Verdicts(DocumentSpec spec) =>
        (Verdict(() => SpecRenderer.Render(spec)), Verdict(() => SpecCodeEmitter.Emit(spec)));

    private static IEnumerable<DocumentSpec> Corpus()
    {
        foreach (var name in SymmetryTests.AdversarialSpecificationNames())
        {
            var method = typeof(SymmetryTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
            yield return (DocumentSpec)method.Invoke(null, null)!;
        }
    }

    /// <summary>
    /// At least one adversarial specification must be rejected by BOTH
    /// consumers, or the guard's central comparison is only ever evaluated
    /// with one pair of inputs and cannot be said to discriminate.
    /// </summary>
    [Fact]
    public void Corpus_ContainsASpecificationBothConsumersReject() =>
        Assert.Contains(Corpus().Select(Verdicts), verdict => verdict is (ConsumerVerdict.RejectedAsMalformed, ConsumerVerdict.RejectedAsMalformed));

    /// <summary>
    /// And at least one must be the documented asymmetry NAMED in this file's
    /// class remark: <see cref="SpecRenderer.Render"/> refusing at run time
    /// something only execution can discover, while
    /// <see cref="SpecCodeEmitter.Emit"/> succeeds. Neither outcome is a
    /// rejection AS MALFORMED, so the previous two-state verdict (did either
    /// consumer throw an <see cref="ArgumentException"/>) could not tell this
    /// apart from a specification both consumers simply accept; it is
    /// satisfied by <see cref="SharedStyleAcrossNestedStructureSpecification"/>
    /// today, which is not an asymmetry at all. This assertion names the
    /// exact three-way shape instead: Render refused at run time, Emit
    /// accepted.
    /// </summary>
    [Fact]
    public void Corpus_ContainsADocumentedAsymmetry() =>
        Assert.Contains(
            Corpus().Select(Verdicts),
            verdict => verdict is (ConsumerVerdict.RefusedAtRuntime, ConsumerVerdict.Accepted));

    /// <summary>
    /// Pins the fixed roster on <see cref="SymmetryTests.AdversarialSpecificationNames"/>
    /// against <see cref="SymmetryTests.DiscoverAdversarialSpecificationNamesBySuffix"/>,
    /// the original discovery-by-suffix rule. A factory renamed off the
    /// <c>Specification</c> suffix (with its own <see langword="nameof"/>
    /// reference updated to match, so the fixed roster still compiles) drops
    /// out of the suffix-based side and fails here.
    /// <para>
    /// NOTE: this does NOT catch every new factory left off the fixed
    /// roster, despite what an earlier version of this remark claimed. The
    /// two sides compared here are the fixed <see langword="nameof"/> list
    /// and the suffix rule; a new factory enrols in the suffix-based side
    /// only if its OWN name happens to end in <c>Specification</c>. A new
    /// factory added with that suffix but omitted from the fixed roster is
    /// caught here, from the suffix side. A new factory added WITHOUT that
    /// suffix appears on neither side and is caught by neither this test nor
    /// the suffix rule at all: the fixed roster is a list an author must
    /// remember to extend, and nothing enforces that reminder for a factory
    /// the suffix rule was never going to discover either. The residual
    /// exposure this leaves is narrower than the one the fixed roster itself
    /// replaced, since an EXISTING member can no longer be silently dropped
    /// by a rename, only a brand NEW one can fail to enrol, but it is a
    /// residual, not a closed case.
    /// </para>
    /// </summary>
    [Fact]
    public void Corpus_ContainsExactlyTheExpectedRoster() =>
        RosterAssertions.AssertSameRoster(
            SymmetryTests.AdversarialSpecificationNames(),
            SymmetryTests.DiscoverAdversarialSpecificationNamesBySuffix());
}
