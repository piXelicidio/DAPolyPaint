using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class DAPolyPaint : EditorWindow
{
    GameObject targetObject;
    Vector2 currMousePos;
    Vector3 currMousePosCam;
    int lastFace;

    [MenuItem("DA-Tools/D.A. Poly Paint")]
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
        if (GUILayout.Button("START PAINT MODE"))
        {
            if (targetObject != null)
            {
                PrepareObject(targetObject);
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
    }

    void OnSelectionChange()
    {
        targetObject = Selection.activeGameObject;
        Repaint();
    }

    void PrepareObject(GameObject obj)
    {

    }
}
