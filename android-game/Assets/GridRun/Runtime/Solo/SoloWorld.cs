using System;
using System.Collections.Generic;
using GridRun.Sim;
using UnityEngine;

namespace GridRun.Solo
{
    // Renders a SceneView: map set, riders, trails, weapons, pickups, operation set pieces, effects.
    // Simulation coordinates map to Unity as (x, -z) so relative left/right turns read correctly.
    public sealed class SoloWorld
    {
        public static Vector3 W(double x, double z, float y = 0) => new Vector3((float)x, y, (float)-z);
        public static float Yaw(int dir) => dir * 90f;

        sealed class RiderVisuals
        {
            public Transform Root;
            public CycleVisual Cycle;
            public RunnerVisual Runner;
            public Material Light;
            public string Form = "cycle";
            public double LastFormChange = double.NaN;
        }

        sealed class Particle { public Transform T; public Vector3 V; public float Age, Life; public bool Ring; public float Grow; }

        readonly Transform root;
        readonly Material floorMaterial, bodyMaterial, glowBase;
        Transform mapSet, dynamicSet;
        MapDef map;
        string opId;
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        readonly Dictionary<string, RiderVisuals> riders = new Dictionary<string, RiderVisuals>();
        readonly Dictionary<int, Transform> trails = new Dictionary<int, Transform>();
        readonly Stack<Transform> trailPool = new Stack<Transform>();
        readonly List<Transform> bullets = new List<Transform>(), rockets = new List<Transform>(), discs = new List<Transform>();
        readonly Dictionary<int, Transform> pickups = new Dictionary<int, Transform>();
        readonly List<Particle> particles = new List<Particle>();
        readonly Stack<Transform> particlePool = new Stack<Transform>();
        Transform[] walls, wallEdges, nextRing;
        readonly List<GateParts> gates = new List<GateParts>();
        Transform scanner, laneMark, beam;
        readonly HashSet<int> seen = new HashSet<int>();
        static Mesh cube, discMesh, ringMesh, sparkMesh;
        static readonly Dictionary<string, Mesh> pickupMeshes = new Dictionary<string, Mesh>();

        sealed class GateParts { public Renderer[] Frame; public Transform Slab; }

        public Transform Player { get; private set; }
        public bool PlayerOnFoot { get; private set; }

        public SoloWorld(Transform root, Material floor, Material body, Material glow)
        {
            this.root = root;
            floorMaterial = floor;
            bodyMaterial = body;
            glowBase = glow;
            if (cube == null)
            {
                cube = GridMesh.Box(Vector3.zero, Vector3.one);
                discMesh = GridMesh.Lathe(GridMesh.RoundedProfile(.05f, .16f, .3f, .02f, 2), 32, true);
                ringMesh = GridMesh.Annulus(.88f, 1f, 64, true);
                sparkMesh = GridMesh.Box(Vector3.zero, Vector3.one * .14f);
            }
        }

        // ---- Materials -----------------------------------------------------------------------

        static string Key(string kind, float[] c, float k) => kind + c[0].ToString("0.00") + c[1].ToString("0.00") + c[2].ToString("0.00") + k;

        public Material Glow(float[] color, float intensity = 2.2f)
        {
            string key = Key("glow", color, intensity);
            if (materials.TryGetValue(key, out var m)) return m;
            m = new Material(glowBase) { name = "Glow " + key };
            m.SetColor("_BaseColor", new Color(color[0] * intensity, color[1] * intensity, color[2] * intensity));
            return materials[key] = m;
        }

        Material Wall(float[] color, string style, float fill = .34f)
        {
            string key = Key("wall" + style + fill, color, 1);
            if (materials.TryGetValue(key, out var m)) return m;
            m = new Material(GridLook.Assets.TrailCyan) { name = "Trail " + key };
            m.SetColor("_Color", new Color(color[0] * 2.1f, color[1] * 2.1f, color[2] * 2.1f));
            m.SetFloat("_FillAlpha", fill);
            m.SetFloat("_StripeA", style == "hunter" ? .69f : style == "survivor" ? .45f : style == "trapper" ? .33f : 0);
            m.SetFloat("_StripeB", style == "trapper" ? .74f : 0);
            return materials[key] = m;
        }

        GameObject Mesh(string name, Transform parent, Mesh mesh, Material material) => GridLook.MeshObject(name, parent, mesh, material);

