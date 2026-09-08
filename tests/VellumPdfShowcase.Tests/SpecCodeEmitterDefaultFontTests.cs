using VellumPdfShowcase.Web.Generation;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// C4-F-M2: <c>document.SetDefaultFont</c> is emitted only when the
/// specification actually contains a <see cref="PlainTextSpec"/> left
/// unstyled, the one content item whose emitted code reads
/// <see cref="DocumentSpec.DefaultTextStyle"/>. Emitting it unconditionally,
/// as before this fix, produced a fully spelled-out expression that did
/// nothing in eleven of the twelve round-trip samples, sitting directly
/// above unstyled <c>ListItem</c> and <c>Cell</c> constructions it does not
/// affect. A round-trip byte comparison cannot catch this: the call is
/// inert whether or not it is present, so these tests assert on the emitted
/// text directly.
/// </summary>
public class SpecCodeEmitterDefaultFontTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void Emit_NoUnstyledPlainText_OmitsSetDefaultFont()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new HeadingSpec { Text = "Heading", Level = 0 }],
        };

        var code = SpecCodeEmitter.Emit(spec);

        Assert.DoesNotContain("SetDefaultFont", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_UnstyledPlainText_EmitsSetDefaultFont()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new PlainTextSpec { Text = "Uses the default." }],
        };

        var code = SpecCodeEmitter.Emit(spec);

        Assert.Contains("SetDefaultFont", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_StyledPlainTextOnly_OmitsSetDefaultFont()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new PlainTextSpec { Text = "Explicitly styled.", Style = Style() }],
        };

        var code = SpecCodeEmitter.Emit(spec);

        Assert.DoesNotContain("SetDefaultFont", code, StringComparison.Ordinal);
    }

}

/// <summary>
/// <see cref="SpecCodeEmitter"/>'s style hoisting must count
/// <see cref="DocumentSpec.DefaultTextStyle"/> toward a shared style's usage
/// count exactly when <see cref="SpecCodeEmitter.HasUnstyledPlainText"/> says
/// the emitted code actually reads it, never unconditionally. A round-trip
/// byte comparison cannot see a violation of this: making the internal
/// CollectTextStyles helper yield DefaultTextStyle unconditionally changes
/// which styles get hoisted into a shared local without changing a single
/// rendered byte, since the affected style is still applied at every real use
/// site either way. Only an assertion on the emitted text itself, as here,
/// can catch it.
/// </summary>
public class SpecCodeEmitterHoistingScopeTests
{
    [Fact]
    public void Emit_DefaultTextStyleValueEqualToASingleUseStyle_DoesNotHoistIt()
    {
        var style = new TextStyleSpec { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };
        var valueEqualStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = style,
            Content = [new HeadingSpec { Text = "Heading", Level = 0, Style = valueEqualStyle }],
        };

        var code = SpecCodeEmitter.Emit(spec);

        // No unstyled PlainTextSpec exists, so DefaultTextStyle must not
        // count toward hoisting: the heading's value-equal style is used
        // exactly once and must be written out inline, not declared as a
        // shared "style0" local, and SetDefaultFont must not appear.
        Assert.DoesNotContain("var style0", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SetDefaultFont", code, StringComparison.Ordinal);
    }
}

/// <summary>
/// Plan section 5.4's controls exist to be exercised, not merely present:
/// section 3.4.0.2 records that an untested output-intent branch previously
/// looked like coverage while asserting nothing. These tests drive
/// <see cref="Generation.SpecRenderer"/>'s output-intent try/catch into
/// actually catching something, which no other test in this project does.
/// </summary>
public class SpecRendererOutputIntentErrorHandlingTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    /// <summary>
    /// <c>Document.SetPdfAOutputIntent</c> throws <c>ArgumentOutOfRangeException</c>
    /// when <c>componentCount</c> is not 1, 3 or 4. The model's own
    /// <see cref="IccProfileHeader"/> check only constrains
    /// <see cref="PdfAOutputIntentSpec.ComponentCount"/> against a colour
    /// space it recognises (GRAY, RGB, Lab, CMYK); a profile declaring an
    /// unrecognised colour space passes that check with any component count,
    /// so this is reachable from a fully constructed, model-valid
    /// specification, and only <see cref="Generation.SpecRenderer"/>'s
    /// try/catch stands between it and an unhandled exception.
    /// </summary>
    [Fact]
    public void Render_OutputIntentComponentCountRejectedByLibrary_ThrowsLegibleInvalidOperationException()
    {
        var profile = (byte[])DocumentSpecSamples.SrgbIccProfileBytes().Clone();

        // Replace the declared colour space (offset 16, four ASCII bytes)
        // with a signature IccProfileHeader does not recognise, so its own
        // ComponentCount check does not fire ahead of the library.
        profile[16] = (byte)'2';
        profile[17] = (byte)'C';
        profile[18] = (byte)'L';
        profile[19] = (byte)'R';

        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Conformance = VellumPdf.Document.PdfConformance.PdfA2b,
            Content = [new PlainTextSpec { Text = "x", Style = Style() }],
            OutputIntent = new PdfAOutputIntentSpec { IccProfile = profile, ComponentCount = 2, OutputConditionIdentifier = "x" },
        };

        var exception = Assert.Throws<InvalidOperationException>(() => SpecRenderer.Render(spec));
        Assert.Contains("output intent", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The omit-when-default convention <see cref="Generation.SpecCodeEmitter"/>
/// applies everywhere else in the snippet must also apply to
/// <c>Heading.Level</c> (default 0) and <c>PdfEncryptionSettings.Permissions</c>
/// (default <c>PdfPermissions.All</c>). Neither is visible to a round-trip
/// byte comparison, since both defaults are what the library itself applies
/// when the initializer is left out, so these assert on the emitted text.
/// </summary>
public class SpecCodeEmitterOmitDefaultsTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void Emit_HeadingAtDefaultLevel_OmitsLevelInitializer()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new HeadingSpec { Text = "Top level", Level = 0 }],
        };

        var code = SpecCodeEmitter.Emit(spec);

        Assert.DoesNotContain("Level =", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_HeadingAtNonDefaultLevel_IncludesLevelInitializer()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new HeadingSpec { Text = "Sub-heading", Level = 1 }],
        };

        var code = SpecCodeEmitter.Emit(spec);

        Assert.Contains("Level = 1", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_EncryptionAtDefaultPermissions_OmitsPermissionsInitializer()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new PlainTextSpec { Text = "x", Style = Style() }],
            Encryption = new EncryptionSpec { UserPassword = "user-secret", OwnerPassword = "owner-secret" },
        };

        var code = SpecCodeEmitter.Emit(spec);

        Assert.DoesNotContain("Permissions", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_EncryptionAtRestrictedPermissions_IncludesPermissionsInitializer()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new PlainTextSpec { Text = "x", Style = Style() }],
            Encryption = new EncryptionSpec
            {
                UserPassword = "user-secret",
                OwnerPassword = "owner-secret",
                Permissions = VellumPdf.Encryption.PdfPermissions.Print,
            },
        };

        var code = SpecCodeEmitter.Emit(spec);

        Assert.Contains("Permissions = PdfPermissions.Print", code, StringComparison.Ordinal);
    }
}
