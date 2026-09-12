// Exercises the preview analysis against documents built to have known answers,
// and checks the harness's own source for the byte that once corrupted it.
//
// This exists because the analysis was wrong in ways nothing could see. Its
// operator test once carried literal backspace bytes, so two of three
// alternatives were unmatchable. It once accepted `n`, which ends a path without
// painting it, so nine blank sheets passed every route. It once inflated every
// stream in the file, so an embedded font drowned the page content.
//
// Driving the real site cannot catch any of those, because every document the
// site produces draws. Only documents built to draw nothing, or to draw only an
// image, or to compress their content, or to carry a nested dictionary, can tell
// a working predicate from a broken one.
//
// A review measured the previous version of this file and found that four
// branches of the analysis could be broken outright with all eleven cases still
// passing: the page-dictionary walk, the inflate path, the /Contents array, and
// nine of the fifteen painting operators. The cases below were chosen to reach
// each of those.
//
// Usage:
//   node eng/smoke/self-test.mjs
//
// Exit code 0 when every case answers as expected, 1 otherwise.

import { readFile, readdir } from 'node:fs/promises';
import { deflateSync } from 'node:zlib';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';
import { chromium } from 'playwright';
import { analysePreview } from './pdf-analysis.mjs';

const here = fileURLToPath(new URL('.', import.meta.url));

/** Latin-1 bytes of a string, so offsets stay bytes. */
function latin1(text) {
  return Uint8Array.from([...text].map(character => character.charCodeAt(0) & 0xff));
}

/**
 * Builds a PDF whose pages draw exactly what is given, one entry per page.
 *
 * Options: `compress` runs each content stream through Flate, `resources`
 * inserts a nested dictionary into each page, `contentsAsArray` splits a page's
 * content across two objects, `generation` sets the content objects' generation
 * number, and `extraObjects` appends whatever is passed.
 */
function build(pageContents, options = {}) {
  const { compress = false, resources = false, contentsAsArray = false, generation = 0, extraObjects = '', typeLast = false } = options;

  let next = 10;
  const objects = [];
  const pageRefs = [];

  for (const content of pageContents) {
    const parts = contentsAsArray ? [content.slice(0, Math.ceil(content.length / 2)), content.slice(Math.ceil(content.length / 2))] : [content];
    const contentNumbers = [];

    for (const part of parts) {
      const number = next++;
      contentNumbers.push(number);
      const raw = compress ? deflateSync(Buffer.from(part, 'latin1')).toString('latin1') : part;
      const filter = compress ? '/Filter/FlateDecode' : '';
      objects.push(number + ' ' + generation + ' obj<<' + filter + '/Length ' + raw.length + '>>stream\n' + raw + '\nendstream endobj\n');
    }

    const pageNumber = next++;
    pageRefs.push(pageNumber);

    const contents = contentsAsArray
      ? '/Contents[' + contentNumbers.map(number => number + ' ' + generation + ' R').join(' ') + ']'
      : '/Contents ' + contentNumbers[0] + ' ' + generation + ' R';

    const nested = resources ? '/Resources<</ProcSet[/PDF /Text]/Font<</F1 99 0 R>>/XObject<<>>>>' : '';
    const type = '/Type/Page';
    const rest = '/Parent 2 0 R/MediaBox[0 0 595 842]' + nested + contents;

    objects.push(pageNumber + ' 0 obj<<' + (typeLast ? rest + type : type + rest) + '>>endobj\n');
  }

  return '%PDF-1.7\n'
    + '1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n'
    + '2 0 obj<</Type/Pages/Kids[' + pageRefs.map(number => number + ' 0 R').join(' ') + ']/Count ' + pageRefs.length + '>>endobj\n'
    + objects.join('')
    + extraObjects
    + 'trailer<</Root 1 0 R/ID[<00000000000000000000000000000000>]>>\n'
    + '%%EOF\n';
}

const TEXT = 'BT /F1 12 Tf 72 700 Td (hello) Tj ET';

