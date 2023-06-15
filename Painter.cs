using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

 
namespace DAPolyPaint 
{
    /// <summary>
    /// Deals with Mesh and UV painting functions. Can be used in play mode.
    /// </summary>
    //Doesn't know anything about UI or GameObjects. 
    //Many field/variable names are selected based on the 3ds Max Poly Paint tool implementation
    //and Maxscript methods/properties naming (e. g. NumVerts, NumFaces).
    public class Painter
    {
        Mesh _targetMesh;
        bool _skinned;
        List<Vector2> _UVs;
        Vector3[] _vertices;            //all non optimized vertices of the actual mesh
        Vector3[] _indexedVerts;
        private List<int>[] _facesUsingVert;
        int[] _triangles;
        int[] _indexedFaces;
        float[] _angles;                //angle of each triangle corners
        List<FaceLink>[] _faceLinks;
        FaceData[] _faceData;
        List<List<Vector2>> _undoLevels;
        int _undoPos;
        private int _undoSequenceCount;
        int _channel = 0;
        Texture2D _textureData;
        private MeshCopy _oldMesh;
        
        public Mesh Target { get { return _targetMesh; } }
        public int NumUVCalls { get; private set; }
        public int NumFaces { get { return _triangles.Length / 3; } }
        public int NumVerts { get { return _vertices.Length; } }
        public float QuadTolerance { get; set; }

        public Painter()
        {
            QuadTolerance = 120;
            _undoLevels = new List<List<Vector2>>();
        }

        /// <summary>
        /// Set the mesh to be painted and rebuild the internal data structures.
        /// </summary>        
        //THIS NEED to be called first before any other painting function. TODO: maybe put in the constructor?
        public void SetMeshAndRebuild(Mesh target, bool skinned, Texture2D texture)
        {
            _targetMesh = target;
            _skinned = skinned;
            _textureData = texture;
            RebuildMeshForPainting();            
        }

        ///<summary>Rebuild the mesh data to get ready for painting.</summary>
        void RebuildMeshForPainting()
        {
            var t = Environment.TickCount;
            Debug.Log("<b>Preparing mesh...</b> ");
            var m = _targetMesh;

            //var tris = m.triangles;
            //var vertices = m.vertices;
            //var UVs = m.uv; //channel 0 
            //var normals = m.normals;
            //var boneWeights = m.boneWeights;

            _oldMesh = new MeshCopy(m);

            var newVertices = new List<Vector3>();
            var newUVs = new List<Vector2>();
            var newTris = new List<int>();
            var newNormals = new List<Vector3>();
            var newBW = new BoneWeight[_oldMesh.tris.Length];
            //no more shared vertices, each triangle will have its own 3 vertices.
            for (int i = 0; i < _oldMesh.tris.Length; i++)
            {
                var idx = _oldMesh.tris[i];
                newTris.Add(i);
                newVertices.Add(_oldMesh.vertices[idx]);
                newNormals.Add(_oldMesh.normals[idx]);
                //also UVs but the 3 values will be the same
                newUVs.Add(_oldMesh.UVs[_oldMesh.tris[i - i % 3]]);

                if (_skinned)
                {
                    newBW[i] = _oldMesh.boneWeights[idx];
                }
            }

            //updating mesh with new distribution of vertices
            m.Clear();
            if (newVertices.Count>60000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            if (m.subMeshCount > 1) m.subMeshCount = 1; //NOT support for multiple submeshes.
            m.SetVertices(newVertices);
            m.SetUVs(_channel, newUVs);            
            m.SetTriangles(newTris, 0);
            m.SetNormals(newNormals);

            _UVs = newUVs; //keep ref for painting
            _vertices = newVertices.ToArray();
            _triangles = newTris.ToArray();            

            if (_skinned)
            {
                //TODO(maybe later): Use the more complex new SetBoneWeights to support more than 4 bones per vertex
                m.boneWeights = newBW;                
            }

            string s;
            s = "<b>Rebuild, Elapsed:</b> " + (Environment.TickCount - t).ToString() + "ms - ";
            t = Environment.TickCount;

            Indexify();
            s += "<b>Indexify, Elapsed:</b> " + (Environment.TickCount - t).ToString() + "ms - ";
            t = Environment.TickCount;

            CalcAngles();
            s += "<b>CalcAngles, Elapsed:</b> " + (Environment.TickCount - t).ToString() + "ms - ";
            t = Environment.TickCount;

            BuildFaceGraph();
            Undo_Reset();
            s += "<b>BuildFaceGraph, Elapsed:</b> " + (Environment.TickCount - t).ToString() + "ms";
            Debug.Log(s);

            
        }

        //After calling RestoreOldMesh, all other painting functions will fail, unless you call RebuildMeshForPainting again.
        public void RestoreOldMesh()
        {
            Debug.Log("Restoring mesh...");
            _oldMesh.PasteTo(_targetMesh);
        }



        ///<summary>Defines a new indexed view of the mesh, optimized like it was on 3ds Max source.</summary>
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
            
            for (int i=0; i<NumVerts; i++)
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

            for (int i=0; i<_triangles.Length; i++)
            {
                _indexedFaces[i] = indexReplace[_triangles[i]];
            }
            _indexedVerts = sharedVerts.ToArray();
            _facesUsingVert = facesUsingVert.ToArray();

            //Tested with casual_Female_G model when from 4k verts to originallly 824 verts, just like 3ds Max version.
            Debug.Log(String.Format("NumVerts before:{0} after:{1} dumbSymmetryTest:{2}", NumVerts, sharedVerts.Count, dumbSymmetryTest));
        }

