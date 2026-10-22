#!/usr/bin/env python3
"""Creates the WhatsApp templates Keke Beauty sends through Zavu and submits them to Meta for approval.

    python3 scripts/zavu_templates.py create     # creates the two templates that do not exist yet, then submits them
    python3 scripts/zavu_templates.py status     # name, language, status and rejection reason of every template

The API key is read from ZAVU_API_KEY (environment or .env) and never printed. A test key (zv_test_) cannot submit templates:
use the live key once the WhatsApp Business account is connected. ZAVU_SENDER_ID selects the WhatsApp account (optional).
Template names must match WhatsApp__OtpTemplate / WhatsApp__NotificationTemplate (keke_otp, keke_notification by default).
"""
import json
import os
import sys
import urllib.error
import urllib.request

BASE = os.environ.get("ZAVU_BASE_URL", "https://api.zavu.dev")

TEMPLATES = [
    {
        "name": "keke_otp",
        "language": "fr",
        "category": "AUTHENTICATION",
        "body": "{{1}} est votre code de vérification.",
        "buttons": [{"type": "otp", "text": "Copier le code", "otpType": "COPY_CODE"}],
        "addSecurityRecommendation": True,
        "codeExpirationMinutes": 10,
        "examples": {"var1": "123456"},
    },
    {
        "name": "keke_notification",
        "language": "fr",
        "category": "UTILITY",
        "body": "Bonjour, une information de Keke Beauty : {{1}} - {{2}}. Pour ne plus recevoir ces messages, répondez STOP.",
        "variables": ["titre", "message"],
        "examples": {
            "var1": "Rendez-vous confirmé",
            "var2": "Votre rendez-vous chez Salon Éclat le 12/11/2026 à 10:00 est confirmé",
        },
    },
]


def setting(name):
    value = os.environ.get(name)
    if value:
        return value
    try:
        with open(os.path.join(os.path.dirname(__file__), "..", ".env"), encoding="utf-8") as env:
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
    if setting("ZAVU_SENDER_ID"):
        headers["Zavu-Sender"] = setting("ZAVU_SENDER_ID")
    request = urllib.request.Request(BASE + path, method=method, headers=headers,
                                     data=json.dumps(body).encode() if body is not None else None)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", "replace")[:500]
        sys.exit(f"{method} {path} -> HTTP {error.code}: {detail}")


def existing():
    items, cursor = [], None
    for _ in range(5):
        page = call("GET", "/v1/templates?limit=100" + (f"&cursor={cursor}" if cursor else ""))
        items += page.get("items", [])
        cursor = page.get("nextCursor")
        if not cursor:
            break
    return items


def create():
    known = {(t["name"], t["language"]) for t in existing()}
    for template in TEMPLATES:
        if (template["name"], template["language"]) in known:
            print(f"{template['name']}: already exists, skipped")
            continue
        payload = {k: v for k, v in template.items() if k != "examples"}
        created = call("POST", "/v1/templates", payload)
        submit = {"category": template["category"], "variableExamples": template["examples"]}
        if setting("ZAVU_SENDER_ID"):
            submit["senderId"] = setting("ZAVU_SENDER_ID")
        call("POST", f"/v1/templates/{created['id']}/submit", submit)
        print(f"{template['name']}: created and submitted to Meta (review takes 1 h to 72 h)")


def status():
    for t in existing():
        reason = t.get("rejectionReason")
        print(f"{t['name']} [{t['language']}] {t.get('status')} {t.get('category', '')}" + (f" - {reason}" if reason else ""))


if __name__ == "__main__":
    {"create": create, "status": status}.get(sys.argv[1] if len(sys.argv) > 1 else "", lambda: sys.exit(__doc__))()
