// Decides what a rendered PDF actually puts on paper, and fingerprints it.
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
// bytes before the file was written, leaving two thirds of its operator test
// unmatchable while every route still passed. Edit this file with an editor,
// never through a shell; self-test.mjs fails if any control byte appears here.

/**
 * Reads the bytes behind the page's preview frame and reports what it draws.
 *
 * Returns a string beginning "ok:" when every page of the preview puts marks on
 * paper, carrying the page count and a fingerprint of the drawing operators, or
 * a sentence naming what is wrong.
 */
export async function analysePreview() {
  // Everything is declared inside the function on purpose: Playwright serialises
  // the function alone, so anything at module scope is simply absent in the page.

  const DELIMITERS = new Set([' ', '\t', '\r', '\n', '\f', '\0', '(', ')', '<', '>', '[', ']', '{', '}', '/', '%']);

  // Operators that put marks on paper without needing a text operand.
  //
  // NOTE two absences are deliberate and were both defects once. Tf selects a
  // font and paints nothing. And n ends a path WITHOUT filling or stroking it,
  // so `re W n`, the standard way to set a clipping region, paints nothing at
  // all; including it let a site of nine blank sheets pass every route.
  const MARKS = new Set(['Do', 'sh', 'EI', 'f', 'F', 'f*', 'B', 'B*', 'b', 'b*', 'S', 's']);

  // Operators that show text, and therefore mark paper only if what they are
  // given is not blank.
  const SHOWS_TEXT = new Set(['Tj', 'TJ', "'", '"']);

  /** Whether a string operand would actually put ink on the page. */
  const hasInk = text => /[^\s\0]/.test(text);

  /**
   * Whether a content stream puts marks on paper, decided by TOKENISING it and
   * reading the operands, not by searching it.
   *
   * A pattern match cannot answer this: the site's ordered-list document draws
   * the label "(b.)", and b is the close-fill-and-stroke operator, so a page
   * with every genuine painting operator removed still reported painting.
   *
   * NOR is the presence of a show-text operator enough. A review rewrote every
   * literal string on the site to spaces, leaving seven pages completely blank
   * with their operators, coordinates and structure intact, and every route
   * passed. `() Tj` shows nothing. The operand decides.
   */
  function marksPaper(text) {
    let token = '';
    let index = 0;

    // Operands seen since the last operator. A show-text operator consumes the
    // most recent one; TJ consumes an array of them.
    let operands = [];
    let inArray = false;
    let arrayOperands = [];

    const settle = () => {
      const finished = token;
      token = '';
      return finished;
    };

    const remember = value => {
      if (inArray) {
        arrayOperands.push(value);
      } else {
        operands.push(value);
      }
    };

    // Returns true when this operator marks paper, given what it was handed.
    const apply = operator => {
      if (operator === 'BI') {
        // An inline image runs from BI to EI with arbitrary bytes between, and
        // it draws.
        return true;
      }

      if (MARKS.has(operator)) {
        return true;
      }

      if (SHOWS_TEXT.has(operator)) {
        const shown = operator === 'TJ' ? arrayOperands : operands.slice(-1);
        return shown.some(hasInk);
      }

      return false;
    };

    while (index < text.length) {
      const character = text[index];

      // A comment runs to the end of the line.
      if (character === '%') {
        if (apply(settle())) {
          return true;
        }

        while (index < text.length && text[index] !== '\n' && text[index] !== '\r') {
          index++;
        }

        continue;
      }

      // A literal string, which may nest parentheses and escape them. Its
      // CONTENT is kept, because whether it is blank is the whole question.
      if (character === '(') {
        if (apply(settle())) {
          return true;
        }

        let depth = 1;
        let content = '';
        index++;

        while (index < text.length && depth > 0) {
          if (text[index] === '\\') {
            // An escaped character stands for itself, or for a code the next
            // digits give. Either way it is one character of content, and the
            // only thing that matters here is whether it is blank.
            content += text[index + 1] ?? '';
            index += 2;
            continue;
          }

          if (text[index] === '(') {
            depth++;
          } else if (text[index] === ')') {
            depth--;
            if (depth === 0) {
              index++;
              break;
            }
          }

          content += text[index];
          index++;
        }

        remember(content);
        continue;
      }

      // A hex string. Two angle brackets open a dictionary instead.
      if (character === '<' && text[index + 1] !== '<') {
        if (apply(settle())) {
          return true;
        }

        let digits = '';
        index++;

        while (index < text.length && text[index] !== '>') {
          digits += text[index];
          index++;
        }

        index++;

        const cleaned = digits.replace(/[^0-9A-Fa-f]/g, '');
        let content = '';
        for (let at = 0; at + 1 < cleaned.length || at < cleaned.length; at += 2) {
          content += String.fromCharCode(parseInt((cleaned.slice(at, at + 2) + '0').slice(0, 2), 16));
        }

        remember(content);
        continue;
      }

      // A name, which may spell anything at all after the slash.
      if (character === '/') {
        if (apply(settle())) {
          return true;
        }

        index++;
        while (index < text.length && !DELIMITERS.has(text[index])) {
          index++;
        }

        continue;
      }

      if (character === '[') {
        if (apply(settle())) {
          return true;
        }

        inArray = true;
        arrayOperands = [];
        index++;
        continue;
      }

      if (character === ']') {
        if (apply(settle())) {
          return true;
        }

        inArray = false;
        index++;
        continue;
      }

      if (DELIMITERS.has(character)) {
        const finished = settle();
        if (apply(finished)) {
          return true;
        }

        if (finished !== '') {
          // An operator that did not mark paper consumes its operands.
          operands = [];
        }

        index++;
        continue;
      }

      token += character;
      index++;
    }

    return apply(settle());
  }

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

  // Indexed by object number. A later definition wins, which is what an
  // incremental update means.
  //
  // NOTE this walks OBJECTS rather than searching for /Type/Page and reading
  // forward. Reading forward from the type entry missed a dictionary that listed
  // /Contents before /Type, and hardcoding generation zero missed any object at
  // a later generation; both reported a page that draws as drawing nothing.
  const objects = new Map();

  for (const match of body.matchAll(/(\d+)\s+(\d+)\s+obj/g)) {
    const endsAt = body.indexOf('endobj', match.index);
    objects.set(match[1], { from: match.index + match[0].length, to: endsAt === -1 ? body.length : endsAt });
  }

  const textOf = number => {
    const span = objects.get(number);
    return span === undefined ? '' : body.slice(span.from, span.to);
  };

  // The pages the document ACTUALLY HAS, resolved through the page tree rather
  // than counted by how many page objects the file happens to contain.
  //
  // A review dropped a page from /Kids while leaving its object in the file.
  // Every viewer showed one page; the count still said two, because it was
  // counting objects. Page-assembly defects live in the tree, which is exactly
  // where the old count was not looking.
  const rootMatch = body.match(/\/Root\s+(\d+)\s+\d+\s+R/g);
  const root = rootMatch === null ? null : /(\d+)/.exec(rootMatch[rootMatch.length - 1])[1];
  const pagesRoot = root === null ? null : /\/Pages\s+(\d+)\s+\d+\s+R/.exec(textOf(root))?.[1] ?? null;

  const ordered = [];
  const seen = new Set();

  const walk = number => {
    if (number === undefined || seen.has(number)) {
      return;
    }

    seen.add(number);
    const text = textOf(number);

    if (/\/Type\s*\/Page(?![a-zA-Z])/.test(text)) {
      ordered.push(number);
      return;
    }

    const kids = /\/Kids\s*\[([^\]]*)\]/.exec(text);
    if (kids === null) {
      return;
    }

    for (const child of kids[1].matchAll(/(\d+)\s+\d+\s+R/g)) {
      walk(child[1]);
    }
  };

  walk(pagesRoot ?? undefined);

  if (ordered.length === 0) {
    return 'the preview has no page at all';
  }

  const drawings = [];

  for (const number of ordered) {
    const text = textOf(number);
    const references = [];

    const single = /\/Contents\s+(\d+)\s+\d+\s+R/.exec(text);
    if (single) {
      references.push(single[1]);
    } else {
      const array = /\/Contents\s*\[([^\]]*)\]/.exec(text);
      if (array) {
        references.push(...[...array[1].matchAll(/(\d+)\s+\d+\s+R/g)].map(reference => reference[1]));
      }
    }

    const streams = [];

    for (const reference of references) {
      const span = objects.get(reference);
      if (span === undefined) {
        continue;
      }

      const streamAt = body.indexOf('stream', span.from);
      if (streamAt === -1 || streamAt > span.to) {
        continue;
      }

      let from = streamAt + 'stream'.length;
      if (bytes[from] === 13) {
        from++;
      }

      if (bytes[from] === 10) {
        from++;
      }

      const dictionary = body.slice(span.from, streamAt);

      // The declared length, which is authoritative. Trimming trailing
      // end-of-line bytes instead ate real data: the last byte of a Flate stream
      // is the low byte of its checksum, so roughly one stream in 128 ends in a
      // carriage return or newline, and the trim removed it and made a working
      // document undecodable. That would have fired on an innocuous edit and
      // blamed the site for the harness.
      const declared = /\/Length\s+(\d+)(?!\s+\d+\s+R)/.exec(dictionary);
      let to;

      if (declared) {
        to = from + Number(declared[1]);
      } else {
        to = body.indexOf('endstream', streamAt);
        if (to === -1) {
          continue;
        }

        // Exactly one end-of-line belongs to the delimiter, not to the data.
        if (bytes[to - 1] === 10) {
          to--;
        }

        if (bytes[to - 1] === 13) {
          to--;
        }
      }

      const raw = bytes.slice(from, Math.min(to, bytes.length));
      if (raw.length === 0) {
        continue;
      }

      if (!/\/Filter/.test(dictionary)) {
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
        return 'the preview uses a stream filter this check cannot read, on object ' + reference;
      }

      streams.push(inflated);
    }

    drawings.push(streams.join(''));
  }

  // EVERY page must mark paper, not merely one of them. Testing the pages
  // together let a document whose later pages were blank pass, which matters
  // most for the capability whose whole subject is content repeating across
  // pages.
  const blank = [];
  for (let index = 0; index < drawings.length; index++) {
    if (!marksPaper(drawings[index])) {
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
