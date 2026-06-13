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
dead key — only Event and Intrinsic route through links. Behave per unity-agent-protocol; operate the editor per unity-cli.

## 2. PORTABLE SEMANTICS

True in ANY project containing `BovineLabs.Timeline.Essence` (plus its Essence and Reaction dependencies). Provenance tags =
where PROVEN, not where it applies. (All verified vex-ee 2026-06 via reflection dumps, raw YAML, package-source reads,
fresh-load read-backs, a real forced bake for the silent-null demo — all `unity-cli exec`, no play mode; runtime claims
source-derived.)

### THE HEADLINE — permanent, clamped, self-healing

Everything this track writes **persists forever** — the opposite of the Event track's same-frame evaporation. No deactivation
job, no revert, no stored "before": the only undo is a compensating clip with the opposite amount (the clamp may eat part of
it). The writer **self-heals** missing entity entries (auto-add at schema default), clamps every delta into
static-range-unless-stat-overridden bounds, and carries the family's **only LOUD failure**: an unknown runtime-config key
LogErrors — at RUNTIME, never at bake.

| Type | Base | Facts |
|---|---|---|
| `TimelineEssenceIntrinsicTrack` | `DOTSTrack` | sealed, EMPTY body. `[TrackClipType(TimelineEssenceIntrinsicClip)]`, `[TrackBindingType(BovineLabs.Reaction.Authoring.Core.TargetsAuthoring)]`, `[TrackColor(0.2,0.6,0.9)]`, `[DisplayName("BovineLabs/Essence/Timeline Intrinsic")]` |
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
§3.4). Also: internal `RestrictMin`/`RestrictMax` re-clamp when a LIMIT stat changes (`EssenceConfig.StatsLimitIntrinsics`
reverse map); they skip `!IsCreated` entities — auto-add applies only to Add/Set/Subtract.

### Runtime semantics (one paragraph, source-derived)

`TimelineEssenceIntrinsicSystem` mirrors the Event pipeline with a persistent destination: on each clip's ACTIVATION edge only
(`[WithAll(ClipActive)] [WithDisabled(ClipActivePrevious)]` — duration/end/deactivation meaningless), `GatherJob` skips
silently (`if (data.Intrinsic.Value == 0 || binding.Value == Entity.Null) return;`), otherwise resolves the receiver through
the Essence resolver (routeTo first and mandatory; routeLink wins when its hunt resolves, falls back to routeTo otherwise) and
accumulates `(target, key, amount)`; `ApplyJob` coalesces same-frame same-key amounts per receiver in a `FixedList4096Bytes`
(overflow entries apply immediately without joining the sum; entities without an `Intrinsic` buffer are silently skipped by
`Writers.TryGet`) and issues ONE `IntrinsicWriter.Add(key, summedAmount)` per (receiver, key).

### Traps & DO/DON'T (each proven live or source-quoted, vex-ee 2026-06)

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

## 3. DISCOVERY RECIPES

Act only through `unity-cli exec` / `unity-cli console`; never the filesystem; never play mode. Follow the unity-cli Safe Loop
on every mutation. Names below are parameters — discover them in THIS project; never assume the worked example (§5).

**3.1 Confirm the package exists (else report a missing prerequisite — protocol §6):**
```csharp
var t = System.Type.GetType("BovineLabs.Timeline.Essence.Authoring.TimelineEssenceIntrinsicTrack, BovineLabs.Timeline.Essence.Authoring");
if (t == null) foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
    { t = asm.GetType("BovineLabs.Timeline.Essence.Authoring.TimelineEssenceIntrinsicTrack"); if (t != null) break; }
return t == null ? "MISSING_PREREQUISITE|TimelineEssenceIntrinsicTrack not found - package BovineLabs.Timeline.Essence is absent" : "OK|" + t.AssemblyQualifiedName + "|dataPath=" + UnityEngine.Application.dataPath;
```

**3.2 Find the active scene + SubScene(s):** run the unity-cli First Command; record `parentScenePath` + candidate
`subScenePath`(s).

**3.3 Find PlayableDirector(s) inside the SubScene** (read-only additive open, restore parent after):
`FindObjectsByType<PlayableDirector>(Include, None)`; print hierarchy path, scene.path, playableAsset path-or-null, other
components (DOTS timelines need a timeline-reference authoring component on the director); state your selection rule; zero
directors → protocol §6.

**3.4 Find/confirm the bind target + receiver prerequisites + intrinsic schema.** The track binds the **`TargetsAuthoring`
COMPONENT** of a SubScene-baked object; the receiver needs `StatAuthoring` with `AddIntrinsics=True` — **empty
`IntrinsicDefaults` is FINE** (auto-add heals, verified). Discover intrinsic schemas LIVE — **keys/defaults/ranges/stat-links
drift between projects; NEVER create schema assets**:
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
Confirm the chosen schema is REGISTERED in `EssenceSettings.intrinsicSchemas` — unregistered = the loud runtime config error.
For `routeLink`, discover `EntityLinkSchema` assets/ids likewise; confirm the routeTo entity reaches a link root carrying the
key. Know the schema's floor before consume clips (the clamp absorbs, §2).

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

