﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;


namespace DAPolyPaint 
{
    /// <summary>
    /// Deals with Mesh and UV painting functions. Can be used in play mode.
    /// It prepares mesh data, applies UV-based colors to faces, handles topological relationships.
    /// </summary>
    //Doesn't know anything about UI or GameObjects. 
    //Many field/variable names are selected based on the 3ds Max Poly Paint tool implementation
    //and Maxscript methods/properties naming (e. g. NumVerts, NumFaces).
    public class Painter
    {
        Mesh _targetMesh;
        Mesh _skinnedMesh;
        List<Vector2> _UVs;
        Vector3[] _vertices;            //all non optimized vertices of the actual mesh
        Vector3[] _verticesBackup = null;
        Vector3[] _indexedVerts;
        List<int>[] _facesUsingVert;
        int[] _triangles;               // the raw mesh triangles (one‐to‐one per duplicated vertex) points to _vertices
        int[] _indexedFaces;            // triangles pointing to indexed vertices _indexedVerts
        float[] _angles;                //angle of each triangle corners
        List<FaceLink>[] _AllFaceConnections;
        FaceLink[,] _faceConnectionsBySide;
        List<List<Vector2>> _undoLevels;
        HashSet<int> _selectedFaces = new HashSet<int>();
        HashSet<int> _hiddenFaces = new HashSet<int>();
        int _undoPos;
        int _undoSequenceCount;
        int _channel = 0;
        Texture2D _textureData;
        MeshCopy _meshCopy;
        private MeshCopy _skinnedMeshCopy;

        public ToolAction ToolAction { get; set; }
        public bool IsSelectSub {get; set;}
        public Mesh Target { get { return _targetMesh; } }
        public int NumUVCalls { get; private set; }
        public int NumFaces { get { return _triangles.Length / 3; } }
        public int NumVerts { get { return _vertices.Length; } }
        public float QuadTolerance { get; set; }
        public HashSet<int> SelectedFaces { get { return _selectedFaces; } }
        public bool RestrictToSelected { get; set; } = true;
        public Vector3[] Vertices { get { return _vertices; }}


        public Painter()
        {
            QuadTolerance = 120;
            _undoLevels = new List<List<Vector2>>();
            IsSelectSub = false;
        }

        public void Export(string filePath)
        {
            ObjFormat.Export(
                _indexedVerts,
                _indexedFaces,
                _UVs,
                _triangles,
                filePath
            );
        }

        public void Import(string filePath)
        {
            ObjFormat.ImportToMesh(filePath, _targetMesh);
            SetMeshAndRebuild(_targetMesh, null, _textureData);
        }

        /// <summary>
        /// Set the mesh to be painted and rebuild the internal data structures.
        /// </summary>        
        //THIS NEED to be called first before any other painting function. TODO: maybe put in the constructor?
        public void SetMeshAndRebuild(Mesh target, Mesh skinnedMesh, Texture2D texture)
        {
            _targetMesh = target;
            _skinnedMesh = skinnedMesh;
            _textureData = texture;
            _skinnedMeshCopy = null;
            _verticesBackup = null;
            RebuildMeshForPainting();            
        }