const cases = [
  { name: 'a page showing text', pdf: build([TEXT]), expect: 'ok' },
  { name: 'a page drawing only an image', pdf: build(['q 200 0 0 200 60 500 cm /Im1 Do Q']), expect: 'ok' },
  { name: 'a page painting only a filled path', pdf: build(['0 0 1 rg 100 100 200 300 re f']), expect: 'ok' },
  { name: 'a page stroking a path', pdf: build(['1 w 100 100 m 300 300 l S']), expect: 'ok' },
  { name: 'a page painting with the even-odd rule', pdf: build(['100 100 200 200 re f*']), expect: 'ok' },
  { name: 'a page drawing a shading', pdf: build(['q /Sh1 sh Q']), expect: 'ok' },
  { name: 'a page showing text with the quote operator', pdf: build(['BT /F1 12 Tf (line) \' ET']), expect: 'ok' },

  { name: 'a page that only selects a font', pdf: build(['BT /F1 12 Tf ET']), expect: 'draws nothing' },
  { name: 'a page that only sets a clipping region', pdf: build(['q 0 0 595 842 re W n Q']), expect: 'draws nothing' },
  { name: 'a page whose content is a comment', pdf: build(['% this page paints nothing at all']), expect: 'draws nothing' },
  { name: 'a page with an empty content stream', pdf: build(['']), expect: 'draws nothing' },
  { name: 'a page with no contents entry at all', pdf: '%PDF-1.7\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n', expect: 'draws nothing' },

  { name: 'a compressed content stream', pdf: build([TEXT], { compress: true }), expect: 'ok' },
  { name: 'a compressed page that draws nothing', pdf: build(['BT /F1 12 Tf ET'], { compress: true }), expect: 'draws nothing' },
  { name: 'a page whose dictionary nests another', pdf: build([TEXT], { resources: true }), expect: 'ok' },
  { name: 'a page whose contents is an array', pdf: build([TEXT], { contentsAsArray: true }), expect: 'ok' },
  { name: 'content objects at a later generation', pdf: build([TEXT], { generation: 1 }), expect: 'ok' },
  { name: 'a page listing its type last', pdf: build([TEXT], { typeLast: true }), expect: 'ok' },

  { name: 'three pages that all draw', pdf: build([TEXT, TEXT + ' ', TEXT + '  ']), expect: 'ok' },
  { name: 'three pages where only the first draws', pdf: build([TEXT, '% blank', '% blank too']), expect: 'draws nothing on page 2, 3' },
  { name: 'three pages where only the last is blank', pdf: build([TEXT, TEXT + ' ', '% blank']), expect: 'draws nothing on page 3' },

  {
    name: 'a blank page beside an operator-rich font stream',
    pdf: build(['% nothing here'], {
      extraObjects: '5 0 obj<</Type/FontFile2/Length 60>>stream\nBT (text in a font) Tj ET 10 10 20 20 re f\nendstream endobj\n',
    }),
    expect: 'draws nothing',
  },
  // Tokens that LOOK like painting operators but sit inside strings, names,
  // hex strings and comments. A review demonstrated this with the site's own
  // ordered-list document, whose label "(b.)" made a page with every genuine
  // painting operator removed still report painting.
  { name: 'a list label spelling a painting operator', pdf: build(['BT /F1 12 Tf 72 700 Td (b.) ET']), expect: 'draws nothing' },
  { name: 'a sentence containing a lone S', pdf: build(['BT /F1 12 Tf 72 700 Td (Section S of the report) ET']), expect: 'draws nothing' },
  { name: 'a name spelling a painting operator', pdf: build(['BT /F1 12 Tf /f 12 Tf ET']), expect: 'draws nothing' },
  { name: 'a hex string spelling one', pdf: build(['BT /F1 12 Tf <6620532062> ET']), expect: 'draws nothing' },
  { name: 'a comment mentioning one', pdf: build(['% draw f here and S there']), expect: 'draws nothing' },
  { name: 'a string with an escaped bracket', pdf: build(['BT /F1 12 Tf 72 700 Td (a \\) b (c) d) ET']), expect: 'draws nothing' },
  { name: 'the same label actually shown', pdf: build(['BT /F1 12 Tf 72 700 Td (b.) Tj ET']), expect: 'ok' },
  { name: 'an inline image', pdf: build(['BI /W 2 /H 2 /BPC 8 /CS /G ID  EI']), expect: 'ok' },

  // Structural checks a review broke outright with every case still passing.
  { name: 'a preview that is not a PDF at all', pdf: 'NOTAPDF and nothing else', expect: 'does not begin with %PDF-' },
  { name: 'a preview truncated before its trailer', pdf: build([TEXT]).replace('%%EOF', ''), expect: 'no %%EOF' },

  { name: 'a document with no page at all', pdf: '%PDF-1.7\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n', expect: 'no page at all' },
];

