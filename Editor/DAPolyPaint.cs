using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using EGL = UnityEditor.EditorGUILayout;
using System;

public class DAPolyPaint : EditorWindow
{
    bool _paintingMode;
    bool _objectInfo = true;
    int _setUVcalls = 0;
    bool _isPressed = false;

    GameObject _targetObject;
    Mesh _targetMesh;
    Texture _targetTexture;
    List<Vector2> _targetMeshUVs;
    Vector3 _currMousePosCam;

    RaycastHit _lastHit;
    int _lastFace;
    Vector2 _lastUVpick = new Vector2(0.5f, 0.5f);
    Vector2 _lastInTextureMousePos;

    [MenuItem("DA-Tools/Poly Paint")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow(typeof(DAPolyPaint));
    }

    public void CreateGUI()
    {
        SceneView.duringSceneGui += OnScene;
    }

    public void OnDestroy()
    {
        SceneView.duringSceneGui -= OnScene;
    }
    void OnGUI()
    {

        using (new EditorGUI.DisabledScope(_targetMesh == null))
        {
            if (!_paintingMode)
            {
                if (GUILayout.Button("START PAINT MODE"))
                {
                    PrepareObject(_targetObject);
                    _paintingMode = true;
                }
            }
            else
            {
                if (GUILayout.Button("STOP PAINT MODE"))
                {
                    _paintingMode = false;
                }
            }
        }

        var s = "";
        if (_targetObject == null) s = "Object: None selected"; else s = _targetObject.name;


        _objectInfo = EGL.BeginFoldoutHeaderGroup(_objectInfo, s);
        if (_objectInfo)
        {
            var info = "";
            var isOk = true;
            if (_targetMesh == null) { s = "NOT FOUND"; isOk = false; } else s = "ok";
            info += "Mesh: " + s;
            if (_targetTexture == null) { s = "NOT FOUND"; isOk = false; } else s = _targetTexture.name;
            info += "\nTex: " + s;
            info += "\nFace: " + _lastFace.ToString();
            info += "\nSetUVs calls: " + _setUVcalls.ToString();
            var mt = MessageType.Info;
            if (!isOk) mt = MessageType.Warning;
            EGL.HelpBox(info, mt);
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
            EGL.LabelField(rt.ToString());
            //EditorGUI.DrawRect(rtCursor, Color.yellow);
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

    void PaintUV(int face, Vector2 uvc)
    {
        if (face >= 0)
        {
            _targetMeshUVs[face * 3] = uvc;
            _targetMeshUVs[face * 3 + 1] = uvc;
            _targetMeshUVs[face * 3 + 2] = uvc;
            _targetMesh.SetUVs(0, _targetMeshUVs);  //could be improved if Start, Length parameters actually worked
            _setUVcalls++;
        }
    }

    int GetFaceHit(SceneView sv, Vector2 currMousePos)
    {
        int result = -1;
        if (_targetMesh != null)
        {           
                        
            _currMousePosCam = currMousePos;
            _currMousePosCam.y = sv.camera.pixelHeight - _currMousePosCam.y;
            var ray = sv.camera.ScreenPointToRay(_currMousePosCam);            

            var coll = _targetObject.GetComponent<Collider>();
            if (coll)
            {
                if (coll.Raycast(ray, out _lastHit, 100f))
                {
                    result = _lastHit.triangleIndex;                    
                }
            }
        }
        return result;
    }

    void OnScene(SceneView scene)
    {
        if (_paintingMode)
        {
            int id = GUIUtility.GetControlID(0xDA3D, FocusType.Passive);
            var ev = Event.current;
            //consume input except when Alt or MiddleMouse is pressed (view rotation, panning)
            if (ev.alt) return;
            if (ev.button == 2) return;

            if (ev.type == EventType.MouseDrag)
            {
                var prevFace = _lastFace;
                _lastFace = GetFaceHit(SceneView.lastActiveSceneView, ev.mousePosition);
                if (_lastFace != prevFace)
                {
                    if (_isPressed) PaintUV(_lastFace, _lastUVpick);
                    Repaint();
                }
            }
            else if (ev.type == EventType.MouseDown)
            {
                AcquireInput(ev, id);
                _isPressed = true;
                if (_targetMesh != null)
                {
                    _lastFace = GetFaceHit(SceneView.lastActiveSceneView, ev.mousePosition);
                    PaintUV(_lastFace, _lastUVpick);
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

    void OnSelectionChange()
    {
        _targetObject = Selection.activeGameObject;
        if (_targetObject!=null)
        {
            var mf = _targetObject.GetComponent<MeshFilter>();
            if (mf != null)
            {
                _targetMesh = mf.sharedMesh;
            } else
            {
                _targetMesh = null;
            }
            var r = _targetObject.GetComponent<Renderer>();
            if (r != null)
            {
                _targetTexture = r.sharedMaterial.mainTexture;
            } else
            {
                _targetTexture = null;
            }

        } else
        {
            _targetMesh = null;
        }
        Repaint();
    }

    void PrepareObject(GameObject obj)
    {
        var meshFilter = obj.GetComponent<MeshFilter>();
        if (meshFilter != null)
        {
            var m = meshFilter.sharedMesh;
            LogMeshInfo(m);
            RebuildMeshForPainting(m);
            //
            if (!obj.GetComponent<MeshCollider>()) obj.AddComponent<MeshCollider>();        
        }
    }

    void RebuildMeshForPainting(Mesh m)
    {
        var tris = m.triangles;
        var vertices = m.vertices;
        var UVs = m.uv;
        var normals = m.normals;
        var newVertices = new List<Vector3>();
        var newUVs = new List<Vector2>();
        var newTris = new List<int>();
        var newNormals = new List<Vector3>();
        //no more shared vertices, each triangle will have its own 3 vertices.
        for (int i= 0; i < tris.Length; i++)
        {
            var idx = tris[i];
            newVertices.Add(vertices[idx]);
            newNormals.Add(normals[idx]);
            //also UVs but the 3 values will be the same
            newUVs.Add(UVs[tris[i - i % 3]]);            
            //newUVs.Add(new Vector2(0.82f, 0.1f));
            newTris.Add(i);
        }
        //TODO: assign new data, recarculate normals:
        if (m.subMeshCount > 1) m.subMeshCount = 1;
        m.SetVertices(newVertices);
        m.SetUVs(0, newUVs);
        _targetMeshUVs = newUVs; //keep ref for painting
        m.SetTriangles(newTris, 0);
        m.SetNormals(newNormals);
        //m.RecalculateNormals();
    }

    void LogMeshInfo(Mesh m)
    {
        var s = "<b>" + m.name + "</b>";
        s = s + " - SubMeshes: " + m.subMeshCount;
        s = s + " - Triangles: " + (m.triangles.Length / 3) ;        
        s = s + " - Vertices: " + m.vertices.Length;
        s = s + " - UVs: " + m.uv.Length;
        Debug.Log(s);
    }
}
