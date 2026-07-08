# TODO.md

> Scope: `Packages/BovineLabs.Timeline.Essence` (runtime/data/authoring/debug/editor/tests), `Packages/com.bovinelabs.reaction.addon` (+ `Vex.Spawning`), with the supporting `com.bovinelabs.essence` and `com.bovinelabs.reaction` cores and the `Reaction.Timeline` bridge.
>
> **Verification status: COMPLETE.** Every claim below was verified against the real source on disk (2026-07-07) by four independent Opus review agents. Each item carries a `Verified:` stamp with file:line evidence. Items refuted during verification are listed in "Refuted / Resolved During Verification" near the end — do not implement those.
>
> **Package locations (matters for where fixes land):**
> - EMBEDDED (fix directly here): `Packages/BovineLabs.Timeline.Essence`, `Packages/com.bovinelabs.reaction.addon`.
> - GIT DEPENDENCIES (fix in the `IAFahim/tertle-monorepo` fork, then bump): `com.bovinelabs.essence@316898e50d07`, `com.bovinelabs.reaction@4e6cebc9855d`, `com.bovinelabs.timeline@f2e051774ec5`, `com.bovinelabs.core@0a2094767ea8` — all resolve into `Library/PackageCache/`. Items touching these are tagged **[UPSTREAM]**.

## Executive Summary

The runtime architecture is genuinely strong: edge-triggered delivery (`ClipActive`/`ClipActivePrevious`), a pure-function delivery gate with a retry latch (`TimelineEssenceDeliveryPending` — verified correct, truth table Skip→false / Drop→false / Retry→true / Fire→false), per-frame coalescing in the Event/Intrinsic systems, tick rollback, and loop re-arming are all correct patterns with real unit-test coverage. The verified risk clusters:

1. **Null-pointer dereference in player builds for unregistered event keys (NEW Critical).** `ConditionConfig.GetPayloadType` → `BlobHashMap` indexer dereferences a NULL `Ptr` when the key is missing and collections checks are off. Any `ConditionEventObject` used at runtime but absent from `ReactionSettings` = native crash/UB in a player.
2. **Event-key collision + batching semantics in tick delivery (Critical cluster).** One `ConditionEvent` write per key per receiver per frame is enforced by `TryAdd` — and the rejection log is inside `ENABLE_UNITY_COLLECTIONS_CHECKS`, so in player builds the drop is **completely silent** (and `EventsDirty` is still set). `TimelineEssenceTickSystem` and `ActionTickDistributionSystem` don't coalesce across sources, and both batch multiple ticks into one event whose value is the batch **sum** — `Equality.Equal` reaction conditions silently miss ticks at low FPS.
3. **The addon lags the Timeline systems' correctness patterns.** `ActionTickDistributionSystem` commits `AppliedTicks`/`EndFired` before any delivery check (missing writer OR null To-target both permanently lose ticks), has an unsynchronized main-thread `Clear()` race on job-owned containers, and the Create systems silently skip unregistered `ObjectDefinition`s.
4. **A concurrency race in `ActionTimelineSystem`** — parallel writes to `Timer`/`TimelineActive`/`TrackBinding` when two reactions activate the same director in one frame (only `Targets` is queued). **[UPSTREAM]**
5. **Silent misdirection and dead-config traps for designers**: link-route fallback quietly fires at the *unlinked* target; stat key 0 / `(int)` value truncation / event `value == 0` / `valuePerTick == 0` all bake dead-or-wrong clips with zero editor-time validation; deactivate-edge actions are skipped on dying entities (`[WithDisabled(DestroyEntity)]`) — silently breaking the clone-expires→spawn-follow-up pattern the addon exists for.

Refuted during verification (dump-truncation artifacts; real code is fine): `TargetsDebugSystem` names/leak, `StatTrendSystem` sample interval, `EssenceDeliveryGate.Evaluate` out-param, `(double)LocalTime` units. All six proposed integration tests were confirmed MISSING from the test assembly.

## System Inventory

**BovineLabs.Timeline.Essence** (embedded)
- Authoring: `TimelineEssence{Event,Intrinsic,Stat,Tick}Clip/Track` — DOTS Timeline clips baking `EntityLinkRef` routes + payloads via the Data builders. Tracks bind `TargetsAuthoring`.
- Data: components (`TimelineEssence{Event,Intrinsic,Stat,Tick}Data`, `DeliveryPending` enableable latch, `StatState`, `TickState`), pure math (`EssenceDeliveryGate`, `LoopRefireMath`, `StatModifierMath`, `TickMath`, `CdfIntegration`, `DistributionCurveBlob`), `TimelineEssenceResolver` (Target slot + optional EntityLink hop).
- Runtime: `TimelineEssence{Event,Intrinsic}System` (gather→coalesce→apply via `ConditionEventWriter`/`IntrinsicWriter` facets, plus `DiagnoseMissedJob`), `TimelineEssenceStatSystem` (while-active `StatModifiers` add/remove + `ICleanupComponentData` safety net), `TimelineEssenceTickSystem` (CDF-driven tick emission with rollback), `TimelineEssenceLoopRefireSystem` (re-arms `ClipActivePrevious` on loop wrap).
- Debug: `EssenceTelemetrySystem`, `ReactionTelemetrySystem` (+`HistorySystem`), `StatTrendSystem`, `TargetsDebugSystem`, `EssenceDebugNamesBaker`.
- Editor: `EssenceInspectorWindow`, `essence_state` / `schema_list` CLI tools, `SchemaIconPostprocessor`.
- Tests: strong unit coverage of the pure math + system fixtures (event retry/once-only; tick rollback; stat apply/remove/reapply). All cross-system/integration scenarios below verified MISSING.

**com.bovinelabs.reaction.addon** (embedded) — `ActionCreateOn{Activate,Deactivate,ChanceFail}`, `ActionDestroyOn{Activate,Deactivate,ChanceFail}` (buffer + system pairs on the Active enable/disable edges), `ActionTickDistribution` (stat-driven CDF ticks → intrinsic and/or events), `ActionResolver` (verified SAFE: Null/`Exists` guarded; ECB `AddComponent` idempotent). Empty `BovineLabs.Reaction.Addon.Debug` assembly. `Vex.Spawning`: `CloneSpawnLifetime` timer→`DestroyEntity`.

**Cores (Library/PackageCache, [UPSTREAM])** — Essence: `Stat`/`Intrinsic` dynamic hashmaps, `StatModifierCalculator`, `IntrinsicWriter`/`ConditionEventWriter` facets, condition-write queues, ghost sync. Reaction: conditions/actives/actions pipeline, `ConditionEventWriteSystem` (payload double-rewind allocator), `ActionTimelineSystem` bridge.

## Dependency & Flow Map

- **Bake**: clip `Bake()` → `Essence*Builder.ApplyTo(BakerCommands)` → components + disabled `DeliveryPending` / zeroed state. Routes bake as `EntityLinkRef {ReadRootFrom: Target, LinkKey}`.
- **Frame order (essence timeline path)**: `TimelineComponentAnimationGroup`: `EntityLinkTargetPatchSystem` → `TimelineEssenceLoopRefireSystem` (mutates `ClipActivePrevious`; `[UpdateBefore]` Intrinsic/Event/Tick verified; Stat deliberately excluded) → Event/Intrinsic/Tick/Stat systems. Delivery = resolve binding → Target slot → optional link hop → require the *receiver capability* (`ConditionEvent`+`EventsDirty`, `Intrinsic` buffer, `StatModifiers` buffer) → coalesce per (target,key) → facet write.
- **Consumption**: `ConditionWriteEventsGroup`: `ConditionEventWriteSystem` drains `ConditionEvent` maps into subscriber `ConditionActive`/`ConditionValues`, clears buffers, rewinds the payload allocator (2-frame lifetime). Then `ConditionAllActiveSystem` → `ActiveSystem` → `ActiveEnabled/DisabledSystemGroup` (addon Action* systems, `ActionTimelineSystem`).
- **Stats**: `StatModifiers` + `StatChanged` → `StatChangedSystemGroup` (`StatCalculationSystem` → `IntrinsicValidationSystem` clamp → `StatChangedResetSystem`).
- **Verified implicit contracts**: (a) one `ConditionEvent` write per key per receiver per frame, enforced by `TryAdd` with a checks-only error — silent in players; (b) receivers must carry `EventWriterAuthoring` / `StatAuthoring` capability buffers or all essence writes are impossible (timeline clips retry+diagnose; the addon has no equivalent net); (c) `ClipActivePrevious` on Event/Intrinsic/Tick clip archetypes is owned by LoopRefire; (d) cleanup components can't be baked, so `TimelineEssenceStatSystem` attaches its cleanup one frame late via `BeginSimulationEntityCommandBufferSystem` — verified: no other removal path exists if the clip dies first.

