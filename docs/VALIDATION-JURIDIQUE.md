# Dossier de validation juridique

Les textes de `/cgu` et `/confidentialite` (fichier `src/frontend/keke-beauty-web/src/app/features/public/legal/legal.ts`)
sont des **projets rédigés par l'équipe technique**. Ils ne sont pas validés juridiquement : un juriste ivoirien doit
les relire avant la mise en production. Ce document liste ce qu'il faut lui remettre et ce qui manque.

## À fournir par l'éditeur (informations que l'application ne peut pas inventer)

| Élément | Où l'ajouter |
|---|---|
| Dénomination sociale, forme juridique, capital, RCCM, adresse du siège | Mentions légales (page à créer) et pied de page |
| Directeur de la publication | Mentions légales |
| Adresse e-mail et postale de contact, point de contact pour les droits RGPD / loi 2013-450 | Politique de confidentialité, section « Vos droits » |
| Hébergeur (nom, adresse) | Mentions légales |
| Numéro de récépissé de déclaration ou d'autorisation ARTCI | Politique de confidentialité |
| Tribunal compétent et médiation éventuelle | CGU, article 7 |
| Plafonds de responsabilité, durée de préavis de suspension | CGU, articles 2 et 6 |

## Faits techniques à vérifier par le juriste (ce que l'application fait réellement)

| Sujet | Réalité de l'application |
|---|---|
| Données du compte | Nom, prénom, e-mail, mot de passe haché, secret TOTP ; téléphone et note de rendez-vous |
| Pièce d'identité des professionnels | Stockée dans un bucket privé, accessible aux administrateurs habilités, chaque consultation journalisée ; durée de conservation **à fixer** (paramètre non encore configurable) |
| Localisation | Utilisée côté navigateur pour classer par distance, avec accord, non conservée |
| Cookies | Un seul cookie technique (jeton de session) ; aucun cookie publicitaire ni de mesure d'audience |
| Sous-traitants | Hébergement, passerelle de paiement (WiniPayer ou CinetPay), relais SMTP, fournisseur SMS (non branché), fournisseur de cartes ; **contrats de sous-traitance à établir** |
| Transferts hors Côte d'Ivoire | Dépendent de l'hébergeur et des fournisseurs choisis : à documenter |
| Cartes | Le fournisseur de tuiles reçoit l'adresse IP du visiteur (mentionné dans la politique) |
| Droits | Export JSON et suppression par anonymisation disponibles dans « Mon compte » |
| Facturation | Numérotation `KB-AAAA-NNNNNN`, conservation légale à préciser (durée) |
| Paiement des rendez-vous | Hors ligne, sur place (aucun paiement client dans la V1) |

## Démarches
1. Déclaration ou demande d'autorisation auprès de l'ARTCI (traitements de données personnelles).
2. Relecture des CGU et de la politique par un avocat, puis mise à jour du fichier `legal.ts`.
3. Création de la page « Mentions légales » avec les informations du premier tableau.
