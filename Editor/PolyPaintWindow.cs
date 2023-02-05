using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using EGL = UnityEditor.EditorGUILayout;
using System;
using System.IO;
using System.Text;

namespace DAPolyPaint
{

    public class PolyPaintWindow : EditorWindow
    {
        Painter _painter;

        bool _paintingMode;
        bool _objectInfo = true;
        bool _isPressed = false;

        GameObject _targetObject;
        GameObject _dummyObject;
        MeshCollider _dummyCollider;
        Mesh _targetMesh;
        private bool _skinned;
        Texture _targetTexture;
        Texture2D _textureData;
        Vector3 _currMousePosCam;

        RaycastHit _lastHit;
        int _lastFace;
        private PaintCursor _paintCursor;
        Vector2 _lastUVpick;
        private Color _lastPixelColor;
        private Vector2 _scrollPos;
        private MeshCollider _meshCollider;
        private bool _autoQuads;
        private string[] _toolNames = new string[] { "Brush", "Fill", "Loop", "Pick" };
        private int _currTool = 0;
        private bool _anyModifiers = false;
        private int _savedTool;
        private bool _autoSave = true;
        const float _statusColorBarHeight = 3;
        private const string DummyName = "$pp_dummy$";
        public readonly Color ColorStatusReady = Color.green;
        public readonly Color ColorStatusPainting = new Color(1, 0.4f, 0);//orange
        public readonly Color ColorStatusError = Color.red;

        [MenuItem("DA-Tools/Poly Paint")]
        public static void ShowWindow()
        {
            var ew = EditorWindow.GetWindow(typeof(PolyPaintWindow));
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
            if (_paintingMode) StopPaintMode();
            SceneView.duringSceneGui -= OnSceneGUI;            
        }
        
        void StartPaintMode()
        {
            PrepareObject();
            _paintingMode = true;
            _lastPixelColor = _painter.GetTextureColor(_lastUVpick);
            PaintEditor.SetPixelColor(_lastPixelColor);
            SceneView.lastActiveSceneView.Repaint();
            PaintEditor.PaintMode = true;
        }