**4.1 Create timeline + intrinsic track + clips, then wire the director:**

```csharp
// ---- parameters (discovered in §3 / chosen with designer) ----
var parentScenePath = "<DISCOVERED>"; var subScenePath = "<DISCOVERED>";
var directorGoName  = "<DISCOVERED>"; var bindTargetPath = "<DISCOVERED>"; // carries TargetsAuthoring
var schemaPath      = "<DISCOVERED>"; var assetFolder = "<CHOSEN>"; var assetPath = assetFolder + "/<Name>.playable"; // schema §3.4, registered in EssenceSettings, NEVER created

var parentScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
var subScene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(subScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
UnityEditor.SceneManagement.EditorSceneManager.SetActiveScene(subScene);
try {
    // CAPTURE (print + journal): PRE|folderExisted=<bool> PRE|assetExisted=<bool>
    var folderExisted = UnityEditor.AssetDatabase.IsValidFolder(assetFolder);
    var timeline = UnityEngine.ScriptableObject.CreateInstance<UnityEngine.Timeline.TimelineAsset>();
    UnityEditor.AssetDatabase.CreateAsset(timeline, assetPath);
    var track = timeline.CreateTrack(/* §3.1 track type */, null, "<trackName>");
    var schema = UnityEditor.AssetDatabase.LoadMainAssetAtPath(schemaPath);

    // GRANT ("+N at this beat"): amount=+N — only the clip's START matters.
    // CONSUME ("spend N"): amount=-N — at the floor the consume is silently absorbed (no debt, no failure
    //   signal); gate via a Reaction condition if "can't afford" matters; order consume clips AFTER their grants.
    // COALESCE ("N pickups at once"): N clips, same schema/target/start (same-track overlap is API-legal) -> ONE summed Add.
    // ROUTE ("give the LINKED actor the points"): routeTo = a resolving Targets slot (unset = silent total
    //   loss); routeLink = <discovered EntityLinkSchema>; a failed hunt falls back to routeTo, which still receives.
    var clip = track.CreateClip(/* TimelineEssenceIntrinsicClip type */);
    clip.start = 1; clip.duration = 0.5; clip.displayName = "<clipName>"; // duration irrelevant
    var so = new UnityEditor.SerializedObject((UnityEngine.Object)clip.asset);
    so.FindProperty("intrinsic").objectReferenceValue = schema; so.FindProperty("routeTo").intValue = 4; // Self
    so.FindProperty("amount").intValue = 5;            // <CHOSEN>, signed
    so.ApplyModifiedPropertiesWithoutUndo();
    UnityEditor.AssetDatabase.SaveAssets();

    var director = UnityEngine.GameObject.Find(directorGoName).GetComponent<UnityEngine.Playables.PlayableDirector>();
    // CAPTURE (print + journal) BEFORE mutating: PRE|playableAsset=<asset path or null>
    //   and PRE|binding|<each output track of the CURRENT asset>|<GetGenericBinding value>
    var bindComp = UnityEngine.GameObject.Find(bindTargetPath).GetComponent<BovineLabs.Reaction.Authoring.Core.TargetsAuthoring>();  // the COMPONENT
    director.playableAsset = timeline;
    director.SetGenericBinding(track, bindComp);
    UnityEditor.EditorUtility.SetDirty(director); UnityEditor.SceneManagement.EditorSceneManager.SaveScene(subScene);
    return "OK|" + assetPath;
} finally {
    UnityEditor.SceneManagement.EditorSceneManager.SetActiveScene(parentScene); UnityEditor.SceneManagement.EditorSceneManager.CloseScene(subScene, false);
    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(parentScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
}
```

## 5. WORKED EXAMPLE (vex-ee training stage) — example environment; rediscover, never assume

- Project: `/home/i/GitHub/vex-ee` (`dataPath=/home/i/GitHub/vex-ee/Assets`). Parent scene `Assets/Scenes/Main Scene.unity`;
  SubScene `Assets/Scenes/Main Sub Scene.unity`. `Stage_Director` (PlayableDirector + TimelineReferenceAuthoring, the only
  director); receiver `Stage_Actor` (TargetsAuthoring + StatAuthoring: `AddIntrinsics=True`, `IntrinsicDefaults.size=0`,
  `IntrinsicDefaultGroups.size=0` — fine, auto-add heals; contrast: the stat track needed a StatDefaults append).
- Schemas: **78** `IntrinsicSchemaObject`s under `Assets/Settings/Schemas/Intrinsics/`, all 78 registered in
  `Assets/Settings/Settings/EssenceSettings.asset` (intrinsics=78, stats=114); **39 of 78 set `maxStat`** (none `minStat`) —
  e.g. `BlazeStacks` (key 10, range [0,5], maxStat → `MaxBlazeStacks.asset`, StatKey 73). Demo schema `BaseTime.asset`: key
  45, default 0, range [0,999999], no stat links, guid `c4e9e530a0074407a46098adb9ebcbee`. **The intrinsic→event bridge is
  dormant in vex-ee**: ZERO of the 78 schemas carry the ConditionEventObject sub-asset — `Event` bakes 0 everywhere; the code
  path is real, the content doesn't use it.
