---
version: alpha
name: Shutter Rail
description: A high-contrast product system inspired by contact sheets, viewfinders, and reliable studio equipment.
colors:
  canvas: "oklch(1 0 0)"
  surface: "oklch(0.965 0.006 113)"
  surface-raised: "oklch(0.992 0.002 113)"
  surface-strong: "oklch(0.925 0.012 113)"
  ink: "oklch(0.18 0.018 113)"
  ink-muted: "oklch(0.43 0.025 113)"
  line: "oklch(0.84 0.018 113)"
  primary: "oklch(0.82 0.16 113)"
  primary-hover: "oklch(0.77 0.17 113)"
  primary-pressed: "oklch(0.72 0.16 113)"
  on-primary: "oklch(0.18 0.018 113)"
  accent: "oklch(0.62 0.19 28)"
  on-accent: "oklch(1 0 0)"
  viewfinder: "oklch(0.12 0.01 113)"
  viewfinder-raised: "oklch(0.19 0.014 113)"
  on-viewfinder: "oklch(0.97 0.006 113)"
  success: "oklch(0.56 0.145 145)"
  warning: "oklch(0.74 0.15 78)"
  error: "oklch(0.56 0.185 25)"
  info: "oklch(0.57 0.135 245)"
  focus: "oklch(0.62 0.19 28)"
typography:
  kiosk-display:
    fontFamily: "Manrope Variable, Manrope, sans-serif"
    fontSize: 4rem
    fontWeight: 720
    lineHeight: 1.02
    letterSpacing: -0.035em
  headline-lg:
    fontFamily: "Manrope Variable, Manrope, sans-serif"
    fontSize: 2rem
    fontWeight: 700
    lineHeight: 1.15
    letterSpacing: -0.025em
  headline-md:
    fontFamily: "Manrope Variable, Manrope, sans-serif"
    fontSize: 1.5rem
    fontWeight: 680
    lineHeight: 1.2
    letterSpacing: -0.018em
  headline-sm:
    fontFamily: "Manrope Variable, Manrope, sans-serif"
    fontSize: 1.25rem
    fontWeight: 650
    lineHeight: 1.25
    letterSpacing: -0.012em
  body-lg:
    fontFamily: "Manrope Variable, Manrope, sans-serif"
    fontSize: 1.125rem
    fontWeight: 450
    lineHeight: 1.55
    letterSpacing: 0em
  body-md:
    fontFamily: "Manrope Variable, Manrope, sans-serif"
    fontSize: 1rem
    fontWeight: 450
    lineHeight: 1.5
    letterSpacing: 0em
  label-md:
    fontFamily: "Manrope Variable, Manrope, sans-serif"
    fontSize: 0.875rem
    fontWeight: 650
    lineHeight: 1.25
    letterSpacing: 0.005em
  caption:
    fontFamily: "Manrope Variable, Manrope, sans-serif"
    fontSize: 0.75rem
    fontWeight: 560
    lineHeight: 1.35
    letterSpacing: 0.015em
  technical:
    fontFamily: "IBM Plex Mono, monospace"
    fontSize: 0.8125rem
    fontWeight: 500
    lineHeight: 1.35
    letterSpacing: 0.025em
rounded:
  none: 0px
  sm: 6px
  md: 10px
  lg: 16px
  xl: 24px
  full: 9999px
spacing:
  2xs: 0.25rem
  xs: 0.5rem
  sm: 0.75rem
  md: 1rem
  lg: 1.5rem
  xl: 2rem
  2xl: 3rem
  3xl: 4rem
  4xl: 6rem
  touch-min: 2.75rem
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    typography: "{typography.label-md}"
    rounded: "{rounded.md}"
    height: 3rem
    padding: 0.75rem 1rem
  button-primary-kiosk:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    typography: "{typography.body-lg}"
    rounded: "{rounded.lg}"
    height: 4rem
    padding: 1rem 1.5rem
  input:
    backgroundColor: "{colors.canvas}"
    textColor: "{colors.ink}"
    borderColor: "{colors.line}"
    rounded: "{rounded.md}"
    height: 3rem
    padding: 0.75rem 0.875rem
  panel:
    backgroundColor: "{colors.surface-raised}"
    textColor: "{colors.ink}"
    rounded: "{rounded.lg}"
    padding: 1.5rem
