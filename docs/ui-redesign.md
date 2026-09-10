# Living Diorama — interface redesign

A specification for taking the interface from "works, looks like a prototype" to
something that reads as a released game. Written against the interface as it
actually is on 10 September 2026, not as it was designed.

---

## 0. What is actually wrong, with evidence

Three findings shaped everything below. They are worth stating first because two
of them invalidate the obvious plan.

**The interface is UI Toolkit, not uGUI.** `Packages/manifest.json` has no
TextMeshPro package and no DOTween; there is no `Canvas` and no `UnityEngine.UI`
anywhere in `Assets/Scripts`. Every screen is `GameUI.uxml` + `LivingDiorama.uss`
driven from `UIDocument`. So "prefabs" and "DOTween micro-animations" do not
apply as written, and Section 5 and Section 6 give the native equivalents
instead. TextMeshPro *is* reachable and is a real upgrade — see 3.2.

**The last art pass produced illustrations, not interface assets.** Of the 22
files in `Assets/Resources/UI/Art/`, exactly three are used. `UiSkin.SkinAll`
records why, in its own comments:

> Chips are deliberately not plated. The painted pill is a 21:9 image and a
> currency chip is nearer 2:1, so stretching it crushes the brass rim into a blob.

> Text buttons are deliberately not plated. The painted pill carries its ornament
> across the middle, exactly where the label goes.

Measured: `button_primary.png` is 1536×640 **RGB — no alpha channel at all**, as
are `button_ghost`, `chip_plate`, `bar_track`, `banner_header` and `panel_frame`.
An interface sprite without transparency is a picture of a button, not a button.
Every icon is 1024×1024, four to eight times the size it renders at.

This is the single most important input to the Recraft plan. Section 4 records
what nine test generations showed: the model will not reliably paint an interface
primitive, and asking it to is what produced that folder. So the primitives are
drawn in USS instead, and only the pieces that are genuinely *objects* are
painted.

**Roughly 7 MB of that art ships in every build anyway.** Anything under
`Resources/` is included whether or not it is referenced. The build report lists
`panel_frame.png`, `button_primary.png`, `chip_plate.png`, `bar_track.png` and
`banner_header.png` among shipped assets; none of them draws a single pixel.

A fourth, smaller thing: the body font is loaded as a raw TrueType face
(`resource("Fonts/Nunito-SemiBold")`). That is bitmap text. It cannot carry an
outline or a shadow, and it softens whenever it is scaled.

---

## 1. Art direction

**Lantern-lit brass on night glass.**

The world is warm, saturated and busy. The interface must frame it, not compete
with it, and must not go so quiet that it reads as unfinished — which is exactly
where it sits now: one rounded dark rectangle with a thin gold stroke, repeated
eleven times on the HUD.

Five rules, all of which the asset prompts encode:

1. **Two materials, no more.** Structure is *night glass*: deep blue-black,
   translucent, faintly textured, cool. Ornament is *brass*: warm, worn, lit from
   the upper left. Glass never carries ornament in its middle; brass never covers
   text.
2. **Ornament lives on edges and corners.** Filigree belongs in the outer band of
   a plate, where a nine-slice keeps it un-stretched. The middle of every panel is
   flat by construction, because that is where words go. This is the rule the last
   pass broke.
3. **Colour is information.** The interface is near-monochrome — glass, brass,
   ink. Saturated colour is reserved for rarity, and for one gold accent that
   means "this is worth your attention". A screen where everything glows is a
   screen with no hierarchy.
4. **Elevation is drawn by Unity, not baked into the art.** No drop shadows in the
   PNGs. Depth comes from three glass tints and a hairline. Baked shadows tile
   wrongly, fight the real lighting and cannot be tinted per state.
5. **Flat and front-facing.** Orthographic, no perspective, no vanishing points.
   A button drawn at a three-quarter angle cannot be nine-sliced and cannot sit
   next to one that was. In practice this rule is enforced by *not painting* the
   flat pieces at all -- see 4.2 -- because a generative model cannot be talked
   out of perspective, only routed around.

