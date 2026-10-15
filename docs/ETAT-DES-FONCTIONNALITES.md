# État des fonctionnalités

Situation au 13 octobre 2026, par exigence du cahier des charges (`PRD_Keke_Beauty.md`).
Légende : ✅ implémenté et testé · 🟡 implémenté, avec une réserve (ce qui n'a pas pu être vérifié est dit) · ⏸ implémenté mais **désactivé** volontairement · ❌ non implémenté.

Tests : 198 unitaires + 178 d'intégration (PostgreSQL et Redis réels via Testcontainers) + 156 Vitest + **109 tests de bout en bout** dans un vrai navigateur (ordinateur et téléphone), dont 30 attaques rejouées (`docs/PENTEST.md`).

## Écarts assumés par rapport au cahier des charges

- **Connexion** : e-mail + mot de passe + double authentification TOTP obligatoire (au lieu du téléphone + OTP SMS), sur votre décision ; connexion Google optionnelle ; connexion par code WhatsApp prête mais ⏸.
- **« Durée des OTP »** : interprétée comme la **durée de session** (déconnexion après inactivité, 60 minutes, réglable par l'admin).
- **Tâches planifiées** : services d'arrière-plan .NET au lieu de Hangfire.
- **PDF des factures** : écrivain PDF maison (QuestPDF et ImageSharp écartés pour leur licence) ; SkiaSharp pour les images.
- **PrimeNG** : version gratuite 21.1.10.

## Identité et comptes

| Exigence | État | Remarque |
|---|---|---|
| AUTH-01 Inscription / connexion | ✅ | E-mail + mot de passe + TOTP, parcours complet rejoué dans le navigateur (courrier capté par Mailpit) |
| AUTH-01 bis Connexion / liaison WhatsApp | ⏸ | Code envoyé par WhatsApp puis TOTP ; **aucun fournisseur WhatsApp Business n'est branché** (la documentation LeTexto fournie n'en décrit pas) : les messages vont au journal |
| AUTH-02 Comptes admin | ✅ | SuperAdmin créé au démarrage, administrateurs nommés par un SuperAdmin |
| AUTH-03 Sessions | ✅ | Liste, révocation, suspension ; **déconnexion après inactivité réglable** (60 min par défaut), côté serveur et dans l'application |
| Droits RGPD / loi 2013-450 | ✅ | Consentement daté, export JSON, suppression de compte ; **durée de conservation des pièces d'identité réglable** par l'admin, effacement automatique |

## Annuaire public

| Exigence | État | Remarque |
|---|---|---|
| SRCH-01 à 05 Recherche | ✅ | PostGIS, liste et carte |
| EST-01 Médias | ✅ | Photos puis **vidéos** (MP4/WebM, 3 par fiche, 50 Mo, formule Premium), lecture avec requêtes d'intervalle, affiche et durée |
| EST-02 à 03, 05, 06 | ✅ | |
| Fiche en onglets (inspirée de la v1.0) | ✅ | Photo de couverture, note moyenne, **ouvert maintenant / ouvre à…**, puis onglets **Prestations et tarifs** (prix, durée et bouton **Réserver** sur chaque ligne : la prestation est déjà cochée à la réservation), **Réalisations**, **Avis**, **À propos** (horaires, paiements, carte) ; l'onglet s'ouvre par lien (`?onglet=avis`) |
| Avis clients | ✅ | Un avis (1 à 5 étoiles, commentaire facultatif) **uniquement après un rendez-vous terminé**, une seule fois par rendez-vous (index unique), dans les 90 jours ; auteur affiché « Awa K. » ; **réponse publique du partenaire** (page `/pro/etablissements/{id}/avis`) ; masquage par un administrateur avec motif (API, pas encore d'écran) ; note moyenne sur les cartes, la fiche et un tri **Mieux notés** |
| Comptoir (cliente sans compte) | ✅ | Le partenaire enregistre une cliente présente (`/pro/etablissements/{id}/comptoir`) : prestations, nom, téléphone facultatif, paiement reçu ; rendez-vous **confirmé aussitôt** sur la première personne libre, sans règle de délai ; pas de notification (la cliente n'a pas de compte) ni de rappel ; une cliente de comptoir ne peut pas laisser d'avis |
| Pass QR et scanner | ✅ | La cliente affiche un **QR code** depuis « Mes rendez-vous » (jeton signé, limité dans le temps, valable jusqu'à 6 h après la fin du rendez-vous) ; le partenaire le lit avec la caméra (navigateurs qui savent le faire) ou le colle (`/pro/etablissements/{id}/scanner`) ; un pass d'un autre établissement est refusé |
| Favoris | ✅ | Cœur sur les cartes, la carte plein écran et la fiche, page **Mes favoris** ; un visiteur non connecté est envoyé se connecter puis ramené |
| Carte plein écran | ✅ | La carte est l'écran principal, avec un carrousel de salons en bas (nom, commune et quartier, téléphone, nombre de prestations, note, **Détails** / **Réserver**) : glisser les cartes déplace la carte, toucher un point avance sa carte ; filtres repliés derrière « Filtres » ; utilisable au clavier |
| Navigation mobile | ✅ | Quatre onglets : **Explorer, Carte, Mes RDV, Mon espace** |
| Mises à jour de l'application | ✅ | Un visiteur qui revient passe tout seul à la nouvelle version (un rechargement) |
| EST-04 « S'y rendre » | 🟡 | Google Maps, Apple Plans et **Yango** (lien avec coordonnées vérifié en émulation téléphone) ; **ouverture réelle de l'application Yango non testable sans téléphone** ; identifiant partenaire `ref` à demander à Yango |

## Rendez-vous

| Exigence | État | Remarque |
|---|---|---|
| RDV-01 à 03, 05, 06 | ✅ | Créneaux, double réservation impossible, reprogrammation |
| RDV-04 Notifications | 🟡 | In-app temps réel ✅, e-mail ✅ (SMTP/LeTexto), push ✅ côté serveur et client, SMS ⏸ (LeTexto configuré, désactivé) ; **aucun envoi réel vers un fournisseur n'a été fait** |
| Rappels automatiques | ✅ | 1er et 2e rappels (24 h et 2 h par défaut, réglables), par les canaux choisis par le client, sans doublon |
| Paiement de la prestation | ✅ | **Le client paie l'établissement directement** : espèces sur place ou Mobile Money sur les comptes que l'établissement déclare (Wave, Orange Money, MTN, Moov). Affichés sur la fiche et à la réservation ; la plateforme n'encaisse rien et ne prélève aucune commission. **La boutique marque le rendez-vous « payé »** (espèces, ou Mobile Money avec la référence de la transaction, obligatoire) depuis son agenda ; le client voit « Réglé à l'établissement (Wave, réf. …) » dans ses rendez-vous |
| Paiement en ligne du rendez-vous via WiniPayer (acompte, commission, remboursement, reversement) | ⏸ | Développé puis **mis hors service** : plus accessible depuis l'interface (réglage administrateur retiré, pages retirées), désactivé par défaut. Code conservé |

## Espace partenaire

| Exigence | État | Remarque |
|---|---|---|
| ONB-01 à 04, SVC-01 à 03, AGD-01 à 08 | ✅ | Galerie photos **et vidéos** |
| Moyens de paiement de l'établissement | ✅ | Espèces et jusqu'à 4 comptes Mobile Money (opérateur, numéro, titulaire), page « Moyens de paiement » |

## Abonnements et paiement

| Exigence | État | Remarque |
|---|---|---|
| BIL-01 Formules paramétrables | ✅ | **L'admin crée, duplique et modifie autant de formules qu'il veut** : nom, étiquette, description, prix mensuel/annuel (0 = rythme non proposé), engagement (1 à 36 mois), limites (photos, vidéos, employés/ressources, rendez-vous par mois, vide = illimité) et options (réservation, statistiques, mise en avant). Les pages Tarifs et Abonnement se construisent à partir de ces réglages. Limites appliquées à l'import de photos et vidéos, à l'ajout de ressources et à la réservation. **Période d'essai** par formule (0 à 90 jours, une seule fois par établissement et par formule) : fonctionnalités immédiates sans paiement, souscription anticipée possible sans perdre de jours, retour à la formule Gratuite si rien n'est payé à l'échéance. **Changement de formule en cours d'engagement** : montée en gamme immédiate avec crédit au prorata de la période déjà payée (devis affiché avant confirmation, montant arrondi aux règles de la passerelle) ; descente en gamme programmée à la fin de l'engagement, annulable jusque-là. **Promotions en pourcentage** : remise directe sur une formule (1 à 90 %, date de fin, nombre de factures remisées) et **codes promo** créés par l'admin (validité, utilisations maximales, formules concernées, désactivation) ; le meilleur des deux s'applique, jamais les deux ; prix barré sur Tarifs et Abonnement. Une montée en gamme n'est pas remisée et le crédit se calcule sur la somme réellement payée. Pas de formule privée par boutique ; un seul abonnement actif à la fois |
| BIL-03, 05, 06, 07 | ✅ | Prix et durée d'engagement figés à la souscription |
| BIL-02, 08 Souscription et moyens de paiement | 🟡 | **WiniPayer ne sert qu'aux abonnements des boutiques.** Passerelle de développement testée ; **WiniPayer et CinetPay jamais exécutées contre leurs serveurs** |
| BIL-04 Relances | 🟡 | Notifications ✅ ; e-mails si SMTP/LeTexto configuré |

## Notifications

| Exigence | État | Remarque |
|---|---|---|
| NOT-01 Canaux | 🟡 | In-app ✅, e-mail ✅, push Web (VAPID) ✅ côté code et tests, SMS ⏸, WhatsApp ⏸. **Push réel non testé** (nécessite le service push d'un navigateur sur HTTPS) |
| NOT-02 Modèles éditables | ✅ | Y compris rappels et paiements |
| NOT-03 / NOT-04 | ✅ | Journal d'envoi, 3 tentatives, accusés de réception LeTexto (signés par secret) |
| Préférences client | ✅ | Canaux, rappels, liaison WhatsApp, activation du push par appareil |

## Back-office

| Exigence | État | Remarque |
|---|---|---|
| ADM-01 à 04, 06, 07 | ✅ | |
| ADM-05 Paramétrage | ✅ | Plans, catégories, zones, messages, **textes légaux (éditeur + historique des versions)**, délai de grâce, inactivité, conservation des pièces, rappels, canaux |
| Canaux (SMS, WhatsApp, push, e-mail) | ✅ | État de chaque canal, solde LeTexto, SMS de test, demande d'expéditeur |
| **Sauvegarde de la base** (SuperAdmin) | ✅ | `pg_dump` réel depuis l'application : créer, télécharger (archive vérifiée dans le navigateur), supprimer, planification et rétention ; **la restauration reste en ligne de commande** (`docs/DEPLOYMENT.md` §10) |

## Exigences non fonctionnelles

| Domaine | État | Remarque |
|---|---|---|
| Sécurité | ✅ / 🟡 | En-têtes, CSP, limitation de débit, verrouillage de compte, jetons ; **tests d'intrusion rejoués (30 scénarios, `docs/PENTEST.md`)** ; aucune dépendance vulnérable. Pas d'audit externe ni de scanner automatisé |
| Données personnelles | 🟡 | Textes légaux **éditables mais non validés par un juriste** (`docs/VALIDATION-JURIDIQUE.md`), déclaration ARTCI à faire |
| Accessibilité WCAG 2.1 AA | ✅ / 🟡 | Lighthouse **100** sur accueil, annuaire, fiche, tarifs, CGU, connexion ; axe-core sans violation sur 10 pages publiques, ordinateur et téléphone. Pas de test avec lecteur d'écran (décision) |
| PWA | ✅ / 🟡 | Manifeste, service worker, **ouverture hors ligne**, émulation téléphone (Pixel 7) dans la suite. Pas testé sur un vrai téléphone |
| Observabilité | ✅ | **OpenTelemetry** : traces (requêtes, appels sortants, PostgreSQL, spans métier) et métriques, export OTLP, Jaeger en option. Non vérifié contre un collecteur réel (testé : activation, spans et compteurs) |
| Scalabilité temps réel | ✅ | **Backplane Redis** pour SignalR, vérifié avec deux serveurs et un vrai Redis |
| Performance | ✅ / 🟡 | Latences mesurées sur 50 000 fiches dans les budgets ; bundle initial 156 Ko compressés. **Test de charge sur l'environnement final : à votre charge** |
| Responsive | ✅ / 🟡 | Aucun débordement horizontal vérifié à 412 px et 1280 px dans la suite de bout en bout ; mise à l'échelle dès 1920 px. Pas testé sur de vrais appareils |
| Cartes | 🟡 | Tuiles OpenStreetMap servies **par le cache du site** (`/tiles/…` : plus de dépendance du navigateur à un tiers) ; les tuiles gratuites de CARTO exigent désormais une clé d'API (elles renvoient une image « API KEY REQUIRED »). **À remplacer en production par un fournisseur avec contrat** (`MAP_TILES_URL`) et à identifier proprement auprès d'OpenStreetMap (`nginx.conf`) |
| Tests de bout en bout | ✅ | `e2e/` (Playwright, Edge), rapport HTML, voir `e2e/README.md` |
| Documentation | ✅ | `DEPLOYMENT.md`, `PRODUCTION-CHECKLIST.md`, `PENTEST.md`, `VALIDATION-JURIDIQUE.md` |

## Ce qui reste, honnêtement

1. **Brancher et essayer les vrais fournisseurs** : clé et expéditeurs LeTexto (puis `LETEXTO_ENABLED=true`), WiniPayer (abonnements des boutiques, seul usage prévu), un fournisseur WhatsApp Business, un collecteur OpenTelemetry. Tout est configuré et testé contre des simulations.
2. **Essais sur de vrais téléphones** : Yango, installation PWA, notifications push, rendu.
3. **Validation juridique** des textes et déclaration ARTCI.
4. **Test de charge** sur l'environnement final (à votre charge) et audit de sécurité externe avant l'ouverture.
5. **Restauration de sauvegarde** : procédure documentée, à répéter une fois sur un serveur de test.

## Hors périmètre V1 (cahier des charges)

Applications natives (Capacitor), avis et notes, favoris, messagerie client ↔ établissement.
