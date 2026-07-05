using System.Collections.Generic;
using System.Linq;
using BovineLabs.Core.Iterators;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Conditions;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityCliConnector;
using UnityEditor;

namespace BovineLabs.Timeline.Essence.Editor.CliTools
{
    /// <summary>
    /// Reads live Essence state (intrinsics / stats / condition events) off an ECS entity so an agent can see what
    /// input → event → reaction actually produced. The write-side twin is the `player_input` tool: tap an action,
    /// then `essence_state` to watch OnInputPrimary/IsAttacking/etc. change. Names are resolved from the schema assets.
    /// </summary>
    [UnityCliTool(
        Name = "essence_state",
        Group = "vex",
        Description =
            "Dump an entity's live Essence: intrinsics (counters), stats (x100 fixed-point) and this-frame condition events, keyed by schema name. " +
            "ops: dump (default). Params: world (name substring; default the game world when playing else the converted subscene), " +
            "entity (ECS index int; default = the entity with the most intrinsics = the player), filter (name substring), events_only.")]
    public static class EssenceStateTool
    {
        public static object HandleCommand(JObject @params)
        {
            var p = new ToolParams(@params);
            var op = (p.Get("op", "dump") ?? "dump").Trim().ToLowerInvariant();
            if (op != "dump")
                return new ErrorResponse($"Unknown op '{op}'. Only 'dump' is supported.");

            var world = PickWorld(p.Get("world"));
            if (world == null)
                return new ErrorResponse("No ECS world with Essence entities found. Enter play mode, or open the SubScene.");

            var em = world.EntityManager;

            var intrinsicNames = BuildKeyNames("t:IntrinsicSchemaObject");
            var statNames = BuildKeyNames("t:StatSchemaObject");
            var conditionNames = BuildKeyNames("t:ConditionEventObject");

            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<Intrinsic>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            if (ents.Length == 0)
                return new ErrorResponse($"World '{world.Name}' has no entity with an Intrinsic buffer.");

            var entityParam = p.GetInt("entity", -1) ?? -1;
            Entity target;
            if (entityParam >= 0)
            {
                target = ents.FirstOrDefault(e => e.Index == entityParam);
                if (target == Entity.Null)
                    return new ErrorResponse(
                        $"No Essence entity with index {entityParam}. Present: {string.Join(", ", ents.Select(e => e.Index))}.");
            }
            else
            {
                // Default to the richest intrinsic buffer — that's the player.
                target = Entity.Null;
                var best = -1;
                foreach (var e in ents)
                {
                    var c = em.GetBuffer<Intrinsic>(e, true).AsMap().Count;
                    if (c > best) { best = c; target = e; }
                }
            }

            var filter = p.Get("filter");
            var eventsOnly = p.GetBool("events_only", false);

            var events = ReadMap(em.GetBuffer<ConditionEvent>(target, true).AsMap(), conditionNames, "ConditionKey", filter, v => v.Read<int>());
            object intrinsics = null, stats = null;
            if (!eventsOnly)
            {
                intrinsics = ReadMap(em.GetBuffer<Intrinsic>(target, true).AsMap(), intrinsicNames, "IntrinsicKey", filter, v => v);
                stats = ReadStats(em, target, statNames, filter);
            }

            return new SuccessResponse(
                $"Essence of {target} in '{world.Name}' — {(events as System.Array)?.Length ?? 0} live event(s)"
                + (UnityEngine.Application.isPlaying ? " [play]" : " [edit — events only fire in play]"),
                new { world = world.Name, entity = target.Index, events, intrinsics, stats });
        }

