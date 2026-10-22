using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GridRun
{
    // Procedural mesh primitives for the Grid art set. Meshes are built once and shared;
    // Unity's clockwise front-face winding is used throughout (outward normals).
    public static class GridMesh
    {
        public struct Ring
        {
            public Vector3 Center;
            public float HalfWidth, HalfHeight;
            public Ring(float z, float y, float halfWidth, float halfHeight)
            {
                Center = new Vector3(0, y, z); HalfWidth = halfWidth; HalfHeight = halfHeight;
            }
        }

        // Collects transformed meshes per material slot, then merges each slot into one mesh.
        public sealed class Batch
        {
            private readonly Dictionary<int, List<CombineInstance>> slots = new Dictionary<int, List<CombineInstance>>();

            public void Add(int slot, Mesh mesh, Matrix4x4 matrix)
            {
                if (!slots.TryGetValue(slot, out var list)) slots[slot] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mesh, transform = matrix });
            }

            public void Add(int slot, Mesh mesh) => Add(slot, mesh, Matrix4x4.identity);
            public void Add(int slot, Mesh mesh, Vector3 position, Quaternion rotation) =>
                Add(slot, mesh, Matrix4x4.TRS(position, rotation, Vector3.one));

            public Dictionary<int, Mesh> Build(string name)
            {
                var result = new Dictionary<int, Mesh>();
                foreach (var pair in slots)
                {
                    int vertices = 0;
                    foreach (var item in pair.Value) vertices += item.mesh.vertexCount;
                    var mesh = new Mesh { name = name + " " + pair.Key };
                    if (vertices > 65000) mesh.indexFormat = IndexFormat.UInt32;
                    mesh.CombineMeshes(pair.Value.ToArray(), true, true);
                    mesh.RecalculateBounds();
                    mesh.UploadMeshData(true);
                    result[pair.Key] = mesh;
                }
                return result;
            }
        }

        private static Mesh Finish(string name, List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float SignedPow(float value, float power) =>
            Mathf.Sign(value) * Mathf.Pow(Mathf.Abs(value), power);

        // Superellipse cross-sections lofted along Z, closed at both ends with soft points.
        public static Mesh Loft(IList<Ring> rings, int around, float exponent, float startTip, float endTip)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float power = 2f / exponent;
            foreach (var ring in rings)
                for (int j = 0; j < around; j++)
                {
                    float angle = j * Mathf.PI * 2 / around;
                    vertices.Add(ring.Center + new Vector3(SignedPow(Mathf.Cos(angle), power) * ring.HalfWidth,
                        SignedPow(Mathf.Sin(angle), power) * ring.HalfHeight, 0));
                }
            for (int i = 0; i < rings.Count - 1; i++)
                for (int j = 0; j < around; j++)
                {
                    int a = i * around + j, b = i * around + (j + 1) % around;
                    int c = a + around, d = b + around;
                    triangles.AddRange(new[] { a, b, c, b, d, c });
                }
            int start = vertices.Count;
            vertices.Add(rings[0].Center - new Vector3(0, 0, startTip));
            int end = vertices.Count;
            vertices.Add(rings[rings.Count - 1].Center + new Vector3(0, 0, endTip));
            int last = (rings.Count - 1) * around;
            for (int j = 0; j < around; j++)
            {
                int next = (j + 1) % around;
                triangles.AddRange(new[] { start, next, j });
                triangles.AddRange(new[] { end, last + j, last + next });
            }
            return Finish("Loft", vertices, triangles);
        }

        // Revolves an (axial, radius) profile around the X axis. Angle 0 points to +Z, 90 to +Y.
        // Closed profiles run clockwise in (axial, radius) space: outer edge toward +axial first.
        // A partial sweep tapers the profile to its centroid at both ends so the shell closes.
        public static Mesh Lathe(IList<Vector2> profile, int segments, bool closedProfile,
            float startDegrees = 0, float endDegrees = 360)
        {
            bool full = Mathf.Approximately(endDegrees - startDegrees, 360);
            int columns = full ? segments : segments + 1;
            var centroid = Vector2.zero;
            foreach (var point in profile) centroid += point;
            centroid /= profile.Count;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int s = 0; s < columns; s++)
            {
                float t = (float)s / segments;
                float angle = Mathf.Lerp(startDegrees, endDegrees, t) * Mathf.Deg2Rad;
                float taper = full ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / .14f)) *
                    Mathf.SmoothStep(0, 1, Mathf.Clamp01((1 - t) / .14f));
                foreach (var source in profile)
                {
                    var point = full ? source : centroid + (source - centroid) * Mathf.Max(taper, .02f);
                    vertices.Add(new Vector3(point.x, Mathf.Sin(angle) * point.y, Mathf.Cos(angle) * point.y));
                }
            }
            int count = profile.Count;
            int edges = closedProfile ? count : count - 1;
            for (int s = 0; s < segments; s++)
            {
                int s1 = full ? (s + 1) % segments : s + 1;
                for (int k = 0; k < edges; k++)
                {
                    int k1 = (k + 1) % count;
                    int a = s * count + k, b = s1 * count + k, c = s * count + k1, d = s1 * count + k1;
                    triangles.AddRange(new[] { a, c, b, c, d, b });
                }
            }
            return Finish("Lathe", vertices, triangles);
        }

        // Rounded rectangle profile in (axial, radius) space, clockwise for Lathe.
        public static List<Vector2> RoundedProfile(float halfWidth, float inner, float outer, float corner, int steps = 4)
        {
            var points = new List<Vector2>();
            void Arc(Vector2 center, float from)
            {
                for (int i = 0; i <= steps; i++)
                {
                    float a = (from - 90f * i / steps) * Mathf.Deg2Rad;
                    points.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * corner);
                }
            }
            Arc(new Vector2(-halfWidth + corner, outer - corner), 180);
            Arc(new Vector2(halfWidth - corner, outer - corner), 90);
            Arc(new Vector2(halfWidth - corner, inner + corner), 0);
            Arc(new Vector2(-halfWidth + corner, inner + corner), -90);
            return points;
        }

        public static List<Vector3> Smooth(IList<Vector3> points, int subdivisions)
        {
            var result = new List<Vector3>();
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 p0 = points[Mathf.Max(i - 1, 0)], p1 = points[i], p2 = points[i + 1];
                Vector3 p3 = points[Mathf.Min(i + 2, points.Count - 1)];
                for (int s = 0; s < subdivisions; s++)
                {
                    float t = (float)s / subdivisions, t2 = t * t, t3 = t2 * t;
                    result.Add(.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
                        (-p0 + 3 * p1 - 3 * p2 + p3) * t3));
                }
            }
            result.Add(points[points.Count - 1]);
            return result;
        }

        // Tapered tube along a polyline with rounded end caps (parallel-transport frames).
        public static Mesh Tube(IList<Vector3> path, float startRadius, float endRadius, int sides = 10,
            int subdivisions = 6)
        {
            var points = path.Count > 2 && subdivisions > 1 ? Smooth(path, subdivisions) : new List<Vector3>(path);
            int n = points.Count;
            var radii = new List<float>();
            float total = 0;
            var lengths = new float[n];
            for (int i = 1; i < n; i++) lengths[i] = total += Vector3.Distance(points[i - 1], points[i]);
            for (int i = 0; i < n; i++) radii.Add(Mathf.Lerp(startRadius, endRadius, total > 0 ? lengths[i] / total : 0));
            Vector3 startDir = (points[1] - points[0]).normalized, endDir = (points[n - 1] - points[n - 2]).normalized;
            points.Insert(0, points[0] - startDir * startRadius * .55f);
            radii.Insert(0, startRadius * .72f);
            points.Add(points[points.Count - 1] + endDir * endRadius * .55f);
            radii.Add(endRadius * .72f);
            n = points.Count;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            Vector3 normal = Vector3.Cross(startDir, Mathf.Abs(startDir.y) < .9f ? Vector3.up : Vector3.right).normalized;
            for (int i = 0; i < n; i++)
            {
                Vector3 tangent = (points[Mathf.Min(i + 1, n - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;
                normal = (normal - tangent * Vector3.Dot(normal, tangent)).normalized;
                Vector3 binormal = Vector3.Cross(tangent, normal);
                for (int j = 0; j < sides; j++)
                {
                    float a = j * Mathf.PI * 2 / sides;
                    vertices.Add(points[i] + (normal * Mathf.Cos(a) + binormal * Mathf.Sin(a)) * radii[i]);
                }
            }
            for (int i = 0; i < n - 1; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = i * sides + j, b = i * sides + (j + 1) % sides, c = a + sides, d = b + sides;
                    triangles.AddRange(new[] { a, b, c, b, d, c });
                }
            int head = vertices.Count;
            vertices.Add(points[0] - startDir * startRadius * .3f);
            int tail = vertices.Count;
            vertices.Add(points[n - 1] + endDir * endRadius * .3f);
            int last = (n - 1) * sides;
            for (int j = 0; j < sides; j++)
            {
                int next = (j + 1) % sides;
                triangles.AddRange(new[] { head, next, j });
                triangles.AddRange(new[] { tail, last + j, last + next });
            }
            return Finish("Tube", vertices, triangles);
        }

        // Flat ring in the YZ plane, single sided, facing +X or -X.
        public static Mesh Annulus(float inner, float outer, int segments, bool facePositiveX, float x = 0)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int s = 0; s < segments; s++)
            {
                float a = s * Mathf.PI * 2 / segments;
                var direction = new Vector3(0, Mathf.Sin(a), Mathf.Cos(a));
                vertices.Add(new Vector3(x, 0, 0) + direction * inner);
                vertices.Add(new Vector3(x, 0, 0) + direction * outer);
            }
            for (int s = 0; s < segments; s++)
            {
                int i0 = s * 2, o0 = i0 + 1, i1 = (s + 1) % segments * 2, o1 = i1 + 1;
                if (facePositiveX) triangles.AddRange(new[] { i0, i1, o0, o0, i1, o1 });
                else triangles.AddRange(new[] { i0, o0, i1, o0, o1, i1 });
            }
            return Finish("Annulus", vertices, triangles);
        }

        // Axis-aligned box with hard edges.
        public static Mesh Box(Vector3 center, Vector3 size)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            Vector3 h = size * .5f;
            void Face(Vector3 normal, Vector3 up)
            {
                Vector3 right = Vector3.Cross(normal, up);
                Vector3 c = center + Vector3.Scale(normal, h);
                Vector3 r = Vector3.Scale(right, h), u = Vector3.Scale(up, h);
                int i = vertices.Count;
                vertices.Add(c - r - u); vertices.Add(c - r + u); vertices.Add(c + r + u); vertices.Add(c + r - u);
                triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            Face(Vector3.right, Vector3.up); Face(Vector3.left, Vector3.up);
            Face(Vector3.forward, Vector3.up); Face(Vector3.back, Vector3.up);
            Face(Vector3.up, Vector3.forward); Face(Vector3.down, Vector3.back);
            var mesh = new Mesh { name = "Box" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        // Extruded convex outline in the XZ plane (wings, fins), flat-shaded.
        public static Mesh Plate(IList<Vector2> outline, float thickness)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float h = thickness * .5f;
            var center = Vector2.zero;
            foreach (var p in outline) center += p;
            center /= outline.Count;
            int count = outline.Count;
            float area = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 p = outline[i], q = outline[(i + 1) % count];
                area += p.x * q.y - q.x * p.y;
            }
            bool counterClockwise = area > 0; // in (x, z) with z drawn upward
            foreach (float y in new[] { h, -h })
            {
                int hub = vertices.Count;
                vertices.Add(new Vector3(center.x, y, center.y));
                for (int i = 0; i < count; i++) vertices.Add(new Vector3(outline[i].x, y, outline[i].y));
                for (int i = 0; i < count; i++)
                {
                    int a = hub + 1 + i, b = hub + 1 + (i + 1) % count;
                    bool up = y > 0;
                    if (up == counterClockwise) triangles.AddRange(new[] { hub, b, a });
                    else triangles.AddRange(new[] { hub, a, b });
                }
            }
            for (int i = 0; i < count; i++)
            {
                Vector2 p = outline[i], q = outline[(i + 1) % count];
                int s = vertices.Count;
                vertices.Add(new Vector3(p.x, h, p.y)); vertices.Add(new Vector3(q.x, h, q.y));
                vertices.Add(new Vector3(q.x, -h, q.y)); vertices.Add(new Vector3(p.x, -h, p.y));
                if (counterClockwise) triangles.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                else triangles.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }
            var mesh = new Mesh { name = "Plate" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        public static Mesh Ellipsoid(Vector3 radii, int segments = 20, int rings = 12)
        {
            var profile = new List<Vector2>();
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.PI * i / rings;
                profile.Add(new Vector2(-Mathf.Cos(a), Mathf.Sin(a) + (i == 0 || i == rings ? .0001f : 0)));
            }
            var unit = Lathe(profile, segments, false);
            var vertices = unit.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = Vector3.Scale(vertices[i], radii);
            unit.vertices = vertices;
            unit.RecalculateNormals();
            unit.RecalculateBounds();
            return unit;
        }
    }
}