Reference feel: the quiet chrome of *Egg, Inc.* and *Loop Hero*'s restraint, with
the material warmth of a *Slay the Spire* card frame. Not *Raid*-style gradient
maximalism — the diorama is doing the shouting.

---

## 2. Screen redesign

### 2.1 Main diorama HUD

**What is wrong now.** Eleven separate dark rounded rectangles, all of the same
visual weight, scattered across three corners. Four of them are in the top-left
alone (three currency chips plus a level card). `+20 / hr` — the number an idle
game lives on — is set smaller than the clock above it. `Chronicle 0/9`, `SPIN`
and the gear float in the middle-right in three different shapes, aligned to
nothing. The tray gives the primary action the same footprint as the other two.

**Redesign.**

```
┌─────────────────────────────────────────────┐
│ ▣ 350  │ ◆ 0  │ ⚷ 1        ╭──────────────╮ │   resource rail (one plate)
│ ▬▬▬▬▬▬▬▬▬▬▬░░░░░░  Lv 1     │ Day 1 · 07:01│ │   + level hairline beneath
│                             │  ✦ +20 / hr  │ │   income promoted to gold pill
│                             │ ●●●○○ 3/5    │ │   capacity as pips
│                             ╰──────────────╯ │
│                                          (◉) │   right rail, 56px circles:
│                                          (◎) │   chronicle / spin / settings
│                the diorama                (⚙)│
│                                              │
│                                              │
│  ╭────────────────╮ ╭─────────╮ ╭─────────╮  │
│  │ ▣ MYSTERY BOX ●│ │Collection│ │ Expand  │  │   primary is wider + raised
│  ╰────────────────╯ ╰─────────╯ ╰─────────╯  │
└─────────────────────────────────────────────┘
```

- **One resource rail** replaces four boxes: a single nine-sliced glass bar with
  the three currencies separated by hairlines, and the level bar as a 3px brass
  progress line along its lower edge with `Lv 1` at the right. Four objects become
  one, and the eye stops counting them.
- **Clock plate keeps three facts but ranks them.** `Day 1 · 07:01` in micro
  label, `+20 / hr` promoted to a gold pill at heading size — it is the reason to
  come back — and creature capacity as five pips rather than the string `3 / 5
  creatures`.
- **A right-edge rail** for the three loose buttons: identical 56px brass circles,
  stacked with 10px gaps, aligned to the same right margin as the clock plate.
  Chronicle carries a count badge; SPIN carries a "free" dot when it is free.
- **Tray: one primary, two secondary.** Mystery Box gets 44% of the width, the
  raised brass plate, a 28px chest icon and a notification dot when a free box is
  ready. Collection and Expand are flat glass with brass hairlines. Height 72 plus
  the bottom safe-area inset — the tray currently sits hard against the gesture
  bar.
- **Give the world its frame back.** Top chrome drops from 210px to about 150px,
  and a 120px soft vignette replaces the hard edge so the diorama bleeds under the
  rail instead of stopping at it.

### 2.2 Mystery Box popup

**What is wrong now.** The box art is a 60px thumbnail — the product is the
smallest thing on the screen. The odds read as a sentence that wraps mid-fact
(`Rare 8% | Epic 0% | Legendary` / `0%`). The rainbow bar above it is decoration
because nothing ties a segment to a label. Two competing calls to action stack on
the right at two different widths. The heaviest, widest, most primary-looking
element on the whole screen is **Close**. And the card floats in the middle with
190px of dead space above it.

**Redesign.**

