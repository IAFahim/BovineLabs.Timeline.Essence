using System.Collections.Generic;
using TMPro;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using TargetsAuthoring = BovineLabs.Reaction.Authoring.Core.TargetsAuthoring;
using TargetSlot = BovineLabs.Reaction.Data.Core.Target;
using StatAuthoring = BovineLabs.Essence.Authoring.StatAuthoring;
using StatModifierAuthoring = BovineLabs.Essence.Authoring.StatModifierAuthoring;
using StatSchemaObject = BovineLabs.Essence.Authoring.StatSchemaObject;
using IntrinsicSchemaObject = BovineLabs.Essence.Authoring.IntrinsicSchemaObject;
using StatAuthoringType = BovineLabs.Essence.Authoring.StatAuthoringType;
using ConditionEventObject = BovineLabs.Reaction.Authoring.Conditions.ConditionEventObject;
using LifeCycleAuthoring = BovineLabs.Core.Authoring.LifeCycle.LifeCycleAuthoring;
using TimelineBeginAuthoring = BovineLabs.Timeline.Core.Authoring.TimelineBeginAuthoring;
using TimelineBeginMode = BovineLabs.Timeline.Core.Authoring.TimelineBeginMode;
using StatTrack = BovineLabs.Timeline.Essence.Authoring.TimelineEssenceStatTrack;
using StatClip = BovineLabs.Timeline.Essence.Authoring.TimelineEssenceStatClip;
using IntrinsicTrack = BovineLabs.Timeline.Essence.Authoring.TimelineEssenceIntrinsicTrack;
using IntrinsicClip = BovineLabs.Timeline.Essence.Authoring.TimelineEssenceIntrinsicClip;
using EventTrack = BovineLabs.Timeline.Essence.Authoring.TimelineEssenceEventTrack;
using EventClip = BovineLabs.Timeline.Essence.Authoring.TimelineEssenceEventClip;

public static class EssenceShowcaseBuilder
{
    private const string SampleFolder = "Assets/Samples/EssenceShowcase";
    private const string TimelineFolder = SampleFolder + "/Timelines";
    private const string ParentPath = SampleFolder + "/EssenceShowcase.unity";
    private const string SubPath = SampleFolder + "/EssenceShowcase_Sub.unity";

    private const string RequiredInSubScenePath = "Assets/Prefabs/Required In Subscene.prefab";
    private const string MaxHealthPath = "Assets/Settings/Schemas/Stats/Max Health.asset";
    private const string MovementSpeedPath = "Assets/Settings/Schemas/Stats/MovementSpeed.asset";
    private const string AttackPowerPath = "Assets/Settings/Schemas/Stats/AttackPower.asset";
    private const string GoldenOrbsPath = "Assets/Settings/Schemas/Intrinsics/GoldenOrbs.asset";
    private const string ComboPointsPath = "Assets/Settings/Schemas/Intrinsics/ComboPoints.asset";
    private const string ArmorStacksPath = "Assets/Settings/Schemas/Intrinsics/ArmorStacks.asset";
    private const string EventGainedPath = "Assets/Settings/Schemas/Events/OnArmorStackGained.asset";
    private const string EventHitPath = "Assets/Settings/Schemas/Events/OnArmorHit.asset";
    private const string EventBlazePath = "Assets/Settings/Schemas/Events/OnBlazeExplosion.asset";

    private static readonly Color StatColor = new Color(0.20f, 0.90f, 0.40f);
    private static readonly Color IntrinsicColor = new Color(0.20f, 0.60f, 0.90f);
    private static readonly Color EventColor = new Color(0.90f, 0.40f, 0.20f);
    private static readonly Color TargetColor = new Color(0.95f, 0.20f, 0.20f);
    private static readonly Color PadColor = new Color(0.22f, 0.24f, 0.29f);
    private static readonly Color BannerColor = new Color(0.06f, 0.08f, 0.12f);

    private const float StatX = -14f;
    private const float IntrinsicX = 0f;
    private const float EventX = 14f;
    private const float RowStep = 6.5f;
    private const float ActorY = 1.0f;