        /// <summary>
        /// Transforms the input mesh into an internal, per-face representation optimized for UV painting,
        /// and builds all necessary topological data structures (e.g., face graph, indexed vertices).
        /// </summary>
        void RebuildMeshForPainting()
        {
            var t = Environment.TickCount;                        

            _meshCopy = new MeshCopy(_targetMesh); 
            if (_skinnedMesh != null)  _skinnedMeshCopy = new MeshCopy(_skinnedMesh);

            // If UVs are missing or the wrong size, create default UVs
            if (_meshCopy.UVs == null || _meshCopy.UVs.Count != _meshCopy.vertices.Count)
            {
                _meshCopy.UVs = new List<Vector2>(_meshCopy.vertices.Count);
                for (int i = 0; i < _meshCopy.UVs.Count; i++)
                {
                    // Simple planar mapping as a fallback (can be improved)
                    _meshCopy.UVs.Add(new Vector2(_meshCopy.vertices[i].x, _meshCopy.vertices[i].z));
                }
            }


            var newVertices = new List<Vector3>();
            var newUVs = new List<Vector2>();
            var newTris = new List<int>();
            var newNormals = new List<Vector3>();
            var newBW = new BoneWeight[0];
            if (_skinnedMesh) newBW = new BoneWeight[_meshCopy.tris.Count];

            var newVerticesSkinned = new List<Vector3>();
            var newNormalsSkinned = new List<Vector3>();

            //no more shared vertices, each triangle will have its own 3 vertices.
            for (int i = 0; i < _meshCopy.tris.Count; i++)
            {
                var idx = _meshCopy.tris[i];
                newTris.Add(i);
                newVertices.Add(_meshCopy.vertices[idx]);
                newNormals.Add(_meshCopy.normals[idx]);
                //also UVs but the 3 values will be the same
                newUVs.Add(_meshCopy.UVs[_meshCopy.tris[i - i % 3]]);

                if (_skinnedMesh)
                {
                    newVerticesSkinned.Add(_skinnedMeshCopy.vertices[idx]);
                    newNormalsSkinned.Add(_skinnedMeshCopy.normals[idx]);
                    newBW[i] = _skinnedMeshCopy.boneWeights[idx];
                }
            }


            _UVs = newUVs; //keep ref for painting
            _vertices = newVertices.ToArray();
            //_verticesBackup = newVertices.ToArray();
            _triangles = newTris.ToArray();



            string s;
            s = "<b>Rebuild, Elapsed:</b> " + (Environment.TickCount - t).ToString() + "ms - ";
            t = Environment.TickCount;

            Indexify();
            //Indexify_boundBox();

            s += "<b>Indexify, Elapsed:</b> " + (Environment.TickCount - t).ToString() + "ms - ";
            t = Environment.TickCount;

            CalcAngles();
            s += "<b>CalcAngles, Elapsed:</b> " + (Environment.TickCount - t).ToString() + "ms - ";
            t = Environment.TickCount;

            BuildFaceGraph();
            Undo_Reset();
            s += "<b>BuildFaceGraph, Elapsed:</b> " + (Environment.TickCount - t).ToString() + "ms";
            //Debug.Log(s);

            //updating mesh with new distribution of vertices
            _targetMesh.Clear();
            if (newVertices.Count > 60000) _targetMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            if (_targetMesh.subMeshCount > 1) _targetMesh.subMeshCount = 1; //NOT support for multiple submeshes.
            _targetMesh.SetVertices(newVertices);
            _targetMesh.SetUVs(_channel, newUVs);
            _targetMesh.SetTriangles(newTris, 0);
            _targetMesh.SetNormals(newNormals);

            if (_skinnedMesh)
            {
                _skinnedMesh.Clear();
                if (newVertices.Count > 60000) _skinnedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                if (_skinnedMesh.subMeshCount > 1) _skinnedMesh.subMeshCount = 1;
                _skinnedMesh.SetVertices(newVerticesSkinned);
                _skinnedMesh.SetUVs(_channel, newUVs); //same as non-skinned
                _skinnedMesh.SetTriangles(newTris, 0);
                _skinnedMesh.SetNormals(newNormalsSkinned);
                //TODO(maybe later): Use the more complex new SetBoneWeights to support more than 4 bones per vertex
                _skinnedMesh.boneWeights = newBW;
            }


        }

        public void UpdateSkinnedUVS()
        {
            if (_skinnedMesh != null)
            {
                _skinnedMesh.SetUVs(_channel, _UVs);
            }
        }

        //After calling RestoreOldMesh, all other painting functions will fail, unless you call RebuildMeshForPainting again.
        public void RestoreOldMesh()
        {
            Debug.Log("Restoring mesh...");
            _meshCopy.PasteTo(_targetMesh);
        }


