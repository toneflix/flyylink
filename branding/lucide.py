"""Builds the Lucide icons both apps use, from the lucide-static npm package.

Run from the repo root: python3 branding/lucide.py
Needs npm. Add an icon by putting its Lucide name in MAC, ANDROID or WINDOWS and running it again.
"""
import math
import re
import subprocess
import tarfile
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

VERSION = "1.70.0"
ROOT = Path(__file__).resolve().parent.parent

MAC = [
    "arrow-down-to-line", "arrow-up", "audio-lines", "badge-check", "bell", "bell-off", "bell-ring",
    "cast", "check", "chevron-down", "chevron-left", "chevron-right", "circle", "circle-arrow-down",
    "circle-check", "circle-pause", "circle-x", "clipboard", "copy", "download", "eye", "file",
    "file-archive", "file-text", "file-up", "film", "hand", "hash", "headphones", "image", "images",
    "keyboard", "laptop", "link", "lock", "map-pin", "message-square", "messages-square", "mic", "mic-off", "monitor", "mouse-pointer-2", "music",
    "pause", "phone", "phone-off", "play", "radio", "rotate-cw", "screen-share", "settings", "skip-back", "skip-forward",
    "smartphone", "square-pen", "triangle-alert", "unplug", "video", "volume-2", "volume-x", "wifi", "x", "zap",
    "radar", "send", "folder-open",
    "tv", "house", "arrow-left", "chevron-up", "ellipsis", "volume-1", "delete", "corner-down-left", "gamepad-2",
]

ANDROID = [
    "battery-charging", "bell", "bell-ring", "bluetooth", "cast", "circle-alert", "circle-check",
    "clipboard", "download", "eye-off", "file-text", "file-up", "image", "keyboard", "link", "lock", "map-pin", "moon", "phone",
    "message-square", "pointer", "settings", "wifi", "radar", "send", "x", "check", "smartphone", "laptop",
    "tv", "music", "house", "arrow-left", "chevron-up", "chevron-down", "chevron-left", "chevron-right", "ellipsis",
    "play", "pause", "skip-back", "skip-forward", "volume-1", "volume-2", "volume-x", "delete", "corner-down-left", "film",
    "folder-open",
    "mouse-pointer-2", "presentation", "monitor-off", "maximize-2", "minimize-2",
]

WINDOWS = [
    "arrow-up", "audio-lines", "battery-charging", "cast", "circle", "copy", "bell", "bell-off", "bell-ring", "check", "chevron-down",
    "chevron-left", "chevron-right", "circle-check", "circle-x", "clipboard", "download", "external-link", "file",
    "file-archive", "file-text", "file-up", "film", "folder-open", "image", "images", "keyboard", "laptop", "link", "lock", "map-pin",
    "message-square", "messages-square", "mic", "mic-off", "monitor", "mouse-pointer-2", "music", "pause", "phone", "phone-off", "play",
    "rotate-cw", "screen-share", "search", "settings", "skip-back", "skip-forward", "smartphone", "square-pen", "triangle-alert",
    "unplug", "video", "volume-2", "volume-x", "wifi", "x", "zap", "radar", "send",
    "tv", "house", "arrow-left", "chevron-up", "ellipsis", "volume-1", "delete", "corner-down-left", "gamepad-2",
]

# Drawn solid as well as stroked, like the filled media controls people expect.
SOLID = {"pause", "play", "skip-back", "skip-forward", "zap"}


