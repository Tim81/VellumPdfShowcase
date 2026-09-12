// Drives every route of the published site in a real browser and fails when one
// of them is broken.
//
// This exists because of a gap nothing else in this repository closes. The test
// suite runs on desktop .NET; the site runs on browser WebAssembly. A capability
// that encrypts passed every test and failed in every visitor's tab with
// "Algorithm 'Aes' is not supported on this platform", because AES is absent from
// the browser runtime. Two other defects in the same layer were also invisible to
// the suite: a component parameter that rendered the text "_code" instead of the
// generated snippet, because an unprefixed attribute value on a string-typed
// parameter is a literal, and a null reference on the pages of capabilities the
// library does not implement yet. All three are caught here.
//
// It runs against the PUBLISHED output, trimmed, rather than a development build,
// so a defect introduced by trimming is in scope too.
//
// Usage:
//   node eng/smoke/smoke.mjs <published-wwwroot-directory>
//
// Exit code 0 when every route passes, 1 otherwise. Each failure names the route
// and what was wrong with it.
//
// NOTE every assertion here has been made to fire against a deliberately broken
// copy of the published output, because a check that has never failed is a check
// nobody has tested. Measured, one mutation at a time, against a real publish:
//
//   a shipped asset deleted        the missing-asset report, the 404 in the
//                                  console, and the page's own legible error
//   the runtime files removed      no <h1> appeared, and the run stops after the
//                                  first route rather than waiting out sixteen
//                                  more timeouts
//   the error bar forced visible   the Blazor error bar is showing, on all 17
//   a console error injected       the console error, on all 17
//   a console warning injected     the console warning, on all 17
//   encryption marked available    the AES message, and a missing gallery card
//   a literal component binding    the code panel does not hold C#
//   the demonstrability guard      the null reference on both planned routes
//     removed
//   every preview a corrupt PDF    the preview has no %%EOF, on all 9 previews
//   every preview hidden by CSS    every match of iframe is hidden, on all 9
//   one route serving another      the heading does not match the route
//     capability's document
//   the manifest emptied           fewer routes than the floor, before launching
//   every preview at opacity 0     every match is hidden or positioned away
//   every preview off screen       the same
//   one blank PDF everywhere,      routes showing the same preview as each
//     one snippet everywhere,        other, the same snippet as each other, and
//     three cards where 11 belong    fewer cards than the catalogue has entries
//
// A second review found the last three after the first eight were merged, each
// passing 17 of 17 at the time. The lessons are recorded rather than the fixes
// alone: presence is not visibility, visibility is not what Playwright's
// isVisible() means, an element is not its contents, a well-formed PDF is not
// the right PDF, and a page that renders is not the page that was asked for.
//
//   one real document everywhere, routes showing the same preview as each other,
//     /ID randomised per call        caught because the fingerprint is what the
//                                    page DRAWS, not its bytes: the library
//                                    stamps a fresh /ID into every document, so
//                                    hashing bytes fingerprinted the generation
//                                    and never the document
//   a blank but valid page          the preview draws nothing
//   every preview clipped to
//     nothing by an ancestor        every match is hidden or positioned away
//
// KNOWN GAPS, stated rather than implied, and re-checked by each review. Content
// that draws the WRONG thing passes, so long as it draws something and differs
// from what the other routes draw. Nothing here clicks anything, so the runtime
// smoke page's own buttons are never pressed and the playground's controls are
// never operated. Occlusion and clip-path WERE gaps and are now closed by a hit
// test; an element covered at its centre but visible at its edges would still
// pass.
//
// The unbroken site passes all 17 before and after each mutation.

