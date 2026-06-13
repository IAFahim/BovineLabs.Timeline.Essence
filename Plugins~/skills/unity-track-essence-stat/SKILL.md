---
name: unity-track-essence-stat
description: Master of TimelineEssenceStatTrack + TimelineEssenceStatClip (package BovineLabs.Timeline.Essence) — while-active stat modifiers (add-on-enter/remove-on-exit by clip identity), the ×100 fixed-point int-Added truth, and the formula Σadded×(1+Σincreased)×Π(1+more). Portable to any project containing the package; worked example from vex-ee.
---

# TimelineEssenceStatTrack specialist

## 1. SCOPE

You are the specialist for **`TimelineEssenceStatTrack`** + **`TimelineEssenceStatClip`** from package
`BovineLabs.Timeline.Essence`, namespace `BovineLabs.Timeline.Essence.Authoring`. Scope: exactly this track — one clip = one
**while-active** `StatModifier` (tagged with its own clip entity as `SourceEntity`) appended to the resolved entity's
`StatModifiers` buffer on activation and removed — exactly that one — on deactivation. Duration IS the effect window. This
topic **closes the Essence triad**: family fundamentals live in `unity-track-essence-event` (resolver semantics, all-silent
guard matrix, dead-`RouteLinkKey`-on-Stat); stage construction belongs to `unity-stage-foundations`. Triad: events =
transient signals, intrinsics = permanent integer counters, **stats = while-active float modifiers (THIS — the only
self-reverting track in the family)**.

Operate per `unity-timeline-track-authoring`; behave per `unity-agent-protocol`; use the editor per `unity-cli`.

## 2. PORTABLE SEMANTICS

True in ANY project containing `BovineLabs.Timeline.Essence` (plus its Essence + Reaction dependencies). Provenance tags =
where PROVEN, not where it applies. (All verified vex-ee 2026-06 via reflection dumps, package-source reads, raw YAML,
fresh-load read-backs, one real forced SubScene bake — all `unity-cli exec`, no play mode; runtime claims source-derived.)

### Type facts

| Type | Base | Facts |
|---|---|---|
| `TimelineEssenceStatTrack` | `DOTSTrack` | sealed, EMPTY body. `[TrackClipType(TimelineEssenceStatClip)]`, **`[TrackBindingType(BovineLabs.Reaction.Authoring.Core.TargetsAuthoring)]`** (the bind target), `[TrackColor(0.2,0.9,0.4)]`, `[DisplayName("BovineLabs/Essence/Timeline Stat")]` |
| `TimelineEssenceStatClip` | `DOTSClip` | sealed, `ClipCaps.Blending \| Looping` (COSMETIC — see traps), `duration => 1` (seed only) |
| System | `TimelineEssenceStatSystem` | `[UpdateInGroup(TimelineComponentAnimationGroup)]`, `[UpdateAfter(typeof(EntityLinkTargetPatchSystem))]` — sees same-frame TargetPatch retargets |

FullNames: `BovineLabs.Timeline.Essence.Authoring.TimelineEssenceStatTrack` / `...TimelineEssenceStatClip`, assembly
`BovineLabs.Timeline.Essence.Authoring`.

### Clip fields — camelCase (reflection + fresh-instance defaults)

| Field | Type | Default | Meaning |
|---|---|---|---|
| `routeTo` | `BovineLabs.Reaction.Data.Core.Target` | `Self` (4) | Who gets the modifier (`None` behaves like `Self`) |
| `routeLink` | `EntityLinkSchema` | null | **Baked but DEAD** — the Stat system never reads it |
| `stat` | `BovineLabs.Essence.Authoring.StatSchemaObject` | null | Which stat. Null → **SILENT bake ABORT** (no component at all) |
| `modifyType` | `BovineLabs.Essence.Authoring.StatAuthoringType` | `Added` (0) | Designer-positive vocabulary, six values (table below) |
| `value` | `float` | 0 | Magnitude. Negation for Subtracted/Reduced/Less happens at BAKE; **Added is `(int)`-truncated** |

Bake (quoted from `TimelineEssenceStatClip.Bake`):

```csharp
if (stat == null) return;                       // SILENT bake abort - no LogError, no component, skips base.Bake too
EntityLinkAuthoringUtility.TryGetKey(routeLink, out var linkKey);
var builder = new EssenceStatBuilder
{
    RouteTo = routeTo,
    RouteLinkKey = linkKey,                     // baked... and never read again (dead)
    Stat = stat.Key,
    ModifyType = StatAuthoringUtil.GetModifier(modifyType),
    Value = modifyType is StatAuthoringType.Subtracted or StatAuthoringType.Reduced
        or StatAuthoringType.Less ? -value : value   // NEGATION at bake; YAML stores the positive value
};
```