## Critical TODOs

### TODO: Unregistered ConditionEventObject → null-pointer dereference in player builds [UPSTREAM + local validation]

**Priority:** Critical
**Certainty:** Confirmed
**Lens:** Validation / Edge Case
**Verified:** CONFIRMED — `ConditionConfig.cs:15-18` (`return this.Value.Value.PayloadTypes[key];`); `BlobHashMap.cs:27-42` (com.bovinelabs.core): on miss, checks builds throw `KeyNotFoundException`; with checks off, `TryGetValue` leaves `item = default` (`Ptr<TValue>` with null pointer) and the indexer returns `ref value.Ref` = `UnsafeUtility.AsRef<T>(null)`. Reached from `ConditionEventWriter.cs:47` for any event key missing from the baked `PayloadTypes` map.
**Problem:** Any `ConditionEventObject` referenced by a timeline clip, tick distribution, or gameplay code that is not registered in `ReactionSettings` (and therefore absent from the `ConditionConfig` blob) crashes or corrupts memory in a player build the first time it fires. In the editor it throws — but only if the event actually fires during testing.
**Why It Matters:** A shipping-crash class defect triggered by an asset-registration mistake — the exact mistake the auto-register pipeline occasionally misses (known unregistered/duplicate-schema traps in this project).
**Suggested Change:** Two layers. (1) **[UPSTREAM]** `ConditionEventWriter.Trigger` uses `TryGetValue` on the payload map and fails loudly-but-safely (error + drop); or `BlobHashMap`'s indexer hard-fails in all build types. (2) **Local build-time validation**: content check that every `ConditionEventObject` referenced by any baked clip/action exists in `ReactionSettings` — fail the build, not the player.
**Implementation Path:** Upstream: `GetPayloadType` → `TryGetPayloadType(key, out type)`; update call sites (`ConditionEventWriter`, `ConditionEventWriteSystem`). Local: build-preprocessor scanning `TimelineEssence*Clip` assets + addon authoring for event refs, diffed against `ReactionSettings.ConditionEvents`.
**How to Verify:** Checks-off test world firing an unregistered key → error log + no crash after the fix; validator flags a scene containing an unregistered event clip.
**Tradeoffs:** One extra branch per trigger — negligible.
**Confidence:** High

### TODO: Coalesce ALL same-frame ConditionEvent writes per (receiver, key) — tick systems collide and the drop is SILENT in player builds

**Priority:** Critical
**Certainty:** Confirmed
**Lens:** Event / Timing
**Verified:** CONFIRMED — `ConditionEventWriter.cs:63` (`TryAdd`, rejects duplicates), `:65-70` (error log inside `#if ENABLE_UNITY_COLLECTIONS_CHECKS` → silent drop in players), `:72` (`EventsDirty` set even on failure). `TimelineEssenceTickSystem.cs:103` calls `writer.Trigger` per clip with no cross-clip coalescing (contrast `TimelineEssenceEventSystem.cs:242-261`, which coalesces via `AddOrAccumulate`); rollback at `:105`/`:135` covers only resolution failure — `Trigger` returns void, so a duplicate-key rejection leaves `tickState.Fired` **committed as delivered**.
**Problem:** Two tick clips with the same event on the same target, or a tick clip + an event clip + an addon tick distribution sharing a key, in one frame → one write wins non-deterministically (job order), the rest vanish, and the tick source believes it delivered.
**Why It Matters:** Lost gameplay events under exactly the conditions designers create (two DoT clips sharing an `OnTic` event). Editor gives error spam; player builds give nothing.
**Suggested Change:** Make int-payload `Trigger` *accumulate* within the frame (`GetOrAddRef += value`) — the semantics the coalescing ApplyJobs already implement — keeping reject+error only for typed (non-int) payloads where summing is undefined. **[UPSTREAM]** (writer lives in the Reaction core). Alternative: route every essence-side write through one shared per-frame accumulator singleton; the writer change is smaller and fixes all producers at once.
**Implementation Path:** 1) Upstream `TriggerPayload`: int path accumulates (drop entries that sum to zero, respecting the existing `Check.Assume(value != 0)`). 2) Keep per-system FixedList accumulation as a batching optimization or delete it. 3) Test: two tick clips, same key/target/frame → one summed event, no error, both `Fired` committed.
**How to Verify:** Testing #1; `TimelineEssenceEventSystemTests` must stay green.
**Tradeoffs:** Changes the "one writer per event" contract project-wide; grep for the duplicate-error string first to find anyone relying on it as a design guard.
**Confidence:** High

### TODO: Decide and enforce batched-tick event VALUE semantics under frame skips (low-FPS correctness)

**Priority:** Critical
**Certainty:** Confirmed
**Lens:** Timing / Designer Safety
**Verified:** CONFIRMED — `TimelineEssenceTickSystem.cs:96-98` (`value = ValuePerTick * delta`, single `Trigger`); `TimelineEssenceTickStatSystemTests.cs:35` delivers 10 missed ticks as ONE value-10 event; `ReactionUtil.EqualityCheck` compares the raw batched value. No subscriber-side test exists (verified MISSING).
**Problem:** A reaction condition authored as `OnTic == 1` (or `== ValuePerTick`) works at high FPS and silently misses whenever a frame delivers ≥2 ticks — i.e., precisely during lag spikes. The total is conserved only for `Accumulate`/`>=` consumers. Identical semantics in `ActionTickDistributionSystem`.
**Suggested Change:** (a) Document loudly (clip tooltip + wiki): tick events are *batched*; consume with `ConditionFeature.Accumulate` or `GreaterThanEqual`, never `Equal`. (b) Editor validator: warn when a `ConditionEventObject` referenced by a tick clip is consumed anywhere with `Equal`/`NotEqual`. (c) Optional opt-in per-clip `FirePerTick` mode: clamp delta per frame; the CDF target re-drives the remainder next frame (bounded catch-up).
**Snippet/Pseudocode:**
```csharp
var due = target - tickState.Fired;
var fire = data.FirePerTick ? math.min(due, data.MaxCatchUpPerFrame) : due;
tickState.Fired += fire; // remainder re-emerges next frame from the same CDF target
```
**How to Verify:** Testing #2: one-step 0→1 advance, `TickCount=10`, vs an `Equal 1` subscriber (documents the miss) and an `Accumulate` subscriber (conserved).
**Tradeoffs:** Catch-up trades burst delivery for up-to-N-frames latency; batching stays the default (allocation-free, total-conserving).
**Confidence:** High

### TODO: ActionTickDistributionSystem — no rollback (missing writer AND null To-target) plus an unsynchronized container Clear race

