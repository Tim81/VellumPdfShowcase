#Requires -Version 7
<#
.SYNOPSIS
    Fails the build when any branch in SpecCodeEmitter.cs is not exercised by
    the test suite.

.DESCRIPTION
    An emitter branch that no test reaches can be corrupted without turning
    the suite red; repeated manual review of this repository found such
    branches by hand, again and again. This script makes that failure
    automatic instead of depending on a reviewer noticing.

    It runs the test project through coverlet.MTP, coverlet's own
    Microsoft.Testing.Platform integration (NOT coverlet.collector, which is
    a VSTest data collector and never activates under the native MTP host
    this project runs), instrumenting every type in the
    VellumPdfShowcase.Web.Generation namespace (see NAMESPACE-WIDE
    INSTRUMENTATION FIX below for why this is the whole namespace, not only
    SpecCodeEmitter itself). It
    then reads the resulting Cobertura report directly and asserts, over
    EVERY class the report contains, per line, that:

      1. Every line was executed at least once (hits > 0).
      2. Every line coverlet marks as a branch point was taken on every one
         of its recorded outcomes (condition-coverage is exactly 100%).

    A single unreached outcome on a single line fails the whole run and is
    named individually. There is no percentage threshold to sit under: the
    property enforced is "every branch", not "most branches", so one new
    branch added to a large file cannot dilute its way past this gate the
    way it could dilute a project-wide percentage.

.NAMESPACE-WIDE INSTRUMENTATION FIX
    This gate previously instrumented by TYPE name
    ([...]SpecCodeEmitter.Web.Generation.SpecCodeEmitter*, matching every
    type whose name starts with "SpecCodeEmitter" regardless of which file
    declares it, which is how a partial class works) but asserted by FILE
    name (every <class> node whose filename attribute contained
    "SpecCodeEmitter.cs"). The two conditions look equivalent and are not:
    making SpecCodeEmitter partial and moving branches into a second file,
    SpecCodeEmitter.Probe.cs, kept them inside the type the instrument
    filter matched (so coverlet measured them; the run's own summary table
    showed branch coverage below 100%) while moving them outside the
    STRING "SpecCodeEmitter.cs" (so the file-name filter silently dropped
    them from the per-line assertion below, and the gate printed PASSED at
    exit 0 regardless). The empty-class-list guard did not catch this
    either, because the original, now-smaller SpecCodeEmitter.cs still
    contained classes of its own.

    The fix has two parts, both needed:

      1. Assert over EVERY class the report contains
         ($report.SelectNodes("//class"), no filename filter), rather than
         re-filtering by file name after the fact. Whatever the
         --coverlet-include pattern instruments is exactly what this script
         must hold to account; a second, independent filter over the same
         data can only drop coverage the first filter already scoped
         correctly, never add safety.
      2. Widen --coverlet-include from SpecCodeEmitter* to the whole
         VellumPdfShowcase.Web.Generation namespace, so a differently-named
         helper type in a different file (not a partial piece of
         SpecCodeEmitter at all) is instrumented too, rather than silently
         escaping the instrument filter itself the way SpecRenderer and
         ConformanceMapping did before this fix.

    Widening surfaced four defensive arms nothing in the public API can
    reach: SpecCodeEmitter.ImageLoaderName, SpecRenderer's own image-loader
    dispatch, SpecRenderer.ToTextStyle's font-kind dispatch, and
    SpecRenderer.AddContentItem's content-item dispatch, one `default` arm
    each, required by the compiler (a `switch` cannot prove a public
    enumeration or an unsealed hierarchy exhaustive from its named members
    alone) but unreachable in practice, for the same reason in each case:
    DocumentSpec's own construction-time validation already rejects
    anything that would reach it. A first attempt to keep the `default` arm
    and extract only its throw into a separate, narrowly
    [ExcludeFromCodeCoverage]-marked method did NOT bring the gate to 100%:
    coverlet attributes a `switch`'s own branch outcome ("which arm
    matched") to the ENCLOSING method regardless of where the code an arm
    runs lives, so the calling switch's own line still showed the
    default arm as never taken. Each of the four was rewritten instead so
    the unreachable arm does not exist as a branch at all: the two
    enumeration dispatches (image format, font kind) became lookup tables
    (a table has no branch outcome; every entry executes once,
    unconditionally, when the table itself is initialised) and the
    content-item dispatch, a `switch` STATEMENT rather than an expression,
    simply does not need a default arm to compile, so it was deleted. No
    [ExcludeFromCodeCoverage] appears anywhere in the
    VellumPdfShowcase.Web.Generation namespace as of this fix; see each
    rewritten member's own remark for why removing its default arm is safe.

    Reproduced directly: with SpecCodeEmitter split across two files as
    above and one new branch left deliberately untaken, the PRE-fix script
    printed "Branch coverage gate PASSED" at exit 0 while its own summary
    table showed branch coverage under 100%; the POST-fix script fails,
    naming the untaken branch in SpecCodeEmitter.Probe.cs by file and line.

