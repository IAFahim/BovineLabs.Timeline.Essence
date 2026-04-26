using BovineLabs.Essence.Authoring.Actions;
using UnityEditor;
using UnityEngine;

namespace BovineLabs.Essence.Editor
{
    [CustomEditor(typeof(ActionTickDistributionAuthoring))]
    public class ActionTickDistributionAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var authoring = (ActionTickDistributionAuthoring)target;
            var curve = authoring.Curve;

            if (curve == null || curve.keys.Length == 0) return;

            var lastKey = curve.keys[^1];
            if (Mathf.Abs(lastKey.time - 1f) > 0.01f || Mathf.Abs(lastKey.value - 1f) > 0.01f)
            {
                EditorGUILayout.HelpBox("Curve MUST end at Time: 1, Value: 1 (CDF constraint).", MessageType.Warning);
                if (GUILayout.Button("Fix Curve End"))
                {
                    Undo.RecordObject(authoring, "Fix Curve");
                    var keys = curve.keys;
                    keys[^1] = new Keyframe(1f, 1f, lastKey.inTangent, lastKey.outTangent);
                    curve.keys = keys;
                    EditorUtility.SetDirty(authoring);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tick Preview (Assuming 10 total ticks)", EditorStyles.boldLabel);
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(24),
                GUILayout.ExpandWidth(true));

            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
            Handles.DrawSolidRectangleWithOutline(rect, Color.clear, Color.gray);

            var totalTicks = 10;
            var lastExpected = 0;
            Handles.color = Color.green;

            for (var i = 0; i <= 100; i++)
            {
                var t = i / 100f;
                var expected = Mathf.FloorToInt(curve.Evaluate(t) * totalTicks);
                if (expected > lastExpected)
                {
                    var x = rect.x + t * rect.width;
                    Handles.DrawLine(new Vector3(x, rect.y), new Vector3(x, rect.y + rect.height));
                    lastExpected = expected;
                }
            }

            Handles.color = Color.white;
        }
    }
}