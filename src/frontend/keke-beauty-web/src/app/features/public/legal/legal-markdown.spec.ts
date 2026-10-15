import { describe, expect, it } from 'vitest';
import { parseLegalText } from './legal-markdown';

describe('parseLegalText', () => {
  it('splits headings, paragraphs and lists', () => {
    const blocks = parseLegalText(
      '## Objet\nPremière ligne\nseconde ligne\n\n- un\n- deux\n\nFin du texte',
    );

    expect(blocks).toEqual([
      { kind: 'heading', text: 'Objet' },
      { kind: 'paragraph', text: 'Première ligne seconde ligne' },
      { kind: 'list', items: ['un', 'deux'] },
      { kind: 'paragraph', text: 'Fin du texte' },
    ]);
  });

  it('closes a paragraph before a list and a list before a paragraph, with no blank line', () => {
    expect(parseLegalText('Intro\n- a\nSuite')).toEqual([
      { kind: 'paragraph', text: 'Intro' },
      { kind: 'list', items: ['a'] },
      { kind: 'paragraph', text: 'Suite' },
    ]);
  });

  it('keeps markup as text and handles Windows line endings and empty input', () => {
    expect(parseLegalText('<script>alert(1)</script>\r\n\r\n## <b>Titre</b>')).toEqual([
      { kind: 'paragraph', text: '<script>alert(1)</script>' },
      { kind: 'heading', text: '<b>Titre</b>' },
    ]);
    expect(parseLegalText('')).toEqual([]);
  });
});
