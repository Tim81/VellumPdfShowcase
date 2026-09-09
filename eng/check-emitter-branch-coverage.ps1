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
    CLAUDE.md), instrumenting only the types declared in SpecCodeEmitter.cs.
    It then reads the resulting Cobertura report directly and asserts, per
    line, that:

      1. Every line was executed at least once (hits > 0).
      2. Every line coverlet marks as a branch point was taken on every one
         of its recorded outcomes (condition-coverage is exactly 100%).

    A single unreached outcome on a single line fails the whole run and is
    named individually. There is no percentage threshold to sit under: the
    property enforced is "every branch", not "most branches", so one new
    branch added to a large file cannot dilute its way past this gate the
    way it could dilute a project-wide percentage.

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
    SpecCodeEmitter.ImageLoaderName carries [ExcludeFromCodeCoverage] on a
    default arm that C# requires (ImageFormat is a public enum, not
    provably exhaustive to the compiler) but that DocumentSpec.Content
    already makes unreachable from the public API, by rejecting any
    ImageSpec whose declared Format does not match its own byte signature
    before construction can succeed. See the remark on that method. This is
    the one line in the file this gate does not see; every other line is
    unconditionally live.

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
    the test run itself. Run this script before opening a pull request that
    touches SpecCodeEmitter.cs, and wire it into the step 9 deploy workflow
    alongside the plain test invocation once that workflow exists.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot 'tests/VellumPdfShowcase.Tests/VellumPdfShowcase.Tests.csproj'
$targetFile = 'SpecCodeEmitter.cs'
$coverletInclude = '[VellumPdfShowcase.Web]VellumPdfShowcase.Web.Generation.SpecCodeEmitter*'
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

Write-Host "Running the suite with branch coverage instrumented over $targetFile..."

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

$targetClasses = $report.SelectNodes("//class[contains(@filename, '$targetFile')]")
if ($targetClasses.Count -eq 0) {
    Write-Error "The coverage report contains no class from $targetFile. Confirm --coverlet-include still matches its namespace."
    exit 1
}

$findings = [System.Collections.Generic.List[string]]::new()

foreach ($class in $targetClasses) {
    $lineNodes = $class.SelectNodes('lines/line')
    foreach ($line in $lineNodes) {
        $number = $line.number
        $hits = [int]$line.hits

        if ($hits -eq 0) {
            $findings.Add("$targetFile`:$number — never executed (0 hits), in $($class.name)")
            continue
        }

        $isBranch = $line.branch -eq 'True'
        $conditionCoverage = $line.'condition-coverage'
        if ($isBranch -and $conditionCoverage -and -not $conditionCoverage.StartsWith('100%')) {
            $findings.Add("$targetFile`:$number — branch not fully taken, $conditionCoverage, in $($class.name)")
        }
    }
}

$findings = $findings | Sort-Object -Unique

if ($findings.Count -gt 0) {
    Write-Host ''
    Write-Host "Branch coverage gate FAILED: $($findings.Count) location(s) in $targetFile were not fully reached."
    foreach ($finding in $findings) {
        Write-Host "  - $finding"
    }

    Write-Host ''
    Write-Host 'Add a DocumentSpecSamples case that reaches the missing outcome, or, if the branch is'
    Write-Host 'provably unreachable from the public API, extract the smallest possible helper method and'
    Write-Host 'mark it [ExcludeFromCodeCoverage] with a comment stating why, following the precedent on'
    Write-Host 'SpecCodeEmitter.ImageLoaderName.'
    exit 1
}

Write-Host "Branch coverage gate PASSED: every reachable branch in $targetFile was taken at least once."
exit 0