import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { createReadStream } from 'node:fs';
import { extname, join, normalize, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { analysePreview } from './pdf-analysis.mjs';

const here = fileURLToPath(new URL('.', import.meta.url));

const root = resolve(process.argv[2] ?? '');
if (!process.argv[2]) {
  console.error('usage: node eng/smoke/smoke.mjs <published-wwwroot-directory>');
  process.exit(2);
}

// How long a route may take to finish generating before it is called stuck. The
// slowest capability measured on a development machine is well under a second;
// the margin is for a loaded continuous integration runner.
const SETTLE_TIMEOUT_MS = 30_000;

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.wasm': 'application/wasm',
  '.dat': 'application/octet-stream',
  '.blat': 'application/octet-stream',
  '.dll': 'application/octet-stream',
  '.pdb': 'application/octet-stream',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.ttf': 'font/ttf',
  '.icc': 'application/vnd.iccprofile',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.bmp': 'image/bmp',
  '.tif': 'image/tiff',
  '.gif': 'image/gif',
  '.svg': 'image/svg+xml',
  '.md': 'text/markdown; charset=utf-8',
  '.txt': 'text/plain; charset=utf-8',
  '.ico': 'image/x-icon',
};

/**
 * Serves the published output with the single-page fallback the real host needs.
 * A deep route such as /capability/tables is not a file, and without the fallback
 * it would 404 before the application ever started, so every deep route would
 * "fail" for a reason that has nothing to do with the site.
 */
function serve(directory) {
  const missing = [];

  const server = createServer(async (request, response) => {
    const requested = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);

    // Contain the path inside the served directory, so a traversal cannot read
    // the machine this runs on.
    const candidate = resolve(join(directory, normalize(requested)));
    if (candidate !== directory && !candidate.startsWith(directory + sep)) {
      response.writeHead(403).end();
      return;
    }

    let file = candidate;
    let info = await stat(file).catch(() => null);

    if (info?.isDirectory()) {
      file = join(file, 'index.html');
      info = await stat(file).catch(() => null);
    }

    if (!info?.isFile()) {
      // A path with no extension is a client route; anything else is a genuinely
      // missing file and is recorded, because a missing asset is a real defect.
      if (extname(requested) === '') {
        file = join(directory, 'index.html');
      } else {
        if (requested !== '/favicon.ico') {
          missing.push(requested);
        }

        response.writeHead(404).end();
        return;
      }
    }

    response.writeHead(200, {
      'Content-Type': MIME[extname(file).toLowerCase()] ?? 'application/octet-stream',
      'Cache-Control': 'no-store',
    });

    createReadStream(file).pipe(response);
  });

  return { server, missing };
}

/**
 * Whether an element is actually shown to a visitor, evaluated in the page.
 *
 * NOTE this deliberately does not use Playwright's isVisible(), which means "has
 * a box and is not visibility:hidden" and models neither opacity nor position. A
 * review hid every preview with one line of `opacity: 0` and the harness reported
 * seventeen of seventeen passing. A later one clipped every preview to nothing
 * with `height: 0; overflow: hidden`, which has a box, a clean ancestor chain,
 * and is equally invisible.
 *
 * Horizontally the element must fall within the viewport, because this site is
 * not meant to scroll sideways, so `left: 12000px` is as hidden as `-12000px`.
 * Vertically it need only fall within the document, because content below the
 * fold is shown: testing the viewport there reported the gallery's own cards as
 * hidden.
 *
 * KNOWN GAP: an element covered by another is not modelled.
 */