        /// <summary>
        /// Creates an optimized, indexed view of the mesh's vertices and faces,
        /// mapping original (un-indexed) vertices to unique shared vertex indices for topological operations.
        /// </summary>        
        //Needed for geting relationships between faces, edges, verts.
        //_indexedVerts store unique vertices
        //_indexedFaces every 3 values are 3 indexes pointing to _indexedVerts values.
        //Number of faces keep the same, and keep same order. _indexedFaces[n] -> corresponds to -> _triangles[n]
        private void Indexify()
        {
            var sharedVerts = new List<Vector3>();
            var vertsDir = new Dictionary<Vector3, int>();
            var indexReplace = new int[_triangles.Length];
            var facesUsingVert = new List<List<int>>();
            _indexedFaces = new int[_triangles.Length];
            var dumbSymmetryTest = 0;

            for (int i = 0; i < NumVerts; i++)
            {
                var v = _vertices[i];
                int idx;
                if (!vertsDir.TryGetValue(v, out idx))
                {
                    idx = sharedVerts.Count;
                    vertsDir.Add(v, idx);
                    sharedVerts.Add(v);
                    var list = new List<int>();
                    list.Add(i / 3);
                    facesUsingVert.Add(list);
                    if (v.x > 0) dumbSymmetryTest++; else if (v.x < 0) dumbSymmetryTest--;
                }
                indexReplace[i] = idx;
                facesUsingVert[idx].Add(i / 3);
            }

            for (int i = 0; i < _triangles.Length; i++)
            {
                _indexedFaces[i] = indexReplace[_triangles[i]];
            }
            _indexedVerts = sharedVerts.ToArray();
            _facesUsingVert = facesUsingVert.ToArray();

            //Tested with casual_Female_G model when from 4k verts to originallly 824 verts, just like 3ds Max version.
            //Debug.Log(String.Format("NumVerts before:{0} after:{1} dumbSymmetryTest:{2}", NumVerts, sharedVerts.Count, dumbSymmetryTest));
        }

        private void InvalidFaceRemoval()
        {
            for (int i = 0; i < _indexedFaces.Length; i += 3)
            {
                var idx0 = _indexedFaces[i];
                var idx1 = _indexedFaces[i + 1];
                var idx2 = _indexedFaces[i + 2];
            }
        }

        /// <summary>
        /// Calculates the angle at each vertex corner for every triangle in the indexed mesh,
        /// used primarily for quad detection algorithms.
        /// </summary>
        private void CalcAngles()
        {
            _angles = new float[_indexedFaces.Length];
            for (var f = 0; f < NumFaces; f++)
            {
                for (var i = 0; i < 3; i++)
                {
                    var thisIdx = f * 3 + i;
                    var nextIdx = f * 3 + (i + 1) % 3;
                    var prevIdx = f * 3 + ((i + 3) - 1) % 3;
                    var v1 = _indexedVerts[_indexedFaces[nextIdx]] - _indexedVerts[_indexedFaces[thisIdx]];
                    var v2 = _indexedVerts[_indexedFaces[prevIdx]] - _indexedVerts[_indexedFaces[thisIdx]];
                    _angles[f * 3 + i] = Vector3.Angle(v1, v2);
                }
            }
        }

        ///<summary>Return array of Verts indices, no validations</summary>               
        private int[] GetFaceVertIdxs(int face)
        {
            return new int[] {
                _indexedFaces[face*3],
                _indexedFaces[face*3+1],
                _indexedFaces[face*3+2]
            };
        }

        /// <summary>
        /// Maps a pair of local vertex indices (0-2) from a triangle to a unique integer
        /// representing one of the triangle's three sides (0, 1, or 2).
        /// </summary>           
        private int GetTriangleSide(int p1, int p2)
        {
            var low = Math.Min(p1, p2);
            var high = Math.Max(p1, p2);
            if (low == 0 && high == 2)
            {
                return 2;
            } else
            {
                return low;
            }
        }