→ adds `TimelineEssenceStatData { Target RouteTo; ushort RouteLinkKey; StatKey Stat; StatModifyType ModifyType; float Value;
}` to the clip entity.

### StatAuthoringType → StatModifyType (quoted `StatAuthoringUtil.GetModifier`)

| StatAuthoringType | StatModifyType | Negated at bake? |
|---|---|---|
| `Added` | `Added` (0) | no |
| `Subtracted` | `Added` (0) | **yes** |
| `Increased` | `Additive` (1) | no |
| `Reduced` | `Additive` (1) | **yes** |
| `More` | `Multiplicative` (2) | no |
| `Less` | `Multiplicative` (2) | **yes** |

### StatModifier — the ×100 fixed-point / int-Added truth (family-critical)

`StatModifier { StatKey Type; StatModifyType ModifyType; uint ValueRaw }` — `ValueRaw` is a raw union: `Value` reinterprets as
**int** (used by Added), `ValueFloat` as float (Additive/Multiplicative). The buffer element is `StatModifiers { Entity
SourceEntity; StatModifier Value }`. **Flat adds are integers** — quoted `StatAuthoringUtil.GetValueRaw`, also where
`StatDefaults` entries enter (`StatModifierAuthoring.ToStatModifier()`):

```csharp
case StatAuthoringType.Added:      { var s = (int)value;  return UnsafeUtility.As<int, uint>(ref s); }
case StatAuthoringType.Subtracted: { var s = (int)-value; return UnsafeUtility.As<int, uint>(ref s); }
case StatAuthoringType.Increased:
case StatAuthoringType.More:       { return UnsafeUtility.As<float, uint>(ref value); }
case StatAuthoringType.Reduced:
case StatAuthoringType.Less:       { var neg = -value; return UnsafeUtility.As<float, uint>(ref neg); }
```

And `StatValue` (quoted — the ×100 fixed-point convention):

```csharp
public const float ToInt = 100f;  public const float ToFloat = 1 / ToInt;
public int Added;  public float Multi;
public float Value      => this.Added * this.Multi;
public float ValueFloat => this.Added * this.Multi * ToFloat;   // GetValueFloat consumers divide by 100
```

**The formula** (quoted `StatModifierCalculator` fold; `Sum` starts `{Added=0, Increased=1.0, More=1.0}`; Added `+=` int,
Increased `+=`, More `*= (1 + value)`; commutative — order moot): **`Value = (Σ added) × (1 + Σ increased) × Π(1 + more)`** —
integer Σadded; float consumers (`GetValueFloat`, e.g. the TimelineTimeScale track) read `Value / 100`. Corollary: **a
float-consumed "0.25" must be authored as Added = 25** (`ValueFloat = Added/100`); a literal 0.25 truncates to int 0.

### Runtime semantics (one paragraph, source-quoted)

