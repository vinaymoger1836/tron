using System.Collections.Generic;
using UnityEngine;

namespace GridRun
{
    // Shared light-cycle art: sculpted shell, hubless ring wheels, prone rider, deployable
    // jet wings and solar sail. Game rules and hitboxes remain in their existing simulations.
    public sealed class CycleVisual : MonoBehaviour
    {
        private const int Shell = 0, Light = 1, Suit = 2, Tire = 3;
        private static Dictionary<int, Mesh> chassisMeshes, wheelMeshes, jetMeshes, plumeMeshes, sailMeshes;

        private readonly List<Renderer> bodyParts = new List<Renderer>();
        private readonly List<Renderer> lightParts = new List<Renderer>();
        private readonly List<Transform> wheels = new List<Transform>();
        private Transform sculpture, jetWings, jetPlume, sail;
        private float sailOpen, wingsOpen, lean, previousYaw;
        private Vector3 previousPosition;
        private bool jetOn, sailOn;

        public Vector4 GlowColor { get; private set; }

        public static CycleVisual Create(Transform parent, string name, Material body, Material light)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var visual = root.AddComponent<CycleVisual>();
            visual.Build(body, light);
            return visual;
        }

        public void SetPalette(Material body, Material light)
        {
            foreach (var part in bodyParts) part.sharedMaterial = body;
            foreach (var part in lightParts) part.sharedMaterial = light;
            UpdateGlowColor(light);
        }

        public void SetFlight(bool jet, bool openSail)
        {
            jetOn = jet;
            sailOn = openSail;
        }

        private void UpdateGlowColor(Material light)
        {
            Color color = light != null && light.HasProperty("_BaseColor") ? light.GetColor("_BaseColor") : Color.cyan;
            float peak = Mathf.Max(1, color.maxColorComponent);
            GlowColor = new Vector4(color.r / peak, color.g / peak, color.b / peak, 1);
        }

        private void OnEnable() { if (!GridLook.Emitters.Contains(this)) GridLook.Emitters.Add(this); }
        private void OnDisable() => GridLook.Emitters.Remove(this);

        private void Attach(Transform parent, string name, Dictionary<int, Mesh> meshes, Material body, Material light)
        {
            foreach (var pair in meshes)
            {
                Material material = pair.Key == Shell ? body : pair.Key == Light ? light :
                    pair.Key == Suit ? GridLook.Assets.Suit : GridLook.Assets.Tire;
                var renderer = GridLook.MeshObject(name, parent, pair.Value, material).GetComponent<Renderer>();
                if (pair.Key == Shell) bodyParts.Add(renderer);
                if (pair.Key == Light) lightParts.Add(renderer);
            }
        }

