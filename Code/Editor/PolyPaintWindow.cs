using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using EGL = UnityEditor.EditorGUILayout;
using GL = UnityEngine.GUILayout;
using System;
using System.IO;
using System.Text.RegularExpressions;
using static UnityEngine.GraphicsBuffer;
using UnityEngine.SceneManagement;

namespace DAPolyPaint 
{
    /// <summary>
    /// GUI Editor Window for PolyPaint
    /// </summary>
    public class PolyPaintWindow : EditorWindow
    {
        #region ---------- Painting State ----------
        Painter _painter;
        bool _paintingMode;
        bool _isMousePressed = false;
        GameObject _targetObject;
        GameObject _dummyObject;
        MeshCollider _dummyCollider;
        Mesh _targetMesh;
        Vector3[] _vertices;
        bool _skinned;
        Mesh _skinAffected;
        Vector3[] _verticesSkinned;
        Texture _targetTexture;
        Texture2D _textureData;
        Vector3 _currMousePosCam;
        RaycastHit _lastHit;
        int _lastFace; //last face hit by raycast
        int _prevFace; //previous face hit by raycast;
        int _prevFace_Mirror;
        PaintCursor _paintCursor;
        Vector2 _lastUVpick;
        Color _lastPixelColor;
        Vector2 _scrollPos;
        MeshCollider _meshCollider;
        RaycastHit _lastHit_mirror;
        int _lastFace_Mirror;
        Color _tryPickColor;
        Material _remapMaterial;
        const string DummyName = "$pp_dummy$";
        #endregion


        #region --------- User Interface ---------
        bool _objectInfoFoldout = true;
        const string _toolName = "DAPolyPaint";
        bool _autoQuads = true;
        bool _loopTwoWays = true;
        bool _mirrorCursor = false;
        int _currMirrorAxis;
        float _axisOffset;
        readonly string[] _toolNames = new string[] { "Brush", "Fill", "Loop", "Pick" };
        readonly string[] _mirrorAxis = new string[] { "X", "Y", "Z" };
        readonly string[] _toolHints = new string[]
        {
            "Click or Drag over faces", //brush
            "Click a starting face", //fill
            "Drag over a quad edge", //loop
            "Click a face to get color" //pick
        };
        int _currToolCode = 0;        
        int _fillVariant;
        readonly string[] _fillVariantOptions = new string[] { " Flood", " Replace", " Element" };
        bool _anyModifiers = false;
        int _savedTool;
        bool _autoSave = false;
        const float _statusColorBarHeight = 3;
        public readonly Color ColorStatusReady = Color.green;
        public readonly Color ColorStatusPainting = new Color(1, 0.4f, 0);//orange
        public readonly Color ColorStatusError = Color.red;
        private bool _autoSwitchMaterial = true;
        #endregion

        //---------------------------------------------------------------------------------------------
        [MenuItem("Tools/DA/Poly Paint")]
        public static void ShowWindow()
        {
            var ew =(PolyPaintWindow) EditorWindow.GetWindow(typeof(PolyPaintWindow));
            if (ew._painter == null) ew._painter = new Painter();
            ew.titleContent = new GUIContent("DA Poly Paint");
        }

