// Drives every route of the published site in a real browser and fails when one
// of them is broken.
//
// This exists because of a gap nothing else in this repository closes. The test
// suite runs on desktop .NET; the site runs on browser WebAssembly. A capability
// that encrypts passed all 480 tests and failed in every visitor's tab with
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

async function main() {
  const routes = JSON.parse(await readFile(join(here, 'routes.json'), 'utf8'));
  const { server, missing } = serve(root);

  await new Promise(done => server.listen(0, '127.0.0.1', done));
  const origin = `http://127.0.0.1:${server.address().port}`;
  console.log(`serving ${root} at ${origin}`);

  const browser = await chromium.launch();
  const failures = [];

  try {
    for (const route of routes.routes) {
      const context = await browser.newContext();
      const page = await context.newPage();

      const problems = [];
      page.on('pageerror', error => problems.push(`uncaught: ${error.message}`));
      page.on('console', message => {
        if (message.type() === 'error') {
          problems.push(`console: ${message.text()}`);
        }
      });

      const response = await page.goto(origin + route.path, { waitUntil: 'domcontentloaded' });

      if (response && response.status() !== 200) {
        problems.push(`status ${response.status()}`);
      }

      // The heading is the first thing the application renders, so waiting for it
      // separates "the runtime never started" from "a page rendered badly".
      await page.waitForSelector('h1', { timeout: SETTLE_TIMEOUT_MS }).catch(() => {
        problems.push('no <h1> appeared, so the application did not start');
      });

      if (!(await settle(page))) {
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
        }
      }

      // What the route is supposed to prove it can do, stated per route rather
      // than assumed, so "it rendered" is not mistaken for "it worked".
      for (const selector of route.expect ?? []) {
        if ((await page.locator(selector).count()) === 0) {
          problems.push(`expected ${selector}, found none`);
        }
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
    }
  } finally {
    await browser.close();
    server.close();
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
  console.log(`${routes.routes.length} route(s) driven, ${failures.length} failing.`);
  return failures.length === 0 ? 0 : 1;
}

process.exit(await main());
