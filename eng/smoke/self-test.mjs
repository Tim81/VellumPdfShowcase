// Exercises the preview analysis against documents built to have known answers.
//
// This exists because the analysis was wrong in a way nothing could see. Its
// operator test carried four literal backspace bytes, so two of its three
// alternatives were unmatchable; it passed on every real page only because a
// font-selecting operator happened to satisfy the third. A site whose every page
// was blank reported seventeen routes of seventeen passing, and the file looked
// correct in an editor.
//
// Driving the real site cannot catch that, because the real site's documents all
// draw. Only documents built to draw nothing, or to draw only an image, or to
// store their content uncompressed, can tell a working predicate from a broken
// one. That is what this does.
//
// Usage:
//   node eng/smoke/self-test.mjs
//
// Exit code 0 when every case answers as expected, 1 otherwise.

import { chromium } from 'playwright';
import { analysePreview } from './pdf-analysis.mjs';

/** Builds a single-page PDF whose content stream is exactly what is given. */
function documentDrawing(content, { compress = false, extraObjects = '' } = {}) {
  const stream = compress ? null : content;
  const filter = compress ? '/Filter/FlateDecode' : '';

  return {
    text:
      '%PDF-1.7\n'
      + '1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n'
      + '2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n'
      + '3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Contents 4 0 R>>endobj\n'
      + '4 0 obj<<' + filter + '/Length ' + (stream === null ? 0 : stream.length) + '>>stream\n'
      + (stream === null ? '' : stream)
      + '\nendstream endobj\n'
      + extraObjects
      + 'trailer<</Root 1 0 R/ID[<00000000000000000000000000000000>]>>\n'
      + '%%EOF\n',
  };
}

const cases = [
  {
    name: 'a page showing text',
    pdf: documentDrawing('BT /F1 12 Tf 72 700 Td (hello) Tj ET'),
    expect: 'ok',
  },
  {
    name: 'a page drawing only an image',
    pdf: documentDrawing('q 200 0 0 200 60 500 cm /Im1 Do Q'),
    expect: 'ok',
  },
  {
    name: 'a page painting only a filled path',
    pdf: documentDrawing('0 0 1 rg 100 100 200 300 re f'),
    expect: 'ok',
  },
  {
    name: 'a page that only selects a font and paints nothing',
    pdf: documentDrawing('BT /F1 12 Tf ET'),
    expect: 'draws nothing',
  },
  {
    name: 'a page whose content is a comment',
    pdf: documentDrawing('% this page paints nothing at all'),
    expect: 'draws nothing',
  },
  {
    name: 'a page with an empty content stream',
    pdf: documentDrawing(''),
    expect: 'draws nothing',
  },
  {
    name: 'a blank page beside a large embedded font stream',
    // The font stream contains operator-looking bytes. An earlier version
    // inflated every stream in the file and passed on exactly this.
    pdf: documentDrawing('% nothing here', {
      extraObjects:
        '5 0 obj<</Type/FontFile2/Length 60>>stream\n'
        + 'BT (text inside a font program) Tj ET 10 10 20 20 re f\n'
        + 'endstream endobj\n',
    }),
    expect: 'draws nothing',
  },
  {
    name: 'a page whose content stream is stored uncompressed',
    pdf: documentDrawing('BT (uncompressed but real) Tj ET'),
    expect: 'ok',
  },
  {
    name: 'a document with no page at all',
    pdf: { text: '%PDF-1.7\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n' },
    expect: 'no page content',
  },
];

const browser = await chromium.launch();
const page = await browser.newPage();
await page.setContent('<iframe></iframe>');

let failed = 0;

for (const testCase of cases) {
  // The analysis reads the frame's blob, so the case is handed over the same way
  // the real site hands over a document.
  await page.evaluate(text => {
    const bytes = new Uint8Array(text.length);
    for (let index = 0; index < text.length; index++) {
      bytes[index] = text.charCodeAt(index) & 0xff;
    }

    document.querySelector('iframe').src = URL.createObjectURL(new Blob([bytes], { type: 'application/pdf' }));
  }, testCase.pdf.text);

  const verdict = await page.evaluate(analysePreview);
  const matched = testCase.expect === 'ok' ? verdict.startsWith('ok:') : verdict.includes(testCase.expect);

  if (matched) {
    console.log('ok    ' + testCase.name);
  } else {
    failed++;
    console.log('FAIL  ' + testCase.name);
    console.log('        expected ' + JSON.stringify(testCase.expect) + ', got ' + JSON.stringify(verdict));
  }
}

// Two documents that draw different things must fingerprint differently, and the
// same drawing must fingerprint the same way. The distinctness gate rests on this.
const first = await fingerprintOf('BT (one) Tj ET');
const again = await fingerprintOf('BT (one) Tj ET');
const other = await fingerprintOf('BT (two) Tj ET');

if (first !== again) {
  failed++;
  console.log('FAIL  the same drawing fingerprints differently: ' + first + ' then ' + again);
} else {
  console.log('ok    the same drawing fingerprints the same way');
}

if (first === other) {
  failed++;
  console.log('FAIL  two different drawings share a fingerprint: ' + first);
} else {
  console.log('ok    different drawings fingerprint differently');
}

async function fingerprintOf(content) {
  await page.evaluate(text => {
    const bytes = new Uint8Array(text.length);
    for (let index = 0; index < text.length; index++) {
      bytes[index] = text.charCodeAt(index) & 0xff;
    }

    document.querySelector('iframe').src = URL.createObjectURL(new Blob([bytes], { type: 'application/pdf' }));
  }, documentDrawing(content).text);

  return page.evaluate(analysePreview);
}

await browser.close();

console.log('');
console.log(cases.length + 2 + ' case(s) checked, ' + failed + ' failing.');
process.exitCode = failed === 0 ? 0 : 1;