        /// <summary>
        /// Builds a graph of relationships between adjacent faces in the mesh,
        /// identifying shared edges and storing connection details in FaceLink objects.
        /// </summary>
        /// <remarks>
        /// assumes Indexify() was called before
        /// </remarks>
        private void BuildFaceGraph()
        {
            int overlappingFaces = 0;

            void FindCoincidences(int face1, int face2)
            {
                if (face1 == face2) return;
                var count = 0;
                int[] pos = new int[3];
                int[] posOther = new int[3];
                for (int p1 = 0; p1 < 3; p1++)
                {
                    for (int p2 = 0; p2 < 3; p2++)
                    {
                        if (_indexedFaces[p1 + face1 * 3] == _indexedFaces[p2 + face2 * 3])
                        {
                            pos[count] = p1;
                            posOther[count] = p2;
                            count++;
                            break;
                        }
                    }
                }
                //ignoring single shared vertices.
                //ignoring three shared vertices = overlaping face.
                if (count == 2)
                {
                    //there is connection:
                    var link = new FaceLink();
                    link.with = face2;
                    link.p1 = pos[0];                  //shared face points 
                    link.p2 = pos[1];
                    link.edge.v1 = _indexedFaces[face1 * 3 + pos[0]];
                    link.edge.v2 = _indexedFaces[face1 * 3 + pos[1]];
                    link.side = GetTriangleSide(pos[0], pos[1]);
                    link.pOut = 3 - (pos[0] + pos[1]); //point left out

                    var otherFaceSide = GetTriangleSide(posOther[0], posOther[1]);
                    var otherLink = _faceConnectionsBySide[face2, otherFaceSide];

                    bool otherOk = otherLink == null || otherLink.with == face1;

                    //my face side is unlinked?                    
                    if (_faceConnectionsBySide[face1, link.side] == null && otherOk)
                    {
                        //other face has this side unlinked or linked to me?                    
                        if (_AllFaceConnections[face1].Count < 3)
                        {
                            _AllFaceConnections[face1].Add(link);
                            _faceConnectionsBySide[face1, link.side] = link;
                        }
                        else
                        {
                            Debug.LogWarning("Graph trying to add a 4th connection to a face!");
                        }
                    }
                }
                else if (count == 3) 
                {
                    overlappingFaces++;
                }
            }

            //var sum = 0.0f;
            _AllFaceConnections = new List<FaceLink>[NumFaces];
            _faceConnectionsBySide = new FaceLink[NumFaces,3];
            for (int i = 0; i < NumFaces; i++)
            {
                _AllFaceConnections[i] = new List<FaceLink>();
            }

            //Narrows the search by faces that already  have at least a vertex in common
            var myVerts = new int[3];
            for (int i = 0; i < NumFaces; i++)
            {
                var nearFaces = GetFacesUsingVerts(GetFaceVertIdxs(i));
                foreach (int f in nearFaces)
                {
                    FindCoincidences(i, f);
                }
            }

            int badConnections = 0;
            //clean bad backlinks pass, from weird meshes
            for (int thisFace = 0; thisFace < NumFaces; thisFace++)
            {
                var thisFaceLinks = _AllFaceConnections[thisFace];
                //sum += links.Count;
                for (int j = thisFaceLinks.Count - 1; j >= 0; j--)
                {
                    var backlink = _AllFaceConnections[thisFaceLinks[j].with].Find(x => x.with == thisFace);
                    if (backlink == null)
                    {
                        badConnections ++;
                        var flink = thisFaceLinks[j];
                        thisFaceLinks.RemoveAt(j);
                        //link should be removed also from _faceConnectionsBySide
                        for (int i = 0; i < 3; i++)
                        {
                            if (_faceConnectionsBySide[thisFace, i] == flink) _faceConnectionsBySide[thisFace, i] = null;
                        }
                    }

                }
            }

            //backlinks
            for (int thisFace = 0; thisFace < NumFaces; thisFace++)
            {
                var thisFaceLinks = _AllFaceConnections[thisFace];
                //sum += links.Count;
                for (int j = thisFaceLinks.Count - 1; j >= 0; j--)
                {
                    var backlink = _AllFaceConnections[thisFaceLinks[j].with].Find(x => x.with == thisFace);
                    if (backlink != null)
                    {
                        backlink.backLinkIdx = j;
                    } else
                    {
                        Debug.LogWarning("Backlink not found! Shouldn't be happening.");
                        DebugFace(thisFace);
                        DebugFace(thisFaceLinks[j].with);                        
                    }

                }
            }
            if (overlappingFaces > 0 || badConnections > 0)
            {
                Debug.LogWarning($"Found funky mesh topology: Multiple overlapping faces, and veritces. Some tools my not not perform 100% correctly.");
            }
        }

        void DebugFace(int faceIdx)
        {
            Debug.Log("Face: " + faceIdx.ToString());
            Debug.Log(String.Format("v0: {0}", _indexedFaces[faceIdx]));
            Debug.Log(String.Format("v1: {0}", _indexedFaces[faceIdx + 1]));
            Debug.Log(String.Format("v2: {0}", _indexedFaces[faceIdx + 2]));

        }