        public void CreateGUI()
        {     
            if (_painter==null) _painter = new Painter();            
            this.OnSelectionChange();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;

            _objectInfoFoldout = EditorPrefs.GetBool(_toolName+"_objectInfoFoldout", true);
            _lastUVpick.x = EditorPrefs.GetFloat(_toolName + "_lastUVpick.x", 0.5f);
            _lastUVpick.y = EditorPrefs.GetFloat(_toolName + "_lastUVpick.y", 0.5f);
            _currToolCode = EditorPrefs.GetInt(_toolName + "_currToolCode", 0);
            _fillVariant = EditorPrefs.GetInt(_toolName + "_fillVariant", 0);
            _autoQuads = EditorPrefs.GetBool(_toolName + "_autoQuads", true);
            _currMirrorAxis = EditorPrefs.GetInt(_toolName + "_mirrorAxis", 0);
            _axisOffset = EditorPrefs.GetFloat(_toolName + "_axisOffset", 0);
            PaintEditor.ShowMirrorPlane = EditorPrefs.GetBool(_toolName + "ShowMirrorPlane", true);
            _loopTwoWays = EditorPrefs.GetBool(_toolName + "_loopTwoWays", true);
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;

            EditorPrefs.SetBool(_toolName + "_objectInfoFoldout", _objectInfoFoldout);
            EditorPrefs.SetFloat(_toolName + "_lastUVpick.x", _lastUVpick.x);
            EditorPrefs.SetFloat(_toolName + "_lastUVpick.y", _lastUVpick.y);
            EditorPrefs.SetInt(_toolName + "_currToolCode", _currToolCode);
            EditorPrefs.SetInt(_toolName + "_fillVariant", _fillVariant);
            EditorPrefs.SetBool(_toolName + "_autoQuads", _autoQuads);
            EditorPrefs.SetInt(_toolName + "_mirrorAxis", _currMirrorAxis);
            EditorPrefs.SetFloat(_toolName + "_axisOffset", _axisOffset);
            EditorPrefs.SetBool(_toolName + "ShowMirrorPlane", PaintEditor.ShowMirrorPlane);
            EditorPrefs.SetBool(_toolName + "_loopTwoWays", _loopTwoWays);
        }

        public void OnBeforeAssemblyReload()
        {
            Debug.Log("Assembly Reloading...");
            AbortPaintMode();
        }

        public void OnAfterAssemblyReload()
        {
            Debug.Log("Assembly Reloaded.");
        }


        public void OnDestroy()
        {
            if (_paintingMode) StopPaintMode();           
        }
        
        void StartPaintMode()
        {
            CheckComponents(_targetObject);
            if (_targetTexture != null)
            {
                PrepareObject();
                _paintingMode = true;
                _lastPixelColor = _painter.GetTextureColor(_lastUVpick);
                PaintEditor.SetPixelColor(_lastPixelColor);
                SceneView.lastActiveSceneView.Repaint();
                PaintEditor.PaintMode = true;
            }
        }