        Transform Box(string name, Transform parent, Material material)
        {
            var t = Mesh(name, parent, cube, material).transform;
            return t;
        }

        static void Place(Transform t, Vector3 center, Vector3 size) { t.localPosition = center; t.localScale = size; }

        // ---- Map ------------------------------------------------------------------------------

        public void BuildMap(string mapId, string operationId)
        {
            if (mapSet != null) UnityEngine.Object.Destroy(mapSet.gameObject);
            map = SimMaps.Get(mapId);
            opId = operationId;
            mapSet = new GameObject("Map set").transform;
            mapSet.SetParent(root, false);
            float limit = (float)map.Limit;
            var rail = map.Rail;
            var floor = GridLook.Floor(mapSet, Vector3.zero, Vector2.one * (limit * 2 + 10), 4, floorMaterial);
            var block = new MaterialPropertyBlock();
            var renderer = floor.GetComponent<MeshRenderer>();
            renderer.GetPropertyBlock(block);
            block.SetColor("_LineColor", new Color(rail[0] * .7f, rail[1] * .7f, rail[2] * .7f));
            block.SetColor("_BaseColor", new Color(map.Floor[0] * .55f, map.Floor[1] * .55f, map.Floor[2] * .55f));
            renderer.SetPropertyBlock(block);
            var accent = Glow(rail, 2f);
            var soft = Glow(rail, .75f);
            GridLook.Stadium(mapSet, limit + 2, accent, soft);
            GridLook.Skyline(mapSet, 125, 185, 70, map.Id == "solar" ? 13 : map.Id == "vault" ? 19 : 7, accent, soft);
            foreach (var o in map.Obstacles) Pylon(o, rail);
            walls = new Transform[4]; wallEdges = new Transform[4]; nextRing = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                walls[i] = Box("Boundary wall", mapSet, Wall(rail, "boundary", .1f));
                wallEdges[i] = Box("Boundary edge", mapSet, Glow(rail, 2.4f));
                nextRing[i] = Box("Collapse warning", mapSet, Glow(new[] { 1f, .76f, .23f }, 2.6f));
                nextRing[i].gameObject.SetActive(false);
            }
            gates.Clear();
            scanner = laneMark = beam = null;
            if (operationId != null)
            {
                var mission = SimOperations.Get(operationId);
                if (mission.Kind == "checkpoints") foreach (var gate in mission.Gates) gates.Add(BuildGate(gate));
                if (mission.Kind == "scanner") BuildScanner();
            }
            SetLimit(map.Limit, double.NaN);
        }

        void Pylon(Obstacle o, float[] rail)
        {
            float r = (float)o.Radius;
            float height = r > 3 ? 4.6f : 3.2f;
            var t = new GameObject(o.Id).transform;
            t.SetParent(mapSet, false);
            t.localPosition = W(o.X, o.Z);
            var up = Quaternion.Euler(0, 0, 90);
            var body = GridMesh.Lathe(GridMesh.RoundedProfile(height * .5f, .001f, r, .25f, 3), 48, true);
            var structure = Mesh("Pylon body", t, body, GridLook.Assets.Structure).transform;
            structure.localPosition = new Vector3(0, height * .5f, 0);
            structure.localRotation = up;
            foreach (float h in new[] { .35f, height * .55f, height - .35f })
            {
                var ring = Mesh("Pylon light", t, GridMesh.Lathe(GridMesh.RoundedProfile(.05f, r - .05f, r + .035f, .02f, 2), 48, true), Glow(rail, 2.6f)).transform;
                ring.localPosition = new Vector3(0, h, 0);
                ring.localRotation = up;
            }
            var halo = Mesh("Pylon floor ring", t, GridMesh.Annulus(r + .2f, r + .32f, 48, true), Glow(rail, 1.6f)).transform;
            halo.localPosition = new Vector3(0, .015f, 0);
            halo.localRotation = up;
        }

