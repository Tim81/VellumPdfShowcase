// Decides what a rendered PDF actually draws, and fingerprints it.
//
// This function is handed to Playwright and runs INSIDE the page, so it may use
// only what a browser provides and may not close over anything from Node. It
// lives in its own file because it is the part of the harness with real logic in
// it, and self-test.mjs exercises it against documents built to have known
// answers. Driving the real site cannot do that job: every document the site
// produces draws something, so a predicate that answers "draws" for everything
// passes every route.
//
// NOTE on how this file is edited. An earlier version was spliced in through a
// shell heredoc, which resolved its word-boundary escapes to literal backspace
// bytes before the file was written. Two of the three alternatives in its
// operator test were unmatchable, the check survived only because a font-setting
// operator satisfied the third, and a site whose every page was blank reported
// seventeen routes of seventeen passing. No editor showed it. Edit this file with
// an editor, never through a shell, and self-test.mjs now fails if any control
// byte appears in the harness.

/**
 * Reads the bytes behind the page's preview frame and reports what it draws.
 *
 * Returns a string beginning "ok:" when every page of the preview draws
 * something, carrying a fingerprint of the drawing operators, or a sentence
 * naming what is wrong.
 */
export async function analysePreview() {
  // Declared inside the function on purpose: Playwright serialises the function
  // alone, so anything at module scope is simply absent in the page.

  // A PDF token ends at anything that is not a regular character, so the
  // complement of that class is a safe boundary. Written as a class rather than
  // a word-boundary escape, for the reason in the header.
  const boundary = '[^A-Za-z0-9]';

  // Operators that put marks on the page. Built from a list so that nothing in
  // it needs escaping by hand.
  //
  // NOTE two absences are deliberate and were both defects once. Tf selects a
  // font and paints nothing. And n ends a path WITHOUT filling or stroking it,
  // so `re W n`, the standard way to set a clipping region, paints nothing at
  // all; including it let a site of nine blank sheets pass every route.
  const painting = [
    'Tj', 'TJ', "'", '"',
    'Do', 'sh', 'EI',
    'f', 'F', 'f*', 'B', 'B*', 'b', 'b*', 'S', 's',
  ];

  const alternatives = painting
    .map(operator => operator.replace(/[*'"]/g, character => '[' + character + ']'))
    .join('|');

  const paints = new RegExp('(^|' + boundary + ')(' + alternatives + ')(' + boundary + '|$)');

  const frame = document.querySelector('iframe');
  if (!frame) {
    return 'no frame';
  }

  if (!frame.src.startsWith('blob:')) {
    return 'no blob URL appeared for the preview';
  }

  const bytes = new Uint8Array(await (await fetch(frame.src)).arrayBuffer());

  // latin1 maps every byte to one code point, so offsets in this string are byte
  // offsets. It is used for structure only, never for meaning.
  const decoder = new TextDecoder('latin1');
  const body = decoder.decode(bytes);

  if (decoder.decode(bytes.slice(0, 5)) !== '%PDF-') {
    return 'the preview does not begin with %PDF-';
  }

  if (!decoder.decode(bytes.slice(-2048)).includes('%%EOF')) {
    return 'the preview has no %%EOF, so it is truncated';
  }

  // Indexed by object number, so a /Contents reference can be resolved whatever
  // order the file lists its objects in, and whatever generation it carries.
  //
  // NOTE this walks OBJECTS rather than searching for /Type/Page and reading
  // forward. Reading forward from the type entry missed a dictionary that listed
  // /Contents before /Type, and hardcoding generation zero missed any object at a
  // later generation; both reported a page that draws as drawing nothing.
  const objects = new Map();

  for (const match of body.matchAll(/(\d+)\s+(\d+)\s+obj/g)) {
    const endsAt = body.indexOf('endobj', match.index);
    objects.set(match[1], { from: match.index + match[0].length, to: endsAt === -1 ? body.length : endsAt });
  }

  const pages = [];

  for (const [, span] of objects) {
    const text = body.slice(span.from, span.to);
    if (!/\/Type\s*\/Page(?![a-zA-Z])/.test(text)) {
      continue;
    }

    const single = text.match(/\/Contents\s+(\d+)\s+\d+\s+R/);
    if (single) {
      pages.push([single[1]]);
      continue;
    }

    const array = text.match(/\/Contents\s*\[([^\]]*)\]/);
    if (array) {
      pages.push([...array[1].matchAll(/(\d+)\s+\d+\s+R/g)].map(reference => reference[1]));
      continue;
    }

    // A page with no /Contents at all is a blank sheet, and is reported as one
    // rather than skipped.
    pages.push([]);
  }

  if (pages.length === 0) {
    return 'the preview has no page at all';
  }

  const drawings = [];

  for (let index = 0; index < pages.length; index++) {
    const streams = [];

    for (const number of pages[index]) {
      const span = objects.get(number);
      if (span === undefined) {
        continue;
      }

      const streamAt = body.indexOf('stream', span.from);
      const endsAt = body.indexOf('endstream', streamAt);
      if (streamAt === -1 || endsAt === -1 || streamAt > span.to) {
        continue;
      }

      let from = streamAt + 'stream'.length;
      if (bytes[from] === 13) {
        from++;
      }

      if (bytes[from] === 10) {
        from++;
      }

      // The end-of-line before endstream is not stream data. Leaving it on makes
      // the decoder reject the whole stream as having trailing rubbish, which
      // once turned every document into "draws nothing".
      let to = endsAt;
      while (to > from && (bytes[to - 1] === 10 || bytes[to - 1] === 13)) {
        to--;
      }

      const raw = bytes.slice(from, to);
      if (raw.length === 0) {
        continue;
      }

      if (!/\/Filter/.test(body.slice(span.from, streamAt))) {
        streams.push(decoder.decode(raw));
        continue;
      }

      let inflated = null;
      for (const format of ['deflate', 'deflate-raw']) {
        try {
          const stream = new Blob([raw]).stream().pipeThrough(new DecompressionStream(format));
          inflated = decoder.decode(new Uint8Array(await new Response(stream).arrayBuffer()));
          break;
        } catch {
          // Try the other framing.
        }
      }

      if (inflated === null) {
        // Named rather than dropped. A filter this cannot read is a limitation of
        // this check, and calling the page blank would blame the site for it.
        return 'the preview uses a stream filter this check cannot read, on object ' + number;
      }

      streams.push(inflated);
    }

    drawings.push(streams.join(''));
  }

  // EVERY page must draw, not merely one of them. Concatenating the pages and
  // testing once let a document whose later pages were blank pass, which matters
  // most for the capability whose whole subject is content repeating across
  // pages.
  const blank = [];
  for (let index = 0; index < drawings.length; index++) {
    if (!paints.test(drawings[index])) {
      blank.push(index + 1);
    }
  }

  if (blank.length > 0) {
    return 'the preview draws nothing on page ' + blank.join(', ') + ' of ' + drawings.length;
  }

  const operators = drawings.join('');

  let hash = 0;
  for (let index = 0; index < operators.length; index++) {
    hash = ((hash << 5) - hash + operators.charCodeAt(index)) | 0;
  }

  return 'ok:' + drawings.length + ':' + operators.length + ':' + hash;
}