```
╔═══════════════════════════════════════════╗ ✕
║  MYSTERY BOXES                            ║   header band, close as a circle
║  Every box holds one creature.            ║
║ ╭───────────────────────────────────────╮ ║
║ │ ╭─────────╮  WOODEN BOX               │ ║   art 150px in a rarity-tinted well
║ │ │         │  ▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬      │ ║   segmented odds bar
║ │ │  chest  │  ● Common 92  ● Rare 8    │ ║   inline legend, dot = segment
║ │ │         │  ● Epic 0     ● Legend. 0 │ ║
║ │ ╰─────────╯  Rare guaranteed  ▬▬▬░ 3/10│ ║   pity as a meter, not a promise
║ │              ╭───────────────────────╮ │ ║
║ │              │  ▣ 120                │ │ ║   one primary CTA
║ │              ╰───────────────────────╯ │ ║
║ │              ╭───────────────────────╮ │ ║
║ │              │  ▶ Free — watch an ad │ │ ║   ghost, same width, below
║ │              ╰───────────────────────╯ │ ║
║ ╰───────────────────────────────────────╯ ║
╚═══════════════════════════════════════════╝
```

- **The box becomes the hero**: 150px of art in a sunken well tinted by the box's
  top rarity, with a slow idle float.
- **Odds become a chart with a legend.** The segmented bar keeps its widths, and
  each segment gets a legend entry below it — coloured dot, name, percentage — in
  a two-column grid so nothing wraps mid-fact. Zero-percent tiers stay visible and
  dimmed: knowing a Legendary exists and this box cannot give you one is
  information.
- **Pity becomes a meter.** `A Rare is guaranteed within 10 boxes` is a promise;
  showing `3 / 10` against it is a reason to open the fourth.
- **One primary per card**, price on the left as a coin glyph plus number. The ad
  option sits beneath it as a ghost button at the same width, with a play glyph
  and, when spent, a countdown in place of the label.
- **Close becomes a 44px circle at the top-right** of the modal. The bottom slab
  goes. The modal stretches to 88% height so the cards use the space instead of
  floating.

### 2.3 Collection popup

**What is wrong now.** Cards are flat glass with a coloured stroke; rarity is a
1px line. Ownership is a text line (`owned`, `x2`, `undiscovered`) doing the job
of a badge. There is no way to filter, so finding the two creatures you are
missing means reading every card.

**Redesign.**

- **Rarity is the frame**, not a stroke: a nine-sliced rarity plate per tier, so a
  Legendary card is visibly a different object across the room. Five frames, one
  per tier.
- **Portrait sits in a sunken well** with a soft rarity-tinted glow behind it, so
  the creature pops off the card.
- **Ownership becomes a badge** in the top-right corner — `×2`, or a lock glyph.
  The text line under the name goes, and the name gets the space.
- **Locked cards keep the rarity frame, desaturated**, with a silhouette and a
  lock. Knowing a Legendary exists is half the reason to keep opening boxes; the
  card should still advertise it.
- **A segmented filter row** — All / Owned / Missing — under the header band, with
  the discovered count moving into the band itself.
- Three cards across on a phone, 12px gutters, fixed card height (already fixed
  in the last pass so a two-line name cannot misalign a row).
- Tapping opens the full-screen inspector, which already exists and works.

### 2.4 Bottom navigation

- Height 72 + `safe-area-inset-bottom`. The tray currently ends flush with the
  screen edge, under the gesture bar on most phones.
- Three items: one primary (44% width, raised brass, 28px icon), two secondary
  (28% each, flat glass, 24px icon).
- Icon above label on the secondaries, icon beside label on the primary — the
  primary reads as a button, the others as tabs.
- Label 13px semibold, 4px from the icon, single line, never truncated: the
  longest label is "Collection" at 13px = 74px, which fits 28% of a 360dp screen
  with 8px to spare.
- Active/pressed: plate darkens 8%, scales to 0.96, brass edge brightens.
- A notification dot (8px, gold, top-right of the icon) when a free box is ready
  or a Chronicle moment is unread.

---

## 3. Design system

### 3.1 Colour

The existing tokens in `LivingDiorama.uss` are close and should be kept where they
are. What is missing is a glass ramp and a brass ramp — the two materials the art
direction is built on.

