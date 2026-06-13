---
name: unity-track-essence-intrinsic
description: Master of TimelineEssenceIntrinsicTrack + TimelineEssenceIntrinsicClip (package BovineLabs.Timeline.Essence) — one-shot PERMANENT integer counters with self-healing auto-add and stat-driven clamping. Portable to any project containing the package; worked example from vex-ee. Use when a designer asks "at this beat, grant +N / consume N of a counter".
---

# TimelineEssenceIntrinsicTrack specialist

## 1. SCOPE

You are the specialist for **`TimelineEssenceIntrinsicTrack`** and **`TimelineEssenceIntrinsicClip`** from the package
`BovineLabs.Timeline.Essence`, namespace `BovineLabs.Timeline.Essence.Authoring`. Scope: exactly this track — one clip = one
edge-triggered, **permanent**, clamped delta to an integer counter (`IntrinsicKey` + signed amount) in a resolved entity's
`Intrinsic` buffer. Family fundamentals live in `unity-track-essence-event` (resolver semantics, all-silent guard matrix);
stage construction belongs to `unity-stage-foundations`. Triad: events = transient signals, **intrinsics = permanent integer
counters (THIS — the counter IS the state)**, stats = while-active float modifiers; `routeLink` is LIVE here, unlike Stat's
dead key — only Event and Intrinsic route through links.

**Operate per `unity-timeline-track-authoring`; behave per `unity-agent-protocol`; use the editor per `unity-cli`.**

## 2. TYPE FACTS

True in ANY project containing `BovineLabs.Timeline.Essence` (plus its Essence and Reaction dependencies). Provenance tags =
where PROVEN, not where it applies. (All verified vex-ee 2026-06 via reflection dumps, raw YAML, package-source reads,
fresh-load read-backs, a real forced bake for the silent-null demo — all `unity-cli exec`, no play mode; runtime claims
source-derived.)

| Type | Base | Facts |
|---|---|---|
| `TimelineEssenceIntrinsicTrack` | `DOTSTrack` | sealed, EMPTY body. `[TrackClipType(TimelineEssenceIntrinsicClip)]`, **`[TrackBindingType(BovineLabs.Reaction.Authoring.Core.TargetsAuthoring)]`** (the bind target), `[TrackColor(0.2,0.6,0.9)]`, `[DisplayName("BovineLabs/Essence/Timeline Intrinsic")]` |
| `TimelineEssenceIntrinsicClip` | `DOTSClip` | sealed, `ClipCaps.None`, `duration => 1` (seed only) |
| System | `TimelineEssenceIntrinsicSystem` | `[UpdateInGroup(TimelineComponentAnimationGroup)]`, `[UpdateAfter(typeof(EntityLinkTargetPatchSystem))]` — sees same-frame TargetPatch retargets |

### Clip fields — camelCase (reflection + fresh-instance defaults)

| Field | Type | Default | Meaning |
|---|---|---|---|
| `routeTo` | `BovineLabs.Reaction.Data.Core.Target` | **`Self` (4)** | Who receives the counter change (`None` behaves like `Self`) |
| `routeLink` | `EntityLinkSchema` | null | Optional link override — **LIVE here** (unlike Stat); wins when it resolves, falls back to routeTo otherwise |
| `intrinsic` | `BovineLabs.Essence.Authoring.IntrinsicSchemaObject` | null | Which counter. Null is SILENT at bake (key 0) and runtime-filtered |
| `amount` | `int` | 1 | Signed delta. **Negative is legal = subtract** (serialized verbatim; nothing clamps at authoring). **Zero is a legal quiet no-op — NO zero-assert**, unlike the Event family |

Bake (quoted from `TimelineEssenceIntrinsicClip.Bake`):

```csharp
EntityLinkAuthoringUtility.TryGetKey(routeLink, out var linkKey);
var builder = new EssenceIntrinsicBuilder
{
    RouteTo = routeTo,
    RouteLinkKey = linkKey,
    Intrinsic = intrinsic ? intrinsic.Key : default(IntrinsicKey),   // SILENT null guard
    Amount = amount
};
```

→ adds `TimelineEssenceIntrinsicData { Target RouteTo; ushort RouteLinkKey; IntrinsicKey Intrinsic; int Amount; }` to the clip
entity. No `Debug.LogError` anywhere — Essence = silent everywhere at bake (the family-critical guard rule).

## 3. RUNTIME SEMANTICS — permanent, clamped, self-healing