        public HashSet<int> GetFacesUsingVerts(int[] verts)
        {
            var result = new HashSet<int>();
            for (int i = 0; i < verts.Length; i++)
            {
                result.UnionWith(_facesUsingVert[verts[i]]);
            }
            return result;
        }




        /// <summary>
        /// Finds the adjacent face that, when combined with the input face, forms a quad,
        /// based on angle proximity to 90 degrees.
        /// </summary>
        /// <returns>The index of the detected quad-brother face, or -1 if not found.</returns>
        /// <param name="face"></param>
        /// <param name="tolerance"></param>
        /// <returns>Face index or -1 if not found.</returns>
        public int FindQuad(int face)
        {
            if (face == -1) return -1;
            var best = -1;
            var nearBest = QuadTolerance;

            for (int i = 0; i < _AllFaceConnections[face].Count; i++)
            {
                var linkTo = _AllFaceConnections[face][i];
                var faceOther = linkTo.with;
                var linkFrom = _AllFaceConnections[faceOther][linkTo.backLinkIdx]; 
                if (linkFrom.with != face)
                {
                    Debug.LogWarning("Bad backlinkIdx!");
                    continue;
                }


                var angles = new float[4];
                //the two corners that are not part of the common edge
                angles[0] = _angles[face * 3 + linkTo.pOut];
                angles[1] = _angles[faceOther * 3 + linkFrom.pOut];
                //shared corners added togather, sum should be close to 90... to be a quad
                angles[2] = _angles[face * 3 + linkTo.p1] + _angles[faceOther * 3 + linkFrom.p1];
                angles[3] = _angles[face * 3 + linkTo.p2] + _angles[faceOther * 3 + linkFrom.p2];

                var near = 0f;
                for (int j = 0; j < 4; j++)
                {
                    near += Math.Abs(90f - angles[j]);
                }
                if (near < nearBest)
                {
                    best = faceOther;
                    nearBest = near;
                }
            }
            return best;
        }


        public void Set(int face, Vector2 uvc)
        {
            switch (ToolAction)
            {
                case ToolAction.Paint:
                    if (face >= 0) {
                        if (!RestrictToSelected || _selectedFaces.Count == 0 || _selectedFaces.Contains(face) )
                        {
                            _UVs[face * 3] = uvc;
                            _UVs[face * 3 + 1] = uvc;
                            _UVs[face * 3 + 2] = uvc;
                            NumUVCalls++;
                        }
                    }
                    break;
                case ToolAction.Select when !IsSelectSub:
                    _selectedFaces.Add(face);
                    break;
                case ToolAction.Select when IsSelectSub:
                    _selectedFaces.Remove(face);
                    break;
            }
        }


        public void RefreshUVs()
        {
            _targetMesh.SetUVs(_channel, _UVs);
        }

        public void MoveFaces(HashSet<int> faces, Vector3 vec)
        {
            if (_verticesBackup == null)
            {
                _verticesBackup = new Vector3[_vertices.Length];
                Array.Copy(_vertices, _verticesBackup, _vertices.Length);
            }
            foreach (var face in faces)
            {
                _vertices[_triangles[face * 3]] += vec;
                _vertices[_triangles[face * 3+1]] += vec;
                _vertices[_triangles[face * 3+2]] += vec;
            }
            _targetMesh.SetVertices(_vertices);
        }

        //A face actually have 3 UV values but, here all are the same
        //so only one is enough
        public Vector2 GetUV(int face)
        {
            if (face != -1 && face < _UVs.Count)
            {
                return _UVs[face * 3];
            } else
            {
                return new Vector2(-1, -1);
            }
        }

        public void FullRepaint(Vector2 uvc)
        {
            for (int i = 0; i < NumFaces; i++)
            {
                Set(i, uvc);
            }
            RefreshUVs();
        }



        public List<FaceLink> GetFaceLinks(int face)
        {
            return _AllFaceConnections[face];
        }