.PROPERTY ENFORCED
    Reachability, not observability. A line coverlet marks fully covered
    was executed by some test; it does not follow that the test's
    assertions depend on what that line produced, and this script cannot
    tell the two apart; the round-trip test is what supplies that
    property, not this gate. SpecRoundTripTests compares emitted-and-executed bytes
    against SpecRenderer's own output for every sample this script's runs
    exercise, which is what supplies the observability property this gate
    does not. The two are complementary, not redundant: this gate would
    have caught the eight branches an earlier review found with zero samples
    reaching them at all; it would not, by itself, have caught the seven
    that executed while nothing asserted on their output.

.KNOWN EXCLUSION
    None. No member anywhere in the
    VellumPdfShowcase.Web.Generation namespace carries
    [ExcludeFromCodeCoverage]; see NAMESPACE-WIDE INSTRUMENTATION FIX above for how the four
    defensive arms that previously needed it (one of them, on
    SpecCodeEmitter.ImageLoaderName, via exactly this attribute) were
    rewritten to have no unreachable branch to exclude in the first place.
    Every line this gate sees is unconditionally live. If a future change
    reintroduces a genuinely unreachable defensive arm that IS protected by
    the symmetry guard (VellumPdfShowcase.Tests.SymmetryTests) and cannot be
    designed away the same way, [ExcludeFromCodeCoverage] on the smallest
    possible extracted member remains the correct tool, per the failure
    message below. Do NOT treat "eliminate the branch" as the only
    acceptable remedy for a genuinely unreachable defensive arm: that
    incentive itself once caused two live divergences between
    SpecRenderer and SpecCodeEmitter, because eliminating an arm from one
    side is not the same act as eliminating it from both, and this gate
    could not see the difference; see THE INCENTIVE THIS GATE CREATES below.

.THE INCENTIVE THIS GATE CREATES
    The namespace-wide instrumentation fix above correctly closed the
    specific branches it found, but left a standing incentive: "no unreached
    branch" reads, to a future change, as "delete whichever arm is
    unreached", and a defensive arm can become unreached on ONE side of the
    SpecRenderer / SpecCodeEmitter pair without the same change touching the
    other side at all. That is exactly what produced the ContentItemSpec and
    FontKind divergences: SpecRenderer's own
    defensive arms for an unrecognised ContentItemSpec and an out-of-range
    FontKind were removed (or never added) while SpecCodeEmitter's
    equivalents survived, because some test happened to reach
    SpecCodeEmitter's arm but nothing symmetric existed for SpecRenderer's.
    This gate stayed at 100% throughout, on both sides, because each side's
    OWN branches were each reached by something; nothing here compares the
    two sides to each other at all, and nothing here ever will, because
    that is not a property of one method's own branches.

    The fix has two parts. First, this file's REMEDY GUIDANCE, printed on
    failure, no longer names "redesign the branch away" as the first or only
    acceptable remedy for a defensive arm; it names the symmetry guard as
    the tool that actually protects a defensive arm's CORRECTNESS (whether
    both sides agree about the input that reaches it), which branch coverage
    was never positioned to check regardless of how it is met. Second,
    VellumPdfShowcase.Tests.SymmetryTests.RenderAndEmitAgreeOnArgumentRejection
    is now the PRIMARY guard for the round-trip invariant, run over every
    DocumentSpecSamples entry plus a corpus of adversarial specifications;
    this gate remains a secondary, complementary check (see PROPERTY
    ENFORCED) that a line is reachable at all, never a check that the two
    consumers agree about what running it produces.

