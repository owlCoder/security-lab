import sys
from svgkit import Svg, COLORS
P = "#5e35b1"   # interface colour
s = Svg(1500, 1060)

def p(t): return (t, {"color": P})
def it(t): return (t, {"italic": True, "color": "#555"})

# ---------- clusters (labels at the bottom-left so no edge crosses them) ----------
s.rect(350, 185, 1130, 265, "#f1f8e9", "#2e7d32", rx=10, sw=1.8, opacity=0.5)
s.text(1200, 443, "Platform.Api · host", 12.5, "bold", color="#2e7d32")
s.rect(350, 480, 1130, 410, "#f9fbe7", "#1b5e20", rx=10, sw=1.8, opacity=0.5)
s.text(362, 882, "Platform.Audit · modul (vlasnik: Tim 12)", 12.5, "bold", color="#1b5e20")
s.rect(360, 495, 1110, 75, "#ede7f6", P, rx=8, sw=1.2, opacity=0.35)
s.text(372, 566, "Ugovori modula (public)", 10.5, "bold", color=P)
s.rect(10, 185, 270, 705, "none", "#ef6c00", rx=10, dash="7 5", sw=1.6)
s.text(22, 882, "Ostatak platforme (drugi timovi)", 12.5, "bold", color="#ef6c00")

# ---------- external callers ----------
s.box(370, 70, 250, 80, "other", "Frontend (React)",
      ["Odg.: prikaz audit događaja", "nastavniku / asistentu"], tag="LAB UI")
s.box(1210, 65, 250, 100, "other", "Moduli proizvođači događaja",
      ["Identity, REV-05, Access adapter,", "članstva (ACC-03/07)", "Odg.: prijave ŠTA se desilo",
       p("traži: IAuditWriter / ITrusted…")])

# ---------- host ----------
s.box(370, 215, 250, 150, "existing", "HTTP pipeline", [
    "1 CorrelationMiddleware", "   → serverski korelacioni ID",
    "2 AccessControlMiddleware", "   → [RequireAccess], default deny",
    "3 SessionCheckMiddleware", "   → validirana sesija u HttpContext",
    p("pruža: ICorrelationContext")], tag="postojeće")
s.box(650, 215, 250, 215, "new", "AuditEventsController", [
    "GET /api/audit/courses/", "      {courseId}/events",
    "[RequireAccess(audit.events.read,", "      course, courseId)]",
    "Odg.: HTTP ulaz, parsiranje filtera,", "mapiranje DTO, statusi", "200 / 400 / 401 / 503, no-store",
    p("traži: IAuditCourseReader,"), p("IAuditActorSource, ICorrelationContext")], tag="NOVO")
s.box(930, 215, 250, 120, "new", "SessionAuditActorSource", [
    "Odg.: ko je pozivalac = SAMO", "potvrđena sesija (nikad body,", "query string ni zaglavlje)",
    p("pruža: IAuditActorSource")], tag="NOVO")
s.box(1210, 215, 170, 95, "new", "Program.cs", [
    "Odg.: kompozicija", "AddAuditRequestContext", "<SessionAuditActorSource>"], tag="IZMENA")

# ---------- other teams ----------
s.box(20, 215, 250, 95, "other", "Identity · IDN-02/03", [
    "Odg.: prijava, sesije, status naloga", p("pruža: ISessionManager,"), p("IAccountStatusReader")], tag="tim 1/2")
s.box(20, 330, 250, 120, "other", "Access · ACC-05/08", [
    "Odg.: odluka o pristupu,", "podrazumevano odbijanje", p("pruža: IAccessDecision"),
    p("traži: IAccessPolicy,"), p("IMembershipReader")], tag="tim 3")
s.box(20, 775, 250, 95, "other", "Članstva i delegacije · ACC-03/07", [
    "Odg.: uloga na kursu, delegacije", p("pruža: IMembershipReader"),
    it("još nije na main → EmptyMembershipReader")])

# ---------- contracts ----------
s.iface(385, 505, 220, 50, "ICorrelationContext")
s.iface(665, 505, 220, 50, "IAuditCourseReader")
s.iface(945, 505, 220, 50, "IAuditActorSource")
fill, stroke = COLORS["iface"]
s.rect(1225, 505, 220, 50, fill, stroke, rx=14)
s.text(1335, 519, "«interface»", 9, anchor="middle", color=stroke)
s.text(1335, 533, "IAuditWriter", 11, "bold", anchor="middle")
s.text(1335, 547, "ITrustedAuditWriter", 11, "bold", anchor="middle")

# ---------- application ----------
s.box(370, 625, 250, 120, "existing", "CourseAuditReadPolicy", [
    "Odg.: audit.events.read samo za", "nastavnika ili asistenta", "sa delegacijom",
    p("pruža: IAccessPolicy"), p("traži: IMembershipReader")], tag="postojeće")
s.box(650, 625, 250, 120, "new", "AuditCourseReader", [
    "Odg.: validira upit, čita SAMO", "jedan kurs, beleži", "audit.events.viewed PRE odgovora",
    "(fail closed: bez zapisa → 503)", p("pruža: IAuditCourseReader")], tag="NOVO")
s.box(930, 625, 250, 120, "new", "RequestAuditContextProvider", [
    "Odg.: AuditContext =", "akter + korelacioni ID", "(bez konteksta → odbijanje)",
    p("traži: IAuditActorSource,"), p("ICorrelationContext")], tag="NOVO")