| Token | Value | Use |
|---|---|---|
| `--glass-1` | `rgba(14, 17, 28, 0.86)` | flat surfaces, list rows |
| `--glass-2` | `rgba(24, 29, 46, 0.92)` | raised cards, chips |
| `--glass-3` | `rgba(32, 38, 60, 0.96)` | modals, tray |
| `--glass-sunken` | `rgba(8, 9, 16, 0.78)` | wells, bar tracks |
| `--brass-dim` | `rgb(122, 92, 48)` | hairlines, inactive edges |
| `--brass` | `rgb(198, 152, 84)` | default ornament |
| `--brass-bright` | `rgb(255, 213, 138)` | active edge, focus |
| `--gold` | `rgb(255, 199, 88)` | the one accent that means "act" |
| `--ink` | `rgb(240, 243, 250)` | primary text |
| `--ink-dim` | `rgba(240, 243, 250, 0.62)` | secondary text |
| `--ink-faint` | `rgba(240, 243, 250, 0.34)` | disabled, hints |
| `--rarity-*` | unchanged | the only saturated colour |

Contrast floor: body text on `--glass-2` must clear 7:1. `--ink-dim` on
`--glass-1` measures 8.9:1; `--ink-faint` measures 4.6:1 and is therefore only
legal for non-essential text at 15px or above.

### 3.2 Typography

Switch both faces from raw TrueType to **TextMeshPro SDF font assets**. This is
the one part of the original request that maps directly onto UI Toolkit: a
`FontAsset` referenced from `-unity-font-definition` renders through TextCore,
stays crisp at any scale, and unlocks per-style outline and shadow — none of
which raw `.ttf` can do. Generate with the Font Asset Creator at SDF 16, atlas
1024², character set Latin + the Indonesian diacritics.

| Role | Face | Size | Weight | Tracking | Use |
|---|---|---|---|---|---|
| Display | Cinzel Bold | 30 | — | +1 | modal titles |
| Title | Cinzel Bold | 22 | — | +1 | card names, section heads |
| Heading | Nunito Bold | 17 | 700 | 0 | prices, key numbers |
| Body | Nunito SemiBold | 15 | 600 | 0 | descriptions, flavour |
| Label | Nunito Bold | 13 | 700 | +0.5 | buttons, nav |
| Micro | Nunito SemiBold | 11 | 600 | +0.5 | meta, timestamps |

Numerals in prices and counters use tabular figures so a counter ticking from 99
to 100 does not shift the layout.

Minimum body size is 15px. The last pass already had to fix narration set too
small to read; do not regress it.

### 3.3 Spacing, radius, elevation

Spacing scale, 4-based: `4, 8, 12, 16, 20, 24, 32, 40`. Nothing between.

Radii: `--radius-sm 10`, `--radius 16`, `--radius-lg 24`, `--radius-pill 999`.

Three elevations, expressed as glass tint plus edge — UI Toolkit has no real
shadow, and baking one into the art breaks nine-slicing:

| Level | Surface | Edge | Use |
|---|---|---|---|
| 0 | `--glass-1` | none | list rows, inactive tabs |
| 1 | `--glass-2` | 1px `--brass-dim` | cards, chips, ghost buttons |
| 2 | `--glass-3` | nine-sliced brass frame | modals, tray, primary button |

### 3.4 Component inventory

Each becomes a UXML template under `Assets/Resources/UI/Components/`, instantiated
from C#. These are the UI Toolkit equivalent of prefabs.

| Component | Template | Notes |
|---|---|---|
| Chip | `Chip.uxml` | icon + tabular number, three variants |
| ResourceRail | `ResourceRail.uxml` | three chips + level hairline |
| ClockPlate | `ClockPlate.uxml` | day, income pill, capacity pips |
| IconButton | `IconButton.uxml` | 56px circle, optional badge |
| PrimaryButton | `Button.uxml` + `--primary` | brass plate, icon + label |
| GhostButton | `Button.uxml` + `--ghost` | glass, brass hairline |
| BoxCard | `BoxCard.uxml` | art well, odds chart, pity meter, CTAs |
| OddsBar | `OddsBar.uxml` | segments + two-column legend |
| ProgressMeter | `Meter.uxml` | track, fill, `n / m` label |
| CreatureCard | `CreatureCard.uxml` | rarity frame, portrait well, badge |
| ModalCard | `Modal.uxml` | frame, header band, close circle |
| SegmentedControl | `Segments.uxml` | collection filter |
| Badge | `Badge.uxml` | count or lock |
| Toast | existing | restyle only |

