using System;
using GridRun.Sim;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GridRun.Solo
{
    // In-round HUD, radar, and touch controls. Control GameObject names are stable for the
    // desktop verification flows (Left, Right, Forward, Fire, Light Jet, Solar Sail, Form, Disc...).
    public sealed class SoloHud
    {
        public GameObject Root { get; }
        public Text Matchline, Status, Intent, Score, Countdown, Toast, Caption, Weapon, ObjectiveTitle, ObjectiveProgress, ObjectiveWarning, Shift;
        readonly GameObject objective, shiftPanel, cycleLeft, cycleRight, footLeft, footRight, flightRow, formButton, fireButton;
        readonly HoldControl forward, fire, up, down, left, right;
        readonly Text autoLabel, pauseLabel, jetLabel, sailLabel, fireLabel, discLabel;
        readonly Button jetButton, sailButton, discButton;
        readonly RawImage radar;
        readonly Texture2D radarTexture;
        readonly Color32[] radarPixels;
        const int RadarSize = 192;
        float toastUntil, captionUntil, radarTimer;

        public bool ForwardHeld => forward.Held;
        public bool FireHeld => fire.Held;
        public Vector2 FootMove => new Vector2((right.Held ? 1 : 0) - (left.Held ? 1 : 0), (up.Held ? 1 : 0) - (down.Held ? 1 : 0));

        public SoloHud(Transform safe, UnityAction<int> turn, UnityAction pause, UnityAction auto, UnityAction jet,
            UnityAction sail, UnityAction form, UnityAction disc)
        {
            var hud = SoloUi.Stretch("Play HUD", safe);
            Root = hud.gameObject;
            SoloUi.Bar("Top contrast", hud, new Vector2(.5f, 1), new Vector2(.5f, 1), Vector2.zero, new Vector2(2400, 200), new Color(.002f, .01f, .02f, .72f));
            Matchline = SoloUi.Label("Matchline", "", hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -16), new Vector2(1020, 46), 32, TextAnchor.MiddleLeft, null, true);
            Status = SoloUi.Label("Status", "", hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -64), new Vector2(1020, 40), 20, TextAnchor.MiddleLeft, UiSkin.Muted);
            Intent = SoloUi.Label("Rival intent", "", hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -104), new Vector2(1020, 38), 22, TextAnchor.MiddleLeft, UiSkin.Accent);
            Score = SoloUi.Label("Score", "", hud, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-460, -16), new Vector2(560, 92), 24, TextAnchor.UpperRight, null, true);

            objective = SoloUi.Rect("Objective", hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -150), new Vector2(880, 150)).gameObject;
            UiSkin.Panel(objective.AddComponent<Image>());
            ObjectiveTitle = SoloUi.Label("Objective title", "", objective.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -12), new Vector2(832, 40), 19, TextAnchor.MiddleLeft, UiSkin.Accent, true);
            ObjectiveProgress = SoloUi.Label("Objective progress", "", objective.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -54), new Vector2(832, 38), 25, TextAnchor.MiddleLeft, null, true);
            ObjectiveWarning = SoloUi.Label("Objective warning", "", objective.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -96), new Vector2(832, 40), 22, TextAnchor.MiddleLeft, new Color(1f, .82f, .45f));

            shiftPanel = SoloUi.Rect("Shift status", hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -150), new Vector2(760, 96)).gameObject;
            UiSkin.Panel(shiftPanel.AddComponent<Image>());
            Shift = SoloUi.Label("Shift detail", "", shiftPanel.transform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(24, 0), new Vector2(712, 84), 23);

            pauseLabel = SoloUi.Caption(SoloUi.Button("Pause", "PAUSE", hud, Vector2.one, Vector2.one, new Vector2(-30, -26), new Vector2(170, 80), pause));
            autoLabel = SoloUi.Caption(SoloUi.Button("Auto Drive", "AUTO ON", hud, Vector2.one, Vector2.one, new Vector2(-214, -26), new Vector2(200, 80), auto, false, 22));
            radar = SoloUi.Rect("Radar", hud, Vector2.one, Vector2.one, new Vector2(-30, -124), new Vector2(236, 236)).gameObject.AddComponent<RawImage>();
            radar.raycastTarget = false;
            radarTexture = new Texture2D(RadarSize, RadarSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Radar" };
            radarPixels = new Color32[RadarSize * RadarSize];
            radar.texture = radarTexture;

            Countdown = SoloUi.Label("Countdown", "", hud, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 120), new Vector2(600, 200), 150, TextAnchor.MiddleCenter, Color.white, true);
            Countdown.gameObject.AddComponent<Outline>().effectColor = new Color(.1f, .8f, 1f, .5f);
            Toast = SoloUi.Label("Toast", "", hud, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -322), new Vector2(1000, 50), 30, TextAnchor.MiddleCenter, Color.white, true);
            Toast.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .8f);
            Caption = SoloUi.Label("Caption", "", hud, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 330), new Vector2(900, 40), 24, TextAnchor.MiddleCenter, UiSkin.Accent, true);
            Weapon = SoloUi.Label("Weapon status", "", hud, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-36, 316), new Vector2(620, 36), 23, TextAnchor.MiddleRight, null, true);

            // Cycle controls.
            cycleLeft = SoloUi.Stretch("Cycle steering", hud).gameObject;
            SoloUi.Hold("Left", "LEFT", cycleLeft.transform, Vector2.zero, Vector2.zero, new Vector2(34, 34), new Vector2(230, 150), () => turn(-1), 30);
            SoloUi.Hold("Right", "RIGHT", cycleLeft.transform, Vector2.zero, Vector2.zero, new Vector2(282, 34), new Vector2(230, 150), () => turn(1), 30);
            cycleRight = SoloUi.Stretch("Cycle drive", hud).gameObject;
            forward = SoloUi.Hold("Forward", "FORWARD", cycleRight.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-34, 34), new Vector2(240, 150));
            fire = SoloUi.Hold("Fire", "FIRE 24", cycleRight.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-292, 34), new Vector2(220, 150));
            fireButton = fire.gameObject;
            fireLabel = fire.GetComponentInChildren<Text>();
            flightRow = SoloUi.Stretch("Flight row", hud).gameObject;
            jetButton = SoloUi.Button("Light Jet", "JET", flightRow.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-34, 200), new Vector2(170, 96), jet, false, 22);
            sailButton = SoloUi.Button("Solar Sail", "SAIL", flightRow.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-218, 200), new Vector2(170, 96), sail, false, 22);
            jetLabel = SoloUi.Caption(jetButton); sailLabel = SoloUi.Caption(sailButton);
            formButton = SoloUi.Button("Form", "FORM", hud, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-402, 200), new Vector2(170, 96), form, false, 22).gameObject;

            // On-foot controls (Grid Shift): arena-direction pad and disc.
            footLeft = SoloUi.Stretch("Foot pad", hud).gameObject;
            up = SoloUi.Hold("Move Up", "UP", footLeft.transform, Vector2.zero, Vector2.zero, new Vector2(182, 256), new Vector2(140, 104), null, 22);
            down = SoloUi.Hold("Move Down", "DOWN", footLeft.transform, Vector2.zero, Vector2.zero, new Vector2(182, 34), new Vector2(140, 104), null, 22);
            left = SoloUi.Hold("Move Left", "LEFT", footLeft.transform, Vector2.zero, Vector2.zero, new Vector2(34, 145), new Vector2(140, 104), null, 22);
            right = SoloUi.Hold("Move Right", "RIGHT", footLeft.transform, Vector2.zero, Vector2.zero, new Vector2(330, 145), new Vector2(140, 104), null, 22);
            footRight = SoloUi.Stretch("Foot actions", hud).gameObject;
            discButton = SoloUi.Button("Disc", "DISC", footRight.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-34, 34), new Vector2(240, 150), disc, true, 30);
            discLabel = SoloUi.Caption(discButton);
        }

        public void ShowToast(string message, float seconds = 1.6f)
        {
            Toast.text = message;
            toastUntil = Time.unscaledTime + seconds;
        }

        public void ShowCaption(string message)
        {
            Caption.text = message;
            captionUntil = Time.unscaledTime + 1.2f;
        }

        public void ClearMessages()
        {
            Toast.text = ""; Caption.text = ""; Countdown.text = ""; Intent.text = "";
            toastUntil = captionUntil = 0;
        }

        public void SetPaused(bool paused) => pauseLabel.text = paused ? "RESUME" : "PAUSE";
        public void SetAuto(bool auto) => autoLabel.text = auto ? "AUTO ON" : "AUTO OFF";

        public void ClearHolds()
        {
            foreach (var hold in new[] { forward, fire, up, down, left, right }) hold.Clear();
        }

        // Shows the control set for the current mode/form and refreshes labels and cooldowns.
        public void Sync(Round round, string mode, bool running)
        {
            if (Time.unscaledTime > toastUntil) Toast.text = "";
            if (Time.unscaledTime > captionUntil) Caption.text = "";
            var player = round?.Player;
            bool shift = mode == "shift", lesson = mode == "lesson";
            bool foot = shift && player != null && player.Form == "foot";
            cycleLeft.SetActive(!foot);
            cycleRight.SetActive(!foot);
            footLeft.SetActive(foot);
            footRight.SetActive(foot);
            fireButton.SetActive(!lesson);
            flightRow.SetActive(!lesson && !foot);
            formButton.SetActive(shift);
            if (player == null) return;
            fireLabel.text = player.RocketReady ? "ROCKET" : "FIRE " + player.Ammo;
            foreach (var (kind, button, label) in new[] { ("jet", jetButton, jetLabel), ("sail", sailButton, sailLabel) })
            {
                double readyAt = kind == "jet" ? player.JetReadyAt : player.SailReadyAt;
                double remaining = Math.Max(0, readyAt - round.Elapsed);
                bool active = player.FlightKind == kind && round.Elapsed < player.FlightUntil;
                label.text = kind.ToUpperInvariant() + (active ? " ACTIVE" : remaining > 0 ? " " + remaining.ToString("0.0") + "S" : "");
                button.interactable = running && player.Alive && player.FlightKind == null && remaining <= 0;
            }
            if (shift && round.Shift != null)
            {
                bool discOut = round.Shift.Discs.Exists(d => d.OwnerId == player.Id);
                double cooldown = Math.Max(0, player.DiscReadyAt - round.Elapsed);
                discLabel.text = discOut ? "DISC OUT" : cooldown > 0 ? "DISC " + cooldown.ToString("0.0") + "S" : "DISC";
                discButton.interactable = running && !discOut && cooldown <= 0;
            }
            Weapon.text = lesson ? "" : (foot ? "ON FOOT" : player.RocketReady ? "ROCKET 1" : "GUN " + player.Ammo + "/48") +
                (round.Elapsed < player.NitroUntil ? "  /  NITRO " + Math.Ceiling(player.NitroUntil - round.Elapsed) + "S" : "");
        }

        public void SetObjective(bool visible, string title = "", string progress = "", string warning = "")
        {
            objective.SetActive(visible);
            if (!visible) return;
            ObjectiveTitle.text = title; ObjectiveProgress.text = progress; ObjectiveWarning.text = warning;
        }

        public void SetShiftPanel(bool visible, string text = "")
        {
            shiftPanel.SetActive(visible);
            Shift.text = text;
        }

        // ---- Radar (north up, like the browser) ------------------------------------------------

        public void DrawRadar(SceneView view, float dt, bool force = false)
        {
            radarTimer -= dt;
            if (!force && radarTimer > 0) return;
            radarTimer = 1 / 15f;
            var map = SimMaps.Get(view.Map);
            double limit = map.Limit;
            float scale = RadarSize / (float)(limit * 2 + 6);
            int Px(double x) => (int)((x + limit + 3) * scale);
            int Py(double z) => (int)((-z + limit + 3) * scale);
            var background = new Color32(7, 18, 29, 235);
            for (int i = 0; i < radarPixels.Length; i++) radarPixels[i] = background;
            var gridLine = new Color32(78, 220, 243, 30);
            for (int i = 0; i <= 8; i++)
            {
                int p = Mathf.Min(RadarSize - 1, i * RadarSize / 8);
                Line(p, 0, p, RadarSize - 1, gridLine, 0);
                Line(0, p, RadarSize - 1, p, gridLine, 0);
            }
            var rail = new Color32((byte)(map.Rail[0] * 255), (byte)(map.Rail[1] * 255), (byte)(map.Rail[2] * 255), 255);
            Rect(Px(-limit), Py(limit), Px(limit), Py(-limit), view.Limit < limit ? new Color32(120, 155, 165, 90) : rail);
            if (view.Limit < limit) Rect(Px(-view.Limit), Py(view.Limit), Px(view.Limit), Py(-view.Limit), new Color32(255, 109, 83, 255));
            foreach (var o in map.Obstacles) Disc(Px(o.X), Py(o.Z), Math.Max(2, (int)(o.Radius * scale)), rail);
            if (view.OpId != null)
            {
                var mission = SimOperations.Get(view.OpId);
                if (mission.Kind == "checkpoints")
                    for (int i = 0; i < mission.Gates.Length; i++)
                    {
                        var g = mission.Gates[i];
                        bool open = view.Elapsed >= g.OpenFrom && view.Elapsed < g.OpenUntil;
                        var c = i < view.OpProgress ? new Color32(83, 122, 131, 255) : open ? new Color32(95, 247, 171, 255) : new Color32(255, 123, 83, 255);
                        Rect(Px(g.MinX), Py(g.MaxZ), Px(g.MaxX), Py(g.MinZ), c);
                    }
                else if (mission.Kind == "scanner" && view.HasBeam)
                    FillRect(Px(view.BeamX - .65), Py(24), Px(view.BeamX + .65), Py(-24), view.BeamActive ? new Color32(255, 76, 55, 200) : new Color32(255, 208, 92, 170));
                else if (mission.Kind == "collapse" && !double.IsNaN(view.NextLimit))
                    Rect(Px(-view.NextLimit), Py(view.NextLimit), Px(view.NextLimit), Py(-view.NextLimit), new Color32(255, 204, 85, 255), 3);
            }
            foreach (var s in view.Segments)
            {
                var owner = view.Riders.Find(r => r.Id == s.OwnerId);
                var c = owner.Color ?? GridRules.Colors.Player;
                int dash = owner.IsPlayer ? 0 : owner.Style == "hunter" ? 6 : owner.Style == "survivor" ? 2 : 4;
                bool warning = view.Rule == "decay" && !s.Active && s.ClosedAt + view.TrailLifetime - view.Elapsed <= 2;
                var color = warning ? new Color32(255, 232, 121, 255) : new Color32((byte)(c[0] * 255), (byte)(c[1] * 255), (byte)(c[2] * 255), 200);
                Line(Px(s.X1), Py(s.Z1), Px(s.X2), Py(s.Z2), color, dash);
            }
            foreach (var p in view.Pickups)
            {
                var c = p.Type == "ammo" ? new Color32(64, 217, 255, 255) : p.Type == "nitro" ? new Color32(64, 255, 102, 255) : new Color32(255, 138, 46, 255);
                FillRect(Px(p.X) - 3, Py(p.Z) - 3, Px(p.X) + 3, Py(p.Z) + 3, c);
            }
            foreach (var b in view.Bullets) FillRect(Px(b.X) - 1, Py(b.Z) - 1, Px(b.X) + 1, Py(b.Z) + 1, b.FromPlayer ? new Color32(183, 255, 247, 255) : new Color32(255, 210, 154, 255));
            foreach (var d in view.Discs) FillRect(Px(d.X) - 2, Py(d.Z) - 2, Px(d.X) + 2, Py(d.Z) + 2, d.FromPlayer ? new Color32(183, 250, 255, 255) : new Color32(255, 188, 123, 255));
            foreach (var r in view.Riders)
            {
                if (!r.Alive) continue;
                var c = r.Color;
                var color = new Color32((byte)(c[0] * 255), (byte)(c[1] * 255), (byte)(c[2] * 255), 255);
                Marker(Px(r.X), Py(r.Z), r.Dir, r.IsPlayer ? 7 : 5, color, r.IsPlayer || r.Style == "hunter" ? 0 : r.Style == "trapper" ? 1 : 2);
            }
            Rect(0, 0, RadarSize - 1, RadarSize - 1, new Color32(51, 140, 179, 255));
            radarTexture.SetPixels32(radarPixels);
            radarTexture.Apply(false);
        }

        void Plot(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= RadarSize || y >= RadarSize) return;
            int i = y * RadarSize + x;
            if (c.a >= 250) { radarPixels[i] = c; return; }
            var d = radarPixels[i];
            float a = c.a / 255f;
            radarPixels[i] = new Color32((byte)(d.r + (c.r - d.r) * a), (byte)(d.g + (c.g - d.g) * a), (byte)(d.b + (c.b - d.b) * a), 255);
        }

        void Line(int x0, int y0, int x1, int y1, Color32 c, int dash)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy, n = 0;
            while (true)
            {
                if (dash == 0 || (n / dash) % 2 == 0) { Plot(x0, y0, c); Plot(x0 + (dx > dy ? 0 : 1), y0 + (dx > dy ? 1 : 0), c); }
                n++;
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        void Rect(int x0, int y0, int x1, int y1, Color32 c, int dash = 0)
        {
            Line(x0, y0, x1, y0, c, dash); Line(x1, y0, x1, y1, c, dash);
            Line(x1, y1, x0, y1, c, dash); Line(x0, y1, x0, y0, c, dash);
        }

        void FillRect(int x0, int y0, int x1, int y1, Color32 c)
        {
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++) Plot(x, y, c);
        }

        void Disc(int cx, int cy, int r, Color32 c)
        {
            for (int y = -r; y <= r; y++) for (int x = -r; x <= r; x++) if (x * x + y * y <= r * r) Plot(cx + x, cy + y, c);
        }

        // Heading triangle (player, Hunter), diamond (Trapper), or square (Survivor).
        void Marker(int cx, int cy, int dir, int size, Color32 c, int shape)
        {
            for (int y = -size; y <= size; y++)
                for (int x = -size; x <= size; x++)
                {
                    // Rotate into the rider's frame: forward is +v on the radar (north up).
                    float fx, fy;
                    switch (dir)
                    {
                        case 0: fx = x; fy = y; break;
                        case 1: fx = -y; fy = x; break;
                        case 2: fx = -x; fy = -y; break;
                        default: fx = y; fy = -x; break;
                    }
                    bool inside = shape == 0 ? fy <= size && fy >= -size * .7f && Math.Abs(fx) <= (size - fy) * .55f
                        : shape == 1 ? Math.Abs(fx) + Math.Abs(fy) <= size : Math.Abs(fx) <= size * .7f && Math.Abs(fy) <= size * .7f;
                    if (!inside) continue;
                    bool edge = shape == 0 ? Math.Abs(fx) > (size - fy) * .55f - 1.2f || fy < -size * .7f + 1.2f : false;
                    Plot(cx + x, cy + y, edge ? new Color32(255, 255, 255, 255) : c);
                }
        }
    }
}
