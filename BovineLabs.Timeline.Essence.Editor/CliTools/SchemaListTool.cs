using System.Collections.Generic;
using System.Linq;
using BovineLabs.Core.Editor.Settings;
using BovineLabs.Essence.Authoring;
using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Timeline.Core.Editor.CliTools.Shared;
using Newtonsoft.Json.Linq;
using UnityCliConnector;
using UnityEditor;
using UnityEngine;

namespace BovineLabs.Timeline.Essence.Editor.CliTools
{
    [UnityCliTool(
        Name = "schema_list",
        Group = "vex",
        Description =
            "Read-only asset sweep of Essence/Reaction schemas (stat / intrinsic / event): assetPath, runtime key, type-specific fields, and whether each is registered in its settings registry (EssenceSettings for stats+intrinsics, ReactionSettings for events).")]
    public static class SchemaListTool
    {
        private static readonly HashSet<string> ValidTypes = new() { "all", "stat", "intrinsic", "event" };

        public static object HandleCommand(JObject @params)
        {
            var p = new Params(@params);
            try
            {
                var type = (p.OptString("type", "all") ?? "all").Trim().ToLowerInvariant();
                if (!ValidTypes.Contains(type))
                    throw new ToolException("BAD_VALUE", $"Unknown type '{type}'.",
                        "Expected one of: stat, intrinsic, event, all.");

                var essence = EditorSettingsUtility.GetSettings<EssenceSettings>();
                var reaction = EditorSettingsUtility.GetSettings<ReactionSettings>();

                var stats = Wants(type, "stat") ? ListStats(essence) : null;
                var intrinsics = Wants(type, "intrinsic") ? ListIntrinsics(essence) : null;
                var events = Wants(type, "event") ? ListEvents(reaction) : null;

                var count = (stats?.Count ?? 0) + (intrinsics?.Count ?? 0) + (events?.Count ?? 0);

                return ToolEnvelope.Ok(
                    $"{count} schema(s).",
                    new { stats, intrinsics, events });
            }
            catch (ToolException e)
            {
                return ToolEnvelope.FromException(e);
            }
        }

        private static bool Wants(string requested, string family)
        {
            return requested == "all" || requested == family;
        }

        private static HashSet<Object> RegisteredSet(IEnumerable<Object> registered)
        {
            return new HashSet<Object>(registered ?? Enumerable.Empty<Object>());
        }

        private static List<object> ListStats(EssenceSettings essence)
        {
            var registered = RegisteredSet(essence != null ? essence.StatSchemas.Cast<Object>() : null);
            return Load<StatSchemaObject>().Select(s => (object)new
            {
                assetPath = AssetDatabase.GetAssetPath(s),
                s.name,
                key = s.Key,
                conditionType = s.ConditionType,
                isGlobal = s.IsGlobal,
                registered = registered.Contains(s)
            }).ToList();
        }

        private static List<object> ListIntrinsics(EssenceSettings essence)
        {
            var registered = RegisteredSet(essence != null ? essence.IntrinsicSchemas.Cast<Object>() : null);
            return Load<IntrinsicSchemaObject>().Select(i => (object)new
            {
                assetPath = AssetDatabase.GetAssetPath(i),
                i.name,
                key = i.Key,
                conditionType = i.ConditionType,
                isGlobal = i.IsGlobal,
                defaultValue = i.DefaultValue,
                min = i.Range.x,
                max = i.Range.y,
                minStat = i.MinStat != null ? i.MinStat.name : null,
                maxStat = i.MaxStat != null ? i.MaxStat.name : null,
                registered = registered.Contains(i)
            }).ToList();
        }

        private static List<object> ListEvents(ReactionSettings reaction)
        {
            var registered = RegisteredSet(reaction != null ? reaction.ConditionEvents.Cast<Object>() : null);
            return Load<ConditionEventObject>().Select(e => (object)new
            {
                assetPath = AssetDatabase.GetAssetPath(e),
                e.name,
                key = e.Key,
                conditionType = e.ConditionType,
                isGlobal = e.IsGlobal,
                isEvent = e.IsEvent,
                customDataType = e.CustomDataType != null ? e.CustomDataType.ResolveType()?.FullName : null,
                registered = registered.Contains(e)
            }).ToList();
        }

        private static IEnumerable<T> Load<T>() where T : ScriptableObject
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(a => a != null)
                .OrderBy(a => a.name);
        }

        public class Parameters
        {
            [ToolParameter("Which schema family to list: 'stat' | 'intrinsic' | 'event' | 'all' (default 'all').")]
            public string Type { get; set; }
        }
    }
}