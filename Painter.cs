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

        int _channel = 0;
        public float _nearBest;

        public Mesh Target { get { return _targetMesh; }  }
        public int NumUVCalls { get; private set; }
        public int NumFaces { get { return _triangles.Length / 3; } }
        public int NumVerts { get { return _vertices.Length; } }

        public void SetMeshAndRebuild(Mesh target, bool skinned)
        {
            _targetMesh = target;
            _skinned = skinned;
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
                    for (int v1 = i * 3; v1 < i * 3 + 3; v1++)
                    {
                        for (int v2 = j * 3; v2 < j * 3 + 3; v2++)
                        {
                            if (_indexedFaces[v1] == _indexedFaces[v2])
                            {
                                pos[count] = v1 - i * 3;
                                posOther[count] = v2 - j * 3;
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
                        FaceLink link, linkOther;
                        link.with = j;
                        link.p1 = pos[0];                  //shared face points 
                        link.p2 = pos[1];
                        link.pOut = 3 - (pos[0] + pos[1]); //point left out
                            
                        linkOther.with = i;
                        linkOther.p1 = posOther[0];
                        linkOther.p2 = posOther[1];
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

        public int FindQuad(int face, float tolerance = 60f)
        {
            var best = -1;
            var nearBest = tolerance;
            //if ( _faceLinks[face].Count > 0)
            //{
            //    return _faceLinks[face][0].with;
            //}
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
            _nearBest = nearBest;
            return best;
        }

        private (int a, int b, int c) GetFaceVertsIdxs(int face)
        {
            return (face *3, face*3 + 1, face*3 + 2);
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
            if (face > 0)
            {
                verts.Clear();
                verts.Add(_vertices[face * 3]);
                verts.Add(_vertices[face * 3 + 1]);
                verts.Add(_vertices[face * 3 + 2]);
            }
        }

        public void GetFaceVerts(int face, List<Vector3> verts, Matrix4x4 transformMat)
        {
            verts.Clear();
            if (face > 0)
            {                
                verts.Add(transformMat.MultiplyPoint3x4(_vertices[face * 3]));
                verts.Add(transformMat.MultiplyPoint3x4(_vertices[face * 3 + 1]));
                verts.Add(transformMat.MultiplyPoint3x4(_vertices[face * 3 + 2]));
            }
        }

        public List<FaceLink> GetFaceLinks(int face)
        {
            return _faceLinks[face]; 
        }
    }

    public struct FaceLink
    {
        public int with;            //linked with which other face?
        public int p1, p2;          //shared vertices, as position index in the face 0,1 or 2;
        public int pOut;            //the vertice point that is not shared.
        public int backLinkIdx;     //position index on the other face List of links corresponding to this link O.o capichi
    }
    
}