        /// <summary>
        /// Fills a contiguous region of faces with a specified UV color,
        /// either by matching the starting face's original color or by simply filling all connected faces.
        /// </summary>
        public void FillPaint(int startFace, Vector2 uvc, bool DontCheckColor = false)
        {
            var bk_pixelColor = GetTextureColor(GetUV(startFace));
            var border = new HashSet<int>();
            border.Add(startFace);
            var neighbors = new HashSet<int>();
            var iters = 0;
            var visited = new bool[NumFaces];

            var t = Environment.TickCount;

            do
            {
                //paint border
                foreach (int face in border)
                {
                    Set(face, uvc);
                    visited[face] = true;
                }
                //find neighbors
                neighbors.Clear();

                foreach (int face in border)
                {
                    var faceLinks = GetFaceLinks(face);
                    foreach (var link in faceLinks)
                    {
                        if (DontCheckColor)
                        {
                            if (!visited[link.with])
                            {
                                neighbors.Add(link.with);
                            }
                        }
                        else
                        {
                            if (GetTextureColor(_UVs[link.with * 3]) == bk_pixelColor && !visited[link.with])
                            {
                                neighbors.Add(link.with);
                            }
                        }
                    }
                }
                //swap                
                (border, neighbors) = (neighbors, border);

                iters++;
            } while (border.Count > 0 && iters < 1000);  //TODO: Improve this arbitrary limit
            RefreshUVs(); 

            //Debug.Log(String.Format("Iters:{0} Elapsed:{1}ms", iters, Environment.TickCount - t));
        }

        public Color GetTextureColor(Vector2 uv)
        {
            if (_textureData == null)
                return Color.white.linear;
            else
                return _textureData.GetPixel((int)(uv.x * _textureData.width), (int)(uv.y * _textureData.height)).linear;
        }