    private static readonly Vector3 CameraPos = new Vector3(0f, 16f, -34f);

    private static Scene activeSub;
    private static StatSchemaObject maxHealth;
    private static StatSchemaObject movementSpeed;
    private static StatSchemaObject attackPower;
    private static IntrinsicSchemaObject goldenOrbs;
    private static IntrinsicSchemaObject comboPoints;
    private static IntrinsicSchemaObject armorStacks;
    private static ConditionEventObject eventGained;
    private static ConditionEventObject eventHit;
    private static ConditionEventObject eventBlaze;

    private sealed class TrackBind
    {
        public string TrackName;
        public string BindActorName;
    }

    private sealed class CellWire
    {
        public string DirectorName;
        public string TimelinePath;
        public List<TrackBind> Binds;
    }

    private static readonly List<CellWire> Wires = new List<CellWire>();

    private sealed class CaptionData
    {
        public string Title;
        public string Usage;
        public Vector3 CellPos;
        public Color Color;
    }

    private static readonly List<CaptionData> Captions = new List<CaptionData>();

    [MenuItem("Showcase/Build Essence")]
    public static void Build()
    {
        Wires.Clear();
        Captions.Clear();

        maxHealth = AssetDatabase.LoadAssetAtPath<StatSchemaObject>(MaxHealthPath);
        movementSpeed = AssetDatabase.LoadAssetAtPath<StatSchemaObject>(MovementSpeedPath);
        attackPower = AssetDatabase.LoadAssetAtPath<StatSchemaObject>(AttackPowerPath);
        goldenOrbs = AssetDatabase.LoadAssetAtPath<IntrinsicSchemaObject>(GoldenOrbsPath);
        comboPoints = AssetDatabase.LoadAssetAtPath<IntrinsicSchemaObject>(ComboPointsPath);
        armorStacks = AssetDatabase.LoadAssetAtPath<IntrinsicSchemaObject>(ArmorStacksPath);
        eventGained = AssetDatabase.LoadAssetAtPath<ConditionEventObject>(EventGainedPath);
        eventHit = AssetDatabase.LoadAssetAtPath<ConditionEventObject>(EventHitPath);
        eventBlaze = AssetDatabase.LoadAssetAtPath<ConditionEventObject>(EventBlazePath);

        if (maxHealth == null || movementSpeed == null || attackPower == null ||
            goldenOrbs == null || comboPoints == null || armorStacks == null ||
            eventGained == null || eventHit == null || eventBlaze == null)
        {
            Debug.LogError("EssenceShowcase: one or more schema assets missing. " +
                           "maxHealth=" + (maxHealth != null) + " movementSpeed=" + (movementSpeed != null) +
                           " attackPower=" + (attackPower != null) + " goldenOrbs=" + (goldenOrbs != null) +
                           " comboPoints=" + (comboPoints != null) + " armorStacks=" + (armorStacks != null) +
                           " eventGained=" + (eventGained != null) + " eventHit=" + (eventHit != null) +
                           " eventBlaze=" + (eventBlaze != null));
            return;
        }

        EnsureFolders();
        ResetAssets();

        var parent = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(parent, ParentPath);
        var sub = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.SetActiveScene(sub);
        activeSub = sub;

        BuildRequiredInSubScene();
        BuildPads();
        BuildStatColumn();
        BuildIntrinsicColumn();
        BuildEventColumn();

        EditorSceneManager.SaveScene(sub, SubPath);
        EditorSceneManager.SetActiveScene(parent);
        EditorSceneManager.CloseScene(sub, true);

        sub = EditorSceneManager.OpenScene(SubPath, OpenSceneMode.Additive);
        EditorSceneManager.SetActiveScene(sub);
        activeSub = sub;

        foreach (var w in Wires)
        {
            WireCell(w);
        }

        EditorSceneManager.MarkSceneDirty(sub);
        EditorSceneManager.SaveScene(sub);

        EditorSceneManager.SetActiveScene(parent);
        BuildParent();
        EditorSceneManager.SaveScene(parent);

        EditorSceneManager.CloseScene(sub, true);
        EditorSceneManager.OpenScene(ParentPath, OpenSceneMode.Single);

        Debug.Log("EssenceShowcase: built grid at " + ParentPath + " directors=" + Wires.Count +
                  " | statKeys MaxHealth=" + maxHealth.Key + " MovementSpeed=" + movementSpeed.Key +
                  " AttackPower=" + attackPower.Key + " | intrinsicKeys GoldenOrbs=" + goldenOrbs.Key +
                  " ComboPoints=" + comboPoints.Key + " ArmorStacks=" + armorStacks.Key +
                  " | eventKeys Gained=" + eventGained.Key + " Hit=" + eventHit.Key + " Blaze=" + eventBlaze.Key);
    }