        void StopPaintMode()
        {
            if (_autoSave)
            {
                SaveMeshAsset();
            }
            else if (_painter.isModified())
            {
                var apply = EditorUtility.DisplayDialog("Apply changes?",
                    "Apply all changes from this paint session?", "Ok", "Discard");
                if (!apply)
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
            _paintCursor.enabled = false;
            //DestroyImmediate(_dummyObject);
        }

        void AbortPaintMode()
        {
            //_painter.RestoreOldMesh();
            _paintingMode = false;
            SceneView.lastActiveSceneView.Repaint();
            PaintEditor.PaintMode = false;
            _paintCursor.enabled = false;
        }

        //Editor Window User Interface - PolyPaint --------------------------------
        void OnGUI()
        {
            //processing input events when the window is focused
            OnGUI_InputEvents();

            //Big PAINT MODE button
            _scrollPos = EGL.BeginScrollView(_scrollPos);
            using (new EditorGUI.DisabledScope(_targetTexture == null))
            {
                if (!_paintingMode)
                {
                    if (GL.Button("START PAINTING")) StartPaintMode();                                           
                }
                else
                {
                    var oldColor = GUI.backgroundColor;
                    GUI.backgroundColor = ColorStatusPainting;
                    if (GL.Button("END SESSION")) StopPaintMode();
                    GUI.backgroundColor = oldColor;                   
                }
            }

            OnGUI_ObjectStatus();
            OnGUI_TexturePalette();

            //Painting tools
            using (new EditorGUI.DisabledScope(!_paintingMode))
            {
                OnGUI_PaintingTools();
                OnGUI_SavePaintedMesh();
                OnGUI_Remapping();                
            }

            EGL.EndScrollView();

        }

        private void OnGUI_Remapping()
        {
            EGL.Space();
            GL.BeginVertical(EditorStyles.textArea);
            var RemapClicked = GL.Button("Remap to texture in...");                          
            _remapMaterial = (Material) EGL.ObjectField("Target Material", _remapMaterial, typeof(Material), true);
            _autoSwitchMaterial = EGL.ToggleLeft("Switch Material after remap", _autoSwitchMaterial);

            if (RemapClicked)
            {
                bool ok = TryRemappingTo(_remapMaterial, out var tex2d, _autoSwitchMaterial);
                if (ok && _autoSwitchMaterial)
                {
                    if (_targetObject.TryGetComponent<Renderer>(out var r))
                    {
                        r.material = _remapMaterial;
                        _targetTexture = r.sharedMaterial.mainTexture;
                        _textureData = tex2d;                        
                    }
                }
            }

            GL.EndVertical();
        }


        private bool TryRemappingTo(Material remapMaterial, out Texture2D tex2d, bool switchTexure = false)
        {
            tex2d = null;
            if (_remapMaterial == null) return false;
            else
            {
                var tex = remapMaterial.mainTexture;
                if (tex != null)
                {
                    tex2d = ToTexture2D(tex);
                    bool ok = _painter.RemapTo(tex2d, switchTexure);
                    if (ok)
                    {
                        _painter.Undo_SaveState();
                    }
                    return ok;
                }  else return false;
            }
        }

        private void OnGUI_PaintingTools()
        {
            EGL.Space();
            if (_painter != null)
            {
                if (GL.Button("Full Repaint"))
                {
                    _painter.FullRepaint(_lastUVpick);
                    SceneView.lastActiveSceneView.Repaint();
                    _painter.Undo_SaveState();
                }
            }
            EGL.Space();

            GL.BeginVertical(EditorStyles.textArea);
            //TODO: toolbar selected parameter can be set to -1 to unselect all buttons.
            //TOOLBAR of: Brush, Fill, Loop, Pick
            _currToolCode = GL.Toolbar(_currToolCode, _toolNames);
            if (_currToolCode >= 0 && _currToolCode <= _toolNames.Length)
            {
                EGL.LabelField(_toolHints[_currToolCode], EditorStyles.miniLabel);
            }
            if (_currToolCode == ToolType.fill)
            {
                _fillVariant = GL.SelectionGrid(_fillVariant, _fillVariantOptions, 3, EditorStyles.radioButton);
            } 
            else if (_currToolCode == ToolType.brush)
            {
                _autoQuads = EGL.ToggleLeft("Auto-detect quads", _autoQuads);
            }
            else if (_currToolCode == ToolType.loop)
            {
                _loopTwoWays = EGL.ToggleLeft("Two Ways", _loopTwoWays);
            } 
            else if (_currToolCode == ToolType.pick)
            {               
            }
            else
            {
                EGL.Space();
            }

            

            //EGL.PrefixLabel("Max quad tolerance:");
            //if (_painter!=null)  _painter.QuadTolerance = EGL.Slider(_painter.QuadTolerance, 0.1f, 360f);
            _mirrorCursor = EGL.ToggleLeft(new GUIContent("Mirror Cursor. Axis:", ""), _mirrorCursor);            
            PaintEditor.MirrorMode = _mirrorCursor;
            if (_mirrorCursor)
            {
                _currMirrorAxis = GL.Toolbar(_currMirrorAxis, _mirrorAxis);
                _axisOffset = EGL.FloatField("Axis Offset:", _axisOffset);
                PaintEditor.ShowMirrorPlane = EGL.ToggleLeft("Show Mirror Plane", PaintEditor.ShowMirrorPlane);
                PaintEditor.MirrorAxis = _currMirrorAxis;
                PaintEditor.AxisOffset = _axisOffset;
                if (PaintEditor.ShowMirrorPlane) SceneView.lastActiveSceneView.Repaint();
            }
            
            GL.EndVertical();

        }

        private void OnGUI_InputEvents()
        {
            var currEvent = Event.current;
            if (currEvent.isKey)
            {
                if (currEvent.control)
                {
                    if (currEvent.keyCode == KeyCode.Z)
                    {
                        if (currEvent.type == EventType.KeyUp)
                        {
                            //Debug.Log("Ctrl+Z pressed on window focused!");                            
                            _painter.Undo_Undo();
                        }
                        currEvent.Use();
                    }
                    if (currEvent.keyCode == KeyCode.Y)
                    {
                        if (currEvent.type == EventType.KeyUp)
                        {
                            //Debug.Log("Ctrl+Y pressed on window focused!");
                            _painter.Undo_Redo();
                        }
                        currEvent.Use();
                    }
                }
            }
        }

        //GUI section about saving the changes
        void OnGUI_SavePaintedMesh()
        {
            //saving mesh test
            EGL.Space();
            _autoSave = EGL.ToggleLeft("Auto-save at the end of the session", _autoSave);
            EGL.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_autoSave))
            {                
                if (GL.Button(new GUIContent("Save", "Save the modified painted mesh")))
                {
                    if (SaveMeshAsset(false, false))
                    {
                        _painter.Undo_Reset();
                    };
                }
                if (GL.Button(new GUIContent("Save As...", "Save the modified painted mesh")))
                {
                    if (SaveMeshAsset(false, true))
                    {
                        _painter.Undo_Reset();
                    };
                }               
            }
            EGL.EndHorizontal();
        }
        
