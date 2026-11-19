import type { Router } from "./router";

type Platform = "android" | "mac" | "windows";

interface Build {
  version: string;
  /** File for first installs. */
  download: string;
  /** File the app updates itself from, when it differs from `download`. */
  update?: string;
}

type Latest = Partial<Record<Platform, Build>>;

/** Platforms installed from a store instead of from R2. */
const stores: Partial<Record<Platform, string>> = {
  android: "https://play.google.com/store/apps/details?id=com.toneflix.flyylink",
};

const contentTypes: Record<string, string> = {
  apk: "application/vnd.android.package-archive",
  aab: "application/octet-stream",
  dmg: "application/x-apple-diskimage",
  zip: "application/zip",
  exe: "application/vnd.microsoft.portable-executable",
  xml: "application/xml",
};

/**
 * Serves releases from R2. The release workflow uploads each file to `releases/`, the Mac and Windows
 * update feeds to `updates/appcast.xml` and `updates/windows-appcast.xml`, and records the newest build
 * per platform in `latest.json`. Windows builds from before WinSparkle read `/updates/windows.json`.
 */
export class Downloads {
  constructor(private readonly bucket: R2Bucket) {}

  register(router: Router): void {
    router
      .get("/download/:platform", (request, { platform }) => this.download(request, platform))
      .get("/releases/:file", (request, { file }) => this.file(request, `releases/${file}`, true))
      .get("/updates/mac/appcast.xml", (request) => this.file(request, "updates/appcast.xml", false))
      .get("/updates/windows/appcast.xml", (request) => this.file(request, "updates/windows-appcast.xml", false))
      .get("/updates/windows.json", (request) => this.windowsUpdate(request));
  }

  private async download(request: Request, platform: string): Promise<Response> {
    const store = stores[platform as Platform];
    if (store) return Response.redirect(store, 302);

    const build = (await this.latest())[platform as Platform];
    if (!build) return new Response("No release yet.", { status: 404 });

    return Response.redirect(new URL(`/releases/${build.download}`, request.url).toString(), 302);
  }

  private async windowsUpdate(request: Request): Promise<Response> {
    const build = (await this.latest()).windows;
    if (!build) return new Response("No release yet.", { status: 404 });

    const origin = new URL(request.url).origin;

    return Response.json(
      { version: build.version, installer: `${origin}/releases/${build.download}`, notes: `${origin}/#download` },
      { headers: { "Cache-Control": "public, max-age=300" } },
    );
  }

  private async latest(): Promise<Latest> {
    const object = await this.bucket.get("latest.json");

    return object ? await object.json<Latest>() : {};
  }

  private async file(request: Request, key: string, attachment: boolean): Promise<Response> {
    const object = await this.bucket.get(key, { range: request.headers, onlyIf: request.headers });
    if (!object) return new Response("Not found.", { status: 404 });

    const headers = new Headers();
    object.writeHttpMetadata(headers);
    headers.set("ETag", object.httpEtag);
    headers.set("Accept-Ranges", "bytes");
    headers.set("Content-Type", contentTypes[key.split(".").pop() ?? ""] ?? "application/octet-stream");
    headers.set("Cache-Control", attachment ? "public, max-age=86400, immutable" : "public, max-age=300");
    if (attachment) headers.set("Content-Disposition", `attachment; filename="${key.split("/").pop()}"`);

    if (!("body" in object)) return new Response(null, { status: 304, headers });

    const range = object.range as { offset: number; length: number } | undefined;
    if (range && request.headers.has("Range")) {
      headers.set("Content-Range", `bytes ${range.offset}-${range.offset + range.length - 1}/${object.size}`);
      headers.set("Content-Length", String(range.length));

      return new Response(request.method === "HEAD" ? null : object.body, { status: 206, headers });
    }

    headers.set("Content-Length", String(object.size));

    return new Response(request.method === "HEAD" ? null : object.body, { headers });
  }
}