    // ============================================================
    //  STAT column (green) — while-active, self-reverting modifiers.
    //  Actors seed whole-number Added bases for every demoed stat so
    //  Increased/More are observable (% on a 0 base = 0).
    // ============================================================

    private static void BuildStatColumn()
    {
        // Row 0 — Added flat (+10 Max Health while active).
        {
            var z = 0 * RowStep;
            var cell = "Stat0";
            MakeStatActor(cell + "_Actor", new Vector3(StatX, ActorY, z), StatColor, null);
            var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
            var t = timeline.CreateTrack<StatTrack>(null, "Stat");
            AddStatClip(t, 0.5, 3.0, "+10 Added", TargetSlot.Self, maxHealth, StatAuthoringType.Added, 10f);
            FinishCell(timeline, cell, StatX, z,
                "Added +10 (while active)",
                "TimelineEssenceStatClip modifyType=Added value=10 on Max Health (key " + maxHealth.Key +
                "). Added on clip-enter, removed by identity on clip-exit -> Max Health rises +10 then reverts each loop.",
                StatColor, "Stat", cell + "_Actor");
        }

        // Row 1 — Subtracted flat (-5 Max Health while active; bakes negative Added).
        {
            var z = 1 * RowStep;
            var cell = "Stat1";
            MakeStatActor(cell + "_Actor", new Vector3(StatX, ActorY, z), StatColor, null);
            var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
            var t = timeline.CreateTrack<StatTrack>(null, "Stat");
            AddStatClip(t, 0.5, 3.0, "-5 Subtracted", TargetSlot.Self, maxHealth, StatAuthoringType.Subtracted, 5f);
            FinishCell(timeline, cell, StatX, z,
                "Subtracted -5 (while active)",
                "modifyType=Subtracted value=5 (positive in YAML; bake negates -> Added -5) on Max Health -> the stat drops 5 while active, restores on exit (loops).",
                StatColor, "Stat", cell + "_Actor");
        }

        // Row 2 — Increased % (+50% Movement Speed; needs a whole base, seeded).
        {
            var z = 2 * RowStep;
            var cell = "Stat2";
            MakeStatActor(cell + "_Actor", new Vector3(StatX, ActorY, z), StatColor, null);
            var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
            var t = timeline.CreateTrack<StatTrack>(null, "Stat");
            AddStatClip(t, 0.5, 3.0, "+50% Increased", TargetSlot.Self, movementSpeed, StatAuthoringType.Increased, 0.5f);
            FinishCell(timeline, cell, StatX, z,
                "Increased +50% (additive %)",
                "modifyType=Increased value=0.5 on Movement Speed (seeded base Added=10). Increased stacks additively -> MovementSpeed.ValueFloat goes base*1.5 while active (loops).",
                StatColor, "Stat", cell + "_Actor");
        }

        // Row 3 — More x1.75 + Less x0.75 overlapping (multiplicative compounding).
        {
            var z = 3 * RowStep;
            var cell = "Stat3";
            MakeStatActor(cell + "_Actor", new Vector3(StatX, ActorY, z), StatColor, null);
            var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
            var t = timeline.CreateTrack<StatTrack>(null, "Stat");
            AddStatClip(t, 0.5, 3.0, "More x1.75", TargetSlot.Self, attackPower, StatAuthoringType.More, 0.75f);
            AddStatClip(t, 1.5, 2.0, "Less x0.75", TargetSlot.Self, attackPower, StatAuthoringType.Less, 0.25f);
            FinishCell(timeline, cell, StatX, z,
                "More x1.75 then Less x0.75",
                "Two multiplicative clips on Attack Power (seeded base Added=20): More value=0.75 -> x1.75, overlapped by Less value=0.25 -> x0.75. They compound (x1.3125) while overlapping, revert as each exits (loops).",
                StatColor, "Stat", cell + "_Actor");
        }
    }