---

# Shutter Rail Design System

## Overview

The physical scene is a guest using a 10-inch tablet in mixed ballroom light while
a photographer watches a Windows console nearby and needs calm, trustworthy status.
That scene calls for a light, high-contrast interface with a dark live-view stage—not
an all-dark tool and not a decorative wedding theme.

The visual identity combines three familiar pieces of photographic equipment:

- the ordered rhythm of a contact sheet;
- the confident crop corners of a camera viewfinder;
- the durable, labeled controls of studio hardware.

The system should feel confident, celebratory, and precise. The guest surface is
spacious and directional. The operator, portal, and editor surfaces are denser and
quiet. Event themes may replace guest background media and selected brand accents,
but system, payment, privacy, warning, focus, and recovery semantics remain protected.

The signature move is the **Shutter Rail**: an ordered strip of shot or workflow
frames that fills as work becomes safely persisted. It is functional wayfinding, not
film decoration.

## Colors

The strategy is restrained: neutral architecture carries the product and color is
spent on actions and state. All implementation colors use the normative OKLCH tokens
in the frontmatter.

- **Canvas:** literal white keeps text and photography honest in unpredictable event
  lighting.
- **Ink:** a brand-hued near-black provides strong readability without blue-gray SaaS
  sameness.
- **Shutter lime:** the primary action and ready state. It appears on one dominant
  action or current selection per screen, not as decoration.
- **Flash coral:** focus, capture emphasis, and rare celebratory accents. It must not
  replace semantic error red.
- **Viewfinder:** the dark stage behind live preview, media, and editor canvas. Dark
  surfaces are local to image work rather than the whole application.
- **Semantic colors:** success, warning, error, and info always pair color with an
  icon and text label.

Tenant event colors may color the attract screen, non-semantic framing, and template
art. If an uploaded color cannot meet contrast requirements, the product adds an
opaque system text plate or uses the nearest accessible system foreground.

## Typography

Use bundled **Manrope Variable** for interface copy and **IBM Plex Mono** only for
technical identifiers, timestamps, device codes, checksums, frame counts, and print
dimensions. The contrast is functional: human instructions versus machine state.

Product typography uses the fixed rem scale in the frontmatter. The guest kiosk may
use the larger fixed kiosk display role because it is read at arm's length; portal
headings do not use fluid marketing scales. Body copy stays at 1rem or larger and is
limited to 65–72 characters where it forms prose.

Use sentence case. Do not use script fonts, decorative display faces, gradient text,
or tiny uppercase eyebrows. Frame numbers such as `SHOT 2 / 3` may use the technical
face and modest tracking because they represent actual sequence metadata.

## Layout

Use a 4-point base with the named spacing scale in the frontmatter. Related controls
sit 8–12px apart; distinct groups usually separate by 24–48px. Space and weight
create hierarchy before color or extra containers.

### Guest kiosk

- Primary target: 10-inch landscape tablet around 1280×800, also usable in portrait.
- One task per screen with a persistent Shutter Rail, one dominant stage, and a
  bottom or right-side action zone reachable by touch.
- Maximum two columns. Secondary choices use progressive disclosure.
- Primary controls are at least 64px high; every target remains at least 44×44px.
- Live preview fills the available stage without hiding crop or safe-area guidance.
- Payment, privacy, and recovery copy remains above the fold at 600px viewport height.

### Operator console and tenant portal

- Desktop app shell with a stable side rail at wide widths, compact top navigation at
  intermediate widths, and a drawer or bottom action bar on narrow screens.
- Use tables for comparable operational records and split views for list/detail work.
  Do not convert every dataset into cards.
- Status summary leads to actionable exceptions. Avoid ornamental metrics.
- Keep page content within a practical maximum width; media/editor workspaces may
  expand wider than forms and prose.

### Template studio

- Four regions at desktop: asset/library rail, central canvas stage, properties
  inspector, and a compact layers/history area.
- Collapse to canvas plus one selected drawer on tablet. The freeform editor is not a
  phone workflow; phones receive view/review and simple metadata actions only.
