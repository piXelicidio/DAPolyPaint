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
        Vector3[] _vertices;
        Vector3[] _indexedVerts;
        int[] _triangles;
        int[] _indexedFaces;
        List<FaceLink>[] _faceLinks;
        int _channel = 0;

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
            BuildFaceGraph();
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
            
            for (int i=0; i<NumVerts-1; i++)
            {
                var v = _vertices[i];
                var idx = sharedVerts.FindIndex(x => x == v);
                if (idx == -1)
                {
                    sharedVerts.Add(v);
                    indexReplace[i] = i;
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
            //TODO: It's too slow, while it does the work
            //TODO: Maybe only edge link is needed, 2 common verts. Ignore a single common vertex.
            //NOTE: 3 common verts is an annomally
            var sum = 0.0f;
            _faceLinks = new List<FaceLink>[NumFaces];
            for (var i=0; i<NumFaces-1; i++)
            {
                _faceLinks[i] = new List<FaceLink>();
                for (var j=0; j<NumFaces-1; j++)
                    if (i!=j)
                    {
                        //TODO: get rid of "new" objects creation here?
                        var commonVerts = new HashSet<int>(GetFaceVerts(i));                        
                        commonVerts.IntersectWith(new HashSet<int>(GetFaceVerts(j)));
                        if (commonVerts.Count > 0)
                        {
                            //there is connection:
                            FaceLink link;
                            link.face = j;
                            link.commonVerts = commonVerts.ToArray();
                            _faceLinks[i].Add(link);
                        }
                    }
                sum += _faceLinks[i].Count;
            }
            Debug.Log("Average Num Links: " + (sum / NumFaces).ToString());
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
    }

    public struct FaceLink
    {
        public int face;
        public int[] commonVerts;        
    }
}