- Asset built in training (lesson 12): `Assets/Training/12-timeline-essence-intrinsic-track/IntrinsicMastery.playable` — one
  track `IntrinsicTrack`, clips A_Add5 (1–1.5s, +5), B_Subtract2 (3–3.5s, −2, `amount: -2` serialized verbatim), C_Add3 +
  D_Add4 (both 5–5.5s, deliberately overlapping on one track → coalesce to one `Add(45, +7)`); all `intrinsic=BaseTime`,
  `routeTo=Self`, `routeLink=null`.
- Clamp-order math on BaseTime (default 0, floor 0): A then B → 5 then 3; B alone/first → clamp(0−2)=0, effective delta **0**,
  no event; A after → 5, not 3.
- Wiring: `IntrinsicTrack → Stage_Actor (TargetsAuthoring)`; binding table grew 9 → **10** (prior 9 preserved); director
  restored to `Assets/Training/01-transform-position-track/PositionMastery.playable`.
- Known pre-existing console baseline: UnityCliConnector HTTP server start, PerformanceTesting
  IPrebuildSetup/IPostBuildCleanup, TestResults.xml save, lessons 08–10 `[Worker2]` EntityLinks bake errors.

## 6. UNDO APPENDIX

**The honest permanence statement first.** The RUNTIME effect is a permanent counter mutation with no revert path in the
package (evidence of absence, §2): deleting the .playable / restoring the director does NOT revert counters in any world that
already played, looped, or scrubbed the timeline. No clean automatic undo of fired deltas exists:
- The only compensation is a **compensating clip/write with the opposite amount**, and the clamp can absorb it asymmetrically
  — a clamped original's EFFECTIVE delta is only knowable from runtime observation. `EXPECTED:` without observing the live
  counter before/after, exact compensation cannot be guaranteed — say so rather than claiming restoration.
- The verified authoring workflow never enters play mode, so an authoring session fires nothing: undoing the artifacts below
  IS a complete undo. State which case applies.

Artifact inventory for one run of §4 (vex-ee instance shown in §5):
1. Created asset `<assetPath>` (.playable; `DeleteAsset` removes the track/clip sub-assets with the file).
2. Possibly-created folder(s) `<assetFolder>` (only if `PRE|folderExisted=false`).
3. Mutated `director.playableAsset` (vex-ee lesson 12: captured pre value `PositionMastery.playable`, 9 bindings pre-lesson —
   printed in the report).
4. Added generic binding entry for the new track in the SubScene file (vex-ee: table 9 → 10; `EXPECTED:` the prior 9 entries'
   bound-object values were not printed pre-wiring — capture the full table per §3.5).
5. No scene values, schemas, EssenceSettings, or StatAuthoring fields changed (no stage mutation needed — auto-add heals).

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
// UNDO-3: restore any other captured scene values — normally none beyond UNDO-1; include only entries your
// own journal recorded. If the timeline FIRED in a live world (play mode, loops, scrubs), record under Gaps
// that counters were permanently mutated; only a compensating write can approximate restoration
// (clamp absorption may make it inexact - see the honest permanence statement above).
```

UNDO-4 (verify, fresh load — protocol §7): reload the SubScene additively; `director.playableAsset` and the binding table must
equal the CAPTURED `PRE|` values; `AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath)` must be null; restore the
parent scene; console clean against the project baseline.

## 7. VERIFICATION PROTOCOL

1. **Fresh-load asset dump**: new exec block; `LoadAssetAtPath` the `.playable`, dump every track/clip (name, start/duration,
   `intrinsic` + key, `routeTo`, `routeLink`, `amount` — negatives must read back verbatim). In-memory state after a save is
   not evidence.
2. **Raw YAML check**: `routeTo` as byte; `routeLink: {fileID: 0}` when unset; `intrinsic` guid present (no `{fileID: 0}`
   where a ref was intended); negative `amount` serialized verbatim; same-start overlapping clips survive reload.
3. **Schema checks**: re-dump the chosen schema's key/default/range/stat-links live (§3.4 — ids and inventories drift);
   confirm registration in `EssenceSettings.intrinsicSchemas`.
4. **Binding check from a RELOADED SubScene**: expect `BINDING|<trackName>|bound=<bindTarget> (TargetsAuthoring)` — the
   component, not the Transform; prior entries intact.
5. **Parent-scene restore**: end with `sceneCount=1`, `scene[0]=<parentScenePath>|loaded=True|active=True|dirty=False`.
6. **Console**: nothing new beyond the project baseline (§5). Bake-silent even when misconfigured — silence is expected, not
   evidence; the only loud path is the runtime config-key LogError.
