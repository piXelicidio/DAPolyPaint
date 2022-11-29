using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using EGL = UnityEditor.EditorGUILayout;
using System;
using System.Linq;

namespace DAPolyPaint
{

    public class PolyPaintWindow : EditorWindow
    {
        Painter _painter;

        bool _paintingMode;
        bool _objectInfo = true;
        bool _isPressed = false;

        GameObject _targetObject;
        Mesh _targetMesh;
        private bool _skinned;
        Texture _targetTexture;
        Texture2D _textureData;
        Vector3 _currMousePosCam;

        RaycastHit _lastHit;
        int _lastFace;
        private Paint _paint;
        Vector2 _lastUVpick;
        private Color _lastPixelColor;
        private Vector2 _scrollPos;
        private MeshCollider _meshCollider;
        private bool _autoQuads;
        const float _statusColorBarHeight = 3; 

        [MenuItem("DA-Tools/Poly Paint")]
        public static void ShowWindow()
        {
            var ew  = EditorWindow.GetWindow(typeof(PolyPaintWindow));
            ew.titleContent = new GUIContent("Poly Paint");            
        }

        public void CreateGUI()
        {
            _painter = new Painter();
            SceneView.duringSceneGui += OnSceneGUI;
            this.OnSelectionChange();
        }

        public void OnDestroy()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        //Editor Window User Interface - PolyPaint --------------------------------
        void OnGUI()
        {
            _scrollPos = EGL.BeginScrollView(_scrollPos);
            using (new EditorGUI.DisabledScope(_targetMesh == null))
            {
                if (!_paintingMode)
                {
                    if (GUILayout.Button("START PAINT MODE"))
                    {
                        PrepareObject();
                        _paintingMode = true;
                        _lastPixelColor = _painter.GetTextureColor(_lastUVpick);
                        PaintEditor.SetPixelColor(_lastPixelColor);
                        SceneView.lastActiveSceneView.Repaint();
                        PaintEditor.PaintMode = true;
                    }
                }
                else
                {
                    if (GUILayout.Button("STOP PAINT MODE"))
                    {
                        _paintingMode = false;
                        SceneView.lastActiveSceneView.Repaint();
                        PaintEditor.PaintMode = false;
                    }
                }
            }

            OnGUI_ObjectStatus();
            OnGUI_TexturePalette();

            using (new EditorGUI.DisabledScope(!_paintingMode))
            {
                EGL.Space();
                _autoQuads = EGL.Toggle("Auto-detect quads", _autoQuads);
                if (GUILayout.Button("Full Repaint")) _painter.FullRepaint(_lastUVpick);
            }

            EGL.EndScrollView();

        }

        private void OnGUI_TexturePalette()
        {
            if (_targetTexture)
            {
                var texWidth = EditorGUIUtility.currentViewWidth;

                var rt = EGL.GetControlRect(false, texWidth);
                rt.height = rt.width;
                EditorGUI.DrawPreviewTexture(rt, _targetTexture);
                var rtCursor = new Vector2(rt.x, rt.y);
                rtCursor.x += _lastUVpick.x * rt.width;
                rtCursor.y += (1 - _lastUVpick.y) * rt.height;
                EditorGUIDrawCursor(rtCursor);


                if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag)
                {
                    var mousePos = Event.current.mousePosition;
                    if (rt.Contains(mousePos))
                    {
                        mousePos -= rt.position;
                        mousePos.x /= rt.width;
                        mousePos.y /= rt.height;
                        mousePos.y = 1 - mousePos.y;
                        _lastUVpick = mousePos;

                        _lastPixelColor = _painter.GetTextureColor(_lastUVpick);
                        PaintEditor.SetPixelColor(_lastPixelColor);

                        Repaint();
                    }
                }

                var cRect = EGL.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                cRect.width = cRect.width / 2;
                cRect.x += cRect.width;
                EditorGUI.LabelField(cRect, _lastUVpick.ToString());
                cRect.x = cRect.width - cRect.height;
                cRect.width = cRect.height;
                EditorGUI.DrawRect(cRect, _lastPixelColor);
            }
        }