class PathBuilder:
    """Turns SVG shapes into absolute move, line, cubic and close commands."""

    def __init__(self):
        self.commands = []

    def element(self, node):
        tag = node.tag.split("}")[-1]
        num = lambda key, default=0.0: float(node.get(key, default))

        if tag == "path":
            self.path(node.get("d"))
        elif tag == "circle":
            self.ellipse(num("cx"), num("cy"), num("r"), num("r"))
        elif tag == "ellipse":
            self.ellipse(num("cx"), num("cy"), num("rx"), num("ry"))
        elif tag == "line":
            self.commands += [("M", [(num("x1"), num("y1"))]), ("L", [(num("x2"), num("y2"))])]
        elif tag in ("polyline", "polygon"):
            values = [float(v) for v in re.findall(r"-?[\d.]+(?:e-?\d+)?", node.get("points"))]
            points = list(zip(values[::2], values[1::2]))
            self.commands.append(("M", [points[0]]))
            self.commands += [("L", [p]) for p in points[1:]]
            if tag == "polygon":
                self.commands.append(("Z", []))
        elif tag == "rect":
            self.rect(num("x"), num("y"), num("width"), num("height"), node.get("rx"), node.get("ry"))
        else:
            raise ValueError(f"Unsupported element {tag}")

    def ellipse(self, cx, cy, rx, ry):
        self.path(f"M{cx - rx},{cy} A{rx},{ry} 0 1 0 {cx + rx},{cy} A{rx},{ry} 0 1 0 {cx - rx},{cy} Z")

    def rect(self, x, y, w, h, rx, ry):
        rx = float(rx if rx is not None else ry or 0)
        ry = float(ry if ry is not None else rx)
        if rx == 0:
            self.path(f"M{x},{y} H{x + w} V{y + h} H{x} Z")
            return

        self.path(
            f"M{x + rx},{y} H{x + w - rx} A{rx},{ry} 0 0 1 {x + w},{y + ry} V{y + h - ry} "
            f"A{rx},{ry} 0 0 1 {x + w - rx},{y + h} H{x + rx} A{rx},{ry} 0 0 1 {x},{y + h - ry} "
            f"V{y + ry} A{rx},{ry} 0 0 1 {x + rx},{y} Z"
        )

    def path(self, d):
        tokens = re.findall(r"[A-Za-z]|-?(?:\d+\.?\d*|\.\d+)(?:e-?\d+)?", d)
        i = 0
        x = y = sx = sy = 0.0
        last_control = None
        op = None

        def take(count, flags=()):
            nonlocal i
            values = []
            for index in range(count):
                if index in flags and len(tokens[i]) > 1 and tokens[i][0] in "01":
                    # Arc flags may be packed: "01" is two flags.
                    flag, rest = tokens[i][0], tokens[i][1:]
                    tokens[i] = rest
                    values.append(float(flag))
                    continue
                values.append(float(tokens[i]))
                i += 1
            return values

        while i < len(tokens):
            if tokens[i].isalpha():
                op = tokens[i]
                i += 1
            rel = op.islower()
            kind = op.upper()
            ox, oy = (x, y) if rel else (0.0, 0.0)

            if kind == "Z":
                self.commands.append(("Z", []))
                x, y = sx, sy
                last_control = None
                op = None
                continue
            if kind == "M":
                px, py = take(2)
                x, y = ox + px, oy + py
                sx, sy = x, y
                self.commands.append(("M", [(x, y)]))
                op = "l" if rel else "L"
                last_control = None
            elif kind == "L":
                px, py = take(2)
                x, y = ox + px, oy + py
                self.commands.append(("L", [(x, y)]))
                last_control = None
            elif kind == "H":
                (v,) = take(1)
                x = ox + v
                self.commands.append(("L", [(x, y)]))
                last_control = None
            elif kind == "V":
                (v,) = take(1)
                y = oy + v
                self.commands.append(("L", [(x, y)]))
                last_control = None
            elif kind in "CS":
                if kind == "C":
                    c = take(6)
                    c1 = (ox + c[0], oy + c[1])
                    c2, end = (ox + c[2], oy + c[3]), (ox + c[4], oy + c[5])
                else:
                    c = take(4)
                    c1 = (2 * x - last_control[0], 2 * y - last_control[1]) if last_control and self.commands[-1][0] == "C" else (x, y)
                    c2, end = (ox + c[0], oy + c[1]), (ox + c[2], oy + c[3])
                self.commands.append(("C", [c1, c2, end]))
                last_control = c2
                x, y = end
            elif kind in "QT":
                if kind == "Q":
                    c = take(4)
                    q, end = (ox + c[0], oy + c[1]), (ox + c[2], oy + c[3])
                else:
                    c = take(2)
                    q = (2 * x - last_control[0], 2 * y - last_control[1]) if last_control else (x, y)
                    end = (ox + c[0], oy + c[1])
                c1 = (x + 2 / 3 * (q[0] - x), y + 2 / 3 * (q[1] - y))
                c2 = (end[0] + 2 / 3 * (q[0] - end[0]), end[1] + 2 / 3 * (q[1] - end[1]))
                self.commands.append(("C", [c1, c2, end]))
                last_control = q
                x, y = end
            elif kind == "A":
                rx, ry, angle, large, sweep, px, py = take(7, flags=(3, 4))
                end = (ox + px, oy + py)
                self.commands += [("C", c) for c in self.arc((x, y), end, rx, ry, angle, large, sweep)]
                x, y = end
                last_control = None
            else:
                raise ValueError(f"Unsupported path command {op}")

    @staticmethod
    def arc(start, end, rx, ry, angle, large, sweep):
        """Splits an SVG arc into cubic curves of at most a quarter turn each."""
        if start == end:
            return []
        if rx == 0 or ry == 0:
            return [[start, end, end]]

        phi = math.radians(angle)
        cos, sin = math.cos(phi), math.sin(phi)
        dx, dy = (start[0] - end[0]) / 2, (start[1] - end[1]) / 2
        x1 = cos * dx + sin * dy
        y1 = -sin * dx + cos * dy
        rx, ry = abs(rx), abs(ry)
        scale = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry)
        if scale > 1:
            rx, ry = rx * math.sqrt(scale), ry * math.sqrt(scale)

        num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1
        den = rx * rx * y1 * y1 + ry * ry * x1 * x1
        factor = math.sqrt(max(0.0, num / den))
        if large == sweep:
            factor = -factor
        cx1, cy1 = factor * rx * y1 / ry, -factor * ry * x1 / rx
        cx = cos * cx1 - sin * cy1 + (start[0] + end[0]) / 2
        cy = sin * cx1 + cos * cy1 + (start[1] + end[1]) / 2

        def angle_of(ux, uy, vx, vy):
            a = math.atan2(ux * vy - uy * vx, ux * vx + uy * vy)
            return a

        theta = angle_of(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry)
        delta = angle_of((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry)
        if not sweep and delta > 0:
            delta -= 2 * math.pi
        elif sweep and delta < 0:
            delta += 2 * math.pi

        segments = max(1, math.ceil(abs(delta) / (math.pi / 2) - 1e-9))
        step = delta / segments
        k = 4 / 3 * math.tan(step / 4)
        point = lambda t: (cx + rx * math.cos(t) * cos - ry * math.sin(t) * sin, cy + rx * math.cos(t) * sin + ry * math.sin(t) * cos)
        deriv = lambda t: (-rx * math.sin(t) * cos - ry * math.cos(t) * sin, -rx * math.sin(t) * sin + ry * math.cos(t) * cos)
        curves = []

        for n in range(segments):
            t0, t1 = theta + n * step, theta + (n + 1) * step
            p0, p1 = point(t0), point(t1)
            d0, d1 = deriv(t0), deriv(t1)
            curves.append([(p0[0] + k * d0[0], p0[1] + k * d0[1]), (p1[0] - k * d1[0], p1[1] - k * d1[1]), p1])
        curves[-1][-1] = end

        return curves


class Icon:
    def __init__(self, name, svg: Path):
        self.name = name
        builder = PathBuilder()
        for node in ET.parse(svg).getroot():
            builder.element(node)
        self.commands = builder.commands

    @property
    def swift_case(self):
        head, *rest = self.name.split("-")
        return head + "".join(word.capitalize() for word in rest)

    @property
    def csharp_name(self):
        return "".join(word.capitalize() for word in self.name.split("-"))

    @property
    def resource(self):
        return "lucide_" + self.name.replace("-", "_")

    def path_data(self):
        fmt = lambda p: f"{round(p[0], 3):g},{round(p[1], 3):g}"
        return " ".join(op + " ".join(fmt(p) for p in pts) for op, pts in self.commands)

    def android(self):
        return (
            '<?xml version="1.0" encoding="utf-8"?>\n'
            f'<!-- Lucide "{self.name}" (ISC). Generated by branding/lucide.py. -->\n'
            '<vector xmlns:android="http://schemas.android.com/apk/res/android"\n'
            '    android:width="24dp"\n    android:height="24dp"\n'
            '    android:viewportWidth="24"\n    android:viewportHeight="24">\n'
            '    <path\n'
            '        android:strokeColor="#FF000000"\n'
            '        android:strokeWidth="2"\n'
            '        android:strokeLineCap="round"\n'
            '        android:strokeLineJoin="round"\n'
            f'        android:pathData="{self.path_data()}" />\n'
            '</vector>\n'
        )

    def swift(self):
        point = lambda p: f"CGPoint(x: {round(p[0], 3):g}, y: {round(p[1], 3):g})"
        lines = []
        for op, pts in self.commands:
            if op == "M":
                lines.append(f"            path.move(to: {point(pts[0])})")
            elif op == "L":
                lines.append(f"            path.addLine(to: {point(pts[0])})")
            elif op == "C":
                lines.append(f"            path.addCurve(to: {point(pts[2])}, control1: {point(pts[0])}, control2: {point(pts[1])})")
            else:
                lines.append("            path.closeSubpath()")

        return f"        case .{self.swift_case}:\n" + "\n".join(lines)


def fetch(tmp: Path) -> Path:
    subprocess.run(["npm", "pack", f"lucide-static@{VERSION}", "--silent", "--pack-destination", str(tmp)], check=True, capture_output=True)
    archive = next(tmp.glob("lucide-static-*.tgz"))
    with tarfile.open(archive) as tar:
        tar.extractall(tmp, filter="data")

    return tmp / "package/icons"


def write_swift(icons):
    cases = "\n".join(f'    case {icon.swift_case} = "{icon.name}"' for icon in icons)
    solid = ", ".join(f".{icon.swift_case}" for icon in icons if icon.name in SOLID)
    bodies = "\n".join(icon.swift() for icon in icons)

    source = f"""// Lucide icons (https://lucide.dev, ISC license), generated by branding/lucide.py.
import SwiftUI

/// The Lucide icons the Mac app draws, as outlines on Lucide's 24-point grid.
enum Lucide: String, CaseIterable {{
{cases}

    /// Icons that also get filled in, such as the media controls.
    var isSolid: Bool {{
        [{solid}].contains(self)
    }}

    /// The outline on a 24 by 24 grid, stroked 2 points wide with round ends.
    var path: Path {{
        var path = Path()

        switch self {{
{bodies}
        }}

        return path
    }}
}}
"""
    (ROOT / "macos/Sources/FlyyLink/Lucide.swift").write_text(source)


def write_csharp(icons):
    names = "\n".join(f"    {icon.csharp_name}," for icon in icons)
    solid = " or ".join(f"Lucide.{icon.csharp_name}" for icon in icons if icon.name in SOLID)
    solid = f"icon is {solid}" if solid else "false"
    data = "\n".join(f'        Lucide.{icon.csharp_name} => "{icon.path_data()}",' for icon in icons)

    source = f"""// Lucide icons (https://lucide.dev, ISC license), generated by branding/lucide.py.
namespace FlyyLink.Icons;

/// The Lucide icons the Windows app draws.
public enum Lucide
{{
{names}
}}

public static class LucidePaths
{{
    /// Icons that also get filled in, such as the media controls.
    public static bool IsSolid(this Lucide icon) => {solid};

    /// The outline on a 24 by 24 grid, stroked 2 units wide with round ends, as XAML path markup.
    public static string Data(this Lucide icon) => icon switch
    {{
{data}
        _ => "",
    }};
}}
"""
    (ROOT / "windows/src/FlyyLink/Icons/Lucide.cs").write_text(source)


def main():
    with tempfile.TemporaryDirectory() as tmp:
        source = fetch(Path(tmp))
        load = lambda names: [Icon(name, source / f"{name}.svg") for name in sorted(set(names))]

        write_swift(load(MAC))

        write_csharp(load(WINDOWS))

        drawables = ROOT / "android/app/src/main/res/drawable"
        for stale in drawables.glob("lucide_*.xml"):
            stale.unlink()
        for icon in load(ANDROID):
            (drawables / f"{icon.resource}.xml").write_text(icon.android())


if __name__ == "__main__":
    main()
