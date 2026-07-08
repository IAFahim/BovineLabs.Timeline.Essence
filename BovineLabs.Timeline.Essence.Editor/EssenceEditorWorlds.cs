using System;
using BovineLabs.Essence.Data;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Timeline.Essence.Editor
{
    /// <summary>
    /// Shared world-picking for the Essence editor tooling. Prefers the world most likely to hold live Essence data:
    /// the playing Game world first, else a converted SubScene world. A single implementation used by both the
    /// <c>essence_state</c> CLI tool and the Essence inspector window so they agree on which world to read.
    /// </summary>
    internal static class EssenceEditorWorlds
    {
        /// <summary> Pick the ECS world most likely to hold live Essence data, or null if none qualifies. </summary>
        /// <param name="filter"> Optional world-name substring; when set, returns the first matching world that has an Intrinsic buffer. </param>
        public static World PickWorld(string filter = null)
        {
            World converted = null, playing = null;
            foreach (var w in World.All)
            {
                if (!w.IsCreated)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(filter))
                {
                    if (w.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        using var fq = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Intrinsic>());
                        if (fq.CalculateEntityCount() > 0)
                        {
                            return w;
                        }
                    }

                    continue;
                }

                bool has;
                using (var hq = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Intrinsic>()))
                {
                    has = hq.CalculateEntityCount() > 0;
                }

                if (!has)
                {
                    continue;
                }

                if (w.Flags.HasFlag(WorldFlags.Game))
                {
                    playing = w;
                }
                else if (w.Name.Contains("Converted Scene") && !w.Name.Contains("Shadow"))
                {
                    converted = w;
                }
                else
                {
                    converted ??= w;
                }
            }

            if (Application.isPlaying && playing != null)
            {
                return playing;
            }

            return converted ?? playing;
        }
    }
}