        GateParts BuildGate(Gate gate)
        {
            var parts = new GateParts();
            float cx = (float)(gate.MinX + gate.MaxX) / 2, cz = (float)(gate.MinZ + gate.MaxZ) / 2;
            float sx = (float)(gate.MaxX - gate.MinX), sz = (float)(gate.MaxZ - gate.MinZ);
            var list = new List<Renderer>();
            var material = Glow(new[] { 1f, .41f, .18f });
            foreach (double x in new[] { gate.MinX, gate.MaxX })
            {
                var post = Box("Gate post", mapSet, material);
                Place(post, W(x, cz, 1.2f), new Vector3(.16f, 2.4f, sz + .12f));
                list.Add(post.GetComponent<Renderer>());
            }
            foreach (double z in new[] { gate.MinZ, gate.MaxZ })
            {
                var strip = Box("Gate floor line", mapSet, material);
                Place(strip, W(cx, z, .06f), new Vector3(sx + .12f, .12f, .16f));
                list.Add(strip.GetComponent<Renderer>());
            }
            var top = Box("Gate beam", mapSet, material);
            Place(top, W(cx, cz, 2.42f), new Vector3(sx + .12f, .1f, sz + .12f));
            list.Add(top.GetComponent<Renderer>());
            parts.Frame = list.ToArray();
            parts.Slab = Box("Gate shutter", mapSet, Wall(new[] { 1f, .41f, .18f }, "gate", .42f));
            Place(parts.Slab, W(cx, cz, .55f), new Vector3(sx, 1.1f, sz));
            return parts;
        }

        void BuildScanner()
        {
            scanner = new GameObject("Search craft").transform;
            scanner.SetParent(mapSet, false);
            var hull = GridMesh.Plate(new[] { new Vector2(-2.2f, 0), new Vector2(-.9f, 1.8f), new Vector2(.9f, 1.8f), new Vector2(2.2f, 0), new Vector2(.9f, -1.8f), new Vector2(-.9f, -1.8f) }, .5f);
            Mesh("Craft hull", scanner, hull, GridLook.Assets.Structure);
            var core = Mesh("Craft core", scanner, GridMesh.Ellipsoid(new Vector3(.8f, .45f, .8f)), Glow(new[] { .12f, .47f, .55f }, 2.4f)).transform;
            core.localPosition = new Vector3(0, -.15f, 0);
            foreach (float side in new[] { -1f, 1f })
            {
                var wing = Mesh("Craft wing", scanner, GridMesh.Plate(new[] { new Vector2(0, -.5f), new Vector2(2.4f, -.3f), new Vector2(2.4f, .3f), new Vector2(0, .5f) }, .16f), GridLook.Assets.Structure).transform;
                wing.localPosition = new Vector3(side * 1.9f, .1f, 0);
                wing.localScale = new Vector3(side, 1, 1);
                var strut = Box("Craft emitter", scanner, Glow(new[] { .15f, .59f, .64f }, 2.6f));
                Place(strut, new Vector3(side * 3.1f, -1.1f, 0), new Vector3(.16f, 2.4f, .16f));
            }
            laneMark = Box("Scanner lane mark", mapSet, Glow(new[] { 1f, .74f, .2f }, 2.2f));
            beam = Box("Scanner beam", mapSet, Wall(new[] { 1f, .2f, .12f }, "beam", .5f));
        }

        void SetLimit(double limit, double next)
        {
            bool collapsed = opId == "collapse" && limit < map.Limit;
            float[] color = collapsed ? new[] { 1f, .26f, .16f } : map.Rail;
            float height = collapsed ? 1.2f : 2f;
            float l = (float)limit;
            for (int side = 0; side < 4; side++)
            {
                bool alongZ = side < 2;
                float sign = side % 2 == 0 ? -1 : 1;
                var center = alongZ ? new Vector3(sign * (l + .15f), 0, 0) : new Vector3(0, 0, sign * (l + .15f));
                var size = alongZ ? new Vector3(.3f, height, l * 2 + .6f) : new Vector3(l * 2 + .6f, height, .3f);
                Place(walls[side], center + Vector3.up * height * .5f, size);
                walls[side].GetComponent<Renderer>().sharedMaterial = Wall(color, "boundary", .12f);
                Place(wallEdges[side], center + Vector3.up * (height + .03f), new Vector3(alongZ ? .34f : size.x, .06f, alongZ ? size.z : .34f));
                wallEdges[side].GetComponent<Renderer>().sharedMaterial = Glow(color, 2.4f);
                bool warn = !double.IsNaN(next);
                nextRing[side].gameObject.SetActive(warn);
                if (!warn) continue;
                float n = (float)next;
                var nc = alongZ ? new Vector3(sign * n, .06f, 0) : new Vector3(0, .06f, sign * n);
                Place(nextRing[side], nc, alongZ ? new Vector3(.18f, .12f, n * 2) : new Vector3(n * 2, .12f, .18f));
            }
        }

