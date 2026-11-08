"""Tiny helpers for hand-laid SVG diagrams (fixed coordinates, no auto layout)."""
from xml.sax.saxutils import escape

FONT = "Helvetica, Arial, sans-serif"

COLORS = {
    "new":      ("#e8f5e9", "#2e7d32"),
    "existing": ("#f2f2f2", "#6b6b6b"),
    "other":    ("#fff3e0", "#ef6c00"),
    "iface":    ("#ede7f6", "#5e35b1"),
    "store":    ("#e3f2fd", "#1565c0"),
    "entity":   ("#ffffff", "#222222"),
}


class Svg:
    def __init__(self, width, height):
        self.w, self.h = width, height
        self.parts = []

    def add(self, s):
        self.parts.append(s)

    def text(self, x, y, s, size=11, weight="normal", anchor="start", color="#111", italic=False):
        style = ' font-style="italic"' if italic else ""
        self.add(f'<text x="{x}" y="{y}" font-family="{FONT}" font-size="{size}" font-weight="{weight}" '
                 f'text-anchor="{anchor}" fill="{color}"{style}>{escape(s)}</text>')

    def lines(self, x, y, rows, size=11, anchor="start", gap=None):
        """rows: list of str or (str, dict) tuples."""
        gap = gap or size + 4
        for i, row in enumerate(rows):
            if isinstance(row, tuple):
                s, kw = row
            else:
                s, kw = row, {}
            self.text(x, y + i * gap, s, size=kw.get("size", size), weight=kw.get("weight", "normal"),
                      anchor=anchor, color=kw.get("color", "#111"), italic=kw.get("italic", False))

    def rect(self, x, y, w, h, fill="#fff", stroke="#333", rx=6, dash=None, sw=1.4, opacity=1):
        d = f' stroke-dasharray="{dash}"' if dash else ""
        self.add(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="{fill}" '
                 f'fill-opacity="{opacity}" stroke="{stroke}" stroke-width="{sw}"{d}/>')

    def box(self, x, y, w, h, kind, title, rows=(), tag=None, size=10.5):
        fill, stroke = COLORS[kind]
        self.rect(x, y, w, h, fill, stroke, rx=5)
        title_rows = [(title, {"weight": "bold", "size": size + 1})]
        if tag:
            self.text(x + w - 6, y + 15, tag, size=9, anchor="end", color=stroke, weight="bold")
        self.lines(x + 8, y + 17, title_rows + list(rows), size=size, gap=size + 4)

    def iface(self, x, y, w, h, name):
        fill, stroke = COLORS["iface"]
        self.rect(x, y, w, h, fill, stroke, rx=14)
        self.text(x + w / 2, y + h / 2 - 3, "«interface»", size=9, anchor="middle", color=stroke)
        self.text(x + w / 2, y + h / 2 + 11, name, size=11, anchor="middle", weight="bold")

    def cluster(self, x, y, w, h, label, stroke, fill="none", dash=None, size=13):
        self.rect(x, y, w, h, fill, stroke, rx=10, dash=dash, sw=1.8, opacity=0.35 if fill != "none" else 1)
        self.text(x + 12, y + 18, label, size=size, weight="bold", color=stroke)

    def circle(self, cx, cy, r, kind, rows):
        fill, stroke = COLORS[kind]
        self.add(f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="{fill}" stroke="{stroke}" stroke-width="1.6"/>')
        n = len(rows)
        y0 = cy - (n - 1) * 7 + 4
        for i, row in enumerate(rows):
            self.text(cx, y0 + i * 14, row, size=11 if i else 12, weight="bold" if i == 0 else "normal",
                      anchor="middle")

    def store(self, x, y, w, h, ident, rows):
        """DFD data store: open-ended rectangle with id cell."""
        fill, stroke = COLORS["store"]
        self.add(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{fill}" stroke="none"/>')
        self.add(f'<path d="M{x + w},{y} H{x} V{y + h} H{x + w}" fill="none" stroke="{stroke}" stroke-width="1.6"/>')
        self.add(f'<line x1="{x + 44}" y1="{y}" x2="{x + 44}" y2="{y + h}" stroke="{stroke}" stroke-width="1.2"/>')
        self.text(x + 22, y + h / 2 + 4, ident, size=12, weight="bold", anchor="middle")
        n = len(rows)
        self.lines(x + 52, y + h / 2 - (n - 1) * 7 + 4, rows, size=10.5, gap=14)

    def cylinder(self, x, y, w, h, rows):
        fill, stroke = COLORS["store"]
        ry = 10
        self.add(f'<path d="M{x},{y + ry} A{w / 2},{ry} 0 0 1 {x + w},{y + ry} V{y + h - ry} '
                 f'A{w / 2},{ry} 0 0 1 {x},{y + h - ry} Z" fill="{fill}" stroke="{stroke}" stroke-width="1.6"/>')
        self.add(f'<path d="M{x},{y + ry} A{w / 2},{ry} 0 0 0 {x + w},{y + ry}" fill="none" stroke="{stroke}" stroke-width="1.6"/>')
        n = len(rows)
        self.lines(x + w / 2, y + h / 2 - (n - 1) * 7 + 8, rows, size=11, anchor="middle", gap=14)

    def path(self, pts, color="#333", dash=None, head="filled", sw=1.4, label=None, lpos=None,
             lanchor="start", lcolor=None, lsize=10):
        d = "M" + " L".join(f"{x},{y}" for x, y in pts)
        marker = {"filled": "arrow", "open": "tri", "none": None}[head]
        mk = f' marker-end="url(#{marker}-{color.strip("#")})"' if marker else ""
        da = f' stroke-dasharray="{dash}"' if dash else ""
        self.add(f'<path d="{d}" fill="none" stroke="{color}" stroke-width="{sw}"{da}{mk}/>')
        self._markers.add((marker, color)) if marker else None
        if label:
            lx, ly = lpos
            rows = label if isinstance(label, list) else [label]
            self.lines(lx, ly, rows, size=lsize, anchor=lanchor, gap=lsize + 3) if not lcolor else \
                self.lines(lx, ly, [(r, {"color": lcolor}) for r in rows], size=lsize, anchor=lanchor, gap=lsize + 3)

    _markers = set()

    def render(self, title=None, subtitle=None):
        defs = []
        for marker, color in sorted(self._markers):
            cid = color.strip("#")
            if marker == "arrow":
                defs.append(f'<marker id="arrow-{cid}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" '
                            f'markerHeight="8" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="{color}"/></marker>')
            else:
                defs.append(f'<marker id="tri-{cid}" viewBox="0 0 12 12" refX="11" refY="6" markerWidth="11" '
                            f'markerHeight="11" orient="auto-start-reverse"><path d="M0,0 L12,6 L0,12 z" fill="#fff" '
                            f'stroke="{color}" stroke-width="1.3"/></marker>')
        head = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{self.w}" height="{self.h}" '
                f'viewBox="0 0 {self.w} {self.h}"><defs>{"".join(defs)}</defs>'
                f'<rect width="{self.w}" height="{self.h}" fill="#ffffff"/>')
        t = []
        if title:
            t.append(f'<text x="20" y="30" font-family="{FONT}" font-size="18" font-weight="bold" fill="#111">{escape(title)}</text>')
        if subtitle:
            t.append(f'<text x="20" y="50" font-family="{FONT}" font-size="11.5" fill="#444">{escape(subtitle)}</text>')
        return head + "".join(t) + "".join(self.parts) + "</svg>"
