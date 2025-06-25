using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;


namespace DAPolyPaint 
{
    public static class ObjFormat
    {
        /// <summary>
        /// verts: shared mesh vertices  
        /// faces: triangle indices (3 per face)  
        /// uvs: list of all UV coordinates  
        /// uvFaces: per-corner indices into uvs (same length as faces)  
        /// </summary>
        public static bool Export(
            Vector3[] verts,
            int[] faces,
            List<Vector2> uvs,
            int[] uvFaces,
            string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# DA PolyPaint OBJ exporter");
            sb.AppendLine("");
            // write positions
            foreach (var v in verts)
                sb.AppendLine($"v {v.x} {v.y} {v.z}");
            sb.AppendLine($"# {verts.Length} vertices");
            sb.AppendLine();

            // write texture verts
            foreach (var uv in uvs)
                sb.AppendLine($"vt {uv.x} {uv.y}");
            sb.AppendLine($"# {uvs.Count} texture coords");
            sb.AppendLine();

            // write faces: f v1/vt1 v2/vt2 v3/vt3
            for (int i = 0; i < faces.Length; i += 3)
            {
                int v0 = faces[i] + 1;
                int v1 = faces[i + 1] + 1;
                int v2 = faces[i + 2] + 1;

                int t0 = uvFaces[i] + 1;
                int t1 = uvFaces[i + 1] + 1;
                int t2 = uvFaces[i + 2] + 1;

                sb.AppendLine($"f {v0}/{t0} {v1}/{t1} {v2}/{t2}");
            }
            sb.AppendLine($"# {faces.Length / 3} faces"); 
            try
            {
                File.WriteAllText(filePath, sb.ToString());
            }
            catch
            {
                Debug.Log($"Error writing file: {filePath}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Reads an OBJ and fills:
        ///  - verts: list of all "v" positions  
        ///  - uvs:   list of all "vt" coords  
        ///  - faces: one ObjFace per corner of each triangle (ignores extra verts on a polygon)  
        /// </summary>
        public static bool Import(
            string filePath,
            out List<Vector3> verts,
            out List<Vector2> uvs,
            out List<ObjFace> faces)
        {
            verts = new List<Vector3>();
            uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            faces = new List<ObjFace>();

            string[] allLines;
            try
            {
                allLines = File.ReadAllLines(filePath);
            }
            catch 
            {
                Debug.Log($"Error reading file: {filePath}");
                return false;
            }

            foreach (var raw in allLines)
            {
                if (raw.Length < 2 || raw[0] == '#') continue;

                var parts = raw.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                switch (parts[0])
                {
                    case "v":
                        verts.Add(new Vector3(
                            float.Parse(parts[1], CultureInfo.InvariantCulture),
                            float.Parse(parts[2], CultureInfo.InvariantCulture),
                            float.Parse(parts[3], CultureInfo.InvariantCulture)
                        ));
                        break;

                    case "vt":
                        uvs.Add(new Vector2(
                            float.Parse(parts[1], CultureInfo.InvariantCulture),
                            float.Parse(parts[2], CultureInfo.InvariantCulture)
                        ));
                        break;

                    case "vn":
                        normals.Add(new Vector3(
                            float.Parse(parts[1], CultureInfo.InvariantCulture),
                            float.Parse(parts[2], CultureInfo.InvariantCulture),
                            float.Parse(parts[3], CultureInfo.InvariantCulture)
                        ));
                        break;

                    case "f":
                        // triangle‐fan: always emit triangles (c0, ci, ci+1)
                        var c0 = parts[1].Split('/');
                        for (int i = 2; i < parts.Length; i++)
                        {
                            if (i + 1 >= parts.Length) break;
                            var ca = parts[i].Split('/');
                            var cb = parts[i + 1].Split('/');

                            int v0 = int.Parse(c0[0]) - 1, t0 = c0.Length > 1 && c0[1] != "" ? int.Parse(c0[1]) - 1 : -1, n0 = c0.Length > 2 ? int.Parse(c0[2]) - 1 : -1;
                            int va = int.Parse(ca[0]) - 1, ta = ca.Length > 1 && ca[1] != "" ? int.Parse(ca[1]) - 1 : -1, na = ca.Length > 2 ? int.Parse(ca[2]) - 1 : -1;
                            int vb = int.Parse(cb[0]) - 1, tb = cb.Length > 1 && cb[1] != "" ? int.Parse(cb[1]) - 1 : -1, nb = cb.Length > 2 ? int.Parse(cb[2]) - 1 : -1;

                            faces.Add(new ObjFace(v0, t0, n0));
                            faces.Add(new ObjFace(va, ta, na));
                            faces.Add(new ObjFace(vb, tb, nb));
                        }
                        break;
                }
            }
            return true;
        }

        public static void ImportToMesh(string filePath, Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));

            // 1) read raw OBJ lists
            List<Vector3> inVerts;
            List<Vector2> inUVs;
            List<ObjFace> inFaces;
            Import(filePath, out inVerts, out inUVs, out inFaces);

            // 2) build per-corner buffers
            var verts = new List<Vector3>(inFaces.Count);
            var uvs = new List<Vector2>(inFaces.Count);
            var tris = new List<int>(inFaces.Count);
            for (int i = 0; i < inFaces.Count; i++)
            {
                var f = inFaces[i];
                verts.Add(inVerts[f.vIndex]);
                uvs.Add(inUVs[f.tIndex]);
                tris.Add(i);
            }

            // 3) assign into the existing Mesh
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        public struct ObjFace
        {
            public int vIndex, tIndex, nIndex;
            public ObjFace(int v, int t, int n) { vIndex = v; tIndex = t; nIndex = n; }
        }

    }

}