        // ---- Per frame -------------------------------------------------------------------------

        public void Clear()
        {
            foreach (var r in riders.Values) UnityEngine.Object.Destroy(r.Root.gameObject);
            riders.Clear();
            foreach (var t in trails.Values) { t.gameObject.SetActive(false); trailPool.Push(t); }
            trails.Clear();
            foreach (var t in pickups.Values) UnityEngine.Object.Destroy(t.gameObject);
            pickups.Clear();
            foreach (var p in particles) { p.T.gameObject.SetActive(false); particlePool.Push(p.T); }
            particles.Clear();
            Player = null;
        }

        public void Render(SceneView view, float dt)
        {
            if (map == null || map.Id != view.Map) BuildMap(view.Map, view.OpId);
            if (dynamicSet == null)
            {
                dynamicSet = new GameObject("Round objects").transform;
                dynamicSet.SetParent(root, false);
            }
            SetLimit(view.Limit, view.NextLimit);
            RenderRiders(view);
            RenderTrails(view);
            RenderBullets(view);
            RenderDiscs(view);
            RenderPickups(view);
            RenderOperation(view);
            UpdateParticles(dt);
        }

        void RenderRiders(SceneView view)
        {
            foreach (var r in view.Riders)
            {
                if (!riders.TryGetValue(r.Id, out var v))
                {
                    v = new RiderVisuals { Root = new GameObject(r.IsPlayer ? "Player cycle" : "Rider " + r.Id).transform };
                    v.Root.SetParent(dynamicSet, false);
                    v.Light = Glow(r.Color);
                    v.Cycle = CycleVisual.Create(v.Root, "Light cycle", bodyMaterial, v.Light);
                    v.Runner = RunnerVisual.Create(v.Root, "On-foot rider", GridLook.Assets.Shell, v.Light);
                    v.Runner.gameObject.SetActive(false);
                    riders[r.Id] = v;
                }
                if (r.IsPlayer) { Player = v.Root; PlayerOnFoot = r.Form == "foot"; }
                v.Root.gameObject.SetActive(r.Alive);
                if (!r.Alive) continue;
                float lift = 0;
                if (r.FlightKind != null)
                {
                    double span = Math.Max(.01, r.FlightUntil - r.FlightStartedAt);
                    float progress = Mathf.Clamp01((float)((view.Elapsed - r.FlightStartedAt) / span));
                    lift = Mathf.Sin(progress * Mathf.PI) * (r.FlightKind == "jet" ? 2.4f : 1.7f);
                }
                v.Root.localPosition = W(r.X, r.Z, lift);
                v.Root.localRotation = Quaternion.Euler(0, Yaw(r.Dir), 0);
                bool foot = r.Form == "foot";
                v.Cycle.gameObject.SetActive(!foot);
                v.Runner.gameObject.SetActive(foot);
                v.Runner.HasDisc = !r.HasDiscOut;
                if (!foot) v.Cycle.SetFlight(r.FlightKind == "jet", r.FlightKind == "sail");
                if (r.FormChangedAt > -10 && r.FormChangedAt != v.LastFormChange)
                {
                    if (!double.IsNaN(v.LastFormChange)) Ring(r.X, r.Z, r.Color, 1.6f);
                    v.LastFormChange = r.FormChangedAt;
                }
            }
        }

        void RenderTrails(SceneView view)
        {
            seen.Clear();
            bool decay = view.Rule == "decay";
            foreach (var s in view.Segments)
            {
                double dx = s.X2 - s.X1, dz = s.Z2 - s.Z1;
                double length = JsMath.Hypot(dx, dz);
                if (length < .06) continue;
                seen.Add(s.Id);
                if (!trails.TryGetValue(s.Id, out var t))
                {
                    t = trailPool.Count > 0 ? trailPool.Pop() : Box("Light trail", null, GridLook.Assets.TrailCyan);
                    t.SetParent(dynamicSet, false);
                    t.gameObject.SetActive(true);
                    trails[s.Id] = t;
                }
                var owner = view.Riders.Find(r => r.Id == s.OwnerId);
                float[] color = owner.Color ?? GridRules.Colors.Player;
                string style = owner.IsPlayer ? "player" : owner.Style;
                bool warning = decay && !s.Active && s.ClosedAt + view.TrailLifetime - view.Elapsed <= 2;
                var material = warning
                    ? (Mathf.FloorToInt((float)view.Elapsed * 4) % 2 == 0 ? Wall(new[] { 1f, .91f, .48f }, style, .1f) : Wall(new[] { 1f, .91f, .48f }, style))
                    : Wall(color, style);
                var renderer = t.GetComponent<Renderer>();
                if (renderer.sharedMaterial != material) renderer.sharedMaterial = material;
                float sx = Math.Abs(dx) > .01 ? (float)length : .16f, sz = Math.Abs(dz) > .01 ? (float)length : .16f;
                Place(t, W((s.X1 + s.X2) / 2, (s.Z1 + s.Z2) / 2, .61f), new Vector3(sx, 1.22f, sz));
            }
            var stale = new List<int>();
            foreach (var pair in trails) if (!seen.Contains(pair.Key)) stale.Add(pair.Key);
            foreach (int id in stale)
            {
                var t = trails[id];
                t.gameObject.SetActive(false);
                trailPool.Push(t);
                trails.Remove(id);
            }
        }

