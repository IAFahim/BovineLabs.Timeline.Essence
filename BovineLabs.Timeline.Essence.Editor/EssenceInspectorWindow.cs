// <copyright file="EssenceInspectorWindow.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Timeline.Essence.Editor
{
    using System;
    using System.Collections.Generic;
    using BovineLabs.Core.Iterators;
    using BovineLabs.Essence.Authoring;
    using BovineLabs.Essence.Data;
    using BovineLabs.Reaction.Authoring.Core;
    using BovineLabs.Reaction.Data.Core;
    using BovineLabs.Timeline.Core.Editor;
    using Unity.Entities;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Companion window that de-black-boxes the Essence Stats + Reaction systems for designers: select an actor and
    /// it shows the resolved stat values (the ×100 / Added·Increased·More math worked out), intrinsic ranges/clamps,
    /// every reaction in plain English, live runtime values in play mode, and a permanent legend. Read-only — it
    /// never edits, and never replaces the package's own component inspectors.
    /// </summary>
    public sealed class EssenceInspectorWindow : EditorWindow
    {
        private ScrollView body;

        [MenuItem("Tools/BovineLabs/Essence Inspector")]
        public static void Open()
        {
            GetWindow<EssenceInspectorWindow>("Essence Inspector").Show();
        }

        private void CreateGUI()
        {
            this.body = new ScrollView();
            this.rootVisualElement.Add(this.body);
            this.Rebuild();

            // Live values: rebuild on a slow tick, but only while playing (no edit-mode flicker).
            this.rootVisualElement.schedule.Execute(() =>
            {
                if (Application.isPlaying)
                {
                    this.Rebuild();
                }
            }).Every(500);
        }

        private void OnEnable()
        {
            Selection.selectionChanged += this.Rebuild;
            EditorApplication.playModeStateChanged += this.OnPlayModeChanged;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= this.Rebuild;
            EditorApplication.playModeStateChanged -= this.OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange _)
        {
            this.Rebuild();
        }

        private void Rebuild()
        {
            if (this.body == null)
            {
                return;
            }

            this.body.Clear();

            var go = Selection.activeGameObject;
            if (go == null)
            {
                this.body.Add(Muted("Select a GameObject with StatAuthoring / ReactionAuthoring."));
                this.body.Add(BuildLegend());
                return;
            }

            // Header
            var header = Row();
            var title = new Label(go.name) { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1 } };
            header.Add(title);
            header.Add(EditorInspect.CreateButton(() => go, "◎", "Open this object's Properties window."));
            this.body.Add(header);

            var stat = go.GetComponent<StatAuthoring>();
            var reaction = go.GetComponent<ReactionAuthoring>();

            if (stat == null && reaction == null)
            {
                this.body.Add(Muted("No StatAuthoring or ReactionAuthoring on this object."));
            }

            TrySection("Stats", stat != null, () => this.BuildStats(stat, go));
            TrySection("Intrinsics", stat != null, () => this.BuildIntrinsics(stat, go));
            TrySection("Reactions", reaction != null, () => this.BuildReactions(reaction));

            this.body.Add(BuildLegend());
        }

        private void TrySection(string title, bool show, Func<VisualElement> build)
        {
            if (!show)
            {
                return;
            }

            var foldout = Section(title);
            try
            {
                foldout.Add(build());
            }
            catch (Exception e)
            {
                foldout.Add(Muted($"({title} unavailable: {e.GetType().Name})"));
            }

            this.body.Add(foldout);
        }

        // ---- Stats -------------------------------------------------------------------------------------------

        private VisualElement BuildStats(StatAuthoring stat, GameObject go)
        {
            var container = new VisualElement();
            container.Add(Muted($"AddStats={stat.AddStats}   CanBeModified={stat.StatsCanBeModified}"));

            var hasLive = TryGetLiveStats(go, out var liveStats);

            // Group modifiers by stat schema and resolve Value = Σadded × (1+Σincreased) × Π(1+more).
            var groups = GroupStatModifiers(stat);
            if (groups.Count == 0)
            {
                container.Add(Muted("(no stat defaults)"));
            }

            foreach (var kvp in groups)
            {
                var value = ResolveStatValue(kvp.Value);

                var line = $"{kvp.Key.name} = {value:0.##}  (×{value / StatValue.ToInt:0.00})";
                if (hasLive && liveStats.TryGetValue((StatKey)kvp.Key, out var live))
                {
                    line += $"   ▶ live {live.Value:0.##}";
                }

                container.Add(Bold(line));
                foreach (var m in kvp.Value)
                {
                    container.Add(Muted($"    · {m.ModifyType} {m.Value:0.###}"));
                }
            }

            return container;
        }

        private static Dictionary<StatSchemaObject, List<StatModifierAuthoring>> GroupStatModifiers(StatAuthoring stat)
        {
            var groups = new Dictionary<StatSchemaObject, List<StatModifierAuthoring>>();
            foreach (var m in stat.StatDefaults)
            {
                if (m?.Stat == null)
                {
                    continue;
                }

                if (!groups.TryGetValue(m.Stat, out var list))
                {
                    groups[m.Stat] = list = new List<StatModifierAuthoring>();
                }

                list.Add(m);
            }

            return groups;
        }

        private static float ResolveStatValue(List<StatModifierAuthoring> modifiers)
        {
            var added = 0f;
            var increased = 0f;
            var more = 1f;
            foreach (var m in modifiers)
            {
                switch (m.ModifyType)
                {
                    case StatAuthoringType.Added: added += m.Value; break;
                    case StatAuthoringType.Subtracted: added -= m.Value; break;
                    case StatAuthoringType.Increased: increased += m.Value; break;
                    case StatAuthoringType.Reduced: increased -= m.Value; break;
                    case StatAuthoringType.More: more *= 1f + m.Value; break;
                    case StatAuthoringType.Less: more *= 1f - m.Value; break;
                }
            }

            return added * (1f + increased) * more;
        }

        // ---- Intrinsics --------------------------------------------------------------------------------------

        private VisualElement BuildIntrinsics(StatAuthoring stat, GameObject go)
        {
            var container = new VisualElement();
            var hasLive = TryGetLiveIntrinsics(go, out var live);

            if (stat.IntrinsicDefaults.Length == 0)
            {
                container.Add(Muted("(no intrinsic defaults)"));
            }

            foreach (var d in stat.IntrinsicDefaults)
            {
                if (d?.Intrinsic == null)
                {
                    continue;
                }

                var schema = d.Intrinsic;
                var range = schema.Range;

                var line = $"{schema.name} = {d.Value}   range [{range.x},{range.y}]{BuildClampSuffix(schema)}";
                if (hasLive && live.TryGetValue((IntrinsicKey)schema, out var liveVal))
                {
                    line += $"   ▶ live {liveVal}";
                }

                container.Add(Bold(line));
                if (d.Value < range.x || d.Value > range.y)
                {
                    container.Add(Warn($"    ⚠ default {d.Value} is outside range [{range.x},{range.y}] — will be clamped."));
                }
            }

            return container;
        }

        private static string BuildClampSuffix(IntrinsicSchemaObject schema)
        {
            if (schema.MinStat == null && schema.MaxStat == null)
            {
                return string.Empty;
            }

            return $"  clamp[{(schema.MinStat != null ? schema.MinStat.name : "-")}..{(schema.MaxStat != null ? schema.MaxStat.name : "-")}]";
        }

        // ---- Reactions ---------------------------------------------------------------------------------------

        private VisualElement BuildReactions(ReactionAuthoring reaction)
        {
            var container = new VisualElement();
            var so = new SerializedObject(reaction);

            container.Add(Bold(BuildTimingLine(so)));
            this.AddChanceAndExpression(container, so);
            this.AddConditions(container, so);
            this.AddActions(container, reaction);

            return container;
        }

        private static string BuildTimingLine(SerializedObject so)
        {
            var cooldown = so.FindProperty("Active.cooldown");
            var duration = so.FindProperty("Active.duration");
            var cancellable = so.FindProperty("Active.cancellable");
            var trigger = so.FindProperty("Active.trigger");
            var timing = trigger != null && trigger.boolValue ? "WHEN triggered" : "WHEN conditions hold";

            var bits = new List<string>();
            if (cooldown != null && cooldown.floatValue > 0)
            {
                bits.Add($"cooldown {cooldown.floatValue:0.###}s");
            }

            if (duration != null && duration.floatValue > 0)
            {
                bits.Add($"active {duration.floatValue:0.###}s{(cancellable != null && cancellable.boolValue ? " (cancellable)" : string.Empty)}");
            }

            return timing + (bits.Count > 0 ? "  ·  " + string.Join(", ", bits) : string.Empty);
        }

        private void AddChanceAndExpression(VisualElement container, SerializedObject so)
        {
            var chance = so.FindProperty("Conditions.chanceToTrigger");
            if (chance != null && chance.floatValue < 1f)
            {
                container.Add(Muted($"    chance {chance.floatValue * 100f:0.#}%"));
            }

            var expr = so.FindProperty("Conditions.conditionLogic.expression");
            if (expr != null && !string.IsNullOrEmpty(expr.stringValue))
            {
                container.Add(Muted($"    composite: {expr.stringValue}"));
            }
        }

        private void AddConditions(VisualElement container, SerializedObject so)
        {
            var conditions = so.FindProperty("Conditions.conditions");
            if (conditions == null || conditions.arraySize == 0)
            {
                container.Add(Muted("    (no conditions — always)"));
                return;
            }

            for (var i = 0; i < conditions.arraySize; i++)
            {
                container.Add(this.BuildCondition(conditions.GetArrayElementAtIndex(i), i));
            }
        }

        private void AddActions(VisualElement container, ReactionAuthoring reaction)
        {
            container.Add(Bold("THEN"));
            var any = false;
            foreach (var c in reaction.GetComponents<MonoBehaviour>())
            {
                if (c == null || !c.GetType().Name.StartsWith("Action", StringComparison.Ordinal))
                {
                    continue;
                }

                any = true;
                var comp = c;
                var row = Row();
                row.Add(new Label($"    · {Pretty(comp.GetType().Name)}") { style = { flexGrow = 1 } });
                row.Add(EditorInspect.CreateButton(() => comp, "◎", "Open this action."));
                container.Add(row);
            }

            if (!any)
            {
                container.Add(Muted("    (no Action* components — add one to do something)"));
            }
        }

        private VisualElement BuildCondition(SerializedProperty cond, int index)
        {
            var schema = cond.FindPropertyRelative("Condition")?.objectReferenceValue;
            var name = schema != null ? schema.name : "(no condition)";
            var target = EnumVal<Target>(cond.FindPropertyRelative("Target"));
            var op = EnumVal<Equality>(cond.FindPropertyRelative("Operation"));
            var custom = cond.FindPropertyRelative("ComparisonMode")?.enumValueIndex == 1; // 0=Constant, 1=Custom

            string value;
            string warn = null;
            if (custom)
            {
                var statProp = cond.FindPropertyRelative("CustomValue")?.FindPropertyRelative("stat");
                var statRef = statProp?.objectReferenceValue;
                value = statRef != null ? $"Stat:{statRef.name}" : "Custom";
                warn = "⚠ custom-stat threshold resolves to 0 until that stat changes at runtime — use a Constant for a static threshold.";
            }
            else
            {
                var v = cond.FindPropertyRelative("Value");
                value = v != null ? v.intValue.ToString() : "?";
            }

            var line = $"    [{index}] {name} · {target} {Symbol(op)} {value}";
            var ve = new VisualElement();
            ve.Add(new Label(line));
            if (warn != null)
            {
                ve.Add(Warn("        " + warn));
            }

            return ve;
        }

        // ---- Live reads (best-effort; never throw) -----------------------------------------------------------

        private static bool TryGetLiveStats(GameObject go, out DynamicHashMap<StatKey, StatValue> map)
        {
            map = default;
            if (!TryGetEntity(go, out var em, out var e) || !em.HasBuffer<Stat>(e))
            {
                return false;
            }

            map = em.GetBuffer<Stat>(e).AsMap();
            return true;
        }

        private static bool TryGetLiveIntrinsics(GameObject go, out DynamicHashMap<IntrinsicKey, int> map)
        {
            map = default;
            if (!TryGetEntity(go, out var em, out var e) || !em.HasBuffer<Intrinsic>(e))
            {
                return false;
            }

            map = em.GetBuffer<Intrinsic>(e).AsMap();
            return true;
        }

        // Resolve the selected GameObject -> its baked Entity via the authoritative bake-time authoring mapping
        // (EntityGuid / originating instance id), NOT by debug-name. Names are not unique (prefab instances,
        // many actors share 'Enemy'/'Player'), and query order is not stable across runs — name-matching would
        // attribute another actor's live stats to this one and flip between runs. When more than one Essence-carrying
        // entity maps back to this exact GameObject the result is genuinely ambiguous, so we bail rather than guess.
        private static bool TryGetEntity(GameObject go, out EntityManager em, out Entity entity)
        {
            em = default;
            entity = Entity.Null;
            try
            {
                if (!Application.isPlaying)
                {
                    return false;
                }

                var world = World.DefaultGameObjectInjectionWorld;
                if (world is not { IsCreated: true })
                {
                    return false;
                }

                em = world.EntityManager;
                using var candidates = new Unity.Collections.NativeList<Entity>(Unity.Collections.Allocator.Temp);
                em.Debug.GetEntitiesForAuthoringObject(go, candidates);

                foreach (var e in candidates)
                {
                    if (!em.HasBuffer<Stat>(e) && !em.HasBuffer<Intrinsic>(e))
                    {
                        continue;
                    }

                    if (entity != Entity.Null)
                    {
                        // Multiple Essence-carrying entities map to this GameObject — ambiguous, do not silently pick one.
                        entity = Entity.Null;
                        return false;
                    }

                    entity = e;
                }

                return entity != Entity.Null;
            }
            catch
            {
                return false;
            }
        }

        // ---- Legend ------------------------------------------------------------------------------------------

        private static VisualElement BuildLegend()
        {
            var f = Section("Legend / cheat-sheet");
            f.value = false;
            void L(string s) => f.Add(Muted(s));

            L("STATS: Value = Σ(Added) × (1 + Σ Increased) × Π(1 + More).  100 = 1.0× (StatValue.ToInt).");
            L("  Added/Subtracted = flat ±.  Increased/Reduced = ± percent (0.1 = 10%).  More/Less = × multiplier.");
            L("INTRINSICS: live integer counter, clamped to the schema range; optional MinStat/MaxStat clamp.");
            L("CONDITIONS: op symbols — == != > >= < <= , 'in' = Between, (any) = exists.");
            L("  Constant = fixed threshold (bakes the number).  Custom = compare to a live Stat/Intrinsic.");
            L("  ⚠ Custom-vs-static-stat resolves to 0 until the stat changes — prefer Constant for fixed thresholds.");
            L("  Features: Condition = gates active; Value = records value; Accumulate = both, per frame.");
            L("KEYS/IDS auto-assign on import; id 0 = unusable (re-import). Registries: EssenceSettings, ReactionSettings.");
            return f;
        }

        // ---- helpers -----------------------------------------------------------------------------------------

        private static T EnumVal<T>(SerializedProperty p)
            where T : struct, Enum
        {
            if (p == null)
            {
                return default;
            }

            var values = (T[])Enum.GetValues(typeof(T));
            var i = p.enumValueIndex;
            return i >= 0 && i < values.Length ? values[i] : default;
        }

        private static string Symbol(Equality op) => op switch
        {
            Equality.Equal => "==",
            Equality.NotEqual => "!=",
            Equality.GreaterThan => ">",
            Equality.GreaterThanEqual => ">=",
            Equality.LessThan => "<",
            Equality.LessThanEqual => "<=",
            Equality.Between => "in",
            _ => "(any)",
        };

        private static string Pretty(string typeName)
        {
            return typeName.EndsWith("Authoring", StringComparison.Ordinal)
                ? typeName.Substring(0, typeName.Length - "Authoring".Length)
                : typeName;
        }

        private static Foldout Section(string title)
        {
            var f = new Foldout { text = title, value = true };
            f.style.marginTop = 4;
            f.style.unityFontStyleAndWeight = FontStyle.Bold;
            return f;
        }

        private static VisualElement Row()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.alignItems = Align.Center;
            return r;
        }

        private static Label Bold(string s)
        {
            var l = new Label(s);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        private static Label Muted(string s)
        {
            var l = new Label(s) { style = { opacity = 0.7f } };
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        private static Label Warn(string s)
        {
            var l = new Label(s);
            l.style.color = new Color(1f, 0.7f, 0.2f);
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }
    }
}
