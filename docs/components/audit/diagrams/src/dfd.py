import sys
from svgkit import Svg, COLORS
R = "#c62828"; K = "#333333"
s = Svg(1640, 970)

# ---------- trust boundaries ----------
s.rect(15, 230, 220, 500, "#ffebee", R, rx=10, dash="8 5", sw=2, opacity=0.6)
s.text(25, 248, "TB0 · nepouzdano", 12, "bold", color=R)
s.text(25, 262, "Internet / pregledač", 10, color=R)
s.rect(1150, 62, 210, 100, "#ffebee", R, rx=10, dash="8 5", sw=2, opacity=0.6)
s.text(1160, 156, "TB0 · nepouzdano", 9.5, "bold", color=R)
s.rect(255, 195, 1195, 590, "#fffde7", "#f9a825", rx=10, sw=2, opacity=0.6)
s.text(268, 776, "TB1 · pouzdan serverski proces (Platform.Api + Platform.Audit)", 12, "bold", color="#b26a00")
s.rect(470, 70, 385, 105, "#e1f5fe", "#0277bd", rx=10, dash="4 3", sw=2, opacity=0.6)
s.text(480, 87, "TB2 · baza (identity, članstva)", 10.5, "bold", color="#0277bd")
s.rect(585, 800, 750, 95, "#e1f5fe", "#0277bd", rx=10, dash="4 3", sw=2, opacity=0.6)
s.text(595, 888, "TB2 · PostgreSQL (runtime uloga: SELECT, INSERT)", 10.5, "bold", color="#0277bd")

# ---------- external entities ----------
s.rect(35, 290, 180, 400, "#ffffff", "#222222", rx=2, sw=2)
s.lines(125, 455, [("E1", {"weight": "bold", "size": 13}), ("Nastavnik / asistent", {"weight": "bold"}),
                   "(pregledač, React UI)", " ", ("isti ulaz koristi i", {"italic": True}),
                   ("napadač — sve je", {"italic": True}), ("nepouzdano", {"italic": True})],
        size=11, anchor="middle", gap=15)
s.rect(1170, 72, 170, 66, "#ffffff", "#222222", rx=2, sw=2)
s.lines(1255, 92, [("E2", {"weight": "bold", "size": 13}), ("Korisnik ili servis", {"weight": "bold"}),
                   "čija akcija se beleži"], size=10.5, anchor="middle", gap=14)

# ---------- data stores ----------
s.store(485, 95, 165, 40, "D1", ["Sesije", "(identity šema)"])
s.store(670, 95, 175, 40, "D2", ["Članstva i", "delegacije (ACC-03/07)"])
s.store(330, 450, 510, 40, "D0", ["Kontekst zahteva u memoriji (HttpContext): korelacioni ID,",
                                    "validirana sesija — živi samo dok traje zahtev"])
s.store(600, 820, 720, 48, "D3", ["audit.audit_events — append-only (bez UPDATE / DELETE)",
                                    "događaji: tip, ishod, akter, vreme UTC, kurs, resurs, korelacioni ID, allowlist detalji"])

# ---------- processes ----------
s.circle(380, 330, 52, "existing", ["P1", "Dodela", "korelac. ID"])
s.circle(560, 330, 52, "existing", ["P2", "Autentikacija", "sesije"])
s.circle(740, 330, 52, "existing", ["P3", "Autorizacija", "audit.events", ".read"])
s.circle(920, 330, 52, "new", ["P4", "Validacija", "upita"])
s.circle(920, 580, 52, "new", ["P5", "Čitanje", "događaja", "kursa"])
s.circle(740, 580, 52, "new", ["P6", "Beleženje", "pregleda"])
s.circle(560, 580, 52, "new", ["P7", "Formiranje", "odgovora"])
s.circle(1240, 300, 46, "other", ["P8", "Modul", "proizvođač"])
s.circle(1240, 445, 46, "new", ["P9", "Kontekst", "zapisa"])
s.circle(1240, 590, 46, "existing", ["P10", "Validacija", "nacrta"])
s.circle(1240, 725, 46, "existing", ["P11", "Upis", "(append-only)"])

