using System.Collections.Generic;
using UnityEngine;

namespace GridRun
{
    // Shared, lightweight visual. Game rules and hitboxes remain in their existing simulations.
    public sealed class CycleVisual : MonoBehaviour
    {
        private readonly List<Renderer> bodyParts = new List<Renderer>();
        private readonly List<Renderer> lightParts = new List<Renderer>();
        private readonly List<Renderer> darkParts = new List<Renderer>();
        private readonly List<Transform> wheels = new List<Transform>();
        private Transform sculpture;
        private Transform jetPlume;
        private Transform sail;
        private float sailOpen, lean, previousYaw;
        private Vector3 previousPosition;
        private bool jetOn, sailOn;

        public static CycleVisual Create(Transform parent, string name, Material body, Material light, Material dark)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var visual = root.AddComponent<CycleVisual>();
            visual.Build(body, light, dark);
            return visual;
        }

        public void SetPalette(Material body, Material light)
        {
            foreach (var part in bodyParts) part.sharedMaterial = body;
            foreach (var part in lightParts) part.sharedMaterial = light;
        }

        public void SetFlight(bool jet, bool openSail)
        {
            jetOn = jet;
            sailOn = openSail;
        }

        private static Renderer Shape(string name, PrimitiveType type, Transform parent, Vector3 position,
            Vector3 scale, Material material)
        {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(obj.GetComponent<Collider>());
            return obj.GetComponent<Renderer>();
        }

