using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DAPolyPaint
{
    /// <summary>
    /// This will actually added to a dummy object, not the object being edited. Improve names.
    /// </summary>
    [HideInInspector]
    public class PaintCursor : MonoBehaviour
    {        
        public Mesh TargetMesh;
    }
}
