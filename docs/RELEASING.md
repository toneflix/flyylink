# Releasing

Push a tag like `1.2.3` to release all three apps, or `android-1.2.3`, `macos-1.2.3` or `windows-1.2.3` for one. Tags have no `v` prefix. Nothing is published unless every app in the release builds.

| App     | Files                                                             |
| ------- | ----------------------------------------------------------------- |
| Android | `FlyyLink-1.2.3.apk` and the Play Store bundle `.aab`             |
| Mac     | `FlyyLink-1.2.3.dmg`, signed and notarized, and its update `.zip` |
| Windows | `FlyyLink-Setup-1.2.3.exe`                                        |

The Android build number is the base in `version.json` plus the workflow's run number.

## Google Play

After the GitHub release is published, the Android bundle is uploaded to Google Play and sent for review. The version picks the track:

| Version                        | Play track                    |
| ------------------------------ | ----------------------------- |
| `1.2.3`                        | `production`                  |
| `1.2.3-beta.1` or `1.2.3-rc.1` | `beta` (open testing)         |
| `1.2.3-alpha.1`                | `alpha` (closed testing)      |
| `1.2.3-internal.1`             | `internal` (internal testing) |

A build also goes to every testing track below its own: production also goes to open, closed and internal testing, open testing to closed and internal, and closed testing to internal.

Any other suffix stops the release. A version with a suffix is marked as a pre-release on GitHub and isn't put on the website or in the Mac and Windows update feeds. Use `android-1.2.3-beta.1` to send a test build of the Android app alone.

Without the `PLAY_SERVICE_ACCOUNT_JSON` secret, the Play step is skipped with a warning.

One-time setup:

1. In [Google Cloud Console](https://console.cloud.google.com/), pick or create a project, enable the **Google Play Android Developer API**, then under IAM & Admin > Service accounts create a service account and add a JSON key. Save the downloaded file.
2. In [Play Console](https://play.google.com/console/), go to Users and permissions, invite the service account's email, and give it access to FlyyLink with **Release to production, exclude devices, and use Play App Signing** and **Release apps to testing tracks**.
3. In GitHub, add the JSON file's contents as the `PLAY_SERVICE_ACCOUNT_JSON` secret.

The bundle must be signed with the upload key Play expects, which is the `ANDROID_KEYSTORE` key. Play rejects a build number it has already seen, so a re-run of an old release fails the Play step only.

## Admin CLI

`flyylink-cli` is released on its own, to npm as `@toneflix/flyylink-cli`. Push a tag like `cli-1.2.3` (a pre-release such as `cli-1.3.0-beta.1` goes out under the `next` tag), or run the **Publish flyylink-cli** workflow by hand with a version. `.github/workflows/publish-cli.yml` type-checks and tests the CLI against the worker, sets the version from the tag and publishes. App release tags don't publish it. Install and use are in [ADMIN.md](ADMIN.md).

## Where files go

Every file is uploaded to the `flyylink-releases` R2 bucket. The website serves it at `/download/<platform>` and `/releases/<file>`, and serves the update feeds: `/updates/mac/appcast.xml` for the Mac and `/updates/windows.json` for Windows, which installed copies check twice a day.

The Windows installer isn't signed, so on first install Windows SmartScreen asks: pick "More info", then "Run anyway".

## Secrets and variables

Set these under GitHub > Settings > Secrets and variables > Actions. `.github/workflows/release.yml` lists them too.

| App             | Secrets                                                                                                                                                                     | Variables            |
| --------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------- |
| Android         | `ANDROID_KEYSTORE` (base64 `.jks`), `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD` if the key has its own                                         |                      |
| Google Play     | `PLAY_SERVICE_ACCOUNT_JSON` (service account JSON key)                                                                                                                      |                      |
| Mac             | `MACOS_CERT_P12` (base64 Developer ID Application `.p12`), `MACOS_CERT_PASSWORD`, `NOTARY_KEY` (`.p8` contents), `NOTARY_KEY_ID`, `NOTARY_ISSUER_ID`, `SPARKLE_PRIVATE_KEY` | `SPARKLE_PUBLIC_KEY` |
| Mac and Windows |                                                                                                                                                                             | `LICENCE_PUBLIC_KEY` |
| Website upload  | `CLOUDFLARE_API_TOKEN` (Workers R2 Storage edit), `CLOUDFLARE_ACCOUNT_ID`                                                                                                   |                      |
| Admin CLI       | `NPM_TOKEN` (npm automation token that can publish under `@toneflix`)                                                                                                       |                      |

Without the Sparkle key and variable, the Mac release works but can't auto-update. Without `LICENCE_PUBLIC_KEY`, the Mac and Windows builds skip the licence check, as local builds do. See [LICENCES.md](LICENCES.md).
