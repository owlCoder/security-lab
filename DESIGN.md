---
name: Security Lab
description: Existing light interface for laboratory work and competency links.
colors:
  action: "#315d8a"
  action-hover: "#244a70"
  navigation-link: "#245589"
  ink: "#223045"
  muted: "#52647b"
  laboratory-muted: "#5b6b7f"
  page: "#f7f9fc"
  surface: "white"
  line: "#e1e6ed"
  laboratory-line: "#d5dde7"
  input-line: "#b9c5d3"
  info: "#eaf1f8"
  info-line: "#9db7d3"
  info-ink: "#1f3550"
  selected: "#edf3f9"
  error: "#fdecec"
  error-line: "#e3a3a3"
  error-ink: "#5a1a1a"
  success: "#e8f5ec"
  success-line: "#9ad0a8"
  success-ink: "#173d22"
  warning: "#fff6e0"
  warning-line: "#f0d69a"
  warning-ink: "#7a5300"
typography:
  body:
    fontFamily: "system-ui, -apple-system, sans-serif"
  headline:
    fontSize: "clamp(1.75rem, 3vw, 2.125rem)"
    lineHeight: 1.2
    letterSpacing: "-.02em"
  title:
    fontSize: "1.125rem"
    lineHeight: 1.4
  section-title:
    fontSize: "1rem"
    lineHeight: 1.4
  item:
    fontSize: ".9375rem"
    fontWeight: 600
  help:
    fontSize: ".875rem"
    lineHeight: 1.6
  label:
    fontSize: ".8125rem"
    fontWeight: 600
  code:
    fontFamily: "ui-monospace, monospace"
    fontSize: ".8rem"
rounded:
  small: "6px"
  control: "8px"
  panel: "10px"
  surface: "12px"
  chip: "16px"
spacing:
  small-gap: "8px"
  medium-gap: "12px"
  large-gap: "16px"
  panel-gap: "20px"
  workspace-inset: "24px"
  section-separation: "32px"
components:
  button-primary:
    backgroundColor: "{colors.action}"
    textColor: "{colors.surface}"
    rounded: "{rounded.control}"
    padding: "10px 14px"
  button-primary-hover:
    backgroundColor: "{colors.action-hover}"
  button-search-secondary:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.action}"
    rounded: "{rounded.control}"
    padding: "8px 12px"
  input-search:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: "10px 12px"
  card:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.surface}"
    padding: "22px"
  chip-example:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.action}"
    rounded: "{rounded.chip}"
    padding: "6px 10px"
  competency-option:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    padding: "17px 12px"
  competency-option-selected:
    backgroundColor: "{colors.selected}"
---

# Design System: Security Lab

## Overview

**Creative North Star: "The existing Security Lab interface"**

Security Lab uses a quiet, light interface with system typography, white working surfaces, dark blue text and blue actions. Preserve these user-confirmed commitments and Serbian feature text when extending the application. Hierarchy comes from headings, borders, spacing and native controls.

This is a descriptive scan of the incumbent laboratory interface and the completed PRG-04 extension, not a new visual identity. Primary target: `frontend/src/components/competencies/LaboratoryCompetencies.tsx`; route wrapper: `frontend/src/pages/CompetenciesPage.tsx`. Source authority: `frontend/src/style.css`, `frontend/src/components/labs/labs.css`, `frontend/src/components/labs/labSearch.css` and `frontend/src/components/competencies/competencies.css`, with durable constraints from `PRODUCT.md` and the matching `.impeccable/surfaces/s-competencies-laboratorycompetencies-tsx-3ac879be.md`.

Visual evidence: `.impeccable/review/incumbent.png`, `desktop.png`, `mobile.png` and `readonly.png` in that review directory. These are local review captures, not shipping assets. Tokens below describe their stated scope; related modules retain distinct local values and are not claimed to be globally uniform. No world seed, custom font or new image asset was introduced.

**Key Characteristics:**

- Cool light page with white bordered surfaces.
- System typography and restrained blue controls.
- Native form controls with visible keyboard focus.
- Responsive working layouts and explicit inline states.

## Colors

### Primary

Action Blue is the shared laboratory and competency action color. Its darker hover value belongs to laboratory buttons. Navigation Link Blue is the existing shell and landing-link variant; it is not a replacement primary color.

### Neutral

Ink supplies default text. Page and Surface separate the cool page from white working areas. Line outlines global surfaces and the competency workspace; Laboratory Line remains a local laboratory variant. Muted is used in competencies, while Laboratory Muted remains the laboratory helper color. Input Line is shared by laboratory and competency fields.

### State colors

Info supplies the pale blue laboratory notices and selected search filters. Selected is the competency-row fill. Success, Error and Warning each retain the existing laboratory background, border and text combination. Competency catalog errors also use the existing global error text value (`#8a1f1f`); general competency failures appear as readable feedback panels.

**The Existing Palette Rule.** Extend the existing light palette and white surfaces; reuse the color variant belonging to the surrounding component.

## Typography

The family is the system stack in `typography.body`; there are no downloaded display or body fonts. Use that same family for headings, controls and prose. Monospace is confined to existing laboratory codes and request identifiers.

The recorded headline, title, section-title, item, help and label roles describe the competency page. Its heading uses the compact responsive headline token; the global heading remains larger (`clamp(2rem, 5vw, 3rem)`, line height `1.15`). The competency and laboratory-search scopes set body line height to `1.5`; the root stylesheet does not establish a universal body line height or font-size token. Counts use tabular numerals.