        public string ConvertToValidFileName(string input, char replacement = '_')
        {
            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            string invalidRegEx = $@"([{invalidChars}]*\.+$)|([{invalidChars}]+)";

            return Regex.Replace(input, invalidRegEx, match =>
            {
                if (match.Value.StartsWith("."))
                {
                    return ".";
                }
                return replacement.ToString();
            });
        }


        //Try to apply the changes to the mesh, return false if the process is aborted.
        public bool SaveMeshAsset(bool optimizeMesh = false, bool forceNewFileName = false)
        {
            var currentMeshFile = AssetDatabase.GetAssetPath(_targetMesh);
            Debug.Log(currentMeshFile);
            var projectPath = Path.GetDirectoryName(Application.dataPath);
            Debug.Log(projectPath);

            //Debug.Log(currentMeshPath);
            //Debug.Log(assetFolderPath);
            if (string.IsNullOrEmpty(currentMeshFile))
            {
                Debug.Log("No asset path found for mesh");
                return false;
            }
            else
            {
                var format = Path.GetExtension(currentMeshFile);                

                if (optimizeMesh)
                {                    
                   // MeshUtility.Optimize(mesh);
                }

                //if not a separated asset already, save as new asset
                var reassign = false;
                string newPath = "";
                string newFileName = "";
                if (format != ".asset" || forceNewFileName)
                {                    
                    if (currentMeshFile.Substring(0,6).ToUpper() == "ASSETS")
                    {
                        newPath = Path.Combine(projectPath, Path.GetDirectoryName(currentMeshFile));
                    } else
                    {
                        newPath = Path.Combine(projectPath, "Assets");                        
                    }
                    newFileName = Path.GetFileNameWithoutExtension(currentMeshFile);
                    if (format == "")
                    {                     
                        if (newFileName == "unity default resources")
                        {
                            newFileName = ConvertToValidFileName(_targetMesh.name);
                        }                        
                    }
                    newFileName += ".asset";
                    //Debug.Log(newPath);
                    //Debug.Log(newFileName);
                    //newFileName = EditorUtility.SaveFilePanel("Save As...", newPath, newFileName,  "asset");
                    newFileName = EditorUtility.SaveFilePanelInProject("Save As...", newFileName, "asset", "Saving as new mesh data asset", newPath);
                    if (newFileName == "") return false;
                    //newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);
                    var mesh = Instantiate(_targetMesh) as Mesh;
                    AssetDatabase.CreateAsset(mesh, newFileName);
                    reassign = true; 
                } else
                {
                    EditorUtility.SetDirty(_targetMesh);
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                if (reassign)
                {                    
                    _targetMesh = AssetDatabase.LoadAssetAtPath<Mesh>(newFileName);
                    if (_skinned)
                    {
                        var meshComp = _targetObject.GetComponent<SkinnedMeshRenderer>();
                        Undo.RecordObject(meshComp, "PolyPaint Mesh");
                        meshComp.sharedMesh = _targetMesh;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(meshComp);
                    }
                    else
                    {
                        var meshComp = _targetObject.GetComponent<MeshFilter>();
                        Undo.RecordObject(meshComp, "PolyPaint Mesh");
                        meshComp.sharedMesh = _targetMesh;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(meshComp);
                    }
                    CheckComponents(_targetObject);
                    PrepareObject();
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
                //Debug.Log("to: " + filePath);
                Type assetType = AssetDatabase.GetMainAssetTypeAtPath(filePath);
                //Debug.Log("Type: " + assetType);
                return (AssetDatabase.LoadAssetAtPath<Mesh>(filePath));
            }
            return null;
        }

        string GetCurrToolName()
        {
            var index = _currToolCode;
            if (index >= 0 && index < _toolNames.Length)
            {
                return _toolNames[index];
            }
            else return "None";
        }

        void SetCurrTool(string name)
        {
            var index = Array.IndexOf(_toolNames, name);
            _currToolCode = index;
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

                //current colors
                var cRect = EGL.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                if (_currToolCode == ToolType.pick && _lastFace != -1)
                {
                    cRect.width = cRect.width / 2;
                    EditorGUI.DrawRect(cRect, _lastPixelColor);
                    cRect.x = cRect.width;
                    EditorGUI.DrawRect(cRect, _tryPickColor);
                }
                else
                {
                    EditorGUI.DrawRect(cRect, _lastPixelColor);
                }
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
            if (_targetObject == null) s = "Object: None selected";
            else
            {
                s = _targetObject.name;
                if (!check.isOk) s += ": Issues...";
            }

            _objectInfoFoldout = EGL.BeginFoldoutHeaderGroup(_objectInfoFoldout, s);
            if (_objectInfoFoldout)
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
            if (_targetMesh == null) 
            {
                s = "NOT FOUND"; 
                isOk = false;
                if (_targetObject != null)
                {
                    var childNameWithMesh = "";
                    var m = _targetObject.GetComponentInChildren<MeshRenderer>();
                    if (m == null)
                    {
                        var ms = _targetObject.GetComponentInChildren<SkinnedMeshRenderer>();
                        if (ms!=null) childNameWithMesh = ms.gameObject.name;
                    } else
                    {
                        childNameWithMesh = m.gameObject.name;
                    }
                    if (childNameWithMesh != "")
                    {
                        s += " (But detected on childs)";
                    }
                }
                
            }
            else s = "ok";
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

        /// <summary>
        /// Does a raycast and updates _prevFace and _lastFace. 
        /// </summary>
        private void DoFaceHit(SceneView sv, Vector2 currMousePos)
        {
            _prevFace = _lastFace;
            _prevFace_Mirror = _lastFace_Mirror;
            (_lastFace, _lastFace_Mirror)  = GetFaceHit(sv, currMousePos, _mirrorCursor);
           // _lastHit.

            //TODO: useful but does nothing yet:
            //if (_prevFace == -1 && _lastFace >= 0)
            //{
            //    OnCursorEnterObject(sv.position);
            //} else if (_prevFace >=0 && _lastFace == -1) 
            //{
            //    OnCursorExitObject(sv.position);
            //}
        }
        
        //Does a face hit, return face, also mirroered face if needed.
        (int,int) GetFaceHit(SceneView sv, Vector2 currMousePos, bool mirrorHit = false)
        {
            int result = -1;
            int result_mirror = -1;
            if (_targetMesh != null)
            {

                _currMousePosCam = currMousePos;
                _currMousePosCam.y = sv.camera.pixelHeight - _currMousePosCam.y;
                var ray = sv.camera.ScreenPointToRay(_currMousePosCam);
                Ray mirror_ray = ray; 

                //var coll = _targetObject.GetComponent<MeshCollider>();
                var coll = _dummyCollider;
                if (coll)
                {
                    if (coll.Raycast(ray, out _lastHit, 100f))
                    {
                        result = _lastHit.triangleIndex;
                    }                    

                    if (mirrorHit)
                    {
                        mirror_ray.direction = MirrorFromPivot(ray.direction, false);
                        mirror_ray.origin = MirrorFromPivot(ray.origin);

                        if (coll.Raycast(mirror_ray, out _lastHit_mirror, 100f))
                        {
                            result_mirror = _lastHit_mirror.triangleIndex;
                        }

                    }

                    UpdateCursorRays(result, result_mirror);

                } else 
                {
                    Debug.LogWarning("No collider to do raycast.");
                }
            }
            return (result, result_mirror);
        }

        public static Vector3 AxisDirection(int axis, Transform transform)
        {
            if (axis == 0) return transform.right;      //X
            else if (axis == 1 ) return transform.up;   //Y
            else return transform.forward;                   //Z
        }

        Vector3 MirrorFromPivot(Vector3 vec, bool isPosition = true) 
        { 
            var plane = PolyPaintWindow.AxisDirection(_currMirrorAxis, _targetObject.transform);
            Vector3 offset = plane * _axisOffset;

            var origin = _targetObject.transform.position + offset;
            if (isPosition)
            {
                return Vector3.Reflect(vec - origin, plane) + origin;
            } else {
                return Vector3.Reflect(vec, plane);
            }
        }

        void UpdateCursorRays(int faceResult,  int mirror_faceResult)
        {
            if (faceResult>0 )
            {
                PaintEditor.CursorRays[0].enabled = true;
                PaintEditor.CursorRays[0].direction = _lastHit.normal;
                PaintEditor.CursorRays[0].origin = _lastHit.point;                 

            } else
            {
                PaintEditor.CursorRays[0].enabled = false;                
            }
            if (mirror_faceResult > 0)
            {
                PaintEditor.CursorRays[1].enabled = true;
                PaintEditor.CursorRays[1].direction = _lastHit_mirror.normal;
                PaintEditor.CursorRays[1].origin = _lastHit_mirror.point;
            }
            else
            {
                PaintEditor.CursorRays[1].enabled = false;
            }
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

                ProcessSceneEvents(scene, id, ev);
            }
        }

        private void OnCursorEnterObject(Rect position)
        {
            //_CursorOverObject = true;
            //Debug.Log("Entering 3D object...");
        }

        private void OnCursorExitObject(Rect position)
        {
            //_CursorOverObject = false;
            //Debug.Log("Exiting 3D object...");
        }

        private void ProcessSceneEvents(SceneView scene, int id, Event ev)
        {          
            
            //When the mouse is moving while the left click is pressed
            if (ev.type == EventType.MouseDrag)
            {
                DoMouseDrag(scene, ev, _currToolCode);
            }
            //When the mouse is moving freely around
            else if (ev.type == EventType.MouseMove)
            {
                DoFaceHit(scene, ev.mousePosition);
                if (_currToolCode == ToolType.pick)
                {
                    if (_lastFace != -1)
                    {
                        TryPick(_lastHit);
                        Repaint();
                    }
                }
                if (_lastFace != _prevFace)
                {
                    BuildCursor();
                }
                scene.Repaint();
            }
            //When the mouse click is pressed down
            else if (ev.type == EventType.MouseDown)
            {
                if (ev.button == 0)
                {
                    DoMouseDownLeftClick(scene, id, ev, _currToolCode);
                }
            }
            //When the mouse click is released
            else if (ev.type == EventType.MouseUp)
            {
                if (ev.button == 0)
                {
                    if (_currToolCode == ToolType.loop)
                    {
                        PaintUsingCursor();
                        _painter.Undo_SaveState();
                    }
                    else if (_currToolCode == ToolType.brush)
                    {
                        _painter.Undo_SaveState();
                    }
                    ReleaseInput(ev);
                    _isMousePressed = false;
                }
            }
            //when a key is pressed down
            else if (ev.type == EventType.KeyDown)
            {
                if (ev.control)
                {
                    if (ev.keyCode == KeyCode.Z)
                    {
                        //catching Ctrl+Z
                        //Debug.Log("Ctrl+Z: Undo");
                        _painter.Undo_Undo();
                    }
                    else if (ev.keyCode == KeyCode.Y)
                    {
                        //Debug.Log("Ctrl+Y: Redo");
                        _painter.Undo_Redo();
                    }
                }
                if (!isAllowedInput(ev)) { ev.Use(); }
            }
            //when a key is released
            else if (ev.type == EventType.KeyUp)
            {
                if (!isAllowedInput(ev)) { ev.Use(); }
            }
        }

        private void TryPick(RaycastHit hit)
        {            
            _tryPickColor = _painter.GetTextureColor(hit.textureCoord);
        }

        private void DoMouseDownLeftClick(SceneView scene, int id, Event ev, int tool)
        {
            AcquireInput(ev, id);
            _isMousePressed = true;
            if (_targetMesh != null)
            {
                DoFaceHit(scene, ev.mousePosition);
                BuildCursor();
                if (tool == ToolType.fill)
                {
                    var anyFace = _lastFace >= 0;
                    var anyMirror = (_mirrorCursor && _lastFace_Mirror >= 0);
                    if (anyFace || anyMirror)
                    {
                        if (anyFace) DoFillTool(_lastFace, _lastUVpick);
                        if (anyMirror) DoFillTool(_lastFace_Mirror, _lastUVpick);
                        _painter.Undo_SaveState();
                    }
                }
                else if (tool == ToolType.pick)
                {
                    PickFromSurface(_lastFace);
                }
                else if (tool == ToolType.loop) { }
                else PaintUsingCursor();
                Repaint();
            }
        }

        private void DoFillTool(int face, Vector2 UV)
        {
            if (_fillVariant == FillVariant.replace)
            {
                _painter.FillReplace(face, UV);
            } else if (_fillVariant == FillVariant.element)
            {
                _painter.FillElement(face, UV);
            } else  _painter.FillPaint(face, UV);
        }

        private void DoMouseDrag(SceneView scene, Event ev, int tool)
        {
            DoFaceHit(scene, ev.mousePosition);
            if (_lastFace != _prevFace)
            {
                BuildCursor();
                if (_isMousePressed)
                {
                    if (tool == ToolType.loop)
                    {
                        //Debug.Log(String.Format("Ctrl+drag: From {0} to {1}", _prevFace, _lastFace));
                        BuildLoopCursor(_prevFace, _lastFace, true);
                        if (_mirrorCursor) BuildLoopCursor(_prevFace_Mirror, _lastFace_Mirror, false);
                        //scene.Repaint();
                        //PaintUsingCursor();
                    }
                    else if (tool == ToolType.pick)
                    {
                        PickFromSurface(_lastFace);
                    }
                    else if (tool == ToolType.brush) PaintUsingCursor();
                }
            }
            scene.Repaint();
            this.Repaint();
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
                    _savedTool = _currToolCode;
                }
                if (ev.shift && ev.control) _currToolCode = ToolType.loop;
                else if (ev.control) _currToolCode = ToolType.fill;
                else if (ev.shift) _currToolCode = ToolType.pick;
                Repaint();
            } else
            {
                if (_anyModifiers)
                {
                    _anyModifiers = false;
                    _currToolCode = _savedTool;
                    //ending modifiers;
                    Repaint();
                }
            }
        }

