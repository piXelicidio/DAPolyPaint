using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using EGL = UnityEditor.EditorGUILayout;
using System;


namespace DAPolyPaint
{

    public class PolyPaintWindow : EditorWindow
    {
        Painter _painter;

        bool _paintingMode;
        bool _objectInfo = true;
        int _setUVcalls = 0;
        bool _isPressed = false;

        GameObject _targetObject;
        Mesh _targetMesh;
        private bool _skinned;
        Texture _targetTexture;
        List<Vector2> _targetMeshUVs;
        Vector3 _currMousePosCam;

        RaycastHit _lastHit;
        int _lastFace;
        Vector2 _lastUVpick = new Vector2(0.5f, 0.5f);
        Vector2 _lastInTextureMousePos;
        private Vector2 _scrollPos;
        private MeshCollider _meshCollider;
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
            SceneView.duringSceneGui += OnScene;
            this.OnSelectionChange();
        }

        public void OnDestroy()
        {
            SceneView.duringSceneGui -= OnScene;
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
                        SceneView.lastActiveSceneView.Repaint();
                    }
                }
                else
                {
                    if (GUILayout.Button("STOP PAINT MODE"))
                    {
                        _paintingMode = false;
                        SceneView.lastActiveSceneView.Repaint();
                    }
                }
            }

            var check = CheckObject();
            var statusColorRect = EGL.GetControlRect(false, _statusColorBarHeight);
            Color statusColor;
            if (!check.isOk)
            {
                statusColor = Color.yellow;
            } else {
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
                } else {
                    EGL.HelpBox(check.info, MessageType.Warning);
                    //var r = GUILayoutUtility.GetLastRect();
                    //EditorGUIUtility.rect
                    //Debug.Log(r.ToString());                    
                    //EditorGUI.DrawRect(statusColorRect, statusColor);
                }

                
            }
            EGL.EndFoldoutHeaderGroup();


            if (_targetTexture)
            {
                var currWidth = EditorGUIUtility.currentViewWidth;
                var rt = EGL.GetControlRect(false, currWidth);
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
                        //_targetTexture.
                        Repaint();
                    }
                }
            }



            EGL.LabelField(_lastUVpick.ToString());
            EGL.EndScrollView();
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
                info += "\nSetUVs calls: " + _setUVcalls.ToString();
                info += "\nSkinned: " + _skinned.ToString();
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
        void OnScene(SceneView scene)
        {
            if (_paintingMode)
            {
                //draw
                Handles.BeginGUI();
                EditorGUIDrawFrame("PAINT MODE");
                Handles.EndGUI();

                //input events
                int id = GUIUtility.GetControlID(0xDA3D, FocusType.Passive);
                var ev = Event.current;
                //consume input except when doing navigation, view rotation, panning..
                if (ev.alt || ev.button > 0)  return;                

                if (ev.type == EventType.MouseDrag)
                {
                    var prevFace = _lastFace;
                    _lastFace = GetFaceHit(scene, ev.mousePosition);
                    if (_lastFace != prevFace)
                    {
                        if (_isPressed) _painter.SetUV(_lastFace, _lastUVpick);
                        Repaint();
                    }
                }
                else if (ev.type == EventType.MouseDown)
                {
                    AcquireInput(ev, id);
                    _isPressed = true;
                    if (_targetMesh != null)
                    {
                        _lastFace = GetFaceHit(scene, ev.mousePosition);
                        _painter.SetUV(_lastFace, _lastUVpick);
                        Repaint();
                    }
                }
                else if (ev.type == EventType.MouseUp)
                {
                    ReleaseInput(ev);
                    _isPressed = false;
                }
                else if (ev.type == EventType.MouseMove)
                {
                    
                }
                
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
                }
                else
                {
                    _targetTexture = null;
                }

            }
            else
            {
                _targetMesh = null;
            }
            Repaint();
        }

        void PrepareObject()
        {            
            if (_targetMesh != null)
            {
                LogMeshInfo(_targetMesh);
                _painter.SetMesh(_targetMesh, _skinned);
                if (!_targetObject.GetComponent<MeshCollider>())
                {
                    _meshCollider = _targetObject.AddComponent<MeshCollider>();
                    if (!_skinned)
                    {
                       _meshCollider.sharedMesh = _targetMesh;
                      // _targetMesh.GetVertices(_colliderMeshVertices);
                       
                    }

                    else
                    {
                        //snapshoting the skinned mesh so we can paint over a mesh distorted by bone transformations.
                        var smr = _targetObject.GetComponent<SkinnedMeshRenderer>();
                        var snapshot = new Mesh();
                        smr.BakeMesh(snapshot, true);
                        _meshCollider.sharedMesh = snapshot;
                        
                    }
                }
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


    }

}