---
name: unity-track-essence-stat
description: Master of TimelineEssenceStatTrack + TimelineEssenceStatClip (package BovineLabs.Timeline.Essence) — while-active stat modifiers (add-on-enter/remove-on-exit by clip identity), the ×100 fixed-point int-Added truth, and the formula Σadded×(1+Σincreased)×Π(1+more). Portable to any project containing the package; worked example from vex-ee. Use when a designer asks "during this clip, buff/nerf a stat".
---

# TimelineEssenceStatTrack specialist

## 1. SCOPE

You are the specialist for **`TimelineEssenceStatTrack`** and **`TimelineEssenceStatClip`** from the package
`BovineLabs.Timeline.Essence`, namespace `BovineLabs.Timeline.Essence.Authoring`. Scope: exactly this track — one clip = one
**while-active** `StatModifier` (tagged with its own clip entity as `SourceEntity`) appended to the resolved entity's
`StatModifiers` buffer on activation and removed — exactly that one — on deactivation. Duration IS the effect window. This
topic **closes the Essence triad**: family fundamentals live in `unity-track-essence-event` (resolver semantics, all-silent
guard matrix, dead-`RouteLinkKey`-on-Stat — this skill is its third confirmation); stage construction belongs to
`unity-stage-foundations`. Triad: events = transient signals, intrinsics = permanent integer counters, **stats = while-active
float modifiers (THIS — the only self-reverting track in the family)**. Behave per unity-agent-protocol; operate the editor
per unity-cli.

## 2. PORTABLE SEMANTICS

True in ANY project containing `BovineLabs.Timeline.Essence` (plus its Essence and Reaction dependencies). Provenance tags =
where PROVEN, not where it applies. (All verified vex-ee 2026-06 via reflection dumps, package-source reads, raw YAML,
fresh-load read-backs, one real forced SubScene bake — all `unity-cli exec`, no play mode; runtime claims source-derived.)

| Type | Base | Facts |
|---|---|---|
| `TimelineEssenceStatTrack` | `DOTSTrack` | sealed, EMPTY body. `[TrackClipType(TimelineEssenceStatClip)]`, `[TrackBindingType(BovineLabs.Reaction.Authoring.Core.TargetsAuthoring)]`, `[TrackColor(0.2,0.9,0.4)]`, `[DisplayName("BovineLabs/Essence/Timeline Stat")]` |
| `TimelineEssenceStatClip` | `DOTSClip` | sealed, `ClipCaps.Blending \| Looping` (COSMETIC — see traps), `duration => 1` (seed only) |
| System | `TimelineEssenceStatSystem` | `[UpdateInGroup(TimelineComponentAnimationGroup)]`, `[UpdateAfter(typeof(EntityLinkTargetPatchSystem))]` — sees same-frame TargetPatch retargets |

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
three, so stats freeze at baked values and the clip is a silent runtime `continue` skip.

### FAMILY SUMMARY (condensed from the family-closing report)

All three Essence tracks: empty sealed `DOTSTrack`s bound to `TargetsAuthoring`; clips carry `routeTo` (default `Self`; the
resolver treats `None` like `Self` — unlike `Targets.Get(None)=Null`) and `routeLink`; all run in
`TimelineComponentAnimationGroup` after `EntityLinkTargetPatchSystem`. **The all-silent Essence guard rule (family-critical):
SILENT EVERYWHERE at bake** — Event/Intrinsic bake THROUGH a null schema (Null/0 key, runtime-filtered); Stat silently ABORTS
bake; runtime silent too, except Intrinsic's one loud config-key LogError. Stat alone listens to BOTH edges — the only
guaranteed-temporary Essence track. `routeTo` is mandatory, resolves FIRST; `routeLink` is LIVE on Event/Intrinsic, **DEAD on
Stat** (`TryResolveTarget`, never `TryResolveLinkedTarget`; confirmed three times). Triage: a clean console proves NOTHING —
verify YAML and schema fields directly.

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

## 3. DISCOVERY RECIPES

Act only through `unity-cli exec` / `unity-cli console`; never the filesystem; never play mode. Follow the unity-cli Safe Loop
on every mutation. Names below are parameters — discover them in THIS project; never assume the worked example (§5).

**3.1 Confirm the package exists (else report a missing prerequisite — protocol §6):**
```csharp
var t = System.Type.GetType("BovineLabs.Timeline.Essence.Authoring.TimelineEssenceStatTrack, BovineLabs.Timeline.Essence.Authoring");
if (t == null) foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
    { t = asm.GetType("BovineLabs.Timeline.Essence.Authoring.TimelineEssenceStatTrack"); if (t != null) break; }
return t == null ? "MISSING_PREREQUISITE|TimelineEssenceStatTrack not found - package BovineLabs.Timeline.Essence is absent" : "OK|" + t.AssemblyQualifiedName + "|dataPath=" + UnityEngine.Application.dataPath;
```

