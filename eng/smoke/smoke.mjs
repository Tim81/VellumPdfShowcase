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
// KNOWN GAPS, stated rather than implied. Occlusion by an overlaying element is
// not modelled. A document with the right structure but wrong content passes, so
// long as it differs from the others. Nothing here clicks anything, so /smoke's
// own buttons are never pressed.
//
// The unbroken site passes all 17 before and after each mutation.

import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { createReadStream } from 'node:fs';
import { extname, join, normalize, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

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
        // The blob URL is created strictly AFTER the settle predicate goes false:
        // the page clears its busy flag in the same block that assigns the bytes,
        // and the preview then makes two interop round trips before it has a URL.
        // Reading immediately therefore races the site and fails a working one.
        // Measured: a 400 ms delay inside createBlobUrl failed nine routes.
        await page
          .waitForFunction(() => document.querySelector('iframe')?.src?.startsWith('blob:') === true, null, {
            timeout: SETTLE_TIMEOUT_MS,
          })
          .catch(() => problems.push('no blob URL appeared for the preview'));

        const verdict = await page.evaluate(async () => {
          const frame = document.querySelector('iframe');
          if (!frame) {
            return 'no frame';
          }

          if (!frame.src.startsWith('blob:')) {
            return `the frame source is not a blob URL: ${JSON.stringify(frame.src.slice(0, 60))}`;
          }

          const bytes = new Uint8Array(await (await fetch(frame.src)).arrayBuffer());
          const decoder = new TextDecoder('latin1');
          const header = decoder.decode(bytes.slice(0, 5));
          const trailer = decoder.decode(bytes.slice(-2048));

          if (header !== '%PDF-') {
            return `the preview does not begin with %PDF- but with ${JSON.stringify(header)}`;
          }

          if (!trailer.includes('%%EOF')) {
            return 'the preview has no %%EOF, so it is truncated';
          }

          // A document with a page, a font and any content at all is far larger
          // than this. The bound is deliberately loose: its job is to catch a
          // stub, not to pin a size that would need revisiting.
          if (bytes.length < 500) {
            return `the preview is only ${bytes.length} bytes`;
          }

          // A blank page is a well-formed PDF. What distinguishes a real
          // document is that it draws something, which means a content stream
          // with operators in it.
          const body = decoder.decode(bytes);
          if (!body.includes('/Contents')) {
            return 'the preview has no page content at all';
          }

          // Returned rather than discarded, so the caller can tell whether two
          // routes are showing the SAME document. A review pointed every preview
          // at one shared blank PDF and every route passed.
          let hash = 0;
          for (let index = 0; index < bytes.length; index++) {
            hash = ((hash << 5) - hash + bytes[index]) | 0;
          }

          return `ok:${bytes.length}:${hash}`;
        }).catch(error => `the preview could not be read: ${error.message}`);

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
      for (const [selector, minimum] of Object.entries(route.expectAtLeast ?? {})) {
        const found = await page.locator(selector).count();
        if (found < minimum) {
          problems.push(`expected at least ${minimum} of ${selector}, found ${found}`);
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
          visible = await handle.evaluate(element => {
            const box = element.getBoundingClientRect();
            if (box.width < 1 || box.height < 1) {
              return false;
            }

            const page = document.documentElement;
            const left = box.left + scrollX;
            const top = box.top + scrollY;
            if (left + box.width < 0 || top + box.height < 0 || left > page.scrollWidth || top > page.scrollHeight) {
              return false;
            }

            // Opacity compounds down the tree, so an ancestor at zero hides a
            // child whose own computed style reads 1.
            for (let node = element; node instanceof Element; node = node.parentElement) {
              const style = getComputedStyle(node);
              if (style.visibility === 'hidden' || style.display === 'none' || Number(style.opacity) === 0) {
                return false;
              }
            }

            return true;
          }).catch(() => false);

          await handle.dispose();
          if (visible) {
            break;
          }
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