**The System Type Rule.** Keep the user-confirmed system typography, and preserve local hierarchy rather than imposing one heading scale on every module.

## Layout

The existing global `main` has a maximum width of `900px`, centered margins and horizontal padding of `24px`; its default vertical margin is `8vh`. The competency route uses a real main landmark and inherits that width, with its own vertical margins of `36px auto 56px`. The global site navigation is a separate flex layout; it has no matching maximum-width rule and was not redesigned.

Global sections and cards use the recorded surface inset and separation. Laboratory panels use `18px` insets and `20px` grid gaps. Laboratory search uses a more spacious `24px` rhythm and fields/actions at least `44px` high.

The competency workspace has a full-width laboratory context row, then a `1.45fr / 1fr` catalog-and-linked grid. Desktop content insets are `24px`, with `26px` above each column. Feedback prose is limited to `65ch`. At `680px` and below the context becomes a grid, columns stack in source order, insets become `20px`, and remove controls grow from `32px` to `40px`. Read-only mode has one white linked-content column and no catalog or editing controls. The global navigation also stacks at this breakpoint. Laboratory search additionally collapses its search row and filter columns at `560px`.

## Elevation & Depth

Laboratory panels and the competency workspace are flat at rest. White fills, cool tinted supporting areas and thin borders provide separation; there is no shadow token for these surfaces. The existing login button alone has a hover shadow (`0 2px 6px rgba(34, 48, 69, .25)`), which is not a general card elevation.

**The Flat Surface Rule.** Use the existing border and tonal treatments for laboratory and competency containers.

## Shapes

Controls are gently rounded, with the recorded small and control radii. Global surfaces and the competency workspace use the surface radius; laboratory panels use the panel radius. Laboratory example chips use the chip radius. Existing badges have their own pill-like radii and are not a universal control shape. Competency options are flat separated rows inside a rounded outer workspace.

## Components

### Buttons

Primary laboratory actions inherit the global filled blue button with medium-weight text. Laboratory hover uses Action Hover; disabled buttons inherit opacity `.6` and a blocked cursor. Search secondary actions use a white fill, Info Line border and blue text, changing to Info on hover. The existing global secondary variant instead has an Action Blue border; keep that local difference.

### Inputs / Fields

Laboratory and competency fields use white backgrounds, Input Line strokes and the recorded control radius and padding. Laboratory fields retain visible labels. The competency search has the associated, visually hidden label “Pretražite kompetencije” and visible placeholder “Pretražite kompetencije...”. Laboratory search fields have the minimum height described above. Laboratory invalid inputs use `#b3261e` and the laboratory form can add an inset-like one-pixel shadow.

Global inputs and buttons have a `2px` Action Blue outline offset by `2px`. Laboratory controls override it with a `3px` outline in `#1f4468`, offset by `2px`. Competency anchors, inputs and buttons use a `3px` Action Blue outline offset by `3px`. Preserve visible native checkbox state. Competency selection and caret colors are blue, and the local color scheme remains light.

### Navigation

The existing shell has a bold brand link, blue text links and a thin bottom border. Its layout and mobile stacking remain as implemented. The competency page adds a small blue back link with an inline SVG arrow, an underline on hover and the local focus treatment. Do not treat that page-level link as a new global navigation pattern.

### Chips

Laboratory example chips are white with a blue text and border treatment, becoming pale blue on hover. Search filter chips and checkbox tags are a distinct, less rounded `44px` minimum-height pattern; checked tags gain a blue border and pale blue fill.

### Cards / Containers

Reuse the white, thin bordered global card for surrounding content. The competency workspace removes the generic section inset, clips its contents to the outer radius and supplies its own internal context and columns. Its linked column is tinted only in editable mode. A compact autosave live status sits below the card, without a footer band.

### Competency selection and feedback

The catalog heading is “Katalog kompetencija”. Each option pairs a native checkbox with a name and optional description. A selected row receives the Selected fill; a hovered row uses Page. The background transition is `140ms ease-out`, removed under reduced motion. Changes save one relationship at a time, disable competing edit controls while pending and show the pending item inline. Separators sit between the search and options and between rows, with no trailing row border.

The linked heading reads “Povezane kompetencije (N)”. Its rows contain names and labelled remove buttons without leading check icons; lines separate adjacent rows only. The page has no introductory prose, editable-permission badge or helper copy beneath the catalog and linked headings. Read-only mode retains names and descriptions, the parenthesized count, “Samo pregled” lock badge and lock explanation, without edit affordances. Loading, empty catalog, empty links, no search matches, catalog failure and unconfirmed mutation have explicit Serbian text. The compact status below the card uses a polite live region and defaults to “Izmene se automatski čuvaju.”; errors use alerts and retry actions where implemented. These are feature patterns, not claims that every module implements identical states.

## Do's and Don'ts

### Do:

- Do preserve the existing light palette, white surfaces and system font.
- Do inherit the existing main width and keep the surrounding shell intact.
- Do retain Serbian feature labels and explicit capability, pending, empty and failure states.
- Do use the local focus treatment and native form semantics.

### Don't:

- Don't introduce a replacement palette, display font or decorative asset for an ordinary extension.
- Don't flatten local laboratory, search and competency variants into an invented universal token.
- Don't imply edit permission using color alone; retain the visible text and read-only behavior.
- Don't promote existing module-specific decoration or spacing differences into new global rules.
