using BovineLabs.Core.Collections;
using BovineLabs.Core.ObjectManagement;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Essence.Debug
{
    // Lives in the runtime BovineLabs.Timeline.Essence assembly (not the editor-only .Debug
    // assembly) so the type resolves in player builds. A component that a baker serializes into
    // a subscene MUST exist at runtime; otherwise the whole entity section fails to deserialize
    // ("Cannot find TypeIndex"). The baker + the debug systems that read this stay editor-only.
    public struct EssenceDebugNames : IComponentData
    {
        public BlobAssetReference<Data> Value;

        public struct Data
        {
            public BlobHashMap<BLId, FixedString32Bytes> StatNames;
            public BlobHashMap<BLId, FixedString32Bytes> IntrinsicNames;
            public BlobHashMap<BLId, FixedString32Bytes> EventNames;
        }
    }
}
