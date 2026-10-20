#!/bin/sh
# Activates the repository's git hooks (secret scan before each commit). Run once per clone.
set -eu
cd "$(dirname "$0")/.."
chmod +x scripts/git-hooks/*
git config core.hooksPath scripts/git-hooks
echo "Git hooks enabled (scripts/git-hooks)."