        public void GetFaceVerts(int face, List<Vector3> verts)
        {
            verts.Clear();
            if (face != -1)
            {
                if (_skinAffected == null)
                {
                    verts.Add(_vertices[face * 3]);
                    verts.Add(_vertices[face * 3 + 1]);
                    verts.Add(_vertices[face * 3 + 2]);
                }
                else
                {
                    verts.Add(_verticesSkinned[face * 3]);
                    verts.Add(_verticesSkinned[face * 3 + 1]);
                    verts.Add(_verticesSkinned[face * 3 + 2]);
                }
            }
        }

        public void GetFaceVerts(int face, List<Vector3> verts, Matrix4x4 transformMat)
        {
            verts.Clear();
            if (face != -1)
            {
                if (_skinAffected == null)
                {
                    verts.Add(transformMat.MultiplyPoint3x4(_vertices[face * 3]));
                    verts.Add(transformMat.MultiplyPoint3x4(_vertices[face * 3 + 1]));
                    verts.Add(transformMat.MultiplyPoint3x4(_vertices[face * 3 + 2]));
                }
                else
                {
                    verts.Add(transformMat.MultiplyPoint3x4(_verticesSkinned[face * 3]));
                    verts.Add(transformMat.MultiplyPoint3x4(_verticesSkinned[face * 3 + 1]));
                    verts.Add(transformMat.MultiplyPoint3x4(_verticesSkinned[face * 3 + 2]));
                }
            }
        }