# ---------- AUD-06 read flows ----------
s.path([(215, 330), (328, 330)], K, label=["1 GET + filteri", "+ session cookie"], lpos=(244, 307), lsize=9.5)
s.path([(432, 330), (508, 330)], K, label="2 zahtev", lpos=(470, 322), lanchor="middle", lsize=9.5)
s.path([(545, 135), (545, 281)], K, label="3 token", lpos=(540, 222), lanchor="end", lsize=9.5)
s.path([(575, 281), (575, 135)], K, label="4 userId, nivo", lpos=(580, 222), lsize=9.5)
s.path([(612, 330), (688, 330)], K, label="5 subjekt", lpos=(650, 322), lanchor="middle", lsize=9.5)
s.path([(725, 135), (725, 281)], K, label="6 nalog, kurs", lpos=(720, 248), lanchor="end", lsize=9.5)
s.path([(755, 281), (755, 135)], K, label=["7 uloga,", "delegacije"], lpos=(760, 240), lsize=9.5)
s.path([(792, 330), (868, 330)], K, label="8 Allow", lpos=(830, 322), lanchor="middle", lsize=9.5)
s.path([(920, 382), (920, 528)], K, label=["9 filter +", "kurs iz RUTE"], lpos=(927, 400), lsize=9.5)
s.path([(905, 630), (905, 820)], K, label=["10 SELECT", "(parametri)"], lpos=(900, 760), lanchor="end", lsize=9.5)
s.path([(935, 820), (935, 630)], K, label=["11 redovi", "samo tog kursa"], lpos=(940, 760), lsize=9.5)
s.path([(868, 580), (792, 580)], K, label="12 stranica", lpos=(830, 572), lanchor="middle", lsize=9.5)
s.path([(750, 632), (750, 820)], K, label=["13 INSERT", "audit.events", ".viewed"], lpos=(745, 712), lanchor="end", lsize=9.5)
s.path([(780, 820), (780, 632)], K, label="14 Recorded", lpos=(785, 712), lsize=9.5)
s.path([(688, 580), (612, 580)], K, label="15 stranica", lpos=(650, 572), lanchor="middle", lsize=9.5)
s.path([(508, 580), (215, 580)], K, label=["16 200 {items, nextCursor}", "Cache-Control: no-store"],
       lpos=(244, 556), lsize=9.5)
# request context store
s.path([(380, 382), (380, 450)], K, label="ID", lpos=(386, 442), lsize=9.5)
s.path([(590, 373), (590, 450)], K, label="sesija", lpos=(596, 442), lsize=9.5)
s.path([(740, 490), (740, 528)], K, label="posmatrač", lpos=(746, 514), lsize=9.5)
# deny / fail-closed paths
for x, y in [(530, 373), (725, 380), (895, 376)]:
    s.path([(x, y), (x, 420)], R, dash="5 4", head="none")
s.path([(895, 420), (215, 420)], R, dash="5 4", label="odbijeno: 401 / 403 / 400", lpos=(244, 434), lcolor=R, lsize=9.5)
for x, y in [(890, 622), (710, 622)]:
    s.path([(x, y), (x, 670)], R, dash="5 4", head="none")
s.path([(890, 670), (215, 670)], R, dash="5 4", label="503 — nijedan događaj se ne vraća", lpos=(244, 684), lcolor=R, lsize=9.5)

# ---------- AUD-01 write flows ----------
s.path([(1255, 138), (1255, 254)], K, label=["W1 bezbednosna akcija", "(prijava, promena prava…)"],
       lpos=(1248, 178), lanchor="end", lsize=9.5)
