using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Client.Visuals
{
    /// <summary>
    /// Builds prototype geometry in code. Until there are art assets and hand-built scenes,
    /// levels and props are assembled from these pieces at startup.
    /// </summary>
    public static class GreyboxKit
    {
        static readonly Dictionary<Vector3, Mesh> BoxMeshes = new Dictionary<Vector3, Mesh>();
        static readonly Dictionary<PrimitiveType, Mesh> PrimitiveMeshes = new Dictionary<PrimitiveType, Mesh>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            BoxMeshes.Clear();
            PrimitiveMeshes.Clear();
        }

        /// <summary>
        /// A solid, collidable box of level geometry. Its texture coordinates are in metres,
        /// so the grid stays one metre per square on every face whatever the box's size.
        /// </summary>
        /// <param name="solid">False for a box that is only drawn, where the collision is built elsewhere.</param>
        public static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Color color, bool solid = true)
        {
            var go = new GameObject(name) { layer = GameLayers.Default };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;

            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size);
            go.AddComponent<MeshRenderer>().sharedMaterial = GreyboxMaterials.Grid(color);
            if (solid) go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        /// <summary>A unit primitive with no collider, for things that are only looked at.</summary>
        public static GameObject Visual(Transform parent, string name, PrimitiveType shape,
            Vector3 localPosition, Vector3 localScale, Material material, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;

            go.AddComponent<MeshFilter>().sharedMesh = PrimitiveMesh(shape);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (!castShadows)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return go;
        }

        /// <summary>The built-in mesh for a primitive shape, fetched once and shared.</summary>
        public static Mesh PrimitiveMesh(PrimitiveType shape)
        {
            if (PrimitiveMeshes.TryGetValue(shape, out Mesh mesh) && mesh != null) return mesh;

            GameObject temp = GameObject.CreatePrimitive(shape);
            temp.SetActive(false);
            mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(temp);

            PrimitiveMeshes[shape] = mesh;
            return mesh;
        }

        static Mesh BoxMesh(Vector3 size)
        {
            if (BoxMeshes.TryGetValue(size, out Mesh cached) && cached != null) return cached;

            var vertices = new List<Vector3>(24);
            var normals = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var triangles = new List<int>(36);
            Vector3 half = size * 0.5f;

            // For each face: outward normal, then the directions that read as "right" and "up"
            // when looking at the face from outside.
            AddFace(Vector3.up, Vector3.right, Vector3.forward);
            AddFace(Vector3.down, Vector3.right, Vector3.back);
            AddFace(Vector3.forward, Vector3.left, Vector3.up);
            AddFace(Vector3.back, Vector3.right, Vector3.up);
            AddFace(Vector3.right, Vector3.forward, Vector3.up);
            AddFace(Vector3.left, Vector3.back, Vector3.up);

            var mesh = new Mesh { name = "Greybox Box " + size };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            BoxMeshes[size] = mesh;
            return mesh;

            void AddFace(Vector3 normal, Vector3 right, Vector3 up)
            {
                float halfWidth = Mathf.Abs(Vector3.Dot(right, half));
                float halfHeight = Mathf.Abs(Vector3.Dot(up, half));
                Vector3 centre = normal * Mathf.Abs(Vector3.Dot(normal, half));
                Vector3 r = right * halfWidth, u = up * halfHeight;

                int start = vertices.Count;
                vertices.Add(centre - r - u);
                vertices.Add(centre - r + u);
                vertices.Add(centre + r + u);
                vertices.Add(centre + r - u);
                for (int i = 0; i < 4; i++) normals.Add(normal);

                float width = halfWidth * 2f, height = halfHeight * 2f;
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, height));
                uvs.Add(new Vector2(width, height));
                uvs.Add(new Vector2(width, 0f));

                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
            }
        }
    }
}