### 3.5 States

| State | Transform | Surface | Duration |
|---|---|---|---|
| default | scale 1 | base | — |
| pressed | scale 0.96 | darken 8% | 90ms in, 140ms out |
| disabled | scale 1 | opacity 0.45, no brass | instant |
| focus | scale 1 | edge → `--brass-bright` | 120ms |
| notify | — | gold dot, breathing pulse | 1.8s loop |

---

## 4. Recraft asset plan

### 4.1 What was learned by testing before committing

Nine images went into finding out how this model behaves before spending the
batch. The result changed the plan, so it is worth recording.

**Recraft V3 draws objects well and interface primitives badly.**

| Asked for | Got |
|---|---|
| ornate panel frame | usable first time: flat, transparent, ornament on the border |
| brass key / chest / scroll icons | usable first time, consistent style |
| pill button plate | a photographic brass doorbell in three-quarter perspective, with **PRESS** engraved across the middle |
| the same, harder negative wording | still 3D, still photographic, text gone |
| the same, "flat vector game UI sprite" wording | an illustration *of* a game UI mockup: gibberish text, and Mario |
| stack of coins | a **$** stamped on the faces and a baked drop shadow |
| any `substyle` (`2d_art_poster`, `bold_fantasy`, ...) | editorial illustration; one returned a photo-real astronaut |
| `style: vector_illustration` | genuinely flat, but returns **SVG**, and rasterising it on Windows needs a native cairo that is not there |

The instruction "flat, orthographic, no perspective, no text" does essentially
nothing. Fighting the model costs credits and produces worse art than letting it
draw an object the way it wants to.

### 4.2 So the interface is built from three sources, not one

This is a better design than the original brief, not a workaround. Premium mobile
interfaces draw their primitives in code and reserve painted art for the pieces
that carry identity.

| Source | What it makes | Why |
|---|---|---|
| **Painted (Recraft)** | ornate frames, rarity frames, box art, emblem, object icons | These carry the fantasy. They are things, which the model draws well. |
| **USS** | every button, chip, rail, tray, bar, well, badge | A rounded rectangle with a rim is three lines of USS. It costs no texture memory, stays sharp at any density, tints per state, and cannot come back with a word engraved on it. |
| **Generated SDF in code** | cross, tick, play triangle, chevron, plus | Geometric glyphs. `EmoteIcons.cs` already does exactly this for the mood bubbles; extend it to `GlyphIcons` rather than shipping fifteen tiny PNGs. |

The previous art pass tried to paint the primitives. Every one of those files is
still on disk, unused, and `UiSkin` explains in its own comments why. Not
repeating that is the single most valuable outcome of this plan.

### 4.3 Shared style suffix

Append verbatim to every painted prompt. No flatness or perspective instructions:
they were measured to do nothing.

> Stylised hand-painted fantasy game art, warm gold and brass with deep navy
> shadow, bold readable silhouette, viewed straight on, crisp clean edges, no
> text, no letters, no numbers, no shadow cast on the ground, isolated on a fully
> transparent background

### 4.4 The 23 painted assets

`SL` = nine-sliced; border is the sprite border in shipped pixels.

