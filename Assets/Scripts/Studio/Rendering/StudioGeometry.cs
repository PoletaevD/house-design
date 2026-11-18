using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HouseDesign
{
    internal sealed class StudioGeometry
    {
        private static readonly int s_BaseMapTransform = Shader.PropertyToID("_BaseMap_ST");
        private readonly Mesh _box;
        private readonly Mesh _roundedBox;
        private readonly Mesh _cylinder;
        private readonly Mesh _sphere;
        private readonly MaterialPropertyBlock _properties = new();

        internal StudioGeometry()
        {
            var boxSamples = new[]
            {
                -.5f,
                .5f
            };
            _box = CreateBox(boxSamples, false);

            var roundedSamples = new[]
            {
                -.5f,
                -.465f,
                -.415f,
                .415f,
                .465f,
                .5f
            };
            _roundedBox = CreateBox(roundedSamples, true);

            _cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            _sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        }

        internal Transform Box(Transform parent, Vector3 position, Vector3 size, Material material, Vector4? textureTransform = null)
        {
            var part = Part(parent, position, size, material, _box, out var renderer);

            if (textureTransform.HasValue)
            {
                _properties.SetVector(s_BaseMapTransform, textureTransform.Value);
                renderer.SetPropertyBlock(_properties);
            }

            return part;
        }

        internal Transform RoundedBox(Transform parent, Vector3 position, Vector3 size, Material material)
        {
            return Part(parent, position, size, material, _roundedBox, out _);
        }

        internal Transform Cylinder(Transform parent, Vector3 position, Vector3 size, Material material)
        {
            return Part(parent, position, size * .5f, material, _cylinder, out _);
        }

        internal Transform Sphere(Transform parent, Vector3 position, Vector3 size, Material material)
        {
            return Part(parent, position, size * .5f, material, _sphere, out _);
        }

        internal void Dispose()
        {
            Object.Destroy(_box);
            Object.Destroy(_roundedBox);
        }

        private Transform Part(Transform parent, Vector3 position, Vector3 size, Material material, Mesh mesh, out MeshRenderer renderer)
        {
            var part = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
            part.layer = parent.gameObject.layer;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = size;
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            return part.transform;
        }

        private Mesh CreateBox(float[] samples, bool rounded)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var indices = new List<int>();

            AddFace(Vector3.forward, Vector3.right, Vector3.up, samples, rounded, vertices, normals, uv, indices);
            AddFace(Vector3.back, Vector3.left, Vector3.up, samples, rounded, vertices, normals, uv, indices);
            AddFace(Vector3.right, Vector3.back, Vector3.up, samples, rounded, vertices, normals, uv, indices);
            AddFace(Vector3.left, Vector3.forward, Vector3.up, samples, rounded, vertices, normals, uv, indices);
            AddFace(Vector3.up, Vector3.right, Vector3.back, samples, rounded, vertices, normals, uv, indices);
            AddFace(Vector3.down, Vector3.right, Vector3.forward, samples, rounded, vertices, normals, uv, indices);
            var mesh = new Mesh
            {
                name = rounded ? "Rounded box" : "Box"
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(indices, 0);

            return mesh;
        }

        private void AddFace(Vector3 normal, Vector3 right, Vector3 up, float[] samples, bool rounded, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv, List<int> indices)
        {
            const float Inner = .415f;
            var start = vertices.Count;
            var stride = samples.Length;

            for (var row = 0; row < stride; row++)
            {
                for (var column = 0; column < stride; column++)
                {
                    var vertex = normal * .5f + right * samples[column] + up * samples[row];
                    var surfaceNormal = normal;

                    if (rounded)
                    {
                        var inner = new Vector3(Mathf.Clamp(vertex.x, -Inner, Inner), Mathf.Clamp(vertex.y, -Inner, Inner), Mathf.Clamp(vertex.z, -Inner, Inner));
                        surfaceNormal = (vertex - inner).normalized;
                        vertex = inner + surfaceNormal * (.5f - Inner);
                    }

                    vertices.Add(vertex);
                    normals.Add(surfaceNormal);
                    uv.Add(new Vector2(samples[column] + .5f, samples[row] + .5f));

                    if (row == 0 || column == 0)
                    {
                        continue;
                    }

                    var far = start + row * stride + column;
                    indices.Add(far - stride - 1);
                    indices.Add(far - stride);
                    indices.Add(far);
                    indices.Add(far - stride - 1);
                    indices.Add(far);
                    indices.Add(far - 1);
                }
            }
        }
    }
}
