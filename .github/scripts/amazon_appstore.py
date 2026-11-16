"""Replaces the app's APK on the Amazon Appstore and submits it for review.

Usage: amazon_appstore.py <app id> <release.apk>
Reads AMAZON_CLIENT_ID and AMAZON_CLIENT_SECRET from the environment.
"""

import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request


class AppstoreError(Exception):
    def __init__(self, message, status=None):
        super().__init__(message)
        self.status = status


class AmazonAppstore:
    TOKEN_URL = "https://api.amazon.com/auth/o2/token"
    API_URL = "https://developer.amazon.com/api/appstore/v1/applications"
    APK_TYPE = "application/vnd.android.package-archive"

    def __init__(self, app_id, client_id, client_secret):
        self.app_url = f"{self.API_URL}/{app_id}"
        self.token = self._token(client_id, client_secret)

    def release(self, apk_path):
        edit = self._open_edit()
        apks = self._request("GET", f"/edits/{edit}/apks")[0]

        if len(apks) > 1:
            raise AppstoreError("The app has more than one APK on the Appstore. Remove the extras in the console first.")

        with open(apk_path, "rb") as file:
            apk = file.read()

        if apks:
            apk_id = apks[0]["id"]
            etag = self._request("GET", f"/edits/{edit}/apks/{apk_id}")[1]
            self._request("PUT", f"/edits/{edit}/apks/{apk_id}/replace", apk, self.APK_TYPE, etag)
        else:
            self._request("POST", f"/edits/{edit}/apks/upload", apk, self.APK_TYPE)

        etag = self._request("GET", f"/edits/{edit}")[1]
        self._request("POST", f"/edits/{edit}/commit", etag=etag)

        return edit

    def _open_edit(self):
        try:
            active = self._request("GET", "/edits")[0]
        except AppstoreError as error:
            if error.status != 404:
                raise

            active = None

        if active and active.get("id"):
            print("::warning::Reusing the Amazon edit that was already open, so its other changes are submitted too.")
            return active["id"]

        return self._request("POST", "/edits")[0]["id"]

    def _token(self, client_id, client_secret):
        body = urllib.parse.urlencode({
            "grant_type": "client_credentials",
            "client_id": client_id,
            "client_secret": client_secret,
            "scope": "appstore::apps:readwrite",
        }).encode()
        request = urllib.request.Request(self.TOKEN_URL, body, {"Content-Type": "application/x-www-form-urlencoded"})

        return self._send(request)[0]["access_token"]

    def _request(self, method, path, body=None, content_type="application/json", etag=None):
        headers = {"Authorization": f"Bearer {self.token}", "Accept": "application/json", "Content-Type": content_type}

        if etag:
            headers["If-Match"] = etag

        return self._send(urllib.request.Request(self.app_url + path, body, headers, method=method))

    @staticmethod
    def _send(request):
        try:
            with urllib.request.urlopen(request, timeout=600) as response:
                text = response.read().decode() or "null"
                return json.loads(text), response.headers.get("ETag")
        except urllib.error.HTTPError as error:
            detail = error.read().decode(errors="replace")

            if error.code == 412:
                raise AppstoreError(f"Amazon refused the change (412). The app may be in review. {detail}", 412) from error

            raise AppstoreError(f"{request.get_method()} {request.full_url} failed with {error.code}: {detail}", error.code) from error


if __name__ == "__main__":
    app_id, apk_path = sys.argv[1:3]
    store = AmazonAppstore(app_id, os.environ["AMAZON_CLIENT_ID"], os.environ["AMAZON_CLIENT_SECRET"])

    try:
        edit = store.release(apk_path)
    except AppstoreError as error:
        print(f"::error::{error}")
        sys.exit(1)

    print(f"Submitted edit {edit} for review on the Amazon Appstore.")