| # | Key | Gen | Ship | Border | SL |
|---|---|---|---|---|---|
| 1 | `panel_modal` | 1024x1024 | 512x512 | 64 | yes |
| 2 | `band_header` | 1536x1024 | 512x160 | 48 / 24 | yes |
| 3-7 | `frame_common` ... `frame_legendary` | 1024x1024 | 256x256 | 32 | yes |
| 8-19 | twelve object icons | 1024x1024 | 128x128 | - | |
| 20-23 | `box_wooden`, `box_arcane`, `rarity_burst`, `title_emblem` | 1024x1024 | 512x512 | - | |

**1 - panel_modal** — modal background.
> A tall rectangular ornate panel of dark navy-black glass in a thin warm brass
> frame, with small filigree scrollwork in the four corners only. The centre of the
> panel is plain empty glass with no decoration on it. `[suffix]`

**2 - band_header** — the title bar inside a modal.
> A long narrow horizontal brass nameplate bar of dark navy glass, with a small
> brass scroll ornament at its far left tip and another at its far right tip. The
> long middle stretch is plain empty glass. `[suffix]`

**3-7 - rarity frames** — one prompt, five metals.
> A square rounded ornate picture frame, hollow and completely empty in the middle
> so artwork can show through. The frame band is {**common:** plain dull pewter, very
> simple, no decoration / **uncommon:** weathered bronze with a small leaf motif at
> the top centre / **rare:** polished blue steel with a single faceted gem at the top
> centre / **epic:** dark violet metal with ornate scrollwork along the top and
> bottom / **legendary:** radiant gold with elaborate filigree at all four corners and
> a small crown at the top centre}. Decoration confined to the frame band itself.
> `[suffix]`

**8-19 - object icons** — `A single {subject}. [suffix].`

| Key | Subject |
|---|---|
| `icon_coin` | ornate fantasy gold medallion coin with a smooth blank unmarked face |
| `icon_essence` | teardrop-shaped glass vial of glowing teal liquid, corked with brass |
| `icon_key` | ornate old brass key with a looped bow and a simple toothed bit |
| `icon_chest` | small closed treasure chest of dark wood with brass bands and a lock plate |
| `icon_collection` | open leather-bound book with brass corner fittings |
| `icon_chronicle` | rolled parchment scroll tied with a red ribbon |
| `icon_expand` | small pennant flag on a wooden pole planted in a patch of grass |
| `icon_settings` | brass cog wheel with eight thick teeth |
| `icon_spin` | brass fortune wheel with eight coloured segments |
| `icon_lock` | closed brass padlock |
| `icon_clock` | brass sundial with a triangular gnomon |
| `icon_paw` | small rounded animal paw print |

"Blank unmarked face" on the coin is load-bearing: without it the model stamps a
dollar sign on it.

**20-23 - illustrations**

| Key | Prompt |
|---|---|
| `box_wooden` | A closed rustic wooden treasure chest bound with plain iron bands, humble and worn, sitting closed and still. `[suffix]` |
| `box_arcane` | A closed ornate arcane chest of dark wood and violet crystal with faintly glowing runes along its bands, sitting closed and still. `[suffix]` |
| `rarity_burst` | A radial burst of light rays and small sparkles spreading from a single centre point, white and pale gold, soft at the outer edge, nothing at all in the middle. `[suffix]` |
| `title_emblem` | An ornate circular brass emblem containing a tiny stylised floating island with one tree on it, filigree running around the rim. `[suffix]` |

**920 credits** of the 5,000 on the account, leaving room to regenerate any that
come back wrong.

### 4.5 Post-processing, per asset

`Tools/recraft_ui_pipeline.py` does all of this:

1. Download the RGBA PNG.
2. **Alpha-bleed** — Recraft writes a checkerboard into the RGB channels beneath
   transparent pixels. Left alone, bilinear filtering drags grey into every edge
   and each plate gets a halo. The colour is flooded outward first.
3. **Trim** to the alpha bounding box, so the ornament lands where the nine-slice
   border expects it.
4. **Downscale** (Lanczos) to the ship size. Nothing leaves at generation size:
   the last pass shipped 1024-pixel icons that render at 24.