.KNOWN LIMITATION: COMPILER-GENERATED CLOSURES ARE INVISIBLE HERE
    Coverlet's Cobertura report OMITS compiler-generated CLOSURE classes
    (<>c and <>c__DisplayClassN_M, generated for a lambda that captures no
    or some outer state) entirely; a branch inside a lambda body is
    therefore invisible to this gate regardless of whether any test reaches
    it. Demonstrated on SHIPPED code, twice. First, empirically: inspecting
    a Cobertura report instrumented over this exact namespace lists ten
    classes, three of them compiler-generated ITERATOR state machines
    (SpecCodeEmitter/<CollectTextStyles>d__5 and its two overloads) and ZERO
    compiler-generated CLOSURE classes, despite the emitter shipping
    branch-bearing lambdas at the time (EmitPermissions' Where predicate at
    what was then SpecCodeEmitter.cs line 1121, EmitPageSizeInitializer's
    Where predicate, EmitUsings' Content.OfType<ImageSpec>().Any() and
    EmitRequiredAssetsComment's identical check). Iterator state machines
    ARE caught by this gate; closures are NOT, for reasons internal to how
    coverlet resolves a sequence point's declaring type, not anything this
    script controls. Second, directly: appending a never-false conjunct to
    EmitPermissions' own predicate left this gate at exit 0 reporting 100%
    on a version of the emitter whose outcome that conjunct never actually
    took. No coverlet option to instrument closures was found (its GitHub
    issue tracker and documentation were searched; none is documented as of
    this writing). The mitigation actually in place here is NOT a gate
    setting: keep a lambda body itself free of branches wherever the
    ImageLoaders/ToTextStyle/ImageLoaderName pattern applies (a lookup table
    or a two-way comparison has no branch for either coverlet or this gate
    to miss), and rely on VellumPdfShowcase.Tests.SymmetryTests and
    SpecRoundTripTests, not this gate, for a lambda body's correctness. This
    is a genuine, unclosed gap in what this gate can promise; it is recorded
    here, rather than left for a reader to discover by surprise, because a
    gate that silently cannot see an entire category of branch is worse than
    one that says so.

.KNOWN LIMITATION: SCOPE DOES NOT YET COVER MODEL OR COMPONENTS.PAGES
    This gate instruments VellumPdfShowcase.Web.Generation only.
    VellumPdfShowcase.Web.Model carries the content walk and every member
    validator both real consumers depend on before doing anything else,
    and VellumPdfShowcase.Web.Components.Pages will carry whatever the UI
    later adds; neither is instrumented today, so a branch
    added to either is as invisible to this gate as one in a closure is.
    Widening --coverlet-include to VellumPdfShowcase.Web.Model.* was tried
    directly, and RE-MEASURED against the tree as it now stands rather than
    left at an older figure: the suite reaches 96.68% line and 87.09% branch
    coverage there, 48 of 372 branches untaken, across 13 types. Two types
    hold more than half of them. ImageSignature accounts for 17, and
    DocumentSpec.ContentWalkState for 11.

    NOTE the shape of those gaps, since it decides what closing them is worth.
    The 28 in those two types are short-circuit operands inside multi-condition
    expressions. The JPEG arm of the magic-byte sniff takes 3 of its 6 branch
    outcomes and the TIFF arm 5 of 16, because a test supplying correct bytes
    and a test supplying a wholly wrong format between them never produce a
    header whose first byte matches and whose second does not. The walk's
    `!TryVisit() || !TryAddCharacters(...)` chains are the same pattern.
    Reaching those operands needs input contrived to fail at one specific
    position, which is worth doing for the sniff, the control standing between
    attacker-chosen bytes and a clean-room parser, and worth much less for the
    walk, where either operand failing produces the identical refusal.

    Of the remaining 20, twelve are the `value is null ? null : Validate(...)`
    and `value ?? throw ArgumentNullException` pattern on an optional or
    required member whose null case no test pairs with it. Those are cheap to
    close and carry no argument against closing them. The last eight are
    scattered singletons: an ICC colour-space switch, a URI parse, two
    empty-collection ternaries.

    Turning the widened include on without first closing those gaps would fail
    this gate outright, so the include remains Generation-only and this
    measurement is recorded here as the reason, not silently deferred.
    Widening to Model is real, tractable follow-up work; widening to
    Components.Pages first needs a UI test harness (bUnit or equivalent)
    this project does not yet have, since VellumPdfShowcase.Web.Components.Pages.Smoke
    is Blazor component code with JS interop, not pure logic.

    What IS in place now: the SENTINEL check below, so that migrating a
    round-trip-relevant type OUT of the instrumented namespace (rather than
    merely failing to instrument a namespace that was never in scope) is
    caught immediately rather than silently narrowing what this gate
    protects.

.PARAMETER Configuration
    Build configuration to run the test project under. Defaults to Debug,
    matching the invocation this repository uses to run the suite.

.EXAMPLE
    pwsh eng/check-emitter-branch-coverage.ps1

.NOTES
    Cost, measured on this machine: about five seconds end to end, roughly
    one second more than running the suite with no coverage collection at
    all (coverlet.MTP instruments ahead of time, once, before the run
    starts). This is a separate invocation from the plain
    `dotnet run --project tests/.../VellumPdfShowcase.Tests.csproj -c Debug`
    that is the normal way to run the suite on this machine:
    coverlet.MTP has no built-in threshold gate (see its README,
    "Known Limitations: Threshold validation is not yet supported"), so the
    assertion in this script is a necessary second step, not folded into
    the test run itself.

    REQUIRED, NOT YET AUTOMATED: no CI workflow exists in this repository
    yet, deployment being when one would normally be added. Until one
    exists and runs this script, it is
    the CONTRIBUTOR's own responsibility to run it by hand, alongside
    `dotnet run --project tests/.../VellumPdfShowcase.Tests.csproj -c Debug`,
    as part of the verification sequence for ANY change that touches
    src/VellumPdfShowcase.Web/Generation, not merely SpecCodeEmitter.cs
    itself: no automation currently enforces that either script runs at all.
    Wire this script into the step 9 deploy workflow alongside the plain
    test invocation once that workflow exists, so this note can be deleted
    once it stops being true.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot 'tests/VellumPdfShowcase.Tests/VellumPdfShowcase.Tests.csproj'
$targetNamespaceLabel = 'VellumPdfShowcase.Web.Generation'
$coverletInclude = '[VellumPdfShowcase.Web]VellumPdfShowcase.Web.Generation.*'
$filePrefix = 'emitter-branch'
$resultsDir = Join-Path $repoRoot "tests/VellumPdfShowcase.Tests/bin/$Configuration/net10.0/TestResults"

if (-not (Test-Path $testProject)) {
    Write-Error "Test project not found at $testProject."
    exit 1
}

# Remove stale reports first, so a run that fails to produce a fresh one
# cannot be mistaken for evidence of a previous pass.
if (Test-Path $resultsDir) {
    Get-ChildItem -Path $resultsDir -Filter "$filePrefix.coverage.cobertura.*.xml" -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

Write-Host "Running the suite with branch coverage instrumented over $targetNamespaceLabel..."

& dotnet run --project $testProject -c $Configuration -- `
    --coverlet `
    --coverlet-output-format cobertura `
    --coverlet-include $coverletInclude `
    --coverlet-file-prefix $filePrefix
$testExitCode = $LASTEXITCODE

if ($testExitCode -ne 0) {
    Write-Error "The test run itself failed (exit $testExitCode). Branch coverage was not evaluated."
    exit $testExitCode
}

$reportFile = Get-ChildItem -Path $resultsDir -Filter "$filePrefix.coverage.cobertura.*.xml" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $reportFile) {
    Write-Error ("No Cobertura report was produced under $resultsDir. Confirm coverlet.MTP is referenced by " +
        "tests/VellumPdfShowcase.Tests/VellumPdfShowcase.Tests.csproj and that '--coverlet' was accepted by the test host.")
    exit 1
}

[xml]$report = Get-Content -Raw -Path $reportFile.FullName

# Assert over EVERY class the report contains. There is no
# second, file-name-based filter here any more: --coverlet-include above is
# the only scope decision this script makes, so whatever it instrumented is
# exactly what gets asserted on. A class the include pattern did not match
# never appears in the report at all, so no filter is needed to exclude it;
# a second filter here could only DROP classes the first one correctly let
# through, which is exactly how the previous version of this gate let
# SpecCodeEmitter.Probe.cs's own branches escape assertion while still being
# instrumented and counted in the run's own summary table.
$targetClasses = $report.SelectNodes('//class')
if ($targetClasses.Count -eq 0) {
    Write-Error "The coverage report contains no classes at all. Confirm --coverlet-include ($coverletInclude) still matches $targetNamespaceLabel."
    exit 1
}

# Migration out of the instrumented namespace,
# distinct from a namespace never being in scope, is otherwise silent. A
# round-trip-relevant type moved out of VellumPdfShowcase.Web.Generation (to
# a differently-named file, a nested namespace, or elsewhere entirely)
# without updating $coverletInclude to match would simply stop appearing in
# $targetClasses, and every check below would keep passing over whatever
# remained, with nothing to say the omission happened at all. Every type
# named here is expected, unconditionally, to appear among $targetClasses;
# a reader adding a new round-trip-relevant type to this namespace should
# add it here too, so ITS OWN future migration out of scope is caught the
# same way.
$expectedTypes = @(
    'VellumPdfShowcase.Web.Generation.SpecRenderer',
    'VellumPdfShowcase.Web.Generation.SpecCodeEmitter',
    'VellumPdfShowcase.Web.Generation.ConformanceMapping',
    'VellumPdfShowcase.Web.Generation.SpecAssets'
)
# Nested types and compiler-generated state machines appear under names such as
# '...SpecCodeEmitter/Emitter' and '...SpecCodeEmitter/<CollectTextStyles>d__5'.
# Both belong to their enclosing type for roster purposes, so the segment before
# the first '/' is what is compared.
$seenTypeNames = $targetClasses | ForEach-Object { ($_.name -split '/')[0] } | Sort-Object -Unique
$missingTypes = $expectedTypes | Where-Object { $seenTypeNames -notcontains $_ }
if ($missingTypes.Count -gt 0) {
    Write-Error (
        "The coverage report is missing $($missingTypes.Count) type(s) this gate expects to find in " +
        "${targetNamespaceLabel}: $($missingTypes -join ', '). Each was either renamed, moved to a namespace " +
        "`$coverletInclude` ($coverletInclude) no longer matches, or genuinely deleted. If it moved " +
        "deliberately, update `$coverletInclude` (and `$expectedTypes` in this script) to follow it; a type " +
        "silently leaving this gate's scope is exactly the failure mode this check exists to catch."
    )
    exit 1
}

# The check above is one-directional, and a subset check cannot see a type
# ARRIVING. A helper extracted out of SpecCodeEmitter into a new type in this
# same namespace is instrumented and branch-checked immediately, but it is not
# on the roster, so ITS own later migration out of scope would go unnoticed:
# the very failure the roster exists to prevent, reintroduced by the extraction
# that made the roster stale. Requiring equality rather than containment means
# a new type has to be added here deliberately.
$surplusTypes = $seenTypeNames | Where-Object { $expectedTypes -notcontains $_ }
if ($surplusTypes.Count -gt 0) {
    Write-Error (
        "The coverage report holds $($surplusTypes.Count) type(s) in ${targetNamespaceLabel} that this gate's " +
        "roster does not name: $($surplusTypes -join ', '). A type this gate covers must be on the roster, so " +
        "that its own future migration out of scope is caught. Add it to `$expectedTypes` in this script."
    )
    exit 1
}

$findings = [System.Collections.Generic.List[string]]::new()

foreach ($class in $targetClasses) {
    $location = "$($class.filename) ($($class.name))"
    $lineNodes = $class.SelectNodes('lines/line')
    foreach ($line in $lineNodes) {
        $number = $line.number
        $hits = [int]$line.hits

        if ($hits -eq 0) {
            $findings.Add("$location`:$number — never executed (0 hits)")
            continue
        }

        $isBranch = $line.branch -eq 'True'
        $conditionCoverage = $line.'condition-coverage'
        if ($isBranch -and $conditionCoverage -and -not $conditionCoverage.StartsWith('100%')) {
            $findings.Add("$location`:$number — branch not fully taken, $conditionCoverage")
        }
    }
}

$findings = $findings | Sort-Object -Unique

if ($findings.Count -gt 0) {
    Write-Host ''
    Write-Host "Branch coverage gate FAILED: $($findings.Count) location(s) in $targetNamespaceLabel were not fully reached."
    foreach ($finding in $findings) {
        Write-Host "  - $finding"
    }

    Write-Host ''
    Write-Host 'The expected remedy is a DocumentSpecSamples case that reaches the missing outcome; add one.'
    Write-Host ''
    Write-Host '"Eliminate the branch" is NOT the only acceptable remedy, and treating it as'
    Write-Host 'the default one is what deleted a live defensive arm from SpecRenderer while SpecCodeEmitter kept'
    Write-Host 'its own equivalent, with this gate green on both sides throughout (the ContentItemSpec and FontKind divergences). A branch'
    Write-Host 'that is a genuine, symmetric defensive arm on BOTH SpecRenderer and SpecCodeEmitter, and that is'
    Write-Host 'covered by VellumPdfShowcase.Tests.SymmetryTests asserting the two agree about the input that'
    Write-Host 'reaches it, is an ACCEPTABLE outcome for this gate to flag, not a defect to design away; add the'
    Write-Host 'missing DocumentSpecSamples case (or, if the branch is provably unreachable from the public API,'
    Write-Host 'a targeted symmetry-guard case proving both sides would agree if it were ever reached) rather'
    Write-Host 'than deleting the arm to satisfy this gate alone.'
    Write-Host ''
    Write-Host 'If the branch belongs to a switch default arm that the compiler requires but the public API'
    Write-Host 'makes genuinely, provably unreachable (a closed set enforced at construction, the way'
    Write-Host 'DocumentSpec.Content and FontSpec.Kind now are), consider redesigning it away entirely, the way'
    Write-Host 'SpecRenderer.ImageLoaders and SpecRenderer.ToTextStyle do: a lookup table or a two-way comparison'
    Write-Host 'has no unreachable branch to exclude, unlike an extracted-and-excluded throw, whose ENCLOSING'
    Write-Host 'switch still shows the default arm as untaken regardless of where the throw itself lives. Only'
    Write-Host 'when eliminating the branch would also remove a defensive arm the symmetry guard still needs is'
    Write-Host '[ExcludeFromCodeCoverage] the right tool instead, on the smallest possible extracted member, with'
    Write-Host 'a comment stating why: exclusion is the last resort here, not the default remedy for a coverage'
    Write-Host 'gap, and neither is deletion.'
    exit 1
}

Write-Host "Branch coverage gate PASSED: every reachable branch in $targetNamespaceLabel was taken at least once."
exit 0
