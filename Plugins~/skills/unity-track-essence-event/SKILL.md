---
name: unity-track-essence-event
description: Master of TimelineEssenceEventTrack + TimelineEssenceEventClip (package BovineLabs.Timeline.Essence) — firing transient ConditionEvents at entities from the timeline (the cutscene→reaction bridge), routeTo/routeLink resolution, the all-silent Essence guard rule; carries the ESSENCE FAMILY REFERENCE. Portable to any project containing the package; worked example from vex-ee. Use when a designer asks "at this moment, fire the OnX event at this thing".
---

# TimelineEssenceEventTrack specialist

## 1. SCOPE

You are the specialist for **`TimelineEssenceEventTrack`** and **`TimelineEssenceEventClip`** from the package
`BovineLabs.Timeline.Essence`, namespace `BovineLabs.Timeline.Essence.Authoring`. Scope: exactly this track — one clip = one
edge-triggered write of a transient `ConditionEvent` (key + amount) into a resolved entity's event buffer, so Reaction
conditions keyed by that event can respond. As the FIRST Essence track this skill carries the **ESSENCE FAMILY REFERENCE** the
Stat and Intrinsic skills cross-reference: resolver semantics, all-silent guard matrix, dead-`RouteLinkKey`-on-Stat. Triad:
**events = transient signals (fire, react, evaporate — THIS)**, intrinsics = permanent integer counters, stats = while-active
float modifiers. Stage construction belongs to `unity-stage-foundations`. Behave per unity-agent-protocol; operate the editor
per unity-cli.

## 2. PORTABLE SEMANTICS

True in ANY project containing `BovineLabs.Timeline.Essence` (plus its Essence and Reaction dependencies). Provenance tags =
where PROVEN, not where it applies. (All verified vex-ee 2026-06 via reflection dumps, package-source quotes, raw YAML,
fresh-load read-backs, a real forced bake for the silent-null demo — all `unity-cli exec`, no play mode; runtime claims
source-derived.)

### THE HEADLINE — transient, silent, and summed

Everything this track writes **evaporates the same frame it is consumed** — the opposite of the EntityLinks family's
persistence; nothing to undo, ever. Every failure mode is **silent** (null event, null link, dead routeTo): a clip that "does
nothing" with a clean console is ALWAYS a config/resolution problem. Same-frame duplicates are **pre-summed**: ONE `Trigger`
per (receiver, key) — the buffer rejects and dev-logs duplicates.

| Type | Base | Facts |
|---|---|---|
| `TimelineEssenceEventTrack` | `DOTSTrack` | sealed, EMPTY body — no Bake override, no fields. `[TrackClipType(TimelineEssenceEventClip)]`, `[TrackBindingType(BovineLabs.Reaction.Authoring.Core.TargetsAuthoring)]`, `[TrackColor(0.9,0.4,0.2)]`, `[DisplayName("BovineLabs/Essence/Timeline Event")]` |
| `TimelineEssenceEventClip` | `DOTSClip` | sealed, `ClipCaps.None` (no blend/ease), `duration => 1` (seed only) |
| System | `TimelineEssenceEventSystem` | `[UpdateInGroup(TimelineComponentAnimationGroup)]`, `[UpdateAfter(typeof(EntityLinkTargetPatchSystem))]` — events see same-frame TargetPatch retargets |

### Clip fields — camelCase (reflection + fresh-instance defaults)

