# Dossier WhatsApp Business : modèles, cas d'usage et consentement

À envoyer au fournisseur (Arolitec ou Zavu) avec les pièces demandées dans sa liste de prérequis. Rédigé le 27 octobre 2026.
Ce qui est **entre crochets** est à compléter par toi : je n'invente aucune information sur l'entreprise.

## 1. Informations de l'entreprise (à fournir)

| Élément | Valeur |
|---|---|
| Dénomination sociale exacte | [à compléter : telle qu'elle figure sur le Kbis / RCCM] |
| Justificatif légal | [Kbis, certificat d'immatriculation ou équivalent] |
| Adresse du siège et téléphone | [à compléter] |
| Site web officiel | [à compléter : le nom de domaine, avec une adresse e-mail du même domaine] |
| Justificatif d'adresse ou de numéro | [facture de service ou relevé bancaire] |
| Numéro WhatsApp dédié | [+225 …] : jamais inscrit sur WhatsApp ni WhatsApp Business, capable de recevoir un code par SMS ou appel, détenu par l'entreprise |

### Profil visible par les clientes (proposition)

| Élément | Proposition |
|---|---|
| Nom d'affichage | Keke Beauty |
| Catégorie d'activité | Beauté, spa et salon |
| Description (256 caractères au plus) | Réservez salons de coiffure, onglerie, spa et soins en Côte d'Ivoire. Confirmations et rappels de vos rendez-vous. |
| Logo | image carrée de 640 × 640 px au moins, PNG ou JPG : [à choisir dans `docs/brand/`] |
| Coordonnées | adresse, e-mail de contact, site : [à compléter] |

## 2. Les cas d'usage : ce qui part par WhatsApp, et ce qui reste par e-mail

**Principe** : WhatsApp ne sert qu'aux **clientes** et aux **codes de connexion**. Les partenaires (salons) et la facturation sont prévenus **par e-mail et dans l'application** : moins de modèles à faire valider, moins de messages comptés sur le quota.

| Cas d'usage | Qui | Canal | Modèle |
|---|---|---|---|
| Code de connexion à usage unique | cliente avec numéro lié | WhatsApp | `keke_otp` |
| Demande de rendez-vous envoyée | cliente | WhatsApp + e-mail | `rdv_demande_envoyee` |
| Rendez-vous confirmé | cliente | WhatsApp + e-mail | `rdv_confirme` |
| Demande refusée ou expirée | cliente | WhatsApp + e-mail | `rdv_non_retenu` |
| Rendez-vous annulé par l'établissement | cliente | WhatsApp + e-mail | `rdv_annule` |
| Nouveau créneau proposé par l'établissement | cliente | WhatsApp + e-mail | `rdv_nouveau_creneau` |
| Rendez-vous déplacé (par la cliente) | cliente | WhatsApp + e-mail | `rdv_deplace` |
| Rappel la veille et 2 h avant | cliente | WhatsApp + e-mail | `rdv_rappel` |
| Créneau libéré (liste d'attente) | cliente inscrite | WhatsApp + e-mail | `creneau_libere` |
| Paiement reçu, non abouti, remboursement | cliente | e-mail (WhatsApp facultatif) | `paiement_recu` |
| Invitation à revenir (30 jours après la visite) | cliente avec accord **marketing** | WhatsApp + e-mail | `keke_retour` |
| Demandes, annulations, retards, récapitulatif | partenaire | **e-mail et application seulement** | aucun |
| Factures, rappels d'échéance, reversements | partenaire | **e-mail seulement** | aucun |

## 3. Les modèles à soumettre

Langue : français (`fr`). Chaque exemple de valeur est **réel et vraisemblable** (Meta l'exige pour valider).
Pied de page commun : « Répondez STOP pour ne plus recevoir ces messages » (48 caractères, sans variable).

### 3.1 Authentification

| Champ | Contenu |
|---|---|
| Nom | `keke_otp` |
| Catégorie | **AUTHENTICATION** (format imposé par Meta : contenu très restreint) |
| Corps | `{{1}} est votre code de vérification.` |
| Bouton | « Copier le code » |
| Options | mention « ne partagez pas ce code » activée ; validité du code : 10 minutes |
| Variable | `{{1}}` : le code à 6 chiffres. Exemple : `482915` |

### 3.2 Utilitaires (confirmations, rappels, annulations)

Catégorie **UTILITY** pour tous. Variables communes : `{{1}}` prénom, `{{2}}` établissement, `{{3}}` date, `{{4}}` heure.
Exemples communs : `Awa`, `Maison Éclat Coiffure`, `12/11/2026`, `10:00`.
Exceptions : dans `paiement_recu`, `{{2}}` est le montant (exemple `10 000 F CFA`) et `{{3}}` l'établissement ; dans `rdv_rappel`, `{{1}}` est aussi le prénom.

| Nom | Corps |
|---|---|
| `rdv_demande_envoyee` | Bonjour {{1}}, votre demande de rendez-vous chez {{2}} le {{3}} à {{4}} a bien été envoyée. Vous serez prévenu(e) dès qu'elle sera validée. |
| `rdv_confirme` | Bonjour {{1}}, votre rendez-vous chez {{2}} le {{3}} à {{4}} est confirmé. À bientôt ! |
| `rdv_non_retenu` | Bonjour {{1}}, {{2}} ne peut pas vous recevoir le {{3}} à {{4}}. Vous pouvez choisir un autre créneau sur Keke Beauty. |
| `rdv_annule` | Bonjour {{1}}, votre rendez-vous chez {{2}} prévu le {{3}} à {{4}} a été annulé. Vous pouvez reprendre rendez-vous sur Keke Beauty. |
| `rdv_nouveau_creneau` | Bonjour {{1}}, {{2}} vous propose le {{3}} à {{4}} à la place de votre rendez-vous. Ouvrez Keke Beauty pour accepter ou refuser. |
| `rdv_deplace` | Bonjour {{1}}, votre rendez-vous chez {{2}} a été déplacé au {{3}} à {{4}}. |
| `rdv_rappel` | Rappel : {{1}}, votre rendez-vous chez {{2}} a lieu le {{3}} à {{4}}. Pour modifier ou annuler, ouvrez Keke Beauty. |
| `creneau_libere` | Bonjour {{1}}, un créneau vient de se libérer chez {{2}} le {{3}} à {{4}}. Réservez vite : le premier qui réserve l'obtient. |
| `paiement_recu` | Bonjour {{1}}, votre paiement de {{2}} pour le rendez-vous chez {{3}} a bien été reçu. |

### 3.3 Marketing (uniquement avec un accord marketing explicite)

| Champ | Contenu |
|---|---|
| Nom | `keke_retour` |
| Catégorie | **MARKETING** (facturée plus cher, la plus contrôlée par Meta) |
| Corps | Bonjour {{1}}, votre dernière visite chez {{2}} remonte à quelques semaines. Réservez votre prochain rendez-vous en quelques secondes sur Keke Beauty. |
| Exemples | `Awa`, `Maison Éclat Coiffure` |

**Risque de reclassement** : `creneau_libere` peut être jugé promotionnel par Meta et reclassé en marketing (même texte, facturation plus élevée). Les autres modèles utilitaires sont des suites directes d'une action de la cliente.

### 3.4 Règles de Meta : vérification de ces textes

- Aucune variable au tout début ni à la toute fin ; jamais deux variables d'affilée ; numérotation continue.
- Corps de 1 024 caractères au plus (les modèles ci-dessus en font moins de 200) ; pied de page de 60 au plus ; noms en minuscules avec tirets bas, sans accent.
- Aucun lien raccourci ; pas d'abus de majuscules, d'émojis ou de ponctuation.
- Chaque modèle se comprend seul (établissement, date et heure y figurent).
- Une valeur de variable ne contient ni retour à la ligne, ni tabulation, ni longue suite d'espaces (l'application les remplace déjà).

## 4. Consentement (opt-in) et désinscription (opt-out)

Meta exige un accord **explicite** avant tout message, et la **preuve est à ta charge**.

### Ce qui existe déjà
| Élément | État |
|---|---|
| Lier son numéro suppose de recevoir et de saisir un code reçu sur WhatsApp : la cliente prouve qu'elle contrôle le numéro | ✅ |
| Case « WhatsApp » dans « Mes notifications » : la cliente peut tout couper à tout moment | ✅ |
| Réponse **STOP** à un message : coupe les messages WhatsApp (connecteur Zavu) | ✅ (à reproduire côté Arolitec s'il est choisi) |
| Pied de page « Répondez STOP… » dans chaque message | ✅ dans la proposition ci-dessus |

### Ce qui manque avant la production (je peux le développer)
| Manque | Pourquoi |
|---|---|
| **Texte d'accord affiché au moment de lier le numéro** : « J'accepte de recevoir sur WhatsApp les confirmations et rappels de mes rendez-vous » | Sans texte clair, l'accord n'est pas explicite |
| **Preuve enregistrée** : date et heure, numéro, version du texte, page d'origine | Meta peut la demander ; en cas de plainte, c'est ta défense |
| **Accord marketing séparé**, décoché par défaut, pour `keke_retour` | Un accord pour les confirmations ne couvre pas les offres |
| Aujourd'hui **tous** les messages, y compris l'invitation à revenir, partent avec le même modèle générique : il faut un modèle par message (§3) avant la production | Un message promotionnel envoyé comme « utilitaire » est refusé ou reclassé, avec un effet sur la note de qualité du numéro |

## 5. Limites et bonnes pratiques d'envoi

- Numéro non vérifié : **250 conversations initiées par 24 h**, puis paliers de 1 000, 10 000, 100 000 selon le volume et la qualité.
- Une note de qualité dégradée (blocages, signalements) réduit la limite et peut suspendre l'envoi : n'écrire qu'à des personnes qui ont consenti, au bon moment.
- Plan gratuit de Zavu : 2 000 messages par mois, envoyés **et reçus** ; un rendez-vous coûte environ 3 messages.
- Un message utilitaire envoyé dans les 24 h suivant un message de la cliente est gratuit ; hors fenêtre, Meta facture par message selon le pays du destinataire.

## 6. Liste à cocher (reprise de la checklist du fournisseur)

| Élément | Responsable | Statut |
|---|---|---|
| Accès au Business Manager (idéalement un accès partenaire limité au compte WhatsApp, plutôt que « Administrateur ») | toi | ☐ |
| Acceptation de l'invitation | fournisseur | ☐ |
| Documents de vérification de l'entreprise (§1) | toi | ☐ |
| Numéro non inscrit sur WhatsApp, avec accès pour recevoir le code | toi | ☐ |
| Nom d'affichage, logo, description (§1) | toi | ☐ |
| Cas d'usage (§2) | ce dossier | ☑ |
| Textes, variables et exemples de valeurs (§3) | ce dossier | ☑ |
| Catégorie de chaque modèle (§3) | ce dossier | ☑ |
| Mécanisme d'accord et de désinscription (§4) | à compléter (preuve d'accord) | ☐ |
| Création du sender et soumission des modèles | fournisseur (`python3 scripts/zavu_templates.py` pour Zavu) | ☐ |