function isShown(element) {
  const box = element.getBoundingClientRect();
  if (box.width < 2 || box.height < 2) {
    return false;
  }

  if (box.right < 0 || box.left > innerWidth) {
    return false;
  }

  if (box.bottom + scrollY < 0 || box.top + scrollY > document.documentElement.scrollHeight) {
    return false;
  }

  let clip = box;

  for (let node = element; node instanceof Element; node = node.parentElement) {
    const style = getComputedStyle(node);

    if (style.visibility === 'hidden' || style.display === 'none' || Number(style.opacity) < 0.05) {
      return false;
    }

    // A filter can make something transparent without touching the opacity
    // property, and getComputedStyle still reports an opacity of 1. One line of
    // `filter: opacity(0)` hid every preview on the site while the harness
    // reported seventeen routes of seventeen passing.
    if (/opacity\(\s*0?(\.0+)?\s*\)/.test(style.filter)) {
      return false;
    }

    // An ancestor that clips its overflow can hide a descendant entirely while
    // the descendant keeps its own box and a clean chain above it.
    if (node !== element && style.overflow !== 'visible') {
      const bounds = node.getBoundingClientRect();
      const left = Math.max(clip.left, bounds.left);
      const top = Math.max(clip.top, bounds.top);
      const right = Math.min(clip.right, bounds.right);
      const bottom = Math.min(clip.bottom, bounds.bottom);

      if (right - left < 2 || bottom - top < 2) {
        return false;
      }

      clip = new DOMRect(left, top, right - left, bottom - top);
    }
  }

  // Finally, ask the browser what is actually AT the element's position. This is
  // the only test here that does not reason about properties one at a time, so it
  // catches what property inspection misses: an element clipped away by
  // `clip-path`, and an element covered by something opaque on top of it, which
  // was a gap this file previously only declared.
  //
  // Only meaningful when the point is inside the viewport, so content below the
  // fold is exempted rather than failed.
  const x = clip.left + clip.width / 2;
  const y = clip.top + clip.height / 2;

  if (x >= 0 && y >= 0 && x <= innerWidth && y <= innerHeight) {
    // NOTE the accepted answers are the element itself or something INSIDE it.
    // Accepting an ancestor as well, which looks harmless, defeats the whole
    // test: a full-page overlay drawn with ::after hit-tests as <body>, and body
    // contains everything, so every route passed.
    const hit = document.elementFromPoint(x, y);
    if (hit === null || !(hit === element || element.contains(hit))) {
      return false;
    }
  }

  return true;
}

/**
 * Waits until the page has stopped saying it is working, so that a route is not
 * judged while it is still legitimately mid-generation.
 */
async function settle(page) {
  const deadline = Date.now() + SETTLE_TIMEOUT_MS;

  while (Date.now() < deadline) {
    const working = await page
      .locator('text=/^(Generating…|Validating…|Preparing…)$/')
      .count()
      .catch(() => 0);

    if (working === 0) {
      return true;
    }

    await page.waitForTimeout(250);
  }

  return false;
}

// The manifest is held to the catalogue by RouteManifestTests, but that test runs
// in a different continuous integration job, so on its own this harness would
// report success for an empty list. A floor here means the browser gate cannot
// certify itself after the manifest has been gutted.
const MINIMUM_ROUTES = 10;

