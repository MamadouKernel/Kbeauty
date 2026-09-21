# Quickstart: Historique des rendez-vous client et paiement en ligne

Prérequis : stack Docker déjà démarrée (`docker compose up -d`), migration `0008_transaction_rdv.sql`
appliquée (`scripts/db/migrate.sh`), un client déjà authentifié (`idClient`) et un établissement avec
au moins une prestation.

## 1. Historique vide
```bash
curl -s http://localhost:8081/rdv -H "X-Client-Id: <idClient-sans-rdv>"
# Attendu : []
```

## 2. Demande de RDV avec paiement sur place (comportement inchangé)
```bash
curl -s -X POST http://localhost:8081/rdv \
  -H "X-Client-Id: <idClient>" -H "Content-Type: application/json" \
  -d '{"idEtablissement":"<id>","idPrestation":"<id>","dateHeureDebut":"2026-09-20T14:30:00Z"}'
# Attendu : { "idRdv": "...", "statut": "DEMANDE" }  (pas de champ "paiement")
```

## 3. Demande de RDV avec paiement en ligne
```bash
curl -s -X POST http://localhost:8081/rdv \
  -H "X-Client-Id: <idClient>" -H "Content-Type: application/json" \
  -d '{"idEtablissement":"<id>","idPrestation":"<id>","dateHeureDebut":"2026-09-20T16:00:00Z","payerEnLigne":true}'
# Attendu (env TEST WiniPayer configure) :
# { "idRdv": "...", "statut": "DEMANDE", "paiement": { "statutPaiement": "EN_COURS", "lienPaiement": "https://...", "referenceExterne": "..." } }
```

## 4. Vérifier l'historique montre le paiement en attente
```bash
curl -s http://localhost:8081/rdv -H "X-Client-Id: <idClient>"
# Attendu : le RDV de l'étape 3 apparaît avec "statutPaiement": "EN_COURS"
```

## 5. Simuler le callback WiniPayer (succès)
Utiliser la même méthode de calcul de hash que 008 (`sha256(privateKey+uuid+crypto+amount+created_at)`)
avec la `referenceExterne` reçue à l'étape 3 :
```bash
curl -s -X POST http://localhost:8081/webhooks/winipayer/callback \
  -H "Content-Type: application/json" \
  -d '{"uuid":"<referenceExterne>","crypto":"XOF","amount":15000,"created_at":"...","state":"success","operator":"wave-cote-divoire","hash":"<hash-calcule>"}'
# Attendu : { "status": "applied" }
```

## 6. Re-vérifier l'historique : paiement passé à REUSSIE
```bash
curl -s http://localhost:8081/rdv -H "X-Client-Id: <idClient>"
# Attendu : "statutPaiement": "REUSSIE" pour ce RDV ; "statutRdv" reste "DEMANDE" (FR-010, non affecté)
```

## 7. Idempotence : retenter le paiement en ligne sur le même RDV
```bash
curl -s -X POST http://localhost:8081/rdv/<idRdv>/paiement/verifier -H "X-Client-Id: <idClient>"
# Attendu : { "statutPaiement": "REUSSIE" } - aucune nouvelle ligne transaction_rdv creee (verifier en base : SELECT count(*) FROM transaction_rdv WHERE id_rdv = '<idRdv>' doit valoir 1)
```

## 8. Cas agrégateur non configuré (PROD désactivée, cf. .env)
```bash
curl -s -X POST http://localhost:8081/rdv/<idRdv>/paiement/verifier -H "X-Client-Id: <idClient>"
# Attendu si Billing:WiniPayer:ProdPrivateKey vide et env=prod : 503 { "status": "not_configured" }
```