Everything this track writes **persists forever** — the opposite of the Event track's same-frame evaporation. No deactivation
job, no revert, no stored "before": the only undo is a compensating clip with the opposite amount (the clamp may eat part of
it). The writer **self-heals** missing entity entries (auto-add at schema default), clamps every delta into
static-range-unless-stat-overridden bounds, and carries the family's **only LOUD failure**: an unknown runtime-config key
LogErrors — at RUNTIME, never at bake.

### THE CORE — `IntrinsicWriter.Add` + `GetLimits`, quoted

Source: package `com.bovinelabs.essence`, `BovineLabs.Essence/IntrinsicWriter.cs`.

```csharp
public (int Current, int Delta) Add(IntrinsicKey key, int delta)
{
    if (!this.EssenceConfig.Value.Value.IntrinsicDatas.TryGetValue(key, out var ptr))
    {
        BLGlobalLogger.LogError($"Key {key.Value} not found in the intrinsic config");
        return (0, 0);
    }
    ref var data = ref ptr.Ref;
    ref var intrinsic = ref this.intrinsics.AsMap().GetOrAddRefUnsafe(key, data.DefaultValue);
    var (min, max) = this.GetLimits(data);
    var before = intrinsic;
    intrinsic = math.clamp(intrinsic + delta, min, max);
    delta = intrinsic - before; // The actual delta
    if (Hint.Unlikely(delta == 0))
    {
        return (intrinsic, 0);
    }
    this.TryWriteEvents(data, delta);
    return (intrinsic, delta);
}
```

(`Subtract(key, delta)` is literally `Add(key, -delta)`; `Set` clamps the same way.) `GetLimits`: min/max start at the
schema's static `range`; if the schema sets `MinStatKey`/`MaxStatKey` (≠ 0) AND that stat key is in the entity's stat buffer
(`statMap.TryGetValue`), the bound becomes `(int)math.floor(stat.Value)`; a configured-but-missing stat falls back to the
static bound (TryGetValue — no silent-zero trap here). `TryWriteEvents`: `if (this.eventWriter.IsValid && intrinsicData.Event
!= 0) this.eventWriter.Trigger(intrinsicData.Event, delta);`.

The walkthrough: (1) **config lookup** — the family's ONLY loud failure, at RUNTIME (`LogError` + no-op `(0,0)`); the blob is
built ONLY from `EssenceSettings.intrinsicSchemas` (null slots silently skipped) — the error means "schema removed from /
never added to the list, or stale settings bake"; deleting the schema ASSET instead nulls the clip ref → key 0 → SILENT skip
(**deletion downgrades loud to silent**); missing `EssenceConfig` → the system never updates. (2) **auto-add** —
`GetOrAddRefUnsafe` creates a missing entry at the schema default (self-healing). (3) **clamp** — static range unless
overridden by `floor(stat)` bounds. (4) **effective delta** = clamped − before; 0 → early return, no event — Event's
`Check.Assume(value != 0)` is unreachable from here. (5) **event bridge** — a real change fires the schema's ConditionKey with
the EFFECTIVE delta; the key comes from a `ConditionEventObject` sub-asset nested INSIDE the schema asset (check YOUR schemas,
§5). Also: internal `RestrictMin`/`RestrictMax` re-clamp when a LIMIT stat changes (`EssenceConfig.StatsLimitIntrinsics`
reverse map); they skip `!IsCreated` entities — auto-add applies only to Add/Set/Subtract.

### System pipeline (one paragraph, source-derived)

`TimelineEssenceIntrinsicSystem` mirrors the Event pipeline with a persistent destination: on each clip's ACTIVATION edge only
(`[WithAll(ClipActive)] [WithDisabled(ClipActivePrevious)]` — duration/end/deactivation meaningless), `GatherJob` skips
silently (`if (data.Intrinsic.Value == 0 || binding.Value == Entity.Null) return;`), otherwise resolves the receiver through
the Essence resolver (routeTo first and mandatory; routeLink wins when its hunt resolves, falls back to routeTo otherwise) and
accumulates `(target, key, amount)`; `ApplyJob` coalesces same-frame same-key amounts per receiver in a `FixedList4096Bytes`
(overflow entries apply immediately without joining the sum; entities without an `Intrinsic` buffer are silently skipped by
`Writers.TryGet`) and issues ONE `IntrinsicWriter.Add(key, summedAmount)` per (receiver, key).

### Silence profile + Traps & DO/DON'T (each proven live or source-quoted, vex-ee 2026-06)

Bake-silent even when misconfigured — silence is expected, not evidence. The ONLY loud path is the runtime config-key LogError.