        private void BuildLoopCursor(int fromFace, int toFace, bool clearPolyCursor)
        {
            var loop = _painter.FindLoop(fromFace, toFace);
            if (_loopTwoWays)
            {
                var loopBack = _painter.FindLoop(toFace, fromFace);
                loop.UnionWith(loopBack);
            }

            if (clearPolyCursor) PaintEditor.PolyCursor.Clear();

            foreach (var f in loop)
            {
                var poly = new PolyFace();
                GetFaceVerts(f, poly, _targetObject.transform.localToWorldMatrix);
                poly.FaceNum = f;
                PaintEditor.PolyCursor.Add(poly);
            }
            Debug.Log("num faces in loop: " + loop.Count.ToString());

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

        /// <summary>
        /// Apply the painting using the current cursor info, that may include multiple faces.
        /// </summary>
        private void PaintUsingCursor()
        {
            foreach (var polyFace in PaintEditor.PolyCursor)
            {
                _painter.SetUV(polyFace.FaceNum, _lastUVpick);
            }
        }

        /// <summary>
        /// Given a face number return a list of vertices in World Coord. System.
        /// </summary>
        private PolyFace CreatePoly(int faceNum)
        {
            var poly = new PolyFace();
            GetFaceVerts(faceNum, poly, _targetObject.transform.localToWorldMatrix);
            poly.FaceNum = faceNum;
            return poly;
        }

        /// <summary>
        /// Builds the data (vertex positions) for the polyline cursor
        /// </summary>
        private void BuildCursor()
        {
            PaintEditor.PolyCursor.Clear();
            if (_lastFace >= 0)
            {                
                PaintEditor.PolyCursor.Add(CreatePoly(_lastFace));
                if (_autoQuads && (_currToolCode == ToolType.brush || _currToolCode == ToolType.loop))
                {
                    var quadBro = _painter.FindQuad(_lastFace);
                    if (quadBro != -1)
                    {                        
                        PaintEditor.PolyCursor.Add(CreatePoly(quadBro));
                    }
                }                
            }
            if (_mirrorCursor&&(_lastFace_Mirror >= 0 ))
            {
                PaintEditor.PolyCursor.Add(CreatePoly(_lastFace_Mirror));
                if (_autoQuads && (_currToolCode == ToolType.brush || _currToolCode == ToolType.loop))
                {
                    var quadBro = _painter.FindQuad(_lastFace_Mirror);
                    if (quadBro != -1)
                    {
                        PaintEditor.PolyCursor.Add(CreatePoly(quadBro));
                    }
                }
            }
        }

        void OnSelectionChange()
        {
            if (_paintingMode) return;
            
            _targetObject = Selection.activeGameObject;
            if (_targetObject != null)
            {
                CheckComponents(_targetObject);
            }
            else
            {
                _targetMesh = null;
                _targetTexture = null;
                _textureData = null;
            }
            Repaint();
        }

        private void CheckComponents(GameObject target)
        {
            _skinned = false;
            var solid = target.GetComponent<MeshFilter>();
            var skinned = target.GetComponent<SkinnedMeshRenderer>();
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

            var r = target.GetComponent<Renderer>();
            if (r != null)
            {
                _targetTexture = null;
                _textureData = null;
                var mat = r.sharedMaterial;
                if (mat != null) _targetTexture = mat.mainTexture;
                if (_targetTexture != null)
                {
                    _textureData = ToTexture2D(_targetTexture);
                }
            }
            else
            {
                _targetTexture = null;
                _textureData = null;
            }
        }

        void PrepareObject()
        {
            if (_targetMesh != null)
            {                
                (_dummyObject, _dummyCollider) = GetDummy(_targetObject);
                
                LogMeshInfo(_targetMesh);
                _painter.SetMeshAndRebuild(_targetMesh, _skinned, _textureData);
                _vertices = _targetMesh.vertices;
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

                    _skinAffected = snapshot;
                    _verticesSkinned = snapshot.vertices;
                }
                _lastFace = -1;                                
                _paintCursor.TargetMesh = _targetMesh;
                _paintCursor.enabled = true;
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
        public static PolyList PolyCursor { get { return _polyCursor; }}
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
                if ( _polyCursor.Count > 0)
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