    // ============================================================
    //  INTRINSIC column (blue) — permanent, clamped counters.
    //  NOTE: intrinsics never revert; the loop re-grants each pass so
    //  the counter ratchets upward over time (until clamp).
    // ============================================================

    private static void BuildIntrinsicColumn()
    {
        // Row 0 — GRANT +5 (Golden Orbs) on enter.
        {
            var z = 0 * RowStep;
            var cell = "Intr0";
            MakeStatActor(cell + "_Actor", new Vector3(IntrinsicX, ActorY, z), IntrinsicColor, null);
            var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
            var t = timeline.CreateTrack<IntrinsicTrack>(null, "Intrinsic");
            AddIntrinsicClip(t, 0.5, 1.0, "+5 Golden Orbs", TargetSlot.Self, goldenOrbs, 5);
            FinishCell(timeline, cell, IntrinsicX, z,
                "Grant +5 (permanent)",
                "TimelineEssenceIntrinsicClip amount=+5 on Golden Orbs (key " + goldenOrbs.Key +
                ") fires on clip-enter only. Permanent -> the counter ratchets +5 every loop (no revert).",
                IntrinsicColor, "Intrinsic", cell + "_Actor");
        }

        // Row 1 — GRANT +5 then CONSUME -2 (later start; ordered after grant).
        {
            var z = 1 * RowStep;
            var cell = "Intr1";
            MakeStatActor(cell + "_Actor", new Vector3(IntrinsicX, ActorY, z), IntrinsicColor, null);
            var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
            var t = timeline.CreateTrack<IntrinsicTrack>(null, "Intrinsic");
            AddIntrinsicClip(t, 0.5, 1.0, "+5 grant", TargetSlot.Self, goldenOrbs, 5);
            AddIntrinsicClip(t, 2.5, 1.0, "-2 consume", TargetSlot.Self, goldenOrbs, -2);
            FinishCell(timeline, cell, IntrinsicX, z,
                "Grant +5 then Consume -2",
                "Two clips on Golden Orbs: +5 then a later -2 (negative amount = consume). Net +3 per loop; the counter accumulates upward in steps (loops).",
                IntrinsicColor, "Intrinsic", cell + "_Actor");
        }

        // Row 2 — COALESCE: two same-start clips summed into ONE Add.
        {
            var z = 2 * RowStep;
            var cell = "Intr2";
            MakeStatActor(cell + "_Actor", new Vector3(IntrinsicX, ActorY, z), IntrinsicColor, null);
            var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
            var t = timeline.CreateTrack<IntrinsicTrack>(null, "Intrinsic");
            AddIntrinsicClip(t, 0.5, 1.0, "+3 Combo", TargetSlot.Self, comboPoints, 3);
            AddIntrinsicClip(t, 0.5, 1.0, "+4 Combo", TargetSlot.Self, comboPoints, 4);
            FinishCell(timeline, cell, IntrinsicX, z,
                "Coalesce +3 & +4 -> +7",
                "Two clips on Combo Points (key " + comboPoints.Key +
                ") share the same start; the system coalesces same-frame same-key amounts into ONE IntrinsicWriter.Add(+7) per loop.",
                IntrinsicColor, "Intrinsic", cell + "_Actor");
        }

        // Row 3 — ROUTE to a Targets slot (lands on a separate target actor).
        {
            var z = 3 * RowStep;
            var cell = "Intr3";
            var targetName = cell + "_Target";
            MakeStatActor(targetName, new Vector3(IntrinsicX + 2.4f, ActorY, z), TargetColor, null);
            MakeStatActor(cell + "_Actor", new Vector3(IntrinsicX - 2.4f, ActorY, z), IntrinsicColor, targetName);
            var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
            var t = timeline.CreateTrack<IntrinsicTrack>(null, "Intrinsic");
            AddIntrinsicClip(t, 0.5, 1.0, "+2 -> Target", TargetSlot.Target, armorStacks, 2);
            FinishCell(timeline, cell, IntrinsicX, z,
                "Route +2 to Target slot",
                "routeTo=Target -> the +2 Armor Stacks (key " + armorStacks.Key +
                ") lands on the RED target actor (Targets.Target), not the bound blue actor. The red one's counter climbs each loop.",
                IntrinsicColor, "Intrinsic", cell + "_Actor");
        }
    }