s.box(1210, 625, 250, 120, "existing", "AuditWriter + Validator", [
    "Odg.: katalog događaja, allowlist", "detalja, bez tajni; vreme UTC,", "akter i korelacija sa servera",
    p("pruža: IAuditWriter,"), p("ITrustedAuditWriter")], tag="postojeće")

# ---------- infrastructure ----------
s.box(650, 775, 250, 80, "new", "PostgresAuditEventQueryStore", [
    "Odg.: parametrizovan SELECT,", "uvek WHERE course_id, keyset"], tag="NOVO")
s.box(1210, 775, 250, 80, "existing", "PostgresAuditEventStore", [
    "Odg.: append-only INSERT,", "idempotentan EventId"], tag="postojeće")
s.cylinder(650, 925, 810, 95, ["PostgreSQL · šema audit · tabela audit_events",
                                "runtime uloga: SELECT, INSERT (bez UPDATE / DELETE)"])

# ---------- edges ----------
k = "#333333"
s.path([(495, 150), (495, 215)], k, label="HTTPS GET + session cookie", lpos=(502, 182))
s.path([(1000, 215), (1000, 200), (560, 200), (560, 215)], k,
       label="čita validiranu sesiju (HttpContext.Items)", lpos=(780, 195), lanchor="middle")
s.path([(620, 300), (650, 300)], k, label="Allow", lpos=(635, 293), lanchor="middle", lsize=9)
s.path([(370, 250), (270, 250)], k, label="ISessionManager", lpos=(286, 243), lsize=8.5, lcolor=P)
s.path([(370, 340), (270, 340)], k, label="IAccessDecision", lpos=(286, 333), lsize=8.5, lcolor=P)
s.path([(150, 450), (150, 690), (370, 690)], k, label=["IAccessPolicy", "(audit.events.read)"],
       lpos=(158, 560), lcolor=P)
s.path([(495, 745), (495, 822), (270, 822)], k, label="IMembershipReader", lpos=(388, 815), lanchor="middle", lcolor=P)
s.path([(1210, 262), (1180, 262)], k, dash="2 3", label="DI", lpos=(1195, 256), lanchor="middle", lsize=9)
# realizations (dashed, hollow head, pointing at the interface)
s.path([(480, 365), (480, 505)], "#555555", dash="6 4", head="open")
s.path([(1095, 335), (1095, 505)], "#555555", dash="6 4", head="open")
s.path([(775, 625), (775, 555)], "#555555", dash="6 4", head="open")
s.path([(1335, 625), (1335, 555)], "#555555", dash="6 4", head="open")
# uses of interfaces (purple)
s.path([(1430, 165), (1430, 505)], P, label="WriteAsync(draft)", lpos=(1424, 470), lanchor="end", lcolor=P)
s.path([(775, 430), (775, 505)], P)
s.path([(700, 430), (700, 465), (545, 465), (545, 505)], P)
s.path([(850, 430), (850, 465), (1020, 465), (1020, 505)], P)
s.path([(1050, 625), (1050, 555)], P)
s.path([(960, 625), (960, 582), (585, 582), (585, 555)], P)
s.path([(880, 625), (880, 607), (1255, 607), (1255, 555)], P,
       label="audit.events.viewed", lpos=(1155, 601), lanchor="middle", lcolor=P, lsize=9.5)
s.path([(1210, 685), (1180, 685)], k)
s.path([(775, 745), (775, 775)], k)
s.path([(1335, 745), (1335, 775)], k)
s.path([(775, 855), (775, 925)], k, label="SELECT", lpos=(783, 912))
s.path([(1335, 855), (1335, 925)], k, label="INSERT", lpos=(1343, 912))

# ---------- legend ----------
s.rect(20, 915, 610, 125, "#ffffff", "#999999", rx=8)
s.text(32, 934, "Legenda", 12, "bold")
items = [("new", "nova komponenta (ovaj rad)"), ("existing", "postojeća komponenta Audit modula"),
         ("other", "komponenta drugog tima / veza sa platformom")]
for i, (kind, txt) in enumerate(items):
    f, st = COLORS[kind]
    s.rect(32, 945 + i * 28, 26, 18, f, st, rx=3)
    s.text(66, 958 + i * 28, txt, 10.5)
f, st = COLORS["iface"]
s.rect(320, 945, 26, 18, f, st, rx=8)
s.text(354, 958, "«interface» — ugovor (Odg. = odgovornost)", 10.5)
s.path([(320, 982), (346, 982)], "#333333"); s.text(354, 986, "zavisnost / poziv", 10.5)
s.path([(320, 1004), (346, 1004)], P); s.text(354, 1008, "korišćenje interfejsa (traži)", 10.5)
s.path([(320, 1026), (346, 1026)], "#555555", dash="6 4", head="open"); s.text(354, 1030, "realizuje interfejs (pruža)", 10.5)
s.text(32, 1030, "Akter, vreme i korelacioni ID nikad ne dolaze od klijenta.", 10.5, italic=True, color="#444")

out = s.render("Audit (AUD-01 / AUD-06) — dijagram arhitekture",
               "Ključne komponente, odgovornosti, interfejsi, zavisnosti i veza sa ostalim delovima platforme")
open(sys.argv[1], "w", encoding="utf-8").write(out)