async function main() {
  const routes = JSON.parse(await readFile(join(here, 'routes.json'), 'utf8'));

  if (!Array.isArray(routes.routes) || routes.routes.length < MINIMUM_ROUTES) {
    console.error(
      `the manifest lists ${routes.routes?.length ?? 0} route(s); at least ${MINIMUM_ROUTES} are expected. `
      + 'Driving a handful of routes and reporting success is worse than not running at all.');
    return 1;
  }

  const { server, missing } = serve(root);

  await new Promise(done => server.listen(0, '127.0.0.1', done));
  const origin = `http://127.0.0.1:${server.address().port}`;
  console.log(`serving ${root} at ${origin}`);

  const browser = await chromium.launch();
  const failures = [];

  // One entry per preview and per snippet, so that two routes claiming to
  // demonstrate different capabilities can be held to showing different things.
  // Every assertion before this one is satisfied by a site that serves one
  // document and one snippet everywhere.
  const fingerprints = [];
  let driven = 0;

  try {
    for (const route of routes.routes) {
      driven++;
      const context = await browser.newContext();
      const page = await context.newPage();

      const problems = [];
      page.on('pageerror', error => problems.push(`uncaught: ${error.message}`));
      // Warnings count as well as errors. The .NET runtime writes Console.WriteLine
      // to console.log and console.debug rather than console.error, so a catch
      // block that logs instead of surfacing a message would otherwise be
      // entirely outside this harness's view. The site emits neither on any route
      // today, so the bar costs nothing to hold.
      page.on('console', message => {
        if (message.type() === 'error' || message.type() === 'warning') {
          problems.push(`console ${message.type()}: ${message.text()}`);
        }
      });

      const response = await page.goto(origin + route.path, { waitUntil: 'domcontentloaded' });

      if (response && response.status() !== 200) {
        problems.push(`status ${response.status()}`);
      }

      // The heading is the first thing the application renders, so waiting for it
      // separates "the runtime never started" from "a page rendered badly".
      let started = true;
      await page.waitForSelector('h1', { timeout: SETTLE_TIMEOUT_MS }).catch(() => {
        started = false;
        problems.push('no <h1> appeared, so the application did not start');
      });

      // Waiting for a page to stop working is meaningless when it never began.
      // Measured: with the runtime removed from the published output, letting
      // every route wait out both timeouts took over ten minutes to report what
      // the first route already knew.
      if (started && !(await settle(page))) {
        problems.push(`still working after ${SETTLE_TIMEOUT_MS} ms`);
      }

      // The Blazor error bar means an exception escaped the application. Security
      // checklist item 4 requires a legible message instead, on every path.
      if (await page.locator('#blazor-error-ui').isVisible().catch(() => false)) {
        problems.push('the Blazor error bar is showing');
      }

      // The class the pages use for a generation failure. A route that reports one
      // has not done what it exists to do.
      const errors = await page.locator('.error').allInnerTexts().catch(() => []);
      for (const text of errors) {
        problems.push(`error shown: ${text.replace(/\s+/g, ' ').slice(0, 160)}`);
      }

      const heading = await page.locator('h1').first().innerText().catch(() => '');
      if (heading.trim() === '') {
        problems.push('the heading is empty');
      }

      // The preview must hold a PDF, not merely be an element on the page. The
      // frame's source is a blob URL, so the bytes behind it can be read back and
      // checked from inside the page. Without this, a preview showing a corrupt
      // document passed every route: the harness could tell "bytes exist" from
      // "bytes do not exist" and nothing finer.
      if ((route.expect ?? []).includes('iframe')) {
        // The blob URL is created strictly AFTER the settle predicate goes
        // false: the page clears its busy flag in the same block that assigns
        // the bytes, and the preview then makes two interop round trips before
        // it has a URL. Reading immediately races the site and fails a working
        // one. Measured: a 400 ms delay inside createBlobUrl failed nine routes.
        await page
          .waitForFunction(() => document.querySelector('iframe')?.src?.startsWith('blob:') === true, null, {
            timeout: SETTLE_TIMEOUT_MS,
          })
          .catch(() => undefined);

        // The analysis lives in its own module because it is the only part of
        // this harness with real logic in it, and self-test.mjs exercises it
        // against documents built to have known answers. Four review rounds
        // found defects in it that driving the real site could not: the real
        // site's documents all draw, so only documents built to draw nothing
        // can tell a working predicate from a broken one.
        const verdict = await page
          .evaluate(analysePreview)
          .catch(error => `the preview could not be read: ${error.message}`);

        if (!verdict.startsWith('ok:')) {
          problems.push(verdict);
        } else if (route.distinct !== false) {
          fingerprints.push({ path: route.path, kind: 'preview', value: verdict });
        }
      }

      // A code panel that exists is not a code panel that works. One of the
      // defects this check exists for rendered the text "_code" here, because an
      // unprefixed attribute value on a string-typed component parameter is a
      // literal rather than an expression. Asserting only that the element is
      // present would have passed. Every emitted snippet opens with using
      // directives and constructs a Document, so both are required.
      const snippets = await page.locator('.code-panel-body').allInnerTexts().catch(() => []);
      for (const snippet of snippets) {
        if (!snippet.includes('using ') || !snippet.includes('new Document')) {
          problems.push(`the code panel does not hold C#: ${JSON.stringify(snippet.slice(0, 60))}`);
        } else if (route.distinct !== false) {
          fingerprints.push({ path: route.path, kind: 'snippet', value: snippet });
        }
      }

      // What the route is supposed to prove it can do, stated per route rather
      // than assumed, so "it rendered" is not mistaken for "it worked".
      //
      // NOTE visibility rather than presence. `count()` is visibility-blind: one
      // line of stylesheet hiding every preview left seventeen routes passing
      // while the site showed nothing at all.
      // Counted by what is SHOWN. Counting the document instead let a review
      // hide every card but one with a stylesheet while eleven remained in the
      // markup, and the harness reported success.
      for (const [selector, minimum] of Object.entries(route.expectAtLeast ?? {})) {
        const found = await page.evaluate(
          ({ css, shown }) => {
            // eslint-disable-next-line no-eval
            const predicate = eval(`(${shown})`);
            return [...document.querySelectorAll(css)].filter(predicate).length;
          },
          { css: selector, shown: isShown.toString() });

        if (found < minimum) {
          problems.push(`expected at least ${minimum} of ${selector} to be shown, found ${found}`);
        }
      }

      for (const selector of route.expect ?? []) {
        const matches = page.locator(selector);
        const count = await matches.count();

        if (count === 0) {
          problems.push(`expected ${selector}, found none`);
          continue;
        }

        // Any one of the matches being visible is enough. Taking only the first
        // would fail on a selector that also matches something the layout hides,
        // such as a navigation control collapsed at this viewport.
        //
        // NOTE this deliberately does NOT use Playwright's isVisible(), which
        // means "has a box and is not visibility:hidden" and models neither
        // opacity nor position. A review hid every preview on the site with one
        // line of `opacity: 0` and the harness reported seventeen of seventeen
        // passing.
        //
        // NOTE also that it asks whether the element sits within the DOCUMENT,
        // not within the viewport. Content below the fold is shown; content at
        // left:-10000px is not. Checking the viewport instead reported the
        // gallery's own cards as hidden.
        //
        // Occlusion by another element is still not modelled, and is recorded in
        // the ledger above as a known gap rather than claimed.
        let visible = false;
        const handles = await matches.elementHandles();

        for (const handle of handles) {
          if (!visible) {
            visible = await handle.evaluate(isShown).catch(() => false);
          }

          // Disposed unconditionally rather than only for the match that
          // answered, so an early exit does not leave the rest of the snapshot
          // held until the context closes.
          await handle.dispose();
        }

        if (!visible) {
          problems.push(`expected ${selector} to be visible, every match is hidden or positioned away`);
        }
      }

      // The heading identifies WHICH page answered. Without this, any capability
      // route could serve any other capability's document and pass, because every
      // one of them carries the same expectations.
      if (route.heading && heading.trim() !== route.heading) {
        problems.push(`expected the heading ${JSON.stringify(route.heading)}, got ${JSON.stringify(heading.trim())}`);
      }

      for (const selector of route.reject ?? []) {
        if ((await page.locator(selector).count()) > 0) {
          problems.push(`did not expect ${selector}, found one`);
        }
      }

      if (route.text) {
        const body = await page.locator('body').innerText();
        if (!body.includes(route.text)) {
          problems.push(`expected the page to say ${JSON.stringify(route.text)}`);
        }
      }

      if (problems.length > 0) {
        failures.push({ path: route.path, problems });
        console.log(`FAIL  ${route.path}`);
        for (const problem of problems) {
          console.log(`        ${problem}`);
        }
      } else {
        console.log(`ok    ${route.path}  (${heading.trim()})`);
      }

      await context.close();

      // An application that does not start will not start on the next route
      // either, and every further route costs a full timeout to learn nothing.
      // Stopping here turns a ten-minute report into a ten-second one.
      if (!started) {
        console.log('');
        console.log('The application did not start at all, so the remaining routes were not driven.');
        break;
      }
    }
  } finally {
    await browser.close();
    server.close();
  }

  // Two routes showing the same document is not something any per-route check
  // can see, because each of them is individually fine.
  for (const kind of ['preview', 'snippet']) {
    const seen = new Map();
    for (const { path, value } of fingerprints.filter(entry => entry.kind === kind)) {
      if (seen.has(value)) {
        const message = `${path} shows the same ${kind} as ${seen.get(value)}`;
        console.log(`FAIL  ${path}`);
        console.log(`        ${message}`);
        failures.push({ path, problems: [message] });
      } else {
        seen.set(value, path);
      }
    }
  }

  if (missing.length > 0) {
    const unique = [...new Set(missing)];
    console.log(`FAIL  ${unique.length} asset(s) were requested and are not published:`);
    for (const path of unique) {
      console.log(`        ${path}`);
    }

    failures.push({ path: '(assets)', problems: unique });
  }

  console.log('');
  console.log(
    driven === routes.routes.length
      ? `${driven} route(s) driven, ${failures.length} failing.`
      : `${driven} of ${routes.routes.length} route(s) driven before stopping, ${failures.length} failing.`);
  return failures.length === 0 ? 0 : 1;
}

process.exit(await main());
