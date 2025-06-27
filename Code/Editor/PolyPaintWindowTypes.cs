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
        public static Color CurrPixelColor { get { return _currPixelColor; } set { _currPixelColor = value; } }
        public static Color TryPickColor { get; set; }
        public static int CurrToolCode { get; set; } = 0;
        public static ToolAction CurrToolAction { get; set; }
        public static bool IsShiftDown {get; set; }
        public static bool IsCtrlDown {get; set; }
        public static bool PaintMode { get; set; }
        public static bool MirrorMode { get; set; }
        public static int MirrorAxis { get; set; }
        public static bool ShowMirrorPlane { get; set; }
        public static float AxisOffset { get; set; }
        public static bool ShadeSelected { get; set; }
        public static PolyList SelectedFaces { get { return _selectedFaces; } } 
        public static PolyList PolyCursor { get { return _polyCursor; } }
        public static CursorRay[] CursorRays = new CursorRay[2];
        private static Vector3[] _mirrorPlane = new Vector3[5];
        private static Color SelColor = new Color(1f, 0.4f, 0f, 0.6f).linear;

        public PaintCursorDrawer()
        { 
        }

        //Draws
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        static void DrawGizmos(PaintCursor obj, GizmoType gizmoType) //need to be static
        {
            if (!obj.enabled || !PaintMode) return;

            //mirror axis..
            if (MirrorMode && ShowMirrorPlane)
            {
                DrawMirrorPlane(obj);
            }            
        }



        private static void DrawPolyIfFacing(Vector3[] verts, Color frontColor, Color backColor = default)
        {
            if (verts.Length < 3) return;

            // 1. Compute normal (assuming verts are in world space, and convex)
            Vector3 e1 = verts[1] - verts[0];
            Vector3 e2 = verts[2] - verts[0];
            Vector3 normal = Vector3.Cross(e1, e2).normalized;

            // 2. Find center of polygon
            Vector3 center = verts[0];

            // 3. Get SceneView camera direction
            var cam = SceneView.currentDrawingSceneView.camera;
            Vector3 viewDir = (center - cam.transform.position).normalized;

            // 4. Dot to see if front-facing
            float dot = Vector3.Dot(normal, viewDir);

            // 5. Choose color (or skip if back-facing)
            if (dot <= 0f)
            {
                Handles.color = frontColor;
            }
            else
            {
                if (backColor == default)
                    return;             // cull
                Handles.color = backColor;
            }

            // 6. Draw it!
            Handles.DrawAAConvexPolygon(verts);
        }

        public static void DrawSelected()
        {
            for (int p = 0; p < _selectedFaces.Count; p++)
            {
                var poly = _selectedFaces[p];
                if (poly.Count > 2)
                {
                    Handles.color = SelColor;
                    Vector3 a = poly[0];
                    Vector3 b = poly[1];
                    Vector3 c = poly[2];
                    Vector3 d = poly[0];
                    //Handles.DrawAAConvexPolygon(a, b, c);
                    if (ShadeSelected) DrawPolyIfFacing(new Vector3[] { a, b, c }, SelColor); 
                    Handles.DrawPolyLine(new Vector3[] { a, b, c, d });                    
                    
                }
            }
        }

        public static void DrawCursorRays()
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
                        Handles.color = _currPixelColor.gamma;
                    }
                    else
                    {
                        Handles.color = TryPickColor.gamma;
                    }
                    Handles.DrawDottedLine(ray.origin, v2, 2f);
                    if (CurrToolCode >= 0 && CurrToolCode < ToolType.ToolNames.Length)
                    {
                        var actionStr = "";
                        if (CurrToolAction != ToolAction.Paint && CurrToolCode != ToolType.pick) {
                            actionStr = " Select";
                            if (IsShiftDown) actionStr += " (-)"; 
                                else if (IsCtrlDown) actionStr += " (+)";
                            
                        }
                        Handles.Label(v2, ToolType.ToolNames[CurrToolCode] + actionStr);
                    }
                }
            }
        }

        public static void DrawCursor()
        {
            if (_polyCursor.Count > 0 && CurrToolCode != ToolType.pick)
            {
                if (CurrToolAction == ToolAction.Paint)
                {
                    for (int p = 0; p < _polyCursor.Count; p++)
                    {
                        var poly = _polyCursor[p];
                        if (poly.Count > 2)
                        {
                            Handles.color = _currPixelColor.gamma;
                            Vector3 a = poly[0];
                            Vector3 b = poly[1];
                            Vector3 c = poly[2];
                            Handles.DrawAAConvexPolygon(new Vector3[] { a, b, c });
                        }
                    }
                }
                else
                {
                    for (int p = 0; p < _polyCursor.Count; p++)
                    {
                        var poly = _polyCursor[p];
                        if (poly.Count > 2)
                        {
                            Handles.color = SelColor;
                            Vector3 a = poly[0];
                            Vector3 b = poly[1];
                            Vector3 c = poly[2];
                            Vector3 d = poly[0];
                            Handles.DrawPolyLine(new Vector3[] { a, b, c, d });
                        }
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
        public static string[] ToolNames = new string[] { "brush", "fill", "loop", "pick" };
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
           // _content.image = null;
            return _content;
        }
    }


}