    // ============================================================
    //  EVENT column (orange) — transient ConditionEvent fires.
    //  Value is NEVER 0 (dev assert). Cleared same frame by Reaction.
    // ============================================================

    private static void BuildEventColumn()
    {
        // Row 0 — FIRE-AT-SELF.
        {
            var z = 0 * RowStep;
            var cell = "Event0";
            MakeStatActor(cell + "_Actor", new Vector3(EventX, ActorY, z), EventColor, null);
            var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
            var t = timeline.CreateTrack<EventTrack>(null, "Event");
            AddEventClip(t, 0.5, 1.0, "fire @ self", TargetSlot.Self, eventGained, 1);
            FinishCell(timeline, cell, EventX, z,
                "Fire-at-self (transient)",
                "TimelineEssenceEventClip routeTo=Self conditionEvent=OnArmorStackGained (key " + eventGained.Key +
                ") value=1 -> a transient ConditionEvent triggers on the bound actor each loop; the Reaction consumer clears it the same frame.",
                EventColor, "Event", cell + "_Actor");
        }

        // Row 1 — ROUTED to Target.
        {
            var z = 1 * RowStep;
            var cell = "Event1";
            var targetName = cell + "_Target";
            MakeStatActor(targetName, new Vector3(EventX + 2.4f, ActorY, z), TargetColor, null);
            MakeStatActor(cell + "_Actor", new Vector3(EventX - 2.4f, ActorY, z), EventColor, targetName);
            var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
            var t = timeline.CreateTrack<EventTrack>(null, "Event");
            AddEventClip(t, 0.5, 1.0, "fire -> Target", TargetSlot.Target, eventHit, 1);
            FinishCell(timeline, cell, EventX, z,
                "Routed to Target slot",
                "routeTo=Target conditionEvent=OnArmorHit (key " + eventHit.Key +
                ") -> the event fires at the RED target actor (Targets.Target), demonstrating routed condition delivery.",
                EventColor, "Event", cell + "_Actor");
        }

        // Row 2 — ACCUMULATE: two same-start clips pre-summed into one trigger.
        {
            var z = 2 * RowStep;
            var cell = "Event2";
            MakeStatActor(cell + "_Actor", new Vector3(EventX, ActorY, z), EventColor, null);
            var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
            var t = timeline.CreateTrack<EventTrack>(null, "Event");
            AddEventClip(t, 0.5, 1.0, "+2", TargetSlot.Self, eventBlaze, 2);
            AddEventClip(t, 0.5, 1.0, "+3", TargetSlot.Self, eventBlaze, 3);
            FinishCell(timeline, cell, EventX, z,
                "Accumulate +2 & +3 -> value 5",
                "Two same-start clips on OnBlazeExplosion (key " + eventBlaze.Key +
                "); same-frame (receiver,key) values are pre-summed -> ONE ConditionEventWriter.Trigger with value 5 per loop (never sums to 0).",
                EventColor, "Event", cell + "_Actor");
        }
    }

    // ============================================================
    //  actor + clip builders
    // ============================================================