        private void CalcAngles()
        {
            _angles = new float[_indexedFaces.Length];            
            for (var f=0; f<NumFaces; f++)
            {
                for (var i=0; i<3; i++)
                {
                    var thisIdx = f * 3 + i;
                    var nextIdx = f * 3 + (i + 1) % 3;
                    var prevIdx = f * 3 + ((i+3) - 1) % 3;
                    var v1 = _indexedVerts[_indexedFaces[nextIdx]] - _indexedVerts[_indexedFaces[thisIdx]];
                    var v2 = _indexedVerts[_indexedFaces[prevIdx]] - _indexedVerts[_indexedFaces[thisIdx]];
                    _angles[f*3+i] = Vector3.Angle(v1, v2);
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

        ///<summary> 
        ///Ggiven unsoreted position p1 p2 (0..2) in a triangle. 
        ///Return which side number of the triangle it represents.
        ///</summary>                
        private int GetTriangleSide(int p1, int p2)
        {
            var low = Math.Min(p1, p2);
            var high = Math.Max(p1, p2);
            if (low==0 && high==2)
            {
                return 2;
            } else
            {
                return low;
            }
        }

        /// <summary> build link relatioships between faces </summary>
        //assumes Indexify() was called before
        private void BuildFaceGraph()
        {
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
                    var otherLink = _faceData[face2].links[otherFaceSide];
                    bool otherOk = otherLink == null;
                    if (!otherOk)
                    {
                        otherOk = otherLink.with == face1;
                    }

                    //my face side is unlinked?                    
                    if (_faceData[face1].links[link.side] == null && otherOk)
                    {
                        //other face has this side unlinked or linked to me?                    
                        
                            _faceLinks[face1].Add(link);
                            _faceData[face1].links[link.side] = link;                        
                    }
                }
            }

            var sum = 0.0f;
            _faceLinks = new List<FaceLink>[NumFaces];
            _faceData = new FaceData[NumFaces];
            for (int i = 0; i < NumFaces; i++)
            {
                _faceLinks[i] = new List<FaceLink>();
                _faceData[i] = new FaceData();
            }

            var myVerts = new int[3];
            for (int i = 0; i < NumFaces; i++)
            {                
                var nearFaces = GetFacesUsingVerts(GetFaceVertIdxs(i));
                foreach (int f in nearFaces)
                {
                   FindCoincidences(i, f);                   
                }
            }

            //backlinks
            for (int i = 0; i < NumFaces; i++)
            {
                var links = _faceLinks[i];
                sum += links.Count;
                for (int j = links.Count-1; j >= 0; j--)
                {
                   var backlink = _faceLinks[links[j].with].Find(x => x.with == i);
                    if (backlink != null)
                    {
                        backlink.backLinkIdx = j;
                    } else
                    {
                        Debug.LogWarning("Backlink not found. Removing link.");
                        links.RemoveAt(j);
                    }

                }
            }
            Debug.Log("Average Num Links: " + (sum / NumFaces).ToString());
        }

        public HashSet<int> GetFacesUsingVerts(int[] verts)
        {
            var result = new HashSet<int>();
            for (int i=0; i<verts.Length; i++)
            {
                result.UnionWith(_facesUsingVert[verts[i]]);                
            }
            return result;
        }



        
        /// <summary>
        /// Finds the best neighbor face to complete a quad.
        /// </summary>
        /// <param name="face"></param>
        /// <param name="tolerance"></param>
        /// <returns>Face index or -1 if not found.</returns>
        public int FindQuad(int face)
        {
            if (face == -1) return -1;
            var best = -1;
            var nearBest = QuadTolerance;
      
            for (int i=0; i<_faceLinks[face].Count; i++)
            {
                var linkTo = _faceLinks[face][i];
                var faceOther = linkTo.with;
                var linkFrom = _faceLinks[faceOther][linkTo.backLinkIdx];
                if (linkFrom.with != face)
                {
                    Debug.LogWarning("Bad backlinkIdx!");
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


        public void SetUV(int face, Vector2 uvc)
        {
            if (face >= 0)
            {
                _UVs[face * 3] = uvc;
                _UVs[face * 3 + 1] = uvc;
                _UVs[face * 3 + 2] = uvc;
                _targetMesh.SetUVs(_channel, _UVs);  //could be improved if Start, Length parameters actually worked                
                NumUVCalls++;
            }
        }

        //A face actually have 3 UV values but, here all are the same
        //so only one is enough
        public Vector2 GetUV(int face)
        {
            if (face != - 1 && face < _UVs.Count)
            {
                return _UVs[face*3];
            } else
            {
                return new Vector2(-1,-1);
            }
        }

        public void FullRepaint(Vector2 uvc)
        {
            for (int i = 0; i < _UVs.Count; i++)
            {
                _UVs[i] = uvc;
            }
            _targetMesh.SetUVs(_channel, _UVs);
        }



        public List<FaceLink> GetFaceLinks(int face)
        {
            return _faceLinks[face]; 
        }


        public void FillPaint(int startFace, Vector2 uvc)
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
                    _UVs[face * 3] = uvc;
                    _UVs[face * 3 + 1] = uvc;
                    _UVs[face * 3 + 2] = uvc;
                    visited[face] = true;
                }
                _targetMesh.SetUVs(_channel, _UVs);  //can be called at the end just once
                //find neighbors
                neighbors.Clear();
                var noSpread = 0;
                foreach (int face in border)
                {
                    var faceLinks = GetFaceLinks(face);
                    foreach (var link in faceLinks)
                    {
                        if (GetTextureColor(_UVs[link.with*3]) == bk_pixelColor && !visited[link.with])
                        {
                            neighbors.Add(link.with);                            
                        }
                        else noSpread++;
                    }
                }
                //swap                
                (border, neighbors) = (neighbors, border);

                iters++;
            } while (border.Count > 0 && iters<1000);  //TODO: Improve this arbitrary limit
            Debug.Log(String.Format("Iters:{0} Elapsed:{1}ms", iters, Environment.TickCount - t));
        }

        public Color GetTextureColor(Vector2 uv)
        {
            if (_textureData == null)
                return Color.white;
            else
                return _textureData.GetPixel((int)(uv.x * _textureData.width), (int)(uv.y * _textureData.height));
        }

        /// <summary>
        /// if a link to the other face is not found return null
        /// </summary>
        public FaceLink FindLink(int fromFace, int toFace)
        {
            if (fromFace!=-1  && toFace!=1)
            {
                foreach (var fl in _faceLinks[fromFace])
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
        /// Given two adjacent faces (f1 and f2), find a loop.
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
                var breakLimit = 20;
                do
                {
                    
                    var SharedEdge_f1_f2 = linkToF2.edge;
                    var jumpToFace = -1;
                    foreach (FaceLink fl in _faceLinks[f2_quadBro])
                    {
                        if (!fl.edge.HaveSharedVerts(SharedEdge_f1_f2))
                        {
                            //found oppsite edge
                            jumpToFace = fl.with;                            
                            //AddFace(jumpToFace);
                            break;
                        };
                    }
                    if (jumpToFace == -1) Debug.LogWarning("No opposite side found!");
                    f1 = f2_quadBro;
                    f2 = jumpToFace;
                    noOverlaps = result.Add(f1) && result.Add(f2);
                    f2_quadBro = FindQuad(f2);
                    weQuad = f2_quadBro == f1;
                    linkToF2 = FindLink(f1, f2);
                    breakLimit--;
                } while (breakLimit > 0 && f2_quadBro != -1 && !weQuad && linkToF2 != null && noOverlaps) ;
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
                var state = _undoLevels[_undoPos-1];
                for (int i=0; i<state.Count; i++)
                {
                    _UVs[i] = state[i];
                }
                _targetMesh.SetUVs(_channel, _UVs);
                _undoPos--;
                _undoSequenceCount++;
            }
            //Debug.Log(String.Format("cursor{0} count{1}", _undoPos, _undoLevels.Count));
        }

        public void Undo_Redo()
        {
            if (_undoSequenceCount>0)
            {
                _undoSequenceCount--;
                _undoPos++;
                var state = _undoLevels[_undoPos];
                for (int i=0; i<state.Count; i++)
                {
                    _UVs[i] = state[i];
                }
                _targetMesh.SetUVs(_channel, _UVs);
            }
        }

        public bool isModified()
        {
            return _undoPos > 0;
        }


    }