        void StopPaintMode()
        {
            if (_autoSave)
            {
                SaveMeshAsset();
            }
            else
            {
                var discard = EditorUtility.DisplayDialog("Discard changes?",
                    "Discard all changes from this paint session?", "Discard", "Apply");
                if (discard)
                {
                    _painter.RestoreOldMesh();
                }
                else
                {
                    if (!SaveMeshAsset())
                    {
                        _painter.RestoreOldMesh();
                    };
                }

            }
            _paintingMode = false;
            SceneView.lastActiveSceneView.Repaint();
            PaintEditor.PaintMode = false;
            //DestroyImmediate(_dummyObject);
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
                        StartPaintMode();
                    }
                }
                else
                {
                    if (GUILayout.Button("STOP PAINT MODE"))
                    {
                        StopPaintMode();
                    }
                }
            }

            OnGUI_ObjectStatus();
            OnGUI_TexturePalette();

            //Painting tools
            using (new EditorGUI.DisabledScope(!_paintingMode))
            {
                EGL.Space();
                if (GUILayout.Button("Full Repaint")) _painter.FullRepaint(_lastUVpick);

                EGL.Space();
                GUILayout.Label("Current tool: (Tip: use Ctrl and/or Shift)");
                _currTool = GUILayout.Toolbar(_currTool, _toolNames);
                _autoQuads = EGL.ToggleLeft("Auto-detect quads", _autoQuads);
                EGL.PrefixLabel("Max quad tolerance:");
                _painter.QuadTolerance = EGL.Slider(_painter.QuadTolerance, 0.1f, 360f);

                OnGUI_SavePaintedMesh();
            }

            EGL.EndScrollView();

        }
        
        //GUI section about saving the changes
        void OnGUI_SavePaintedMesh()
        {
            //saving mesh test
            _autoSave = EGL.ToggleLeft("Auto-save", _autoSave);
            using (new EditorGUI.DisabledScope(_autoSave))
            {
                EGL.Space();
                if (GUILayout.Button(new GUIContent("Save Changes", "Save the modified painted mesh")))
                {
                    SaveMeshAsset();
                }
            }            
        }


        //Try to apply the changes to the mesh, return false if the process is aborted.
        public bool SaveMeshAsset(bool optimizeMesh = false)
        {
            var currPath = AssetDatabase.GetAssetPath(_targetMesh);

            if (string.IsNullOrEmpty(currPath))
            {
                Debug.Log("No asset path found for mesh");
                return false;
            }
            else
            {
                var format = Path.GetExtension(currPath);                

                if (optimizeMesh)
                {                    
                   // MeshUtility.Optimize(mesh);
                }

                //if not a separated asset already, save as new asset
                var reassign = false;
                string newPath = "";
                if (format != ".asset")
                {
                    var meshCompName = (_skinned) ? "SkinnedMeshRenderer" : "MeshFilter";
                    var userOk = EditorUtility.DisplayDialog("Saving modified mesh...",
                        "Since the original asset is a "+format+" the mesh will be saved as a new asset, and the " + meshCompName+" reference updated.",
                        "OK", "Cancel");
                    if (!userOk) return false;
                    
                    newPath = Path.GetDirectoryName(currPath) + "/" + Path.GetFileNameWithoutExtension(currPath) + "_pnt.asset";
                    newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);
                    var mesh = Instantiate(_targetMesh) as Mesh;
                    AssetDatabase.CreateAsset(mesh, newPath);                    
                    reassign = true; 
                } else
                {
                    EditorUtility.SetDirty(_targetMesh);
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                if (reassign)
                {
                    _targetMesh = AssetDatabase.LoadAssetAtPath<Mesh>(newPath);
                    if (_skinned)
                    {
                        _targetObject.GetComponent<SkinnedMeshRenderer>().sharedMesh = _targetMesh;
                    }
                    else
                    {
                        _targetObject.GetComponent<MeshFilter>().sharedMesh = _targetMesh;
                    }
                }
                return true;
            }
        }

        public Mesh SaveMeshToFile(Mesh mesh, string fileName, bool createNewInstance = false, bool optimizeMesh = false, bool nameIsFullPath = false)
        {
            string filePath = fileName;
            if (!nameIsFullPath)
            {
                // Show the save file dialog and get the file path
                filePath = EditorUtility.SaveFilePanel("Save Modified Mesh Asset", "Assets/", fileName, "asset");
            }
            if (!string.IsNullOrEmpty(filePath))
            {
                if (!nameIsFullPath) filePath = FileUtil.GetProjectRelativePath(filePath);

                // Create a new instance or use the same mesh
                Mesh meshToSave = (createNewInstance) ? Instantiate(mesh) as Mesh : mesh;

                if (optimizeMesh) MeshUtility.Optimize(meshToSave);

                // Create the asset and save it to the file


                AssetDatabase.CreateAsset(meshToSave, filePath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("to: " + filePath);
                Type assetType = AssetDatabase.GetMainAssetTypeAtPath(filePath);
                Debug.Log("Type: " + assetType);
                return (AssetDatabase.LoadAssetAtPath<Mesh>(filePath));
            }
            return null;
        }

        string GetCurrToolName()
        {
            var index = _currTool;
            if (index >= 0 && index < _toolNames.Length)
            {
                return _toolNames[index];
            }
            else return "None";
        }

        void SetCurrTool(string name)
        {
            var index = Array.IndexOf(_toolNames, name);
            _currTool = index;
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
                statusColor = ColorStatusError;
            }
            else
            {
                if (_paintingMode) statusColor = ColorStatusPainting; else statusColor = ColorStatusReady;
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
                //info += "\nFace: " + _lastFace.ToString();
                //info += "\nSetUVs calls: " + _painter.NumUVCalls.ToString();
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

                //var coll = _targetObject.GetComponent<MeshCollider>();
                var coll = _dummyCollider;
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
            Color c = ColorStatusPainting;
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
            rt.x = border * 2;
            rt.y = height - EditorGUIUtility.singleLineHeight - border * 2;
            var style = new GUIStyle(EditorStyles.label);
            style.fontSize += 2;
            style.normal.textColor = Color.black;
            EditorGUI.LabelField(rt, label, style);
            rt.x -= 1;
            rt.y -= 1;
            style.normal.textColor = ColorStatusPainting;
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
                //consume events except when doing navigation, view rotation, panning..
                if (ev.alt) return;

                //draw                               
                Handles.BeginGUI();
                EditorGUIDrawFrame("PAINT MODE");
                Handles.EndGUI();


                OnSceneShiftState(ev);

                var tool = GetCurrToolName();



                if (ev.type == EventType.MouseDrag)
                {
                    var prevFace = _lastFace;
                    _lastFace = GetFaceHit(scene, ev.mousePosition);

                    if (_lastFace != prevFace)
                    {
                        BuildCursor();
                        if (_isPressed)
                        {
                            if (tool == "Loop")
                            {
                                Debug.Log(String.Format("Ctrl+drag: From {0} to {1}", prevFace, _lastFace));
                                BuildLoopCursor(prevFace, _lastFace);
                                scene.Repaint();
                                //PaintUsingCursor();
                            }
                            else if (tool == "Pick")
                            {
                                PickFromSurface(_lastFace);
                            }
                            else if (tool == "Brush") PaintUsingCursor();
                        }
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
                    if (ev.button == 0)
                    {
                        AcquireInput(ev, id);
                        _isPressed = true;
                        if (_targetMesh != null)
                        {
                            _lastFace = GetFaceHit(scene, ev.mousePosition);
                            BuildCursor();
                            if (tool == "Fill")
                            {
                                if (_lastFace != -1)
                                {
                                    _painter.FillPaint(_lastFace, _lastUVpick);
                                    _painter.Undo_SaveState();
                                }
                            }
                            else if (tool == "Pick")
                            {
                                PickFromSurface(_lastFace);
                            }
                            else if (tool == "Loop") { }
                            else PaintUsingCursor();
                            Repaint();
                        }
                    }
                }
                else if (ev.type == EventType.MouseUp)
                {
                    if (ev.button == 0)
                    {
                        if (tool == "Loop")
                        {
                            PaintUsingCursor();
                            _painter.Undo_SaveState();
                        }
                        else if (tool == "Brush")
                        {
                            _painter.Undo_SaveState();
                        }
                        ReleaseInput(ev);
                        _isPressed = false;
                    }
                }
                else if (ev.type == EventType.KeyDown)
                {
                    if (ev.control)
                    {
                        if (ev.keyCode == KeyCode.Z)
                        {
                            //catching Ctrl+Z
                            Debug.Log("Ctrl+Z: Undo");
                            _painter.Undo_Undo();
                        } else if (ev.keyCode == KeyCode.Y)
                        {
                            Debug.Log("Ctrl+Y: Redo");
                            _painter.Undo_Redo();
                        }
                    }
                    if (!isAllowedInput(ev)) { ev.Use(); }
                }
                else if (ev.type == EventType.KeyUp)
                {
                    if (!isAllowedInput(ev)) { ev.Use(); }
                }

            }
        }

        private bool isAllowedInput(Event ev)
        {
            return (!ev.control && !ev.shift && !ev.alt)
                && (isWASDQEF(ev.keyCode));
        }

        private bool isWASDQEF(KeyCode kc)
        {
            return kc == KeyCode.W || kc == KeyCode.A || kc == KeyCode.S || kc == KeyCode.D || kc == KeyCode.Q || kc == KeyCode.E || kc == KeyCode.F;
        }

        private void OnSceneShiftState(Event ev)
        {
            if (ev.shift || ev.control)
            {
                if (!_anyModifiers)
                {
                    _anyModifiers = true;
                    //starting modifiers;
                    _savedTool = _currTool;
                }
                if (ev.shift && ev.control) _currTool = 2;
                else if (ev.control) _currTool = 1;
                else if (ev.shift) _currTool = 3;
                Repaint();
            } else
            {
                if (_anyModifiers)
                {
                    _anyModifiers = false;
                    _currTool = _savedTool;
                    //ending modifiers;
                    Repaint();
                }
            }
        }

        private void BuildLoopCursor(int fromFace, int toFace)
        {
            var loop = _painter.FindLoop(fromFace, toFace);
            var loopBack = _painter.FindLoop(toFace, fromFace);
            loop.UnionWith(loopBack);

            PaintEditor.PolyCursor.Clear();

            foreach (var f in loop)
            {
                var poly = new PolyFace();
                _painter.GetFaceVerts(f, poly, _targetObject.transform.localToWorldMatrix);
                poly.FaceNum = f;
                PaintEditor.PolyCursor.Add(poly);
            }
            Debug.Log("loop faces: " + PaintEditor.PolyCursor.Count.ToString());

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

        private void PaintUsingCursor()
        {
            foreach (var polyFace in PaintEditor.PolyCursor)
            {
                _painter.SetUV(polyFace.FaceNum, _lastUVpick);
            }
        }

        private void BuildCursor()
        {
            PaintEditor.PolyCursor.Clear();
            if (_lastFace != -1)
            {
                var poly = new PolyFace();
                _painter.GetFaceVerts(_lastFace, poly, _targetObject.transform.localToWorldMatrix);
                poly.FaceNum = _lastFace;
                PaintEditor.PolyCursor.Add(poly);


                if (_autoQuads)
                {
                    var quadBro = _painter.FindQuad(_lastFace);
                    if (quadBro != -1)
                    {
                        poly = new PolyFace();
                        poly.FaceNum = quadBro;
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
            if (_paintingMode) return;
            
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
                    if (_targetTexture != null)
                    {
                        _textureData = ToTexture2D(_targetTexture);
                    } else
                    {
                        _targetTexture = null;
                        _textureData = null;
                    }
                    
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
                (_dummyObject, _dummyCollider) = GetDummy(_targetObject);
                
                LogMeshInfo(_targetMesh);
                _painter.SetMeshAndRebuild(_targetMesh, _skinned, _textureData);
                if (!_skinned)
                {
                    _dummyCollider.sharedMesh = _targetMesh;
                }
                else
                {
                    //snapshoting the skinned mesh so we can paint over a mesh distorted by bone transformations.
                    var smr = _targetObject.GetComponent<SkinnedMeshRenderer>();
                    var snapshot = new Mesh();
                    smr.BakeMesh(snapshot, true);
                    _dummyCollider.sharedMesh = snapshot; 
                    _painter.SetSkinAffected(snapshot);
                }
                _lastFace = -1;                
            }
            else
            {
                Debug.LogWarning("_targetMeshs should be valid before calling PrepareObject()");
            }
        }

        //Creates a dummy object to be used as a raycast target
        (GameObject, MeshCollider) GetDummy(GameObject obj)
        {
            //see first if obj already have a child with DummyName
            GameObject dummy;
            var trans = obj.transform.Find(DummyName);
            if (trans != null)
            {
                dummy = trans.gameObject;
            }
            else
            {
                dummy = new GameObject(DummyName);
                dummy.transform.parent = obj.transform;                
            }
            dummy.hideFlags = HideFlags.DontSave;
            dummy.transform.localPosition = Vector3.zero;
            dummy.transform.localRotation = Quaternion.identity;
            dummy.transform.localScale = Vector3.one;

            var collider = dummy.GetComponent<MeshCollider>();
            if (collider == null) collider = dummy.AddComponent<MeshCollider>();

            _paintCursor = dummy.GetComponent<PaintCursor>();
            if (_paintCursor == null) _paintCursor = dummy.AddComponent<PaintCursor>();
            

            collider.hideFlags = HideFlags.HideInHierarchy; 
            return (dummy, collider);
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

    public class PolyFace : List<Vector3> 
    {
        public int FaceNum;
    }

    public class PolyList : List<PolyFace> { }

    //With this class we can draw the cursor in the scene view using Gizmos.DrawLine.
    [CustomEditor(typeof(PaintCursor))]
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

        //Draws
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        static void DrawGizmos(PaintCursor obj, GizmoType gizmoType) //need to be static
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