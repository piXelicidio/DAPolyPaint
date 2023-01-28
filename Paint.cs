using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DAPolyPaint
{
    public class Paint : MonoBehaviour
    {
        public Mesh _mesh;

        public void SavePaintedMesh(Mesh newMeshData)
        {            
            _mesh = Object.Instantiate<Mesh>(newMeshData);
            GetComponent<MeshFilter>().mesh = _mesh;
        }     

       
    }
}