    public class MeshCopy
    {
        public int[] tris;
        public Vector3[] vertices;
        public Vector2[] UVs; //channel 0 
        public Vector3[] normals;
        public BoneWeight[] boneWeights;
        
        public MeshCopy(Mesh m)
        {
            tris = m.triangles;
            vertices = m.vertices;
            UVs = m.uv;
            normals = m.normals;
            boneWeights = m.boneWeights;            
        }

        public void PasteTo(Mesh m)
        {
            if (m != null)
            {
                m.Clear();
                if (vertices.Length > 60000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(new List<Vector3>(vertices)); //  order is important.
                m.SetUVs(0, new List<Vector2>(UVs));
                m.SetTriangles(new List<int>(tris), 0);
                m.SetNormals(new List<Vector3>(normals));
                m.boneWeights = boneWeights;
            }
        }
    }

    
    /// <summary>
    /// Stores the link relationship between two adjacent faces.
    /// </summary>
    public class FaceLink
    {
        public int with;            //linked with which other face?
        public int p1, p2;          //shared vertices, as position index in the face 0,1 or 2;
        public Edge edge;           //shared vertices numbers
        public int side;            //side of the tirangle: 0, 1, or 2;
        public int pOut;            //the vertice point that is not shared.
        public int backLinkIdx;     //position index on the other face List of links corresponding to this link O.o capichi      
    }

    public class FaceData
    {
        public FaceLink[] links;        

        public FaceData()
        {
            links = new FaceLink[3];
        }
    }

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

}