#Requires -Version 7
<#
.SYNOPSIS
    Fails the build when any branch in SpecCodeEmitter.cs is not exercised by
    the test suite.

.DESCRIPTION
    An emitter branch that no test reaches can be corrupted without turning
    the suite red; six review cycles of this repository found such branches
    by hand, repeatedly. This script makes that failure automatic instead of
    depending on a reviewer noticing.

    It runs the test project through coverlet.MTP, coverlet's own
    Microsoft.Testing.Platform integration (NOT coverlet.collector, which is
    a VSTest data collector and never activates under the native MTP host
    this project runs; see the "dotnet test is broken here" note in
    CLAUDE.md), instrumenting every type in the
    VellumPdfShowcase.Web.Generation namespace (see CYCLE 7 FIX below for
    why this is the whole namespace, not only SpecCodeEmitter itself). It
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

.CYCLE 7 FIX
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
    tell the two apart (see plan section 3.4.0.2 and CLAUDE.md's round-trip
    invariant). SpecRoundTripTests compares emitted-and-executed bytes
    against SpecRenderer's own output for every sample this script's runs
    exercise, which is what supplies the observability property this gate
    does not. The two are complementary, not redundant: this gate would
    have caught the eight branches cycle 6 found with zero samples
    reaching them at all; it would not, by itself, have caught the seven
    that executed while nothing asserted on their output.

.KNOWN EXCLUSION
    None. As of cycle 7, no member anywhere in the
    VellumPdfShowcase.Web.Generation namespace carries
    [ExcludeFromCodeCoverage]; see CYCLE 7 FIX above for how the four
    defensive arms that previously needed it (one of them, on
    SpecCodeEmitter.ImageLoaderName, via exactly this attribute) were
    rewritten to have no unreachable branch to exclude in the first place.
    Every line this gate sees is unconditionally live. If a future change
    reintroduces a genuinely unreachable defensive arm that cannot be
    designed away the same way, [ExcludeFromCodeCoverage] on the smallest
    possible extracted member remains the correct tool, per the failure
    message below; it is documented here as the fallback, not because one
    is in use today.

.PARAMETER Configuration
    Build configuration to run the test project under. Defaults to Debug,
    matching the "dotnet test is broken here" invocation documented in
    CLAUDE.md.

.EXAMPLE
    pwsh eng/check-emitter-branch-coverage.ps1

.NOTES
    Cost, measured on this machine: about five seconds end to end, roughly
    one second more than running the suite with no coverage collection at
    all (coverlet.MTP instruments ahead of time, once, before the run
    starts). This is a separate invocation from the plain
    `dotnet run --project tests/.../VellumPdfShowcase.Tests.csproj -c Debug`
    documented in CLAUDE.md as the normal way to run the suite on this
    machine: coverlet.MTP has no built-in threshold gate (see its README,
    "Known Limitations: Threshold validation is not yet supported"), so the
    assertion in this script is a necessary second step, not folded into
    the test run itself.

    REQUIRED, NOT YET AUTOMATED: no CI workflow exists in this repository as
    of cycle 7 (deployment, which is when one would normally be added, is a
    later step of the plan). Until one exists and runs this script, it is
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

# Cycle 7 fix: assert over EVERY class the report contains. There is no
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
    Write-Host 'If the branch belongs to a switch default arm that the compiler requires but the public API'
    Write-Host 'makes unreachable, first consider redesigning it away entirely, the way SpecRenderer.ImageLoaders'
    Write-Host 'and SpecRenderer.ToTextStyle do: a lookup table or a two-way comparison has no unreachable branch'
    Write-Host 'to exclude, unlike an extracted-and-excluded throw, whose ENCLOSING switch still shows the'
    Write-Host 'default arm as untaken regardless of where the throw itself lives. Only if the branch is'
    Write-Host 'PROVABLY unreachable AND cannot be designed away is [ExcludeFromCodeCoverage] the right tool,'
    Write-Host 'and even then only on the smallest possible extracted member, with a comment stating why:'
    Write-Host 'exclusion is the last resort here, not the default remedy for a coverage gap.'
    exit 1
}

Write-Host "Branch coverage gate PASSED: every reachable branch in $targetNamespaceLabel was taken at least once."
exit 0