        /// <summary>
        /// if a link to the other face is not found return null
        /// </summary>
        public FaceLink FindLink(int fromFace, int toFace)
        {
            if (fromFace != -1 && toFace != 1)
            {
                foreach (var fl in _AllFaceConnections[fromFace])
                {
                    if (fl.with == toFace)
                    {
                        return fl;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Identifies a continuous loop of quads (pairs of adjacent faces) starting from the shared edge of two input faces.
        /// </summary>
        public HashSet<int> FindLoop(int f1, int f2)
        {
            if (f1 == -1 || f2 == -1) return new HashSet<int>();

            var result = new HashSet<int>();

            //confirm they are actually adjacents            
            var linkToF2 = FindLink(f1, f2);

            //is F2 part of a Quad?
            var f2_quadBro = FindQuad(f2);

            //is F1 my Quad brother?
            bool weQuad = f2_quadBro == f1;

            if (linkToF2 != null && f2_quadBro != -1 && !weQuad)
            {
                result.Add(f2);
                bool noOverlaps = true;
                //Debug.Log("Let's find the loop!");
                var breakLimit = 1000; //TODO: arbitrary temporal safe limit, should go away with better solution
                do
                {

                    var SharedEdge_f1_f2 = linkToF2.edge;
                    var jumpToFace = -1;
                    foreach (FaceLink fl in _AllFaceConnections[f2_quadBro])
                    {
                        if (!fl.edge.HaveSharedVerts(SharedEdge_f1_f2))
                        {
                            //found oppsite edge
                            jumpToFace = fl.with;
                            //AddFace(jumpToFace);
                            break;
                        };
                    }
                    //if (jumpToFace == -1) Debug.LogWarning("No opposite side found!");
                    f1 = f2_quadBro;
                    f2 = jumpToFace;
                    noOverlaps = result.Add(f1) && result.Add(f2);
                    f2_quadBro = FindQuad(f2);
                    weQuad = f2_quadBro == f1;
                    linkToF2 = FindLink(f1, f2);
                    breakLimit--;
                } while (breakLimit > 0 && f2_quadBro != -1 && !weQuad && linkToF2 != null && noOverlaps);
            }

            return result;
        }

        public void Undo_Reset()
        {
            _undoLevels.Clear();
            _undoPos = -1;
            _undoSequenceCount = 0;
            Undo_SaveState();
        }

        public void Undo_SaveState()
        {
            if (true)
            {
                var copy = new List<Vector2>(_UVs);
                _undoPos++;
                _undoSequenceCount = 0;
                if (_undoPos == _undoLevels.Count)
                {
                    _undoLevels.Add(copy);
                } else
                {
                    _undoLevels[_undoPos] = copy;
                }

            }
        }

        public void Undo_Undo()
        {
            if (_undoPos > 0)
            {
                var state = _undoLevels[_undoPos - 1];
                for (int i = 0; i < state.Count; i++)
                {
                    _UVs[i] = state[i];
                }
                RefreshUVs();
                _undoPos--;
                _undoSequenceCount++;
            }
            //Debug.Log(String.Format("cursor{0} count{1}", _undoPos, _undoLevels.Count));
        }

        public void Undo_Redo()
        {
            if (_undoSequenceCount > 0)
            {
                _undoSequenceCount--;
                _undoPos++;
                var state = _undoLevels[_undoPos];
                for (int i = 0; i < state.Count; i++)
                {
                    _UVs[i] = state[i];
                }
                RefreshUVs();
            }
        }

        public bool isModified()
        {
            return _undoPos > 0;
        }

        /// <summary>
        /// Paints all faces in the mesh that currently have the same color as the specified starting face with the new UV color.
        /// </summary>
        public void FillReplace(int face, Vector2 UV)
        {
            //Debug.Log("Replacing...");
            if (face > 0 && face < NumFaces)
            {
                var pick = GetTextureColor(GetUV(face));
                for (int i = 0; i < NumFaces; i++)
                {
                    if (GetTextureColor(GetUV(i)) == pick)
                    {
                        Set(i, UV);
                    }
                }
                RefreshUVs();
            }
        }

        /// <summary>
        /// Paints all connected faces of a mesh element with the new UV color, regardless of their current color.
        /// </summary>
        public void FillElement(int face, Vector2 UV)
        {
            FillPaint(face, UV, true);
        }

        class TexelData { 
            public float x; public float y; public Color color; 
            public TexelData(float _x, float _y, Color _color)
            {
                x = _x; y = _y; color = _color;
            }
        };

        /// <summary>
        /// Reassigns the UVs of all mesh faces by finding the closest color match on a new target texture,
        /// effectively remapping the painted model to a different color palette.
        /// </summary>
        public bool RemapTo(Texture2D tex2d, bool switchTexture = false)
        {
            var pixels = tex2d.GetPixelData<Color32>(0);
            
            var ColorCache = new Dictionary<Color32, TexelData>();

            Color32 GetPixel(int x, int y)
            {
                return pixels[y * tex2d.width + x];
            }

            Vector2 ImproveTexel(int x, int y)
            { 
                //TODO: still not perfect, investigate
                var c = GetPixel(x, y);
                var sameColor = true;
                var count = 0;
                //ensuring the seek never overflows
                var maxCount = tex2d.width - x - 1;
                var heightLimit = tex2d.height - y - 1;
                if (heightLimit < maxCount) maxCount = heightLimit;
                //also 20 pixels away is enough
                if (maxCount > 20) maxCount = 20;
                if (maxCount>2)
                {
                    while (sameColor)
                    {
                        count++;
                        var c2 = GetPixel(x + count, y + count);
                        sameColor = (count < maxCount) && c.r == c2.r && c.g == c2.g && c.b == c2.b;
                    }
                    if (count>2)
                    {
                        x = x + count / 2;
                        y = y + count / 2;
                    }
                }
                return new Vector2(x, y);
            }


            void findNearest(Color c, out Color cOut, out Vector2 uvOut)
            {
                Color32 c32 = c;

                TexelData texel;
                if (ColorCache.TryGetValue(c32, out texel))
                {
                    cOut = texel.color;
                    uvOut.x = texel.x;
                    uvOut.y = texel.y;
                }
                else
                {

                    cOut = Color.white;
                    uvOut = Vector2.zero;
                    int xx = 0;
                    int yy = 0;
                    float minDiff = float.MaxValue;

                    for (int y = 0; y < tex2d.height; y++)
                    {
                        for (int x = 0; x < tex2d.width; x++)
                        {
                            var pix = GetPixel(x, y);
                            var c2 = (Color)pix;
                            var diff = ((Vector4)c2 - (Vector4)c).sqrMagnitude;
                            if (diff < minDiff)
                            {
                                //Debug.Log(minDiff);
                                minDiff = diff;
                                cOut = c2;
                                xx = x; yy = y;
                            }

                        }
                    }

                    //improve texel position avoiding borders
                    var betterTexel = ImproveTexel(xx, yy);
                    //Vector2 betterTexel = new Vector2(xx, yy);
                    uvOut.x = betterTexel.x / tex2d.width;
                    uvOut.y = betterTexel.y / tex2d.height;
                    ColorCache.Add(c32, new TexelData(uvOut.x, uvOut.y, cOut));
                }

            }

            var t = Environment.TickCount;
            if (tex2d == null) return false;
            if (tex2d.width == 0 || tex2d.height == 0) return false;
            for (int i = 0; i < NumFaces; i++)
            {
                var oldColor = GetTextureColor(GetUV(i));
                Color newColor;
                Vector2 newUV;
                findNearest(oldColor, out newColor, out newUV);   
                Set(i, newUV);
            }
            RefreshUVs();
            if (switchTexture) _textureData = tex2d;
            Debug.Log("Remap delay: " + (Environment.TickCount - t).ToString() + "ms");
            return true;
        }

        public void GetFaceVerts(int face, List<Vector3> verts, Matrix4x4 transformMat)
        {
            verts.Clear();
            if (face > -1)
            {
                    verts.Add(transformMat.MultiplyPoint3x4(_vertices[face * 3]));
                    verts.Add(transformMat.MultiplyPoint3x4(_vertices[face * 3 + 1]));
                    verts.Add(transformMat.MultiplyPoint3x4(_vertices[face * 3 + 2]));
            }
        }

        public void MoveFacesUndoBack()
        {
            if (_verticesBackup != null)
            {
                Array.Copy(_verticesBackup, _vertices, Vertices.Length);
                _targetMesh.SetVertices(_vertices);
                SelectedFaces.Clear(); //TODO: temporal patch, before fixing the selection that is not updated when faces are undo back
            }
        }
    }

    /// <summary>
    /// A utility class for creating and restoring a copy of a Unity Mesh's essential data.
    /// Used for undo/redo functionality for the entire mesh structure.
    /// </summary>
    public class MeshCopy
    {
        public List<int> tris = new List<int>();
        public List<Vector3> vertices = new List<Vector3>();
        public List<Vector2> UVs = new List<Vector2>();  // channel 0
        public List<Vector3> normals = new List<Vector3>();
        public List<BoneWeight> boneWeights = new List<BoneWeight>();


        public MeshCopy(Mesh m)
        {
            tris = m.triangles.ToList();
            vertices = m.vertices.ToList();
            UVs = m.uv.ToList();
            normals = m.normals.ToList();
            boneWeights = m.boneWeights.ToList();            
        }

        public void PasteTo(Mesh m)
        {
            if (m != null)
            {
                m.Clear();
                if (vertices.Count > 60000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(vertices); //  order is important.
                m.SetUVs(0, UVs);
                m.SetTriangles(tris, 0);
                m.SetNormals(normals);
                m.boneWeights = boneWeights.ToArray();
            }
        }
    }


    /// <summary>
    /// Stores the topological relationship between two adjacent faces in the mesh.
    /// </summary>
    public class FaceLink
    {
        //TODO: this needs an infographic!!
        public int with;            //linked with which other face?
        public int p1, p2;          //shared vertices, as position index in the face 0,1 or 2;
        public Edge edge;           //shared vertices numbers
        public int side;            //side of the tirangle: 0, 1, or 2;
        public int pOut;            //the vertice point that is not shared.
        public int backLinkIdx;     //position index on the other face List of links corresponding to this link O.o capichi      
    }
    

    /// <summary>
    /// Represents an edge in the mesh defined by two vertex indices.
    /// Provides methods for checking equality and shared vertices between edges.
    /// </summary>
    public struct Edge
    {
        public int v1, v2;

        public bool Equals(Edge other)
        {
            return ((v1 == other.v1 && v2 == other.v2) || (v1 == other.v2 && v2 == other.v1));
        }

        public Edge(int vert1, int vert2)         
        {
            v1 = vert1;
            v2 = vert2;
        }

        public bool HaveSharedVerts(Edge other)
        {
            return (v1 == other.v1 || v1 == other.v2 || v2 == other.v1 || v2 == other.v2);
        }
    }

    public enum ToolAction { Paint = 0, Select = 1 }

}