        void RenderBullets(SceneView view)
        {
            int b = 0, r = 0;
            foreach (var bullet in view.Bullets)
            {
                var list = bullet.Rocket ? rockets : bullets;
                int index = bullet.Rocket ? r++ : b++;
                if (list.Count <= index)
                {
                    var t = new GameObject(bullet.Rocket ? "Rocket" : "Bullet").transform;
                    t.SetParent(dynamicSet, false);
                    if (bullet.Rocket)
                    {
                        Mesh("Rocket body", t, GridMesh.Ellipsoid(new Vector3(.36f, .13f, .13f)), Glow(new[] { 1f, .48f, .13f }, 2.8f)).transform.localRotation = Quaternion.Euler(0, -90, 0);
                        var plume = Mesh("Rocket plume", t, GridMesh.Ellipsoid(new Vector3(.5f, .08f, .08f)), Glow(new[] { 1f, .82f, .35f }, 3.2f)).transform;
                        plume.localRotation = Quaternion.Euler(0, -90, 0);
                        plume.localPosition = new Vector3(0, 0, -.6f);
                    }
                    else Mesh("Bullet core", t, GridMesh.Ellipsoid(new Vector3(.3f, .07f, .07f)), Glow(new[] { .56f, 1f, .96f }, 3f)).transform.localRotation = Quaternion.Euler(0, -90, 0);
                    list.Add(t);
                }
                var item = list[index];
                item.gameObject.SetActive(true);
                item.localPosition = W(bullet.X, bullet.Z, .54f);
                item.localRotation = Quaternion.LookRotation(new Vector3(bullet.Vx, 0, -bullet.Vz));
                if (!bullet.Rocket)
                {
                    var renderer = item.GetComponentInChildren<Renderer>();
                    var material = Glow(bullet.FromPlayer ? new[] { .56f, 1f, .96f } : new[] { 1f, .72f, .36f }, 3f);
                    if (renderer.sharedMaterial != material) renderer.sharedMaterial = material;
                }
            }
            for (int i = b; i < bullets.Count; i++) bullets[i].gameObject.SetActive(false);
            for (int i = r; i < rockets.Count; i++) rockets[i].gameObject.SetActive(false);
        }

        void RenderDiscs(SceneView view)
        {
            int index = 0;
            foreach (var disc in view.Discs)
            {
                if (discs.Count <= index)
                {
                    var t = new GameObject("Thrown disc").transform;
                    t.SetParent(dynamicSet, false);
                    Mesh("Disc ring", t, discMesh, Glow(new[] { .48f, .96f, 1f }, 3f)).transform.localRotation = Quaternion.Euler(0, 0, 90);
                    discs.Add(t);
                }
                var item = discs[index++];
                item.gameObject.SetActive(true);
                item.localPosition = W(disc.X, disc.Z, .75f);
                item.Rotate(0, Time.deltaTime * 900, 0, Space.Self);
                var renderer = item.GetComponentInChildren<Renderer>();
                var material = Glow(disc.FromPlayer ? new[] { .48f, .96f, 1f } : new[] { 1f, .62f, .28f }, 3f);
                if (renderer.sharedMaterial != material) renderer.sharedMaterial = material;
            }
            for (int i = index; i < discs.Count; i++) discs[i].gameObject.SetActive(false);
        }

        static float[] PickupColor(string type) =>
            type == "ammo" ? new[] { .25f, .85f, 1f } : type == "nitro" ? new[] { .25f, 1f, .4f } : new[] { 1f, .54f, .18f };