// One case per painting operator, generated rather than written out. A review
// cut nine of the sixteen operators from the analysis with all twenty-six cases
// still passing, including B, which is the only path-painting operator in the
// pie chart capability, and TJ, the array form of show-text.
const OPERATOR_SAMPLES = {
  Tj: 'BT /F1 12 Tf (x) Tj ET',
  TJ: 'BT /F1 12 Tf [(x)] TJ ET',
  "'": "BT /F1 12 Tf (x) ' ET",
  '"': 'BT /F1 12 Tf 1 1 (x) " ET',
  Do: 'q /Im1 Do Q',
  sh: 'q /Sh1 sh Q',
  EI: 'BI /W 1 /H 1 /BPC 8 /CS /G ID  EI',
  f: '0 0 10 10 re f',
  F: '0 0 10 10 re F',
  'f*': '0 0 10 10 re f*',
  B: '0 0 10 10 re B',
  'B*': '0 0 10 10 re B*',
  b: '0 0 m 10 10 l b',
  'b*': '0 0 m 10 10 l b*',
  S: '0 0 m 10 10 l S',
  s: '0 0 m 10 10 l s',
};

for (const [operator, content] of Object.entries(OPERATOR_SAMPLES)) {
  cases.push({ name: 'the ' + operator + ' operator paints', pdf: build([content]), expect: 'ok' });
}

const browser = await chromium.launch();
const page = await browser.newPage();
await page.setContent('<iframe></iframe>');

let failed = 0;

async function verdictFor(pdf) {
  await page.evaluate(codes => {
    document.querySelector('iframe').src = URL.createObjectURL(
      new Blob([new Uint8Array(codes)], { type: 'application/pdf' }));
  }, [...latin1(pdf)]);

  return page.evaluate(analysePreview);
}

for (const testCase of cases) {
  const verdict = await verdictFor(testCase.pdf);
  const matched = testCase.expect === 'ok' ? verdict.startsWith('ok:') : verdict.includes(testCase.expect);

  if (matched) {
    console.log('ok    ' + testCase.name);
  } else {
    failed++;
    console.log('FAIL  ' + testCase.name);
    console.log('        expected ' + JSON.stringify(testCase.expect) + ', got ' + JSON.stringify(verdict));
  }
}

// The distinctness gate rests on these two properties.
const first = await verdictFor(build(['BT (one) Tj ET']));
const again = await verdictFor(build(['BT (one) Tj ET']));
const other = await verdictFor(build(['BT (two) Tj ET']));

if (first === again) {
  console.log('ok    the same drawing fingerprints the same way');
} else {
  failed++;
  console.log('FAIL  the same drawing fingerprints differently: ' + first + ' then ' + again);
}

if (first === other) {
  failed++;
  console.log('FAIL  two different drawings share a fingerprint: ' + first);
} else {
  console.log('ok    different drawings fingerprint differently');
}

await browser.close();

// The byte that corrupted this harness once, and that no editor displays. The
// previous round's commit message claimed such a check existed; it did not, so
// here it is.
let controlBytes = 0;
for (const name of await readdir(here)) {
  if (!name.endsWith('.mjs') && !name.endsWith('.json')) {
    continue;
  }

  const text = await readFile(join(here, name), 'utf8');
  for (let index = 0; index < text.length; index++) {
    const code = text.charCodeAt(index);
    if (code < 32 && code !== 9 && code !== 10 && code !== 13) {
      controlBytes++;
      console.log('FAIL  ' + name + ' carries control byte ' + code + ' at offset ' + index);
    }
  }
}

if (controlBytes === 0) {
  console.log('ok    no control byte in the harness source');
} else {
  failed++;
}

console.log('');
console.log((cases.length + 3) + ' case(s) checked, ' + failed + ' failing.');
process.exitCode = failed === 0 ? 0 : 1;
