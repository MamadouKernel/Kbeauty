#!/usr/bin/env python3
"""Creates the WhatsApp templates Keke Beauty sends through Zavu and submits them to Meta for approval.

    python3 scripts/zavu_templates.py list       # what would be created (name, category, body), nothing is sent
    python3 scripts/zavu_templates.py create     # creates the templates that do not exist yet, then submits them to Meta
    python3 scripts/zavu_templates.py status     # name, status and rejection reason of every template

The texts come from docs/WHATSAPP-TEMPLATES.md (the dossier sent to the provider): one source of truth, and a unit test checks that the
code and the dossier agree. The API key is read from ZAVU_API_KEY (environment or .env) and never printed. A test key (zv_test_)
cannot submit templates: use the live key once the WhatsApp Business account is connected. ZAVU_SENDER_ID selects the WhatsApp account;
without it the only sender of the project is used.
"""
import json
import os
import re
import sys
import urllib.error
import urllib.request

BASE = os.environ.get("ZAVU_BASE_URL", "https://api.zavu.dev")
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
FOOTER = "Répondez STOP pour ne plus recevoir ces messages"

APPOINTMENT_EXAMPLES = {"var1": "Awa", "var2": "Maison Éclat Coiffure", "var3": "12/11/2026", "var4": "10:00"}
EXAMPLES = {
    "paiement_recu": {"var1": "Awa", "var2": "10 000 F CFA", "var3": "Maison Éclat Coiffure"},
    "keke_retour": {"var1": "Awa", "var2": "Maison Éclat Coiffure"},
}

OTP = {
    "name": "keke_otp",
    "language": "fr",
    "whatsappCategory": "AUTHENTICATION",
    "body": "{{1}} est votre code de vérification.",
    "buttons": [{"type": "otp", "text": "Copier le code", "otpType": "COPY_CODE"}],
    "addSecurityRecommendation": True,
    "codeExpirationMinutes": 10,
    "examples": {"var1": "482915"},
}


def templates_from_dossier():
    """The utility and marketing rows of the dossier: | `name` | body |, under the heading of their category."""
    category = None
    found = []
    with open(os.path.join(ROOT, "docs", "WHATSAPP-TEMPLATES.md"), encoding="utf-8") as dossier:
        for line in dossier:
            if line.startswith("### "):
                category = "UTILITY" if "Utilitaires" in line else "MARKETING" if "Marketing" in line else None
            row = re.match(r"^\| `([a-z_]+)` \| (.*\{\{1\}\}.*) \|\s*$", line)
            if category and row:
                name, body = row.group(1), row.group(2).strip()
                variables = [f"var{n}" for n in re.findall(r"\{\{(\d+)\}\}", body)]
                found.append({
                    "name": name, "language": "fr", "whatsappCategory": category, "body": body, "footer": FOOTER,
                    "variables": variables, "examples": EXAMPLES.get(name, APPOINTMENT_EXAMPLES),
                })
    return [OTP] + found


def setting(name):
    value = os.environ.get(name)
    if value:
        return value
    try:
        with open(os.path.join(ROOT, ".env"), encoding="utf-8") as env:
            for line in env:
                if line.startswith(name + "="):
                    return line.split("=", 1)[1].strip()
    except OSError:
        pass
    return ""


def call(method, path, body=None):
    key = setting("ZAVU_API_KEY")
    if not key:
        sys.exit("ZAVU_API_KEY is not set (environment or .env).")
    headers = {"Authorization": f"Bearer {key}", "Content-Type": "application/json"}
    request = urllib.request.Request(BASE + path, method=method, headers=headers,
                                     data=json.dumps(body).encode() if body is not None else None)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", "replace")[:500]
        sys.exit(f"{method} {path} -> HTTP {error.code}: {detail}")


def sender_id():
    configured = setting("ZAVU_SENDER_ID")
    if configured:
        return configured
    senders = call("GET", "/v1/senders").get("items", [])
    if len(senders) != 1:
        sys.exit(f"{len(senders)} senders found: set ZAVU_SENDER_ID to the WhatsApp account to use.")
    return senders[0]["id"]


def existing():
    items, cursor = [], None
    for _ in range(5):
        page = call("GET", "/v1/templates?limit=100" + (f"&cursor={cursor}" if cursor else ""))
        items += page.get("items", [])
        cursor = page.get("nextCursor")
        if not cursor:
            break
    return items


def list_templates():
    for t in templates_from_dossier():
        print(f"{t['name']} [{t['whatsappCategory']}] {len(t['body'])} characters: {t['body']}")


def create():
    known = {(t["name"], t["language"]) for t in existing()}
    sender = sender_id()
    for template in templates_from_dossier():
        if (template["name"], template["language"]) in known:
            print(f"{template['name']}: already exists, skipped")
            continue
        payload = {k: v for k, v in template.items() if k != "examples"}
        created = call("POST", "/v1/templates", payload)
        call("POST", f"/v1/templates/{created['id']}/submit",
             {"senderId": sender, "category": template["whatsappCategory"], "variableExamples": template["examples"]})
        print(f"{template['name']}: created and submitted to Meta (review takes minutes to 24 h)")


def status():
    for t in existing():
        reason = t.get("rejectionReason")
        print(f"{t['name']} [{t['language']}] {t.get('status')} {t.get('category', '')}" + (f" - {reason}" if reason else ""))


if __name__ == "__main__":
    {"list": list_templates, "create": create, "status": status}.get(sys.argv[1] if len(sys.argv) > 1 else "", lambda: sys.exit(__doc__))()