        static Mesh PickupMesh(string type)
        {
            if (pickupMeshes.TryGetValue(type, out var mesh)) return mesh;
            var batch = new GridMesh.Batch();
            var up = Quaternion.Euler(0, 0, 90);
            if (type == "ammo")
                foreach (float x in new[] { -.43f, 0f, .43f })
                {
                    batch.Add(0, GridMesh.Lathe(GridMesh.RoundedProfile(.42f, .001f, .15f, .04f, 2), 16, true), new Vector3(x, .75f, 0), up);
                    batch.Add(0, GridMesh.Ellipsoid(new Vector3(.2f, .15f, .15f), 14, 8), new Vector3(x, 1.2f, 0), up);
                }
            else if (type == "nitro")
            {
                // Lightning bolt from two convex pieces, stood upright (plate XZ -> XY) on crossed axes.
                Vector2[] upper = { new Vector2(.05f, 1.75f), new Vector2(.42f, 1.75f), new Vector2(.12f, 1.12f), new Vector2(-.28f, 1.12f) };
                Vector2[] lower = { new Vector2(-.1f, 1.18f), new Vector2(.36f, 1.18f), new Vector2(-.22f, .2f) };
                foreach (float yaw in new[] { 0f, 90f })
                {
                    var rotate = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(-90, 0, 0);
                    batch.Add(0, GridMesh.Plate(upper, .1f), Vector3.zero, rotate);
                    batch.Add(0, GridMesh.Plate(lower, .1f), Vector3.zero, rotate);
                }
            }
            else
            {
                batch.Add(0, GridMesh.Lathe(GridMesh.RoundedProfile(.5f, .001f, .2f, .05f, 2), 20, true), new Vector3(0, 1f, 0), up);
                batch.Add(0, GridMesh.Ellipsoid(new Vector3(.42f, .2f, .2f), 18, 10), new Vector3(0, 1.62f, 0), up);
                for (int i = 0; i < 4; i++)
                {
                    var fin = GridMesh.Plate(new[] { new Vector2(.16f, 0), new Vector2(.5f, 0), new Vector2(.5f, .1f), new Vector2(.16f, .6f) }, .05f);
                    batch.Add(0, fin, new Vector3(0, .45f, 0), Quaternion.Euler(0, i * 90, 0) * Quaternion.Euler(-90, 0, 0));
                }
            }
            batch.Add(0, GridMesh.Box(new Vector3(0, .035f, 0), new Vector3(1.7f, .05f, .1f)));
            batch.Add(0, GridMesh.Box(new Vector3(0, .035f, 0), new Vector3(.1f, .05f, 1.7f)));
            var built = batch.Build("Pickup " + type);
            return pickupMeshes[type] = built[0];
        }

        void RenderPickups(SceneView view)
        {
            seen.Clear();
            foreach (var p in view.Pickups)
            {
                seen.Add(p.Id);
                if (!pickups.TryGetValue(p.Id, out var t))
                {
                    t = new GameObject("Pickup " + p.Type).transform;
                    t.SetParent(dynamicSet, false);
                    Mesh("Pickup icon", t, PickupMesh(p.Type), Glow(PickupColor(p.Type), 2.4f));
                    pickups[p.Id] = t;
                }
                t.localPosition = W(p.X, p.Z, Mathf.Sin((float)view.Elapsed * 2.4f + p.Id) * .08f);
                t.localRotation = Quaternion.Euler(0, (float)view.Elapsed * 60 + p.Id * 37, 0);
            }
            var stale = new List<int>();
            foreach (var pair in pickups) if (!seen.Contains(pair.Key)) stale.Add(pair.Key);
            foreach (int id in stale) { UnityEngine.Object.Destroy(pickups[id].gameObject); pickups.Remove(id); }
        }

