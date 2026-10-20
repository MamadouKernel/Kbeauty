# Questions à adresser au support WiniPayer

À envoyer depuis l'adresse du titulaire du compte, avant le passage en production. Complétez les champs entre chevrons. **N'y joignez jamais de jeton, de clé privée ni de capture montrant une clé** : le support n'en a pas besoin pour répondre.

---

**Objet : Keke Beauty (API Checkout 2.0) – confirmations avant passage en production**

Bonjour,

Nous intégrons l'API Checkout 2.0 (`checkout/standard`) pour encaisser des abonnements en F CFA (XOF) et préparons la mise en production de notre compte marchand **Keke Beauty** (référence `<référence du compte marchand>`, environnement TEST). Les essais en bac à sable sont concluants ; certains points ne figurent pas dans la documentation et conditionnent notre mise en production. Pourriez-vous nous les confirmer par écrit ?

**1. Notifications (`callback_url` / IPN)**
- En environnement TEST, aucune requête n'est reçue sur notre `callback_url` après un paiement réussi. Est-ce attendu ? Le rappel est-il envoyé en PROD ?
- Quels sont la méthode HTTP, le format et le contenu du rappel ? Comment l'authentifier (signature, en-tête, adresses IP d'envoi à autoriser) ?
- Quelle est la politique de nouvelle tentative en cas d'échec, et quel code de réponse attendez-vous ?
- Quel est le rôle du champ « Lien IPN » du compte marchand par rapport au `callback_url` envoyé à la création ? Peut-on utiliser les deux ?

**2. États et vérification**
- Quelle est la liste complète des états d'une facture (`state`) et de leur signification (en attente, succès, échec, annulée, expirée, remboursée…) ?
- Le champ `hash` de la réponse de détail (`sha256(clé privée . uuid . crypto . montant . date)`) doit-il être contrôlé systématiquement ? Quelle est la forme exacte de la chaîne concaténée (séparateurs, format de la date, montant brut ou formaté) ?
- Y a-t-il une limite de fréquence pour l'appel `checkout/standard/detail/{uuid}` ?

**3. Environnement TEST**
- Comment simuler un paiement **refusé**, **abandonné** ou **expiré** en TEST ? Le paiement TEST se valide seul après l'affichage du QR code « WiniPayer Mobile » ; l'application est-elle disponible, et à quelle date ?

**4. Passage en production**
- Quels documents et quels délais pour le passage en PROD ? Y a-t-il des conditions particulières pour une plateforme qui encaisse pour son propre compte (abonnements) puis verse des fonds à des établissements ?
- Une nouvelle paire de jetons est-elle délivrée en PROD, et les jetons TEST restent-ils utilisables ensuite ?

**5. Montants et frais**
- Limites minimale et maximale par transaction, par jour et par mois (le compte affiche un cumul mensuel restant) ?
- Frais exacts par opérateur pour un paiement et pour un virement, et la différence entre `client_pay_fee=true` et `false`. Dans nos essais, un paiement de 15 000 XOF a crédité 14 850 XOF (1 %) alors que l'exemple de la documentation cite 2,5 %.
- Délai de mise à disposition des fonds (`amount_available`) et modalités de retrait.

**6. Virements et remboursements (API Payout)**
- La liste blanche d'adresses IP du Manager (« IPs whitelist (Payout) », 3 au plus) est-elle obligatoire pour `payout/standard/create` ? Si oui, comment faire avec une infrastructure à adresse sortante variable ?
- Liste des opérateurs disponibles par pays, limites et frais de virement, états possibles d'un virement et de chaque bénéficiaire (la documentation montre `waitting`), contenu du rappel de fin de virement.
- Existe-t-il une API de **remboursement** d'un paiement Checkout ? Sinon, la procédure recommandée ?

**7. Sécurité**
- Double authentification pour la connexion au Manager, gestion de plusieurs utilisateurs avec des droits limités, rotation des jetons sans interruption de service.
- Procédure en cas de compromission d'un jeton (révocation immédiate).

Merci d'avance. Nous restons disponibles pour un échange technique.

Cordialement,
`<nom, fonction, téléphone>`
