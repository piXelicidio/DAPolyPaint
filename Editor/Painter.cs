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
        List<Vector2> _targetMeshUVs;

        int _setUVCalls;

        Mesh Target { get { return _targetMesh; }  }

        public void SetMesh(Mesh target, bool skinned)
        {
            _targetMesh = target;
            _skinned = skinned;
            RebuildMeshForPainting();
            _setUVCalls = 0;
        }

        void RebuildMeshForPainting()
        {
            var m = _targetMesh;
            var tris = m.triangles;
            var vertices = m.vertices;
            var UVs = m.uv;
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
            //TODO: assign new data, recarculate normals:
            if (m.subMeshCount > 1) m.subMeshCount = 1;
            m.SetVertices(newVertices);
            m.SetUVs(0, newUVs);
            _targetMeshUVs = newUVs; //keep ref for painting
            m.SetTriangles(newTris, 0);
            m.SetNormals(newNormals);

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
                _targetMeshUVs[face * 3] = uvc;
                _targetMeshUVs[face * 3 + 1] = uvc;
                _targetMeshUVs[face * 3 + 2] = uvc;
                _targetMesh.SetUVs(0, _targetMeshUVs);  //could be improved if Start, Length parameters actually worked                
                _setUVCalls++;
            }
        }
    }

}