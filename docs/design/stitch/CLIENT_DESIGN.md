# Design System: Keke Beauty — Parcours Client

## 1. Visual Theme & Atmosphere
A warm, gallery-airy beauty concierge with an editorial asymmetry inspired by an Abidjan boutique spa. Density 4/10, variance 7/10, motion 5/10. Discovery pages feel aspirational; booking and payment screens become calmer and more structured as commitment increases.

## 2. Color Palette & Roles
- **Warm Canvas** (#FFFCF8) — global background.
- **Pure Surface** (#FFFFFF) — forms, elevated summaries and receipts.
- **Plum Ink** (#32153F) — primary text and high-contrast controls.
- **Muted Mauve** (#756879) — descriptions and metadata.
- **Whisper Border** (#E8DEEA) — structural separators.
- **Imperial Plum** (#652B79) — the single accent for primary actions, active states and focus rings.
- **Success Jade** (#1B8755) and **Error Crimson** (#BA1A1A) — semantic status colors only.

## 3. Typography Rules
- **Display:** Instrument Serif for short editorial emphasis only.
- **Interface and body:** Plus Jakarta Sans, 14px minimum for supporting copy and 16px for form inputs on mobile.
- Headlines use tight tracking and fluid `clamp()` sizing.
- Dashboard-like history and payment views remain sans-serif.

## 4. Component Stylings
- Buttons use Imperial Plum, a 44px minimum touch target and subtle pressed scale feedback.
- Cards appear only for hierarchy: salon, booking summary, payment status and receipt.
- Inputs always have a visible label, helper or error below, and an Imperial Plum focus ring.
- Loading uses layout-sized skeletons; empty and error states always offer a recovery action.
- Status information never relies on color alone: every badge includes text and an icon where useful.

## 5. Layout Principles
- Mobile-first single-column layout below 768px, contained at 1280px on desktop.
- Discovery uses an asymmetric split hero; transactional screens use a focused two-column desktop grid.
- No page-level horizontal overflow. Small data tables scroll only inside their labelled region.
- Fixed actions account for safe-area insets and never cover content.

## 6. Motion & Interaction
- Motion uses transform and opacity with premium cubic easing.
- Lists reveal with a restrained cascade; active visual elements float by no more than 8px.
- `prefers-reduced-motion` disables nonessential movement.
- Each submit action exposes busy, success and recoverable error states.

## 7. Anti-Patterns (Banned)
No neon glows, pure black, generic serif fonts, oversized centered hero, overlapping content, decorative fake controls, emojis, inaccessible color-only statuses, horizontal page overflow, or placeholder business actions.