        private static Renderer MeshPart(string name, Mesh mesh, Transform parent, Material material)
        {
            var obj = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        private static Mesh HullMesh(float size = 1f)
        {
            // Eight-sided tapered fairing, with a pointed nose and narrowed tail.
            float[] zs = { -1.35f, -1.05f, -.55f, .25f, .85f, 1.32f };
            float[] widths = { .12f, .31f, .43f, .40f, .27f, .04f };
            float[] heights = { .14f, .23f, .28f, .25f, .15f, .04f };
            float[] centers = { .62f, .66f, .68f, .65f, .61f, .59f };
            var vertices = new Vector3[zs.Length * 8];
            for (int ring = 0; ring < zs.Length; ring++)
                for (int side = 0; side < 8; side++)
                {
                    float angle = side * Mathf.PI * .25f;
                    vertices[ring * 8 + side] = new Vector3(
                        Mathf.Cos(angle) * widths[ring] * size,
                        (centers[ring] + Mathf.Sin(angle) * heights[ring]) * size,
                        zs[ring] * size);
                }
            var triangles = new List<int>();
            for (int ring = 0; ring < zs.Length - 1; ring++)
                for (int side = 0; side < 8; side++)
                {
                    int a = ring * 8 + side, b = ring * 8 + (side + 1) % 8;
                    int c = (ring + 1) * 8 + side, d = (ring + 1) * 8 + (side + 1) % 8;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(d); triangles.Add(c);
                }
            for (int side = 1; side < 7; side++)
            {
                triangles.Add(0); triangles.Add(side + 1); triangles.Add(side);
                int last = (zs.Length - 1) * 8;
                triangles.Add(last); triangles.Add(last + side); triangles.Add(last + side + 1);
            }
            var mesh = new Mesh { name = "Streamlined cycle fairing", vertices = vertices, triangles = triangles.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh RingMesh(float radius, float tube)
        {
            const int around = 28, section = 8;
            var vertices = new Vector3[(around + 1) * (section + 1)];
            var triangles = new int[around * section * 6];
            for (int i = 0; i <= around; i++)
            {
                float a = i * Mathf.PI * 2 / around;
                for (int j = 0; j <= section; j++)
                {
                    float b = j * Mathf.PI * 2 / section;
                    float radial = radius + Mathf.Cos(b) * tube;
                    vertices[i * (section + 1) + j] =
                        new Vector3(Mathf.Sin(b) * tube, Mathf.Cos(a) * radial, Mathf.Sin(a) * radial);
                }
            }
            int t = 0;
            for (int i = 0; i < around; i++)
                for (int j = 0; j < section; j++)
                {
                    int a = i * (section + 1) + j, b = a + section + 1;
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = a + 1;
                    triangles[t++] = a + 1; triangles[t++] = b; triangles[t++] = b + 1;
                }
            var mesh = new Mesh { name = "Light cycle wheel ring", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Renderer Link(string name, Transform parent, Vector3 start, Vector3 end,
            float diameter, Material material, PrimitiveType type = PrimitiveType.Capsule)
        {
            var renderer = Shape(name, type, parent, (start + end) * .5f,
                new Vector3(diameter, Vector3.Distance(start, end) * .5f, diameter), material);
            renderer.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (end - start).normalized);
            return renderer;
        }

        private static Mesh SailMesh()
        {
            var mesh = new Mesh { name = "Solar sail membrane" };
            mesh.vertices = new[] {
                new Vector3(0, 1.67f, -.48f), new Vector3(-2.15f, 1.28f, -.72f),
                new Vector3(-1.68f, 1.30f, .54f), new Vector3(2.15f, 1.28f, -.72f),
                new Vector3(1.68f, 1.30f, .54f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 1, 0, 4, 3, 0, 3, 4 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Build(Material body, Material light, Material dark)
        {
            sculpture = new GameObject("Articulated silhouette").transform;
            sculpture.SetParent(transform, false);
            bodyParts.Add(MeshPart("Tapered fairing", HullMesh(), sculpture, body));
            bodyParts.Add(Shape("Raised cockpit", PrimitiveType.Capsule, sculpture,
                new Vector3(0, .93f, -.22f), new Vector3(.53f, .23f, .82f), body));
            for (int side = -1; side <= 1; side += 2)
            {
                bodyParts.Add(Link("Rear fork", sculpture, new Vector3(side * .24f, .61f, -.38f),
                    new Vector3(side * .18f, .39f, -.89f), .09f, body));
                bodyParts.Add(Link("Front fork", sculpture, new Vector3(side * .22f, .65f, .50f),
                    new Vector3(side * .16f, .39f, .87f), .08f, body));
                bodyParts.Add(Link("Rider arm", sculpture, new Vector3(side * .19f, 1.21f, -.45f),
                    new Vector3(side * .32f, .94f, .37f), .12f, body));
                bodyParts.Add(Link("Rider leg", sculpture, new Vector3(side * .19f, 1.03f, -.66f),
                    new Vector3(side * .30f, .56f, -.96f), .15f, body));
                lightParts.Add(Link("Side light", sculpture, new Vector3(side * .38f, .71f, -.68f),
                    new Vector3(side * .28f, .72f, .88f), .045f, light, PrimitiveType.Cylinder));
            }
            var torso = Shape("Rider torso", PrimitiveType.Capsule, sculpture,
                new Vector3(0, 1.17f, -.33f), new Vector3(.39f, .29f, .43f), body);
            torso.transform.localRotation = Quaternion.Euler(55, 0, 0);
            bodyParts.Add(torso);
            bodyParts.Add(Shape("Helmet", PrimitiveType.Sphere, sculpture,
                new Vector3(0, 1.43f, -.02f), new Vector3(.38f, .31f, .43f), body));
            lightParts.Add(Shape("Visor", PrimitiveType.Sphere, sculpture,
                new Vector3(0, 1.44f, .22f), new Vector3(.30f, .13f, .075f), light));
            lightParts.Add(Shape("Nose light", PrimitiveType.Sphere, sculpture,
                new Vector3(0, .61f, 1.27f), new Vector3(.27f, .09f, .09f), light));

            var tireMesh = RingMesh(.39f, .13f);
            var rimMesh = RingMesh(.51f, .035f);
            foreach (float z in new[] { -.88f, .88f })
            {
                var wheel = new GameObject("Spinning light wheel").transform;
                wheel.SetParent(sculpture, false);
                wheel.localPosition = new Vector3(0, .40f, z);
                wheels.Add(wheel);
                darkParts.Add(MeshPart("Dark tire", tireMesh, wheel, dark));
                lightParts.Add(MeshPart("Fine luminous rim", rimMesh, wheel, light));
                for (int spoke = 0; spoke < 3; spoke++)
                {
                    float angle = spoke * Mathf.PI * 2 / 3;
                    bodyParts.Add(Link("Wheel spoke", wheel, Vector3.zero,
                        new Vector3(0, Mathf.Cos(angle) * .31f, Mathf.Sin(angle) * .31f),
                        .06f, body, PrimitiveType.Cylinder));
                }
            }

            jetPlume = new GameObject("Light jet flame").transform;
            jetPlume.SetParent(sculpture, false);
            lightParts.Add(Shape("Jet core", PrimitiveType.Capsule, jetPlume,
                new Vector3(0, .63f, -1.48f), new Vector3(.34f, .28f, .95f), light));
            for (int side = -1; side <= 1; side += 2)
                lightParts.Add(Shape("Jet side plume", PrimitiveType.Capsule, jetPlume,
                    new Vector3(side * .30f, .59f, -1.30f), new Vector3(.14f, .17f, .55f), light));
            jetPlume.gameObject.SetActive(false);

            sail = new GameObject("Deploying solar sail").transform;
            sail.SetParent(sculpture, false);
            bodyParts.Add(MeshPart("Swept solar membrane", SailMesh(), sail, body));
            for (int side = -1; side <= 1; side += 2)
            {
                lightParts.Add(Link("Sail leading edge", sail, new Vector3(0, 1.67f, -.48f),
                    new Vector3(side * 2.15f, 1.28f, -.72f), .035f, light, PrimitiveType.Cylinder));
                lightParts.Add(Link("Sail outer edge", sail, new Vector3(side * 2.15f, 1.28f, -.72f),
                    new Vector3(side * 1.68f, 1.30f, .54f), .035f, light, PrimitiveType.Cylinder));
            }
            sail.localScale = new Vector3(0, 1, 1);
            sail.gameObject.SetActive(false);
            previousPosition = transform.position;
            previousYaw = transform.eulerAngles.y;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            float travel = Vector3.Distance(transform.position, previousPosition);
            previousPosition = transform.position;
            float spin = Mathf.Min(360f, travel / .4f * Mathf.Rad2Deg) + dt * 30f;
            foreach (var wheel in wheels) wheel.Rotate(Vector3.right, spin, Space.Self);
            float yaw = transform.eulerAngles.y;
            float turn = Mathf.DeltaAngle(previousYaw, yaw);
            previousYaw = yaw;
            lean = Mathf.Lerp(lean, Mathf.Clamp(-turn * .12f, -12f, 12f),
                1f - Mathf.Exp(-7f * dt));
            sculpture.localRotation = Quaternion.Euler(0, 0, lean);
            jetPlume.gameObject.SetActive(jetOn);
            if (jetOn) jetPlume.localScale = new Vector3(1, 1, .83f + Mathf.Sin(Time.time * 28f) * .14f);
            sailOpen = Mathf.MoveTowards(sailOpen, sailOn ? 1f : 0f, dt * 4f);
            sail.gameObject.SetActive(sailOpen > .01f);
            sail.localScale = new Vector3(sailOpen, 1, 1);
        }
    }
}

