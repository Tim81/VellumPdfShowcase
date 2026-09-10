using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using VellumPdf.Encryption;
using VellumPdf.Fonts;
using VellumPdf.Images;
using VellumPdf.Layout.Core;
using VellumPdf.Layout.Elements;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// Four ways to build a <see cref="DocumentSpec"/> the library cannot render,
/// all four now rejected at construction with a message naming the actual
/// problem, because <see cref="DocumentSpec.Content"/>,
/// <see cref="PieChartSpec.Slices"/>, <see cref="TableSpec.Rows"/> and
/// <see cref="TableRowSpec.Cells"/> are all <see langword="required"/>
/// properties with a non-empty guard rather than a sensible empty value.
/// </summary>
public class DocumentSpecValidationTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void DocumentSpec_EmptyContent_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec { Page = new PageSizeSpec(200, 200), DefaultTextStyle = Style(), Content = [] });
        Assert.Contains("content", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PieChartSpec_EmptySlices_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() => new PieChartSpec { Slices = [], Diameter = 100 });
        Assert.Contains("slice", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TableSpec_EmptyRows_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() => new TableSpec { Rows = [] });
        Assert.Contains("row", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TableRowSpec_EmptyCells_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() => new TableRowSpec { Cells = [] });
        Assert.Contains("cell", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("ftp://example.com/file")]
    public void TextStyleSpec_LinkUriWithDisallowedScheme_ThrowsAtConstruction(string uri)
    {
        Assert.Throws<ArgumentException>(() => new TextStyleSpec { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica), LinkUri = uri });
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com")]
    public void TextStyleSpec_LinkUriWithAllowedScheme_Constructs(string uri)
    {
        var style = new TextStyleSpec { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica), LinkUri = uri };
        Assert.Equal(uri, style.LinkUri);
    }

    /// <summary>
    /// Cycle 6 left open, cycle 7 closes: <c>Uri.TryCreate</c> accepts a C0
    /// control character or DEL embedded in an otherwise well-formed
    /// <c>http</c>/<c>https</c> URI (measured directly against every value
    /// below) and reports the scheme unchanged, so the scheme check alone
    /// does not reject any of them; the stored value is the caller's own
    /// string, not <c>Uri.AbsoluteUri</c>, so <see cref="Uri"/>'s own
    /// percent-encoding of these bytes in ITS normalised form never reaches
    /// <see cref="TextStyleSpec.LinkUri"/> either.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/\u0000nul")]
    [InlineData("https://example.com/\u0001x")]
    [InlineData("https://example.com/\tx")]
    [InlineData("https://example.com/\rx")]
    [InlineData("https://example.com/\nx")]
    [InlineData("https://example.com/\u001Bescape")]
    [InlineData("https://example.com/\u007Fdel")]
    public void TextStyleSpec_LinkUriWithControlCharacter_ThrowsAtConstruction(string uri)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new TextStyleSpec { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica), LinkUri = uri });
        Assert.Contains("control character", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The counterpart to the control-character rejection above: an
    /// ordinary printable, non-ASCII character (nothing in 0x00-0x1F or
    /// 0x7F) must not be rejected merely for being unusual.
    /// </summary>
    [Fact]
    public void TextStyleSpec_LinkUriWithNonAsciiPrintableCharacter_Constructs()
    {
        var uri = "https://example.com/café";
        var style = new TextStyleSpec { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica), LinkUri = uri };
        Assert.Equal(uri, style.LinkUri);
    }

    /// <summary>
    /// Plan section 3.4.0.1: every member of <see cref="TextStyleSpec"/> must
    /// implement value equality, because <see cref="Generation.SpecRenderer"/>'s
    /// style cache and <see cref="Generation.SpecCodeEmitter"/>'s style
    /// hoisting both key on this record's own equality, and C# record
    /// equality falls back to reference equality for any member whose type
    /// does not implement value equality. Two independently constructed but
    /// equal instances must therefore be <c>Equals</c>, hash alike, and
    /// collide as the same dictionary key.
    /// </summary>
    [Fact]
    public void TextStyleSpec_TwoEqualInstances_AreEqualHashAlikeAndCollideInADictionary()
    {
        var first = new TextStyleSpec
        {
            Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.HelveticaBold),
            FontSize = 13,
            Leading = 15,
            Color = new ColorRgb(0.1, 0.2, 0.3),
            LinkUri = "https://example.com",
        };
        var second = new TextStyleSpec
        {
            Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.HelveticaBold),
            FontSize = 13,
            Leading = 15,
            Color = new ColorRgb(0.1, 0.2, 0.3),
            LinkUri = "https://example.com",
        };

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());

        var dictionary = new Dictionary<TextStyleSpec, string> { [first] = "value" };
        Assert.True(dictionary.ContainsKey(second));
        Assert.Equal("value", dictionary[second]);
    }

    /// <summary>
    /// Guards plan section 3.4.0.1 the way <see cref="TextStyleSpec_TwoEqualInstances_AreEqualHashAlikeAndCollideInADictionary"/>
    /// cannot. That test constructs both instances by naming every member
    /// this record has TODAY; a member added later defaults to
    /// <see langword="null"/> on both without either instance ever setting
    /// it, so the two stay equal regardless of the new member's own equality
    /// behaviour, and the test that is supposed to guard the invariant stays
    /// green while the invariant it names is silently broken. This test
    /// instead reflects over whatever members <see cref="TextStyleSpec"/>
    /// actually has, so it inspects a future member without anyone having to
    /// remember to teach it that member's name.
    /// <para>
    /// Cycle 7 review: the ORIGINAL version of this guard enumerated
    /// <see cref="TextStyleSpec"/>'s own PROPERTIES and waved through every
    /// VALUE type unconditionally, on the theory that
    /// <see cref="ValueType.Equals(object?)"/> always compares every field.
    /// Three shapes broke that theory while leaving the guard green: a
    /// <see langword="readonly record struct"/> WRAPPING an array (an array
    /// field's own equality is reference-based regardless of what wraps it,
    /// and <see cref="System.Collections.Immutable.ImmutableArray{T}"/>, the
    /// natural type for the dash pattern plan section 3.4.0.1 names, has
    /// exactly this shape); a public FIELD, invisible to <c>GetProperties</c>
    /// entirely; and a CLASS that overrides <c>Equals(object?)</c> to compare
    /// by reference, which the original guard's own final check (does an
    /// override exist at all) waved through since an override, any override,
    /// satisfied it. See <see cref="HasValueEquality"/> for how each is
    /// closed: FIELDS, not properties, are enumerated (a struct's own
    /// backing field for a wrapped array is exactly how the field enumeration
    /// below finds it); a member's own type is walked RECURSIVELY into its
    /// own fields regardless of value-type-ness or override presence,
    /// bottoming out only at a genuine primitive, <see langword="enum"/>, or
    /// <see cref="string"/>, and treating <see langword="array"/> as always
    /// unsafe; and a CLASS with a custom override is additionally checked
    /// BEHAVIOURALLY, not merely for the override's presence.
    /// </para>
    /// </summary>
    [Fact]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Test-only reflection over this assembly's own types; never trimmed or published, so " +
            "Type.GetFields/GetMethod cannot observe a member removed by the linker.")]
    public void TextStyleSpec_EveryMember_HasValueEquality()
    {
        var fields = AllInstanceFields(typeof(TextStyleSpec)).ToList();
        Assert.NotEmpty(fields);

        foreach (var field in fields)
        {
            var isSafe = HasValueEquality(field.FieldType, visiting: null, out var reason);
            Assert.True(
                isSafe,
                $"TextStyleSpec.{field.Name} has type {field.FieldType}: {reason} Record equality falls back to " +
                "reference equality for a member like this, which would let two value-equal TextStyleSpec " +
                "instances compare unequal; see the remark on TextStyleSpec.");
        }
    }

    /// <summary>
    /// Every instance field <paramref name="type"/> declares, walked up its
    /// own inheritance chain. <c>Type.GetFields(BindingFlags.NonPublic | ...)</c>
    /// alone returns only a PRIVATE field declared on <paramref name="type"/>
    /// itself: <c>BindingFlags.FlattenHierarchy</c> governs static members
    /// only, so a base type's own private field, exactly the shape a
    /// record's compiler-generated auto-property backing field takes one
    /// level up an inheritance chain, is invisible to a single
    /// non-recursive <c>GetFields</c> call regardless of which flags are
    /// combined with <c>NonPublic</c>. Walking the chain by hand with
    /// <c>BindingFlags.DeclaredOnly</c> at each level is the only way to
    /// reach a base type's own private fields.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Test-only reflection over this assembly's own types; never trimmed or published, so " +
            "Type.GetFields/BaseType cannot observe a member removed by the linker.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Test-only reflection over this assembly's own types; never trimmed or published, so " +
            "Type.BaseType losing DynamicallyAccessedMembers annotations across the walk cannot observe a member " +
            "removed by the linker.")]
    private static List<FieldInfo> AllInstanceFields(Type type)
    {
        List<FieldInfo> fields = [];
        for (var current = type; current is not null; current = current.BaseType)
        {
            fields.AddRange(current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        }

        return fields;
    }

    /// <summary>
    /// Whether <paramref name="type"/> genuinely implements value equality,
    /// walked recursively into its own instance FIELDS (never merely its
    /// properties: a raw public field has no corresponding property, and a
    /// property's own backing field is exactly what this reaches for an
    /// auto-implemented or <see langword="field"/>-keyword property). A
    /// primitive, <see langword="enum"/>, <see cref="decimal"/> or
    /// <see cref="string"/> bottoms the recursion out as safe. An
    /// <see langword="array"/> is always unsafe: <see cref="Array"/> does not
    /// override <c>Equals(object?)</c>, and neither does the compiler-
    /// generated per-field comparison a C# <see langword="record"/> or
    /// <see langword="record struct"/> performs for an array-typed field of
    /// its OWN, so being a record is not, on its own, a reason to stop
    /// recursing either.
    /// <para>
    /// A reference type additionally needs a genuine <c>Equals(object?)</c>
    /// override (otherwise it falls back to <see cref="object.Equals(object?)"/>'s
    /// reference comparison directly) AND passes a BEHAVIOURAL check
    /// reflection alone cannot substitute for: two instances of the type
    /// built through <see cref="RuntimeHelpers.GetUninitializedObject"/>,
    /// which runs no constructor at all, hold identical, all-default field
    /// values by construction, so a genuinely structural override must
    /// consider them equal; an override that instead compares object
    /// identity (<c>ReferenceEquals(this, obj)</c>, the shape plan section
    /// 3.4.0.1 also warns against) reports two distinct, separately
    /// allocated instances as unequal regardless of their field contents,
    /// which is exactly what this check catches and a presence-only check
    /// on the override cannot.
    /// </para>
    /// </summary>
    private static bool HasValueEquality(Type type, HashSet<Type>? visiting = null) =>
        HasValueEquality(type, visiting, out _);

    /// <summary>
    /// Finding 4: the single-<paramref name="type"/> overload above reduced
    /// every rejection to one message, "does not implement value equality,
    /// record equality falls back to reference equality for it", which is
    /// untrue for at least two shapes a maintainer could plausibly meet: a
    /// type whose <c>Equals</c> override IS genuinely structural but that
    /// this guard cannot prove safe (a member typed as an abstract base,
    /// where <see cref="RuntimeHelpers.GetUninitializedObject(Type)"/> itself
    /// throws), and a type whose override correctly and deliberately ignores
    /// a field (a memoised hash cache) that this guard's own
    /// every-field-must-be-noticed rule cannot distinguish from a field
    /// ignored by oversight. Both are still rejected, deliberately: this
    /// guard stays strict rather than special-casing either shape, since
    /// neither is common enough here to be worth the extra rule, but the
    /// <paramref name="reason"/> this overload reports now names which of
    /// the several distinct checks actually failed, rather than always
    /// naming the same generic one.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Test-only reflection over this assembly's own types; never trimmed or published, so " +
            "Type.GetFields/GetMethod cannot observe a member removed by the linker.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Test-only reflection over this assembly's own types; never trimmed or published, so " +
            "RuntimeHelpers.GetUninitializedObject cannot observe a member removed by the linker.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = "Test-only reflection over this assembly's own types; never trimmed or published, so " +
            "RuntimeHelpers.GetUninitializedObject cannot observe a member removed by the linker.")]
    private static bool HasValueEquality(Type type, HashSet<Type>? visiting, out string? reason)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType.IsPrimitive || underlyingType.IsEnum || underlyingType == typeof(string) || underlyingType == typeof(decimal))
        {
            reason = null;
            return true;
        }

        if (underlyingType.IsArray)
        {
            reason = $"{underlyingType} is an array. Array does not override Equals(object?), and neither does the " +
                "compiler-generated per-field comparison a record performs for an array-typed field of its own, so " +
                "it falls back to reference equality regardless of the array's own contents.";
            return false;
        }

        if (underlyingType.IsInterface)
        {
            // Round nine, HIGH 4: an interface-typed member (IReadOnlyList<T>
            // is the shape plan section 3.4.0.1 names by example, a dash
            // pattern) has no instance FIELDS of its own for the recursion
            // below to find, so the original check fell through to
            // `fields.All(...)` over an EMPTY array, which is vacuously true.
            // Measured directly: HasValueEquality(typeof(IReadOnlyList<int>))
            // returned true before this fix. An interface is also not
            // IsClass, so the behavioural blank-instance check above never
            // ran for it either. Nothing about the STATIC field type tells
            // this method what concrete type will actually be stored there
            // at runtime (List<T>, T[], and ImmutableArray<T> all satisfy
            // IReadOnlyList<T> and differ in whether they implement value
            // equality), so there is no safe way to recurse further: treated
            // as unsafe, the same as an array.
            reason = $"{underlyingType} is an interface. Its concrete runtime type is not known statically, and " +
                "different concrete types satisfying it disagree about whether they implement value equality " +
                "(List<T> does not; ImmutableArray<T> does), so this cannot be verified without knowing what is " +
                "actually stored there.";
            return false;
        }

        visiting ??= [];
        if (!visiting.Add(underlyingType))
        {
            // A type reachable from itself. No member type in this codebase
            // is actually self-referential; treat it as safe rather than
            // recursing forever, so a future one fails loudly some other
            // way (a stack overflow while constructing an instance, most
            // likely) instead of silently here.
            reason = null;
            return true;
        }

        try
        {
            var equalsMethod = underlyingType.GetMethod(nameof(Equals), BindingFlags.Public | BindingFlags.Instance, [typeof(object)]);

            // A value type with no override at all falls back to
            // ValueType.Equals, which performs a genuine field-by-field
            // comparison (delegating to each field's own Equals, exactly
            // what the recursion below independently verifies), so it is
            // safe without a behavioural check. A REFERENCE type with no
            // override falls back to object.Equals, reference equality,
            // which is never safe.
            var hasCustomOverride = equalsMethod is not null &&
                equalsMethod.DeclaringType != typeof(object) &&
                equalsMethod.DeclaringType != typeof(ValueType);

            if (underlyingType.IsClass && !hasCustomOverride)
            {
                reason = $"{underlyingType} is a reference type with no Equals(object?) override of its own, so it " +
                    "falls back to object.Equals, reference equality.";
                return false;
            }

            if (hasCustomOverride)
            {
                // Low (round nine): this behavioural check was previously
                // inside an `if (underlyingType.IsClass)` block, so a VALUE
                // TYPE (a struct, including a record struct) with its OWN
                // broken custom Equals override skipped it entirely and fell
                // straight through to the field recursion below, accepted
                // regardless of what its override actually did. Running this
                // check for any type with a custom override, class or
                // struct, closes that.
                object blankA;
                object blankB;
                try
                {
                    blankA = RuntimeHelpers.GetUninitializedObject(underlyingType);
                    blankB = RuntimeHelpers.GetUninitializedObject(underlyingType);
                }
                catch (Exception ex)
                {
                    // Finding 4: previously an unlabelled `catch` that
                    // returned false with no distinguishing message. A
                    // member typed as an abstract base (an abstract record,
                    // for instance) reaches exactly this catch, since
                    // GetUninitializedObject cannot instantiate an abstract
                    // type at all; that failure says nothing about whether
                    // the type's OWN equality is structural, only that this
                    // guard could not test it. Treated the same as a
                    // demonstrated inequality: not proven safe, so unsafe,
                    // but the reason now says which of the two applied.
                    reason = $"{underlyingType}'s Equals override could not be tested behaviourally: constructing " +
                        $"a blank instance via RuntimeHelpers.GetUninitializedObject threw {ex.GetType().Name} " +
                        $"({ex.Message}). This commonly means the type cannot be instantiated without running a " +
                        "constructor (an abstract type, for instance); its value-equality behaviour is therefore " +
                        "unverified, not disproven, and is treated as unsafe rather than assumed safe.";
                    return false;
                }

                try
                {
                    if (!Equals(blankA, blankB))
                    {
                        reason = $"{underlyingType}'s Equals override reports two blank (all-default-field) " +
                            "instances unequal, so it does not perform a genuine structural comparison.";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    // An override that DEREFERENCES a field (rather than
                    // merely comparing it) throws on two all-default blank
                    // instances even though it might behave perfectly well
                    // on real ones; this must not propagate as a raw,
                    // opaque exception out of the guard itself. Treated the
                    // same as a demonstrated inequality: not proven safe, so
                    // unsafe.
                    reason = $"{underlyingType}'s Equals override threw {ex.GetType().Name} ({ex.Message}) while " +
                        "comparing two blank (all-default-field) instances, rather than completing a genuine " +
                        "structural comparison.";
                    return false;
                }

                // The blank-instance check above tests only one polarity.
                // An Equals that ignores its own fields and returns true for
                // anything of its own type passes it, passes the field
                // recursion below, and is accepted. That is the exact mirror
                // of the hazard this guard exists for: an OVER-equal member
                // would make the renderer's style cache and the emitter's
                // hoisting collapse two DIFFERENT styles into one, and the
                // library's adjacent-run merging would follow. So perturb
                // every field, one at a time, and require the override to
                // notice EACH one it is possible to perturb: an override that
                // notices field one but ignores field two is exactly as
                // over-equal, for two instances differing only in field two,
                // as one that ignores every field, so "some field noticed" is
                // not enough.
                if (!ReportsInequalityOnEveryField(underlyingType, out var unnoticedFieldReason))
                {
                    // Finding 4: this rejection can also be a FALSE positive,
                    // for a type whose value equality is genuinely correct in
                    // both polarities but that has a field (a memoised hash
                    // cache, say) its override deliberately and correctly
                    // ignores. This guard cannot distinguish that from a
                    // field ignored by oversight, so it stays strict and
                    // rejects both, but the reason now names the field and
                    // says so, rather than claiming the type "does not
                    // implement value equality", which would be false for
                    // the deliberate-ignore case.
                    reason = $"{underlyingType}'s Equals override does not demonstrably notice a change in every " +
                        $"one of its own instance fields ({unnoticedFieldReason}). This guard cannot distinguish a " +
                        "field the override deliberately and correctly ignores (a memoised hash cache, for " +
                        "instance) from one it silently omits by oversight, so both are treated as unsafe.";
                    return false;
                }
            }

            foreach (var field in AllInstanceFields(underlyingType))
            {
                if (!HasValueEquality(field.FieldType, visiting, out var fieldReason))
                {
                    reason = $"its own field '{field.Name}' has type {field.FieldType}, and {fieldReason}";
                    return false;
                }
            }

            reason = null;
            return true;
        }
        finally
        {
            visiting.Remove(underlyingType);
        }
    }

    /// <summary>
    /// Whether <paramref name="type"/>'s own <c>Equals</c> reports two
    /// instances unequal when exactly one field differs, for EVERY field it
    /// is possible to perturb, not merely some of them. A type with no
    /// perturbable field is accepted, since there is nothing its override
    /// could be ignoring.
    /// </summary>
    /// <remarks>
    /// Fields are set by reflection on uninitialized instances, so this asks
    /// only whether the override READS its own state, never whether the type
    /// would accept those values through its own constructor.
    /// <para>
    /// Requiring EVERY field, rather than accepting the first one whose
    /// perturbation the override happens to notice, closes a Low: the "some
    /// field wins" rule this replaced accepted an override that compares
    /// field one and ignores field two outright, so two instances differing
    /// only in field two compared equal. That is over-equality, the exact
    /// hazard this guard exists to prevent: the renderer's style cache and
    /// the emitter's hoisting would collapse two different styles into one.
    /// By the same "some field wins" logic, an override that THROWS while
    /// comparing one field but answers honestly for another was also
    /// accepted, which was weaker than the blank-instance check above this
    /// one is meant to be symmetric with. Both are now unsafe unconditionally:
    /// a field the override does not demonstrably notice, whether because it
    /// silently agrees or because it throws, fails the whole type immediately,
    /// regardless of what the override does with any other field.
    /// </para>
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Test-only reflection over this assembly's own types; never trimmed or published, so " +
            "Type.GetFields cannot observe a member removed by the linker.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = "Test-only reflection over this assembly's own types; never trimmed or published, so " +
            "RuntimeHelpers.GetUninitializedObject cannot observe a member removed by the linker.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Test-only reflection; this assembly is never AOT-published, and the enum types perturbed " +
            "here are this model's own.")]
    /// <remarks>
    /// Finding 1: enumerates <paramref name="type"/>'s fields through
    /// <see cref="AllInstanceFields"/>, which walks the inheritance chain,
    /// rather than a single non-recursive <c>GetFields</c> call, for the
    /// identical reason <see cref="HasValueEquality(Type, HashSet{Type}?, out string?)"/>
    /// does: a base type's own private field, invisible to a single
    /// <c>GetFields</c> call regardless of which flags accompany
    /// <c>NonPublic</c>, is exactly where an override could be ignoring a
    /// change without this method ever perturbing it to find out.
    /// <paramref name="unnoticedFieldReason"/> names the specific field and
    /// the specific way its perturbation went unnoticed (finding 4), rather
    /// than leaving the caller to report the generic "does not implement
    /// value equality" for what might be a field an override deliberately
    /// and correctly ignores.
    /// </remarks>
    private static bool ReportsInequalityOnEveryField(Type type, out string? unnoticedFieldReason)
    {
        foreach (var field in AllInstanceFields(type))
        {
            if (!TryPerturbedValue(field.FieldType, out var perturbedValue))
            {
                continue;
            }

            object baseline;
            object perturbed;

            try
            {
                baseline = RuntimeHelpers.GetUninitializedObject(type);
                perturbed = RuntimeHelpers.GetUninitializedObject(type);
                field.SetValue(perturbed, perturbedValue);
            }
            catch
            {
                // A field this technique cannot even construct or set
                // proves nothing either way: keep looking.
                continue;
            }

            try
            {
                if (Equals(baseline, perturbed))
                {
                    // The override did not notice this field changed. That is
                    // unsafe on its own, regardless of what it does with any
                    // other field: reject the whole type immediately rather
                    // than let a later field's honest comparison paper over
                    // this one.
                    unnoticedFieldReason = $"perturbing field '{field.Name}' alone left two otherwise-identical " +
                        "instances comparing equal";
                    return false;
                }
            }
            catch (Exception ex)
            {
                // An override that THROWS while comparing a perturbed
                // instance has not demonstrably noticed this field either:
                // it is exactly as unsafe as one that silently agrees for
                // it. Symmetric with the blank-instance check above, which
                // treats a throw as "not proven safe, so unsafe" rather than
                // as evidence of nothing, and, now, with the "did not
                // notice" branch immediately above: reject immediately
                // rather than let another field's honest comparison hide it.
                unnoticedFieldReason = $"comparing an instance with only field '{field.Name}' perturbed threw " +
                    $"{ex.GetType().Name} ({ex.Message})";
                return false;
            }
        }

        // Every perturbable field was noticed (or there were none to
        // perturb, in which case there is nothing to have ignored).
        unnoticedFieldReason = null;
        return true;
    }

    /// <summary>A value distinguishable from <paramref name="fieldType"/>'s default, when one can be produced.</summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Test-only reflection over this assembly's own types; never trimmed or published, so " +
            "Type.GetFields cannot observe a member removed by the linker.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = "Test-only reflection over this assembly's own types; never trimmed or published, so " +
            "RuntimeHelpers.GetUninitializedObject cannot observe a member removed by the linker.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Test-only reflection; this assembly is never AOT-published, and the enum types perturbed " +
            "here are this model's own.")]
    private static bool TryPerturbedValue(Type fieldType, out object? value)
    {
        var underlying = Nullable.GetUnderlyingType(fieldType) ?? fieldType;

        if (underlying == typeof(string))
        {
            value = "perturbed";
            return true;
        }

        if (underlying == typeof(bool))
        {
            value = true;
            return true;
        }

        if (underlying.IsEnum)
        {
            var named = Enum.GetValues(underlying).Cast<object>().FirstOrDefault(member => Convert.ToInt64(member, CultureInfo.InvariantCulture) != 0);
            value = named;
            return named is not null;
        }

        if (underlying.IsPrimitive || underlying == typeof(decimal))
        {
            try
            {
                value = Convert.ChangeType(1, underlying, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        if (underlying.IsClass && !underlying.IsAbstract)
        {
            try
            {
                value = RuntimeHelpers.GetUninitializedObject(underlying);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        if (underlying.IsValueType)
        {
            // A non-primitive value type: ColorRgb and EdgeInsets, already
            // members of this model, are exactly this shape, and neither
            // was reachable by any arm above. Build a blank instance and
            // perturb every one of ITS OWN fields it is possible to
            // perturb, recursively, so a hazard type with a field of this
            // shape can no longer hide behind "this technique produced no
            // perturbed value" the way it could when this arm was missing.
            try
            {
                var candidate = RuntimeHelpers.GetUninitializedObject(underlying);
                var innerFields = underlying.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var perturbedAnyInnerField = false;

                foreach (var innerField in innerFields)
                {
                    if (TryPerturbedValue(innerField.FieldType, out var innerValue))
                    {
                        innerField.SetValue(candidate, innerValue);
                        perturbedAnyInnerField = true;
                    }
                }

                if (perturbedAnyInnerField)
                {
                    value = candidate;
                    return true;
                }
            }
            catch
            {
                // Falls through to the "no perturbed value" result below.
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Proves <see cref="HasValueEquality"/> itself rejects each of the four
    /// shapes review has named (three from cycle 7, the interface-typed
    /// member from round nine's own HIGH 4), none of which the shape's own
    /// PREVIOUS guard caught. Each nested type below exists only to be fed to
    /// <see cref="HasValueEquality"/> directly; none is a member of
    /// <see cref="TextStyleSpec"/>, so this does not depend on, or risk
    /// corrupting, the model itself.
    /// </summary>
    public class HasValueEqualityGuardTests
    {
        /// <summary>The exact shape plan section 3.4.0.1 names by example: a value type wrapping an array, matching <see cref="System.Collections.Immutable.ImmutableArray{T}"/>.</summary>
        private readonly record struct ArrayWrappingRecordStruct(int[] Values);

        /// <summary>A public FIELD, not a property, of array type: invisible to <c>GetProperties</c> entirely.</summary>
        private sealed class PublicArrayFieldHazard
        {
            public int[] Values = [];
        }

        /// <summary>An override that exists, so a presence-only check accepts it, but compares object identity rather than field contents.</summary>
        private sealed class ReferenceEqualityOverrideHazard
        {
            public double Value { get; init; }

            public override bool Equals(object? obj) => ReferenceEquals(this, obj);

            public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
        }

        /// <summary>A genuine, structurally-correct override, included as the counterpart proof: the guard must not reject a type merely for having one.</summary>
        private sealed class StructuralEqualityOverride
        {
            public double Value { get; init; }

            public override bool Equals(object? obj) => obj is StructuralEqualityOverride other && Value.Equals(other.Value);

            public override int GetHashCode() => Value.GetHashCode();
        }

        /// <summary>
        /// The mirror of <see cref="ReferenceEqualityOverrideHazard"/>: an
        /// override that reports EVERY instance of its own type equal,
        /// ignoring its own fields. It passes a check that compares two blank
        /// instances and demands equality, and it passes the field recursion,
        /// so it was accepted before the perturbation check existed.
        /// </summary>
        /// <remarks>
        /// The consequence is the exact hazard this guard exists to prevent,
        /// with the sign reversed. The renderer caches styles and the emitter
        /// hoists them, both keyed on record equality; an over-equal member
        /// would collapse two DIFFERENT styles into one, and the library's
        /// merging of adjacent runs sharing a style instance would follow.
        /// </remarks>
        private sealed class AlwaysEqualOverrideHazard
        {
            public double Value { get; init; }

            public override bool Equals(object? obj) => obj is AlwaysEqualOverrideHazard;

            public override int GetHashCode() => 0;
        }

        [Fact]
        public void AlwaysEqualOverrideHazard_IsRejected() =>
            Assert.False(HasValueEquality(typeof(AlwaysEqualOverrideHazard)));

        [Fact]
        public void ArrayWrappingRecordStruct_IsRejected() =>
            Assert.False(HasValueEquality(typeof(ArrayWrappingRecordStruct)));

        [Fact]
        public void PublicArrayFieldHazard_ArrayFieldType_IsRejected()
        {
            // Demonstrates the enumeration-strategy half of the fix, not
            // only HasValueEquality's own recursion: GetProperties finds
            // nothing on this type at all, so a guard built on it would
            // never even reach the array field to reject it.
            var properties = typeof(PublicArrayFieldHazard).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            Assert.Empty(properties);

            var fields = typeof(PublicArrayFieldHazard).GetFields(BindingFlags.Public | BindingFlags.Instance);
            var valuesField = Assert.Single(fields);
            Assert.False(HasValueEquality(valuesField.FieldType));
        }

        [Fact]
        public void ReferenceEqualityOverrideHazard_IsRejected() =>
            Assert.False(HasValueEquality(typeof(ReferenceEqualityOverrideHazard)));

        [Fact]
        public void StructuralEqualityOverride_IsAccepted() =>
            Assert.True(HasValueEquality(typeof(StructuralEqualityOverride)));

        /// <summary>
        /// Plan section 3.4.0.1's own named example, verbatim: "a dash
        /// pattern for instance". Before the interface fix above, this
        /// vacuously passed: <c>IReadOnlyList&lt;double&gt;</c> is not a
        /// class, not an array, and has no fields of its own for the
        /// recursion to inspect, so <c>fields.All(...)</c> over an empty
        /// array returned <see langword="true"/>. Measured directly against
        /// the guard as it stood in round nine: <c>HasValueEquality(typeof(IReadOnlyList&lt;int&gt;))</c>
        /// was <see langword="true"/>.
        /// </summary>
        private sealed record DashPatternHazard
        {
            public IReadOnlyList<double>? DashPattern { get; init; }
        }

        [Fact]
        public void DashPatternInterfaceMember_IsRejected() =>
            Assert.False(HasValueEquality(typeof(IReadOnlyList<double>)));

        /// <summary>
        /// The concrete failure <see cref="DashPatternInterfaceMember_IsRejected"/>
        /// exists to catch: two <see cref="DashPatternHazard"/> instances
        /// holding separately-allocated but content-equal <see cref="List{T}"/>
        /// instances are NOT <c>Equals</c>, because <see cref="List{T}"/> does
        /// not override <see cref="object.Equals(object?)"/>, and neither does
        /// the compiler-generated per-field comparison a C# <see langword="record"/>
        /// performs for a member of an interface type it cannot see through.
        /// This is precisely the divergence plan section 3.4.0.1 warns a
        /// list- or array-typed <see cref="TextStyleSpec"/> member would
        /// silently reintroduce: <see cref="Generation.SpecRenderer"/>'s style
        /// cache and <see cref="Generation.SpecCodeEmitter"/>'s style hoisting
        /// would stop agreeing about which styles are the same one.
        /// </summary>
        [Fact]
        public void DashPatternHazard_TwoContentEqualInstances_AreNotEqual()
        {
            var first = new DashPatternHazard { DashPattern = new List<double> { 1, 2, 3 } };
            var second = new DashPatternHazard { DashPattern = new List<double> { 1, 2, 3 } };

            Assert.NotEqual(first, second);
        }

        /// <summary>
        /// A LOW round nine found: the behavioural blank-instance check
        /// previously ran only inside an <c>IsClass</c> test, so a value
        /// type (a struct) with its own broken custom <c>Equals</c> override
        /// skipped it entirely and fell straight through to safe-looking
        /// field recursion, regardless of what the override actually did.
        /// This override always returns <see langword="false"/>, the
        /// opposite defect from <see cref="ReferenceEqualityOverrideHazard"/>
        /// but the identical hazard: an override the guard must not accept
        /// merely because it exists.
        /// </summary>
        private readonly struct AlwaysUnequalStructHazard
        {
            public double Value { get; init; }

            public override bool Equals(object? obj) => false;

            public override int GetHashCode() => 0;
        }

        [Fact]
        public void AlwaysUnequalStructHazard_IsRejected() =>
            Assert.False(HasValueEquality(typeof(AlwaysUnequalStructHazard)));

        /// <summary>
        /// A LOW round nine found: an override that DEREFERENCES a field
        /// (rather than merely comparing it) threw a raw, opaque
        /// <see cref="NullReferenceException"/> out of the guard itself on
        /// <see cref="RuntimeHelpers.GetUninitializedObject(Type)"/>'s
        /// all-default blank instances, rather than the guard's own clear
        /// "does not implement value equality" failure. <see cref="Inner"/>
        /// is null on a blank instance, so <c>Equals</c> dereferencing
        /// <c>Inner.Value</c> throws.
        /// </summary>
        private sealed class DereferencingEqualityOverrideHazard
        {
            public sealed class Nested
            {
                public double Value { get; init; }
            }

            public Nested Inner { get; init; } = new();

            public override bool Equals(object? obj) =>
                obj is DereferencingEqualityOverrideHazard other && Inner.Value.Equals(other.Inner.Value);

            public override int GetHashCode() => 0;
        }

        [Fact]
        public void DereferencingEqualityOverrideHazard_IsRejectedRatherThanThrowing() =>
            Assert.False(HasValueEquality(typeof(DereferencingEqualityOverrideHazard)));

        /// <summary>
        /// A value type, not a class: the exact shape <see cref="ColorRgb"/>
        /// and <see cref="EdgeInsets"/> already have, and that this codebase's
        /// two existing model members of that shape share with any future
        /// one. Its own <see langword="record struct"/> equality is genuinely
        /// structural, included as the counterpart proof alongside the hazard
        /// below: the guard must accept a well-behaved non-primitive value
        /// type field, not merely tolerate one.
        /// </summary>
        private readonly record struct NonPrimitiveValueTypeField(double X, double Y);

        /// <summary>
        /// The hazard <see cref="ReportsInequalityOnEveryField"/>'s missing
        /// arm for a non-primitive value type let through: a class with one
        /// field of exactly <see cref="NonPrimitiveValueTypeField"/>'s shape,
        /// and an override that reports every instance of its own type equal
        /// regardless of that field's contents.
        /// </summary>
        /// <remarks>
        /// Before <see cref="TryPerturbedValue"/> gained its non-primitive
        /// value type arm, this field could not be perturbed at all: the
        /// blank-instance check passed (the override always returns
        /// <see langword="true"/> for its own type), and
        /// <see cref="ReportsInequalityOnEveryField"/> then found no
        /// perturbable field and returned <see langword="true"/> vacuously,
        /// accepting this hazard. <see cref="ColorRgb"/> and
        /// <see cref="EdgeInsets"/> are already members of this model and
        /// have exactly this shape, so a hazard type built from one was
        /// accepted unchallenged, which is precisely the divergence this
        /// guard exists to catch: the renderer's style cache and the
        /// emitter's hoisting would collapse two DIFFERENT styles into one.
        /// </remarks>
        private sealed class NonPrimitiveValueTypeFieldAlwaysEqualHazard
        {
            public NonPrimitiveValueTypeField Value { get; init; }

            public override bool Equals(object? obj) => obj is NonPrimitiveValueTypeFieldAlwaysEqualHazard;

            public override int GetHashCode() => 0;
        }

        [Fact]
        public void NonPrimitiveValueTypeField_IsAccepted() =>
            Assert.True(HasValueEquality(typeof(NonPrimitiveValueTypeField)));

        [Fact]
        public void NonPrimitiveValueTypeFieldAlwaysEqualHazard_IsRejected() =>
            Assert.False(HasValueEquality(typeof(NonPrimitiveValueTypeFieldAlwaysEqualHazard)));

        /// <summary>
        /// The second gap in <see cref="ReportsInequalityOnEveryField"/>: an
        /// override that THROWS while comparing a perturbed instance, for
        /// its only perturbable field. The blank-instance check above this
        /// one, comparing two untouched instances where <see cref="Value"/>
        /// is <see langword="0"/> on both, does not throw and does not
        /// disagree, so it passes that check and reaches the perturbation
        /// check.
        /// </summary>
        /// <remarks>
        /// Before the fix, the perturbation check's <c>catch</c> block
        /// decremented a shared "how many fields are perturbable" counter
        /// back to the value it held before this field was tried, so a type
        /// whose only perturbable field throws here read as "nothing was
        /// perturbable" and was accepted vacuously: precisely the polarity
        /// the blank-instance check's own <c>catch</c> rejects one line
        /// above it, for the identical reason (an override that cannot even
        /// be evaluated on these instances is not proven safe).
        /// </remarks>
        private sealed class EqualsThrowsOnPerturbedFieldHazard
        {
            public double Value { get; init; }

            public override bool Equals(object? obj)
            {
                var other = (EqualsThrowsOnPerturbedFieldHazard)obj!;
                return Value != 0 || other.Value != 0
                    ? throw new InvalidOperationException("Equals cannot compare a perturbed instance.")
                    : true;
            }

            public override int GetHashCode() => 0;
        }

        [Fact]
        public void EqualsThrowsOnPerturbedFieldHazard_IsRejectedRatherThanAccepted() =>
            Assert.False(HasValueEquality(typeof(EqualsThrowsOnPerturbedFieldHazard)));

        /// <summary>
        /// The Low <see cref="ReportsInequalityOnEveryField"/> was rewritten
        /// to close: two fields, but the override compares only
        /// <see cref="FieldA"/> and ignores <see cref="FieldB"/> outright. The
        /// "at least one field reports inequality" rule this guard replaced
        /// accepted this type: perturbing <see cref="FieldA"/> alone made
        /// <c>Equals</c> disagree, and the previous rule returned as soon as
        /// any one field did, without ever perturbing <see cref="FieldB"/>.
        /// Two instances differing only in <see cref="FieldB"/> compare equal
        /// under this override, which is the exact over-equality hazard this
        /// guard exists to prevent: the renderer's style cache and the
        /// emitter's hoisting would collapse two different styles into one.
        /// </summary>
        private sealed class IgnoresOneFieldOverrideHazard
        {
            public double FieldA { get; init; }

            public double FieldB { get; init; }

            public override bool Equals(object? obj) =>
                obj is IgnoresOneFieldOverrideHazard other && FieldA.Equals(other.FieldA);

            public override int GetHashCode() => FieldA.GetHashCode();
        }

        [Fact]
        public void IgnoresOneFieldOverrideHazard_IsRejected() =>
            Assert.False(HasValueEquality(typeof(IgnoresOneFieldOverrideHazard)));

        /// <summary>
        /// The mirror Low, also closed by requiring every field: an override
        /// that THROWS while comparing a perturbed <see cref="FieldA"/> but
        /// answers <see cref="FieldB"/> honestly. Under the "at least one
        /// field reports inequality" rule this guard replaced, perturbing
        /// <see cref="FieldA"/> threw (swallowed, since a throw proves
        /// nothing on its own), and perturbing <see cref="FieldB"/> then
        /// reported inequality honestly, so the type was accepted: weaker
        /// than the blank-instance check this method is meant to be
        /// symmetric with, which already treats a throw as unsafe regardless
        /// of anything else.
        /// </summary>
        private sealed class ThrowsOnOneFieldHonestOnAnotherHazard
        {
            public double FieldA { get; init; }

            public double FieldB { get; init; }

            public override bool Equals(object? obj)
            {
                var other = (ThrowsOnOneFieldHonestOnAnotherHazard)obj!;
                if (FieldA != 0 || other.FieldA != 0)
                {
                    throw new InvalidOperationException("Equals cannot compare a perturbed FieldA.");
                }

                return FieldB.Equals(other.FieldB);
            }

            public override int GetHashCode() => 0;
        }

        [Fact]
        public void ThrowsOnOneFieldHonestOnAnotherHazard_IsRejected() =>
            Assert.False(HasValueEquality(typeof(ThrowsOnOneFieldHonestOnAnotherHazard)));

        /// <summary>
        /// Finding 1: <c>GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)</c>,
        /// without <see langword="DeclaredOnly"/> and without walking
        /// <see cref="Type.BaseType"/> by hand, does NOT return a base
        /// type's own PRIVATE fields; <see cref="BindingFlags.FlattenHierarchy"/>
        /// governs static members only. A record's auto-property backing
        /// field is exactly such a private field, so an array-typed member
        /// declared on a BASE record was invisible to the previous,
        /// single-call enumeration one level up an inheritance chain. This
        /// base record carries the hazard.
        /// </summary>
        private record ArrayCarryingBaseRecord
        {
            public int[] Values { get; init; } = [];
        }

        /// <summary>
        /// The derived record itself carries no array; the hazard is
        /// reachable only by walking up to <see cref="ArrayCarryingBaseRecord"/>.
        /// A guard that enumerates only <see cref="ArrayCarryingDerivedRecord"/>'s
        /// own declared fields (its own backing field for
        /// <see cref="Value"/>, a <see langword="double"/>) finds nothing
        /// unsafe and accepts this type; measured directly against the
        /// pre-fix single-call enumeration, it did.
        /// </summary>
        private sealed record ArrayCarryingDerivedRecord : ArrayCarryingBaseRecord
        {
            public double Value { get; init; }
        }

        [Fact]
        public void ArrayCarryingDerivedRecord_IsRejected() =>
            Assert.False(HasValueEquality(typeof(ArrayCarryingDerivedRecord)));

        /// <summary>
        /// The concrete failure <see cref="ArrayCarryingDerivedRecord_IsRejected"/>
        /// exists to catch, the same shape as
        /// <see cref="DashPatternHazard_TwoContentEqualInstances_AreNotEqual"/>
        /// one level of inheritance further down: two instances holding
        /// separately-allocated but content-equal arrays in the INHERITED
        /// member are not <c>Equals</c>, because the compiler-generated
        /// per-field comparison the derived record's own <c>Equals</c>
        /// delegates to, for the base record's fields, compares
        /// <see cref="ArrayCarryingBaseRecord.Values"/> by reference.
        /// </summary>
        [Fact]
        public void ArrayCarryingDerivedRecord_TwoContentEqualInstances_AreNotEqual()
        {
            var first = new ArrayCarryingDerivedRecord { Values = [1, 2, 3], Value = 5 };
            var second = new ArrayCarryingDerivedRecord { Values = [1, 2, 3], Value = 5 };

            Assert.NotEqual(first, second);
        }

        /// <summary>
        /// The honest counterpart the finding asked for: a base and derived
        /// record pair with the identical SHAPE of inheritance as
        /// <see cref="ArrayCarryingBaseRecord"/>/<see cref="ArrayCarryingDerivedRecord"/>,
        /// but with no array anywhere in the chain. The walk up
        /// <see cref="Type.BaseType"/> that finding 1 added must not turn
        /// into over-rejection of a perfectly safe base member merely
        /// because it now reaches fields it previously could not see.
        /// </summary>
        private record WellBehavedBaseRecord
        {
            public double BaseValue { get; init; }
        }

        private sealed record WellBehavedDerivedRecord : WellBehavedBaseRecord
        {
            public double DerivedValue { get; init; }
        }

        [Fact]
        public void WellBehavedDerivedRecord_IsAccepted() =>
            Assert.True(HasValueEquality(typeof(WellBehavedDerivedRecord)));

        /// <summary>
        /// Finding 4, first shape: a class with a memoised hash field its
        /// <c>Equals</c> override correctly and deliberately ignores. Value
        /// equality is correct in BOTH polarities (two instances with equal
        /// <see cref="Value"/> compare equal regardless of
        /// <see cref="_cachedHash"/>; two with different <see cref="Value"/>
        /// compare unequal), but <see cref="ReportsInequalityOnEveryField"/>'s
        /// every-field rule cannot tell that from a field ignored by
        /// oversight, so this type is still rejected. What this test pins is
        /// not the rejection (deliberately kept, per the finding) but that
        /// the failure REASON now names the actual field and the actual
        /// mechanism, rather than the old blanket "does not implement value
        /// equality", which would be false here: this type's own equality
        /// is, in fact, correct.
        /// </summary>
        private sealed class MemoisedHashFieldHazard
        {
            public double Value { get; init; }

            private int? _cachedHash;

            public override bool Equals(object? obj) => obj is MemoisedHashFieldHazard other && Value.Equals(other.Value);

            public override int GetHashCode() => _cachedHash ??= Value.GetHashCode();
        }

        [Fact]
        public void MemoisedHashFieldHazard_IsRejected() =>
            Assert.False(HasValueEquality(typeof(MemoisedHashFieldHazard)));

        [Fact]
        public void MemoisedHashFieldHazard_RejectionReasonNamesTheIgnoredFieldRatherThanClaimingNoValueEquality()
        {
            var isSafe = HasValueEquality(typeof(MemoisedHashFieldHazard), visiting: null, out var reason);

            Assert.False(isSafe);
            Assert.NotNull(reason);
            Assert.Contains("_cachedHash", reason, StringComparison.Ordinal);
            Assert.DoesNotContain("does not implement value equality", reason, StringComparison.Ordinal);
        }

        /// <summary>
        /// Finding 4, second shape: a member typed as an abstract record
        /// base. <see cref="RuntimeHelpers.GetUninitializedObject(Type)"/>
        /// cannot instantiate an abstract type at all, so the behavioural
        /// blank-instance check throws before it can compare anything, which
        /// says nothing about whether this type's OWN equality is
        /// structural. The previous unlabelled <c>catch</c> reported this
        /// identically to a genuinely broken override; this test pins that
        /// the reason now says construction failed, not that equality is
        /// broken.
        /// </summary>
        private abstract record AbstractRecordBase
        {
            public double Value { get; init; }
        }

        private sealed record AbstractRecordBaseMemberHazard
        {
            public AbstractRecordBase? Inner { get; init; }
        }

        [Fact]
        public void AbstractRecordBase_IsRejected() =>
            Assert.False(HasValueEquality(typeof(AbstractRecordBase)));

        [Fact]
        public void AbstractRecordBase_RejectionReasonNamesConstructionFailureRatherThanClaimingNoValueEquality()
        {
            var isSafe = HasValueEquality(typeof(AbstractRecordBase), visiting: null, out var reason);

            Assert.False(isSafe);
            Assert.NotNull(reason);
            Assert.Contains("could not be tested behaviourally", reason, StringComparison.Ordinal);
            Assert.DoesNotContain("does not implement value equality", reason, StringComparison.Ordinal);
        }

        [Fact]
        public void AbstractRecordBaseMemberHazard_IsRejected() =>
            Assert.False(HasValueEquality(typeof(AbstractRecordBaseMemberHazard)));
    }
}

/// <summary>
/// Every collection member snapshots the caller's value with a
/// collection expression at construction, so a fully constructed record
/// cannot be emptied out from under itself by mutating a list the caller
/// happened to alias. Before the fix, each of these nine tests failed: the
/// init accessor checked <c>Count</c> once and then stored the caller's own
/// reference.
/// </summary>
public class DocumentSpecCollectionAliasingTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    private static DocumentSpec MinimalDocument(IReadOnlyList<ContentItemSpec> content) =>
        new() { Page = new PageSizeSpec(200, 200), DefaultTextStyle = Style(), Content = content };

    [Fact]
    public void DocumentSpec_Content_SnapshotsAliasedList()
    {
        List<ContentItemSpec> aliased = [new PlainTextSpec { Text = "kept" }];
        var spec = MinimalDocument(aliased);

        aliased.Clear();

        Assert.Single(spec.Content);
    }

    [Fact]
    public void DocumentSpec_EmbeddedFonts_SnapshotsAliasedList()
    {
        List<byte[]> aliased = [[1, 2, 3]];
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            EmbeddedFonts = aliased,
            Content = [new PlainTextSpec { Text = "x" }],
        };

        aliased.Clear();

        Assert.Single(spec.EmbeddedFonts);
    }

    [Fact]
    public void ParagraphSpec_Runs_SnapshotsAliasedList()
    {
        List<TextRunSpec> aliased = [new TextRunSpec("kept", Style())];
        var spec = new ParagraphSpec { Runs = aliased };

        aliased.Clear();

        Assert.Single(spec.Runs);
    }

    [Fact]
    public void ListSpec_Items_SnapshotsAliasedList()
    {
        List<ListItemSpec> aliased = [new ListItemSpec { Text = "kept" }];
        var spec = new ListSpec { Style = ListStyle.Unordered, Items = aliased };

        aliased.Clear();

        Assert.Single(spec.Items);
    }

    [Fact]
    public void ListItemSpec_Children_SnapshotsAliasedList()
    {
        List<ListItemSpec> aliased = [new ListItemSpec { Text = "kept" }];
        var spec = new ListItemSpec { Text = "parent", Children = aliased };

        aliased.Clear();

        Assert.Single(spec.Children);
    }

    [Fact]
    public void TableSpec_Rows_SnapshotsAliasedList()
    {
        List<TableRowSpec> aliased = [new TableRowSpec { Cells = [new TableCellSpec { Content = "kept" }] }];
        var spec = new TableSpec { Rows = aliased };

        aliased.Clear();

        Assert.Single(spec.Rows);
    }

    [Fact]
    public void TableSpec_ColumnWidths_SnapshotsAliasedList()
    {
        List<double> aliased = [100, 200];
        var spec = new TableSpec
        {
            Rows = [new TableRowSpec { Cells = [new TableCellSpec { Content = "x" }] }],
            ColumnWidths = aliased,
        };

        aliased.Clear();

        Assert.Equal(2, spec.ColumnWidths!.Count);
    }

    [Fact]
    public void TableRowSpec_Cells_SnapshotsAliasedList()
    {
        List<TableCellSpec> aliased = [new TableCellSpec { Content = "kept" }];
        var spec = new TableRowSpec { Cells = aliased };

        aliased.Clear();

        Assert.Single(spec.Cells);
    }

    [Fact]
    public void PieChartSpec_Slices_SnapshotsAliasedList()
    {
        List<PieSlice> aliased = [new PieSlice(1, ColorRgb.Black)];
        var spec = new PieChartSpec { Diameter = 10, Slices = aliased };

        aliased.Clear();

        Assert.Single(spec.Slices);
    }
}

/// <summary>
/// Cycle 7 review: <see cref="TextRunSpec"/> was the one bare positional
/// record in this model, with neither <see cref="TextRunSpec.Text"/> nor
/// <see cref="TextRunSpec.Style"/> validated at all; five different inputs
/// reached <see cref="NullReferenceException"/> as a result, which made
/// <see cref="SpecRenderer.Render"/>'s own documented exception contract
/// false, since that contract promises only <see cref="ArgumentException"/>
/// or <see cref="InvalidOperationException"/>. These tests pin the fix at
/// the narrowest point it can be pinned: construction
/// of a <see cref="TextRunSpec"/> itself, before it can ever reach a
/// <see cref="ParagraphSpec"/>, let alone <see cref="SpecRenderer.Render"/>
/// or <see cref="Generation.SpecCodeEmitter.Emit"/>.
/// </summary>
public class TextRunSpecValidationTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void NullStyle_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentNullException>(() => new TextRunSpec("x", null!));
    }

    [Fact]
    public void NullText_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentNullException>(() => new TextRunSpec(null!, Style()));
    }

    [Fact]
    public void TextBeyondMaxTextLength_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() => new TextRunSpec(new string('x', SpecLimits.MaxTextLength + 1), Style()));
        Assert.Contains("Text", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TextAtMaxTextLength_Constructs()
    {
        var run = new TextRunSpec(new string('x', SpecLimits.MaxTextLength), Style());
        Assert.Equal(SpecLimits.MaxTextLength, run.Text.Length);
    }

    /// <summary>
    /// The specific hazard cycle 7 found: a <see langword="null"/>
    /// <see cref="TextRunSpec.Style"/> reaching <see cref="ParagraphSpec"/>
    /// untouched, because that record's own <c>ValidateRuns</c> checked only
    /// <see cref="TextRunSpec.Text"/>. Now impossible: the
    /// <see cref="ArgumentNullException"/> above fires before a
    /// <see cref="TextRunSpec"/> with a <see langword="null"/>
    /// <see cref="TextRunSpec.Style"/> can exist to be placed in
    /// <see cref="ParagraphSpec.Runs"/> at all.
    /// </summary>
    [Fact]
    public void NullStyleCannotReachParagraphSpec()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ParagraphSpec { Runs = [new TextRunSpec("x", null!)] });
    }
}