        private void OnGUI_ObjectStatus()
        {
            var check = CheckObject();
            var statusColorRect = EGL.GetControlRect(false, _statusColorBarHeight);
            Color statusColor;
            if (!check.isOk)
            {
                statusColor = Color.yellow;
            }
            else
            {
                if (_paintingMode) statusColor = Color.red; else statusColor = Color.green;
            }
            EditorGUI.DrawRect(statusColorRect, statusColor);
            var s = "";
            if (_targetObject == null) s = "Object: None selected"; else s = _targetObject.name;

            _objectInfo = EGL.BeginFoldoutHeaderGroup(_objectInfo, s);
            if (_objectInfo)
            {
                if (check.isOk)
                {
                    EGL.HelpBox(check.info, MessageType.None);
                }
                else
                {
                    EGL.HelpBox(check.info, MessageType.Warning);                    
                }
            }
            EGL.EndFoldoutHeaderGroup();
        }

        private (bool isOk, string info) CheckObject()
        {
            var info = "";
            var s = "";
            var isOk = true;
            if (_targetMesh == null) { s = "NOT FOUND"; isOk = false; } else s = "ok";
            info += "Mesh: " + s;
            if (_targetTexture == null) { s = "NOT FOUND"; isOk = false; } else s = _targetTexture.name;
            info += "\nTex: " + s;
            if (isOk)
            {
                info += "\nFace: " + _lastFace.ToString();
                info += "\nSetUVs calls: " + _painter.NumUVCalls.ToString();
                info += "\nSkinned: " + _skinned.ToString();
                info += "\n quadBest: " + _painter._nearBest.ToString();
            }
            return (isOk, info);
        }

        

        void EditorGUIDrawCross(in Vector2 cur, in Color c, int size = 3, int space = 3)
        {
            var rt = new Rect();
            //horizontal
            rt.x = cur.x + space;
            rt.y = cur.y;
            rt.height = 1;
            rt.width = size;
            EditorGUI.DrawRect(rt, c);
            rt.x = cur.x - (space + size) + 1;
            EditorGUI.DrawRect(rt, c);
            //vertical
            rt.x = cur.x;
            rt.y = cur.y - (size + space) + 1;
            rt.width = 1;
            rt.height = size;
            EditorGUI.DrawRect(rt, c);
            rt.y = cur.y + space;
            EditorGUI.DrawRect(rt, c);
        }

        //Drawing a cross with shadows and space in the center
        void EditorGUIDrawCursor(Vector2 cur)
        {
            cur.x += 1;
            cur.y += 1;
            EditorGUIDrawCross(cur, Color.black);
            cur.x -= 1;
            cur.y -= 1;
            EditorGUIDrawCross(cur, Color.white);
        }

        void AcquireInput(Event e, int id)
        {
            GUIUtility.hotControl = id;
            e.Use();
            EditorGUIUtility.SetWantsMouseJumping(1);
        }

        void ReleaseInput(Event e)
        {
            GUIUtility.hotControl = 0;
            e.Use();
            EditorGUIUtility.SetWantsMouseJumping(0);
        }

        int GetFaceHit(SceneView sv, Vector2 currMousePos)
        {
            int result = -1;
            if (_targetMesh != null)
            {

                _currMousePosCam = currMousePos;
                _currMousePosCam.y = sv.camera.pixelHeight - _currMousePosCam.y;
                var ray = sv.camera.ScreenPointToRay(_currMousePosCam);

                var coll = _targetObject.GetComponent<MeshCollider>();
                if (coll)
                {
                    if (coll.Raycast(ray, out _lastHit, 100f))
                    {
                        result = _lastHit.triangleIndex;
                    }
                } else
                {
                    Debug.LogWarning("No collider to do raycast.");
                }
            }
            return result;
        }

