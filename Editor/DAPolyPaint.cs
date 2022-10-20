using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class DAPolyPaint : EditorWindow
{
    GameObject targetObject;
    Mesh targetMesh;
    List<Vector2> targetMeshUVs;
    Vector2 currMousePos;
    Vector3 currMousePosCam;
    int lastFace;

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
        var s = "none";
        if (targetObject != null) s = targetObject.name;
        EditorGUILayout.LabelField("Target: " + s);
        EditorGUILayout.LabelField("Face: " + lastFace.ToString() );

        using (new EditorGUI.DisabledScope(targetMesh == null))
        {
            if (GUILayout.Button("START PAINT MODE" ))
            {
                if (targetObject != null)
                {
                    PrepareObject(targetObject);
                }
            }
        }
    }

    void OnScene(SceneView scene)
    {
        var cEvent = Event.current;
        if (cEvent.type == EventType.MouseMove)
        {
            currMousePos = cEvent.mousePosition;

            var sv = SceneView.lastActiveSceneView;
            currMousePosCam = currMousePos;
            currMousePosCam.y = sv.camera.pixelHeight - currMousePosCam.y;
            var ray = sv.camera.ScreenPointToRay(currMousePosCam);
            RaycastHit hit;
            lastFace = -1;
            if (targetObject != null)
            {
                var coll = targetObject.GetComponent<Collider>();
                if (coll)
                {
                    if (coll.Raycast(ray, out hit, 100f))
                    {
                       lastFace = hit.triangleIndex;
                    }
                }
            }
            
            Repaint();
        } 
        else if (cEvent.type == EventType.MouseDown)
        {
            if (lastFace != -1)
            {
                var uvPos = new Vector2(0.5f, 0.5f);
                targetMeshUVs[lastFace * 3] = uvPos;
                targetMeshUVs[lastFace * 3 + 1] = uvPos;
                targetMeshUVs[lastFace * 3 + 2] = uvPos;
                targetMesh.SetUVs(0, targetMeshUVs);
            }
        }
    }

    void OnSelectionChange()
    {
        targetObject = Selection.activeGameObject;
        if (targetObject!=null)
        {
            var mf = targetObject.GetComponent<MeshFilter>();
            if (mf != null)
            {
                targetMesh = mf.sharedMesh;
            } else
            {
                targetMesh = null;
            }

        } else
        {
            targetMesh = null;
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
        targetMeshUVs = newUVs; //keep ref for painting
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
