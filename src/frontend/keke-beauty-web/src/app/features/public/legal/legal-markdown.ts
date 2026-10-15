/** A block of a legal text. Rendered with Angular templates (never as raw HTML), so the admin-written text cannot inject markup. */
export type LegalBlock =
  | { readonly kind: 'heading'; readonly text: string }
  | { readonly kind: 'paragraph'; readonly text: string }
  | { readonly kind: 'list'; readonly items: readonly string[] };

/**
 * Parses the small Markdown subset the back-office editor documents: `## ` headings, `- ` list items and paragraphs separated
 * by blank lines (single line breaks inside a paragraph are kept as spaces).
 */
export function parseLegalText(body: string): LegalBlock[] {
  const blocks: LegalBlock[] = [];
  let paragraph: string[] = [];
  let items: string[] = [];

  const flush = (): void => {
    if (paragraph.length > 0) {
      blocks.push({ kind: 'paragraph', text: paragraph.join(' ') });
      paragraph = [];
    }
    if (items.length > 0) {
      blocks.push({ kind: 'list', items });
      items = [];
    }
  };

  for (const raw of body.replace(/\r\n/g, '\n').split('\n')) {
    const line = raw.trim();
    if (line === '') {
      flush();
    } else if (line.startsWith('## ')) {
      flush();
      blocks.push({ kind: 'heading', text: line.slice(3).trim() });
    } else if (line.startsWith('- ')) {
      if (paragraph.length > 0) {
        flush();
      }
      items.push(line.slice(2).trim());
    } else {
      if (items.length > 0) {
        flush();
      }
      paragraph.push(line);
    }
  }
  flush();
  return blocks;
}
