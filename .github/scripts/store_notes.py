"""Writes the Android part of a GitHub release's notes, for the app stores.

Usage: store_notes.py <owner/repo> <tag> <output file>
Reads GH_TOKEN. Writes nothing when no change touches the Android app.
"""

import json
import os
import re
import sys
import urllib.request


class StoreNotes:
    LIMIT = 500
    APP_PATHS = ("android/app/src/", "android/protocol/src/main/")
    HIDDEN_TYPES = {"build", "chore", "ci", "docs", "refactor", "style", "test"}
    PULL = re.compile(r"\s+by @\S+ in https://github\.com/\S+/pull/(\d+)\s*$")
    TYPE = re.compile(r"^(\w+)(\([^)]*\))?!?:\s*")

    def __init__(self, repo, token):
        self.repo = repo
        self.token = token

    def for_tag(self, tag):
        body = self._get(f"/releases/tags/{tag}").get("body") or ""
        lines = [self._line(text) for text in body.splitlines()]
        notes = "\n".join(line for line in lines if line)

        return self._fit(notes)

    def _line(self, text):
        match = self.PULL.search(text)

        if not text.startswith("* ") or not match or not self._touches_app(match.group(1)):
            return None

        title = text[2 : match.start()].strip()
        kind = self.TYPE.match(title)

        if kind and kind.group(1).lower() in self.HIDDEN_TYPES:
            return None

        title = self.TYPE.sub("", title)

        return f"• {title[:1].upper()}{title[1:]}"

    def _touches_app(self, number):
        page = 1

        while True:
            files = self._get(f"/pulls/{number}/files?per_page=100&page={page}")

            if any(file["filename"].startswith(self.APP_PATHS) for file in files):
                return True
            if len(files) < 100:
                return False

            page += 1

    def _fit(self, notes):
        if len(notes) <= self.LIMIT:
            return notes

        kept = notes[: self.LIMIT - 2].rsplit("\n", 1)[0]

        return f"{kept}\n…"

    def _get(self, path):
        request = urllib.request.Request(
            f"https://api.github.com/repos/{self.repo}{path}",
            headers={"Authorization": f"Bearer {self.token}", "Accept": "application/vnd.github+json"},
        )

        with urllib.request.urlopen(request, timeout=60) as response:
            return json.load(response)


if __name__ == "__main__":
    repo, tag, output = sys.argv[1:4]
    notes = StoreNotes(repo, os.environ["GH_TOKEN"]).for_tag(tag)

    if notes:
        os.makedirs(os.path.dirname(output) or ".", exist_ok=True)

        with open(output, "w") as file:
            file.write(notes)

    print(notes or "No Android changes in this release's notes.")