5. Write `Assets/Resources/UI/Art/<key>.png` plus `_import.json` carrying the
   borders. An editor menu item applies them through `TextureImporter`;
   hand-written `.meta` files risk a guid collision, which is a far worse failure
   than a wrong border.
6. **Report alpha coverage and byte size per asset.** An asset that comes back
   with no transparent margin is a picture of a panel rather than a panel, which
   is the exact failure mode of the last set, now caught by a number instead of by
   eye.

## 5. Unity implementation plan

Five phases. Each ends with something checkable, because on this project the
things that broke were always the things nobody measured.

### Phase 0 — foundations (no visual change yet)

1. **Delete the dead art.** Remove the 22 files in `Assets/Resources/UI/Art/` that
   nothing references. Frees ~7 MB of APK. Keep `title_emblem` until its
   replacement lands.
2. **Build SDF font assets.** Window ▸ TextMeshPro ▸ Font Asset Creator for
   Cinzel-Bold and the three Nunito weights, SDF 16, 1024² atlas, Latin +
   diacritics. Save to `Assets/Resources/Fonts/`. Point
   `-unity-font-definition` at the `.asset`, not the `.ttf`.
3. **Token pass in `LivingDiorama.uss`.** Add the glass and brass ramps from 3.1,
   the spacing scale and the three radii. Do not touch layout yet.
4. **Create the skin table.** Replace `UiSkin`'s hardcoded string pairs with a
   `UiSkinTable` ScriptableObject mapping USS class → sprite key → slice border.
   This is what makes the art swappable without a code change.

*Verify:* 67 tests still pass; APK shrinks; a capture shows text unchanged in
size and position but visibly crisper.

### Phase 1 — components

Build the templates in 3.4 as `.uxml` files under
`Assets/Resources/UI/Components/`, each with a matching USS block. Add a small
`UiKit` static that loads and instantiates them:

```csharp
public static class UiKit
{
    static readonly Dictionary<string, VisualTreeAsset> Cache = new();

    public static VisualElement Make(string template)
    {
        if (!Cache.TryGetValue(template, out VisualTreeAsset tree))
        {
            tree = Resources.Load<VisualTreeAsset>("UI/Components/" + template);
            Cache[template] = tree;
        }
        return tree == null ? new VisualElement() : tree.Instantiate();
    }
}
```

This is the UI Toolkit equivalent of a prefab, and it is what lets the collection
grid and the box list stop building their markup in C#.

*Verify:* a scratch screen that instantiates one of every component, captured and
eyeballed; all 22 playthrough steps still pass.

### Phase 2 — screens

In order of payoff: HUD rail and tray first (seen constantly), then Mystery Box
(the monetisation screen), then Collection.

Each screen is a `GameUI.uxml` edit plus a panel-class edit. `BoxPanel`,
`CollectionPanel` and `GameUI` shrink as markup moves out of C# and into
templates.

*Verify:* extend `PlaymodeCapture`'s painted-UI report to also flag any two
visible elements whose rectangles overlap by more than 4px, and any text whose
contrast against the pixel behind it falls under 4.5:1. Both faults have shipped
here before; both are catchable as numbers.

### Phase 3 — motion

Section 6. USS transitions first, code-driven tweens only where a value has to be
interpolated rather than a style.

### Phase 4 — polish and audit

Safe-area insets, an in-game reduced-motion toggle, and one pass on device.

### Sprite import settings

For every nine-sliced asset:

```
textureType: Sprite (2D and UI)
spriteMode: Single
spriteMeshType: FullRect        # required; Tight breaks slicing
spriteBorder: {x, y, z, w}      # from the table in 4.3
mipmapEnabled: 0                # UI never minifies
wrapMode: Clamp
filterMode: Bilinear
alphaIsTransparency: 1
maxTextureSize: 512 (plates) / 256 (frames) / 128 (icons)
textureCompression: ASTC 6x6
```

`spriteMeshType: FullRect` is the one that bites: with the default `Tight`, Unity
trims the transparent margin and the slice insets no longer line up with the art.