- The canvas stage is dark, while editing controls remain light. Safe area, bleed,
  crop, and photo slots use distinct line styles and labels, not color alone.

### Private gallery

- Mobile-first from 320px. The final output is the first meaningful element.
- Preserve the original media aspect ratio; do not crop gallery assets into generic
  card thumbnails.
- Download and privacy/expiry information remain easy to find without a sticky CTA
  covering the photo.

Responsive adaptation is structural. Sidebars collapse, tables choose priority
columns or labeled rows, editor panels become drawers, and action zones move into
thumb reach. Do not scale a desktop composition down uniformly.

## Elevation & Depth

Depth comes primarily from tonal layers and overlap required by the workflow. Use a
subtle one-pixel perimeter line plus a restrained two-layer ambient shadow for
floating menus or movable editor panels. Static sections do not need shadows merely
to look like cards.

Reserve stronger elevation for top-layer elements: popovers, drawers, dialogs,
toasts, and drag previews. Build a semantic z-index sequence: base, sticky, popover,
modal backdrop, modal, toast, tooltip. Never use arbitrary emergency values.

No default glassmorphism, large blurred backgrounds, fake material reflections, or
nested card-on-card frames. A double-bezel treatment may appear once on a hero device
or physical preview when it communicates hardware, never on every panel.

## Shapes

The shape language is engineered but approachable. Standard controls use 10px
corners; major guest surfaces use 16px; large media frames may use 24px. Full pills
are reserved for compact statuses, segmented choices, and true toggles—not every
button.

Viewfinder crop corners are the recurring graphic motif. They use fine lines around
real media or capture stages and never masquerade as buttons. Shutter Rail frames are
rectangular with a slightly clipped or indexed edge, clearly separating completed,
current, upcoming, and exception states.

## Components

### Buttons

- One filled shutter-lime primary action per decision screen.
- Secondary buttons use the surface and a complete perimeter line.
- Destructive actions use error color only at the decision point and name the object
  or consequence.
- Focus-visible, pressed, disabled, and hover on capable devices are required.
  Loading, error, and success apply only to asynchronous or fallible actions.
- Press feedback is a fast `scale(0.98)` transform. Loading never changes the button
  width.

### Shutter Rail

The rail represents real ordered work: session steps, required shots, upload items,
or print jobs. Each item contains a number or short label, an icon, and accessible
state text. Current state uses shutter lime; completed state uses ink/success with a
check; exceptions use semantic color and action text. It must remain understandable
in monochrome and to screen readers.

### Viewfinder stage

The stage holds live video, captured photos, output preview, or the editor canvas.
Provide media-fit controls where appropriate, a clear mirror indicator, safe-area
guides, and a visible fallback when camera or media is unavailable. Never hide a
camera disconnect behind the previous frame.

### Forms and selections

Use visible labels and validate on blur or submit, not every keystroke. Selection
tiles are buttons or radios with text labels and a distinct selected state. Package
price, copies, output type, and payment requirement are stated before selection is
committed.

### Dialogs and overlays

Prefer inline or split-view resolution. Use a modal only when focus must be isolated,
such as irreversible deletion or an operator-PIN gate. Use native dialog/popover
semantics or an accessible library and escape overflow clipping through the top
layer or a portal.

### Status and recovery

Every status pairs an icon, label, and concise explanation. Ambiguous hardware or
payment states say what is known, what will not happen automatically, and what the
operator can safely do next. Skeletons represent page loading; spinners are limited
to local actions with an accessible label.

### Template editor

Fabric owns the canvas object model. React owns tools, properties, library, layers,
history commands, publishing, validation, and accessibility alternatives. Layer
order, locks, names, asset references, and print-safe errors are always visible
outside the canvas.

## Motion

Motion weighting is **Emil Kowalski primary, Jakub Krehel secondary, and Jhey
Tompkins selective** for rare guest completion moments. The frequency gate applies
first: high-frequency editing, keyboard commands, and operator scanning are instant
or nearly instant.

- Standard feedback: 100–150ms.
- View, menu, and state transitions: 160–220ms.
- Large drawer or dialog: up to 300ms.
- Use custom ease-out curves such as `cubic-bezier(0.22, 1, 0.36, 1)`.
- Enter motion may combine up to 8px translate, opacity, and a small bounded blur.
  Exit motion is shorter and subtler.