        void EditorGUIDrawFrame(string label, int border = 2)
        {            
            var width = Camera.current.pixelWidth;
            var height = Camera.current.pixelHeight;
            var rt = new Rect(0, 0, width, border);
            Color c = Color.red;
            EditorGUI.DrawRect(rt, c);
            rt.height = height;
            rt.width = border;
            EditorGUI.DrawRect(rt, c);
            rt.x = width - border;
            EditorGUI.DrawRect(rt, c);
            rt.x = 0;
            rt.y = height - border;
            rt.height = border;
            rt.width = width;
            EditorGUI.DrawRect(rt, c);

            //label
            rt.width = 200;
            rt.height = EditorGUIUtility.singleLineHeight; 
            rt.x = border*2; 
            rt.y = height - EditorGUIUtility.singleLineHeight - border*2;
            var style = new GUIStyle(EditorStyles.label);
            style.fontSize += 2;
            style.normal.textColor = Color.black;
            EditorGUI.LabelField(rt, label, style);
            rt.x -= 1;
            rt.y -= 1;
            style.normal.textColor = Color.red;
            EditorGUI.LabelField(rt, label, style);
        }

        //Current Editor scene events and draw
        void OnSceneGUI(SceneView scene)
        {
            if (_paintingMode)
            {


                //input events
                int id = GUIUtility.GetControlID(0xDA3D, FocusType.Passive);
                var ev = Event.current;
                //consume input except when doing navigation, view rotation, panning..
                if (ev.alt || ev.button > 0) return;

                //draw                               
                Handles.BeginGUI();
                EditorGUIDrawFrame("PAINT MODE");                
                Handles.EndGUI();

                if (ev.type == EventType.MouseDrag )
                {
                    var prevFace = _lastFace;
                    _lastFace = GetFaceHit(scene, ev.mousePosition);
                    
                    if (_lastFace != prevFace)
                    {
                        if (_isPressed) PaintFace();
                        BuildCursor();
                    }                    
                    this.Repaint();                    
                } 
                else if (ev.type == EventType.MouseMove)
                {
                    var prevFace = _lastFace;
                    _lastFace = GetFaceHit(scene, ev.mousePosition);
                    if (_lastFace != prevFace)
                    {
                        BuildCursor();
                        //SceneView.RepaintAll();
                        scene.Repaint();
                        //Repaint();
                    }                    
                }
                else if (ev.type == EventType.MouseDown)
                {
                    AcquireInput(ev, id);                    
                    _isPressed = true;                    
                    if (_targetMesh != null)
                    {                       
                        _lastFace = GetFaceHit(scene, ev.mousePosition);                        
                        BuildCursor();
                        if (ev.shift && ev.control) 
                        {
                            if (_lastFace != -1) _painter.FillPaint(_lastFace, _lastUVpick);
                        } 
                        else if (ev.shift) 
                        {
                            PickFromSurface(_lastFace);
                        }
                        else if (ev.control) {} 
                        else PaintFace();
                        Repaint();
                    }
                }
                else if (ev.type == EventType.MouseUp)
                {
                    ReleaseInput(ev);
                    _isPressed = false;                    
                }

            }
        }

        private void PickFromSurface(int face)
        {
            if (face != -1)
            {
                _lastUVpick = _painter.GetUV(face);
                _lastPixelColor = _painter.GetTextureColor(_lastUVpick);
                PaintEditor.SetPixelColor(_lastPixelColor);
            }
        }

        private void PaintFace()
        {
            _painter.SetUV(_lastFace, _lastUVpick);
            if (_autoQuads)
            {
                var quadBro = _painter.FindQuad(_lastFace);
                if (quadBro != -1)
                {
                    _painter.SetUV(quadBro, _lastUVpick);
                }
            }
        }

        private void BuildCursor()
        {
            PaintEditor.PolyCursor.Clear();
            if (_lastFace > 0)
            {                
                var poly = new List<Vector3>();
                _painter.GetFaceVerts(_lastFace, poly, _targetObject.transform.localToWorldMatrix);
                PaintEditor.PolyCursor.Add(poly);


                if (_autoQuads)
                {
                    var quadBro = _painter.FindQuad(_lastFace);
                    if (quadBro != -1)
                    {
                        poly = new List<Vector3>();
                        _painter.GetFaceVerts(quadBro, poly, _targetObject.transform.localToWorldMatrix);
                        PaintEditor.PolyCursor.Add(poly);
                    } 
                }
                //adding all linked faces
                //foreach (var link in _painter.GetFaceLinks(_lastFace))
                //{
                //    poly = new List<Vector3>();
                //    _painter.GetFaceVerts(link.with, poly, _targetObject.transform.localToWorldMatrix);
                //    PaintEditor.PolyCursor.Add(poly);
                //}
            }
        }

