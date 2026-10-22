using System.Collections.Generic;
using UnityEngine;

namespace GridRun
{
    // On-foot rider for Grid Shift: suit, helmet with visor line, circuit lines, back disc,
    // and a simple walk cycle. Collision is the simulation's 0.28-unit radius, not this mesh.
    public sealed class RunnerVisual : MonoBehaviour
    {
        const int Shell = 0, Light = 1, Suit = 2;
        static Dictionary<int, Mesh> body, leg, arm;
        readonly List<Renderer> lights = new List<Renderer>();
        readonly Transform[] legs = new Transform[2], arms = new Transform[2];
        Vector3 previous;
        float stride, swing;
        public bool HasDisc { get; set; } = true;
        Transform backDisc;

        public static RunnerVisual Create(Transform parent, string name, Material shell, Material light)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var visual = root.AddComponent<RunnerVisual>();
            visual.Build(shell, light);
            return visual;
        }

        public void SetLight(Material light) { foreach (var part in lights) part.sharedMaterial = light; }

        void Attach(Transform parent, string name, Dictionary<int, Mesh> meshes, Material shell, Material light)
        {
            foreach (var pair in meshes)
            {
                var material = pair.Key == Shell ? shell : pair.Key == Light ? light : GridLook.Assets.Suit;
                var renderer = GridLook.MeshObject(name, parent, pair.Value, material).GetComponent<Renderer>();
                if (pair.Key == Light) lights.Add(renderer);
            }
        }

        static Dictionary<int, Mesh> BuildBody()
        {
            var batch = new GridMesh.Batch();
            var up = Quaternion.Euler(0, 0, 90);
            batch.Add(Suit, GridMesh.Ellipsoid(new Vector3(.31f, .2f, .135f)), new Vector3(0, 1.28f, 0), up);
            batch.Add(Suit, GridMesh.Ellipsoid(new Vector3(.13f, .17f, .12f)), new Vector3(0, .98f, 0), up);
            batch.Add(Suit, GridMesh.Tube(new[] { new Vector3(0, 1.55f, 0), new Vector3(0, 1.64f, .01f) }, .055f, .05f, 10, 1));
            batch.Add(Shell, GridMesh.Ellipsoid(new Vector3(.155f, .125f, .14f)), new Vector3(0, 1.76f, .01f), up);
            var visor = new List<Vector3>();
            for (int i = 0; i <= 16; i++)
            {
                float a = Mathf.Lerp(-100, 100, i / 16f) * Mathf.Deg2Rad;
                visor.Add(new Vector3(Mathf.Sin(a) * .128f, 1.77f, .01f + Mathf.Cos(a) * .143f));
            }
            batch.Add(Light, GridMesh.Tube(visor, .011f, .011f, 6, 1));
            foreach (float side in new[] { -1f, 1f })
            {
                batch.Add(Light, GridMesh.Tube(new[] { new Vector3(side * .19f, 1.5f, .02f), new Vector3(side * .2f, 1.25f, .12f), new Vector3(side * .1f, 1.02f, .12f) }, .008f, .008f, 6, 5));
                batch.Add(Shell, GridMesh.Ellipsoid(new Vector3(.08f, .1f, .09f)), new Vector3(side * .25f, 1.5f, 0), Quaternion.identity);
            }
            batch.Add(Light, GridMesh.Tube(new[] { new Vector3(-.17f, 1.08f, .13f), new Vector3(0, 1.04f, .14f), new Vector3(.17f, 1.08f, .13f) }, .009f, .009f, 6, 4));
            return batch.Build("Runner body");
        }

        // Limbs are built hanging from their joint at the origin so they can swing.
        static Dictionary<int, Mesh> BuildLimb(bool isLeg)
        {
            var batch = new GridMesh.Batch();
            Vector3[] path = isLeg
                ? new[] { new Vector3(0, 0, 0), new Vector3(0, -.44f, .03f), new Vector3(0, -.86f, -.01f) }
                : new[] { new Vector3(0, 0, 0), new Vector3(.03f, -.29f, -.02f), new Vector3(.01f, -.56f, .04f) };
            batch.Add(Suit, GridMesh.Tube(path, isLeg ? .085f : .058f, isLeg ? .062f : .045f, 10, 5));
            if (isLeg) batch.Add(Shell, GridMesh.Tube(new[] { new Vector3(0, -.86f, -.03f), new Vector3(0, -.9f, .12f) }, .06f, .05f, 10, 1));
            var line = new Vector3[path.Length];
            for (int i = 0; i < path.Length; i++) line[i] = path[i] + new Vector3(0, 0, isLeg ? .078f : .052f);
            batch.Add(Light, GridMesh.Tube(line, .008f, .007f, 6, 5));
            return batch.Build(isLeg ? "Runner leg" : "Runner arm");
        }

        void Build(Material shell, Material light)
        {
            if (body == null) { body = BuildBody(); leg = BuildLimb(true); arm = BuildLimb(false); }
            Attach(transform, "Runner body", body, shell, light);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1 : 1;
                legs[i] = new GameObject("Runner leg").transform;
                legs[i].SetParent(transform, false);
                legs[i].localPosition = new Vector3(side * .1f, .92f, 0);
                Attach(legs[i], "Runner leg", leg, shell, light);
                arms[i] = new GameObject("Runner arm").transform;
                arms[i].SetParent(transform, false);
                arms[i].localPosition = new Vector3(side * .27f, 1.47f, 0);
                arms[i].localRotation = Quaternion.Euler(0, 0, side * 8);
                Attach(arms[i], "Runner arm", arm, shell, light);
            }
            backDisc = new GameObject("Back disc").transform;
            backDisc.SetParent(transform, false);
            backDisc.localPosition = new Vector3(0, 1.32f, -.15f);
            backDisc.localRotation = Quaternion.Euler(0, 90, 0);
            var disc = GridMesh.Annulus(.075f, .1f, 32, true);
            lights.Add(GridLook.MeshObject("Back disc ring", backDisc, disc, light).GetComponent<Renderer>());
            // Ground ring (as in the browser's foot marker) keeps the small figure readable from above.
            var ring = GridLook.MeshObject("Foot ring", transform, GridMesh.Annulus(.42f, .5f, 40, true), light).transform;
            ring.localPosition = new Vector3(0, .02f, 0);
            ring.localRotation = Quaternion.Euler(0, 0, 90);
            lights.Add(ring.GetComponent<Renderer>());
            previous = transform.position;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            float speed = Vector3.Distance(transform.position, previous) / dt;
            previous = transform.position;
            stride += speed * dt * 2.6f;
            swing = Mathf.Lerp(swing, Mathf.Clamp01(speed / 4f), 1 - Mathf.Exp(-10 * dt));
            float angle = Mathf.Sin(stride) * 32 * swing;
            legs[0].localRotation = Quaternion.Euler(angle, 0, 0);
            legs[1].localRotation = Quaternion.Euler(-angle, 0, 0);
            arms[0].localRotation = Quaternion.Euler(-angle * .8f, 0, -8);
            arms[1].localRotation = Quaternion.Euler(angle * .8f, 0, 8);
            backDisc.gameObject.SetActive(HasDisc);
        }
    }
}