    private static GameObject MakeStatActor(string name, Vector3 pos, Color color, string targetName)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, color);

        go.AddComponent<LifeCycleAuthoring>();

        var stats = go.AddComponent<StatAuthoring>();
        stats.AddStats = true;
        stats.StatsCanBeModified = true;
        stats.AddIntrinsics = true;
        stats.StatDefaults = new[]
        {
            new StatModifierAuthoring { Stat = maxHealth, ModifyType = StatAuthoringType.Added, Value = 100f },
            new StatModifierAuthoring { Stat = movementSpeed, ModifyType = StatAuthoringType.Added, Value = 10f },
            new StatModifierAuthoring { Stat = attackPower, ModifyType = StatAuthoringType.Added, Value = 20f },
        };

        var targets = go.AddComponent<TargetsAuthoring>();
        targets.Owner = go;
        targets.Source = go;
        targets.Custom = go;
        targets.Target = targetName != null ? GameObject.Find(targetName) : go;

        SceneManager.MoveGameObjectToScene(go, activeSub);
        return go;
    }

    private static TimelineClip AddStatClip(TrackAsset t, double start, double dur, string name,
        TargetSlot routeTo, StatSchemaObject stat, StatAuthoringType modify, float value)
    {
        var c = AddClip<StatClip>(t, start, dur, name);
        var a = (StatClip)c.asset;
        a.routeTo = routeTo;
        a.routeLink = null;
        a.stat = stat;
        a.modifyType = modify;
        a.value = value;
        Dirty(c.asset);
        return c;
    }

    private static TimelineClip AddIntrinsicClip(TrackAsset t, double start, double dur, string name,
        TargetSlot routeTo, IntrinsicSchemaObject intrinsic, int amount)
    {
        var c = AddClip<IntrinsicClip>(t, start, dur, name);
        var a = (IntrinsicClip)c.asset;
        a.routeTo = routeTo;
        a.routeLink = null;
        a.intrinsic = intrinsic;
        a.amount = amount;
        Dirty(c.asset);
        return c;
    }

    private static TimelineClip AddEventClip(TrackAsset t, double start, double dur, string name,
        TargetSlot routeTo, ConditionEventObject conditionEvent, int value)
    {
        var c = AddClip<EventClip>(t, start, dur, name);
        var a = (EventClip)c.asset;
        a.routeTo = routeTo;
        a.routeLink = null;
        a.conditionEvent = conditionEvent;
        a.value = value;
        Dirty(c.asset);
        return c;
    }

    // ============================================================
    //  wire / caption plumbing
    // ============================================================

    private static void FinishCell(TimelineAsset timeline, string cell, float x, float z,
        string label, string usage, Color color, string trackName, string actorName)
    {
        FixDuration(timeline);
        Dirty(timeline);
        foreach (var tr in timeline.GetOutputTracks()) Dirty(tr);
        AssetDatabase.SaveAssets();

        var dirName = cell + "_Director";
        MakeDirector(dirName);
        Wires.Add(new CellWire
        {
            DirectorName = dirName,
            TimelinePath = AssetDatabase.GetAssetPath(timeline),
            Binds = new List<TrackBind> { new TrackBind { TrackName = trackName, BindActorName = actorName } },
        });
        Captions.Add(new CaptionData { Title = label, Usage = usage, CellPos = new Vector3(x, 3.8f, z), Color = color });
    }

    private static void WireCell(CellWire w)
    {
        var director = GameObject.Find(w.DirectorName).GetComponent<PlayableDirector>();
        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(w.TimelinePath);
        director.playableAsset = timeline;

        foreach (var track in timeline.GetOutputTracks())
        {
            var bind = FindBind(w, track.name);
            if (bind == null) continue;
            var actor = GameObject.Find(bind.BindActorName);
            director.SetGenericBinding(track, actor.GetComponent<TargetsAuthoring>());
        }

        EditorUtility.SetDirty(director);
    }

    private static TrackBind FindBind(CellWire w, string trackName)
    {
        foreach (var b in w.Binds)
            if (b.TrackName == trackName)
                return b;
        return null;
    }

    private static PlayableDirector MakeDirector(string name)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, activeSub);
        var director = go.AddComponent<PlayableDirector>();
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Loop;
        var begin = go.AddComponent<TimelineBeginAuthoring>();
        begin.Mode = TimelineBeginMode.OnLoad;
        begin.DelaySeconds = 0f;
        return director;
    }

    private static TimelineAsset NewTimeline(string path)
    {
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, path);
        return timeline;
    }

    private static TimelineClip AddClip<T>(TrackAsset track, double start, double duration, string name) where T : PlayableAsset
    {
        var clip = track.CreateClip<T>();
        clip.start = start;
        clip.duration = duration;
        clip.displayName = name;
        return clip;
    }

    private static void FixDuration(TimelineAsset timeline)
    {
        var end = 0.0;
        foreach (var track in timeline.GetOutputTracks())
            foreach (var clip in track.GetClips())
            {
                var clipEnd = clip.start + clip.duration;
                if (clipEnd > end) end = clipEnd;
            }

        // Pad so the loop has dead-time where clips are inactive -> stat reverts visibly,
        // and same-frame coalesce/accumulate clips re-fire cleanly each pass.
        end += 1.5;
        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.fixedDuration = end;
    }

    // ============================================================
    //  primitives / parent scene
    // ============================================================

    private static GameObject MakePad(string name, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = size;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, PadColor);
        SceneManager.MoveGameObjectToScene(go, activeSub);
        return go;
    }

    // The Essence intrinsic & event runtime facets need the EssenceConfig +
    // condition-write singletons baked into the world. Those come from
    // SettingsAuthoring on the project's "Required In Subscene" prefab. Without
    // it, stat modifiers still apply (buffer-direct) but intrinsic/event writes
    // are silently dropped (IntrinsicWriter.Lookup.TryGet fails). Instantiate it.
    private static void BuildRequiredInSubScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RequiredInSubScenePath);
        if (prefab == null)
        {
            Debug.LogError("EssenceShowcase: '" + RequiredInSubScenePath + "' missing; intrinsic/event writes will be dropped (no EssenceConfig).");
            return;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = "Required In Subscene";
        SceneManager.MoveGameObjectToScene(go, activeSub);
    }

    private static void BuildPads()
    {
        float[] xs = { StatX, IntrinsicX, EventX };
        string[] names = { "Stat", "Intrinsic", "Event" };
        var zCenter = RowStep * 1.5f;
        for (var i = 0; i < xs.Length; i++)
            MakePad(names[i] + "_Pad", new Vector3(xs[i], 0.05f, zCenter), new Vector3(10.0f, 0.12f, RowStep * 4f + 2f));
    }

    private static Material MakeMaterial(string name, Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = name + "_Mat" };
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        return mat;
    }

    private static void BuildParent()
    {
        FrameCamera();
        RenderSettings.fog = false;

        MakeBanner("Title_Banner", new Vector3(0f, 14.6f, 0f), new Vector3(50f, 3.4f, 0.1f));
        MakeWorldLabel("Title", "ESSENCE TIMELINE GRID — STAT · INTRINSIC · EVENT", new Vector3(0f, 15.0f, -0.4f), 50f, Color.white, 4.6f, TextAlignmentOptions.Center);
        MakeWorldLabel("Subtitle", "3 tracks · 11 clips driving NUMERIC stat/intrinsic/event values   ·   com.bovinelabs.timeline.essence", new Vector3(0f, 13.7f, -0.4f), 50f, new Color(0.85f, 0.9f, 1f), 1.9f, TextAlignmentOptions.Center);

        MakeColumnHeader("Stat_Header", "TIMELINE STAT", StatX, StatColor);
        MakeColumnHeader("Intrinsic_Header", "TIMELINE INTRINSIC", IntrinsicX, IntrinsicColor);
        MakeColumnHeader("Event_Header", "TIMELINE EVENT", EventX, EventColor);

        foreach (var cap in Captions)
            MakeCaption(cap.Title, cap.Usage, cap.CellPos, cap.Color);

        MakeBanner("Usage_Banner", new Vector3(0f, 0.7f, -8.0f), new Vector3(56f, 2.2f, 0.1f));
        MakeWorldLabel("Usage",
            "Capsules carry StatAuthoring (AddStats + StatsCanBeModified + AddIntrinsics) with whole-number Added bases seeded so % modifiers are visible. Effects are NUMERIC (stat/intrinsic buffer values), not transform motion: STAT modifiers rise & self-revert each loop; INTRINSIC counters ratchet up permanently; EVENTS fire transient ConditionEvents. FixedLength + Loop.",
            new Vector3(0f, 0.7f, -8.3f), 54f, new Color(0.96f, 0.97f, 1f), 1.45f, TextAlignmentOptions.Center);

        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SubPath);
        if (sceneAsset == null)
        {
            Debug.LogError("EssenceShowcase: sub-scene asset missing at " + SubPath);
            return;
        }

        var subSceneGo = new GameObject("Showcase SubScene");
        var subScene = subSceneGo.AddComponent<SubScene>();
        subScene.SceneAsset = sceneAsset;
        subScene.AutoLoadScene = true;
        EditorUtility.SetDirty(subScene);
    }

    private static void MakeColumnHeader(string name, string text, float x, Color color)
    {
        var pos = new Vector3(x, 4.6f, -4.5f);
        MakeBanner(name + "_Banner", pos + new Vector3(0f, 0f, 0.08f), new Vector3(9.4f, 1.4f, 0.1f));
        MakeWorldLabel(name, "<b>" + text + "</b>", pos, 9.2f, color, 2.8f, TextAlignmentOptions.Center);
    }

    private static float CaptionY(float z)
    {
        return 4.6f + z * 0.14f;
    }

    private static void MakeCaption(string title, string usage, Vector3 cellPos, Color color)
    {
        var z = cellPos.z;
        var y = CaptionY(z);
        MakeBanner("CapBanner_" + title + "_" + z, new Vector3(cellPos.x, y, z + 0.06f), new Vector3(9.0f, 2.2f, 0.05f));
        MakeWorldLabel("Cap_" + title + "_" + z, "<b>" + title + "</b>", new Vector3(cellPos.x, y + 0.55f, z), 9.0f, color, 2.3f, TextAlignmentOptions.Center);
        MakeWorldLabel("Use_" + title + "_" + z, usage, new Vector3(cellPos.x, y - 0.45f, z), 9.0f, new Color(0.95f, 0.96f, 1f), 1.15f, TextAlignmentOptions.Center);
    }

    private static void FrameCamera()
    {
        var required = GameObject.Find("Required In Scene");
        if (required == null) return;
        var camTransform = required.transform.Find("Main Camera");
        if (camTransform == null) return;
        camTransform.position = CameraPos;
        camTransform.rotation = Quaternion.Euler(20f, 0f, 0f);
        var cam = camTransform.GetComponent<Camera>();
        if (cam != null)
        {
            cam.fieldOfView = 60f;
            cam.farClipPlane = 400f;
            EditorUtility.SetDirty(cam);
        }

        EditorUtility.SetDirty(camTransform);
    }

    private static void MakeBanner(string name, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(pos - CameraPos, Vector3.up);
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, BannerColor);
    }

    private static void MakeWorldLabel(string name, string text, Vector3 pos, float width, Color color, float fontSize, TextAlignmentOptions alignment)
    {
        var holder = new GameObject(name);
        holder.transform.position = pos;
        holder.transform.rotation = Quaternion.LookRotation(pos - CameraPos, Vector3.up);

        var go = new GameObject("Text");
        go.transform.SetParent(holder.transform, false);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.rectTransform.sizeDelta = new Vector2(width, 4f);
        tmp.rectTransform.localPosition = Vector3.zero;
        tmp.fontStyle = FontStyles.Bold;
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Samples"))
            AssetDatabase.CreateFolder("Assets", "Samples");
        if (!AssetDatabase.IsValidFolder(SampleFolder))
            AssetDatabase.CreateFolder("Assets/Samples", "EssenceShowcase");
        if (!AssetDatabase.IsValidFolder(TimelineFolder))
            AssetDatabase.CreateFolder(SampleFolder, "Timelines");
    }

    private static void ResetAssets()
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(TimelineFolder) != null)
            foreach (var guid in AssetDatabase.FindAssets("t:TimelineAsset", new[] { TimelineFolder }))
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));

        foreach (var p in new[] { ParentPath, SubPath })
            if (AssetDatabase.LoadAssetAtPath<Object>(p) != null)
                AssetDatabase.DeleteAsset(p);
    }

    private static void Dirty(params Object[] objects)
    {
        foreach (var o in objects)
            EditorUtility.SetDirty(o);
    }
}
