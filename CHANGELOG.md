# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- Event/Intrinsic/Stat/Tick timeline clips no longer silently miss when the target/binding/writer
  resolves a few frames after the clip activates (late streaming/spawn): delivery now retries across
  the whole active window instead of only the one-frame activation edge. Genuinely unresolvable Event/
  Intrinsic clips log a warning once instead of dropping silently.
- Event clips with `Value == 0` (or two clips netting to zero on the same target+key) no longer trip
  the `ConditionEventWriter` non-zero assert.

### Migration
- A new baked component (`TimelineEssenceDeliveryPending`) is added to Event/Intrinsic clips. Editing
  the bakers invalidates Unity's incremental-bake cache, so SubScenes re-bake automatically on next
  import/build. Any separately-shipped pre-baked content (Addressables, content-update bundles) must be
  rebuilt against this package version, or those clips will not fire until re-baked.

## [0.1.0] - 2026-04-17

### This is the first release of *\<BovineLabs.Timeline.Essence\>*.

*Short description of this release*