/// <summary>
/// Round nine review, Medium 1: the same shape of gap
/// <see cref="TextRunSpecValidationTests"/> closed for <see cref="TextRunSpec.Style"/>
/// survived in three further <see langword="required"/> members with no null
/// check of their own: <see cref="RunningBandSpec.Style"/>,
/// <see cref="DocumentSpec.DefaultTextStyle"/>, and <see cref="TextStyleSpec.Font"/>.
/// Each reached <see cref="NullReferenceException"/> from a downstream
/// dereference in <see cref="SpecRenderer"/> or <see cref="Generation.SpecCodeEmitter"/>,
/// which made <see cref="SpecRenderer.Render"/>'s documented exception
/// contract false the same way. These tests pin each fix at the narrowest
/// point it can be pinned: the offending record's own construction.
/// </summary>
public class RequiredMemberNullValidationTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void RunningBandSpec_NullStyle_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentNullException>(() => new RunningBandSpec { Template = "{page}", Style = null! });
    }

    [Fact]
    public void DocumentSpec_NullDefaultTextStyle_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = null!,
                Content = [new PlainTextSpec { Text = "x" }],
            });
    }

    [Fact]
    public void TextStyleSpec_NullFont_ThrowsAtConstruction()
    {
        Assert.Throws<ArgumentNullException>(() => new TextStyleSpec { Font = null! });
    }
}