        private static object[] ReadMap<TKey, TValue>(DynamicHashMap<TKey, TValue> map, Dictionary<int, string> names,
            string keyLabel, string filter, System.Func<TValue, object> valueSelector)
            where TKey : unmanaged, System.IEquatable<TKey>
            where TValue : unmanaged
        {
            var list = new List<object>();
            using var kv = map.GetKeyValueArrays(Allocator.Temp);
            for (var i = 0; i < kv.Length; i++)
            {
                var raw = KeyValue(kv.Keys[i]);
                var name = names.TryGetValue(raw, out var n) ? n : $"{keyLabel}#{raw}";
                if (!string.IsNullOrEmpty(filter) && name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                list.Add(new { name, key = raw, value = valueSelector(kv.Values[i]) });
            }
            return list.OrderBy(o => (string)o.GetType().GetProperty("name").GetValue(o)).ToArray();
        }

        private static object[] ReadStats(EntityManager em, Entity target, Dictionary<int, string> names, string filter)
        {
            if (!em.HasBuffer<Stat>(target)) return System.Array.Empty<object>();
            var map = em.GetBuffer<Stat>(target, true).AsMap();
            var list = new List<object>();
            using var kv = map.GetKeyValueArrays(Allocator.Temp);
            for (var i = 0; i < kv.Length; i++)
            {
                var raw = KeyValue(kv.Keys[i]);
                var name = names.TryGetValue(raw, out var n) ? n : $"StatKey#{raw}";
                if (!string.IsNullOrEmpty(filter) && name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var sv = kv.Values[i];
                // StatValue is x100 fixed-point on the additive part; expose raw fields via reflection so we don't
                // couple to its exact layout, plus a /100 convenience on the first int field.
                var fields = sv.GetType().GetFields().ToDictionary(f => f.Name, f => f.GetValue(sv));
                list.Add(new { name, key = raw, value = fields });
            }
            return list.OrderBy(o => (string)o.GetType().GetProperty("name").GetValue(o)).ToArray();
        }

        // IntrinsicKey / StatKey / ConditionKey are thin ushort/int wrappers — pull the first integer field.
        private static int KeyValue<TKey>(TKey key) where TKey : unmanaged
        {
            foreach (var f in typeof(TKey).GetFields())
            {
                var v = f.GetValue(key);
                if (v is ushort us) return us;
                if (v is int it) return it;
                if (v is short sh) return sh;
                if (v is byte b) return b;
            }
            return -1;
        }

        private static Dictionary<int, string> BuildKeyNames(string typeFilter)
        {
            var map = new Dictionary<int, string>();
            foreach (var guid in AssetDatabase.FindAssets(typeFilter))
            {
                var o = AssetDatabase.LoadAssetAtPath<UnityEngine.ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (o == null) continue;
                var so = new SerializedObject(o);
                var kp = so.FindProperty("key") ?? so.FindProperty("Key");
                if (kp == null) continue;
                var it = kp.Copy();
                var end = kp.GetEndProperty();
                var found = -1;
                while (it.NextVisible(true) && !SerializedProperty.EqualContents(it, end))
                {
                    if (it.propertyType == SerializedPropertyType.Integer) { found = it.intValue; break; }
                }
                if (found > 0 && !map.ContainsKey(found)) map[found] = o.name;
            }
            return map;
        }

        private static World PickWorld(string filter)
        {
            World converted = null, playing = null;
            foreach (var w in World.All)
            {
                if (!w.IsCreated) continue;
                if (!string.IsNullOrEmpty(filter))
                {
                    if (w.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0
                        && w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Intrinsic>()).CalculateEntityCount() > 0)
                        return w;
                    continue;
                }

                var has = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Intrinsic>()).CalculateEntityCount() > 0;
                if (!has) continue;
                if (w.Flags.HasFlag(WorldFlags.Game)) playing = w;
                else if (w.Name.Contains("Converted Scene") && !w.Name.Contains("Shadow")) converted = w;
                else converted ??= w;
            }

            if (UnityEngine.Application.isPlaying && playing != null) return playing;
            return converted ?? playing;
        }

        public class Parameters
        {
            [ToolParameter("Operation: dump (default).")]
            public string Op { get; set; }

            [ToolParameter("World name substring. Default: the game world in play mode, else the converted SubScene world.")]
            public string World { get; set; }

            [ToolParameter("ECS entity index to read. Default: the entity with the most intrinsics (the player).")]
            public int Entity { get; set; }

            [ToolParameter("Only show intrinsic/stat/event names containing this substring (case-insensitive).")]
            public string Filter { get; set; }

            [ToolParameter("If true, only dump this-frame condition events (skip intrinsics/stats).")]
            public bool EventsOnly { get; set; }
        }
    }
}
