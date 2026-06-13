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
        Description = "Read-only asset sweep of Essence/Reaction schemas (stat / intrinsic / event): assetPath, runtime key, type-specific fields, and whether each is registered in its settings registry (EssenceSettings for stats+intrinsics, ReactionSettings for events).")]
    public static class SchemaListTool
    {
        public class Parameters
        {
            [ToolParameter("Which schema family to list: 'stat' | 'intrinsic' | 'event' | 'all' (default 'all').")]
            public string Type { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var p = new Params(@params);
            try
            {
                string type = (p.OptString("type", "all") ?? "all").Trim().ToLowerInvariant();
                if (type != "all" && type != "stat" && type != "intrinsic" && type != "event")
                {
                    throw new ToolException("BAD_VALUE", $"Unknown type '{type}'.", "Expected one of: stat, intrinsic, event, all.");
                }

                var essence = EditorSettingsUtility.GetSettings<EssenceSettings>();
                var reaction = EditorSettingsUtility.GetSettings<ReactionSettings>();

                var registeredStats = new HashSet<Object>(essence != null ? essence.StatSchemas.Cast<Object>() : Enumerable.Empty<Object>());
                var registeredIntrinsics = new HashSet<Object>(essence != null ? essence.IntrinsicSchemas.Cast<Object>() : Enumerable.Empty<Object>());
                var registeredEvents = new HashSet<Object>(reaction != null ? reaction.ConditionEvents.Cast<Object>() : Enumerable.Empty<Object>());

                List<object> stats = null;
                List<object> intrinsics = null;
                List<object> events = null;

                if (type is "all" or "stat")
                {
                    stats = Load<StatSchemaObject>().Select(s => (object)new
                    {
                        assetPath = AssetDatabase.GetAssetPath(s),
                        name = s.name,
                        key = s.Key,
                        conditionType = s.ConditionType,
                        isGlobal = s.IsGlobal,
                        registered = registeredStats.Contains(s),
                    }).ToList();
                }

                if (type is "all" or "intrinsic")
                {
                    intrinsics = Load<IntrinsicSchemaObject>().Select(i => (object)new
                    {
                        assetPath = AssetDatabase.GetAssetPath(i),
                        name = i.name,
                        key = i.Key,
                        conditionType = i.ConditionType,
                        isGlobal = i.IsGlobal,
                        defaultValue = i.DefaultValue,
                        min = i.Range.x,
                        max = i.Range.y,
                        minStat = i.MinStat != null ? i.MinStat.name : null,
                        maxStat = i.MaxStat != null ? i.MaxStat.name : null,
                        registered = registeredIntrinsics.Contains(i),
                    }).ToList();
                }

                if (type is "all" or "event")
                {
                    events = Load<ConditionEventObject>().Select(e => (object)new
                    {
                        assetPath = AssetDatabase.GetAssetPath(e),
                        name = e.name,
                        key = e.Key,
                        conditionType = e.ConditionType,
                        isGlobal = e.IsGlobal,
                        isEvent = e.IsEvent,
                        customDataType = e.CustomDataType != null ? e.CustomDataType.FullName : null,
                        registered = registeredEvents.Contains(e),
                    }).ToList();
                }

                int count = (stats?.Count ?? 0) + (intrinsics?.Count ?? 0) + (events?.Count ?? 0);

                return ToolEnvelope.Ok(
                    $"{count} schema(s).",
                    result: new { stats, intrinsics, events });
            }
            catch (ToolException e) { return ToolEnvelope.FromException(e); }
        }

        private static IEnumerable<T> Load<T>() where T : ScriptableObject
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(a => a != null)
                .OrderBy(a => a.name);
        }
    }
}