s.path([(1240, 346), (1240, 399)], K, label="W2 AuditEventDraft", lpos=(1232, 378), lanchor="end", lsize=9.5)
s.path([(840, 470), (1194, 450)], K, label="W3 akter + korelacioni ID", lpos=(1020, 451), lanchor="middle", lsize=9.5)
s.path([(1240, 491), (1240, 544)], K, label=["W4 nacrt +", "AuditContext"], lpos=(1232, 512), lanchor="end", lsize=9.5)
s.path([(1240, 636), (1240, 679)], K, label="W5 prihvaćen", lpos=(1232, 662), lanchor="end", lsize=9.5)
s.path([(1240, 771), (1240, 820)], K, label="W6 INSERT", lpos=(1232, 798), lanchor="end", lsize=9.5)
s.path([(1286, 590), (1310, 590), (1310, 312), (1285, 312)], R, dash="5 4")
s.path([(1286, 725), (1330, 725), (1330, 290), (1285, 290)], K)
s.lines(1337, 470, [("W7 Rejected(kod)", {"color": R}), ("vrednost se nikad", {"color": R}),
                    ("ne vraća", {"color": R})], size=9.5, gap=12)
s.lines(1337, 660, ["W8 Recorded", "(eventId)"], size=9.5, gap=12)

# ---------- inputs / outputs ----------
s.rect(15, 795, 555, 160, "#ffffff", "#999999", rx=8)
s.lines(27, 815, [("Ulazi", {"weight": "bold"}),
                  "AUD-06: courseId (ruta), eventType, outcome, from, to, limit, cursor (query), session cookie",
                  "AUD-01: AuditEventDraft = tip, ishod, kurs, resurs, allowlist detalji (od modula)",
                  ("Izlazi", {"weight": "bold"}),
                  "AUD-06: 200 {items, nextCursor} · 400 · 401 · 403 · 503 (bez podataka)",
                  "AUD-01: Recorded(eventId) / Rejected(kod) / Unavailable + zapis u D3",
                  ("Nikad se ne prenose ni ne beleže: lozinke, tokeni, kolačići, odgovori studenata,", {"italic": True}),
                  ("slobodan tekst; akter, vreme i korelacioni ID ne dolaze od klijenta.", {"italic": True})],
        size=10, gap=17)

# ---------- legend ----------
s.rect(1465, 195, 165, 300, "#ffffff", "#999999", rx=8)
s.text(1475, 214, "Legenda", 12, "bold")
s.rect(1477, 226, 30, 20, "#fff", "#222", rx=1, sw=2); s.text(1514, 240, "akter (E)", 10)
for i, (kind, txt) in enumerate([("new", "proces — novo"), ("existing", "proces — postojeći"),
                                 ("other", "proces drugog tima")]):
    f, st = COLORS[kind]
    s.add(f'<circle cx="1492" cy="{266 + i * 26}" r="10" fill="{f}" stroke="{st}" stroke-width="1.6"/>')
    s.text(1514, 270 + i * 26, txt, 10)
f, st = COLORS["store"]
s.add(f'<path d="M1507,336 H1477 V352 H1507" fill="{f}" stroke="{st}" stroke-width="1.6"/>')
s.text(1514, 348, "skladište (D)", 10)
s.path([(1477, 372), (1507, 372)], K); s.text(1514, 376, "tok podataka", 10)
s.path([(1477, 394), (1507, 394)], R, dash="5 4"); s.text(1514, 398, "odbijanje / greška", 10)
s.rect(1477, 410, 30, 16, "none", R, rx=3, dash="5 3", sw=1.8); s.text(1514, 422, "granica poverenja", 10)
s.lines(1475, 446, ["1–16: čitanje (AUD-06)", "W1–W8: upis (AUD-01)", "D0–D3: skladišta"], size=9.5, gap=14)

out = s.render("Audit (AUD-01 / AUD-06) — dijagram toka podataka (DFD, nivo 1)",
               "Akteri, ulazi/izlazi, procesi, skladišta podataka i tokovi, sa granicama poverenja")
open(sys.argv[1], "w", encoding="utf-8").write(out)