`TimelineEssenceStatSystem` is pure edge logic: on a clip's activation edge (`[WithAll(ClipActive)]
[WithDisabled(ClipActivePrevious)]`) `GatherAddJob` silently skips null-key/null-binding clips (`if (data.Stat.Value == 0 ||
binding.Value == Entity.Null) return;`), resolves the receiver via `TryResolveTarget(data.RouteTo, binding.Value, ...)` ONLY
(routeLink dead), and enqueues a `StatModifier` whose value is `(int)data.Value` for Added or float otherwise; on the
deactivation edge (mirror attributes) `GatherRemoveJob` enqueues a value-blind `{Target, Source = clipEntity}`. A
single-threaded `ApplyJob` drains ALL removes before ALL adds each frame — each remove deletes at most ONE buffer entry whose
`SourceEntity == remove.Source` (`RemoveAtSwapBack` + `break`), each add appends `{SourceEntity = add.Source, Value =
add.Modifier}`; receivers without a `StatModifiers` buffer are silent `continue` skips; every touched receiver gets
`StatChanged` enabled → `StatCalculationSystem` refolds defaults + live modifiers → `StatChangedResetSystem` clears the flag.

### The receiver gate — `StatsCanBeModified` (quoted `StatsBuilder.ApplyTo`)

The receiver MUST have `StatAuthoring` with `AddStats=True` AND **`StatsCanBeModified=True`**. False removes at bake the
**`StatModifiers` buffer, the `StatChanged` enableable, AND the `StatDefaults` blob** — `StatCalculationSystem` requires all
three, so stats freeze at baked values and the clip is a silent runtime `continue` skip. (This is the track-specific D4
prerequisite — verify it on the routeTo-resolved receiver, per `unity-timeline-track-authoring` §1 discovery.)

### FAMILY SUMMARY (condensed from the family-closing report)

All three Essence tracks: empty sealed `DOTSTrack`s bound to `TargetsAuthoring`; clips carry `routeTo` (default `Self`; the
resolver treats `None` like `Self` — unlike `Targets.Get(None)=Null`) and `routeLink`; all run in
`TimelineComponentAnimationGroup` after `EntityLinkTargetPatchSystem`. **The all-silent Essence guard rule (family-critical):
SILENT EVERYWHERE at bake** — Event/Intrinsic bake THROUGH a null schema (Null/0 key, runtime-filtered); Stat silently ABORTS
bake; runtime silent too, except Intrinsic's one loud config-key LogError. Stat alone listens to BOTH edges — the only
guaranteed-temporary Essence track. `routeTo` is mandatory, resolves FIRST; `routeLink` is LIVE on Event/Intrinsic, **DEAD on
Stat** (`TryResolveTarget`, never `TryResolveLinkedTarget`; confirmed three times). Triage: a clean console proves NOTHING —
verify YAML and schema fields directly. (See siblings `unity-track-essence-event`, `unity-track-essence-intrinsic`.)

### Traps & DO/DON'T (each proven live or source-quoted, vex-ee 2026-06)

- **DON'T trust a clean console for a null `stat` — SILENT bake ABORT** — a real forced bake of a null-stat temp clip produced
  ZERO new error lines in 17,245 bytes of worker-log growth; `if (stat == null) return;` skips builder AND `base.Bake` (the
  family's unique quiet abort).
- **DON'T expect overlaps to blend — blending is COSMETIC** — overlaps serialize REAL blend data (mix curves), yet a grep of
  the `BovineLabs.Timeline.Essence` runtime assembly for `Weight/Mix/Blend/IAnimatedComponent/TrackBlendImpl/Ease` is zero
  hits — overlapping stat clips STACK at full strength, never blend.
- **DO trust SourceEntity-handle symmetric removal** — removal matches `SourceEntity == remove.Source` only (value-blind): two
  identical overlapping clips each remove exactly their own entry, survivor untouched.
- **DO rely on removes-drain-before-adds** — a same-frame back-to-back clip handoff never double-counts.
- **DON'T set `routeLink` expecting routing — DEAD on Stat** — it bakes into `RouteLinkKey` but the system file has zero
  references to it.
- **DO trust scrub/stop safety** — timeline deactivation force-clears `ClipActive` (`ResetOnTimelineDeactivatedJob`) while
  `ClipActivePrevious` stays set; GatherRemoveJob fires next frame — no orphaned modifiers.
- **DON'T put percent/multiplicative clips on a zero-base stat — invisible** — with `Σadded = 0` (int-truncated defaults
  included!), Increased/More clips compute `0 × anything = 0`. Give the stat a whole-number Added default first.
- **DON'T author fractional flat adds — truncated at bake AND runtime** — bake-side `GetValueRaw` does `(int)value`
  (StatDefaults take the same path); runtime-side GatherAddJob does `(int)data.Value` — 0.25 becomes 0 both ways.
- **DO stack percent additively, multiplicative compounding** — two +50% Increased clips = +100%, not +125%; More/Less clips
  COMPOUND (`More *=`).

## 3. CEREMONY POINTERS

- Discovery preamble + the five openers (package check, scene/SubScene, director, bind target, PRE| capture):
  `unity-timeline-track-authoring` §1. **Track specifics:** D1 checks
  `BovineLabs.Timeline.Essence.Authoring.TimelineEssenceStatTrack`; D4 finds the **`TargetsAuthoring`** COMPONENT (not the
  Transform) and ALSO verifies the routeTo-resolved RECEIVER carries `StatAuthoring` with `AddStats=True` +
  `StatsCanBeModified=True` (§2 receiver gate), any non-Self `routeTo` slot is assigned, and discovers a stat schema LIVE
  (`AssetDatabase.FindAssets("t:StatSchemaObject")`, read each match's `key.Value` — **keys DRIFT, NEVER create a schema**;
  guid-sweep the chosen stat for gameplay consumers first and prefer an unconsumed one unless the designer says otherwise).
- The SubScene create-and-wire bracket: `unity-timeline-track-authoring` §2 — fill `<TRACK_TYPE>` =
  `TimelineEssenceStatTrack`, `<CLIP_TYPE>` = `TimelineEssenceStatClip`, `<BIND_TARGET>` = `TargetsAuthoring`, set fields via
  `SerializedObject` YAML names (`stat`, `routeTo`, `modifyType`, `value`); `clip.duration` IS the effect window (not a seed
  cap here). Substitute one of the §4 patterns for the track-specific middle.
- Undo appendix STRUCTURE: `unity-timeline-track-authoring` §3. Runtime note: effect is **while-active and self-reverting**
  (SourceEntity-matched remove; scrub/stop safe) — nothing runtime to compensate; artifacts 1–4 only, no schema/StatDefaults
  mutation.
- Verification protocol: `unity-timeline-track-authoring` §4 — the §1 field dump = every clip's `stat`+key, `routeTo`,
  `modifyType`, `value`; YAML check: `value` stays POSITIVE even for Subtracted/Reduced/Less (negation is bake-only), overlap
  blend YAML is cosmetic/harmless; re-check the receiver's `StatsCanBeModified=True` and the schema key live.

## 4. TRACK-SPECIFIC CLIP PATTERNS (the §2 bracket's middle)

Author positive `value` always; Subtracted/Reduced/Less negate at bake. One clip = one while-active modifier; overlaps STACK
(never blend); loops/scrubs re-fire cleanly.

- **"+N flat during the clip"** → `modifyType = Added` (0), `value = N` where **N is WHOLE** (int-truncated bake + runtime).
  For a float-consumed stat (e.g. TimeScale), author the ×100 form: "0.25 speed" → `value = 25`.
- **"−N flat during the clip"** → `modifyType = Subtracted` (1), `value = N` positive (bakes to negative Added).
- **"+50% during the clip"** → `modifyType = Increased` (Additive), `value = 0.5` — stacks additively across clips. Requires a
  whole-number Added base/default on the stat or it computes `0 × … = 0` (zero-base trap).
- **"×0.75 during the clip"** → `modifyType = Less` (Multiplicative), `value = 0.25` (bakes negative; `Π(1+more)` compounds).
  Use `More` for a positive multiplier (`value = 0.75` → ×1.75 factor).

routeTo defaults to `Self` (the bound `TargetsAuthoring`'s own entity); set a discovered-as-assigned slot (e.g. `Target` = 1,
`Source`, `Owner`) only when the designer wants the modifier to land elsewhere. `routeLink` is DEAD here — leave null.

## 5. WORKED EXAMPLE DELTA (vex-ee training stage) — example; rediscover, never assume

Beyond the shared stage in `unity-timeline-track-authoring` §5: lesson 13 built
`Assets/Training/13-timeline-essence-stat-track/StatMastery.playable` — one track `StatTrack`, clips A_Plus2Flat (0–3s,
Added, value=2), B_Increased50pct (1–4s, Increased, value=0.5, overlapping A), C_Less25pct (5–6s, Less, value=0.25,
routeLink=Schema_Actor kept as living documentation of the dead key); all `stat=SlowMo`, `routeTo=Self`. Receiver
`Stage_Actor` carries `StatAuthoring` `AddStats=True`/`StatsCanBeModified=True` with `StatDefaults[0] = {SlowMo, Added, 0.25}`
(from lesson 04). Schema: `SlowMo.asset` under `Assets/Settings/Schemas/Stats/`, **key 94** (114 StatSchemaObjects total).
Binding `StatTrack → Stage_Actor (TargetsAuthoring)`; director's scene-binding table grew 10 → 11 (prior intact), then
restored to `PositionMastery.playable`. **Formula walkthrough** (base `{SlowMo, Added, 0.25}` contributes `(int)0.25` = **0**):
0–1s A only → **2.0**; 1–3s A+B → `(0+2)×(1+0.5)×1` = **3.0 — NOT 3.375** (the default truncates to 0 at bake); 3–4s B only →
**0**; 5–6s C only → **0** (zero-base trap). The lesson-04 `{SlowMo, Added, 0.25}` "vaccine" computes `0 × 1 / 100 = 0` — to
get "0.25 speed" you must author `value = 25`.