        private Transform Group(string name, Transform parent, Vector3 position)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            group.localPosition = position;
            return group;
        }

        private void Build(Material body, Material light)
        {
            if (chassisMeshes == null)
            {
                chassisMeshes = BuildChassis().Build("Cycle chassis");
                wheelMeshes = BuildWheel().Build("Cycle wheel");
                jetMeshes = BuildJet().Build("Cycle jet wings");
                plumeMeshes = BuildPlume().Build("Cycle jet plume");
                sailMeshes = BuildSail().Build("Cycle solar sail");
            }
            UpdateGlowColor(light);
            sculpture = Group("Sculpted cycle", transform, Vector3.zero);
            Attach(sculpture, "Cycle body", chassisMeshes, body, light);
            foreach (float z in new[] { -WheelZ, WheelZ })
            {
                var wheel = Group("Ring wheel", sculpture, new Vector3(0, WheelRadius, z));
                Attach(wheel, "Ring wheel", wheelMeshes, body, light);
                wheels.Add(wheel);
            }
            jetWings = Group("Light jet wings", sculpture, Vector3.zero);
            Attach(jetWings, "Jet wings", jetMeshes, body, light);
            jetWings.localScale = new Vector3(0, 1, 1);
            jetWings.gameObject.SetActive(false);
            jetPlume = Group("Light jet flame", sculpture, Vector3.zero);
            Attach(jetPlume, "Jet plume", plumeMeshes, body, light);
            jetPlume.gameObject.SetActive(false);
            sail = Group("Deploying solar sail", sculpture, Vector3.zero);
            Attach(sail, "Solar sail", sailMeshes, body, light);
            sail.localScale = new Vector3(0, 1, 1);
            sail.gameObject.SetActive(false);
            previousPosition = transform.position;
            previousYaw = transform.eulerAngles.y;
        }

        private const float WheelZ = .95f, WheelRadius = .5f;

        private static readonly GridMesh.Ring[] BodyRings =
        {
            new GridMesh.Ring(-1.10f, .54f, .04f, .12f), new GridMesh.Ring(-1.00f, .56f, .09f, .20f),
            new GridMesh.Ring(-0.80f, .60f, .12f, .28f), new GridMesh.Ring(-0.55f, .64f, .16f, .32f),
            new GridMesh.Ring(-0.28f, .66f, .19f, .31f), new GridMesh.Ring(0.00f, .66f, .195f, .30f),
            new GridMesh.Ring(0.28f, .66f, .19f, .31f), new GridMesh.Ring(0.55f, .64f, .16f, .33f),
            new GridMesh.Ring(0.80f, .60f, .12f, .29f), new GridMesh.Ring(1.00f, .56f, .09f, .21f),
            new GridMesh.Ring(1.16f, .53f, .05f, .12f)
        };

        private static readonly GridMesh.Ring[] TorsoRings =
        {
            new GridMesh.Ring(-0.66f, 1.03f, .10f, .07f), new GridMesh.Ring(-0.55f, 1.05f, .15f, .095f),
            new GridMesh.Ring(-0.38f, 1.07f, .155f, .10f), new GridMesh.Ring(-0.18f, 1.08f, .145f, .095f),
            new GridMesh.Ring(0.02f, 1.10f, .17f, .105f), new GridMesh.Ring(0.18f, 1.115f, .185f, .10f),
            new GridMesh.Ring(0.28f, 1.11f, .15f, .08f), new GridMesh.Ring(0.34f, 1.10f, .07f, .05f)
        };

        // A point on a superellipse ring at angle phi, pushed slightly outward for surface lines.
        private static Vector3 Surface(GridMesh.Ring ring, float phiDegrees, float exponent, float lift)
        {
            float phi = phiDegrees * Mathf.Deg2Rad, power = 2f / exponent;
            float c = Mathf.Cos(phi), s = Mathf.Sin(phi);
            return ring.Center + new Vector3(Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), power) * (ring.HalfWidth + lift),
                Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), power) * (ring.HalfHeight + lift), 0);
        }

        private static void SurfaceLine(GridMesh.Batch batch, GridMesh.Ring[] rings, int from, int to,
            float phi, float exponent, float radius)
        {
            foreach (float side in new[] { 1f, -1f })
            {
                var path = new List<Vector3>();
                for (int i = from; i <= to; i++)
                {
                    var p = Surface(rings[i], phi, exponent, .006f);
                    path.Add(new Vector3(p.x * side, p.y, p.z));
                }
                batch.Add(Light, GridMesh.Tube(path, radius, radius, 6, 5));
            }
        }

        private static Vector3 Mirror(Vector3 p, float side) => new Vector3(p.x * side, p.y, p.z);

        private static GridMesh.Batch BuildChassis()
        {
            var batch = new GridMesh.Batch();
            batch.Add(Shell, GridMesh.Loft(BodyRings, 28, 2.6f, .04f, .05f));
            SurfaceLine(batch, BodyRings, 2, 8, -14, 2.6f, .012f);
            SurfaceLine(batch, BodyRings, 3, 7, 40, 2.6f, .008f);

            // Fenders: partial shells over each ring wheel with a luminous outer edge.
            var fender = GridMesh.RoundedProfile(.175f, .535f, .575f, .017f, 2);
            foreach (var (z, from, to) in new[] { (-WheelZ, 62f, 212f), (WheelZ, -32f, 118f) })
            {
                var center = new Vector3(0, WheelRadius, z);
                batch.Add(Shell, GridMesh.Lathe(fender, 30, true, from, to), center, Quaternion.identity);
                foreach (float side in new[] { 1f, -1f })
                {
                    var edge = new List<Vector3>();
                    for (int i = 0; i <= 14; i++)
                    {
                        float a = Mathf.Lerp(from + 16, to - 16, i / 14f) * Mathf.Deg2Rad;
                        edge.Add(center + new Vector3(side * .176f, Mathf.Sin(a) * .556f, Mathf.Cos(a) * .556f));
                    }
                    batch.Add(Light, GridMesh.Tube(edge, .011f, .011f, 6, 1));
                }
            }

            // Fixed hubs that the ring wheels spin around.
            var hub = new List<Vector2> {
                new Vector2(-.13f, .0001f), new Vector2(-.128f, .12f), new Vector2(-.11f, .25f), new Vector2(-.07f, .30f),
                new Vector2(.07f, .30f), new Vector2(.11f, .25f), new Vector2(.128f, .12f), new Vector2(.13f, .0001f) };
            var hubMesh = GridMesh.Lathe(hub, 32, false);
            foreach (float z in new[] { -WheelZ, WheelZ })
            {
                var center = new Vector3(0, WheelRadius, z);
                batch.Add(Shell, hubMesh, center, Quaternion.identity);
                batch.Add(Light, GridMesh.Annulus(.165f, .185f, 32, true, .1315f), center, Quaternion.identity);
                batch.Add(Light, GridMesh.Annulus(.165f, .185f, 32, false, -.1315f), center, Quaternion.identity);
            }

            // Prone rider: suit, helmet, limbs, and circuit lines.
            batch.Add(Suit, GridMesh.Loft(TorsoRings, 18, 2.3f, .03f, .02f));
            SurfaceLine(batch, TorsoRings, 1, 6, 28, 2.3f, .007f);
            var toZ = Quaternion.Euler(0, -90, 0);
            batch.Add(Shell, GridMesh.Ellipsoid(new Vector3(.155f, .128f, .125f)), new Vector3(0, 1.205f, .47f), toZ);
            batch.Add(Suit, GridMesh.Tube(new[] { new Vector3(0, 1.10f, .30f), new Vector3(0, 1.16f, .40f) }, .055f, .05f, 10, 1));
            var visor = new List<Vector3>();
            for (int i = 0; i <= 16; i++)
            {
                float a = Mathf.Lerp(-105, 105, i / 16f) * Mathf.Deg2Rad;
                visor.Add(new Vector3(Mathf.Sin(a) * .129f, 1.19f, .47f + Mathf.Cos(a) * .158f));
            }
            batch.Add(Light, GridMesh.Tube(visor, .011f, .011f, 6, 1));
            var crest = new List<Vector3>();
            for (int i = 0; i <= 10; i++)
            {
                float a = Mathf.Lerp(-80, 60, i / 10f) * Mathf.Deg2Rad;
                crest.Add(new Vector3(0, 1.205f + Mathf.Cos(a) * .131f, .47f - Mathf.Sin(a) * .16f));
            }
            batch.Add(Light, GridMesh.Tube(crest, .007f, .007f, 6, 1));
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3[] arm = { new Vector3(.17f, 1.12f, .22f), new Vector3(.235f, 1.03f, .40f), new Vector3(.17f, .96f, .64f) };
                Vector3[] leg = { new Vector3(.10f, 1.04f, -.52f), new Vector3(.195f, .88f, -.80f), new Vector3(.17f, .80f, -1.06f) };
                batch.Add(Suit, GridMesh.Tube(System.Array.ConvertAll(arm, p => Mirror(p, side)), .054f, .04f, 10, 5));
                batch.Add(Suit, GridMesh.Tube(System.Array.ConvertAll(leg, p => Mirror(p, side)), .074f, .052f, 10, 5));
                batch.Add(Shell, GridMesh.Ellipsoid(new Vector3(.06f, .036f, .042f)), Mirror(new Vector3(.165f, .95f, .68f), side), toZ);
                batch.Add(Shell, GridMesh.Tube(new[] { Mirror(new Vector3(.17f, .80f, -1.06f), side), Mirror(new Vector3(.16f, .74f, -1.22f), side) }, .056f, .042f, 10, 1));
                batch.Add(Light, GridMesh.Tube(System.Array.ConvertAll(arm, p => Mirror(p + new Vector3(.047f, .012f, 0), side)), .007f, .006f, 6, 5));
                batch.Add(Light, GridMesh.Tube(System.Array.ConvertAll(leg, p => Mirror(p + new Vector3(.066f, .02f, 0), side)), .008f, .007f, 6, 5));
            }
            // Identity disc on the rider's back.
            var flat = Quaternion.Euler(0, 0, 90);
            var discCenter = new Vector3(0, 1.214f, .04f);
            batch.Add(Shell, GridMesh.Ellipsoid(new Vector3(.014f, .092f, .092f)), discCenter, flat);
            batch.Add(Light, GridMesh.Annulus(.064f, .088f, 32, true, .0145f), discCenter, flat);
            batch.Add(Light, GridMesh.Annulus(.018f, .03f, 20, true, .0145f), discCenter, flat);
            return batch;
        }

        private static GridMesh.Batch BuildWheel()
        {
            var batch = new GridMesh.Batch();
            batch.Add(Tire, GridMesh.Lathe(GridMesh.RoundedProfile(.15f, .33f, WheelRadius, .05f, 4), 56, true));
            foreach (float side in new[] { 1f, -1f })
            {
                bool positive = side > 0;
                batch.Add(Light, GridMesh.Annulus(.412f, .442f, 56, positive, side * .1512f));
                batch.Add(Light, GridMesh.Annulus(.384f, .392f, 56, positive, side * .1512f));
            }
            batch.Add(Light, GridMesh.Lathe(GridMesh.RoundedProfile(.012f, .494f, .505f, .004f, 2), 56, true));
            return batch;
        }

        private static GridMesh.Batch BuildJet()
        {
            var batch = new GridMesh.Batch();
            Vector2[] left = { new Vector2(-.16f, .35f), new Vector2(-1.30f, -.22f), new Vector2(-1.55f, -.32f),
                new Vector2(-1.45f, -.50f), new Vector2(-.16f, -.75f) };
            foreach (float side in new[] { -1f, 1f })
            {
                var outline = System.Array.ConvertAll(left, p => new Vector2(p.x * -side, p.y));
                var wing = Matrix4x4.TRS(new Vector3(0, .72f, 0), Quaternion.Euler(0, 0, side * 6), Vector3.one);
                batch.Add(Shell, GridMesh.Plate(outline, .04f), wing);
                Vector3 P(Vector2 p) => wing.MultiplyPoint3x4(new Vector3(p.x, .024f, p.y));
                batch.Add(Light, GridMesh.Tube(new[] { P(outline[0] + new Vector2(side * .04f, -.02f)), P(outline[1]), P(outline[2]) }, .014f, .014f, 6, 1));
                batch.Add(Light, GridMesh.Tube(new[] { P(outline[3]), P(outline[4] + new Vector2(side * .04f, .03f)) }, .01f, .01f, 6, 1));
                Vector3 tip = P(outline[2]);
                batch.Add(Light, GridMesh.Tube(new[] { tip + new Vector3(0, -.02f, -.05f), tip + new Vector3(side * .02f, .26f, -.18f) }, .012f, .008f, 6, 1));
                var nacelle = new List<Vector2> { new Vector2(-.30f, .0001f), new Vector2(-.27f, .06f), new Vector2(-.15f, .09f),
                    new Vector2(.20f, .085f), new Vector2(.30f, .072f), new Vector2(.30f, .0001f) };
                var position = new Vector3(side * .30f, .70f, -.85f);
                batch.Add(Shell, GridMesh.Lathe(nacelle, 20, false), position, Quaternion.Euler(0, 90, 0));
                batch.Add(Light, GridMesh.Annulus(.045f, .068f, 20, true, 0), position + new Vector3(0, 0, -.302f), Quaternion.Euler(0, 90, 0));
            }
            return batch;
        }

        private static GridMesh.Batch BuildPlume()
        {
            var batch = new GridMesh.Batch();
            foreach (float side in new[] { -1f, 1f })
                batch.Add(Light, GridMesh.Ellipsoid(new Vector3(.42f, .05f, .05f), 12, 8),
                    new Vector3(side * .30f, .70f, -1.55f), Quaternion.Euler(0, 90, 0));
            batch.Add(Light, GridMesh.Ellipsoid(new Vector3(.55f, .07f, .07f), 12, 8),
                new Vector3(0, .62f, -1.62f), Quaternion.Euler(0, 90, 0));
            return batch;
        }

        private static GridMesh.Batch BuildSail()
        {
            var batch = new GridMesh.Batch();
            Vector3 apex = new Vector3(0, 1.67f, -.48f);
            var membrane = new Mesh { name = "Solar sail membrane" };
            membrane.vertices = new[] { apex, new Vector3(-2.15f, 1.28f, -.72f), new Vector3(-1.68f, 1.30f, .54f),
                new Vector3(2.15f, 1.28f, -.72f), new Vector3(1.68f, 1.30f, .54f) };
            membrane.triangles = new[] { 0, 1, 2, 0, 2, 1, 0, 4, 3, 0, 3, 4 };
            membrane.RecalculateNormals();
            batch.Add(Suit, membrane);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 back = new Vector3(side * 2.15f, 1.28f, -.72f), front = new Vector3(side * 1.68f, 1.30f, .54f);
                batch.Add(Light, GridMesh.Tube(new[] { apex, back }, .022f, .016f, 6, 1));
                batch.Add(Light, GridMesh.Tube(new[] { back, front }, .016f, .016f, 6, 1));
                batch.Add(Light, GridMesh.Tube(new[] { apex, (back + front) * .5f }, .01f, .01f, 6, 1));
                batch.Add(Shell, GridMesh.Tube(new[] { new Vector3(side * .12f, .95f, -.4f), apex + new Vector3(side * .03f, -.05f, 0) }, .03f, .02f, 8, 1));
            }
            return batch;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            float travel = Vector3.Distance(transform.position, previousPosition) / Mathf.Max(.01f, transform.lossyScale.x);
            previousPosition = transform.position;
            float spin = Mathf.Min(360f, travel / WheelRadius * Mathf.Rad2Deg) + dt * 30f;
            foreach (var wheel in wheels) wheel.Rotate(Vector3.right, spin, Space.Self);
            float yaw = transform.eulerAngles.y;
            float turn = Mathf.DeltaAngle(previousYaw, yaw);
            previousYaw = yaw;
            lean = Mathf.Lerp(lean, Mathf.Clamp(-turn * .12f, -12f, 12f), 1f - Mathf.Exp(-7f * dt));
            sculpture.localRotation = Quaternion.Euler(0, 0, lean);
            wingsOpen = Mathf.MoveTowards(wingsOpen, jetOn ? 1f : 0f, dt * 5f);
            jetWings.gameObject.SetActive(wingsOpen > .01f);
            jetWings.localScale = new Vector3(Mathf.SmoothStep(0, 1, wingsOpen), 1, 1);
            jetPlume.gameObject.SetActive(jetOn);
            if (jetOn) jetPlume.localScale = new Vector3(1, 1, .85f + Mathf.Sin(Time.time * 31f) * .12f);
            sailOpen = Mathf.MoveTowards(sailOpen, sailOn ? 1f : 0f, dt * 4f);
            sail.gameObject.SetActive(sailOpen > .01f);
            sail.localScale = new Vector3(Mathf.SmoothStep(0, 1, sailOpen), 1, 1);
        }
    }
}
