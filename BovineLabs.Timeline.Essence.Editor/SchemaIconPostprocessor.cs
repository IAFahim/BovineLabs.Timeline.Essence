namespace BovineLabs.Timeline.Essence.Editor
{
    using System;
    using System.Collections.Generic;
    using BovineLabs.Essence.Authoring;
    using BovineLabs.Reaction.Authoring.Conditions;
    using UnityEditor;
    using UnityEngine;

    public sealed class SchemaIconPostprocessor : AssetPostprocessor
    {
        const string EditorFolder = "BovineLabs.Timeline.Essence.Editor";
        const string EventIconFile = "Event.png";
        const string StatIconFile = "Stats.png";
        const string IntrinsicIconFile = "Int.png";

        const string SchemaAssetsRoot = "Assets/Settings/Schemas";
        const string SchemaAssetsFilter = " t:ConditionEventObject t:StatSchemaObject t:IntrinsicSchemaObject";

        static string packageRoot;
        static Dictionary<Type, Texture2D> iconCache;
        static HashSet<string> schemaFolders;

        static string PackageRoot
        {
            get
            {
                if (packageRoot != null)
                {
                    return packageRoot;
                }

                var guids = AssetDatabase.FindAssets($"{nameof(SchemaIconPostprocessor)} t:MonoScript");
                if (guids.Length > 0)
                {
                    var thisPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    var idx = thisPath.IndexOf(EditorFolder, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        packageRoot = thisPath.Substring(0, idx + EditorFolder.Length);
                        return packageRoot;
                    }
                }

                Debug.LogError("[SchemaIconPostprocessor] Could not resolve package root.");
                packageRoot = "";
                return packageRoot;
            }
        }

        static Dictionary<Type, Texture2D> IconCache
        {
            get
            {
                if (iconCache != null)
                {
                    return iconCache;
                }

                iconCache = new Dictionary<Type, Texture2D>();
                if (string.IsNullOrEmpty(PackageRoot))
                {
                    Debug.LogError("[SchemaIconPostprocessor] Could not resolve package root.");
                    return iconCache;
                }

                TryCacheIcon(typeof(ConditionEventObject), EventIconFile);
                TryCacheIcon(typeof(StatSchemaObject), StatIconFile);
                TryCacheIcon(typeof(IntrinsicSchemaObject), IntrinsicIconFile);
                return iconCache;
            }
        }

        static HashSet<string> SchemaFolders
        {
            get
            {
                if (schemaFolders != null)
                {
                    return schemaFolders;
                }

                schemaFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Assets/Settings/Schemas/Events",
                    "Assets/Settings/Schemas/Stats",
                    "Assets/Settings/Schemas/Intrinsics",
                };
                return schemaFolders;
            }
        }

        static void TryCacheIcon(Type type, string fileName)
        {
            var path = $"{PackageRoot}/{fileName}";
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (icon == null)
            {
                Debug.LogWarning($"[SchemaIconPostprocessor] Icon not found: {path}");
                return;
            }

            iconCache[type] = icon;
        }

        static bool IsSchemaAsset(string path)
        {
            if (!path.StartsWith(SchemaAssetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);
        }

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (IconCache.Count == 0)
            {
                return;
            }

            foreach (var path in importedAssets)
            {
                if (!IsSchemaAsset(path))
                {
                    continue;
                }

                TryApplyIcon(path);
            }

            foreach (var path in movedAssets)
            {
                if (!IsSchemaAsset(path))
                {
                    continue;
                }

                TryApplyIcon(path);
            }
        }

        [MenuItem("Tools/Fix Schema SO Icons")]
        static void FixExisting()
        {
            if (IconCache.Count == 0)
            {
                Debug.LogError("[SchemaIconPostprocessor] No icons loaded. Aborting.");
                return;
            }

            var count = 0;

            foreach (var folder in SchemaFolders)
            {
                var guids = AssetDatabase.FindAssets(SchemaAssetsFilter, new[] { folder });
                foreach (var guid in guids)
                {
                    if (TryApplyIcon(AssetDatabase.GUIDToAssetPath(guid)))
                    {
                        count++;
                    }
                }
            }

            if (count > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[SchemaIconPostprocessor] Patched {count} assets.");
            }
            else
            {
                Debug.Log("[SchemaIconPostprocessor] All icons already set. No changes.");
            }
        }

        static bool TryApplyIcon(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset == null)
            {
                return false;
            }

            if (!IconCache.TryGetValue(asset.GetType(), out var icon))
            {
                return false;
            }

            var current = EditorGUIUtility.GetIconForObject(asset);
            if (current == icon)
            {
                return false;
            }

            EditorGUIUtility.SetIconForObject(asset, icon);
            EditorUtility.SetDirty(asset);
            return true;
        }

    }
}
