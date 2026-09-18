# Phase 1 — Contracts: API d'authentification client

## POST /auth/otp/request

**Requête**
```json
{ "telephone": "+2250700000000" }
```

**Réponses**
- `202 Accepted` — code envoyé avec succès (FR-002/FR-003)
  ```json
  { "status": "sent" }
  ```
- `400 Bad Request` — format de numéro invalide (FR-008)
  ```json
  { "status": "invalid_phone", "message": "Format de numero invalide (E.164 attendu)." }
  ```
- `502 Bad Gateway` — échec d'envoi côté fournisseur (FR-009, ex. Zavu non configuré/indisponible)
  ```json
  { "status": "send_failed", "message": "Envoi du code impossible pour le moment." }
  ```

## POST /auth/otp/verify

**Requête**
```json
{ "telephone": "+2250700000000", "code": "123456" }
```

**Réponses**
- `200 OK` — code valide, compte créé ou récupéré (FR-003)
  ```json
  { "status": "verified", "idUtilisateur": "<uuid>", "isNewAccount": true }
  ```
- `400 Bad Request` — code incorrect, expiré ou déjà utilisé (FR-004, FR-005, US3)
  ```json
  { "status": "invalid_or_expired_code" }
  ```

Aucun de ces deux endpoints ne MUST jamais retourner le code OTP lui-même, ni en clair ni haché
(FR-010).
