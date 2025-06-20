using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using static UnityEditor.Progress;
using EGL = UnityEditor.EditorGUILayout;
using EGU = UnityEditor.EditorGUIUtility;
using GUL = UnityEngine.GUILayout;


namespace DAPolyPaint 
{
    /// <summary>
    /// GUI Editor Window for PolyPaint
    /// </summary>
    public class PolyPaintWindow : EditorWindow
    {
        #region ---------- Target Context ----------
        TargetContext _target = new TargetContext(); 
        private class TargetContext
        {
            public GameObject Object;
            public Mesh Mesh;
            public Vector3[] Vertices;
            public bool Skinned;
            public Vector3[] VerticesSkinned;
            public Texture Tex;
            public Texture2D TexData;
            public Material Mat;
            internal Mesh SkinnedMesh;
        }  

        #endregion
        #region ---------- Painting State ----------
        Painter _painter;
        bool _paintingMode;
        bool _isMousePressed = false;
        GameObject _dummyObject;
        MeshCollider _dummyCollider;
        Vector3 _currMousePosCam;
        RaycastHit _lastHit;
        int _currFace; //last face hit by raycast
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
        const string WindowName = "DAPolyPaint";
        readonly GUIContent[] _toolNames = {
            new GUIContent("Brush", "Draw face by face"), 
            new GUIContent("Fill", "Fill areas (Shortcut: Ctrl)"), 
            new GUIContent("Loop", "Draw on mesh loops (Shortcut: Ctrl+Shift)"), 
            new GUIContent("Pick","Get color from mesh (Shortcut: Shift)") 
        };
        readonly string[] _mirrorAxis = new string[] { "X", "Y", "Z" };
        readonly string[] _toolHints = new string[] {
            "Click or Drag over faces", //brush
            "Click a starting face (Shortcut: Ctrl)", //fill
            "Drag over a quad edge (Shortcut: Ctrl+Shift)", //loop
            "Click a face to get color (Shortcut: Shift)" //pick
        };
        int _currToolCode = 0;        
        readonly string[] _fillVariantOptions = new string[] { "flood", "replace", "element", "all" };
        bool _anyModifiers = false;
        int _savedTool;
        const float _statusColorBarHeight = 3;
        public readonly Color ColorStatusReady = Color.green;
        public readonly Color ColorStatusPainting = new Color(1, 0.4f, 0);//orange
        public readonly Color ColorStatusError = Color.red;
        private UIState _ui;
        private bool _isShiftDown;
        private bool _isCtrlDown;

        private struct UIState
        {
            public bool ObjectInfoFoldout;
            public bool AutoQuads;
            public bool LoopTwoWays;
            public bool MirrorCursor;
            public int CurrMirrorAxis;
            public float AxisOffset;
            public int FillVariant; // _fillVariant from PolyPaintWindow
            public bool AutoSave;
            public bool AutoSwitchMaterial;
            public bool AutoShadedWireframe;
            public bool ToolsFoldedOut;
            public SceneView.CameraMode SavedSceneViewCameraMode;
            public bool SavedSceneLighting;
            public bool SavedSceneViewDrawGizmos;
            public int ToolAction;
            public bool SettingsFolded;
            public bool ShadeSelection;
            public bool RestrictToSelected;
            internal Vector3 MoveOffset;
            public bool SelectionCommandsFoldout;

            public void Load(string prefix)
            {
                MoveOffset = new Vector3(1, 0, 0);
                ObjectInfoFoldout = EditorPrefs.GetBool(prefix + "_objectInfoFoldout", true);
                FillVariant = EditorPrefs.GetInt(prefix + "_fillVariant", 0);
                AutoQuads = EditorPrefs.GetBool(prefix + "_autoQuads", true);
                CurrMirrorAxis = EditorPrefs.GetInt(prefix + "_mirrorAxis", 0);
                AxisOffset = EditorPrefs.GetFloat(prefix + "_axisOffset", 0);
                LoopTwoWays = EditorPrefs.GetBool(prefix + "_loopTwoWays", true);
                AutoSave = EditorPrefs.GetBool(prefix + "_autoSave", false); 
                AutoSwitchMaterial = EditorPrefs.GetBool(prefix + "_autoSwitchMaterial", true);
                AutoShadedWireframe = EditorPrefs.GetBool(prefix + "_autoShadedWireframe", true);
                ToolsFoldedOut = EditorPrefs.GetBool(prefix + "_toolsFoldedOut", true);
                ShadeSelection = EditorPrefs.GetBool(prefix + "_shadeSelection", false);
                RestrictToSelected = EditorPrefs.GetBool(prefix + "_restrictToSelected", true);
                SelectionCommandsFoldout = EditorPrefs.GetBool(prefix + "_selectionCommandsFoldout", true);
            }

