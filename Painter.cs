using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace DAPolyPaint
{
    //Deals only with Mesh and UV painting,
    //don't know anything about UI or GameObjects, could be used in play mode
    //Many field/variable names are selected based on the 3ds Max Poly Paint tool implementation
    //and Maxscript methods/properties naming (e. g. NumVerts, NumFaces).
    public class Painter
    {
        Mesh _targetMesh;
        bool _skinned;
        List<Vector2> _UVs;
        Vector3[] _vertices;            //all non optimized vertices of the actual mesh
        Vector3[] _indexedVerts;
        int[] _triangles;
        int[] _indexedFaces;
        float[] _angles;                //angle of each triangle corners
        List<FaceLink>[] _faceLinks;
        List<List<Vector2>> _undoLevels;
        int _undoPos;
        private int _undoSequenceCount;
        int _channel = 0;
        Texture2D _textureData;
        private Mesh _skinAffected;
        private Vector3[] _verticesSkinned;
        
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

        public void SetMeshAndRebuild(Mesh target, bool skinned, Texture2D texture)
        {
            _targetMesh = target;
            _skinned = skinned;
            _skinAffected = null;
            _textureData = texture;
            RebuildMeshForPainting();            
        }

        void RebuildMeshForPainting()
        {
            var t = Environment.TickCount;
            Debug.Log("<b>Preparing mesh...</b> ");
            var m = _targetMesh;
            var tris = m.triangles;
            var vertices = m.vertices;
            var UVs = m.uv; //channel 0 
            var normals = m.normals;
            var boneWeights = m.boneWeights;
            var newVertices = new List<Vector3>();
            var newUVs = new List<Vector2>();
            var newTris = new List<int>();
            var newNormals = new List<Vector3>();
            var newBW = new BoneWeight[tris.Length];
            //no more shared vertices, each triangle will have its own 3 vertices.
            for (int i = 0; i < tris.Length; i++)
            {
                var idx = tris[i];
                newTris.Add(i);
                newVertices.Add(vertices[idx]);
                newNormals.Add(normals[idx]);
                //also UVs but the 3 values will be the same
                newUVs.Add(UVs[tris[i - i % 3]]);

                if (_skinned)
                {
                    newBW[i] = boneWeights[idx];
                }
            }
            
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

            
            Indexify();
            CalcAngles();
            BuildFaceGraph();

            Undo_Reset();

            Debug.Log("<b>Elapsed:</b> " + (Environment.TickCount - t).ToString() + "ms");

        }



        //Define a new indexed view of the mesh, optimized like it was on 3ds Max source.
        //Needed for geting relationships between faces, edges, verts.
        //_indexedVerts store unique vertices
        //_indexedFaces every 3 values are 3 indexes pointing to _indexedVerts values.
        //Number of faces keep the same, and keep same order. _indexedFaces[n] -> corresponds to -> _triangles[n]
        private void Indexify()
        {
            var sharedVerts = new List<Vector3>();
            var indexReplace = new int[_triangles.Length];
            _indexedFaces = new int[_triangles.Length];
            
            for (int i=0; i<NumVerts; i++)
            {
                var v = _vertices[i];
                var idx = sharedVerts.FindIndex(x => x == v);
                
                if (idx == -1)
                {
                    indexReplace[i] = sharedVerts.Count;
                    sharedVerts.Add(v);
                } else
                {
                    indexReplace[i] = idx;
                }
            }

            for (int i=0; i<_triangles.Length; i++)
            {
                _indexedFaces[i] = indexReplace[_triangles[i]];
            }
            _indexedVerts = sharedVerts.ToArray();

            //Tested with casual_Female_G model when from 4k verts to originallly 824 verts, just like 3ds Max version.
            Debug.Log(String.Format("NumVerts before:{0} after:{1}", NumVerts, sharedVerts.Count));
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

        //Return list of Verts indices, no validations
        private List<int> GetFaceVerts(int face)
        {
            return new List<int>() { 
                _indexedFaces[face*3], 
                _indexedFaces[face*3+1], 
                _indexedFaces[face*3+2] 
            };
        }   
        
        //given unsoreted position p1 p2 (0..2) in a triangle.
        //return which side of the triangle it represents.
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


        //build relationships, find links between faces
        //Assumes Indexify() was called first
        private void BuildFaceGraph()
        {
            //NOTE: 3 common verts is an annomally
            var sum = 0.0f;
            _faceLinks = new List<FaceLink>[NumFaces];
            for (var i = 0; i < NumFaces-1; i++)
            {
                if (_faceLinks[i] == null) _faceLinks[i] = new List<FaceLink>();
                for (var j = i+1; j < NumFaces; j++)
                {
                    //find coincidences...
                    var count = 0;
                    int[] pos = new int[2];
                    int[] posOther = new int[2];
                    for (int p1 = 0; p1 < 3; p1++)
                    {
                        for (int p2 = 0; p2 < 3; p2++)
                        {
                            if (_indexedFaces[p1 + i*3] == _indexedFaces[p2 + j*3])
                            {
                                pos[count] = p1 ;
                                posOther[count] = p2 ;
                                count++;
                                break;
                            }
                        }
                        if (count == 2) break;
                    }

                    //ignoring single shared vertices.
                    if (count == 2)
                    {
                        //there is connection:
                        var link = new FaceLink();
                        var linkOther = new FaceLink();
                        link.with = j;
                        link.p1 = pos[0];                  //shared face points 
                        link.p2 = pos[1];
                        link.edge.v1 = _indexedFaces[i * 3 + pos[0]];
                        link.edge.v2 = _indexedFaces[i * 3 + pos[1]];
                        link.side = GetTriangleSide(pos[0], pos[1]);
                        link.pOut = 3 - (pos[0] + pos[1]); //point left out
                            
                        linkOther.with = i;
                        linkOther.p1 = posOther[0];
                        linkOther.p2 = posOther[1];
                        linkOther.edge.v1 = _indexedFaces[j * 3 + posOther[0]];
                        linkOther.edge.v2 = _indexedFaces[j * 3 + posOther[1]];
                        linkOther.side = GetTriangleSide(posOther[0], posOther[1]);
                        linkOther.pOut = 3 - (posOther[0] + posOther[1]);                           

                        if (_faceLinks[j] == null) _faceLinks[j] = new List<FaceLink>();
                        link.backLinkIdx = _faceLinks[j].Count;
                        linkOther.backLinkIdx = _faceLinks[i].Count;
                        _faceLinks[i].Add(link);
                        _faceLinks[j].Add(linkOther);
                    }
                    
                    
                }
                sum += _faceLinks[i].Count;
            }
            Debug.Log("Average Num Links: " + (sum / NumFaces).ToString());
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
                    Debug.LogError("Bad backlinkIdx!");
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

        public void GetFaceVerts(int face, List<Vector3> verts )
        {
            verts.Clear();
            if (face != -1)            {
                if (_skinAffected == null)
                {
                    verts.Add(_vertices[face * 3]);
                    verts.Add(_vertices[face * 3 + 1]);
                    verts.Add(_vertices[face * 3 + 2]);
                } else
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
                } else
                {
                    verts.Add(transformMat.MultiplyPoint3x4(_verticesSkinned[face * 3]));
                    verts.Add(transformMat.MultiplyPoint3x4(_verticesSkinned[face * 3 + 1]));
                    verts.Add(transformMat.MultiplyPoint3x4(_verticesSkinned[face * 3 + 2]));
                }
            }
        }

        public List<FaceLink> GetFaceLinks(int face)
        {
            return _faceLinks[face]; 
        }

        public void SetSkinAffected(Mesh snapshot)
        {
            _skinAffected = snapshot;
            _verticesSkinned = snapshot.vertices;
        }

        public void FillPaint(int startFace, Vector2 uvc)
        {            
            var bk_pixelColor = GetTextureColor(GetUV(startFace));
            var border = new HashSet<int>();
            border.Add(startFace);
            var neighbors = new HashSet<int>();
            var iters = 0;

            var t = Environment.TickCount;

            do
            {
                //paint border
                foreach (int face in border)
                {
                    _UVs[face * 3] = uvc;
                    _UVs[face * 3 + 1] = uvc;
                    _UVs[face * 3 + 2] = uvc;             
                    
                }
                _targetMesh.SetUVs(_channel, _UVs);  //can be called at the end just once
                //find neighbors
                neighbors.Clear();
                var noSpread = 0;
                foreach (int face in border)
                {
                    var faceLinks = GetFaceLinks(face);
                    foreach (var l in faceLinks)
                    {
                        if (GetTextureColor(_UVs[l.with*3]) == bk_pixelColor)
                        {
                            neighbors.Add(l.with);                            
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

        //if a link to the other face is not found return null
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
                
        //Given two adjacent faces (f1 and f2), find a loop.
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
                Debug.Log("Let's find the loop!");
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
                    if (jumpToFace == -1) Debug.LogError("Why is happening? no oppsite side found?");
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
            Debug.Log(String.Format("cursor{0} count{1}", _undoPos, _undoLevels.Count));
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
            Debug.Log(String.Format("cursor{0} count{1}", _undoPos, _undoLevels.Count));
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
    }

    public class FaceLink
    {
        public int with;            //linked with which other face?
        public int p1, p2;          //shared vertices, as position index in the face 0,1 or 2;
        public Edge edge;           //shared vertices numbers
        public int side;            //side of the tirangle: 0, 1, or 2;
        public int pOut;            //the vertice point that is not shared.
        public int backLinkIdx;     //position index on the other face List of links corresponding to this link O.o capichi      
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