        void RenderOperation(SceneView view)
        {
            if (view.OpId == null) return;
            var mission = SimOperations.Get(view.OpId);
            if (mission.Kind == "checkpoints")
                for (int i = 0; i < gates.Count && i < mission.Gates.Length; i++)
                {
                    var gate = mission.Gates[i];
                    bool open = view.Elapsed >= gate.OpenFrom && view.Elapsed < gate.OpenUntil;
                    float[] color = i < view.OpProgress ? new[] { .08f, .22f, .27f } : open ? new[] { .12f, .95f, .66f } : new[] { 1f, .41f, .18f };
                    var material = Glow(color, i < view.OpProgress ? 1f : 2.4f);
                    foreach (var r in gates[i].Frame) if (r.sharedMaterial != material) r.sharedMaterial = material;
                    gates[i].Slab.gameObject.SetActive(!open);
                }
            else if (mission.Kind == "scanner" && scanner != null)
            {
                double craftX = view.HasBeam ? view.BeamX : -19 + (view.Elapsed * 5) % 38;
                scanner.localPosition = Vector3.Lerp(scanner.localPosition, W(craftX, 0, 4.8f), .25f);
                laneMark.gameObject.SetActive(view.HasBeam);
                beam.gameObject.SetActive(view.HasBeam && view.BeamActive);
                if (view.HasBeam)
                {
                    var color = view.BeamActive ? new[] { 1f, .2f, .12f } : new[] { 1f, .74f, .2f };
                    laneMark.GetComponent<Renderer>().sharedMaterial = Glow(color, 2.2f);
                    Place(laneMark, W(view.BeamX, 0, .035f), new Vector3(1.3f, .07f, 48));
                    Place(beam, W(view.BeamX, 0, .65f), new Vector3(1.3f, 1.3f, 48));
                }
            }
        }

        // ---- Effects --------------------------------------------------------------------------

        Transform Spark(Material material, Mesh mesh)
        {
            Transform t;
            if (particlePool.Count > 0) t = particlePool.Pop();
            else t = Mesh("Effect", dynamicSet, mesh, material).transform;
            t.SetParent(dynamicSet, false);
            t.GetComponent<MeshFilter>().sharedMesh = mesh;
            t.GetComponent<Renderer>().sharedMaterial = material;
            t.gameObject.SetActive(true);
            t.localScale = Vector3.one;
            t.localRotation = Quaternion.identity;
            return t;
        }

        public void Derez(double x, double z, float[] color, bool reduced)
        {
            if (dynamicSet == null) return;
            var material = Glow(color, 3f);
            int count = reduced ? 6 : 16;
            for (int i = 0; i < count; i++)
            {
                float angle = UnityEngine.Random.value * Mathf.PI * 2, speed = 1.5f + UnityEngine.Random.value * 3;
                var t = Spark(material, sparkMesh);
                t.localPosition = W(x, z, .55f);
                particles.Add(new Particle
                {
                    T = t, V = new Vector3(Mathf.Cos(angle) * speed, 1.2f + UnityEngine.Random.value * 2.8f, Mathf.Sin(angle) * speed),
                    Life = .35f + UnityEngine.Random.value * .35f,
                });
            }
        }

        public void Impact(double x, double z)
        {
            if (dynamicSet == null) return;
            var material = Glow(new[] { 1f, .85f, .45f }, 3f);
            for (int i = 0; i < 9; i++)
            {
                float angle = i * Mathf.PI * 2 / 9;
                var t = Spark(material, sparkMesh);
                t.localPosition = W(x, z, .48f);
                t.localScale = Vector3.one * .7f;
                particles.Add(new Particle { T = t, V = new Vector3(Mathf.Cos(angle) * 3.4f, 2.5f + (i % 3) * .45f, Mathf.Sin(angle) * 3.4f), Life = .24f + (i % 3) * .055f });
            }
        }

        public void Ring(double x, double z, float[] color, float radius)
        {
            if (dynamicSet == null) return;
            var t = Spark(Glow(color, 2.8f), ringMesh);
            t.localPosition = W(x, z, .05f);
            t.localRotation = Quaternion.Euler(0, 0, 90);
            t.localScale = Vector3.one * .3f;
            particles.Add(new Particle { T = t, Ring = true, Grow = radius, Life = .5f });
        }

        void UpdateParticles(float dt)
        {
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    p.T.gameObject.SetActive(false);
                    particlePool.Push(p.T);
                    particles.RemoveAt(i);
                    continue;
                }
                if (p.Ring)
                {
                    float s = Mathf.Lerp(.3f, p.Grow, p.Age / p.Life);
                    p.T.localScale = new Vector3(1 - p.Age / p.Life, s, s);
                }
                else
                {
                    p.T.localPosition += p.V * dt;
                    p.V.y -= 7 * dt;
                    p.T.localScale = Vector3.one * (1 - p.Age / p.Life);
                }
            }
        }
    }
}