            public void Save(string prefix)
            {
                EditorPrefs.SetBool(prefix + "_objectInfoFoldout", ObjectInfoFoldout);
                EditorPrefs.SetInt(prefix + "_fillVariant", FillVariant);
                EditorPrefs.SetBool(prefix + "_autoQuads", AutoQuads);
                EditorPrefs.SetInt(prefix + "_mirrorAxis", CurrMirrorAxis);
                EditorPrefs.SetFloat(prefix + "_axisOffset", AxisOffset);
                EditorPrefs.SetBool(prefix + "_loopTwoWays", LoopTwoWays);
                EditorPrefs.SetBool(prefix + "_autoSave", AutoSave);
                EditorPrefs.SetBool(prefix + "_autoSwitchMaterial", AutoSwitchMaterial);
                EditorPrefs.SetBool(prefix + "_autoShadedWireframe", AutoShadedWireframe);
                EditorPrefs.SetBool(prefix + "_toolsFoldedOut", ToolsFoldedOut);
                EditorPrefs.SetBool(prefix + "_shadeSelection", ShadeSelection);
                EditorPrefs.SetBool(prefix + "_restrictToSelected", RestrictToSelected);
                EditorPrefs.SetBool(prefix + "_selectionCommandsFoldout", SelectionCommandsFoldout);
            }
        }

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
            _ui.Load(WindowName);           
            PaintCursorDrawer.ShowMirrorPlane = EditorPrefs.GetBool(WindowName + "ShowMirrorPlane", true);
            _lastUVpick.x = EditorPrefs.GetFloat(WindowName + "_lastUVpick.x", 0.5f);
            _lastUVpick.y = EditorPrefs.GetFloat(WindowName + "_lastUVpick.y", 0.5f);
            //_loopTwoWays = EditorPrefs.GetBool(WindowName + "_loopTwoWays", true);

 
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            _ui.Save(WindowName);         
            EditorPrefs.SetBool(WindowName + "ShowMirrorPlane", PaintCursorDrawer.ShowMirrorPlane);
            EditorPrefs.SetFloat(WindowName + "_lastUVpick.x", _lastUVpick.x);
            EditorPrefs.SetFloat(WindowName + "_lastUVpick.y", _lastUVpick.y);
            // EditorPrefs.SetBool(WindowName + "_loopTwoWays", _loopTwoWays);
        }

        public void OnBeforeAssemblyReload()
        {
            Debug.Log("Assembly Reloading...");
            SetPaintingMode(false);
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
            _target = CheckComponents(_target.Object);
            if (_target.Mesh != null)
            {
                if (_target.Mesh.subMeshCount > 1)
                {
                    var ok = EditorUtility.DisplayDialog(
                        "Mesh is multimaterial, proceed?",
                        $"Object has multiple materials (submeshes == {_target.Mesh.subMeshCount}). Submeshes will be joined and first valid material with texture selected.",
                        "Ok, proceed!",
                        "Cancel"
                        );
                    if (ok)
                    { 
                        var r = _target.Object.GetComponent<Renderer>();
                        //_targetMesh.subMeshCount = 1;
                        r.materials = new Material[] { _target.Mat };
                    }
                    else return;
                }
            }
            if (_target.Tex != null)
            {
                SetPaintingMode(true);
            }
        }

        private void SetPaintingMode(bool enable)
        {
            if (enable)
            {
                PrepareObject(_target, _painter);
                _paintingMode = true;
                _lastPixelColor = _painter.GetTextureColor(_lastUVpick);
                PaintCursorDrawer.CurrPixelColor =  _lastPixelColor;
                SceneView.lastActiveSceneView.Repaint();
                PaintCursorDrawer.PaintMode = true;
            }
            else
            {
                _painter.MoveFacesUndoBack();
                RestoreSkinned();
                _paintingMode = false;
                SceneView.lastActiveSceneView.Repaint();
                PaintCursorDrawer.PaintMode = false;
                if (_paintCursor!=null) _paintCursor.enabled = false;
            }
            SceneViewSettingsMods(enable);
        }

        private void SceneViewSettingsMods(bool enable)
        {
            var sv = SceneView.lastActiveSceneView;
            if (_ui.AutoShadedWireframe)
            {            
                if (sv == null) return;
                if (enable)
                {                   
                    _ui.SavedSceneViewCameraMode = sv.cameraMode;
                    _ui.SavedSceneLighting = sv.sceneLighting;
                    sv.cameraMode = SceneView.GetBuiltinCameraMode(DrawCameraMode.TexturedWire);
                    sv.sceneLighting = false;
                    sv.Repaint();
                }
                else
                {
                    sv.cameraMode = _ui.SavedSceneViewCameraMode;
                    sv.sceneLighting = _ui.SavedSceneLighting;
                    sv.Repaint();
                }
            }
            if (enable)
            {
                _ui.SavedSceneViewDrawGizmos = sv.drawGizmos;
                sv.drawGizmos = true;
            }
            else
            {
                sv.drawGizmos = _ui.SavedSceneViewDrawGizmos;
            }
        }

        void StopPaintMode()
        {
            if (_ui.AutoSave)
            {
                SaveMeshAsset();
            }

            SetPaintingMode(false);
            //DestroyImmediate(_dummyObject);
        }
                
        /// <summary>
        /// Editor Window User Interface - PolyPaint
        /// </summary>
        void OnGUI()
        {
            //processing input events when the window is focused
            OnGUI_InputEvents();

            //Big PAINT MODE button
            _scrollPos = EGL.BeginScrollView(_scrollPos);
            using (new EditorGUI.DisabledScope(_target.Tex == null))
            {
                if (!_paintingMode)
                {
                    if (GUL.Button("START PAINTING")) StartPaintMode();                                           
                }
                else
                {
                    var oldColor = GUI.backgroundColor;
                    GUI.backgroundColor = ColorStatusPainting;
                    if (GUL.Button("END SESSION")) StopPaintMode();                    
                    GUI.backgroundColor = oldColor;                   
                }
            }

            OnGUI_ObjectStatus();
            OnGUI_TexturePalette();
            

            //Painting tools
            using (new EditorGUI.DisabledScope(!_paintingMode))
            {               
                OnGUI_PaintingTools();  
                OnGUI_SelectionCommands();
                OnGUI_UndoRedo();
                OnGUI_SavePaintedMesh();
                OnGUI_Remapping();                
            }

            OnGUI_Settings();
            OnGUI_Help();
            EGL.EndScrollView();
        }