        void OnSelectionChange()
        {
            _targetObject = Selection.activeGameObject;
            _skinned = false;
            if (_targetObject != null)
            {
                var solid = _targetObject.GetComponent<MeshFilter>();
                var skinned = _targetObject.GetComponent<SkinnedMeshRenderer>();
                if (solid != null)
                {
                    _targetMesh = solid.sharedMesh;
                }
                else if (skinned != null)
                {
                    _targetMesh = skinned.sharedMesh;
                    _skinned = true;
                } 
                else
                {
                    _targetMesh = null;
                }
                
                var r = _targetObject.GetComponent<Renderer>();
                if (r != null)
                {
                    _targetTexture = r.sharedMaterial.mainTexture;
                    _textureData = ToTexture2D(_targetTexture);
                    
                }
                else
                {
                    _targetTexture = null;
                    _textureData = null;
                }

            }
            else
            {
                _targetMesh = null;
                _targetTexture = null;
                _textureData = null;
            }
            Repaint();
        }

        void PrepareObject()
        {            
            if (_targetMesh != null)
            {
                LogMeshInfo(_targetMesh);
                _painter.SetMeshAndRebuild(_targetMesh, _skinned, _textureData);
                _meshCollider = _targetObject.GetComponent<MeshCollider>();
                if (_meshCollider == null) _meshCollider = _targetObject.AddComponent<MeshCollider>();                
                if (!_skinned)
                {
                    _meshCollider.sharedMesh = _targetMesh;                  
                }
                else
                {
                    //snapshoting the skinned mesh so we can paint over a mesh distorted by bone transformations.
                    var smr = _targetObject.GetComponent<SkinnedMeshRenderer>();
                    var snapshot = new Mesh();
                    smr.BakeMesh(snapshot, true);
                    _meshCollider.sharedMesh = snapshot;
                    _painter.SetSkinAffected(snapshot);
                }
                _lastFace = -1;

                _paint = _targetObject.GetComponent<Paint>();
                if (_paint == null) _paint = _targetObject.AddComponent<Paint>();
                
            } else
            {
                Debug.LogWarning("_targetMeshs should be valid before calling PrepareObject()");
            }
        }

        void LogMeshInfo(Mesh m)
        {
            var s = "<b>" + m.name + "</b>";
            s = s + " - SubMeshes: " + m.subMeshCount;
            s = s + " - Triangles: " + (m.triangles.Length / 3);
            s = s + " - Vertices: " + m.vertices.Length;
            s = s + " - UVs: " + m.uv.Length;
            s = s + " - Bones: " + m.bindposes.Length;
            Debug.Log(s);
        }

        Texture2D ToTexture2D(Texture tex)
        {
            var texture2D = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            var currentRT = RenderTexture.active;
            var renderTexture = new RenderTexture(tex.width, tex.height, 32);
            Graphics.Blit(tex, renderTexture);
            RenderTexture.active = renderTexture;
            texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            texture2D.Apply();
            RenderTexture.active = currentRT;
            return texture2D;
        }


    }

    public class PolyList : List<List<Vector3>> { }

    [CustomEditor(typeof(Paint))]
    public class PaintEditor : Editor
    {
        static Color _currPixelColor;
        static PolyList _polyCursor = new PolyList();

        public static bool PaintMode { get; set; }
        public static PolyList PolyCursor { get { return _polyCursor; }}

        public static void SetPixelColor(Color c)
        {
            _currPixelColor = c.linear;
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        static void DrawGizmos(Paint obj, GizmoType gizmoType) //need to be static
        {
            if (PaintMode && _polyCursor.Count > 0)
            {
                for (int p=0; p<_polyCursor.Count; p++)
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
            
	    }

        void OnSceneGUI()
        {
            //can draw GUI or interactive handles
        }
    }
}