**3.2 Find the active scene + SubScene(s):** run the unity-cli First Command; record `parentScenePath` + candidate
`subScenePath`(s).

**3.3 Find PlayableDirector(s) inside the SubScene** (read-only additive open, restore parent after):
`FindObjectsByType<PlayableDirector>(Include, None)`; print per director: hierarchy path, scene.path, playableAsset
path-or-null, other components (DOTS timelines need a timeline-reference authoring component on the director). STATE your
selection rule in the memory card; zero directors → protocol §6.

**3.4 Find/confirm the bind target + receiver prerequisites + stat schema.** The track binds the **`TargetsAuthoring`
COMPONENT** (not the Transform) of a SubScene-baked object. Find candidates by component
(`FindObjectsByType<...TargetsAuthoring>`); confirm with the designer when ambiguous. Verify the stat RECEIVER
(routeTo-resolved) carries `StatAuthoring` with `AddStats=True` + `StatsCanBeModified=True`, and that any non-Self `routeTo`
slot is assigned. Discover stat schemas and keys LIVE — **keys drift between projects; NEVER create schema assets, reuse the
project's**:
```csharp
var sb = new System.Text.StringBuilder();
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:StatSchemaObject")) {
    var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var so = new UnityEditor.SerializedObject(UnityEditor.AssetDatabase.LoadMainAssetAtPath(p));
    sb.AppendLine("STAT_SCHEMA|" + p + "|key=" + so.FindProperty("key").FindPropertyRelative("Value").intValue);
}
return sb.ToString();   // pick WITH the designer; prefer a stat with no gameplay consumers
```
Guid-sweep the chosen stat for consumers first — a gameplay-wired stat is the wrong target unless the designer says so.

**3.5 Capture the chosen director's existing state — this is pre-state (`PRE|`)**:
```csharp
// PRE|playableAsset=<asset PATH or null>   via AssetDatabase.GetAssetPath(director.playableAsset)
// PRE|binding|<i>|<track name>|<track type>|<bound object hierarchy path + component type, or null>
//   one line per GetOutputTracks() of the CURRENT asset, via director.GetGenericBinding(track).
// Capture the asset PATH and each track's NAME/index even when the table looks empty — they are what
// makes the undo journal replayable (UNDO-1 reloads the old asset by path, re-binds by name/index).
```
Record these in the undo journal (§6) before any mutation.

**Name resolution rule**: `GameObject.Find` misses inactive objects and is ambiguous on duplicates — confirm the chosen name
is active and unique in the SubScene, else walk the SubScene roots to the recorded hierarchy path (or `FindObjectsByType`
filtered by `scene`).

## 4. CANONICAL RECIPES

One logical change per exec block; print `PRE|` captures before mutating (protocol §2), save in-block, verify fresh (§7).

**4.1 Create timeline + stat track + clips, then wire the director:**

```csharp
// ---- parameters (discovered in §3 / chosen with designer) ----
var parentScenePath = "<DISCOVERED>"; var subScenePath = "<DISCOVERED>";              // §3.2
var directorGoName  = "<DISCOVERED>"; var bindTargetPath = "<DISCOVERED>";            // §3.3 / §3.4 (carries TargetsAuthoring)
var statSchemaPath  = "<DISCOVERED>"; var assetFolder = "<CHOSEN>"; var assetPath = assetFolder + "/<Name>.playable"; // schema §3.4, NEVER created

var parentScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
var subScene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(subScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
UnityEditor.SceneManagement.EditorSceneManager.SetActiveScene(subScene);
try {
    // CAPTURE (print + journal): PRE|folderExisted=<bool> PRE|assetExisted=<bool>
    var folderExisted = UnityEditor.AssetDatabase.IsValidFolder(assetFolder); // CreateFolder missing segments if needed
    var timeline = UnityEngine.ScriptableObject.CreateInstance<UnityEngine.Timeline.TimelineAsset>();
    UnityEditor.AssetDatabase.CreateAsset(timeline, assetPath);
    var track = timeline.CreateTrack(/* §3.1 track type */, null, "<trackName>");
    var statSchema = UnityEditor.AssetDatabase.LoadMainAssetAtPath(statSchemaPath);

    // FLAT buff ("+N during the clip"): Added — N must be WHOLE (int-truncated; float consumers: ×100, "0.25" = 25).
    // PERCENT ("+50%"): Increased, value=0.5 (stacks additively). MULTIPLICATIVE ("×0.75"): Less, value=0.25 (compounds).
    // Author positive values; Subtracted/Reduced/Less negate at bake.
    var clip = track.CreateClip(/* TimelineEssenceStatClip type */);
    clip.start = 0; clip.duration = 3; clip.displayName = "<clipName>"; // duration IS the effect window
    var so = new UnityEditor.SerializedObject((UnityEngine.Object)clip.asset);
    so.FindProperty("stat").objectReferenceValue = statSchema; so.FindProperty("routeTo").intValue = 4; // Self - or a DISCOVERED-as-assigned slot
    so.FindProperty("modifyType").intValue = 0; so.FindProperty("value").floatValue = 2f; // Added (§2 table); <CHOSEN>
    so.ApplyModifiedPropertiesWithoutUndo();
    UnityEditor.AssetDatabase.SaveAssets();

    // Wire the director (binding table lives in the SCENE file)
    var director = UnityEngine.GameObject.Find(directorGoName).GetComponent<UnityEngine.Playables.PlayableDirector>();
    // CAPTURE (print + journal) BEFORE mutating: PRE|playableAsset=<asset path or null>
    //   and PRE|binding|<each output track of the CURRENT asset>|<GetGenericBinding value>
    var bindComp = UnityEngine.GameObject.Find(bindTargetPath).GetComponent<BovineLabs.Reaction.Authoring.Core.TargetsAuthoring>();   // the COMPONENT, not Transform
    director.playableAsset = timeline;
    director.SetGenericBinding(track, bindComp);
    UnityEditor.EditorUtility.SetDirty(director); UnityEditor.SceneManagement.EditorSceneManager.SaveScene(subScene);
    return "OK|" + assetPath;
} finally {
    UnityEditor.SceneManagement.EditorSceneManager.SetActiveScene(parentScene); UnityEditor.SceneManagement.EditorSceneManager.CloseScene(subScene, false);
    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(parentScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
}
```