        private void OnGUI_Help()
        {
            if (GUL.Button("Help..."))
            {
                Application.OpenURL("https://github.com/piXelicidio/DAPolyPaint/blob/main/README.md");
            }
        }

        private void OnGUI_Settings()
        {
            GUL.BeginVertical(EditorStyles.textArea);
            _ui.SettingsFolded = EGL.BeginFoldoutHeaderGroup(_ui.SettingsFolded, "Settings");
            if (_ui.SettingsFolded)
            {
                _ui.AutoShadedWireframe = EGL.ToggleLeft(Temp.Content("Auto-Shaded Wireframe", "Set Shaded Wrieframe and Camera lighting on Paint Mode"), _ui.AutoShadedWireframe);
            }
            GUL.EndVertical();
        }

        private void OnGUI_UndoRedo()
        {
            EGL.Space();
            EGL.BeginHorizontal();
            if (GUL.Button(Temp.Content("Undo", "Ctrl+Z")))
            { 
                _painter.Undo_Undo();                
            }
            if (GUL.Button(Temp.Content("Redo", "Ctrl+Y")))
            {
                _painter.Undo_Redo();
            }
            EGL.EndHorizontal();
        }

        private void OnGUI_Remapping()
        {
            EGL.Space();
            GUL.BeginVertical(EditorStyles.textArea);

            var RemapClicked = GUL.Button("Remap to texture in...");                          
            _remapMaterial = (Material) EGL.ObjectField("Target Material", _remapMaterial, typeof(Material), true);
            _ui.AutoSwitchMaterial = EGL.ToggleLeft("Switch Material after remap", _ui.AutoSwitchMaterial);

            if (RemapClicked)
            {
                bool ok = TryRemappingTo(_remapMaterial, out var tex2d, _ui.AutoSwitchMaterial);
                if (ok && _ui.AutoSwitchMaterial)
                {
                    if (_target.Object.TryGetComponent<Renderer>(out var r))
                    {
                        r.material = _remapMaterial;
                        _target.Tex = r.sharedMaterial.mainTexture;
                        _target.TexData = tex2d;                        
                    }
                }
            }

            GUL.EndVertical();
        }

        /// <summary>
        /// Attempts to remap the mesh's current UVs (representing colors from the old texture)
        /// to the nearest matching colors on the new texture.
        /// </summary>
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

        /// <summary>
        /// Converts any Unity Texture object into a readable Texture2D,
        /// enabling pixel data access for color sampling.
        /// </summary>
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

        Texture2D MakeTex(int w, int h, Color col)
        {
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            var tex = new Texture2D(w, h);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }


        /// <summary>
        /// Renders the GUI section for painting tools, and associated tool-specific settings        
        /// </summary>
        private void OnGUI_PaintingTools()
        {
            GUL.BeginVertical(EditorStyles.textArea);
            var actionStr = "painting";
            if (_ui.ToolAction > 0) actionStr = _ui.ToolAction == 1 ? "selecting" : "deselecting";
            _ui.ToolsFoldedOut = EGL.BeginFoldoutHeaderGroup(_ui.ToolsFoldedOut, "Tools (" +actionStr + ")");
            if (_ui.ToolsFoldedOut)
            {
                EGL.Space();

                //TODO: toolbar selected parameter can be set to -1 to unselect all buttons.
                //TOOLBAR of: Brush, Fill, Loop, Pick
                SetCurrTool(GUL.Toolbar(_currToolCode, _toolNames));
                    
                if (_currToolCode >= 0 && _currToolCode <= ToolType.ToolNames.Length)
                {
                   EGL.LabelField(_toolHints[_currToolCode], EditorStyles.miniLabel);
                }
                switch (_currToolCode)
                {
                    case ToolType.fill:
                        _ui.FillVariant = GUL.SelectionGrid(_ui.FillVariant, _fillVariantOptions, 4, EditorStyles.radioButton);
                        break;
                    case ToolType.brush:
                        _ui.AutoQuads = EGL.ToggleLeft("Auto-detect quads", _ui.AutoQuads);
                        break;
                    case ToolType.loop:
                        _ui.LoopTwoWays = EGL.ToggleLeft("Two Ways", _ui.LoopTwoWays);
                        break;
                    case ToolType.pick: break;
                    default:
                        EGL.Space();
                        break;
                }

                if (_currToolCode != ToolType.pick)
                {
                    _ui.MirrorCursor = EGL.ToggleLeft(Temp.Content("Mirror Cursor. Axis:", ""), _ui.MirrorCursor);
                }
                PaintCursorDrawer.MirrorMode = _ui.MirrorCursor;
                if (_ui.MirrorCursor)
                {
                    _ui.CurrMirrorAxis = GUL.Toolbar(_ui.CurrMirrorAxis, _mirrorAxis);
                    _ui.AxisOffset = EGL.FloatField("Axis Offset:", _ui.AxisOffset);
                    PaintCursorDrawer.ShowMirrorPlane = EGL.ToggleLeft("Show Mirror Plane", PaintCursorDrawer.ShowMirrorPlane);
                    PaintCursorDrawer.MirrorAxis = _ui.CurrMirrorAxis;
                    PaintCursorDrawer.AxisOffset = _ui.AxisOffset;
                    if (PaintCursorDrawer.ShowMirrorPlane) SceneView.lastActiveSceneView.Repaint();
                }               

                
            }
            EGL.EndFoldoutHeaderGroup();
            GUL.EndVertical();

            OnGUI_ToolAction();
        }

        
        private void OnGUI_ToolAction()
        {
            EGL.LabelField("Tool Action:", EditorStyles.miniLabel);
            _ui.ToolAction = GUL.Toolbar(_ui.ToolAction, new string[] { "paint", "select" });
            _painter.ToolAction = (ToolAction)_ui.ToolAction;
            PaintCursorDrawer.CurrToolAction = (ToolAction)_ui.ToolAction;
        }