| Field | Type | Default | Meaning |
|---|---|---|---|
| `routeTo` | `BovineLabs.Reaction.Data.Core.Target` | **`Self` (4)** | Who receives the event (resolved through the binding's `Targets`). Self default — a fresh clip on a bound track already routes somewhere real. |
| `routeLink` | `EntityLinkSchema` | null | OPTIONAL link override: hunt the link map starting at the routeTo-resolved entity; the linked entity wins if found. Null is silent (`TryGetKey` return value IGNORED). |
| `conditionEvent` | `BovineLabs.Reaction.Authoring.Conditions.ConditionEventObject` | null | The event key asset. `.Key` → `ConditionKey` (int). Null is SILENT at bake AND runtime-filtered. |
| `value` | `int` | 1 | The event amount (summed across same-frame duplicates). Never author 0 (dev assert). |

Bake (quoted from `TimelineEssenceEventClip.Bake`, package source):

```csharp
EntityLinkAuthoringUtility.TryGetKey(routeLink, out var linkKey);   // return value IGNORED
var builder = new EssenceEventBuilder
{
    RouteTo = routeTo,
    RouteLinkKey = linkKey,
    Event = conditionEvent ? conditionEvent.Key : ConditionKey.Null,  // SILENT null guard
    Value = value
};
```

→ adds `TimelineEssenceEventData { Target RouteTo; ushort RouteLinkKey; ConditionKey Event; int Value; }` to the clip entity.
BOTH guards silent — EntityLinks clips LogError on the same condition. Events land in the target's `ConditionEvent` buffer
(`[InternalBufferCapacity(0)] struct ConditionEvent : IDynamicHashMap<ConditionKey, int>` — per-entity key→amount map +
`EventsDirty` enableable). Event key assets: `ConditionEventObject : ConditionSchemaObject` — fields `key` (ConditionKey),
`customDataType`; props `IsEvent=True`, `ConditionType="event"`.

### FAMILY REFERENCE — the resolver, quoted

`TimelineEssenceResolver` (`BovineLabs.Timeline.Essence.Data/TimelineEssenceResolver.cs`). `TryResolveTarget(target, binding,
...)`: `if (target is Target.Self or Target.None) { resolved = binding; return true; }` — else `resolved = t.Get(target,
binding); return resolved != Entity.Null;` from the binding's `Targets` (no `Targets` component → false). Then:

```csharp
public static bool TryResolveLinkedTarget(Target targetMode, ushort linkKey, Entity self, ..., out Entity resolved)
{
    resolved = Entity.Null;
    if (!TryResolveTarget(targetMode, self, targetsLookup, out var target))
        return false;
    if (linkKey == 0)
    {
        resolved = target;
        return true;
    }
    if (EntityLinkResolver.TryResolve(target, linkKey, sources, links, out var linked))
    {
        resolved = linked;
        return true;
    }
    resolved = target;
    return true;
}
```

**Precedence verdict**: `routeTo` resolves FIRST and is load-bearing; the link is a conditional override. (1) routeTo fails
(slot `Entity.Null`, or no `Targets` for Owner/Source/Target/Custom) → **total failure, event not fired, regardless of
routeLink**. (2) routeLink null/id-0 → routeTo receives. (3) routeLink set and the hunt RESOLVES → **the linked entity wins**
— the hunt starts AT the routeTo-resolved entity: root-hop via `EntityLinkSource.Root` (or itself), then linear
`EntityLinkEntry{Key,Target}` search. (4) hunt FAILS → **graceful fallback to routeTo, still fires** (`resolved = target;
return true;`) — contrast EntityLinks, where the same failure is a skip. Deviations: (a) **`Target.None` behaves like `Self`**
here (in `Targets.Get`, None → `Entity.Null`); (b) `routeTo` is BOTH the family's `readRootFrom` AND the fallback — one knob,
two jobs.

### FAMILY REFERENCE — the all-silent guard matrix (family-critical)

All three bake guards quoted from live package source (2026-06):

| Clip | Bake guard (quoted) | Loudness | Consequence |
|---|---|---|---|
| Event | `Event = conditionEvent ? conditionEvent.Key : ConditionKey.Null` | **SILENT, bakes through** | component added with Null key; filtered per-frame at runtime |
| Intrinsic | `Intrinsic = intrinsic ? intrinsic.Key : default(IntrinsicKey)` | **SILENT, bakes through** | component added with key 0; runtime filter `if (data.Intrinsic.Value == 0 ...) return;` |
| Stat | `if (stat == null) return;` (first line of Bake — skips the builder AND `base.Bake`) | **SILENT, bake-ABORT** | NO component added; clip entity inert. (Runtime also guards `if (data.Stat.Value == 0 ...) return;`.) |

Family rule, final form: **EntityLinks = loud bake / silent runtime; Essence = silent EVERYWHERE** (not one bake-guard
`Debug.LogError` in the package; grep verified). Bonus finding: `TimelineEssenceStatData.RouteLinkKey` is baked but DEAD at
runtime — only Event and Intrinsic route through links.

### Runtime semantics (`TimelineEssenceEventSystem`, source-quoted)

Edge-triggered one-shot, three phases. **GatherJob** (parallel, `[WithAll(ClipActive)][WithDisabled(ClipActivePrevious)]`)
fires only on the activation frame; returns early `if (data.Event == ConditionKey.Null || binding.Value == Entity.Null)`, else
resolves via `TryResolveLinkedTarget` and accumulates `(target, EventAmount(Event, Value))` in a multi-hash-map + `UniqueKeys`
set. **GetKeysJob** dedupes targets. **ApplyJob** (per receiver) sums same-key amounts in a `FixedList4096Bytes<EventAmount>`
(`existing.Amount += value.Amount;`) then `writer.Trigger(e.Event, e.Amount)` — ONE Trigger per (target, key); past ~512
distinct keys per target per frame, overflow keys Trigger immediately without joining the sum.

`ConditionEventWriter.Trigger` (Reaction package): returns on `ConditionKey.Null` (the runtime null-event filter), asserts
`Check.Assume(value != 0, "Can't write 0 value event")`, then `conditionEvents.AsMap().TryAdd(key, value)` — under collections
checks a false return logs `Trying to write an event {key.Value} multiple times in a frame.` The pre-sum exists to avoid this.

**The clearing path — events are TRANSIENT.** The CONSUMER clears: `ConditionEventWriteSystem` matches each entry against the
entity's `EventSubscriber` map, sets matched `ConditionActive` bits, disables `EventsDirty`, runs `conditionEvents.Clear();`;
downstream `ConditionEventResetSystem` masks the bits back off — both one-frame transients.

**The timeline → event → reaction bridge.** A `ReactionAuthoring` on a gameplay entity with an event-type condition keyed by a
`ConditionEventObject` bakes an `EventSubscriber` entry keyed `(ConditionKey, conditionType=event)`. The clip's activation
edge resolves the receiver, sums same-frame amounts, `Trigger`s the key into its buffer; `ConditionEventWriteSystem` matches
subscribers, runs the comparison (`ReactionUtil.EqualityCheck` vs `ConditionComparisonValue`, optional value storage via
`ConditionValues`), and sets the subscriber's `ConditionActive` bit — flipping the Reaction active. The event clears the same
frame, the bit is masked off — a cutscene says "NOW the grab completed", gameplay reacts exactly once: no cleanup clip, no
lingering state, no asset→scene reference in the chain (only asset→asset refs).

### Traps & DO/DON'T (each proven live or source-quoted, vex-ee 2026-06)

- **DON'T trust a clean console — every guard is silent; verify YAML** — a real forced bake of a null-`conditionEvent` temp
  clip produced ZERO LogErrors in the worker log; only fresh-load/raw-YAML proves config.
- **DO expect re-fires per activation edge; events auto-clear** — clip length irrelevant, clip end does nothing, loops and
  scrubs re-fire; nothing to compensate.
- **DO rely on pre-summing — a duplicate same-frame Trigger would error.**
- **DON'T author value=0 or let same-frame values sum to 0 (e.g. +1 and -1)** — `Check.Assume(value != 0)` trips in dev
  builds.
- **DON'T expect routeLink to rescue a dead routeTo — the event is lost** — routeTo resolves before the link hunt; an
  unassigned slot silently drops the event regardless of the link. (A FAILED link still fires at routeTo — make routeTo the
  acceptable fallback.)
- **DO know same-track overlap is accepted via API** — `CreateClip` accepts same-start clips on ONE track; they survive save +
  reload; the DOTS bake treats clips independently. (The Timeline EDITOR would resist this by hand on a ClipCaps.None track.)
- **DON'T create schema/event assets, ever** — reuse the project's `ConditionEventObject` inventory (discover per §3.4).

## 3. DISCOVERY RECIPES

Act only through `unity-cli exec` / `unity-cli console`; never the filesystem; never play mode. Follow the unity-cli Safe Loop
on every mutation. Names below are parameters — discover them in THIS project; never assume the worked example (§5).

**3.1 Confirm the package exists (else report a missing prerequisite — protocol §6):**
```csharp
var t = System.Type.GetType("BovineLabs.Timeline.Essence.Authoring.TimelineEssenceEventTrack, BovineLabs.Timeline.Essence.Authoring");
if (t == null) foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
    { t = asm.GetType("BovineLabs.Timeline.Essence.Authoring.TimelineEssenceEventTrack"); if (t != null) break; }
return t == null ? "MISSING_PREREQUISITE|TimelineEssenceEventTrack not found - package BovineLabs.Timeline.Essence is absent" : "OK|" + t.AssemblyQualifiedName + "|dataPath=" + UnityEngine.Application.dataPath;
```

**3.2 Find the active scene + SubScene(s):** run the unity-cli First Command; record `parentScenePath` + candidate
`subScenePath`(s).

**3.3 Find PlayableDirector(s) inside the SubScene** (read-only additive open, restore parent after):
`FindObjectsByType<PlayableDirector>(Include, None)`; print hierarchy path, scene.path, playableAsset path-or-null, other
components (DOTS timelines need a timeline-reference authoring component on the director); state your selection rule; zero
directors → protocol §6.

**3.4 Find/confirm the bind target + the event key asset (+ link schema).** The track binds the **`TargetsAuthoring`
COMPONENT** of a SubScene-baked object. For non-Self `routeTo`, verify the slot is assigned in the binding's
`TargetsAuthoring` (unassigned = silent total loss). Discover event key assets LIVE — **keys drift between projects; NEVER
create schema/event assets**:
```csharp
var sb = new System.Text.StringBuilder();
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:ConditionEventObject")) {
    var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var so = new UnityEditor.SerializedObject(UnityEditor.AssetDatabase.LoadMainAssetAtPath(p));
    sb.AppendLine("EVENT|" + p + "|key=" + so.FindProperty("key").FindPropertyRelative("Value").intValue);
}
return sb.ToString();   // pick WITH the designer; guid-sweep consumers before reusing
```
If routing via `routeLink`: discover `EntityLinkSchema` assets the same way and confirm the routeTo-resolved entity reaches a
link root whose `EntityLinkEntry` buffer carries the key — else the link silently falls back to routeTo (path 4). For the
listening side, find entities whose `ReactionAuthoring` subscribes to the chosen event.

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

**4.1 Create timeline + event track + clips, then wire the director:**

```csharp
// ---- parameters (discovered in §3 / chosen with designer) ----
var parentScenePath = "<DISCOVERED>"; var subScenePath = "<DISCOVERED>";
var directorGoName  = "<DISCOVERED>"; var bindTargetPath = "<DISCOVERED>"; // carries TargetsAuthoring
var eventAssetPath  = "<DISCOVERED>"; var assetFolder = "<CHOSEN>"; var assetPath = assetFolder + "/<Name>.playable"; // event asset §3.4, NEVER created

var parentScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
var subScene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(subScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
UnityEditor.SceneManagement.EditorSceneManager.SetActiveScene(subScene);
try {
    // CAPTURE (print + journal): PRE|folderExisted=<bool> PRE|assetExisted=<bool>
    var folderExisted = UnityEditor.AssetDatabase.IsValidFolder(assetFolder);
    var timeline = UnityEngine.ScriptableObject.CreateInstance<UnityEngine.Timeline.TimelineAsset>();
    UnityEditor.AssetDatabase.CreateAsset(timeline, assetPath);
    var track = timeline.CreateTrack(/* §3.1 track type */, null, "<trackName>");
    var eventAsset = UnityEditor.AssetDatabase.LoadMainAssetAtPath(eventAssetPath);

    // FIRE-AT-SELF (simplest): routeTo=Self, value=1 — the bound entity receives at the clip's start
    //   edge, the ONLY timing that matters.
    // ROUTED: routeTo=Target/Owner/Source/Custom through the binding's Targets (slot must be assigned!);
    //   + routeLink=<discovered EntityLinkSchema> to redirect via the link map; a missed hunt fires at routeTo.
    // ACCUMULATE: N clips, same key/entity/start -> ONE event with the summed value. Never 0, never sums of 0.
    var clip = track.CreateClip(/* TimelineEssenceEventClip type */);
    clip.start = 1; clip.duration = 0.5; clip.displayName = "<clipName>"; // duration irrelevant
    var so = new UnityEditor.SerializedObject((UnityEngine.Object)clip.asset);
    so.FindProperty("conditionEvent").objectReferenceValue = eventAsset; so.FindProperty("routeTo").intValue = 4; // Self
    so.FindProperty("value").intValue = 1;             // never 0
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

- Project: `/home/i/GitHub/vex-ee` (`dataPath=/home/i/GitHub/vex-ee/Assets`); parent scene `Assets/Scenes/Main Scene.unity`;
  SubScene `Assets/Scenes/Main Sub Scene.unity`; `Stage_Director` (PlayableDirector + TimelineReferenceAuthoring, the only
  director); `Stage_Actor` carries `TargetsAuthoring` (Target=Stage_Target) AND `EntityLinkSource` (Root=Stage_LinkRoot);
  `Stage_LinkRoot` bakes `EntityLinkEntry` `{Key=10, Target=Stage_Actor}`.
- Event assets: **152** `ConditionEventObject`s under `Assets/Settings/Schemas/Events/` (curriculum said 156 — drifted;
  re-count). Innocuous choice `OnGrabCompleted.asset`, **key 94**, guid `88e814b9160342b7a8cd01be0478c306`. Link schema
  `Schema_Actor` guid `3b375c42affc2917f956d01310d31894`, id=10.
- Asset built (lesson 11): `Assets/Training/11-timeline-essence-event-track/EssenceEventMastery.playable` — one track
  `EssenceEventTrack`, clips A_FireAtSelf (1–1.5s, Self, value=1), B_FireViaLink (3–3.5s, Target, routeLink=Schema_Actor,
  value=1), C_Accumulate (3–3.5s, Target, value=2, overlapping B on the same track).
- Resolver demos: A → Stage_Actor. B demonstrates **fallback path 4, not link-win 3**: routeTo → Stage_Target (no
  `EntityLinkSource`/`EntityLinkEntry`) → hunt fails → Stage_Target receives anyway. (Link-win here: `routeTo=Self` —
  Stage_Actor → Stage_LinkRoot → `{10 → Stage_Actor}`.) B+C same frame at Stage_Target → ONE `Trigger(OnGrabCompleted, 3)`.
- Wiring: `EssenceEventTrack → Stage_Actor (TargetsAuthoring)`; binding table grew 8 → **9** (prior 8 preserved — tables key
  by track asset, surviving playableAsset swaps); director restored to
  `Assets/Training/01-transform-position-track/PositionMastery.playable`.
- Known pre-existing console baseline: UnityCliConnector HTTP server start, PerformanceTesting
  IPrebuildSetup/IPostBuildCleanup, TestResults.xml save, lessons 08–10 `[Worker2]` EntityLinks bake errors.

## 6. UNDO APPENDIX

Runtime note: the effect is **transient by construction** — the consumer clears the buffer the same frame, the condition bit
is masked off; NEVER runtime state to compensate, even in worlds that played the timeline. Undo is purely the authoring
artifacts:

Artifact inventory for one run of §4 (vex-ee instance shown in §5):
1. Created asset `<assetPath>` (.playable; `DeleteAsset` removes the track/clip sub-assets with the file).
2. Possibly-created folder(s) `<assetFolder>` (only if `PRE|folderExisted=false`).
3. Mutated `director.playableAsset` (vex-ee lesson 11: `EXPECTED:` previously `PositionMastery.playable` — the report proves
   the restored END state but never prints the pre-wiring value; capture it per §3.5).
4. Added generic binding entry for the new track in the SubScene file (vex-ee: table 8 → 9; `EXPECTED:` the prior 8 entries
   were counted, not itemized, pre-wiring — capture the full table per §3.5).
5. No scene values, event assets, or Reaction components changed (event reuse mandatory; listening side = another specialist).

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
// UNDO-3: restore any other captured scene values — normally none beyond UNDO-1 (transient runtime, no
// scene/asset mutations besides the director); include only entries your own journal recorded.
```

UNDO-4 (verify, fresh load — protocol §7): reload the SubScene additively; `director.playableAsset` and the binding table must
equal the CAPTURED `PRE|` values; `AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath)` must be null; restore the
parent scene; console clean against the project baseline.

## 7. VERIFICATION PROTOCOL

1. **Fresh-load asset dump**: new exec block; `LoadAssetAtPath` the `.playable`, dump every track/clip (name, start/duration,
   `routeTo`, `routeLink`, `conditionEvent` + key, `value`). In-memory state after a save is not evidence.
2. **Raw YAML check**: `routeTo` as byte; `conditionEvent`/`routeLink` guids present where intended (no `{fileID: 0}` for an
   intended ref); camelCase field names; same-start overlaps survive reload.
3. **Event-asset check**: re-dump the chosen event key live (§3.4 — keys/inventories drift; vex-ee drifted 156 → 152).
4. **Binding check from a RELOADED SubScene**: expect `BINDING|<trackName>|bound=<bindTarget> (TargetsAuthoring)` — the
   component, not the Transform; prior entries intact.
5. **Parent-scene restore**: end with `sceneCount=1`, `scene[0]=<parentScenePath>|loaded=True|active=True|dirty=False`.
6. **Console**: nothing new beyond the project baseline (§5); NO errors even when misconfigured — silence expected, not evidence.
