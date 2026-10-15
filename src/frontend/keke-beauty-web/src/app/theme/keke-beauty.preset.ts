import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';

/**
 * Keke Beauty PrimeNG preset (PRD §6.2): Aura base with the plum palette (warm ivory paper, deep plum, gold accents).
 * 600 is used as the interactive primary so white text stays above 4.5:1 contrast.
 */
export const KekeBeautyPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#F8F3FA',
      100: '#EFE3F4',
      200: '#DCC3E7',
      300: '#C39AD5',
      400: '#A56BBF',
      500: '#8A43A5',
      600: '#6A2A85',
      700: '#511368',
      800: '#3F0F52',
      900: '#2D0A3A',
      950: '#1F0629',
    },
    colorScheme: {
      light: {
        primary: {
          color: '{primary.600}',
          contrastColor: '#ffffff',
          hoverColor: '{primary.700}',
          activeColor: '{primary.800}',
        },
        highlight: {
          background: '{primary.50}',
          focusBackground: '{primary.100}',
          color: '{primary.800}',
          focusColor: '{primary.900}',
        },
      },
    },
  },
});