        private void OnGUI_SelectionCommands()
        {

            EGL.Space();
            GUL.BeginVertical(EditorStyles.textArea);
            _ui.SelectionCommandsFoldout = EGL.BeginFoldoutHeaderGroup(_ui.SelectionCommandsFoldout, "Selection commands");
            //EGL.LabelField("Selection actions:", EditorStyles.miniLabel);
            if (_ui.SelectionCommandsFoldout)
            {
                if (GUL.Button("Clear selection"))
                {
                    _painter.SelectedFaces.Clear();
                    RebuildSelection();
                    SceneView.lastActiveSceneView.Repaint();
                }
                _ui.ShadeSelection = EGL.ToggleLeft("Shade selected", _ui.ShadeSelection);
                PaintCursorDrawer.ShadeSelected = _ui.ShadeSelection;
                _ui.RestrictToSelected = EGL.ToggleLeft("Restrict painting to selected", _ui.RestrictToSelected);
                _painter.RestrictToSelected = _ui.RestrictToSelected;
                if (GUL.Button("Move Faces Away"))
                {
                    _painter.MoveFaces(_painter.SelectedFaces, _ui.MoveOffset);
                    _dummyCollider.sharedMesh = _painter.Target;
                    RebuildSelection();
                    SceneView.lastActiveSceneView.Repaint();
                }
                //_ui.MoveOffset = EGL.Vector3Field("Offset", _ui.MoveOffset);
                if (GUL.Button("Move All Back"))
                {
                    _painter.MoveFacesUndoBack();
                    _dummyCollider.sharedMesh = _painter.Target;
                    RebuildSelection();
                    SceneView.lastActiveSceneView.Repaint();
                }
            }
            EGL.EndFoldoutHeaderGroup();
            GUL.EndVertical();
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
            _ui.AutoSave = EGL.ToggleLeft("Auto-save at the end of the session", _ui.AutoSave);

            EGL.BeginHorizontal();

            if (GUL.Button(Temp.Content("Save", "Save the modified painted mesh")))
            {
                _painter.MoveFacesUndoBack();
                if (SaveMeshAsset(false, false))
                {
                    _painter.Undo_Reset();
                }
                ;
            }
            if (GUL.Button(Temp.Content("Save As...", "Save the modified painted mesh")))
            {
                _painter.MoveFacesUndoBack();
                if (SaveMeshAsset(false, true))
                {
                    _painter.Undo_Reset();
                }
                ;
            }
            if (GUL.Button(Temp.Content("Discard!", "Restore to the start of current session")))
            {
                _painter.RestoreOldMesh();
                SetPaintingMode(false);
            }
            EGL.EndHorizontal();

            EGL.BeginHorizontal();
            if (GUL.Button("Export OBJ"))
            {
                _painter.Export();
            }
            if (GUL.Button("Import OBJ"))
            {
                _painter.Import();
                _target.Vertices = _target.Mesh.vertices;
                _dummyCollider.sharedMesh = _target.Mesh;

                _currFace = -1;
                _paintCursor.TargetMesh = _target.Mesh;
                _paintCursor.enabled = true;
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


        /// <summary>
        /// Saves the modified mesh data to an asset file. It can either overwrite the original mesh asset
        /// or create a new mesh asset, reassigning it to the target GameObject's MeshFilter or SkinnedMeshRenderer.
        /// </summary>
        public bool SaveMeshAsset(bool optimizeMesh = false, bool forceNewFileName = false)
        {
            _painter.UpdateSkinnedUVS();

            // Use the correct mesh reference for saving
            Mesh meshToSave = (_target.Skinned && _target.SkinnedMesh != null) ? _target.SkinnedMesh : _target.Mesh;

            var currentMeshFile = AssetDatabase.GetAssetPath(meshToSave);
            Debug.Log(currentMeshFile);
            var projectPath = Path.GetDirectoryName(Application.dataPath);
            Debug.Log(projectPath);

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
                            newFileName = ConvertToValidFileName(meshToSave.name);
                        }                        
                    }
                    newFileName += ".asset";
                    //Debug.Log(newPath);
                    //Debug.Log(newFileName);
                    //newFileName = EditorUtility.SaveFilePanel("Save As...", newPath, newFileName,  "asset");
                    newFileName = EditorUtility.SaveFilePanelInProject("Save As...", newFileName, "asset", "Saving as new mesh data asset", newPath);
                    if (newFileName == "") return false;
                    //newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);
                    var mesh = Instantiate(meshToSave) as Mesh;
                    AssetDatabase.CreateAsset(mesh, newFileName);
                    reassign = true; 
                } else
                {
                    EditorUtility.SetDirty(meshToSave);
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                if (reassign)
                {                    
                    _target.Mesh = AssetDatabase.LoadAssetAtPath<Mesh>(newFileName);
                    if (_target.Skinned)
                    {
                        var mf = _target.Object.GetComponent<MeshFilter>();
                        if (mf != null) DestroyImmediate(mf);
                        var mr = _target.Object.GetComponent<MeshRenderer>();
                        if (mr != null) DestroyImmediate(mr);                      
                        _target.SkinnedMesh = null;
                        var meshComp = _target.Object.GetComponent<SkinnedMeshRenderer>();
                        Undo.RecordObject(meshComp, "PolyPaint Mesh");
                        meshComp.sharedMesh = _target.Mesh;
                        meshComp.enabled = true;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(meshComp);
                    }
                    else
                    {
                        var meshComp = _target.Object.GetComponent<MeshFilter>();
                        Undo.RecordObject(meshComp, "PolyPaint Mesh");
                        meshComp.sharedMesh = _target.Mesh;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(meshComp);
                    }
                    _target = CheckComponents(_target.Object);
                    PrepareObject(_target, _painter);
                }
                return true;
            }
        }



        void SetCurrTool(int toolCode)
        {
            _currToolCode = toolCode;
            PaintCursorDrawer.CurrToolCode = toolCode;
        }


        /// <summary>
        /// Renders the GUI section displaying the current color palette texture.
        /// It allows the user to select a color by clicking or dragging on the texture
        /// and displays the currently picked color.
        /// </summary>
        private void OnGUI_TexturePalette()
        {
            if (_target.Tex)
            {
                var texWidth = EGU.currentViewWidth;

                var rt = EGL.GetControlRect(false, texWidth);
                rt.height = rt.width;
                EditorGUI.DrawPreviewTexture(rt, _target.Tex);
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
                        PaintCursorDrawer.CurrPixelColor = _lastPixelColor;

                        Repaint();
                    }
                }

                //current colors
                var cRect = EGL.GetControlRect(false, EGU.singleLineHeight);
                if (_currToolCode == ToolType.pick && _currFace != -1)
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


        /// <summary>
        /// Renders the GUI section displaying the selected object's readiness for painting,
        /// including a color-coded status bar and a foldout with detailed information or warnings.
        /// </summary>
        private void OnGUI_ObjectStatus()
        {
            var check = BuildObjectStatusInfo();
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
            if (_target.Object == null) s = "Object: None selected";
            else
            {
                s = _target.Object.name;
                if (!check.isOk) s += ": Issues...";
            }

            _ui.ObjectInfoFoldout = EGL.BeginFoldoutHeaderGroup(_ui.ObjectInfoFoldout, s);
            if (_ui.ObjectInfoFoldout)
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

        /// <summary>
        /// Checks the selected GameObject's readiness for painting and returns detailed status information.
        /// </summary>
        private (bool isOk, string info) BuildObjectStatusInfo()
        {
            var info = "";
            var s = "";
            var isOk = true;
            if (_target.Mesh == null)
            {
                s = "NOT FOUND";
                isOk = false;
                if (_target.Object != null)
                {
                    var childNameWithMesh = "";
                    var m = _target.Object.GetComponentInChildren<MeshRenderer>();
                    if (m == null)
                    {
                        var ms = _target.Object.GetComponentInChildren<SkinnedMeshRenderer>();
                        if (ms != null) childNameWithMesh = ms.gameObject.name;
                    } else
                    {
                        childNameWithMesh = m.gameObject.name;
                    }
                    if (childNameWithMesh != "")
                    {
                        s += " (But detected on childs)";
                    }
                }
            } else
            {
                s = "ok";
            }

            info += "Mesh: " + s;
            if (_target.Tex == null) { s = "NOT FOUND"; isOk = false; } else s = _target.Tex.name;
            info += "\nTex: " + s;
            if (isOk)
            {
                //info += "\nFace: " + _lastFace.ToString();
                //info += "\nSetUVs calls: " + _painter.NumUVCalls.ToString();
                info += "\nSkinned: " + _target.Skinned.ToString();
                info += "\nMultimaterial:" + (_target.Mesh.subMeshCount > 1).ToString();
            }
            return (isOk, info);
        }


        /// <summary>
        /// Draws a cross shape on the Editor GUI at a specified position with a given color and size.
        /// </summary>
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
            //if (GUIUtility.hotControl == 0)
            GUIUtility.hotControl = id;
            e.Use();
            EGU.SetWantsMouseJumping(1);
        }

        void ReleaseInput(Event e, int id)
        {
            //if (GUIUtility.hotControl == id)
            GUIUtility.hotControl = 0;
            e.Use();
            EGU.SetWantsMouseJumping(0);
        }

        /// <summary>
        /// Does a raycast and updates _prevFace and _lastFace. 
        /// </summary>
        private void DoFaceHit(SceneView sv, Vector2 currMousePos)
        {
            _prevFace = _currFace;
            _prevFace_Mirror = _lastFace_Mirror;
            (_currFace, _lastFace_Mirror)  = GetFaceHit(sv, currMousePos, _ui.MirrorCursor && (_currToolCode != ToolType.pick) );      
        }

        /// <summary>
        /// Performs a raycast from the mouse position in the Scene View to identify the hit mesh face.
        /// Optionally, it also performs a mirrored raycast to find a corresponding mirrored face.
        /// </summary>
        (int,int) GetFaceHit(SceneView sv, Vector2 currMousePos, bool mirrorHit = false)
        {
            int result = -1;
            int result_mirror = -1;
            if (_target.Mesh != null)
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
            var plane = PolyPaintWindow.AxisDirection(_ui.CurrMirrorAxis, _target.Object.transform);
            Vector3 offset = plane * _ui.AxisOffset;

            var origin = _target.Object.transform.position + offset;
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
                PaintCursorDrawer.CursorRays[0].enabled = true;
                PaintCursorDrawer.CursorRays[0].direction = _lastHit.normal;
                PaintCursorDrawer.CursorRays[0].origin = _lastHit.point;                 

            } else
            {
                PaintCursorDrawer.CursorRays[0].enabled = false;                
            }
            if (mirror_faceResult > 0)
            {
                PaintCursorDrawer.CursorRays[1].enabled = true;
                PaintCursorDrawer.CursorRays[1].direction = _lastHit_mirror.normal;
                PaintCursorDrawer.CursorRays[1].origin = _lastHit_mirror.point;
            }
            else
            {
                PaintCursorDrawer.CursorRays[1].enabled = false;
            }
        }

        /// <summary>
        /// Draws a colored border and a text label around the Scene View window.
        /// </summary>
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
            rt.height = EGU.singleLineHeight;
            rt.x = border * 2;
            rt.y = height - EGU.singleLineHeight - border * 2;
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

                OnScene_ShiftState(ev);

                OnScene_EventProcessing(scene, id, ev);
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

        private void OnScene_EventProcessing(SceneView scene, int id, Event ev)
        {
            switch (ev.type)
            {
                case EventType.MouseDrag:
                    DoMouseDrag(scene, ev, _currToolCode);
                    break;

                case EventType.MouseMove:
                    HandleMouseMove(scene, ev);
                    break;

                case EventType.MouseDown when ev.button == 0:
                    HandleMouseDownLeftClick(scene, id, ev, _currToolCode);
                    break;

                case EventType.MouseUp when ev.button == 0:
                    HandleMouseUp(ev, id);
                    break;

                case EventType.KeyDown:
                    if (ev.control && ev.keyCode == KeyCode.Z) _painter.Undo_Undo();
                    else if (ev.control && ev.keyCode == KeyCode.Y) _painter.Undo_Redo();
                    if (!isAllowedInput(ev)) ev.Use();
                    break;

                case EventType.KeyUp:
                    if (!isAllowedInput(ev)) ev.Use();
                    break;
            }
        }

        private void HandleMouseMove(SceneView scene, Event ev)
        {
            DoFaceHit(scene, ev.mousePosition);

            if (_currToolCode == ToolType.pick && _currFace != -1)
            {
                TryPick(_lastHit);
                Repaint();
            }

            if (_currFace != _prevFace)
                BuildCursor();

            scene.Repaint();
        }

        private void HandleMouseUp(Event ev, int id)
        {
            // both loop and brush need to save
            if (_currToolCode == ToolType.loop || _currToolCode == ToolType.brush)
            {
                if (_currToolCode == ToolType.loop)
                    PaintUsingCursor();

                _painter.Undo_SaveState();

                if (_ui.ToolAction > 0 ) //is not painting
                {
                    RebuildSelection();
                }
            }

            ReleaseInput(ev, id);
            _isMousePressed = false;
        }

        private void RebuildSelection()
        {
            var polylist = PaintCursorDrawer.SelectedFaces;
            polylist.Clear();
            foreach (var face in _painter.SelectedFaces)
            {
                if (face >= 0) 
                {
                    var p = CreatePoly(face);
                    polylist.Add(CreatePoly(face));
                }
            }
        }

        private void HandleMouseDownLeftClick(SceneView scene, int id, Event ev, int tool)
        {
            AcquireInput(ev, id);
            _isMousePressed = true;
            if (_target.Mesh != null)
            {
                if (_ui.ToolAction == (int)ToolAction.Select && !_isShiftDown && !_isCtrlDown)
                {
                    _painter.SelectedFaces.Clear();
                }
                DoFaceHit(scene, ev.mousePosition);
                BuildCursor();
                if (tool == ToolType.fill)
                {
                    ToolFillMouseDown();
                }
                else if (tool == ToolType.pick)
                {
                    PickFromSurface(_currFace);
                }
                else if (tool == ToolType.brush) PaintUsingCursor();

                Repaint();
            }
        }

        private void ToolFillMouseDown()
        {
            var anyFace = _currFace >= 0;
            var anyMirror = (_ui.MirrorCursor && _lastFace_Mirror >= 0);
            if (anyFace || anyMirror)
            {
                if (anyFace) DoFillTool(_currFace, _lastUVpick);
                if (anyMirror) DoFillTool(_lastFace_Mirror, _lastUVpick);
                _painter.Undo_SaveState();
                if (_ui.ToolAction > 0 )
                {
                    RebuildSelection();
                }
            }
        }

        private void DoMouseDrag(SceneView scene, Event ev, int tool)
        {
            DoFaceHit(scene, ev.mousePosition);
            if (_currFace != _prevFace)
            {
                BuildCursor();
                if (_isMousePressed)
                {
                    if (tool == ToolType.loop)
                    {
                        ToolLoopMouseDrag();
                    }
                    else if (tool == ToolType.pick)
                    {
                        PickFromSurface(_currFace);
                    }
                    else if (tool == ToolType.brush)
                    { 
                        PaintUsingCursor();
                        if (_ui.ToolAction > 0 ) RebuildSelection();
                    }
                }
            }
            scene.Repaint();
            this.Repaint();
        }

        private void ToolLoopMouseDrag()
        {
            BuildLoopCursor(_prevFace, _currFace, true);
            if (_ui.MirrorCursor) BuildLoopCursor(_prevFace_Mirror, _lastFace_Mirror, false);
        }

        private void TryPick(RaycastHit hit)
        {            
            _tryPickColor = _painter.GetTextureColor(hit.textureCoord);
            PaintCursorDrawer.TryPickColor = _tryPickColor;
        }

        private void DoFillTool(int face, Vector2 UV)
        {
            switch (_ui.FillVariant)
            {
                case FillVariant.flood: _painter.FillPaint(face, UV); break;
                case FillVariant.replace: _painter.FillReplace(face, UV); break;
                case FillVariant.element: _painter.FillElement(face, UV); break;
                case FillVariant.all: _painter.FullRepaint(UV);break;
            }

            //if (_fillVariant == FillVariant.replace)
            //{
            //    _painter.FillReplace(face, UV);
            //} else if (_fillVariant == FillVariant.element)
            //{
            //    _painter.FillElement(face, UV);
            //} else  _painter.FillPaint(face, UV);
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

        private void OnScene_ShiftState(Event ev)
        {

            if (_isShiftDown != ev.shift)
            {
                _isShiftDown = ev.shift;
                _painter.IsSelectSub = _isShiftDown;
                PaintCursorDrawer.IsShiftDown = _isShiftDown;
            }
            _isCtrlDown = ev.control;
            PaintCursorDrawer.IsCtrlDown = _isCtrlDown;
            if (_ui.ToolAction == (int)ToolAction.Select)
            {
                return;
            }

            if (ev.shift || ev.control)
            {
                if (!_anyModifiers)
                {
                    _anyModifiers = true;
                    //starting modifiers;
                    _savedTool = _currToolCode;
                }
                if (ev.shift && ev.control) SetCurrTool(ToolType.loop);
                else if (ev.control) SetCurrTool(ToolType.fill);
                else if (ev.shift) SetCurrTool(ToolType.pick);
                Repaint();
            } else
            {
                if (_anyModifiers)
                {
                    _anyModifiers = false;
                    SetCurrTool(_savedTool);
                    //ending modifiers;
                    Repaint();
                }
            }
        }


        /// <summary>
        /// Populates a list with the world-space vertex positions of a specified mesh face,
        /// applying a given transformation matrix.
        /// </summary>
        public void GetFaceVerts(int face, List<Vector3> verts, Matrix4x4 transformMat)
        {
            verts.Clear();
            if (face != -1)
            {
                if (!_target.Skinned)
                {
                    verts.Add(transformMat.MultiplyPoint3x4(_target.Vertices[face * 3]));
                    verts.Add(transformMat.MultiplyPoint3x4(_target.Vertices[face * 3 + 1]));
                    verts.Add(transformMat.MultiplyPoint3x4(_target.Vertices[face * 3 + 2]));
                }
                else
                {
                    verts.Add(transformMat.MultiplyPoint3x4(_target.VerticesSkinned[face * 3]));
                    verts.Add(transformMat.MultiplyPoint3x4(_target.VerticesSkinned[face * 3 + 1]));
                    verts.Add(transformMat.MultiplyPoint3x4(_target.VerticesSkinned[face * 3 + 2]));
                }
            }
        }

        /// <summary>
        /// Identifies a continuous loop of quads starting from the shared edge of two input faces.
        /// It then prepares their world-space vertices for rendering as the Scene View painting cursor.
        /// </summary>
        /// <remarks>
        /// Why: To visually show the user the faces that will be affected by the loop painting tool.
        /// </remarks>
        private void BuildLoopCursor(int fromFace, int toFace, bool clearPolyCursor)
        {
            var loop = _painter.FindLoop(fromFace, toFace);
            if (_ui.LoopTwoWays)
            {
                var loopBack = _painter.FindLoop(toFace, fromFace);
                loop.UnionWith(loopBack);
            }

            if (clearPolyCursor) PaintCursorDrawer.PolyCursor.Clear();

            foreach (var f in loop)
            {
                var poly = new PolyFace();
                _painter.GetFaceVerts(f, poly, _target.Object.transform.localToWorldMatrix);
                poly.FaceNum = f;
                PaintCursorDrawer.PolyCursor.Add(poly);
            }

        }

        private void PickFromSurface(int face)
        {
            if (face != -1)
            {
                _lastUVpick = _painter.GetUV(face);                
                _lastPixelColor = _painter.GetTextureColor(_lastUVpick);
                PaintCursorDrawer.CurrPixelColor = _lastPixelColor;
            }
        }


        /// <summary>
        /// Apply the painting using the current cursor info, that may include multiple faces.
        /// </summary>
        private void PaintUsingCursor()
        {
            foreach (var polyFace in PaintCursorDrawer.PolyCursor)
            {
                _painter.Set(polyFace.FaceNum, _lastUVpick);
            }
            _painter.RefreshUVs();
        }

        /// <summary>
        /// Given a face number return a list of vertices in World Coord. System.
        /// </summary>
        private PolyFace CreatePoly(int faceNum)
        {
            var poly = new PolyFace();
            _painter.GetFaceVerts(faceNum, poly, _target.Object.transform.localToWorldMatrix);
            poly.FaceNum = faceNum;
            return poly;
        }

        /// <summary>
        /// Builds the data (vertex positions) for the polyline cursor
        /// </summary>
        private void BuildCursor()
        {
            PaintCursorDrawer.PolyCursor.Clear();
            if (_currFace >= 0)
            {                
                PaintCursorDrawer.PolyCursor.Add(CreatePoly(_currFace));
                if (_ui.AutoQuads && (_currToolCode == ToolType.brush || _currToolCode == ToolType.loop))
                {
                    var quadBro = _painter.FindQuad(_currFace);
                    if (quadBro != -1)
                    {                        
                        PaintCursorDrawer.PolyCursor.Add(CreatePoly(quadBro));
                    }
                }                
            }
            if (_ui.MirrorCursor&&(_lastFace_Mirror >= 0 ))
            {
                PaintCursorDrawer.PolyCursor.Add(CreatePoly(_lastFace_Mirror));
                if (_ui.AutoQuads && (_currToolCode == ToolType.brush || _currToolCode == ToolType.loop))
                {
                    var quadBro = _painter.FindQuad(_lastFace_Mirror);
                    if (quadBro != -1)
                    {
                        PaintCursorDrawer.PolyCursor.Add(CreatePoly(quadBro));
                    }
                }
            }
        }

        void OnSelectionChange()
        {
            if (_paintingMode) return;
            
            var GO = Selection.activeGameObject;
            if (GO != null)
            {
                _target = CheckComponents(GO);
            }
            else
            {
                _target.Mesh = null;
                _target.Tex = null;
                _target.TexData = null;
            }
            Repaint();
        }

        /// <summary>
        /// Examines the provided GameObject to identify its mesh (static or skinned) and material texture,
        /// storing references to these components for use by the painting tool.
        /// </summary>
        private TargetContext CheckComponents(GameObject targetGO)
        {
            var t = new TargetContext();
            if (targetGO == null) return t;
            t.Object = targetGO;
            t.Skinned = false;
            t.SkinnedMesh = null;
            var solid = targetGO.GetComponent<MeshFilter>();
            var skinned = targetGO.GetComponent<SkinnedMeshRenderer>();
            if (solid != null)
            {
                t.Mesh = solid.sharedMesh;                
            }
            else if (skinned != null)
            {
                t.Mesh = skinned.sharedMesh;                
                t.Skinned = true;
            }
            else
            {
                t.Mesh = null;
            }

            var r = targetGO.GetComponent<Renderer>();
            if (r != null)
            {
                t.Tex = null;
                t.TexData = null;
                (t.Mat, t.Tex) = GetFirstValidMaterialTexture(r);
                if (t.Tex != null)
                {
                    t.TexData = ToTexture2D(t.Tex);
                }
            }
            else
            {
                t.Tex = null;
                t.TexData = null;
            }
            return t;
        }

        private (Material, Texture) GetFirstValidMaterialTexture(Renderer r)
        {
            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) return (null, null);
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null)
                {
                    var t = mats[i].mainTexture;
                    if (t != null) return (mats[i], t);
                }
            }
            return (null, null);
        }

        /// <summary>
        /// Initializes the painting session by setting up internal data structures,
        /// creating a hidden mesh collider for raycasting, and configuring the painter with the target mesh data.
        /// For skinned meshes, it bakes a snapshot to support deformations.
        /// </summary>
        void PrepareObject(TargetContext target, Painter painter)
        {
            if (target.Mesh != null)
            {                
                (_dummyObject, _dummyCollider) = GetDummy(target.Object);
                LogMeshInfo(target.Mesh);
                if (target.Skinned)
                {
                    var smr = target.Object.GetComponent<SkinnedMeshRenderer>();
                    var snapshot = new Mesh();
                    smr.BakeMesh(snapshot, true);
                    target.SkinnedMesh = target.Mesh;
                    target.Mesh = snapshot;
                    smr.enabled = false;
                    var mf = target.Object.AddComponent<MeshFilter>();
                    mf.sharedMesh = snapshot;
                    var mr = target.Object.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = smr.sharedMaterials;                    
                }
                painter.SetMeshAndRebuild(target.Mesh, target.SkinnedMesh, target.TexData);
                target.Vertices = target.Mesh.vertices;
                _dummyCollider.sharedMesh = target.Mesh;
                
                _currFace = -1;                                
                _paintCursor.TargetMesh = target.Mesh;
                _paintCursor.enabled = true;
            }
            else
            {
                Debug.LogWarning("targetMeshs should be valid before calling PrepareObject()");
            }
        }

        void RestoreSkinned()
        {
            if (_target.SkinnedMesh != null) 
            {
                _painter.UpdateSkinnedUVS();
                var smr = _target.Object.GetComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = _target.SkinnedMesh;
                smr.enabled = true;
                var mf = _target.Object.GetComponent<MeshFilter>();
                if (mf != null) DestroyImmediate(mf);                
                var mr = _target.Object.GetComponent<MeshRenderer>();
                if (mr != null) DestroyImmediate(mr);
                _target.Mesh = _target.SkinnedMesh;
                _target.SkinnedMesh = null;
            }
        }

        /// <summary>
        /// Creates a dummy object to be used as a raycast target
        /// </summary>
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

   

    }

    
}