using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace DAPolyPaint
{
    public class PolyFace : List<Vector3>
    {
        public int FaceNum;
    }

    public class PolyList : List<PolyFace> { }

    public struct CursorRay
    {
        public Vector3 direction;
        public Vector3 origin;
        public bool enabled;
    }

    /// <summary>
    /// This will actually added to a dummy object, not the object being edited. Improve names.
    /// </summary>
    [HideInInspector]
    public class PaintCursor : MonoBehaviour
    {
        public Mesh TargetMesh;
    }

    /// <summary>
    /// With this class we can draw the cursor in the scene view using Gizmos.DrawLine.
    /// </summary>
    [CustomEditor(typeof(PaintCursor))]
    public class PaintEditor : Editor
    {
        static Color _currPixelColor;
        static PolyList _polyCursor = new PolyList();

        public static bool PaintMode { get; set; }
        public static bool MirrorMode { get; set; }
        public static int MirrorAxis { get; set; }
        public static bool ShowMirrorPlane { get; set; }
        public static float AxisOffset { get; set; }
        public static PolyList PolyCursor { get { return _polyCursor; } }
        public static CursorRay[] CursorRays = new CursorRay[2];
        private static Vector3[] _mirrorPlane = new Vector3[5];

        public static void SetPixelColor(Color c)
        {
            _currPixelColor = c.linear;
        }

        //Draws
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        static void DrawGizmos(PaintCursor obj, GizmoType gizmoType) //need to be static
        {
            if (!obj.enabled) return;
            if (PaintMode)
            {
                //Drawing cursor triangles
                if (_polyCursor.Count > 0)
                {
                    for (int p = 0; p < _polyCursor.Count; p++)
                    {
                        var poly = _polyCursor[p];
                        if (poly.Count > 2)
                        {
                            var average = Vector3.zero;

                            for (var i = 0; i < poly.Count; i++)
                            {
                                Gizmos.color = _currPixelColor;
                                Gizmos.DrawLine(poly[i], poly[(i + 1) % poly.Count]);
                                average += poly[i];
                            }
                        }
                    }
                }

                //Drawing cursor rays
                foreach (CursorRay ray in CursorRays)
                {
                    if (ray.enabled)
                    {
                        var v2 = ray.origin + ray.direction * 0.1f;
                        Gizmos.color = Color.white;
                        Gizmos.DrawLine(ray.origin, v2);
                    }
                }

                //mirror axis..
                if (MirrorMode && ShowMirrorPlane)
                {
                    DrawMirrorPlane(obj);
                }
            }

        }

        private static void DrawMirrorPlane(PaintCursor obj)
        {
            var min = obj.TargetMesh.bounds.min;
            var max = obj.TargetMesh.bounds.max;
            var mat = obj.transform.localToWorldMatrix;
            var axisDir = PolyPaintWindow.AxisDirection(MirrorAxis, obj.transform);
            Vector3 offset = AxisOffset * axisDir;

            if (MirrorAxis == 0)
            {
                _mirrorPlane[0].Set(0, max.y, max.z);
                _mirrorPlane[1].Set(0, min.y, max.z);
                _mirrorPlane[2].Set(0, min.y, min.z);
                _mirrorPlane[3].Set(0, max.y, min.z);
                _mirrorPlane[4] = _mirrorPlane[0];
            }
            else if (MirrorAxis == 1)
            {
                _mirrorPlane[0].Set(max.x, 0, max.z);
                _mirrorPlane[1].Set(min.x, 0, max.z);
                _mirrorPlane[2].Set(min.x, 0, min.z);
                _mirrorPlane[3].Set(max.x, 0, min.z);
                _mirrorPlane[4] = _mirrorPlane[0];
            }
            else if (MirrorAxis == 2)
            {
                _mirrorPlane[0].Set(max.x, max.y, 0);
                _mirrorPlane[1].Set(min.x, max.y, 0);
                _mirrorPlane[2].Set(min.x, min.y, 0);
                _mirrorPlane[3].Set(max.x, min.y, 0);
                _mirrorPlane[4] = _mirrorPlane[0];
            }
            TransformVectorArray(mat, ref _mirrorPlane);
            for (var i = 0; i < _mirrorPlane.Length - 1; i++)
            {
                Gizmos.color = i == 1 ? Color.white : Color.gray;
                Gizmos.DrawLine(_mirrorPlane[i] + offset, _mirrorPlane[i + 1] + offset);
            }
        }

        static void TransformVectorArray(Matrix4x4 matrix, ref Vector3[] vectors)
        {
            for (int i = 0; i < vectors.Length; i++)
            {
                vectors[i] = matrix.MultiplyPoint(vectors[i]);
            }
        }

        //void OnSceneGUI()
        //{
        //    //can draw GUI or interactive handles
        //}
    }

    public class ToolType
    {
        public static readonly int brush = 0;
        public static readonly int fill = 1;
        public static readonly int loop = 2;
        public static readonly int pick = 3;
    }

    public class FillVariant
    {
        public static readonly int flood = 0;
        public static readonly int replace = 1;
        public static readonly int element = 2;
    }
}