Values are example choices, not package constants; overlaps stack (never blend); loops/scrubs re-fire cleanly. Verify per §7
in SEPARATE blocks.

## 5. WORKED EXAMPLE (vex-ee training stage) — example environment; rediscover, never assume

- Project: `/home/i/GitHub/vex-ee` (`dataPath=/home/i/GitHub/vex-ee/Assets`); parent scene `Assets/Scenes/Main Scene.unity`;
  SubScene `Assets/Scenes/Main Sub Scene.unity`. Stage: `Stage_Director` (PlayableDirector + TimelineReferenceAuthoring, the
  only director); receiver `Stage_Actor` (TargetsAuthoring + StatAuthoring: `AddStats=True`, `StatsCanBeModified=True`,
  `StatDefaults[0] = {SlowMo, Added, 0.25}` from lesson 04).
- Schemas: **114** `StatSchemaObject` assets under `Assets/Settings/Schemas/Stats/`; demo stat `SlowMo.asset`, **key 94**.
  `EntityLinkSchema` `Schema_Actor` guid `3b375c42affc2917f956d01310d31894`, id=10.
- Asset built in training (lesson 13): `Assets/Training/13-timeline-essence-stat-track/StatMastery.playable` — one track
  `StatTrack`, clips A_Plus2Flat (0–3s, Added, value=2), B_Increased50pct (1–4s, Increased, value=0.5, overlapping A),
  C_Less25pct (5–6s, Less, value=0.25, routeLink=Schema_Actor kept as living documentation of the dead key); all
  `stat=SlowMo`, `routeTo=Self`. A/B carry auto-generated blend YAML (cosmetic, harmless).
- Wiring: binding `StatTrack → Stage_Actor (TargetsAuthoring)`; the director's scene-binding table grew 10 → **11** (prior 10
  intact — tables are keyed by track asset and survive playableAsset swaps); director restored to
  `Assets/Training/01-transform-position-track/PositionMastery.playable`.
- Formula walkthrough on this stage (base `{SlowMo, Added, 0.25}` contributes `(int)0.25` = **0**): 0–1s A only → **2.0**;
  1–3s A+B → `(0+2)×(1+0.5)×1` = **3.0 — NOT 3.375** (the naive "(0.25+2)×1.5" is wrong: the default truncates to 0 at bake);
  3–4s B only → **0**; 5–6s C only → **0** (zero-base trap). Family record correction: the lesson-04 "vaccine" `{SlowMo,
  Added, 0.25}` puts key 94 in the buffer but computes `0 × 1 / 100 = 0` — frozen timeline by another road; **"0.25 speed"
  must be authored as Value = 25**.
- Known pre-existing console baseline: UnityCliConnector HTTP server start, PerformanceTesting
  IPrebuildSetup/IPostBuildCleanup, TestResults.xml save, lessons 08–10 `[Worker2]` EntityLinks bake errors.

## 6. UNDO APPENDIX

Runtime note: the effect is **while-active and self-reverting** (SourceEntity-matched remove; scrub/stop safe) — no lingering
runtime stat state to compensate; the workflow never enters play mode. Undo is purely the authoring artifacts:

Artifact inventory for one run of §4 (vex-ee instance shown in §5):
1. Created asset `<assetPath>` (.playable; `DeleteAsset` removes the track/clip sub-assets with the file).
2. Possibly-created folder(s) `<assetFolder>` (only if `PRE|folderExisted=false`).
3. Mutated `director.playableAsset` (vex-ee lesson 13: captured pre value `PositionMastery.playable`, printed in the report's
   environment block).
4. Added generic binding entry for the new track in the SubScene file (vex-ee: table 10 → 11; prior 10 entries' names verified
   intact; `EXPECTED:` their bound-object values were not printed pre-wiring — capture the full table per §3.5).
5. No other scene values changed; no schema asset was created or modified (schema reuse is mandatory).

ORDER: restore the director FIRST (so nothing references the asset), THEN delete the asset, THEN restore other captured values
— deleting first leaves a dangling `{fileID: 0}` reference and destroys the track objects `ClearGenericBinding` needs.

Journal entry templates (protocol §5 — fill from YOUR captures, reverse order):

```csharp
// UNDO-1: restore director's captured playableAsset + binding table (SubScene bracket)
var parentScenePath = "<CAPTURED>"; var subScenePath = "<CAPTURED>"; var directorGoName = "<CAPTURED>"; var assetPath = "<CAPTURED>";
var parentScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
var subScene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(subScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
UnityEditor.SceneManagement.EditorSceneManager.SetActiveScene(subScene);
try {
    var director = UnityEngine.GameObject.Find(directorGoName).GetComponent<UnityEngine.Playables.PlayableDirector>();
    var myAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Timeline.TimelineAsset>(assetPath);
    foreach (var tr in myAsset.GetOutputTracks()) director.ClearGenericBinding(tr);   // entries I added for MY tracks
    // restore each CAPTURED binding (PRE|binding| lines): reload the PREVIOUS playable asset by captured path, match
    // tracks by name/index, re-find bound objects by captured hierarchy path, SetGenericBinding(prevTrack, boundComponent).
    director.playableAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Playables.PlayableAsset>("<CAPTURED pre path>"); // CAPTURED value (or null if captured null), never "default"
    UnityEditor.EditorUtility.SetDirty(director); UnityEditor.SceneManagement.EditorSceneManager.SaveScene(subScene);
    return "UNDONE|director restored";
} finally {
    UnityEditor.SceneManagement.EditorSceneManager.SetActiveScene(parentScene); UnityEditor.SceneManagement.EditorSceneManager.CloseScene(subScene, false);
    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(parentScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
}
```

```csharp
// UNDO-2: delete the created .playable (+ folder, only if PRE|folderExisted=false and now empty)
var assetPath = "<CAPTURED>"; var assetFolder = "<CAPTURED>"; var folderExisted = false; // <CAPTURED>
var ok = UnityEditor.AssetDatabase.DeleteAsset(assetPath);
if (!folderExisted && UnityEditor.AssetDatabase.FindAssets("", new[]{ assetFolder }).Length == 0)
    UnityEditor.AssetDatabase.DeleteAsset(assetFolder);
return "UNDONE|deleted=" + ok + "|" + assetPath;
```

```csharp
// UNDO-3: restore any other captured scene values — normally none beyond UNDO-1 (the track never mutates
// editor objects, schemas, or StatDefaults); include only entries your own journal recorded.
```

UNDO-4 (verify, fresh load — protocol §7): reload the SubScene additively; `director.playableAsset` and the binding table must
equal the CAPTURED `PRE|` values; `AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath)` must be null; restore the
parent scene; console clean against the project baseline.

## 7. VERIFICATION PROTOCOL

1. **Fresh-load asset dump**: new exec block; `LoadAssetAtPath` the `.playable`, dump every track/clip (name, start/duration,
   `stat` + key, `routeTo`, `modifyType`, `value`). In-memory state after a save is not evidence.
2. **Raw YAML check**: `modifyType` byte per §2 table; `value` POSITIVE even for Subtracted/Reduced/Less (negation is
   bake-only); `stat` guid present (no `{fileID: 0}`); overlap blend YAML cosmetic — expected, harmless.
3. **Stat-side checks**: re-dump the chosen schema's key live (§3.4 — ids and inventories drift); receiver
   `StatsCanBeModified=True`.
4. **Binding check from a RELOADED SubScene**: expect `BINDING|<trackName>|bound=<bindTarget> (TargetsAuthoring)` — the
   component, not the Transform; prior entries intact.
5. **Parent-scene restore**: end with `sceneCount=1`, `scene[0]=<parentScenePath>|loaded=True|active=True|dirty=False`.
6. **Console**: `unity-cli console --filter error` shows nothing new beyond the project's known baseline (vex-ee baseline in
   §5). This track is bake-silent even when misconfigured — silence is expected, not evidence.
