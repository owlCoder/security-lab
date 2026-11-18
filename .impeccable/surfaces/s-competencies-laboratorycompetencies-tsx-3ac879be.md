---
version: 1
slug: "s-competencies-laboratorycompetencies-tsx-3ac879be"
primary_target: "frontend/src/components/competencies/LaboratoryCompetencies.tsx"
related_targets: ["frontend/src/pages/CompetenciesPage.tsx"]
---

# Laboratory competencies

Mode: Operate

Primary target: frontend/src/components/competencies/LaboratoryCompetencies.tsx

## Direction contract

THESIS: Make the laboratory-to-competency relationship legible and editable in one place, with no dashboard metrics or unrelated progress features.

OWN-WORLD: Inherit Security Lab's system font, cool light page, white bordered surfaces, dark blue text and blue actions. Keep the existing shell and its 900px content width.

STORY: Open from a laboratory, recognize its title, review linked competencies, then add or remove a single relationship. Read-only users see the same linked content with no edit controls. The server supplies capability and validates every change.

FIRST VIEWPORT: A back link, page title and laboratory context lead into a plain two-column working area, without introductory or section-helper prose or an editable-permission badge. “Katalog kompetencija” heads the wider left column; its search shows “Pretražite kompetencije...” and retains a visually hidden associated label. “Povezane kompetencije (N)” heads the right column. Linked rows have no leading check icons, and separators appear only between rows and below search. Read-only mode retains the “Samo pregled” lock badge. On mobile columns stack in reading order. Checkbox changes save individually; a compact polite autosave status sits below the card rather than in a footer band, while pending items and failures report inline.

FORM: seed: none (pinned). The user requested "a dedicated page opened from a laboratory" and "colors and design do not deviate from the rest of the project". This is a precise extension of the incumbent application.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

Documentation boundary: this extension preserves the incumbent visual system; no visual-world replacement or DESIGN.md rewrite. No shipping raster assets are needed.
