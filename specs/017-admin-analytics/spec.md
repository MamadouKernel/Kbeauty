# Feature Specification: Tableau de bord global Super-Admin

**Feature Branch**: `017-admin-analytics`
**Created**: 2026-09-19 | **Status**: Draft

**Input**: "Parcours 6 (Super-admin avance), perimetre reel : un tableau de bord agregeant des
statistiques plateforme (etablissements, RDV, revenu estime, abonnements, avis) en lecture seule pour
l'administrateur, reutilisant l'authentification AdminApiKeyFilter existante. Les litiges et acomptes
(gestion de conciergerie, resolution de differends) restent HORS PERIMETRE : aucune entite 'litige'
n'est modelisee dans le projet a ce jour, et en creer une pour une simple vue globale serait fabriquer
une fonctionnalite non demandee par un besoin reel documente - ce sera un cycle spec Kit distinct si
le besoin se confirme."

## User Scenarios & Testing

### User Story 1 - Consulter les statistiques globales de la plateforme (Priority: P1)
En tant qu'administrateur, je veux voir en un coup d'œil l'état de la plateforme (nombre
d'établissements, RDV, revenu estimé, abonnements, avis) pour piloter l'activité.

**Acceptance Scenarios**:
1. **Given** des données réelles en base, **When** l'administrateur ouvre le tableau de bord, **Then**
   les compteurs affichés correspondent exactement aux données réelles (pas d'estimation arbitraire).
2. **Given** une plateforme sans aucune donnée dans une catégorie (ex: aucun avis), **When**
   l'administrateur consulte le tableau de bord, **Then** le compteur affiche 0, jamais une valeur
   fictive.

## Requirements
- **FR-001**: Le système DOIT exposer un endpoint admin (protégé par la clé API admin existante)
  retournant : nombre d'établissements par statut KYC, nombre de RDV par statut, revenu estimé total
  (RDV confirmés/terminés), nombre d'abonnements par statut, nombre d'avis et note moyenne globale.
- **FR-002**: Toutes les valeurs DOIVENT provenir d'agrégations SQL réelles, jamais de constantes.
- **FR-003**: Le système NE DOIT introduire aucune nouvelle entité métier (litige, acompte) sans
  besoin fonctionnel documenté au-delà de l'affichage de compteurs.

## Success Criteria
- **SC-001**: Le tableau de bord se charge en moins de 3 secondes.
- **SC-002**: 100% des compteurs affichés sont vérifiables par une requête SQL directe équivalente.

## Assumptions
- La gestion des litiges/acomptes et la conciergerie (Parcours 6 complet) restent un chantier séparé,
  nécessitant une modélisation MERISE dédiée (entité Litige, workflow de résolution) — non construite
  ici pour éviter de fabriquer une fonctionnalité sans backend réel derrière.
