---
name: unity-track-essence-event
description: Master of TimelineEssenceEventTrack + TimelineEssenceEventClip (package BovineLabs.Timeline.Essence) — firing transient ConditionEvents at entities from the timeline (the cutscene→reaction bridge), routeTo/routeLink resolution, the all-silent Essence guard rule; carries the ESSENCE FAMILY REFERENCE. Portable to any project containing the package; worked example from vex-ee.
---

# TimelineEssenceEventTrack specialist

## 1. SCOPE

You are the specialist for **`TimelineEssenceEventTrack`** and **`TimelineEssenceEventClip`** from the package
`BovineLabs.Timeline.Essence`, namespace `BovineLabs.Timeline.Essence.Authoring`. Scope: exactly this track — one clip = one
edge-triggered write of a transient `ConditionEvent` (key + amount) into a resolved entity's event buffer, so Reaction
conditions keyed by that event can respond. As the FIRST Essence track this skill carries the **ESSENCE FAMILY REFERENCE** the
Stat (`unity-track-essence-stat`) and Intrinsic (`unity-track-essence-intrinsic`) skills cross-reference: resolver semantics,
all-silent guard matrix, dead-`RouteLinkKey`-on-Stat. Triad: **events = transient signals (fire, react, evaporate — THIS)**,
intrinsics = permanent integer counters, stats = while-active float modifiers. Stage construction belongs to
`unity-stage-foundations`.

**Operate per `unity-timeline-track-authoring`; behave per `unity-agent-protocol`; use the editor per `unity-cli`.** That shared
skill owns the discovery preamble (§1), the SubScene bracket (§2), the undo-appendix structure (§3), and the verification
protocol (§4); this skill keeps ONLY the track-unique facts below.

## 2. PORTABLE SEMANTICS

True in ANY project containing `BovineLabs.Timeline.Essence` (plus its Essence and Reaction dependencies). All verified vex-ee
2026-06 via reflection dumps, package-source quotes, raw YAML, fresh-load read-backs, and a real forced bake for the
silent-null demo (all `unity-cli exec`, no play mode; runtime claims source-derived).

### THE HEADLINE — transient, silent, and summed

Everything this track writes **evaporates the same frame it is consumed** — the opposite of the EntityLinks family's
persistence; nothing to undo, ever. Every failure mode is **silent** (null event, null link, dead routeTo): a clip that "does
nothing" with a clean console is ALWAYS a config/resolution problem. Same-frame duplicates are **pre-summed**: ONE `Trigger`
per (receiver, key) — the buffer rejects and dev-logs duplicates.

### Type facts

| Type | Base | Facts |
|---|---|---|
| `TimelineEssenceEventTrack` | `DOTSTrack` | sealed, EMPTY body — no Bake override, no fields. `[TrackClipType(TimelineEssenceEventClip)]`, **`[TrackBindingType(BovineLabs.Reaction.Authoring.Core.TargetsAuthoring)]`** (the bind target), `[TrackColor(0.9,0.4,0.2)]`, `[DisplayName("BovineLabs/Essence/Timeline Event")]` |
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

**The silence profile.** Events are TRANSIENT — the CONSUMER clears: `ConditionEventWriteSystem` matches each entry against the
entity's `EventSubscriber` map, sets matched `ConditionActive` bits, disables `EventsDirty`, runs `conditionEvents.Clear();`;
downstream `ConditionEventResetSystem` masks the bits back off — both one-frame transients. Every config failure (null event,
null link, dead routeTo) is silent at bake AND runtime.

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
- **DON'T create schema/event assets, ever** — reuse the project's `ConditionEventObject` inventory (discover live; keys drift).

## 3. DISCOVERY DELTA

Run the discovery preamble per `unity-timeline-track-authoring` §1 (D1 package check with FullName
`BovineLabs.Timeline.Essence.Authoring.TimelineEssenceEventTrack` / assembly `BovineLabs.Timeline.Essence.Authoring`;
D2 scene/SubScene; D3 director; D5 `PRE|` capture). Track-specific D4 additions:

**Bind target + event key asset (+ link schema).** The track binds the **`TargetsAuthoring` COMPONENT** of a SubScene-baked
object. For non-Self `routeTo`, verify the slot is assigned in the binding's `TargetsAuthoring` (unassigned = silent total
loss). Discover event key assets LIVE — **keys drift between projects; NEVER create schema/event assets**:

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

## 4. CLIP PATTERNS (the bracket's track-specific middle)