---

## 6. Motion

### 6.1 On DOTween

DOTween is not in the project, and it does not animate UI Toolkit. It tweens
`Transform`, `Material`, `CanvasGroup` and raw values; a `VisualElement` has none
of those — its geometry lives in the resolved style. Adding it would mean adding a
package plus a bridge layer to convert every tween into a style write each frame.

UI Toolkit's own two mechanisms cover everything below and cost nothing:

- **USS transitions** — declarative, run on the layout thread, the right tool for
  every state change.
- **`element.experimental.animation`** — code-driven interpolation for values
  that are not styles, such as a counter ticking up.

Recommendation: **do not add DOTween for the interface.** The one place it would
genuinely earn its keep is sequencing the 3D unboxing — and `UnboxingDirector`
already hand-rolls that, working, today.

The specification below is what actually matters, and it is written so it can be
implemented either way.

### 6.2 Motion specification

| Moment | Motion | Duration | Easing |
|---|---|---|---|
| Modal opens | scale 0.94 → 1, opacity 0 → 1 | 200ms | `ease-out-back` |
| Modal closes | scale 1 → 0.97, opacity → 0 | 130ms | `ease-in` |
| Scrim | opacity 0 → 1 | 160ms | `ease-out` |
| Card list appears | each card y +12 → 0, opacity 0 → 1, staggered 40ms | 220ms | `ease-out-cubic` |
| Button press | scale → 0.96, surface darkens | 90ms | `ease-out` |
| Button release | scale → 1 | 150ms | `ease-out-back` |
| Currency changes | number counts up, chip flashes brass | 500ms / 220ms | `ease-out-cubic` |
| Odds bar on open | each segment width 0 → target, staggered 60ms | 420ms | `ease-out-cubic` |
| Pity meter | fill width to target | 500ms | `ease-out-cubic` |
| Tab switch | underline slides between tabs | 180ms | `ease-in-out` |
| Free box ready | primary button breathes scale 1 ↔ 1.02 | 1800ms loop | `ease-in-out-sine` |
| Toast | y +20 → 0 + fade in, hold, fade out | 200 / 2400 / 200ms | `ease-out` |
| Rarity reveal | burst scale 0.6 → 1.4, opacity 1 → 0 | 700ms | `ease-out` |
| Inspector opens | viewport opacity 0 → 1, model yaw settles | 260ms | `ease-out` |

Two rules that keep it from becoming noise:

- **Nothing loops except a call to action.** The breathing primary stops the
  moment the box is opened. A permanently animating interface is a tiring one.
- **Respect reduced motion.** One boolean in settings that swaps every duration to
  0 and disables the loops. Some players get motion sick; more to the point, it is
  the difference between an interface that feels considered and one that does not.

### 6.3 How it is written

Declarative, for every state change:

```css
.button {
    transition-property: scale, background-color, border-color;
    transition-duration: 150ms, 150ms, 120ms;
    transition-timing-function: ease-out-back, ease-out, ease-out;
}

.button:active { scale: 0.96 0.96; }
```

Code-driven, only where the value is not a style:

```csharp
// A coin counter ticking up. `from` and `to` are the real numbers, so the label
// never disagrees with the save file at the end of the animation.
element.experimental.animation
    .Start(from, to, 500, (e, v) => label.text = Mathf.RoundToInt(v).ToString("N0"))
    .Ease(Easing.OutCubic);
```

---

## Appendix — running the asset pipeline

```bash
python Tools/recraft_ui_pipeline.py plan      # print the asset table, cost, no API calls
python Tools/recraft_ui_pipeline.py generate  # everything not already on disk
python Tools/recraft_ui_pipeline.py generate panel_modal button_primary
python Tools/recraft_ui_pipeline.py report    # alpha coverage and size per asset
```

`RECRAFT_API_KEY` comes from `.env`, which is gitignored. State lives in
`Tools/.recraft_state.json` so a re-run never re-spends credits on an asset that
already came back.
