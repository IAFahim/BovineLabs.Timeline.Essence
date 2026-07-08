using System.Collections.Generic;
using System.Text;
using BovineLabs.Core.Editor.Settings;
using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Authoring.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BovineLabs.Timeline.Essence.Editor
{
    /// <summary>
    /// Validates that every <see cref="ConditionEventObject"/> asset is registered (with a valid key) so it cannot
    /// null-pointer dereference at runtime. An unregistered event that fires reaches
    /// <c>ConditionConfig.GetPayloadType</c> → <c>BlobHashMap</c> indexer with a missing key: with collections checks
    /// off (player builds) that dereferences a NULL blob pointer — a native crash / UB. The editor throws instead, but
    /// only if the event actually fires during testing; this catches it as a build/menu-time content check.
    /// </summary>
    public static class EssenceEventRegistrationValidator
    {
        internal readonly struct Offender
        {
            public readonly Object Asset;
            public readonly string Reason;

            public Offender(Object asset, string reason)
            {
                this.Asset = asset;
                this.Reason = reason;
            }
        }

        [MenuItem("Tools/BovineLabs/Validate Essence Event Registration")]
        public static void Validate()
        {
            var offenders = CollectOffenders();
            if (offenders.Count == 0)
            {
                Debug.Log("[Essence] All ConditionEventObject assets are registered with valid keys.");
                return;
            }

            foreach (var o in offenders)
            {
                Debug.LogError(
                    $"[Essence] ConditionEventObject '{DisplayName(o.Asset)}' {o.Reason}. An unregistered / key-0 event " +
                    "that fires at runtime is a null-pointer dereference in a player build (ConditionConfig payload map miss).",
                    o.Asset);
            }

            Debug.LogError(
                $"[Essence] {offenders.Count} ConditionEventObject asset(s) fail event-registration validation — see the errors above.");
        }

        /// <summary>
        /// Collects every <see cref="ConditionEventObject"/> that is (a) not registered in
        /// <see cref="ReactionSettings"/> or (b) has key id 0. Events nested under an <c>IntrinsicSchemaObject</c> are
        /// registered via the intrinsic path (<c>EssenceSettings.Bake</c> scans each intrinsic schema's asset
        /// representations for its event) and are therefore excluded from the "unregistered" report.
        /// </summary>
        internal static List<Offender> CollectOffenders()
        {
            var offenders = new List<Offender>();

            var reaction = EditorSettingsUtility.GetSettings<ReactionSettings>();
            var registered = new HashSet<Object>();
            if (reaction != null)
            {
                foreach (var e in reaction.ConditionEvents)
                {
                    if (e != null)
                    {
                        registered.Add(e);
                    }
                }
            }

            // Events nested as sub-assets under an IntrinsicSchemaObject are registered via the intrinsic bake path.
            var intrinsicNested = new HashSet<Object>();
            foreach (var guid in AssetDatabase.FindAssets("t:IntrinsicSchemaObject"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var rep in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                {
                    if (rep is ConditionEventObject nested)
                    {
                        intrinsicNested.Add(nested);
                    }
                }
            }

            var seen = new HashSet<ConditionEventObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:ConditionEventObject"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (obj is not ConditionEventObject e || !seen.Add(e))
                    {
                        continue;
                    }

                    var key0 = e.Key == 0;
                    var nested = intrinsicNested.Contains(e);
                    var unregistered = !nested && !registered.Contains(e);

                    if (!key0 && !unregistered)
                    {
                        continue;
                    }

                    var reasons = new List<string>(2);
                    if (unregistered)
                    {
                        reasons.Add("is not registered in ReactionSettings.ConditionEvents");
                    }

                    if (key0)
                    {
                        reasons.Add("has key id 0 (asset not imported/registered — re-import)");
                    }

                    offenders.Add(new Offender(e, string.Join(" and ", reasons)));
                }
            }

            return offenders;
        }

        internal static string DisplayName(Object asset)
        {
            if (asset == null)
            {
                return "(null)";
            }

            var path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? asset.name : $"{asset.name} ({path})";
        }
    }

    /// <summary>
    /// Fails the build if any <see cref="ConditionEventObject"/> would null-pointer dereference at runtime — better a
    /// broken build than a shipping crash the first time an unregistered event fires.
    /// </summary>
    public sealed class EssenceEventRegistrationBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var offenders = EssenceEventRegistrationValidator.CollectOffenders();
            if (offenders.Count == 0)
            {
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(
                $"Essence event-registration validation failed: {offenders.Count} ConditionEventObject asset(s) would " +
                "null-pointer dereference at runtime in a player build (unregistered / key-0 events). Register them in " +
                "ReactionSettings.ConditionEvents (or re-import to assign a key):");

            foreach (var o in offenders)
            {
                sb.AppendLine($"  - {EssenceEventRegistrationValidator.DisplayName(o.Asset)}: {o.Reason}.");
            }

            throw new BuildFailedException(sb.ToString());
        }
    }
}
