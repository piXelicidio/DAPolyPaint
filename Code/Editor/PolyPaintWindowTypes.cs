using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

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
    public class PaintCursorDrawer : Editor
    {
        private static Color _currPixelColor;
        private static PolyList _selectedFaces = new PolyList();

        static PolyList _polyCursor = new PolyList();
        public static Color CurrPixelColor { get { return _currPixelColor; } set { _currPixelColor = value.linear; } }
        public static Color TryPickColor { get; set; }
        public static int CurrToolCode { get; set; } = 0;
        public static bool PaintMode { get; set; }
        public static bool MirrorMode { get; set; }
        public static int MirrorAxis { get; set; }
        public static bool ShowMirrorPlane { get; set; }
        public static float AxisOffset { get; set; }
        public static PolyList SelectedFaces { get { return _selectedFaces; } } 
        public static PolyList PolyCursor { get { return _polyCursor; } }
        public static CursorRay[] CursorRays = new CursorRay[2];
        private static Vector3[] _mirrorPlane = new Vector3[5];

        public PaintCursorDrawer()
        { 
        }

        //Draws
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        static void DrawGizmos(PaintCursor obj, GizmoType gizmoType) //need to be static
        {
            if (!obj.enabled || !PaintMode) return;

            DrawCursor();
            DrawCursorRays();

            //mirror axis..
            if (MirrorMode && ShowMirrorPlane)
            {
                DrawMirrorPlane(obj);
            }
            DrawSelected();        }

        private static void DrawSelected()
        {
            for (int p = 0; p < _selectedFaces.Count; p++)
            {
                var poly = _selectedFaces[p];
                if (poly.Count > 2)
                {
                    Handles.color = new Color(1f, 0.4f, 0f);
                    Vector3 a = poly[0];
                    Vector3 b = poly[1];
                    Vector3 c = poly[2];
                    Vector3 d = poly[0];
                    Handles.DrawPolyLine(new Vector3[] { a, b, c, d });
                    //Handles.DrawDottedLines(new Vector3[] { a, b, c, d }, 2f);
                }
            }
        }

        private static void DrawCursorRays()
        {
            foreach (CursorRay ray in CursorRays)
            {
                if (ray.enabled)
                {
                    var v2 = ray.origin + ray.direction * 0.1f;
                    //Gizmos.color = Color.white;
                    //Gizmos.DrawLine(ray.origin, v2);
                    if (CurrToolCode != ToolType.pick)
                    {
                        Handles.color = _currPixelColor;
                    }
                    else
                    {
                        Handles.color = TryPickColor;
                    }
                    Handles.DrawDottedLine(ray.origin, v2, 2f);
                    if (CurrToolCode > 0 && CurrToolCode < ToolType.ToolNames.Length)
                    {
                        Handles.Label(v2, ToolType.ToolNames[CurrToolCode]);
                    }
                }
            }
        }

        private static void DrawCursor()
        {
            if (_polyCursor.Count > 0 && CurrToolCode != ToolType.pick)
            {
                for (int p = 0; p < _polyCursor.Count; p++)
                {
                    var poly = _polyCursor[p];
                    if (poly.Count > 2)
                    {
                        Handles.color = _currPixelColor;
                        Vector3 a = poly[0];
                        Vector3 b = poly[1];
                        Vector3 c = poly[2];
                        Handles.DrawAAConvexPolygon(new Vector3[] { a, b, c });
                    }
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

    public static class ToolType
    {
        public const int brush = 0;
        public const int fill = 1;
        public const int loop = 2;
        public const int pick = 3;
        public static string[] ToolNames = new string[] { "Brush", "Fill", "Loop", "Pick" };
    }

    public static class FillVariant
    {
        public const int flood = 0;
        public const int replace = 1;
        public const int element = 2;
        public const int all = 3;
    }

    public static class Temp
    {
        private static GUIContent _content = new GUIContent("", "");
        public static GUIContent Content(string text, string tooltip)
        {
            _content.text = text;
            _content.tooltip = tooltip;
            return _content;
        }
    }


}