- Animate transform, opacity, and small bounded filter or clip-path effects. Do not
  animate layout properties casually.

The signature guest feedback is a short capture confirmation: a bounded white flash
or clip-path shutter blink followed by the new frame entering the Shutter Rail. It
confirms that the image was persisted; it does not delay the next step. Reduced
motion replaces it with an immediate state and live-region message.

There is no page-load choreography, ambient pulsing, looping attention animation,
parallax, elastic motion, or decorative scroll reveal. GIF and MP4 media provide
pause behavior where required.

## Iconography and Media

Use one Phosphor line icon family at a consistent optical weight. Icons support text;
they do not replace labels on critical guest, payment, privacy, print, or recovery
actions. Do not mix emoji, raster icons, and unrelated icon packs.

Guest photos and templates are the visual content. Do not fill missing media with
generic stock photography. Simulator fixtures must be clearly labeled as simulated
and should exercise different orientations, skin tones, group sizes, and contrast
conditions without pretending to be customer work. They must be synthetic or
separately consented and must never reuse production guest media.

## North-Star Concepts

These project-owned concepts are the selected visual direction. They are composition,
hierarchy, density, atmosphere, and motif references—not screenshots to trace and not
permission to rasterize UI text.

- `docs/design/concepts/shutter-rail-palette.png`
- `docs/design/concepts/guest-kiosk-live-preview.png`
- `docs/design/concepts/tenant-operations-overview.png`
- `docs/design/concepts/template-studio.png`
- `docs/design/concepts/private-gallery-mobile.png`

Carry forward the white/charcoal stage contrast, Shutter Rail, crop-corner motif,
single lime primary action, calm information density, full-width operational strips,
uncropped gallery media, and light-tool/dark-canvas editor split. Do not literalize
generated people, event copy, sample counts, exact pixel spacing, or ornamental marks
inside a template preview.

## Content and Terminology

Use these terms consistently: **event**, **package**, **template**, **shot**,
**session**, **output**, **print**, **private gallery**, **booth**, and **device**.

Buttons use specific verb-plus-object labels: `Start session`, `Take photo`,
`Use this photo`, `Print 2 copies`, `Open private gallery`, `Retry upload`, and
`Keep session`. Avoid `OK`, `Submit`, `Yes`, `No`, `Click here`, and unexplained
technical errors.

Payment and privacy language is factual and calm. Errors answer what happened, why
when known, and what to do. Never use humor when a guest may have paid, when a print
is ambiguous, or when media might be at risk.

## Accessibility

- Meet WCAG 2.2 AA contrast and interaction requirements.
- Visible focus rings use the focus token at 2–3px with offset and at least 3:1
  contrast against adjacent colors.
- Do not disable zoom. Verify reflow at 200% and text at user-selected sizes.
- Use semantic landmarks, headings, labels, descriptions, live regions, and error
  summaries.
- Maintain 44×44px minimum targets; guest primary controls are larger.
- Do not rely on color, position, sound, or animation alone.
- Honor `prefers-reduced-motion`, `forced-colors`, coarse pointers, hover capability,
  safe-area insets, portrait/landscape, and keyboard navigation.
- Timed screens warn before reset and provide a way to extend time where privacy and
  operations allow.

## Do's and Don'ts

- Do make the current state and one safe next action obvious.
- Do reserve shutter lime for primary action, ready state, and current selection.
- Do let event media and templates provide personality inside protected system UI.
- Do use the Shutter Rail only for real sequence or progress information.
- Do verify typography, contrast, and real content at every target viewport.
- Do compare live UI with project-owned north-star concepts before shipping.
- Don't make every section a card or every dashboard tile the same size.
- Don't use gradient text, purple-blue gradients, colored side stripes, or decorative
  hero metrics.
- Don't expose payment success before server verification or soften an ambiguous
  print state into success.
- Don't place gray text on colored backgrounds or hide meaning in color alone.
- Don't use script type, baked-in UI text, fake statistics, dead controls, or generic
  stock images.
- Don't let event theming override focus, warning, error, privacy, payment, or
  recovery semantics.
