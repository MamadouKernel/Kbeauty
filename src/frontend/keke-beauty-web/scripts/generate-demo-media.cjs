/**
 * Generates the illustrative SVG visuals used by the demonstration establishments (public/media/demo).
 * Pure abstract art in the brand palette: no third-party image, no copyright. Run: node scripts/generate-demo-media.cjs
 */
const fs = require('node:fs');
const path = require('node:path');

const OUTPUT = path.join(__dirname, '..', 'public', 'media', 'demo');

/** icon key (matches the API's category icon) -> [from, to, accent] */
const PALETTES = {
  hair: ['#5E2789', '#A366D2', '#F3D9F1'],
  spa: ['#4A1F6B', '#8B45C2', '#EADCF6'],
  massage: ['#5E2789', '#BC8FE0', '#F6F0FB'],
  nails: ['#7433A8', '#E8A3D8', '#FFFFFF'],
  barber: ['#36174F', '#7433A8', '#D5B8EC'],
  skincare: ['#8B45C2', '#EADCF6', '#FFFFFF'],
  makeup: ['#7433A8', '#E86FA8', '#FCE7F3'],
};

const W = 1200;
const H = 800;

function shapes(variant, accent) {
  if (variant === 1) {
    return `
      <circle cx="930" cy="230" r="260" fill="${accent}" opacity="0.18"/>
      <circle cx="930" cy="230" r="150" fill="${accent}" opacity="0.22"/>
      <path d="M0 640 C 220 560 380 720 620 650 S 1000 560 1200 640 V800 H0Z" fill="${accent}" opacity="0.20"/>
      <path d="M0 700 C 260 640 420 780 680 710 S 1040 650 1200 720 V800 H0Z" fill="${accent}" opacity="0.28"/>`;
  }
  if (variant === 2) {
    return [420, 330, 240, 150, 70]
      .map((r, i) => `<circle cx="300" cy="520" r="${r}" fill="none" stroke="${accent}" stroke-width="${6 + i * 2}" opacity="${0.14 + i * 0.05}"/>`)
      .join('\n      ') +
      `\n      <circle cx="930" cy="190" r="110" fill="${accent}" opacity="0.24"/>`;
  }
  const petals = Array.from({ length: 8 }, (_, i) => {
    const angle = i * 45;
    return `<ellipse cx="600" cy="250" rx="70" ry="210" fill="${accent}" opacity="0.18" transform="rotate(${angle} 600 400)"/>`;
  }).join('\n      ');
  return `${petals}\n      <circle cx="600" cy="400" r="62" fill="${accent}" opacity="0.4"/>`;
}

function svg(key, variant, [from, to, accent]) {
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}" role="img" aria-label="Illustration ${key} ${variant}">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="${from}"/>
      <stop offset="1" stop-color="${to}"/>
    </linearGradient>
  </defs>
  <rect width="${W}" height="${H}" fill="url(#bg)"/>
  <g>
      ${shapes(variant, accent)}
  </g>
</svg>
`;
}

fs.mkdirSync(OUTPUT, { recursive: true });
let count = 0;
for (const [key, palette] of Object.entries(PALETTES)) {
  for (const variant of [1, 2, 3]) {
    fs.writeFileSync(path.join(OUTPUT, `${key}-${variant}.svg`), svg(key, variant, palette));
    count += 1;
  }
}
console.log(`${count} visuals written to ${OUTPUT}`);