**Priority:** Critical
**Certainty:** Confirmed
**Lens:** Event / State
**Verified:** CONFIRMED — `ActionTickDistributionSystem.cs:179` commits `AppliedTicks = math.max(...)` *before* `toTarget` is fetched (`:181`) and before the `delta > 0 && toTarget != Entity.Null` guard (`:183`); `EndFired` (`:200`) checks only `toTarget != Null`, never writer presence. `ApplyIntrinsicJob:245` and `ApplyEventJob:292` silently `return` on `Writers.TryGet` failure — neither touches `AppliedTicks`/`EndFired`. **Additional verified race:** `:77-78` main-thread `intrinsicTargets.Clear()` / `eventTargets.Clear()` on persistent sets still readable by the previous frame's `GetKeysJob`s, without completing `state.Dependency` — throws under safety checks, data race without.
**Problem:** Ticks are permanently lost when (a) the To target's writer isn't resolvable yet, or (b) the To target is momentarily `Entity.Null` (link not yet populated) — both common when the reaction spawns its own payload target the same frame (`ActionCreateOnActivate` → tick payload). The Timeline tick system explicitly un-commits (`Fired -= delta`); the addon never got the pattern.
**Suggested Change:** Resolve delivery capability *inside* `EvaluateJob` before committing: require `toTarget != Null` plus, per configured output, `Intrinsics.HasBuffer(toTarget)` / `ConditionEvents.HasBuffer(toTarget) && EventsDirty.HasComponent(toTarget)`; skip the `AppliedTicks`/`EndFired` commit when unresolved. Fix the Clear race by clearing inside a job chained on `state.Dependency` (mirror `TimelineEssence*System`'s `_changes.Clear(state.Dependency)` pattern).
**Snippet/Pseudocode:**
```csharp
var needsIntrinsic = cfg.TickStore != 0;
var needsEvents = !cfg.OnTic.Equals(ConditionKey.Null) || !cfg.OnEnd.Equals(ConditionKey.Null);
var resolved = toTarget != Entity.Null
    && (!needsIntrinsic || Intrinsics.HasBuffer(toTarget))
    && (!needsEvents || (ConditionEvents.HasBuffer(toTarget) && EventsDirty.HasComponent(toTarget)));
if (!resolved) return; // AppliedTicks/EndFired untouched -> retries next frame
state.AppliedTicks = math.max(state.AppliedTicks, expected);
```
**How to Verify:** Port `Tick_MissedDelivery_RollsBackAndDeliversEveryTickOnce` to the addon (Testing #3); run with collections checks + job debugger for the Clear race.
**Tradeoffs:** Perpetually-unresolvable targets now retry forever silently — pair with the diagnose TODO (High).
**Confidence:** High

### TODO: Fix the shared-director write race in ActionTimelineSystem [UPSTREAM]

**Priority:** Critical
**Certainty:** Confirmed
**Lens:** Event / Architecture
**Verified:** CONFIRMED — `ActionTimelineSystem.cs` (com.bovinelabs.reaction): `ActivatedJob` is `ScheduleParallel` (`:37`); `TrackBindings` (`:61-62`), `Timers` (`:64-65`), `TimelineActives` (`:67-68`) are all `[NativeDisableParallelForRestriction]` and written to *other* entities at `:81/:91/:103-104/:126-127`. Only `Targets` is `[ReadOnly]` + queued (`:96-101`) and applied single-threaded (`ApplyDirectorTargetsJob:134-152`).
**Problem:** Two reaction entities activating the same director in one frame race on `TimelineActive`, `Timer.Time`, and `TrackBinding.Value`; with `ResetWhenActive=false` both can pass the `timelineActive.ValueRO` check before either writes, defeating the guard.
**Suggested Change:** Extend the existing pattern: enqueue a `DirectorActivation { Director, InitialTime, Reset, bindings… }` and apply *all* director mutations in the single-threaded `IJob`, which also makes the `ResetWhenActive` check honest (processed in order against actual state).
**Snippet/Pseudocode:**
```csharp
// ActivatedJob: read-only + enqueue
Activations.Enqueue(new DirectorActivation { Director = d, InitialTime = t, Reset = cfg.ResetWhenActive, Source = entity });
// single-threaded ApplyJob:
while (Activations.TryDequeue(out var a)) {
    var active = TimelineActives.GetEnabledRefRW<TimelineActive>(a.Director);
    if (!a.Reset && active.ValueRO) continue;   // honest, race-free
    active.ValueRW = true;
    Timers.GetRefRW(a.Director).ValueRW.Time = new DiscreteTime(a.InitialTime);
    ApplyBindings(a);                            // TrackBinding writes here too
}
```
**How to Verify:** Testing #4: two reactions → one director, one frame; deterministic single activation; job debugger clean.
**Tradeoffs:** Serializes director mutation — trivial (activations are rare).
**Confidence:** High

## High Priority TODOs

### TODO: Make link-route fallback explicit — silent misdirection when the EntityLink is absent

**Priority:** High
**Certainty:** Confirmed (behavior); design intent unverified
**Lens:** Designer Safety / Event
**Verified:** CONFIRMED — `TimelineEssenceResolver.cs:46-63`: when `route.LinkKey != 0` (`:49`) and `EntityLinkResolver.TryResolve` fails or returns `Entity.Null` (`:55`), control falls to `:61-62`: `resolved = target; return true;` — the gate sees `resolved == true` and FIRES at the unlinked base target.
**Problem:** A designer routing "damage whatever I'm linked to" instead damages the base target the moment the link is missing — one-shot, unretryable, invisible.
**Why It Matters:** Misdirected stat modifiers/events corrupt gameplay state silently (self-damage, buffing the wrong unit) and are near-impossible to diagnose without an event trace.
**Suggested Change:** Per-clip policy enum `LinkMissBehavior { FallbackToTarget (current), Retry, Drop }` baked into the route (align with the EntityLinks family's `fallbackToRoot` concept). Default new clips to `Retry` (the pending latch already supports it). At minimum, log when a fallback occurs (debug builds).
**Implementation Path:** Extend `EntityLinkRef`/builders with the policy byte → `TryResolveLinkedTarget` returns false for `Retry`, true+Null for `Drop` → the gate handles both already. Rebake required.
**How to Verify:** Testing #7: clip with `LinkKey != 0`, no link on target → per-policy Retry/Drop/Fallback behavior.
**Confidence:** High

### TODO: Editor-time validation for dead clip configs (key 0, value truncation, event value 0, valuePerTick 0, tick outputs unset)

**Priority:** High
**Certainty:** Confirmed
**Lens:** Validation / Designer Safety
**Verified:** CONFIRMED — `StatModifierMath.cs:13` (`stat.Value == 0` → silent false), `:21` (`(int)value` truncation; 0.5 bakes a live-but-no-op modifier that still occupies a `StatModifiers` slot and flips `StatChanged`). `TimelineEssenceEventSystem` GatherJob `:148` (`hasPayload = ... && data.Value != 0` → value 0 silently Drops, clears the latch, and `DiagnoseMissedJob` never warns because pending is false). NEW (verified): `TimelineEssenceTickSystem:96-98` — `valuePerTick == 0` computes value 0 and `return`s WITHOUT un-committing, so ticks are consumed and nothing fires; no bake validation. `ActionTickDistributionAuthoring.cs:32-59` validates curve fields only — all three sinks (`TickStore`/`OnTic`/`OnEnd`) unset bakes a do-nothing config.
**Suggested Change:** In each clip/authoring `Bake()`: error on `Key.ID == 0` ("asset not imported/registered — re-import"); for Added/Subtracted error when `(int)value == 0 && value != 0` ("Added is whole ×100 fixed-point units; 0.5 truncates to 0 — did you mean 50?"); error on event `value == 0` and tick `valuePerTick == 0`; error when a tick distribution has no sink. Mirror in inspector warning boxes so it appears before play mode.
**Snippet/Pseudocode:**
```csharp
static bool ValidateStatValue(Object ctx, StatAuthoringType type, float value)
{
    if (type is StatAuthoringType.Added or StatAuthoringType.Subtracted && (int)value == 0 && value != 0)
    { Debug.LogError($"{ctx.name}: Added/Subtracted uses whole x100 fixed-point units; {value} truncates to 0 (did you mean {value * 100:0}?)", ctx); return false; }
    return true;
}
```
**How to Verify:** Testing #8: authoring tests with `LogAssert.Expect` per bad config (none exist today — verified zero `LogAssert` in the test assembly).
**Confidence:** High

### TODO: One-frame stat-modifier leak window — cleanup attached via next-frame ECB, no other removal path

**Priority:** High
**Certainty:** Confirmed
**Lens:** State / Full System Flow
**Verified:** CONFIRMED — `TimelineEssenceStatSystem.cs`: `StatModifierCleanup : ICleanupComponentData` (`:81`); attach via `BeginSimulationEntityCommandBufferSystem` ECB (`:45`, `AttachCleanupJob :153-163`, plays back NEXT frame). Verified no other removal path: `GatherRemoveJob` (`:131-151`) needs the state component + deactivation edge; `GatherDestroyedJob` (`:174-188`) needs `StatModifierCleanup` present. A clip destroyed the same frame it applied satisfies neither → the `StatModifiers` entry on the target leaks permanently, and the queued `AddComponent` targets a dead entity at playback.
**Suggested Change:** Attach same-frame: either `EndSimulationEntityCommandBufferSystem` (verify it runs before this project's destroy handling) or an immediate `EntityManager.AddComponent` pass gated on a `WithNone<StatModifierCleanup>` query being non-empty (once per newly-seen clip; rare sync point). Also (verified nuance, P2-5): `AttachCleanupJob` currently tags EVERY stat clip including ones that never applied (`AppliedTarget == Entity.Null`), making every stat clip a destruction "zombie" — gate the attach on `AppliedTarget != Entity.Null` to shrink both the churn and the leak surface.
**How to Verify:** Testing #6: apply then destroy clip entity in one frame → target `StatModifiers` empty after cleanup systems run.
**Confidence:** High

### TODO: Deactivate-edge actions silently skip on dying entities — breaks the clone-expiry→follow-up pattern

**Priority:** High
**Certainty:** Confirmed (behavior); intent undocumented
**Lens:** Designer Safety / Full System Flow
**Verified:** CONFIRMED — `ActionCreateOnDeactivateSystem.cs:42` (and the destroy-on-deactivate/chance systems) carry `[WithDisabled(typeof(DestroyEntity))]`. An entity being torn down via `DestroyEntity` the same frame it deactivates will NOT run its deactivate actions.
**Problem:** A clone expiring via `Vex.Spawning/CloneSpawnLifetime` (which enables `DestroyEntity`) will skip its own `ActionCreateOnDeactivate` — i.e., "clone explodes when it expires" silently does nothing when the expiry is the lifetime timer rather than a reaction duration. This is exactly the pattern the addon is built for.
**Suggested Change:** Decide and document: if intentional (avoid acting on mid-destruction entities), state it in the authoring tooltips and give designers the supported alternative (reaction `Active.duration` instead of `CloneSpawnLifetime`, or `ActionCreateOnDeactivate` firing from the destroy path via `ActiveDisableOnDestroySystem` ordering). If unintentional, remove `[WithDisabled(DestroyEntity)]` from the CREATE-on-deactivate system (destroy-on-deactivate should keep it) and verify ordering vs `DestroyEntitySystem`.
**How to Verify:** Playmode/ECS test: clone with lifetime 0.1s + `ActionCreateOnDeactivate` → does the payload spawn after expiry?
**Confidence:** High on behavior; Medium on the right resolution.

### TODO: Addon Create systems must not silently skip unregistered ObjectDefinitions

**Priority:** High
**Certainty:** Confirmed
**Lens:** Designer Safety / Validation
**Verified:** CONFIRMED — `ActionCreateOnActivateSystem.cs:52`, `ActionCreateOnDeactivateSystem.cs:55`, `ActionCreateOnChanceFailSystem.cs:62`: `if (!ObjectDefinitions.TryGetValue(create.Id, out var prefab)) continue;` with no log. Core `ActionCreateSystem.cs:79` indexes the registry directly (asserts on miss) — the addon deliberately swallows what core surfaces.
**Suggested Change:** Thread `BLLogger` into the jobs and `LogError512` with the ObjectId + source entity on miss. Add bake-time validation in each addon authoring baker: definition id != 0 and present in `ObjectManagementSettings`.
**How to Verify:** Unit test with empty registry → error log, no crash; bake test with unregistered definition → bake error.
**Confidence:** High

### TODO: ChanceFail actions are silently incompatible with composite conditions — validate at bake

**Priority:** High
**Certainty:** Confirmed
**Lens:** Designer Safety / Validation
**Verified:** CONFIRMED — `ActionCreateOnChanceFailSystem.cs:40-41,57` and `ActionDestroyOnChanceFailSystem.cs:38-39,54`: `[WithNone(typeof(ConditionComposite))]` + `AllTrue` detection; core `ConditionAllActiveSystem.cs:270` computes composite pass state via `EvaluateCompositeLogic`, not `AllTrue` — composites are excluded from the query entirely, so their ChanceFail actions can never fire. No authoring validation exists in either ChanceFail authoring.
**Suggested Change:** In the ChanceFail bakers, check the sibling `ReactionAuthoring.Conditions` for a composite expression → `Debug.LogError`; also warn when `chanceToTrigger == 1` (fail actions can never fire). Longer term: publish a `ConditionsPassed` enableable from `ConditionAllActiveSystem` distinct from the chance roll so ChanceFail works for composites.
**How to Verify:** Bake a reaction with an expression + ChanceFail action → console error; runtime unchanged.
**Confidence:** High

### TODO: Idle gating — RequireForUpdate on Event/Stat/LoopRefire (timeline) and the addon tick system; harden the writer singleton fetch

**Priority:** High
**Certainty:** Confirmed (with one correction)
**Lens:** Performance
**Verified:** PARTIAL/CONFIRMED — `TimelineEssenceEventSystem.OnCreate:44-61` no RequireForUpdate; `TimelineEssenceStatSystem.OnCreate:27-33` none (and `OnUpdate:38-45` allocates two `NativeQueue`s + an ECB writer unconditionally); `TimelineEssenceLoopRefireSystem` has no OnCreate at all. CORRECTION: `TimelineEssenceIntrinsicSystem.OnCreate:52` DOES `RequireForUpdate<EssenceConfig>()` (like Tick) — gated in worlds lacking the config, but still ungated on clip presence. Addon: `ActionTickDistributionSystem.cs:58` requires only the always-present `EssenceConfig` and schedules its full 6-job pipeline every frame with zero tick entities (verified P2-2). Also verified (P2-3): `TimelineEssenceEventSystem.OnUpdate:79` updates the `ConditionEventWriter.Lookup` unconditionally, whose generated code `GetSingleton<ConditionConfig>()`/`<ConditionEventPayloadAllocator>()` — throws in a world that has the system but not the Reaction bootstrap.
**Suggested Change:** `RequireForUpdate<TimelineEssence{Event,Stat}Data>()`, a query-require for LoopRefire's WithAny trio, `RequireForUpdate<ActionTickDistribution>()` in the addon; plus `RequireForUpdate<ConditionConfig>()`/`<ConditionEventPayloadAllocator>()` on the event system for robustness.
**How to Verify:** Profiler: zero scheduling cost in scenes without essence clips; event system no longer throws in a bare test world.
**Confidence:** High

### TODO: Tick diagnostics parity — end-of-clip diagnose (timeline) and silent no-op exits (addon); enrich all diagnose messages

**Priority:** High
**Certainty:** Confirmed
**Lens:** Debugging / Event
**Verified:** CONFIRMED — Timeline (B/P2-1): if a tick clip's target never resolves for its entire active window, every tick is rolled back and lost with NO warning, unlike Event/Intrinsic's `DiagnoseMissedJob`. Addon (C/P2-4): `ActionTickDistributionSystem` EvaluateJob has three silent exits — `:159` (From target null / missing Stat buffer), `:165` (`duration <= 0`), `:167` (curve not created) — a misconfigured tick action produces zero runtime signal. Also verified (B/P2-6): the existing DiagnoseMissedJobs' `if (!pending.ValueRO) return;` guard is dead code (query is already enabled-filtered) — harmless, remove. The diagnose message copy names "ReactionAuthoring" where the writer actually comes from `EventWriterAuthoring`, and carries no entity/key identity.
**Suggested Change:** Add a `DiagnoseMissedTickJob` on the tick clip falling edge (warn when `Fired < CDF end target`); one-time warnings on the addon's missing-Stat path; include `entity.ToFixedString()` + key id + route info in all diagnose messages; fix the component name in the copy.
**How to Verify:** End a tick clip against a writer-less target → one warning naming the clip entity and event key.
**Confidence:** High

### TODO: Delete StatExtensions.Read3 — incoherent unsafe code with ZERO callers [UPSTREAM]

**Priority:** High (trivial effort)
**Certainty:** Confirmed
**Lens:** Architecture / State
**Verified:** CONFIRMED — `com.bovinelabs.essence@316898e50d07/BovineLabs.Essence.Data/StatExtensions.cs:75-83`: casts the `DynamicBuffer<Stat>` pointer to `float*`, offsets by a stat *key*, returns `ref float3`; `Stat` is a 1-byte dynamic-hashmap element (`Stat.cs:16-21`) so the buffer holds hashmap bucket/metadata bytes; the bounds check `:89` (`key + 3 > stats.Length`) doesn't bound a 12-byte read at 4-byte stride. Caller search across `Packages/`, `Assets/`, `Library/PackageCache/`: **zero call sites** — only the definition.
**Suggested Change:** Delete the method (and `CheckInRange`) in the monorepo fork. Any future dense-float need should be a dedicated component.
**How to Verify:** Compiles after removal (no callers exist).
**Confidence:** High

### TODO: Clamp baked intrinsic defaults to schema range; reconcile the two "default" semantics [UPSTREAM]

**Priority:** High
**Certainty:** Strongly Likely (core code matches the reviewed dump; end-to-end clamp gap not exercised by agents)
**Lens:** Validation / Designer Safety
**Files/Systems Involved:** `IntrinsicBuilder.ApplyTo` (com.bovinelabs.essence), `StatAuthoring.Baker.BakeIntrinsics`, `IntrinsicWriter`, `EssenceInspectorWindow`
**Problem:** `IntrinsicBuilder` writes raw summed authoring defaults with no clamping against `IntrinsicSchemaObject.Range` or stat-driven bounds — until the first `IntrinsicWriter` touch, out-of-range values are live and visible to conditions/UI (the inspector even warns "will be clamped", but nothing clamps until a write). Separately: schema `DefaultValue` applies only when a key is absent at first write, while authoring defaults pre-populate the key — two different "defaults" with no documentation.
**Suggested Change:** Clamp in `IntrinsicBuilder.ApplyTo` (bakers have the schema; pass min/max into `Default`), or run a one-shot clamp on `InitializeEntity` via `IntrinsicValidationSystem`. Document the DefaultValue-vs-authoring-default rule in both tooltips.
**How to Verify:** Testing #9: bake default 999 with range [0,100] → entity starts at 100; a condition subscribed to the intrinsic sees 100 on frame 1.
**Confidence:** Medium-High

## Medium Priority TODOs

### TODO: ReactionTelemetryHistorySystem — inverted enable gate, O(n) head-inserts, per-frame ECB

**Priority:** Medium (debug/BL_DEBUG builds)
**Certainty:** Confirmed
**Lens:** Performance / Debugging
**Verified:** CONFIRMED — `ReactionTelemetrySystem.cs:97-98`: `if (!Enabled && !HasSingleton<DrawSystem.Singleton>()) return;` — runs the full recorder whenever ANY drawer singleton exists even with reaction telemetry disabled (including servers under BL_DEBUG); `:122` `history.Insert(0, ...)` O(n) shift per event; `:101-111` ECB created + played every non-returning update (an `AddBuffer` structural pass every frame for every `ConditionEvent` entity).
**Suggested Change:** Gate on this drawer actually being active (mirror `TimelineDebugUtility.TryGetDrawer`); append to tail + render newest-last; skip the ECB when the `WithNone<ReactionEventHistoryRecord>` query is empty.
**Confidence:** High

### TODO: StatTrendSystem gates on the force-enable ConfigVar — trend deltas never render via the normal enable path

**Priority:** Medium (debug builds)
**Certainty:** Confirmed (new finding, replaces the refuted sample-interval claim)
**Lens:** Debugging
**Verified:** CONFIRMED — `StatTrendSystem.cs:41` samples only when `EssenceTelemetryConfig.Enabled.Data` (the `essencetelemetry.draw-enabled` FORCE flag, default false); the consumer `EssenceTelemetrySystem.cs:131` activates via `TimelineDebugUtility.TryGetDrawer`, which can enable the panel from a viewer/registration without that flag. Result: panel on, `AppendTrendDelta` (`:552-576`) needs ≥2 samples, zero samples recorded → the `(+delta)` readout silently never appears unless the ConfigVar is force-set.
**Suggested Change:** Gate `StatTrendSystem` on the same drawer-active condition the panel uses (shared helper), not the raw ConfigVar.
**Confidence:** High

### TODO: essence_state CLI tool — missing buffer guard, leaked EntityQueries, key-0 schemas hidden

**Priority:** Medium
**Certainty:** Confirmed
**Lens:** Debugging / Edge Case
**Verified:** CONFIRMED — `EssenceStateTool.cs:74` `em.GetBuffer<ConditionEvent>(target, true)` with no `HasBuffer` guard (target chosen from `Intrinsic`-bearing entities `:45-69`, which doesn't imply an event buffer → throws); `:172,:177` `PickWorld` creates undisposed `EntityQuery`s per world per invocation (contrast the correct `using var q` at `:45`); `:158` `BuildKeyNames` drops key 0 (`if (found > 0 ...)`) — precisely the "key 0 = unusable" misconfiguration a debug tool should surface, silently rendered as `#0` fallback.
**Suggested Change:** `HasBuffer` guard returning an empty events array + note field; `using` the queries; include key 0 flagged as INVALID in the name map.
**Confidence:** High

### TODO: Unify clip Bake() failure behavior; skip the dead-config blob

**Priority:** Medium
**Certainty:** Confirmed
**Lens:** Architecture / Validation
**Verified:** CONFIRMED (with nuance) — Event/Intrinsic/Stat clips early-return before `builder.ApplyTo` AND skip `base.Bake`; verified nuance: `DOTSClip.Bake` is an empty virtual (`DOTSClip.cs:42-44`), so the real asymmetry is skipping the builder, not the base call. `TimelineEssenceTickClip.Bake:52-81` logs but unconditionally runs `BuildCurve()` (Persistent blob, ownership transfers to the BlobAssetStore — wasteful, not leaked) and bakes `TickCount = max(0, tickCount)` even for a dead clip.
**Suggested Change:** One contract: log + bake inert-but-consistent. Skip `BuildCurve` when `tickCount <= 0`; extract a shared `BakeError(this, message)` helper.
**Confidence:** High

### TODO: Document stat-clip retarget-while-active semantics

**Priority:** Medium
**Certainty:** Confirmed
**Lens:** State / Designer Safety
**Verified:** CONFIRMED — `TimelineEssenceStatSystem` GatherAddJob `:108` gates on `if (state.AppliedTarget != Entity.Null) return;` — a `Targets`/EntityLink change mid-clip leaves the modifier on the stale target until deactivation (safe: remove uses `AppliedTarget`; no leak).
**Suggested Change:** Tooltip: "the modifier stays on the entity resolved at clip start". Optional `FollowBindingChanges` flag: detect `resolved != AppliedTarget` while active → remove(old)+add(new) in the same ApplyJob.
**Confidence:** High

### TODO: Zero-sum coalescing consumes one-shot deliveries silently

**Priority:** Medium
**Certainty:** Confirmed
**Lens:** Event / Designer Safety
**Verified:** CONFIRMED — `TimelineEssenceEventSystem` ApplyJob `:239` (`if (e.Amount != 0)`) and overflow path `:260` skip zero sums; the clips' `DeliveryPending` latches were already consumed by `Outcome.Fire` in GatherJob (`:164`). Verified side-note: the skip also prevents an editor throw, since `ConditionEventWriter.Trigger` `Check.Assume`s value != 0 (`ConditionEventWriter.cs:53`).
**Suggested Change:** Debug-build log when a coalesced sum cancels to zero; optional bake-time warning for opposite-signed same-key same-start clips on one track.
**Confidence:** High

### TODO: ActionTickDistribution — undocumented ÷100 stat semantics and duplicated plumbing

**Priority:** Medium
**Certainty:** Confirmed
**Lens:** Designer Safety / Maintainability
**Verified:** CONFIRMED — `ActionTickDistributionSystem.cs:162-163` reads `TicPerSecond`/`TicDuration` via `stats.GetValueFloat` = `Added * Multi / 100` (`StatValue.cs:14-27`): a stat authored base 100 = 1.0 tick/sec; `ActionTickDistributionAuthoring.cs:21-22` fields carry NO tooltip. `:129-140` private `GetTarget` duplicates `Targets.Get` (`Targets.cs:47-60`). `GetKeysJob`/`GetEventKeysJob` (`:207-233`) byte-for-byte identical; `ApplyIntrinsicJob`/`ApplyEventJob` + amount structs (`:235-371`) structurally identical pairs (~120 extractable lines).
**Suggested Change:** Tooltips ("×100 fixed-point: Added 200 = 2.0 ticks/sec"); replace `GetTarget` with `targets.Get`; dedupe the jobs (see Architecture).
**Confidence:** High

### TODO: CloneSpawnLifetime — define Seconds==0 semantics, align world filter, pin an update group

**Priority:** Medium
**Certainty:** Confirmed
**Lens:** Edge Case
**Verified:** CONFIRMED — `CloneSpawnLifetimeAuthoring.cs:18` clamps negatives to 0; `CloneSpawnLifetimeSystem.cs:33-40` destroys when `Seconds <= dt` → 0 = destroy on first tick; `:7` `[WorldSystemFilter(WorldSystemFilterFlags.Default)]` while every addon sibling scopes `LocalSimulation | ClientSimulation | ServerSimulation`; verified additionally (P2-6): NO `[UpdateInGroup]` — lands at an undefined point in `SimulationSystemGroup` relative to the LifeCycle destroy consumer.
**Suggested Change:** Either treat `Seconds <= 0` at bake as "don't add" (infinite, tooltip) or keep instant-destroy and warn; adopt the sibling world-filter triad; pin the system near the LifeCycle group.
**Confidence:** High

### TODO: SyncCleanupJob change filter

**Priority:** Medium
**Certainty:** Confirmed
**Lens:** Performance
**Verified:** CONFIRMED — `TimelineEssenceStatSystem.cs:65,165-172`: no `[WithChangeFilter]`, copies `AppliedTarget` → `cleanup.Target` for every stat clip every frame.
**Suggested Change:** `[WithChangeFilter(typeof(TimelineEssenceStatState))]`.
**Confidence:** High

### TODO: Payload-allocator lifetime guard for out-of-group writers

**Priority:** Medium
**Certainty:** Risk (not agent-verified end-to-end)
**Lens:** Timing / Edge Case
**Problem:** `ConditionEventPayload` >8 bytes lives in `ConditionEventWriteSystem`'s double-rewind allocator. Timeline essence writes happen in a different group; a world running the timeline group without `ConditionWriteEventsGroup` (or with the write system disabled) leaves buffered large payloads to outlive the rewind → dangling reads.
**Suggested Change:** Essence systems `RequireForUpdate<ConditionEventPayloadAllocator>` (also covered by the High idle-gating item) + a checks-build assert that `ConditionEvent` buffers are empty at frame start.
**Confidence:** Medium

### TODO: NetCode attribute guards in Essence.Data [UPSTREAM]

**Priority:** Medium
**Certainty:** Risk (core code; consistency not agent-verified)
**Lens:** Architecture / Portability
**Problem:** `EssenceConfig`/`StatDefaults`/`StatModifiers`/`InitializeStats` use `Unity.NetCode` + `[GhostComponent]` unguarded while `StatGhost`/`IntrinsicGhost` are `#if UNITY_NETCODE` — one rule should apply (guard everything or hard-depend and delete the guards).
**Confidence:** Medium

## Low Priority TODOs

- **Extract the duplicated accumulate/apply plumbing** — `AddOrAccumulate` + keys/apply job shells exist in `TimelineEssenceEventSystem`, `TimelineEssenceIntrinsicSystem`, `ActionTickDistributionSystem` (×2, verified `:207-371`), and core `ActionIntrinsicSystem`. One generic utility (~400 lines saved) is also where the Critical coalescing fix lands once. *(Confirmed)*
- **Empty `BovineLabs.Reaction.Addon.Debug` assembly** — .asmdef only, zero sources, large reference set (Anchor, AppUI, InputSystem, Physics, Entities.Graphics) compiled for nothing. Populate or remove. *(Confirmed, C/P2-7)*
- **Missing `[BurstCompile]`** — `ActionTickDistributionSystem.OnDestroy` (`:61`), `CloneSpawnLifetimeSystem.OnCreate` (`:10`); audit the rest. Timeline.Essence itself verified clean (all jobs/system methods annotated; all persistent containers disposed). *(Confirmed, C/P2-5 + B sweep)*
- **`ActionTagSystem`/`ActionTagDeactivatedSystem` each build a full TypeManager stable-hash map at create** — share one via the existing Singleton. **[UPSTREAM]** *(Confirmed pattern in dump; core file)*
- **Telemetry char-by-char `FixedString.Append('S');Append('T');…`** — replace with `(FixedString32Bytes)"STATS"` literals/cached statics. *(Confirmed)*
- **SchemaIconPostprocessor** — full 3-folder `FindAssets` scan on every domain reload (`:31-32,149-155`); `SaveAssets` only when something changed (verified idempotent via `:168`), but a mis-configured project (missing icon PNGs) logs errors on EVERY reload (`:53,:68,:143`) — log once via a static flag. *(Confirmed/PARTIAL, D/P3-3)*
- **EssenceInspectorWindow** — `World.DefaultGameObjectInjectionWorld` (`:332`, wrong under multi-world; reuse `PickWorld`); duplicated stat formula `ResolveStatValue:147-164` (nuance: `StatModifierCalculator` is `internal` and runtime-typed, so extract a small shared preview helper rather than calling it directly); RW `GetBuffer` for read-only display (`:311,:320`) forces a sync point every 500ms poll — use `GetBuffer(e, true)`. *(Confirmed, D/P3-5)*
- **DiagnoseMissedJob dead guard** — `if (!pending.ValueRO) return;` unreachable under the enabled-filtered query in both Event and Intrinsic systems; remove. *(Confirmed, B/P2-6)*
- **File/name hygiene** — `TimelineEssenceTracks.cs` contains only `TimelineEssenceEventTrack`; `ConditionSchema.cs` (Reaction.Data) is empty. *(Confirmed in dump)*
- **`ConditionAuthoring.OnValidate` log spam** on every inspector touch/import. **[UPSTREAM]** *(Confirmed in dump)*
- **Timeline stat clip duplicates sign/negation logic** owned by `StatAuthoringUtil.GetValueRaw` — route through it to prevent drift. *(Confirmed in dump)*
- **Event ApplyJob FixedList overflow path** (`:260`) triggers immediately per overflow value — >~300 unique keys per target per frame can re-hit the duplicate-write path; convert to a second pass or log once. *(Risk)*

## Refuted / Resolved During Verification — DO NOT IMPLEMENT

These came from the truncated dump; agents verified the real source is fine:

1. **`TargetsDebugSystem` names map + TempJob leak — REFUTED.** Real code adds the name (`TargetsDebugSystem.cs:98-99`) and disposes against the job handle (`:89`). No action.
2. **`StatTrendSystem` samples every frame — REFUTED.** `nextSample = time + SampleInterval` exists (`:44-45`); samples every 0.1s as designed. (Superseded by the NEW ConfigVar-gating finding above.)
3. **`EssenceDeliveryGate.Evaluate` unassigned out-param — RESOLVED.** All paths assign; truth table Skip→false, Drop→false, Retry→true, Fire→false (`EssenceDeliveryGate.cs:35-59`).
4. **`(double)LocalTime.Value` units — RESOLVED.** `DiscreteTime` converts to seconds; `TickMath` units are consistent.
5. **`ActionResolver.EnableDestroy` double-add hazard — RESOLVED SAFE.** Null/`Exists` guarded (`ActionResolver.cs:22`); ECB `AddComponent` idempotent + explicit enable (`:25-26`).
6. **"TickSystem is the only EssenceConfig-gated system" — CORRECTED.** `TimelineEssenceIntrinsicSystem` also requires `EssenceConfig` (`:52`); Event/Stat/LoopRefire remain ungated (see the High idle-gating item).

## Designer Safety TODOs

1. **Editor validation pass for all four clips** (High item above): key-id 0, value truncation, event value 0, `valuePerTick` 0, tick sinks unset — inspector warning boxes, not just bake logs.
2. **Clip inspectors surface the receiver contract**: "target must have EventWriterAuthoring (events/ticks) / StatAuthoring with modifiable stats (stat/intrinsic clips)". Today the requirement appears only as a runtime warning after failure. A custom inspector checking the bound `TargetsAuthoring`'s GameObject at edit time closes most silent-failure tickets.
3. **×100 fixed-point in every tooltip that touches a stat number** — stat clip has it; `ActionTickDistributionAuthoring` (verified: none) and any distance/stat bridges need identical wording.
4. **Link-route policy visible in the inspector** — while fallback remains the behavior, show "falls back to Target when link missing" with a warning icon.
5. **ChanceFail + composite** bake error; warn when ChanceFail actions exist with `chanceToTrigger == 1`.
6. **Tick event consumption guidance**: warn when a tick clip's `ConditionEventObject` is consumed anywhere with `Equal`/`NotEqual` (see batching Critical).
7. **Clone-expiry pattern documentation** (High item): `CloneSpawnLifetime` + `ActionCreateOnDeactivate` silently don't compose today — document the supported pattern.
8. **Showcase builders**: single preflight listing all missing assets at once; remove `GameObject.Find` ordering dependency (sample-only).

## Validation & Guard TODOs

- **Build-time (promoted to Critical support):** every `ConditionEventObject` referenced by any baked clip/action must exist in `ReactionSettings` — an unregistered key is a player-build null deref (Critical #1).
- Bake-time: schema key != 0 across all schema references; duplicate-key scan across `EssenceSettings`/`ReactionSettings` (the known two-StaggerMeter trap) as a settings-inspector button + build check.
- Bake-time: addon Spawn arrays — add zero-id and not-registered checks (High item).
- Runtime: `essence_state` HasBuffer guards; `TimelineEssenceResolver` assert on out-of-enum `ReadRootFrom` (corrupted bake currently resolves Null and retries forever).
- Runtime: diagnose copy fixes (name `EventWriterAuthoring`, include entity + key ids) — the "loud failure where silent is dangerous" guard.

## Timing / Physics / Animation TODOs

- **Batched tick semantics** (Critical) is the headline low-FPS item.
- **`ClipActivePrevious` ownership**: LoopRefire fabricates rising edges for Event/Intrinsic/Tick archetypes (verified `[UpdateBefore]` all three consumers; Stat deliberately excluded). Document the invariant on the system + data README.
- **Clock split**: timeline tick clips advance on *timeline local time* (DiscreteTime seconds, verified); `ActionTickDistribution` advances on `SystemAPI.Time.DeltaTime` (world time) — the two tick families desync under `TimelineTimeScale`/`WorldTimeScale`. Document which clock each uses.
- **Payload allocator lifetime** (Medium item): guard worlds that write events but never run the consumer group.

## Architecture TODOs

### TODO: Extract one shared "essence delivery core" used by the Timeline systems AND the addon

**Priority:** High (enabler for three Critical/High fixes)
**Certainty:** Strongly Likely (duplication verified; design proposal)
**Lens:** Architecture
**Verified:** Duplication confirmed at `TimelineEssenceEventSystem.cs:242-261`, `TimelineEssenceIntrinsicSystem` (same shape), `ActionTickDistributionSystem.cs:207-371` (identical key-jobs + structurally identical apply pairs), plus core `ActionIntrinsicSystem`.
**Problem:** Five systems independently implement gather→coalesce→resolve-writer→apply, and each copy has drifted: rollback exists in one, coalescing in some, diagnostics in two. Every correctness fix currently needs 3–5 edits.
**Suggested Change:** An Essence-core-level `EssenceDeliveryBuffer<TKey, TAmount>` (fallback multimap + unique-key list + apply-with-writer job, two concrete writer specializations — no interface dispatch under Burst) plus the resolver + gate as the single entry point. Timeline systems and the addon become thin gather jobs. Ownership: buffer in Essence core (**[UPSTREAM]** or a local embedded utility assembly if upstream churn is unwanted); delivery *policy* (edges/pending/rollback) stays in each caller.
**Implementation Path:** 1) Lift `AddOrAccumulate`+apply into the utility with tests. 2) Port `TimelineEssenceIntrinsicSystem` (simplest). 3) Port the event system. 4) Port the addon — rollback lands there for free. 5) Delete the duplicates.
**Confidence:** High

- **Make the receiver-capability requirement a baked contract, not a runtime probe** — a `RequiresEssenceReceiver` bake-time tag + scene validation report ("these 3 clips route to prefabs that can never receive them") turns the retry/diagnose machinery into a streaming-latency-only path. *(Medium)*
- **Replace the addon's private `GetTarget` with `Targets.Get`** (verified `:129-140` vs `Targets.cs:47-60`). *(Low, fold into the tick fixes)*
- **`Target` enum hole (Custom = 6, no 5)** — document or reserve. *(Low)*
- **Decide the assembly home of `EssenceDebugNames`** (runtime component consumed only by debug/bakers). *(Low)*

## Debugging / Tooling TODOs

1. **Essence delivery event trace** (the highest-leverage tool): a BL_DEBUG ring buffer of `(frame, source, target, key, outcome[Fire/Retry/Drop/Fallback/DuplicateDrop], value)` written from the gather/apply layer — answers "why didn't my clip fire" for every family, including the two silent player-build paths verified above. Surface in `EssenceInspectorWindow` and as `essence_state op=trace`.
2. Fix `ReactionTelemetryHistorySystem` gating + O(n) insert (Medium item).
3. Fix `StatTrendSystem` enable-path mismatch (Medium item) so trend deltas actually render.
4. `essence_state` guards + query disposal + key-0 surfacing (Medium item).
5. Diagnose message identity/copy fixes (High item).
6. Timeline clip editor affordance: draw tick marks on `TimelineEssenceTickClip` (reuse the addon inspector's CDF preview in a `ClipEditor`).

## Testing TODOs

Verified against the test assembly: **all six scenarios MISSING** (zero `LogAssert` usages anywhere; every system test drives exactly one clip against one target).

1. **Same-frame same-key multi-source event test** (Critical #2): two tick clips → one target; also tick clip + event clip. Today demonstrates the silent drop; after the accumulate fix proves summation.
2. **Low-FPS batched-tick consumer test** (Critical #3): `TickCount=10` in one step vs `Equal ValuePerTick` subscriber and an `Accumulate` subscriber. (The batch-delivery half exists at `TimelineEssenceTickStatSystemTests.cs:35`; the subscriber half is the missing point.)
3. **Addon tick rollback test** (Critical #4): port `Tick_MissedDelivery_RollsBackAndDeliversEveryTickOnce` to `ActionTickDistributionSystem`, covering both missing-writer and null-To-target.
4. **Shared-director double-activation test** (Critical #5): two reactions, one director, one frame — deterministic single activation after the fix.
5. **LoopRefire × Event/Tick system integration**: the real `TimelineEssenceLoopRefireSystem` composed with the delivery systems across a loop wrap (only math/gate units exist today: `LoopRefireMathTests`, `EssenceDeliveryGateTests:65`).
6. **Stat clip same-frame destroy test** (High leak window): apply then destroy clip entity in one frame → target `StatModifiers` empty after cleanup.
7. **Link-route miss policy tests** (High): all existing tests use `LinkKey = 0`; add `LinkKey != 0` unresolvable → per-policy behavior. (The existing "retry" tests cover missing *writer*, a different miss.)
8. **Bake validation tests** (High): `LogAssert.Expect` per bad config — key 0, truncating Added, event value 0, `valuePerTick` 0, tick without sinks.
9. **Baked intrinsic clamp test** (High): out-of-range authored default clamped at init.
10. **Clone-expiry composition test** (High): `CloneSpawnLifetime` + `ActionCreateOnDeactivate` — pins whatever behavior the team decides.

## Suggested Architecture Direction

**Current weakness (verified):** delivery correctness — edge detection, retry, coalescing, rollback, diagnostics — is a *pattern* re-implemented per system rather than a *component*, so it exists in five different states of completeness. The one global invariant (one `ConditionEvent` write per key per receiver per frame) is enforced only by a `TryAdd` whose failure is logged in checks builds and silent in players; and the payload-type lookup behind every trigger dereferences null on unregistered keys in players.

**Desired boundaries & ownership:**
- **Essence core owns delivery**: the accumulate-per-(receiver,key) buffer, writer resolution probes, "sum int payloads / reject typed duplicates" rule, and a `TryGetPayloadType` that can never null-deref — all next to `ConditionEventWriter`. All producers (timeline clips, addon actions, gameplay systems) write through it; `ConditionEventWriteSystem` remains the single consumer.
- **Timeline.Essence owns *when***: edges, pending latches, loop re-arm, per-clip state — gather jobs emitting `(target, key, amount)` plus a delivery-outcome path (commit/rollback of `Fired`/`Pending`).
- **Addon owns reaction-edge policy** only, reusing the same gather shape (this is where its missing rollback, race-free clears, and diagnostics arrive for free).
- **Event flow** becomes observable at exactly one choke point — where the debug event trace attaches.
- **Validation flow**: three layers — clip/settings inspectors (immediate), bake errors (complete per the Validation section), and a build report ("N clips route to receivers lacking capability X"; "M event keys unregistered").
- **Migration:** utility + tests → port Intrinsic → Event → Tick → addon; each step behind the existing suites. Only data-layout change is the optional `LinkMissBehavior` byte (rebake). Upstream changes (`ConditionEventWriter` accumulate, `TryGetPayloadType`, `ActionTimelineSystem` queue, `Read3` deletion) go to the `IAFahim/tertle-monorepo` fork and land as a version bump.
- **Verify** via Testing #1–5 against the shared core, then delete the per-system copies.

## Final Ranked TODO List

**Critical (verified, fix first):**
1. Unregistered `ConditionEventObject` → null-deref/UB in player builds — upstream `TryGetPayloadType` + local build-time registration check. **[UPSTREAM + local]**
2. Coalesce all same-frame ConditionEvent writes per (receiver,key); int payloads accumulate; drop is currently SILENT in players. **[UPSTREAM]**
3. Batched-tick value semantics at low FPS — document + validator + optional FirePerTick mode.
4. ActionTickDistribution: pre-resolve gate before committing AppliedTicks/EndFired (writer miss AND null To-target) + fix the unsynchronized `Clear()` race.
5. ActionTimelineSystem shared-director race → queue + single-threaded apply. **[UPSTREAM]**

**High:**
6. Link-route miss policy (Retry/Drop/Fallback) + fallback diagnostics.
7. Bake validation cluster: key 0, Added truncation, event value 0, `valuePerTick` 0, tick sinks unset.
8. Stat-modifier one-frame leak window: same-frame cleanup attach + gate attach on `AppliedTarget != Null`.
9. Deactivate actions skipped on dying entities (`[WithDisabled(DestroyEntity)]`) — decide + document; clone-expiry pattern currently broken.
10. Addon Create systems: log + bake-validate unregistered ObjectDefinitions.
11. ChanceFail × composite conditions bake error (+ chance==1 warning).
12. Idle gating: RequireForUpdate on Event/Stat/LoopRefire + addon tick; harden the event system's singleton fetch.
13. Tick diagnostics parity (timeline end-of-clip + addon silent exits) + message identity/copy fixes.
14. Delete `StatExtensions.Read3` (zero callers, incoherent unsafe). **[UPSTREAM]**
15. Clamp baked intrinsic defaults; document the two "default" semantics. **[UPSTREAM]**
16. Shared essence delivery core extraction (enabler for 2/4/13).

**Medium:**
17. ReactionTelemetryHistorySystem gating / tail-append / ECB skip.
18. StatTrendSystem enable-path mismatch (trend deltas never render via viewer path).
19. essence_state: HasBuffer guard, query disposal, key-0 surfacing.
20. Unify clip Bake failure contract; skip dead-clip blob.
21. Stat retarget-while-active documentation (+ optional FollowBindingChanges).
22. Zero-sum coalescing warning.
23. ActionTickDistribution ×100 tooltips + `Targets.Get` reuse.
24. CloneSpawnLifetime: Seconds==0 semantics, world-filter triad, pinned update group.
25. SyncCleanupJob change filter.
26. Payload-allocator lifetime guard (Risk).
27. NetCode guard consistency in Essence.Data (Risk). **[UPSTREAM]**

**Low (batchable cleanup pass):**
28. Dedup accumulate/keys/apply plumbing (subsumed by #16 if done).
29. Remove empty Reaction.Addon.Debug assembly (or populate).
30. `[BurstCompile]` on ActionTickDistributionSystem.OnDestroy + CloneSpawnLifetimeSystem.OnCreate; repo-wide audit.
31. ActionTag stable-hash map sharing. **[UPSTREAM]**
32. Telemetry FixedString literals; SchemaIconPostprocessor error-once + scan gating; EssenceInspectorWindow world/RO-buffer/preview-helper; DiagnoseMissedJob dead guard; file naming; OnValidate log spam; stat-clip negation via StatAuthoringUtil; Event ApplyJob overflow path.

**Testing (land with their fixes):** items 1–10 in the Testing section — all verified missing today.

---

# IMPLEMENTATION STATUS (2026-07-08)

All fixes below were implemented by four parallel Opus fix agents and compile-verified per assembly (`dotnet build` / direct Roslyn `csc` with the Unity define set; the only remaining solution errors are a pre-existing unrelated `com.unity.ugui` issue). A fifth agent is running the test suites and adding the new coverage. **No git commits were made** — review and commit per package.

## Where the changes live

- **Embedded (permanent once committed):** `Packages/BovineLabs.Timeline.Essence`, `Packages/com.bovinelabs.reaction.addon` (+ Vex.Spawning).
- **Upstream (canonical):** `~/GitHub/tertle-monorepo` working tree (was clean at HEAD `cf20b76e`; the only modifications are these fixes). **Mirrored** byte-identical into `Library/PackageCache/com.bovinelabs.{reaction,essence}@…` so this project compiles/runs against them NOW — **the mirrors are ephemeral** (wiped on package re-resolve). Permanent path: commit + push the fork, bump `packages-lock.json`.

## Required follow-ups (manual)

1. **Rebake subscenes** — component layouts changed: all four `TimelineEssence*Data` gained a `LinkMiss` byte (defaults to legacy FallbackToTarget); `ActionTickDistributionState` gained `WarnedFlags`.
2. **Open Unity once** so it generates `.meta` files for the three new Editor/Debug sources (`TelemetryGate.cs`, `EssenceEditorWorlds.cs`, `EssenceEventRegistrationValidator.cs`) and regenerates csprojs.
3. **Push the tertle-monorepo fork + bump the lock** to make the upstream fixes permanent (until then, a Unity package re-resolve silently reverts the PackageCache mirrors — the coalescing test added by the test agent is the canary).
4. Decide the deferred behavioral question in item 9 below (deactivate actions on dying entities).

## Per-item status (numbering = Final Ranked TODO List above)

**Critical**
1. Unregistered event null-deref — **FIXED** (upstream `TryGetPayloadType` + writer/write-system TryGet+drop+log; local `EssenceEventRegistrationValidator` menu item + build-failing `IPreprocessBuildWithReport`). Pending fork push.
2. Same-frame event-key collision — **FIXED** upstream: `ConditionEventWriter` now ACCUMULATES int payloads (sum; entry removed on sum==0); typed payloads keep single-writer + error. Pending fork push.
3. Batched-tick value semantics — **PARTIAL**: tooltips/docs landed on tick clip + addon authoring (Accumulate/>= guidance); opt-in `FirePerTick` catch-up mode and the project-wide Equal-consumer scan deliberately deferred.
4. Addon tick rollback + Clear race — **FIXED** (pre-resolve capability gate before `AppliedTicks`/`EndFired` commit; dependency-chained `ClearSetsJob`; `RequireForUpdate<ActionTickDistribution>`; `Targets.Get` reuse; key-job dedup; warn-once missing-From-stats diagnostics).
5. ActionTimelineSystem director race — **FIXED** upstream (read-only ActivatedJob → queued `DirectorActivation` → single-threaded ordered apply with honest `ResetWhenActive`; DeactivatedJob left parallel with a benign-race comment). Pending fork push.

**High**
6. Link-miss policy — **FIXED** (`LinkMissBehavior` Fallback/Retry/Drop through resolver, all four systems, builders, clips; default preserves legacy; REBAKE required).
7. Bake validation cluster — **FIXED** (key 0 / ×100 truncation / event value 0 / valuePerTick 0 / tick sinks unset / addon definition id 0; dead-clip blob skipped).
8. Stat cleanup leak window — **FIXED** (immediate main-thread cleanup attach; AttachCleanupJob deleted; SyncCleanupJob change filter; stat system idle-gate uses `RequireAnyForUpdate(statData, cleanupShadow)` so destroy-cleanup still runs after the last clip dies).
9. Deactivate actions on dying entities — **DOCUMENTED ONLY** (XML doc on CloneSpawnLifetimeAuthoring explains the `[WithDisabled(DestroyEntity)]` gap + supported alternative). Behavioral change deliberately deferred — decide whether Create-on-deactivate should fire during destroy.
10. Unregistered ObjectDefinition silent skip — **FIXED** (LogError512 with entity+id in all three Create systems + bake-time id-0 checks).
11. ChanceFail × composite — **FIXED** (shared reflection-based bake validation in both ChanceFail bakers; chance==1 warning).
12. Idle gating / RequireForUpdate — **FIXED** (Event/Stat/LoopRefire/Tick + ConditionConfig + PayloadAllocator; addon tick).
13. Tick diagnostics parity — **FIXED** (new `DiagnoseMissedTickJob`; enriched entity+key diagnose messages naming `EventWriterAuthoring`/`StatAuthoring`; dead guards removed; addon warn-once).
14. `StatExtensions.Read3` — **FIXED** upstream (deleted). Pending fork push.
15. Baked intrinsic clamp — **FIXED** upstream (`IntrinsicBuilder.Default` min/max + `StatAuthoring` passes schema Range). Pending fork push.
16. Shared delivery core extraction — **DEFERRED** (the writer-level accumulation fix centralizes the main correctness concern; full gather/apply extraction remains an architecture task).

**Medium** — 17 telemetry history gating/O(1) **FIXED** (new `TelemetryGate`); 18 StatTrend viewer-path gating **FIXED**; 19 essence_state guards/leaks/key-0 **FIXED**; 20 bake-contract unification **FIXED**; 21 retarget tooltip **FIXED**; 22 zero-sum log **FIXED**; 23 addon ×100 tooltips + `Targets.Get` **FIXED**; 24 CloneSpawnLifetime (filter triad, `BeforeSceneSystemGroup`+`UpdateBefore(DestroySystemGroup)`, Seconds≤0 warning) **FIXED**; 25 SyncCleanupJob change filter **FIXED**; 26 payload-allocator require **FIXED**; 27 NetCode guard consistency **DEFERRED** (upstream, risk-level).

**Low** — 28 plumbing dedup **PARTIAL** (addon deduped; generic extraction deferred with 16); 29 empty Debug asmdef **FIXED** (deleted + dangling InternalsVisibleTo removed); 30 BurstCompile additions **FIXED**; 31 ActionTag map sharing **FIXED** upstream; 32 batch: telemetry FixedString literals **FIXED**, SchemaIconPostprocessor error-once **FIXED**, EssenceInspectorWindow world/RO-buffers (+ shared `EssenceEditorWorlds`) **FIXED**, DiagnoseMissedJob dead guard **FIXED**, file rename **FIXED** (GUID preserved), OnValidate log noise **FIXED** upstream, stat-clip negation-drift **DEFERRED**, Event ApplyJob overflow path **DEFERRED**.

**Testing** — **WRITTEN + COMPILE-VERIFIED; run pending an external blocker.** Fixture regressions from the new RequireForUpdate gates fixed via a shared `TimelineEssenceTestFixture` (ConditionConfig blob + payload allocator). New tests (9, all compiling clean): `TimelineEssenceCoalescingTests` (two-tick-clips summed / event+tick summed — the PackageCache-mirror canary), `TimelineEssenceLinkMissTests` (Retry latched / Drop consumed / Fallback legacy / Retry→link-resolves delivers once at linked entity), `TimelineEssenceStatDestroyTests` (same-frame destroy → modifiers cleaned), addon `ActionTickDistributionSystemTests` (holds ticks while To lacks buffer → full backlog once, no double-delivery). Bake-validation LogAssert tests skipped (no baking-test precedent in either package). **The measured run is blocked by a collaborator's uncommitted WIP** in `Packages/BovineLabs.Timeline.UI/UIUnscaledClockSystem.cs` (6000.7.0a1 source-gen bug breaks whole-project compile; a parallel session holds the project lock). Re-run once that compiles: helper at `/tmp/run-ee-tests.sh`. **Additional upstream follow-up:** `BovineLabs.Reaction.Tests/Conditions/ConditionEventWriterTests.Trigger_WithDuplicateKey_LogsError` in the tertle-monorepo must be updated for the new accumulation semantics (expects the old error + value 42; now sums to 92 with no error) — update it in the fork alongside the writer fix.
