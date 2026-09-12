// Decides what a rendered PDF actually draws, and fingerprints it.
//
// This function is handed to Playwright and runs INSIDE the page, so it may use
// only what a browser provides and may not close over anything from Node. It
// lives in its own file for two reasons: it is the part of the harness with real
// logic in it, and self-test.mjs exercises it directly against documents built to
// have known answers.
//
// NOTE it is written with no backslash escapes in any string, and its regular
// expressions are built from explicit character classes rather than \b. An
// earlier version of this logic was spliced into the harness through a shell
// heredoc, which resolved its \b escapes to literal backspace bytes before the
// file was written. Two of the three alternatives in the operator test were
// therefore unmatchable, the check passed only because font-setting operators
// happened to satisfy the surviving one, and a site whose every page was blank
// reported seventeen routes of seventeen passing. Nothing in the file's
// appearance showed it; only a byte dump did.

/**
 * Reads the bytes behind the page's preview frame and reports what it draws.
 *
 * Returns a string beginning "ok:" when the preview is a PDF that draws
 * something, carrying a fingerprint of the drawing operators, or a sentence
 * naming what is wrong.
 */
export async function analysePreview() {
  // Declared inside the function on purpose. Playwright serialises the function
  // alone, so anything at module scope is simply absent in the page.
  //
  // A PDF token ends at anything that is not a regular character, so the
  // complement of that class is a safe boundary. This is written out rather than
  // using a word boundary escape, because an earlier version had its escapes resolved before the
  // file was written and carried literal backspace bytes instead.
  const boundary = '[^A-Za-z0-9]';

  // Operators that put marks on the page: text-showing, image drawing, shading,
  // and the path-painting family.
  //
  // NOTE Tf is deliberately absent. It selects a font and paints nothing, and it
  // was what silently satisfied the broken predicate this replaces.
  // Built from a list rather than written as one pattern, so that no character
  // in it needs escaping by hand. The starred forms are the even-odd variants.
  const painting = ['Tj', 'TJ', 'Do', 'sh', 'EI', 'f', 'F', 'f*', 'B', 'B*', 'b', 'b*', 'S', 's', 'n'];
  const alternatives = painting.map(operator => operator.replace('*', '[*]')).join('|');
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
  // offsets. It is only ever used for structure, never for meaning.
  const decoder = new TextDecoder('latin1');
  const body = decoder.decode(bytes);

  if (decoder.decode(bytes.slice(0, 5)) !== '%PDF-') {
    return 'the preview does not begin with %PDF-';
  }

  if (!decoder.decode(bytes.slice(-2048)).includes('%%EOF')) {
    return 'the preview has no %%EOF, so it is truncated';
  }

  // Only the streams the PAGES point at. An earlier version inflated every stream
  // in the file, so an embedded font of 147 kilobytes drowned a content stream of
  // 791 bytes, and a page drawing nothing passed on operators found inside the
  // font program and the character map.
  const contentObjectNumbers = new Set();

  for (const page of body.matchAll(/\/Type\s*\/Page(?![a-zA-Z])/g)) {
    // The page dictionary runs to the double angle bracket that CLOSES it, which
    // is not the first one: a page carries a nested Resources dictionary, and
    // stopping at the first close cut the slice off before /Contents, so every
    // real document reported having no page content at all.
    let depth = 1;
    let cursor = page.index;

    while (cursor < body.length && depth > 0) {
      const opens = body.indexOf('<<', cursor);
      const closes = body.indexOf('>>', cursor);

      if (closes === -1) {
        break;
      }

      if (opens !== -1 && opens < closes) {
        depth++;
        cursor = opens + 2;
      } else {
        depth--;
        cursor = closes + 2;
      }
    }

    const dictionary = body.slice(page.index, cursor);

    const single = dictionary.match(/\/Contents\s+(\d+)\s+\d+\s+R/);
    if (single) {
      contentObjectNumbers.add(single[1]);
      continue;
    }

    const array = dictionary.match(/\/Contents\s*\[([^\]]*)\]/);
    if (array) {
      for (const reference of array[1].matchAll(/(\d+)\s+\d+\s+R/g)) {
        contentObjectNumbers.add(reference[1]);
      }
    }
  }

  if (contentObjectNumbers.size === 0) {
    return 'the preview has no page content at all';
  }

  const drawn = [];
  const undecodable = [];

  for (const number of contentObjectNumbers) {
    const object = body.search(new RegExp('(^|[^0-9])' + number + '\\s+0\\s+obj'));
    if (object === -1) {
      continue;
    }

    const streamAt = body.indexOf('stream', object);
    const endsAt = body.indexOf('endstream', streamAt);
    if (streamAt === -1 || endsAt === -1) {
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
    // the decoder reject the whole stream as having trailing rubbish, which once
    // turned every document into "draws nothing".
    let to = endsAt;
    while (to > from && (bytes[to - 1] === 10 || bytes[to - 1] === 13)) {
      to--;
    }

    const raw = bytes.slice(from, to);
    if (raw.length === 0) {
      continue;
    }

    const dictionary = body.slice(object, streamAt);
    if (!/\/Filter/.test(dictionary)) {
      // Stored as-is. Perfectly legal, and a previous version discarded it and
      // then reported the page as drawing nothing.
      drawn.push(decoder.decode(raw));
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
      // Named rather than silently dropped. A filter this cannot read is a
      // limitation of the harness, and reporting it as "the page draws nothing"
      // would blame the site for it.
      undecodable.push(number);
      continue;
    }

    drawn.push(inflated);
  }

  if (undecodable.length > 0) {
    return 'the preview uses a stream filter this check cannot read, on object(s) ' + undecodable.join(', ');
  }

  const operators = drawn.join('');

  if (!paints.test(operators)) {
    return 'the preview draws nothing: no text, image or path-painting operator in any page content stream';
  }

  let hash = 0;
  for (let index = 0; index < operators.length; index++) {
    hash = ((hash << 5) - hash + operators.charCodeAt(index)) | 0;
  }

  return 'ok:' + operators.length + ':' + hash;
}
