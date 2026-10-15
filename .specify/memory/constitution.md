# Keke Beauty Constitution

## Core Principles

### I. PRD fait foi
`PRD_Keke_Beauty.md` est la source de vérité. Les sections marquées **[HYPOTHÈSE]** sont implémentées telles quelles mais restent paramétrables. Tout écart au PRD est signalé avant implémentation.

### II. Monolithe modulaire
Une seule API déployable, découpée en modules métier (Identity, Geography, Catalog, Onboarding, Scheduling, Billing, Notifications, Administration), un schéma PostgreSQL par module. Un module n'accède jamais aux tables d'un autre : il passe par son interface publique. Pas de microservices en V1.

### III. Test-First (NON-NEGOTIABLE)
Red → Green → Refactor. Règles métier couvertes par xUnit/FluentAssertions ; intégration par Testcontainers (PostGIS) ; E2E Playwright sur les parcours critiques. Couverture minimale : 80 %.

### IV. Sécurité par défaut
Aucun secret dans le dépôt (variables d'environnement). Toute entrée validée aux frontières (FluentValidation). Erreurs au format ProblemDetails. Autorisation par politique **et** par propriété. Pièces KYC dans un bucket privé.

### V. Design system unique
L'UI suit `design-system/keke-beauty/MASTER.md` (style Soft UI Evolution) et les tokens violets du PRD §6.2, via le preset PrimeNG `KekeBeautyPreset`. Pas de couleur brute dans les composants, pas d'emoji comme icône, WCAG 2.1 AA, `prefers-reduced-motion` respecté, mobile-first (cibles tactiles ≥ 44 px).

### VI. Simplicité et immutabilité
YAGNI : ne rien construire avant le lot qui en a besoin. Préférer les objets immuables (records, `readonly`), fichiers < 800 lignes, fonctions < 50 lignes.

## Contraintes techniques

PostgreSQL 16+ (PostGIS, btree_gist, citext, unaccent, pg_trgm) · .NET 10 / EF Core 10 / Minimal APIs · Angular (standalone, signals, OnPush) + PrimeNG · SignalR · Hangfire · S3/MinIO · Serilog + OpenTelemetry. Code, tables, API et commits en anglais ; interface en français.

## Workflow

Livraison par lots (PRD §12) ; chaque lot est livrable, testé et démontrable avant le suivant. Conventional Commits. Une migration EF par changement de schéma, jamais modifiée après fusion. Définition de « terminé » : PRD §14.

## Governance

Cette constitution prime sur les autres pratiques. Tout amendement est documenté et versionné.

**Version**: 1.0.0 | **Ratified**: 2026-10-11 | **Last Amended**: 2026-10-11
