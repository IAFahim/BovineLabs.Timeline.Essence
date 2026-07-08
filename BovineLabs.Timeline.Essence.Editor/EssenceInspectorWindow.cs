using System;
using System.Collections.Generic;
using BovineLabs.Core.Iterators;
using BovineLabs.Essence.Authoring;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Core.Editor;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BovineLabs.Timeline.Essence.Editor
{
    public sealed class EssenceInspectorWindow : EditorWindow
    {
        private ScrollView body;

        private void OnEnable()
        {
            Selection.selectionChanged += Rebuild;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Rebuild;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void CreateGUI()
        {
            body = new ScrollView();
            rootVisualElement.Add(body);
            Rebuild();

            rootVisualElement.schedule.Execute(() =>
            {
                if (Application.isPlaying) Rebuild();
            }).Every(500);
        }

        [MenuItem("Tools/BovineLabs/Essence Inspector")]
        public static void Open()
        {
            GetWindow<EssenceInspectorWindow>("Essence Inspector").Show();
        }

        private void OnPlayModeChanged(PlayModeStateChange _)
        {
            Rebuild();
        }

        private void Rebuild()
        {
            if (body == null) return;

            body.Clear();

            var go = Selection.activeGameObject;
            if (go == null)
            {
                body.Add(Muted("Select a GameObject with StatAuthoring / ReactionAuthoring."));
                body.Add(BuildLegend());
                return;
            }

            var header = Row();
            var title = new Label(go.name) { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1 } };
            header.Add(title);
            header.Add(EditorInspect.CreateButton(() => go, "◎", "Open this object's Properties window."));
            body.Add(header);

            var stat = go.GetComponent<StatAuthoring>();
            var reaction = go.GetComponent<ReactionAuthoring>();

            if (stat == null && reaction == null)
                body.Add(Muted("No StatAuthoring or ReactionAuthoring on this object."));

            TrySection("Stats", stat != null, () => BuildStats(stat, go));
            TrySection("Intrinsics", stat != null, () => BuildIntrinsics(stat, go));
            TrySection("Reactions", reaction != null, () => BuildReactions(reaction));

            body.Add(BuildLegend());
        }

        private void TrySection(string title, bool show, Func<VisualElement> build)
        {
            if (!show) return;

            var foldout = Section(title);
            try
            {
                foldout.Add(build());
            }
            catch (Exception e)
            {
                foldout.Add(Muted($"({title} unavailable: {e.GetType().Name})"));
            }

            body.Add(foldout);
        }

        private VisualElement BuildStats(StatAuthoring stat, GameObject go)
        {
            var container = new VisualElement();
            container.Add(Muted($"AddStats={stat.AddStats}   CanBeModified={stat.StatsCanBeModified}"));

            var hasLive = TryGetLiveStats(go, out var liveStats);

            var groups = GroupStatModifiers(stat);
            if (groups.Count == 0) container.Add(Muted("(no stat defaults)"));

            foreach (var kvp in groups)
            {
                var value = ResolveStatValue(kvp.Value);

                var line = $"{kvp.Key.name} = {value:0.##}  (×{value / StatValue.ToInt:0.00})";
                if (hasLive && liveStats.TryGetValue((StatKey)kvp.Key, out var live))
                    line += $"   ▶ live {live.Value:0.##}";

                container.Add(Bold(line));
                foreach (var m in kvp.Value) container.Add(Muted($"    · {m.ModifyType} {m.Value:0.###}"));
            }

            return container;
        }

        private static Dictionary<StatSchemaObject, List<StatModifierAuthoring>> GroupStatModifiers(StatAuthoring stat)
        {
            var groups = new Dictionary<StatSchemaObject, List<StatModifierAuthoring>>();
            foreach (var m in stat.StatDefaults)
            {
                if (m?.Stat == null) continue;

                if (!groups.TryGetValue(m.Stat, out var list))
                    groups[m.Stat] = list = new List<StatModifierAuthoring>();

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
                switch (m.ModifyType)
                {
                    case StatAuthoringType.Added: added += m.Value; break;
                    case StatAuthoringType.Subtracted: added -= m.Value; break;
                    case StatAuthoringType.Increased: increased += m.Value; break;
                    case StatAuthoringType.Reduced: increased -= m.Value; break;
                    case StatAuthoringType.More: more *= 1f + m.Value; break;
                    case StatAuthoringType.Less: more *= 1f - m.Value; break;
                }

            return added * (1f + increased) * more;
        }

        private VisualElement BuildIntrinsics(StatAuthoring stat, GameObject go)
        {
            var container = new VisualElement();
            var hasLive = TryGetLiveIntrinsics(go, out var live);

            if (stat.IntrinsicDefaults.Length == 0) container.Add(Muted("(no intrinsic defaults)"));

            foreach (var d in stat.IntrinsicDefaults)
            {
                if (d?.Intrinsic == null) continue;

                var schema = d.Intrinsic;
                var range = schema.Range;

                var line = $"{schema.name} = {d.Value}   range [{range.x},{range.y}]{BuildClampSuffix(schema)}";
                if (hasLive && live.TryGetValue((IntrinsicKey)schema, out var liveVal)) line += $"   ▶ live {liveVal}";

                container.Add(Bold(line));
                if (d.Value < range.x || d.Value > range.y)
                    container.Add(
                        Warn($"    ⚠ default {d.Value} is outside range [{range.x},{range.y}] — will be clamped."));
            }

            return container;
        }

        private static string BuildClampSuffix(IntrinsicSchemaObject schema)
        {
            if (schema.MinStat == null && schema.MaxStat == null) return string.Empty;

            return
                $"  clamp[{(schema.MinStat != null ? schema.MinStat.name : "-")}..{(schema.MaxStat != null ? schema.MaxStat.name : "-")}]";
        }

        private VisualElement BuildReactions(ReactionAuthoring reaction)
        {
            var container = new VisualElement();
            var so = new SerializedObject(reaction);

            container.Add(Bold(BuildTimingLine(so)));
            AddChanceAndExpression(container, so);
            AddConditions(container, so);
            AddActions(container, reaction);

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
            if (cooldown != null && cooldown.floatValue > 0) bits.Add($"cooldown {cooldown.floatValue:0.###}s");

            if (duration != null && duration.floatValue > 0)
                bits.Add(
                    $"active {duration.floatValue:0.###}s{(cancellable != null && cancellable.boolValue ? " (cancellable)" : string.Empty)}");

            return timing + (bits.Count > 0 ? "  ·  " + string.Join(", ", bits) : string.Empty);
        }

        private void AddChanceAndExpression(VisualElement container, SerializedObject so)
        {
            var chance = so.FindProperty("Conditions.chanceToTrigger");
            if (chance != null && chance.floatValue < 1f)
                container.Add(Muted($"    chance {chance.floatValue * 100f:0.#}%"));

            var expr = so.FindProperty("Conditions.conditionLogic.expression");
            if (expr != null && !string.IsNullOrEmpty(expr.stringValue))
                container.Add(Muted($"    composite: {expr.stringValue}"));
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
                container.Add(BuildCondition(conditions.GetArrayElementAtIndex(i), i));
        }

        private void AddActions(VisualElement container, ReactionAuthoring reaction)
        {
            container.Add(Bold("THEN"));
            var any = false;
            foreach (var c in reaction.GetComponents<MonoBehaviour>())
            {
                if (c == null || !c.GetType().Name.StartsWith("Action", StringComparison.Ordinal)) continue;

                any = true;
                var comp = c;
                var row = Row();
                row.Add(new Label($"    · {Pretty(comp.GetType().Name)}") { style = { flexGrow = 1 } });
                row.Add(EditorInspect.CreateButton(() => comp, "◎", "Open this action."));
                container.Add(row);
            }

            if (!any) container.Add(Muted("    (no Action* components — add one to do something)"));
        }

        private VisualElement BuildCondition(SerializedProperty cond, int index)
        {
            var schema = cond.FindPropertyRelative("Condition")?.objectReferenceValue;
            var name = schema != null ? schema.name : "(no condition)";
            var target = EnumVal<Target>(cond.FindPropertyRelative("Target"));
            var op = EnumVal<Equality>(cond.FindPropertyRelative("Operation"));
            var custom = cond.FindPropertyRelative("ComparisonMode")?.enumValueIndex == 1;

            string value;
            string warn = null;
            if (custom)
            {
                var statProp = cond.FindPropertyRelative("CustomValue")?.FindPropertyRelative("stat");
                var statRef = statProp?.objectReferenceValue;
                value = statRef != null ? $"Stat:{statRef.name}" : "Custom";
                warn =
                    "⚠ custom-stat threshold resolves to 0 until that stat changes at runtime — use a Constant for a static threshold.";
            }
            else
            {
                var v = cond.FindPropertyRelative("Value");
                value = v != null ? v.intValue.ToString() : "?";
            }

            var line = $"    [{index}] {name} · {target} {Symbol(op)} {value}";
            var ve = new VisualElement();
            ve.Add(new Label(line));
            if (warn != null) ve.Add(Warn("        " + warn));

            return ve;
        }

        private static bool TryGetLiveStats(GameObject go, out DynamicHashMap<StatKey, StatValue> map)
        {
            map = default;
            if (!TryGetEntity(go, out var em, out var e) || !em.HasBuffer<Stat>(e)) return false;

            map = em.GetBuffer<Stat>(e, true).AsMap();
            return true;
        }

        private static bool TryGetLiveIntrinsics(GameObject go, out DynamicHashMap<IntrinsicKey, int> map)
        {
            map = default;
            if (!TryGetEntity(go, out var em, out var e) || !em.HasBuffer<Intrinsic>(e)) return false;

            map = em.GetBuffer<Intrinsic>(e, true).AsMap();
            return true;
        }

        private static bool TryGetEntity(GameObject go, out EntityManager em, out Entity entity)
        {
            em = default;
            entity = Entity.Null;
            try
            {
                // Mirror the essence_state tool: prefer the playing Game world, else a converted SubScene world with
                // Essence data — so live reads also work in edit mode against an open, baked SubScene.
                var world = EssenceEditorWorlds.PickWorld();
                if (world is not { IsCreated: true }) return false;

                em = world.EntityManager;
                using var candidates = new NativeList<Entity>(Allocator.Temp);
                em.Debug.GetEntitiesForAuthoringObject(go, candidates);

                foreach (var e in candidates)
                {
                    if (!em.HasBuffer<Stat>(e) && !em.HasBuffer<Intrinsic>(e)) continue;

                    if (entity != Entity.Null)
                    {
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

        private static VisualElement BuildLegend()
        {
            var f = Section("Legend / cheat-sheet");
            f.value = false;

            void L(string s)
            {
                f.Add(Muted(s));
            }

            L("STATS: Value = Σ(Added) × (1 + Σ Increased) × Π(1 + More).  100 = 1.0× (StatValue.ToInt).");
            L("  Added/Subtracted = flat ±.  Increased/Reduced = ± percent (0.1 = 10%).  More/Less = × multiplier.");
            L("INTRINSICS: live integer counter, clamped to the schema range; optional MinStat/MaxStat clamp.");
            L("CONDITIONS: op symbols — == != > >= < <= , 'in' = Between, (any) = exists.");
            L("  Constant = fixed threshold (bakes the number).  Custom = compare to a live Stat/Intrinsic.");
            L("  ⚠ Custom-vs-static-stat resolves to 0 until the stat changes — prefer Constant for fixed thresholds.");
            L("  Features: Condition = gates active; Value = records value; Accumulate = both, per frame.");
            L(
                "KEYS/IDS auto-assign on import; id 0 = unusable (re-import). Registries: EssenceSettings, ReactionSettings.");
            return f;
        }

        private static T EnumVal<T>(SerializedProperty p)
            where T : struct, Enum
        {
            if (p == null) return default;

            var values = (T[])Enum.GetValues(typeof(T));
            var i = p.enumValueIndex;
            return i >= 0 && i < values.Length ? values[i] : default;
        }

        private static string Symbol(Equality op)
        {
            return op switch
            {
                Equality.Equal => "==",
                Equality.NotEqual => "!=",
                Equality.GreaterThan => ">",
                Equality.GreaterThanEqual => ">=",
                Equality.LessThan => "<",
                Equality.LessThanEqual => "<=",
                Equality.Between => "in",
                _ => "(any)"
            };
        }

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