- **DON'T trust a clean console for a null `intrinsic` — silent bake, key 0** — a real forced bake of a null-schema temp clip
  added ZERO error lines to a 17,245-byte worker-log growth; the clip bakes key 0, GatherJob discards it.
- **DO know WHERE the loud error lives — runtime, config-key only** (the walkthrough above). A clean console at bake proves
  nothing.
- **DO lean on the self-heal / DON'T expect it from stats** — a missing intrinsic key auto-creates at the schema default; a
  missing STAT key returns silent 0. Counter API: "not seen yet = default"; stat API: "not authored = 0".
- **DON'T reorder grant/consume clips casually — the floor absorbs, no debt** — a consume at the floor is clamp-absorbed
  (effective delta 0, no event); a different time order yields a DIFFERENT final counter. Consume clips follow their grants.
- **DO treat every write as permanent AND re-firing** — no deactivation-edge job or revert path exists in the system source
  (evidence of absence); every off→on transition re-Adds: loops and scrubs keep incrementing. Only undo: a compensating clip
  (subject to the clamp).
- **DON'T fear amount=0 (Event asserts; Intrinsic doesn't) — but it still creates the entry** — `Add(key, 0)` early-returns,
  no event, no assert — yet auto-add still happens. Same for same-frame coalescing to zero (+3 and −3) — quiet no-op where
  Event would assert in dev.
- **DO prefer schemas with `maxStat` for "max is a stat" semantics** — the receiver must HAVE the bounding stat in its buffer
  (`StatAuthoring.StatDefaults`) or the static range governs; floor: a stat of 4.9 caps at 4; `RestrictMax` re-clamps down
  when the stat lowers (the max-health pattern).
- **DO know the forced-bake tooling traps** — `SubSceneImporter` is internal
  (`Type.GetType("Unity.Scenes.Editor.SubSceneImporter, Unity.Scenes.Editor")`); `ProduceArtifact` BOTH
  `Assets/SceneDependencyCache/*.sceneWithBuildSettings` entries (cached-artifact trap); `IPlayableAsset` lives in
  `UnityEngine.Playables`, not `UnityEngine.Timeline` — qualify it.

## 4. CLIP PATTERNS (the bracket's track-specific middle)

Discovery per `unity-timeline-track-authoring` §1; build per its §2 SubScene bracket. The bind target (§1 D4) is the
**`TargetsAuthoring` COMPONENT** of a SubScene-baked object; the receiver needs `StatAuthoring` with `AddIntrinsics=True` —
**empty `IntrinsicDefaults` is FINE** (auto-add heals, verified). The clip schema field must be a `IntrinsicSchemaObject`
discovered live (§5) and **registered in `EssenceSettings.intrinsicSchemas`** (unregistered = the loud runtime config error);
NEVER create schema assets. Only the clip's START matters (`ClipCaps.None`, duration is a seed). Field set via
`SerializedObject`: `intrinsic` = the schema (objectReferenceValue), `routeTo` = byte (Self = 4), `amount` = signed int.

- **GRANT — "+N at this beat":** one clip, `amount = +N`.
- **CONSUME — "spend N":** one clip, `amount = -N`. At the floor the consume is silently absorbed (no debt, no failure
  signal); gate via a Reaction condition if "can't afford" matters; order consume clips AFTER their grants.
- **COALESCE — "N pickups at once":** N clips, same schema/target/start (same-track overlap is API-legal) → ONE summed `Add`.
- **ROUTE — "give the LINKED actor the points":** `routeTo` = a resolving `Targets` slot (unset = silent total loss);
  `routeLink` = a discovered `EntityLinkSchema` (LIVE here, unlike Stat); a failed hunt falls back to `routeTo`, which still
  receives.

## 5. WORKED EXAMPLE DELTA (vex-ee training stage) — example environment; rediscover, never assume

Shared stage per `unity-timeline-track-authoring` §5. Track-specific deltas:
- Receiver `Stage_Actor` carries `TargetsAuthoring` + `StatAuthoring` with `AddIntrinsics=True`, `IntrinsicDefaults.size=0`,
  `IntrinsicDefaultGroups.size=0` — fine, auto-add heals (contrast: the stat track needed a `StatDefaults` append).
