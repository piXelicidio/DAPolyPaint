using System;
using System.Collections.Generic;
using UnityEngine;


namespace DAPolyPaint
{
    //Deal with prepare mesh and paint UVs,
    //don't know anything about UI or GameObjects, could be used in play mode
    public class Painter
    {        
        Mesh _targetMesh;
        bool _skinned;
        List<Vector2> _UVs;
        Vector3[] _vertices;
        int[] _triangles;
        int _channel = 0;

        public Mesh Target { get { return _targetMesh; }  }
        public int NumUVCalls { get; private set; }
        public int NumFaces { get { return _triangles.Length / 3; } }

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

}