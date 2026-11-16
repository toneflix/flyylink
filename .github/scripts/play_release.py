"""Uploads an Android bundle to Google Play once and releases it on a track and every track below it.

Usage: play_release.py <package> <bundle.aab> <track>
Reads the service account JSON key from PLAY_SERVICE_ACCOUNT_JSON.
"""

import json
import os
import sys

from google.oauth2 import service_account
from googleapiclient.discovery import build
from googleapiclient.http import MediaFileUpload


class PlayRelease:
    TRACKS = ["production", "beta", "alpha", "internal"]
    SCOPE = "https://www.googleapis.com/auth/androidpublisher"

    def __init__(self, package, key):
        credentials = service_account.Credentials.from_service_account_info(key, scopes=[self.SCOPE])

        self.package = package
        self.edits = build("androidpublisher", "v3", credentials=credentials, cache_discovery=False).edits()

    @classmethod
    def tracks_from(cls, track):
        if track not in cls.TRACKS:
            raise ValueError(f"Unknown track {track}. Use one of {', '.join(cls.TRACKS)}.")

        return cls.TRACKS[cls.TRACKS.index(track):]

    def publish(self, bundle, track):
        tracks = self.tracks_from(track)
        edit = self.edits.insert(packageName=self.package, body={}).execute()["id"]

        media = MediaFileUpload(bundle, mimetype="application/octet-stream", resumable=True)
        code = self.edits.bundles().upload(packageName=self.package, editId=edit, media_body=media).execute()["versionCode"]

        for name in tracks:
            release = {"versionCodes": [str(code)], "status": "completed"}
            self.edits.tracks().update(
                packageName=self.package, editId=edit, track=name, body={"track": name, "releases": [release]}
            ).execute()

        self.edits.commit(packageName=self.package, editId=edit).execute()

        return code, tracks


if __name__ == "__main__":
    package, bundle, track = sys.argv[1:4]
    key = json.loads(os.environ["PLAY_SERVICE_ACCOUNT_JSON"])

    code, tracks = PlayRelease(package, key).publish(bundle, track)

    print(f"Released build {code} on {', '.join(tracks)}.")