- Schemas: **78** `IntrinsicSchemaObject`s under `Assets/Settings/Schemas/Intrinsics/`, all 78 registered in
  `Assets/Settings/Settings/EssenceSettings.asset` (intrinsics=78, stats=114); **39 of 78 set `maxStat`** (none `minStat`) —
  e.g. `BlazeStacks` (key 10, range [0,5], maxStat → `MaxBlazeStacks.asset`, StatKey 73). Demo schema `BaseTime.asset`: key
  45, default 0, range [0,999999], no stat links, guid `c4e9e530a0074407a46098adb9ebcbee`. **The intrinsic→event bridge is
  dormant in vex-ee**: ZERO of the 78 schemas carry the ConditionEventObject sub-asset — `Event` bakes 0 everywhere; the code
  path is real, the content doesn't use it.
- Asset (lesson 12): `Assets/Training/12-timeline-essence-intrinsic-track/IntrinsicMastery.playable` — one track
  `IntrinsicTrack`, clips A_Add5 (1–1.5s, +5), B_Subtract2 (3–3.5s, −2, `amount: -2` serialized verbatim), C_Add3 + D_Add4
  (both 5–5.5s, deliberately overlapping on one track → coalesce to one `Add(45, +7)`); all `intrinsic=BaseTime`,
  `routeTo=Self`, `routeLink=null`. Bound `IntrinsicTrack → Stage_Actor (TargetsAuthoring)`; binding table grew 9 → **10**.
- Clamp-order math on BaseTime (default 0, floor 0): A then B → 5 then 3; B alone/first → clamp(0−2)=0, effective delta **0**,
  no event; A after → 5, not 3.

## 6. UNDO

Undo per `unity-timeline-track-authoring` §3 (restore director FIRST, then DeleteAsset, then any other captured values;
UNDO-1/2/3 templates + UNDO-4 fresh-load verify). **This track REQUIRES the §3 runtime note**, because its effect persists:

The RUNTIME effect is a permanent counter mutation with no revert path in the package (evidence of absence, §3): deleting the
`.playable` / restoring the director does NOT revert counters in any world that already played, looped, or scrubbed the
timeline. The only compensation is a **compensating clip/write with the opposite amount**, and the clamp can absorb it
asymmetrically — a clamped original's EFFECTIVE delta is only knowable from runtime observation; without observing the live
counter before/after, exact compensation cannot be guaranteed — say so rather than claiming restoration. The verified
authoring workflow never enters play mode, so an authoring session fires nothing: undoing the artifacts (asset + binding) IS a
complete undo. State which case applies. No scene values, schemas, `EssenceSettings`, or `StatAuthoring` fields are mutated
(auto-add heals — no stage mutation needed), so the artifact inventory is just the created `.playable`, its possibly-created
folder, the mutated `director.playableAsset`, and the added binding entry.

## 7. VERIFICATION

Verify per `unity-timeline-track-authoring` §4 (fresh-load asset dump, raw-YAML check, live prerequisite re-check, binding
from a reloaded SubScene, parent-scene restore, console). Track-specific expectations to assert in those steps:
- **Asset dump:** every clip's `intrinsic` + resolved key, `routeTo`, `routeLink`, `amount` — **negatives must read back
  verbatim**; same-start overlapping clips survive reload.
- **Raw YAML:** `routeTo` as a byte; `routeLink: {fileID: 0}` when unset; `intrinsic` guid present (no `{fileID: 0}` where a
  ref was intended); negative `amount` serialized verbatim.
- **Live prerequisite re-check:** re-dump the chosen schema's key/default/range/stat-links (ids and inventories DRIFT) and
  confirm registration in `EssenceSettings.intrinsicSchemas`; the bound object is `TargetsAuthoring`.
- **Binding:** `BINDING|<trackName>|bound=<bindTarget> (TargetsAuthoring)` — the component, not the Transform; prior intact.
- **Console:** bake-silent even when misconfigured; the only loud path is the runtime config-key LogError.

Schema discovery recipe (§4 prerequisite — keys/defaults/ranges/stat-links drift, NEVER create schema assets):
```csharp
var sb = new System.Text.StringBuilder();
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:IntrinsicSchemaObject")) {
    var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var so = new UnityEditor.SerializedObject(UnityEditor.AssetDatabase.LoadMainAssetAtPath(p));
    sb.AppendLine("INTRINSIC|" + p + "|key=" + so.FindProperty("key").FindPropertyRelative("Value").intValue
        + "|default=" + so.FindProperty("defaultValue").intValue + "|range=" + so.FindProperty("range").vector2IntValue
        + "|minStat=" + (so.FindProperty("minStat").objectReferenceValue != null) + "|maxStat=" + (so.FindProperty("maxStat").objectReferenceValue != null));
}
return sb.ToString();
```