/// <summary>
/// <see cref="ListItemSpec.Children"/> caps nesting depth at
/// <see cref="SpecLimits.MaxListNestingDepth"/>, the one plan section 5.4
/// control a wrapped parser call cannot rescue, because an uncaught stack
/// overflow terminates the process outright.
/// </summary>
public class ListNestingDepthTests
{
    [Fact]
    public void ListItemSpec_NestingAtLimit_Constructs()
    {
        ListItemSpec item = new() { Text = "leaf" };
        for (var depth = 1; depth < SpecLimits.MaxListNestingDepth; depth++)
        {
            item = new ListItemSpec { Text = $"level{depth}", Children = [item] };
        }

        Assert.NotNull(item);
    }

    [Fact]
    public void ListItemSpec_NestingOneBeyondLimit_ThrowsAtConstruction()
    {
        ListItemSpec item = new() { Text = "leaf" };
        for (var depth = 1; depth < SpecLimits.MaxListNestingDepth; depth++)
        {
            item = new ListItemSpec { Text = $"level{depth}", Children = [item] };
        }

        var captured = item;
        var exception = Assert.Throws<ArgumentException>(() => new ListItemSpec { Text = "one too many", Children = [captured] });
        Assert.Contains("nest", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Plan section 5.4: caps on specification size, so no single specification can freeze or exhaust the tab.</summary>
public class SpecSizeLimitTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void DocumentSpec_ContentBeyondLimit_ThrowsAtConstruction()
    {
        List<ContentItemSpec> content = [.. Enumerable.Range(0, SpecLimits.MaxContentItems + 1).Select(i => (ContentItemSpec)new PlainTextSpec { Text = $"{i}" })];

        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec { Page = new PageSizeSpec(200, 200), DefaultTextStyle = Style(), Content = content });
        Assert.Contains("content", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TableSpec_RowsBeyondLimit_ThrowsAtConstruction()
    {
        List<TableRowSpec> rows = [.. Enumerable.Range(0, SpecLimits.MaxTableRows + 1).Select(_ => new TableRowSpec { Cells = [new TableCellSpec { Content = "x" }] })];

        var exception = Assert.Throws<ArgumentException>(() => new TableSpec { Rows = rows });
        Assert.Contains("row", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TableRowSpec_CellsBeyondLimit_ThrowsAtConstruction()
    {
        List<TableCellSpec> cells = [.. Enumerable.Range(0, SpecLimits.MaxTableCellsPerRow + 1).Select(i => new TableCellSpec { Content = $"{i}" })];

        var exception = Assert.Throws<ArgumentException>(() => new TableRowSpec { Cells = cells });
        Assert.Contains("cell", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PieChartSpec_SlicesBeyondLimit_ThrowsAtConstruction()
    {
        List<PieSlice> slices = [.. Enumerable.Range(0, SpecLimits.MaxChartSlices + 1).Select(_ => new PieSlice(1, ColorRgb.Black))];

        var exception = Assert.Throws<ArgumentException>(() => new PieChartSpec { Diameter = 10, Slices = slices });
        Assert.Contains("slice", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HeadingSpec_TextBeyondLimit_ThrowsAtConstruction()
    {
        var tooLong = new string('a', SpecLimits.MaxTextLength + 1);
        var exception = Assert.Throws<ArgumentException>(() => new HeadingSpec { Text = tooLong, Level = 1 });
        Assert.Contains("Text", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HeadingSpec_TextAtLimit_Constructs()
    {
        var atLimit = new string('a', SpecLimits.MaxTextLength);
        var heading = new HeadingSpec { Text = atLimit, Level = 1 };
        Assert.Equal(SpecLimits.MaxTextLength, heading.Text.Length);
    }

    [Fact]
    public void DocumentSpec_LanguageBeyondLimit_ThrowsAtConstruction()
    {
        var tooLong = new string('a', SpecLimits.MaxLanguageTagLength + 1);
        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec { Page = new PageSizeSpec(200, 200), DefaultTextStyle = Style(), Content = [new PlainTextSpec { Text = "x" }], Language = tooLong });
        Assert.Contains("Language", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TextStyleSpec_LinkUriBeyondLimit_ThrowsAtConstruction()
    {
        var tooLong = "https://example.com/" + new string('a', SpecLimits.MaxUriLength);
        var exception = Assert.Throws<ArgumentException>(() =>
            new TextStyleSpec { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica), LinkUri = tooLong });
        Assert.Contains("LinkUri", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageSpec_BytesBeyondLimit_ThrowsAtConstruction()
    {
        var tooLarge = new byte[SpecLimits.MaxAssetBytes + 1];
        var exception = Assert.Throws<ArgumentException>(() => new ImageSpec { Format = ImageFormat.Png, Bytes = tooLarge });
        Assert.Contains("Bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentSpec_EmbeddedFontBeyondLimit_ThrowsAtConstruction()
    {
        var tooLarge = new byte[SpecLimits.MaxAssetBytes + 1];
        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content = [new PlainTextSpec { Text = "x" }],
                EmbeddedFonts = [tooLarge],
            });
        Assert.Contains("EmbeddedFonts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfAOutputIntentSpec_IccProfileBeyondLimit_ThrowsAtConstruction()
    {
        var tooLarge = new byte[SpecLimits.MaxAssetBytes + 1];
        var exception = Assert.Throws<ArgumentException>(() =>
            new PdfAOutputIntentSpec { IccProfile = tooLarge, ComponentCount = 3, OutputConditionIdentifier = "x" });
        Assert.Contains("IccProfile", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Cycle 7 review: <see cref="PdfAOutputIntentSpec.ComponentCount"/> was
    /// the one numeric member in the model with no cap of its own.
    /// <see cref="IccProfileHeaderTests"/> below covers the cross-check
    /// against a profile's OWN declared colour space, which only fires for
    /// the four colour spaces <see cref="IccProfileHeader.Validate"/>
    /// recognises; a negative value is never one of those, and reached
    /// construction untouched regardless of colour space before this fix.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SpecLimits.MaxIccComponentCount + 1)]
    public void PdfAOutputIntentSpec_ComponentCountOutOfRange_ThrowsAtConstruction(int componentCount)
    {
        var tinyProfile = new byte[128];
        var exception = Assert.Throws<ArgumentException>(() =>
            new PdfAOutputIntentSpec { IccProfile = tinyProfile, ComponentCount = componentCount, OutputConditionIdentifier = "x" });
        Assert.Contains("ComponentCount", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SpecLimits.MinIccComponentCount)]
    [InlineData(SpecLimits.MaxIccComponentCount)]
    public void PdfAOutputIntentSpec_ComponentCountAtBoundary_Constructs(int componentCount)
    {
        var tinyProfile = new byte[128];
        var spec = new PdfAOutputIntentSpec { IccProfile = tinyProfile, ComponentCount = componentCount, OutputConditionIdentifier = "x" };
        Assert.Equal(componentCount, spec.ComponentCount);
    }
}

/// <summary>
/// Plan section 5.4 control 2: the declared <see cref="ImageFormat"/> must
/// agree with the image's own magic bytes, checked by
/// <see cref="DocumentSpec.Content"/> because that is the only property that
/// ever sees both <see cref="ImageSpec.Format"/> and <see cref="ImageSpec.Bytes"/>
/// fully set.
/// </summary>
public class ImageFormatAgreementTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void DocumentSpec_ImageBytesContradictDeclaredFormat_ThrowsAtConstruction()
    {
        // Ten arbitrary bytes matching none of the five signatures ImageSignature checks.
        byte[] notPng = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content = [new ImageSpec { Format = ImageFormat.Png, Bytes = notPng }],
            });
        Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Plan section 5.4: an ICC profile header must be internally consistent
/// before it is embedded as an output intent on a document claiming PDF/A or
/// PDF/UA conformance. Checked by <see cref="DocumentSpec.OutputIntent"/>
/// because that is the only property that ever sees both
/// <see cref="PdfAOutputIntentSpec.IccProfile"/> and
/// <see cref="PdfAOutputIntentSpec.ComponentCount"/> fully set.
/// </summary>
public class IccProfileHeaderTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    private static DocumentSpec DocumentWithOutputIntent(PdfAOutputIntentSpec outputIntent) => new()
    {
        Page = new PageSizeSpec(200, 200),
        DefaultTextStyle = Style(),
        Content = [new PlainTextSpec { Text = "x" }],
        OutputIntent = outputIntent,
    };

    [Fact]
    public void ThreeJunkBytes_ThrowsAtConstruction_RatherThanEmbeddingSilently()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithOutputIntent(new PdfAOutputIntentSpec { IccProfile = [1, 2, 3], ComponentCount = 3, OutputConditionIdentifier = "x" }));
        Assert.Contains("too small", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SizeFieldDisagreesWithActualLength_Throws()
    {
        var profile = DocumentSpecSamples.SrgbIccProfileBytes();
        var tampered = (byte[])profile.Clone();
        tampered[3] = unchecked((byte)(tampered[3] + 1)); // corrupt the low byte of the big-endian size field

        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithOutputIntent(new PdfAOutputIntentSpec { IccProfile = tampered, ComponentCount = 3, OutputConditionIdentifier = "x" }));
        Assert.Contains("size field", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingAcspMagic_Throws()
    {
        var profile = DocumentSpecSamples.SrgbIccProfileBytes();
        var tampered = (byte[])profile.Clone();
        tampered[36] = (byte)'X';

        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithOutputIntent(new PdfAOutputIntentSpec { IccProfile = tampered, ComponentCount = 3, OutputConditionIdentifier = "x" }));
        Assert.Contains("acsp", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComponentCountDisagreesWithDeclaredColorSpace_Throws()
    {
        var profile = DocumentSpecSamples.SrgbIccProfileBytes(); // declares "RGB ", which requires ComponentCount 3

        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithOutputIntent(new PdfAOutputIntentSpec { IccProfile = profile, ComponentCount = 4, OutputConditionIdentifier = "x" }));
        Assert.Contains("colour space", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidSrgbProfileWithMatchingComponentCount_Constructs()
    {
        var spec = DocumentWithOutputIntent(new PdfAOutputIntentSpec
        {
            IccProfile = DocumentSpecSamples.SrgbIccProfileBytes(),
            ComponentCount = 3,
            OutputConditionIdentifier = "sRGB IEC61966-2.1",
        });

        Assert.IsType<PdfAOutputIntentSpec>(spec.OutputIntent);
    }
}

/// <summary>
/// A restricted <see cref="EncryptionSpec.Permissions"/> value
/// requires an <see cref="EncryptionSpec.OwnerPassword"/>, because the
/// library authenticates full owner access to whichever password opens the
/// document, and with no owner password set that password is
/// <see cref="EncryptionSpec.UserPassword"/>. Checked by
/// <see cref="DocumentSpec.Encryption"/> because that is the only property
/// that ever sees both <see cref="EncryptionSpec.Permissions"/> and
/// <see cref="EncryptionSpec.OwnerPassword"/> fully set.
/// </summary>
public class EncryptionOwnerPasswordTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    private static DocumentSpec DocumentWithEncryption(EncryptionSpec encryption) => new()
    {
        Page = new PageSizeSpec(200, 200),
        DefaultTextStyle = Style(),
        Content = [new PlainTextSpec { Text = "x" }],
        Encryption = encryption,
    };

    [Fact]
    public void RestrictedPermissionsWithNullOwnerPassword_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithEncryption(new EncryptionSpec { UserPassword = "user-secret", Permissions = PdfPermissions.Print }));
        Assert.Contains("OwnerPassword", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestrictedPermissionsWithEmptyOwnerPassword_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithEncryption(new EncryptionSpec { UserPassword = "user-secret", OwnerPassword = "", Permissions = PdfPermissions.Print }));
        Assert.Contains("OwnerPassword", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestrictedPermissionsWithOwnerPassword_Constructs()
    {
        var spec = DocumentWithEncryption(new EncryptionSpec
        {
            UserPassword = "user-secret",
            OwnerPassword = "owner-secret",
            Permissions = PdfPermissions.Print,
        });

        Assert.NotNull(spec.Encryption);
    }

    /// <summary>
    /// The null-or-empty guard alone is one keystroke from useless,
    /// because setting <see cref="EncryptionSpec.OwnerPassword"/> equal to
    /// <see cref="EncryptionSpec.UserPassword"/> satisfies it while
    /// reproducing exactly the defect it exists to prevent: there is still
    /// only one password, so it still authenticates as owner and the
    /// restricted permission set still binds nobody who can open the file.
    /// </summary>
    [Fact]
    public void RestrictedPermissionsWithOwnerPasswordEqualToUserPassword_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithEncryption(new EncryptionSpec { UserPassword = "same-secret", OwnerPassword = "same-secret", Permissions = PdfPermissions.Print }));
        Assert.Contains("OwnerPassword", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnrestrictedPermissionsWithNullOwnerPassword_Constructs()
    {
        var spec = DocumentWithEncryption(new EncryptionSpec { UserPassword = "user-secret" });

        Assert.NotNull(spec.Encryption);
        Assert.Null(spec.Encryption!.OwnerPassword);
        Assert.Equal(PdfPermissions.All, spec.Encryption.Permissions);
    }
}

/// <summary>
/// Every collection breadth cap bounds how many distinct objects a
/// specification may hold, but the five collections below had no cap at all
/// before this fix, and <see cref="SpecLimits.MaxWalkedNodes"/> separately
/// bounds the total work a shared subtree can be walked into performing,
/// which no per-collection cap can prevent on its own.
/// </summary>
public class SpecSizeLimitBreadthTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void ListSpec_ItemsBeyondLimit_ThrowsAtConstruction()
    {
        List<ListItemSpec> items = [.. Enumerable.Range(0, SpecLimits.MaxListItems + 1).Select(i => new ListItemSpec { Text = $"{i}" })];

        var exception = Assert.Throws<ArgumentException>(() => new ListSpec { Style = ListStyle.Unordered, Items = items });
        Assert.Contains("items", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListItemSpec_ChildrenBreadthBeyondLimit_ThrowsAtConstruction()
    {
        List<ListItemSpec> children = [.. Enumerable.Range(0, SpecLimits.MaxListItemChildren + 1).Select(i => new ListItemSpec { Text = $"{i}" })];

        var exception = Assert.Throws<ArgumentException>(() => new ListItemSpec { Text = "parent", Children = children });
        Assert.Contains("children", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParagraphSpec_RunsBeyondLimit_ThrowsAtConstruction()
    {
        List<TextRunSpec> runs = [.. Enumerable.Range(0, SpecLimits.MaxParagraphRuns + 1).Select(i => new TextRunSpec($"{i}", Style()))];

        var exception = Assert.Throws<ArgumentException>(() => new ParagraphSpec { Runs = runs });
        Assert.Contains("runs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DocumentSpec_EmbeddedFontsCountBeyondLimit_ThrowsAtConstruction()
    {
        List<byte[]> fonts = [.. Enumerable.Range(0, SpecLimits.MaxEmbeddedFonts + 1).Select(_ => new byte[] { 1, 2, 3 })];

        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content = [new PlainTextSpec { Text = "x" }],
                EmbeddedFonts = fonts,
            });
        Assert.Contains("embedded fonts", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TableSpec_ColumnWidthsBeyondLimit_ThrowsAtConstruction()
    {
        List<double> widths = [.. Enumerable.Range(0, SpecLimits.MaxTableColumnWidths + 1).Select(i => (double)i)];

        var exception = Assert.Throws<ArgumentException>(() =>
            new TableSpec { Rows = [new TableRowSpec { Cells = [new TableCellSpec { Content = "x" }] }], ColumnWidths = widths });
        Assert.Contains("column widths", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The decisive case: a small number of distinct objects, each
/// individually within every per-collection cap, made to multiply through
/// sharing rather than genuine size. <see cref="SpecLimits.MaxWalkedNodes"/>
/// is the only control that can catch this, since it counts the walk itself
/// rather than the number of distinct objects constructed.
/// </summary>
public class WalkedNodeLimitTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    /// <summary>
    /// Builds a chain nested one level short of <see cref="SpecLimits.MaxListNestingDepth"/>,
    /// then references THE SAME chain instance from
    /// <see cref="SpecLimits.MaxListItemChildren"/> sibling slots under one
    /// top-level item, so every individual collection (the chain's own
    /// depth, the top item's breadth, the list's one item) sits at or under
    /// its own per-collection cap. Distinct objects number in the low
    /// hundreds, but a walk that visits a shared reference once per sibling
    /// slot, rather than once per distinct object, visits the chain's full
    /// depth on every one of the hundred slots, and that total must be
    /// rejected even though no individual collection is oversized.
    /// </summary>
    [Fact]
    public void SharedDeeplyNestedSubtree_ReferencedFromManySiblings_ThrowsAtConstruction()
    {
        // NOTE: every ListItemSpec.Text below is empty, deliberately. This
        // walk sums characters against SpecLimits.MaxTotalTextLength exactly
        // as it counts nodes against SpecLimits.MaxWalkedNodes, so any
        // non-trivial text on the thousands of node visits this shared chain
        // produces would trip the CHARACTER limit before the NODE limit this
        // test exists to isolate, and assert the wrong message. Re-checked
        // after MaxTotalTextLength changed from 100,000 to 20,000: this is
        // still true and not merely a leftover from a smaller budget. The
        // chain's own original text ("leaf", "level1".."level62") sums to 429
        // characters; repeating it across shared sibling slots reaches
        // MaxTotalTextLength's 20,000 characters at roughly the 47th sibling
        // (node 2,939), well short of the 80th sibling (node 5,001) needed to
        // reach MaxWalkedNodes. Restoring that text would make this test
        // exercise the character limit instead of the node limit it exists to
        // isolate.
        ListItemSpec chain = new() { Text = "" };
        for (var depth = 1; depth < SpecLimits.MaxListNestingDepth - 1; depth++)
        {
            chain = new ListItemSpec { Text = "", Children = [chain] };
        }

        var sharedChain = chain;
        List<ListItemSpec> siblings = [.. Enumerable.Repeat(sharedChain, SpecLimits.MaxListItemChildren)];
        var topItem = new ListItemSpec { Text = "", Children = siblings };

        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content =
                [
                    new ListSpec { Style = ListStyle.Unordered, Items = [topItem] },
                ],
            });
        Assert.Contains("walking", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The counterpart to the case above: a modest, non-shared document, far
    /// smaller than any per-collection cap, must not be rejected. This is
    /// what keeps <see cref="SpecLimits.MaxWalkedNodes"/> from being so tight
    /// that it interferes with ordinary use.
    /// </summary>
    [Fact]
    public void OrdinaryModestDocument_Constructs()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content =
            [
                new HeadingSpec { Text = "Heading", Level = 0 },
                ParagraphSpec.FromText("A short paragraph.", Style()),
                new ListSpec { Style = ListStyle.Unordered, Items = [new ListItemSpec { Text = "One" }, new ListItemSpec { Text = "Two" }] },
                new TableSpec { Rows = [new TableRowSpec { Cells = [new TableCellSpec { Content = "Cell" }] }] },
            ],
        };

        Assert.NotNull(spec);
    }
}

/// <summary>
/// Cycle 7 review: the content walk counted a <see cref="PieChartSpec"/>'s
/// slices as NODES, against <see cref="SpecLimits.MaxWalkedNodes"/>, but
/// never counted a slice's own <see cref="PieSlice.Label"/>, or an image's or
/// chart's own <see cref="ImageSpec.AltText"/> / <see cref="PieChartSpec.AltText"/>,
/// against <see cref="SpecLimits.MaxTotalTextLength"/>. Measured directly
/// before this fix: 49 charts of 100 slices each with 100,000-character
/// labels emitted 490,220,429 characters, and 2,000 charts with
/// 100,000-character <see cref="PieChartSpec.AltText"/> rendered a
/// 400,384,294-byte PDF, both entirely inside a total this walk was already
/// supposed to bound.
/// </summary>
public class UncountedTextBearingMemberTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
    ];

    [Fact]
    public void PieSliceLabels_AloneExceedMaxTotalTextLength_ThrowsAtConstruction()
    {
        // One slice's label at MaxTextLength already exceeds
        // MaxTotalTextLength (100,000 > 20,000) on its own; no other content
        // is needed to prove the label itself is counted.
        var label = new string('x', SpecLimits.MaxTextLength);
        var slices = new List<PieSlice> { new(1, ColorRgb.Black, label) };

        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(500, 500),
                DefaultTextStyle = Style(),
                Content = [new PieChartSpec { Diameter = 100, Slices = slices }],
            });
        Assert.Contains("characters", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PieChartAltText_AloneExceedsMaxTotalTextLength_ThrowsAtConstruction()
    {
        var altText = new string('x', SpecLimits.MaxTextLength);

        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(500, 500),
                DefaultTextStyle = Style(),
                Content =
                [
                    new PieChartSpec
                    {
                        Diameter = 100,
                        Slices = [new PieSlice(1, ColorRgb.Black)],
                        AltText = altText,
                    },
                ],
            });
        Assert.Contains("characters", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImageAltText_AloneExceedsMaxTotalTextLength_ThrowsAtConstruction()
    {
        var altText = new string('x', SpecLimits.MaxTextLength);

        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(500, 500),
                DefaultTextStyle = Style(),
                Content =
                [
                    new ImageSpec
                    {
                        Format = ImageFormat.Png,
                        Bytes = MinimalPng(),
                        AltText = altText,
                    },
                ],
            });
        Assert.Contains("characters", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HeadingBookmarkTitle_AloneExceedsMaxTotalTextLength_ThrowsAtConstruction()
    {
        var bookmarkTitle = new string('x', SpecLimits.MaxTextLength);

        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(500, 500),
                DefaultTextStyle = Style(),
                Content = [new HeadingSpec { Text = "Heading", Level = 0, BookmarkTitle = bookmarkTitle }],
            });
        Assert.Contains("characters", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The counterpart to the four cases above: modest slice labels and alt
    /// text, far under every cap, must not be rejected.
    /// </summary>
    /// <remarks>
    /// Round nine review (LOW): the original version of this test used
    /// three-to-four-word strings totalling roughly 30 characters, which
    /// survived <see cref="SpecLimits.MaxTotalTextLength"/> being cut from
    /// 20,000 down to 200 without failing, and so demonstrated nothing about
    /// where the boundary between "modest" and "over the cap" actually is: a
    /// negative control that passes regardless of whether the cap it exists
    /// beside is even being enforced is not much of a control. Each field
    /// here now carries 150 characters (450 total), comfortably under
    /// <see cref="SpecLimits.MaxTotalTextLength"/> but large enough that the
    /// SAME reduced-cap experiment (200) would now correctly turn this test
    /// red, which is what makes it worth having beside the four rejection
    /// cases above.
    /// </remarks>
    [Fact]
    public void ModestPieChartAndImageText_Constructs()
    {
        var sliceLabel = new string('a', 150);
        var chartAltText = new string('b', 150);
        var imageAltText = new string('c', 150);

        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(500, 500),
            DefaultTextStyle = Style(),
            Content =
            [
                new PieChartSpec
                {
                    Diameter = 100,
                    Slices = [new PieSlice(1, ColorRgb.Black, sliceLabel)],
                    AltText = chartAltText,
                },
                new ImageSpec { Format = ImageFormat.Png, Bytes = MinimalPng(), AltText = imageAltText },
            ],
        };

        Assert.NotNull(spec);
    }
}

/// <summary>
/// Every non-flags enumeration a specification carries must be rejected at
/// construction when it holds a value outside its own named members, because
/// <see cref="VellumPdfShowcase.Web.Generation.SpecCodeEmitter"/> writes each of
/// them into the displayed C# BY NAME. An undefined value emits text such as
/// <c>ListStyle.99</c>, which does not compile, while
/// <see cref="VellumPdfShowcase.Web.Generation.SpecRenderer"/> renders the same
/// specification successfully.
/// </summary>
/// <remarks>
/// This divergence is invisible to the symmetry guard, which compares whether
/// the two consumers AGREE about rejecting a specification: here they agreed,
/// because neither rejected anything. It is invisible to the round trip too,
/// whose corpus is hand-built and contains no such value. Measured directly
/// before this fix: <c>(ListStyle)99</c> and <c>(HorizontalAlignment)99</c>
/// both rendered, and <c>(Standard14)99</c> made <c>Render</c> throw a raw
/// <see cref="IndexOutOfRangeException"/>, outside its documented contract.
/// <para>
/// NOTE: one case per member, not one per enumeration. The members are what
/// the emitter interpolates, and each carries its own <c>init</c> accessor
/// that can be dropped independently of the others. A theory over the six
/// <see cref="HorizontalAlignment"/> members catches the removal of any one of
/// them; a single case over the enumeration would not.
/// </para>
/// </remarks>
public class EnumMemberValidationTests
{
    private const int Undefined = 99;

    private static TextStyleSpec Style() => new() { Font = FontSpec.FromStandard14(Standard14.Helvetica) };

    public static TheoryData<string, Action> UndefinedEnumMembers() => new()
    {
        { "FontSpec.Kind", () => _ = new FontSpec { Kind = (FontKind)Undefined } },
        { "FontSpec.Standard14Face", () => _ = new FontSpec { Kind = FontKind.Standard14, Standard14Face = (Standard14)Undefined } },
        { "ListSpec.Style", () => _ = new ListSpec { Style = (ListStyle)Undefined, Items = [new ListItemSpec { Text = "x" }] } },
        { "ImageSpec.Format", () => _ = new ImageSpec { Format = (ImageFormat)Undefined, Bytes = [1, 2, 3, 4] } },
        { "DocumentSpec.Conformance", () => _ = new DocumentSpec { Page = new PageSizeSpec(200, 200), DefaultTextStyle = Style(), Content = [new PlainTextSpec { Text = "x" }], Conformance = (VellumPdf.Document.PdfConformance)Undefined } },
        { "HeadingSpec.Alignment", () => _ = new HeadingSpec { Text = "x", Level = 0, Alignment = (HorizontalAlignment)Undefined } },
        { "ParagraphSpec.Alignment", () => _ = new ParagraphSpec { Runs = [new TextRunSpec("x", Style())], Alignment = (HorizontalAlignment)Undefined } },
        { "TableCellSpec.Alignment", () => _ = new TableCellSpec { Content = "x", Alignment = (HorizontalAlignment)Undefined } },
        { "ImageSpec.Alignment", () => _ = new ImageSpec { Format = ImageFormat.Png, Bytes = [1, 2, 3, 4], Alignment = (HorizontalAlignment)Undefined } },
        { "PieChartSpec.Alignment", () => _ = new PieChartSpec { Diameter = 100, Slices = [new PieSlice { Label = "x", Value = 1 }], Alignment = (HorizontalAlignment)Undefined } },
        { "RunningBandSpec.Alignment", () => _ = new RunningBandSpec { Template = "x", Style = Style(), Alignment = (HorizontalAlignment)Undefined } },
    };

    [Theory]
    [MemberData(nameof(UndefinedEnumMembers))]
    public void UndefinedEnumMember_ThrowsAtConstruction(string member, Action construct)
    {
        var exception = Assert.Throws<ArgumentException>(construct);

        var memberName = member.Split('.')[1];
        Assert.Contains(memberName, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The counterpart that stops the check above from over-rejecting: every
    /// named member of every one of those enumerations must still construct.
    /// </summary>
    [Fact]
    public void EveryNamedMember_Constructs()
    {
        foreach (var face in Enum.GetValues<Standard14>())
        {
            Assert.Equal(face, new FontSpec { Kind = FontKind.Standard14, Standard14Face = face }.Standard14Face);
        }

        foreach (var style in Enum.GetValues<ListStyle>())
        {
            Assert.Equal(style, new ListSpec { Style = style, Items = [new ListItemSpec { Text = "x" }] }.Style);
        }

        foreach (var alignment in Enum.GetValues<HorizontalAlignment>())
        {
            Assert.Equal(alignment, new HeadingSpec { Text = "x", Level = 0, Alignment = alignment }.Alignment);
        }

        foreach (var conformance in Enum.GetValues<VellumPdf.Document.PdfConformance>())
        {
            var spec = new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content = [new PlainTextSpec { Text = "x" }],
                Conformance = conformance,
            };

            Assert.Equal(conformance, spec.Conformance);
        }

        foreach (var kind in Enum.GetValues<FontKind>())
        {
            var fontSpec = new FontSpec { Kind = kind, Standard14Face = Standard14.Helvetica, EmbeddedFontIndex = 0 };
            Assert.Equal(kind, fontSpec.Kind);
        }

        foreach (var format in Enum.GetValues<ImageFormat>())
        {
            var imageSpec = new ImageSpec { Format = format, Bytes = [1, 2, 3, 4] };
            Assert.Equal(format, imageSpec.Format);
        }
    }

    /// <summary>
    /// A <see langword="null"/> item in <see cref="DocumentSpec.Content"/>,
    /// which is the same shape of gap as an undefined enumeration member and
    /// was found by the same reasoning. Before the fix that closed it, a
    /// switch on a <see langword="null"/> value matched no
    /// <c>case ContentItemSpec-subtype</c> pattern and fell through, so the
    /// item passed construction, was skipped silently by
    /// <see cref="VellumPdfShowcase.Web.Generation.SpecRenderer.Render"/>, and
    /// surfaced only later and unhelpfully as "The document has no pages" once
    /// every other item had ALSO been skipped.
    /// </summary>
    /// <remarks>
    /// NOTE: this case moved here when <c>SpecCodeEmitterDefensiveThrowsTests</c>
    /// was folded away. That file had stopped calling the emitter it was named
    /// for, and its other two cases duplicated <c>SymmetryTests</c>' own
    /// construction proofs verbatim. This one had no duplicate anywhere, so it
    /// moved rather than being deleted with the file.
    /// </remarks>
    [Fact]
    public void NullContentItem_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content = [null!],
            });

        Assert.Contains("Content", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <see cref="EncryptionSpec.Permissions"/> is deliberately NOT routed
    /// through <see cref="SpecLimits.ValidateEnum{TEnum}"/>: a union of two
    /// named flags is not itself a named member, so
    /// <see cref="Enum.IsDefined{TEnum}(TEnum)"/> would reject a legitimate
    /// permission set. This pins that difference, so that unifying the two
    /// checks fails here rather than in a visitor's document.
    /// </summary>
    [Fact]
    public void PermissionsUnionOfNamedFlags_IsAccepted()
    {
        var union = PdfPermissions.Print | PdfPermissions.Copy;
        Assert.False(Enum.IsDefined(union));

        var spec = new EncryptionSpec
        {
            UserPassword = "user",
            OwnerPassword = "owner",
            Permissions = union,
        };

        Assert.Equal(union, spec.Permissions);
    }
}

/// <summary>
/// Every one of these values was previously unverified, because
/// every test that exercised a cap derived its own input from the constant
/// under test, so a test could pin the PRESENCE of a check without ever
/// pinning its MAGNITUDE. These tests assert the literal values instead, so
/// that changing a cap is a deliberate change to this file, not a silent
/// side effect of changing <see cref="SpecLimits"/> alone.
/// </summary>
public class SpecLimitsValuesAreVerifiedTests
{
    [Fact]
    public void Values_MatchTheDocumentedConstants()
    {
        Assert.Equal(20 * 1024 * 1024, SpecLimits.MaxAssetBytes);
        Assert.Equal(100_000, SpecLimits.MaxTextLength);
        Assert.Equal(35, SpecLimits.MaxLanguageTagLength);
        Assert.Equal(2048, SpecLimits.MaxUriLength);
        Assert.Equal(2_000, SpecLimits.MaxContentItems);
        Assert.Equal(2_000, SpecLimits.MaxTableRows);
        Assert.Equal(100, SpecLimits.MaxTableCellsPerRow);
        Assert.Equal(100, SpecLimits.MaxChartSlices);
        Assert.Equal(64, SpecLimits.MaxListNestingDepth);
        Assert.Equal(100, SpecLimits.MaxTableColumnWidths);
        Assert.Equal(1_000, SpecLimits.MaxParagraphRuns);
        Assert.Equal(2_000, SpecLimits.MaxListItems);
        Assert.Equal(100, SpecLimits.MaxListItemChildren);
        Assert.Equal(100, SpecLimits.MaxEmbeddedFonts);
        Assert.Equal(5_000, SpecLimits.MaxWalkedNodes);
        Assert.Equal(6_000, SpecLimits.MaxTotalTextLength);
        Assert.Equal(1, SpecLimits.MinPageDimensionPoints);
        Assert.Equal(20_000, SpecLimits.MaxPageDimensionPoints);
        Assert.Equal(1_000, SpecLimits.MaxFontSize);
        Assert.Equal(1_000, SpecLimits.MaxLeadingPoints);
        Assert.Equal(0, SpecLimits.MinHeadingLevel);
        Assert.Equal(5, SpecLimits.MaxHeadingLevel);
        Assert.Equal(10_000, SpecLimits.MaxEdgeInsetPoints);
        Assert.Equal(10_000, SpecLimits.MaxPieChartDiameterPoints);
        Assert.Equal(1_000, SpecLimits.MaxStrokeWidthPoints);
        Assert.Equal(10_000, SpecLimits.MaxIndentPoints);
        Assert.Equal(10_000, SpecLimits.MaxImageDimensionPoints);
        Assert.Equal(1_000, SpecLimits.MaxAngleMagnitudeRadians);
        Assert.Equal(15, SpecLimits.MaxIccComponentCount);
        Assert.Equal(1, SpecLimits.MinIccComponentCount);
    }
}

/// <summary>
/// The nine collection members were snapshotted at construction, but
/// the <c>byte[]</c> arrays behind three of them were not, so the byte-level
/// validators (magic-byte sniffing, the ICC header check) were bypassable
/// after construction by overwriting the caller's own array in place.
/// </summary>
public class ByteArraySnapshotTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void ImageSpec_Bytes_SnapshotsAliasedArray()
    {
        var aliased = new byte[] { 1, 2, 3 };
        var spec = new ImageSpec { Format = ImageFormat.Png, Bytes = aliased };

        aliased[0] = 99;

        Assert.Equal(1, spec.Bytes[0]);
    }

    [Fact]
    public void PdfAOutputIntentSpec_IccProfile_SnapshotsAliasedArray()
    {
        var aliased = (byte[])DocumentSpecSamples.SrgbIccProfileBytes().Clone();
        var originalFirstByte = aliased[0];
        var spec = new PdfAOutputIntentSpec { IccProfile = aliased, ComponentCount = 3, OutputConditionIdentifier = "x" };

        aliased[0] = unchecked((byte)(aliased[0] + 1));

        Assert.Equal(originalFirstByte, spec.IccProfile[0]);
    }

    [Fact]
    public void DocumentSpec_EmbeddedFontsEntry_SnapshotsAliasedArray()
    {
        var aliased = new byte[] { 1, 2, 3, 4 };
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new PlainTextSpec { Text = "x" }],
            EmbeddedFonts = [aliased],
        };

        aliased[0] = 99;

        Assert.Equal(1, spec.EmbeddedFonts[0][0]);
    }

    /// <summary>
    /// The concrete security scenario the snapshot exists to close: a
    /// validated PNG's bytes overwritten, after construction, with BMP magic
    /// bytes. Without the copy, <see cref="DocumentSpec.Content"/>'s
    /// magic-byte check would have already passed against the original PNG
    /// bytes, and the renderer would go on to hand BMP bytes to
    /// <c>PngImageLoader</c>, which is exactly what the sniffing exists to
    /// prevent.
    /// </summary>
    [Fact]
    public void DocumentSpec_ImageBytesOverwrittenAfterConstruction_CannotBypassFormatValidation()
    {
        byte[] validPngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
        var aliased = (byte[])validPngSignature.Clone();

        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new ImageSpec { Format = ImageFormat.Png, Bytes = aliased }],
        };

        // Overwrite the caller's own array with BMP magic bytes after construction.
        aliased[0] = 0x42;
        aliased[1] = 0x4D;

        var image = (ImageSpec)spec.Content[0];
        Assert.Equal(0x89, image.Bytes[0]);
        Assert.Equal(0x50, image.Bytes[1]);
    }
}