Build per `unity-timeline-track-authoring` §2. `<TRACK_TYPE>` = `TimelineEssenceEventTrack`, `<CLIP_TYPE>` =
`TimelineEssenceEventClip`, `<BIND_TARGET>` = `BovineLabs.Reaction.Authoring.Core.TargetsAuthoring`. The event asset is
DISCOVERED (§3), NEVER created. Per clip set fields via `SerializedObject` on `clip.asset` (camelCase YAML names); clip
`duration` is irrelevant (one-shot on the start edge):

- **FIRE-AT-SELF (simplest)** — designer "at this beat, fire OnX at this thing": `routeTo` intValue `4` (Self),
  `conditionEvent` objectReferenceValue = discovered event asset, `value` intValue `1`. The bound entity receives at the clip's
  start edge.
- **ROUTED** — designer "fire at the enemy / owner / target": `routeTo` = `Target`/`Owner`/`Source`/`Custom` through the
  binding's `Targets` (the slot MUST be assigned, else silent total loss).
- **ROUTED VIA LINK** — designer "fire at whatever it's linked to": add `routeLink` objectReferenceValue = discovered
  `EntityLinkSchema` to redirect via the link map; a missed hunt fires at routeTo (path 4 fallback).
- **ACCUMULATE** — designer "land N at once": N clips, same key/entity/start → ONE event with the summed value. Never 0, never
  sums of 0.

## 5. WORKED EXAMPLE DELTA (vex-ee training stage) — example, rediscover never assume

Atop the shared stage (`unity-timeline-track-authoring` §5: project `/home/i/GitHub/vex-ee`, parent `Main Scene.unity`, SubScene
`Main Sub Scene.unity`, `Stage_Director` the only director). Track-specific delta:

- `Stage_Actor` carries `TargetsAuthoring` (Target=Stage_Target) AND `EntityLinkSource` (Root=Stage_LinkRoot); `Stage_LinkRoot`
  bakes `EntityLinkEntry` `{Key=10, Target=Stage_Actor}`.
- Event assets: **152** `ConditionEventObject`s under `Assets/Settings/Schemas/Events/` (curriculum said 156 — drifted;
  re-count). Choice `OnGrabCompleted.asset`, **key 94**, guid `88e814b9160342b7a8cd01be0478c306`. Link schema `Schema_Actor`
  guid `3b375c42affc2917f956d01310d31894`, id=10.
- Asset built (lesson 11): `Assets/Training/11-timeline-essence-event-track/EssenceEventMastery.playable` — one track
  `EssenceEventTrack`, clips A_FireAtSelf (1–1.5s, Self, value=1), B_FireViaLink (3–3.5s, Target, routeLink=Schema_Actor,
  value=1), C_Accumulate (3–3.5s, Target, value=2, overlapping B on the same track).
- Resolver demos: A → Stage_Actor. B demonstrates **fallback path 4, not link-win 3**: routeTo → Stage_Target (no
  `EntityLinkSource`/`EntityLinkEntry`) → hunt fails → Stage_Target receives anyway. (Link-win path needs `routeTo=Self` —
  Stage_Actor → Stage_LinkRoot → `{10 → Stage_Actor}`.) B+C same frame at Stage_Target → ONE `Trigger(OnGrabCompleted, 3)`.
- Wiring: `EssenceEventTrack → Stage_Actor (TargetsAuthoring)`; binding table grew 8 → **9** (prior 8 preserved); director
  restored to `Assets/Training/01-transform-position-track/PositionMastery.playable`.

## 6. UNDO + 7. VERIFICATION

Undo per `unity-timeline-track-authoring` §3, verify per its §4. **Runtime note**: the effect is **transient by
construction** — the consumer clears the buffer the same frame, the condition bit is masked off; NEVER runtime state to
compensate, even in worlds that played the timeline. Undo is purely the §3 authoring artifacts (asset, possible folder,
`director.playableAsset`, the added binding entry); no scene values, event assets, or Reaction components are ever changed
(event reuse mandatory; the listening side is another specialist).

Track-specific verification additions to the §4 protocol:
- §4.1 asset dump: include `routeTo`, `routeLink`, `conditionEvent` + key, `value` per clip.
- §4.2 YAML: `routeTo` as a byte; `conditionEvent`/`routeLink` guids present where intended (no `{fileID: 0}`); same-start
  overlaps survive reload.
- Add an **event-asset re-check**: re-dump the chosen event key live (keys/inventories drift; vex-ee drifted 156 → 152).
- §4 console: NO errors even when misconfigured — silence is expected, NOT evidence (every guard is silent).
