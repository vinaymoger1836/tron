using System;
using System.Collections.Generic;
using GridRun.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GridRun.Solo
{
    // Offline game flow for every browser mode: Duel, Daily Signal, Arena, Grid Shift, Operations
    // (single or Journey with optional Sentinel), and the Play lesson. Rules come from GridRun.Sim
    // (a parity-checked port of the browser rules); this class owns menus, timing, input, feedback,
    // results, records, replays, and camera framing. Solar Transit keeps its dedicated scene.
    public sealed class SoloGame : MonoBehaviour
    {
        sealed class OperationFlow
        {
            public bool Journey;
            public List<string> Sequence = new List<string>();
            public int Index, Points;
            public uint Seed;
            public string Difficulty = "standard", Status = "brief";
        }

        public static readonly string[] Modes = { "duel", "arena", "daily", "shift", "operations", "lesson" };

        Camera view;
        SoloWorld world;
        SoloHud hud;
        SoloAudio sound;
        Action returnToMenu;
        RectTransform safe;

        // Menus and overlays.
        GameObject selectPanel, setupPanel, briefPanel, resultPanel, pausePanel, replayBar, recordsPanel;
        Text setupTitle, setupGuide, setupInfo, briefEyebrow, briefTitle, briefDeck, resultEyebrow, resultTitle, resultDeck, resultPrimaryLabel, replayLabel;
        Text recordsLine, recordsBadges, recordsRuns, recordsArchive;
        readonly Dictionary<string, Button> options = new Dictionary<string, Button>();
        Button enterButton, briefPrimary, resultPrimary;

        // Session state.
        public string Phase { get; private set; } = "idle";
        public string SessionMode { get; private set; } = "duel";
        public Round Round { get; private set; }
        public Match Match { get; private set; }
        public DailyChallenge Daily { get; private set; }
        OperationFlow flow;
        string selectedMode = "duel";
        SoloProfile.Setup setup;
        int lessonStage, best;
        double lessonClearAt = double.NaN;
        float accumulator, countdownRemaining, finishDelay;
        int countdownNumber;
        string pausedFrom;
        bool autoDrive = true, lastFire, lastThrottle;
        Vector2 lastMove;
        readonly HashSet<string> runBadges = new HashSet<string>();
        readonly SceneView live = new SceneView(), replayScene = new SceneView();
        readonly List<SceneView> replay = new List<SceneView>();
        float replayElapsed;
        bool replayImpactShown;
        SimEvent pendingResultEvent;
        string resultAction;
        Vector3 cameraAim;
        float cameraIntro;
        public bool IgnoreFocusLoss { get; set; }

        public bool InSession => Phase != "idle";
        public Transform PreviewRider => world?.Player;

        public void Init(Camera camera, RectTransform safeArea, Material floor, Material body, Material glow, Action menu)
        {
            view = camera;
            safe = safeArea;
            returnToMenu = menu;
            sound = gameObject.AddComponent<SoloAudio>();
            sound.Enabled = SoloProfile.Sound;
            world = new SoloWorld(transform, floor, body, glow);
            setup = SoloProfile.LoadSetup();
            hud = new SoloHud(safeArea, Turn, TogglePause, ToggleAuto, () => Flight("jet"), () => Flight("sail"), Form, Disc);
            hud.Root.SetActive(false);
            BuildMenus();
            ShowPreview();
        }

        public void SetSound(bool enabled) { if (sound != null) sound.Enabled = enabled; }

        // A parked Duel layout on the chosen map gives the main menu its showcase cycle.
        public void ShowPreview()
        {
            world.Clear();
            Round = GridRules.CreateDuelRound(1, setup.rival, setup.difficulty, setup.map);
            ApplyLook(Round);
            live.Capture(Round);
            world.Render(live, 0);
        }

        // ---- Menus -----------------------------------------------------------------------------

        static Vector2 Center => new Vector2(.5f, .5f);

        RectTransform Overlay(string name, Vector2 size, out GameObject root)
        {
            var dim = SoloUi.Dim(name, safe, .78f);
            root = dim.gameObject;
            return SoloUi.Panel(name + " panel", dim, size);
        }

        void BuildMenus()
        {
            // Mode select.
            var select = Overlay("Mode select", new Vector2(1760, 860), out selectPanel);
            SoloUi.Label("Select eyebrow", "CHOOSE YOUR CIRCUIT", select, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -30), new Vector2(1600, 36), 24, TextAnchor.MiddleCenter, UiSkin.Accent, true);
            SoloUi.Label("Select title", "ENTER THE GRID", select, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -70), new Vector2(1600, 80), 60, TextAnchor.MiddleCenter, Color.white, true);
            var cards = new (string name, string mode, string title, string detail)[]
            {
                ("Mode Duel", "duel", "DUEL", "One chosen rival. First to three heats."),
                ("Mode Arena", "arena", "ARENA", "Three heats against three AI styles. Survive two."),
                ("Mode Daily", "daily", "DAILY SIGNAL", "Today's fixed Duel. Same seed all day."),
                ("Mode Shift", "shift", "GRID SHIFT", "Ride, dismount, and throw a returning disc."),
                ("Mode Operations", "operations", "OPERATIONS", "Portal Run, System Sweep, Arena Collapse, Journey."),
                ("Solar Transit", "transit", "SOLAR TRANSIT", "Jet and sail across two gaps in 12 seconds."),
                ("Mode Lesson", "lesson", "PLAY LESSON", "Learn to turn, build walls, and stay clear."),
            };
            for (int i = 0; i < cards.Length; i++)
            {
                var card = cards[i];
                int column = i % 3, row = i / 3;
                var offset = new Vector2((column - 1) * 560, 150 - row * 190);
                if (i == cards.Length - 1) offset.x = 0;
                string mode = card.mode;
                var button = SoloUi.Button(card.name, "", select, Center, Center, offset, new Vector2(530, 170), () => ChooseMode(mode), i == 0);
                var label = SoloUi.Caption(button);
                label.alignment = TextAnchor.UpperLeft;
                label.rectTransform.sizeDelta = new Vector2(-56, -40);
                label.text = "";
                SoloUi.Label(card.name + " title", card.title, button.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -22), new Vector2(470, 44), 32, TextAnchor.MiddleLeft, Color.white, true);
                SoloUi.Label(card.name + " detail", card.detail, button.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -72), new Vector2(470, 80), 22, TextAnchor.UpperLeft, UiSkin.Muted);
            }
            SoloUi.Button("Back To Menu", "BACK", select, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 30), new Vector2(320, 80), BackToMenu);

            // Mode setup.
            var setupRect = Overlay("Mode setup", new Vector2(1400, 860), out setupPanel);
            setupTitle = SoloUi.Label("Setup title", "", setupRect, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -30), new Vector2(1300, 76), 56, TextAnchor.MiddleCenter, Color.white, true);
            setupGuide = SoloUi.Label("Setup guide", "", setupRect, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -112), new Vector2(1260, 70), 24, TextAnchor.MiddleCenter, UiSkin.Muted);
            string[] keys = { "Rival", "Difficulty", "Map", "Rule", "Operation", "Final Rival" };
            foreach (string key in keys)
            {
                string captured = key;
                options[key] = SoloUi.Button("Option " + key, key, setupRect, new Vector2(.5f, 1), new Vector2(.5f, 1), Vector2.zero, new Vector2(820, 78), () => CycleOption(captured), false, 25);
            }
            setupInfo = SoloUi.Label("Setup info", "", setupRect, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 140), new Vector2(1260, 110), 23, TextAnchor.MiddleCenter, UiSkin.Text);
            enterButton = SoloUi.Button("Enter Mode", "ENTER", setupRect, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(210, 34), new Vector2(400, 90), EnterMode, true, 30);
            SoloUi.Button("Back To Modes", "BACK", setupRect, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-210, 34), new Vector2(360, 90), OpenSelect);

            // Briefing.
            var brief = Overlay("Briefing", new Vector2(1240, 720), out briefPanel);
            briefEyebrow = SoloUi.Label("Brief eyebrow", "", brief, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -36), new Vector2(1140, 36), 24, TextAnchor.MiddleCenter, UiSkin.Accent, true);
            briefTitle = SoloUi.Label("Brief title", "", brief, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -78), new Vector2(1140, 90), 62, TextAnchor.MiddleCenter, Color.white, true);
            briefDeck = SoloUi.Label("Brief deck", "", brief, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -190), new Vector2(1100, 330), 26, TextAnchor.UpperCenter);
            briefPrimary = SoloUi.Button("Launch", "LAUNCH", brief, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(210, 34), new Vector2(400, 90), Launch, true, 30);
            SoloUi.Button("Brief Change Mode", "CHANGE MODE", brief, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-210, 34), new Vector2(360, 90), ChangeMode);

            // Result.
            var result = Overlay("Result", new Vector2(1240, 720), out resultPanel);
            resultEyebrow = SoloUi.Label("Result eyebrow", "", result, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -36), new Vector2(1140, 36), 24, TextAnchor.MiddleCenter, UiSkin.Accent, true);
            resultTitle = SoloUi.Label("Result title", "", result, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -78), new Vector2(1140, 90), 62, TextAnchor.MiddleCenter, Color.white, true);
            resultDeck = SoloUi.Label("Result details", "", result, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -190), new Vector2(1100, 330), 25, TextAnchor.UpperCenter);
            resultPrimary = SoloUi.Button("Result Primary", "NEXT ROUND", result, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 34), new Vector2(400, 90), ResultPrimary, true, 28);
            resultPrimaryLabel = SoloUi.Caption(resultPrimary);
            SoloUi.Button("Result Change Mode", "CHANGE MODE", result, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-420, 34), new Vector2(360, 90), ChangeMode);
            SoloUi.Button("Main Menu", "MAIN MENU", result, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(420, 34), new Vector2(360, 90), MainMenu);

            // Pause.
            var pause = Overlay("Pause menu", new Vector2(1240, 560), out pausePanel);
            SoloUi.Label("Pause eyebrow", "CONNECTION HELD", pause, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -36), new Vector2(1140, 36), 24, TextAnchor.MiddleCenter, UiSkin.Accent, true);
            SoloUi.Label("Pause title", "GRID PAUSED", pause, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -78), new Vector2(1140, 90), 62, TextAnchor.MiddleCenter, Color.white, true);
            SoloUi.Label("Pause deck", "The Grid is waiting. Your steering and fire input have been cleared.", pause, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -190), new Vector2(1100, 80), 26, TextAnchor.UpperCenter);
            SoloUi.Button("Resume", "RESUME", pause, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 34), new Vector2(400, 90), TogglePause, true, 30);
            SoloUi.Button("Pause Change Mode", "CHANGE MODE", pause, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-420, 34), new Vector2(360, 90), ChangeMode);
            SoloUi.Button("Pause Main Menu", "MAIN MENU", pause, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(420, 34), new Vector2(360, 90), MainMenu);

            // Crash replay bar.
            var bar = SoloUi.Rect("Replay bar", safe, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 30), new Vector2(1300, 110));
            UiSkin.Panel(bar.gameObject.AddComponent<Image>());
            replayBar = bar.gameObject;
            replayLabel = SoloUi.Label("Replay label", "", bar, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(30, 0), new Vector2(880, 90), 25, TextAnchor.MiddleLeft, null, true);
            SoloUi.Button("Skip Replay", "SKIP REPLAY", bar, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-20, 0), new Vector2(330, 80), CompleteReplay, true);

            // Records.
            var records = Overlay("Records", new Vector2(1600, 900), out recordsPanel);
            SoloUi.Label("Records title", "RECORDS", records, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -30), new Vector2(1500, 70), 50, TextAnchor.MiddleCenter, Color.white, true);
            recordsLine = SoloUi.Label("Records line", "", records, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -110), new Vector2(1500, 40), 25, TextAnchor.MiddleCenter, UiSkin.Accent, true);
            recordsBadges = SoloUi.Label("Records badges", "", records, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -158), new Vector2(1500, 60), 22, TextAnchor.MiddleCenter, UiSkin.Muted);
            SoloUi.Label("Runs heading", "RUN ARCHIVE", records, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -228), new Vector2(700, 36), 24, TextAnchor.MiddleLeft, UiSkin.Accent, true);
            recordsRuns = SoloUi.Label("Runs list", "", records, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -270), new Vector2(820, 470), 22, TextAnchor.UpperLeft);
            SoloUi.Label("Archive heading", "GRID ARCHIVE", records, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -228), new Vector2(640, 36), 24, TextAnchor.MiddleLeft, UiSkin.Accent, true);
            recordsArchive = SoloUi.Label("Archive list", "", records, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -270), new Vector2(640, 470), 21, TextAnchor.UpperLeft, UiSkin.Muted);
            SoloUi.Button("Close Records", "BACK", records, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 30), new Vector2(320, 84), BackToMenu, true);
            HideAll();
        }

        void HideAll()
        {
            foreach (var panel in new[] { selectPanel, setupPanel, briefPanel, resultPanel, pausePanel, replayBar, recordsPanel })
                panel.SetActive(false);
        }

        public void OpenSelect()
        {
            EndRound();
            HideAll();
            Phase = "select";
            hud.Root.SetActive(false);
            selectPanel.SetActive(true);
            SoloUi.Select(GameObject.Find("Mode Duel"));
            sound.Music(null);
        }

        public void OpenRecords()
        {
            HideAll();
            Phase = "records";
            recordsLine.text = "PERSONAL RECORDS  /  " + SoloProfile.RecordsLine();
            recordsBadges.text = "BADGES  /  " + SoloProfile.BadgesLine();
            var runs = SoloProfile.Runs;
            var lines = new List<string>();
            foreach (var run in runs)
            {
                string label = !string.IsNullOrEmpty(run.day) ? "DAILY " + run.day
                    : run.mode == "operations" ? OperationLabel(run.operation)
                    : run.mode == "shift" ? "GRID SHIFT / " + SimMaps.Get(run.map).Name.ToUpperInvariant()
                    : run.mode == "lesson" ? "LESSON" : run.mode.ToUpperInvariant() + " / " + SimMaps.Get(run.map).Name.ToUpperInvariant();
                lines.Add((run.date ?? "").Substring(0, Math.Min(10, (run.date ?? "").Length)) + "  " + label + "  /  " + (run.outcome ?? "").ToUpperInvariant() + "  /  " + run.score + " PTS");
            }
            recordsRuns.text = lines.Count > 0 ? string.Join("\n", lines) : "No completed runs yet. Finish a match to record it here.";
            var archive = new List<string>();
            foreach (var id in new[] { "portal", "sweep", "collapse", "sentinel" })
            {
                var mission = SimOperations.Get(id);
                var entry = SoloProfile.Operation(id);
                archive.Add(entry.completed ? mission.Name.ToUpperInvariant() + " / BEST " + entry.best + "\n" + mission.RewardLore
                    : mission.Name.ToUpperInvariant() + " / LOCKED");
            }
            var journey = SoloProfile.Operation("journey");
            archive.Add("JOURNEY / " + (journey.completed ? "CLEARED / BEST " + journey.best : "NOT CLEARED"));
            archive.Add("TRAIL LOOKS / " + string.Join(", ", Array.FindAll(SoloProfile.Looks, SoloProfile.LookUnlocked)).ToUpperInvariant());
            recordsArchive.text = string.Join("\n\n", archive);
            recordsPanel.SetActive(true);
            SoloUi.Select(GameObject.Find("Close Records"));
        }

        static string OperationLabel(string id) => id == "journey" ? "JOURNEY" : SimOperations.Get(id).Name.ToUpperInvariant();

        void ChooseMode(string mode)
        {
            if (mode == "transit") { SceneManager.LoadScene("PhaseThreeTransit"); return; }
            selectedMode = mode;
            HideAll();
            Phase = "setup";
            setupPanel.SetActive(true);
            RefreshSetup();
            SoloUi.Select(enterButton.gameObject);
        }

        string[] VisibleOptions(string mode) => mode switch
        {
            "duel" => new[] { "Rival", "Difficulty", "Map" },
            "arena" => new[] { "Difficulty", "Map", "Rule" },
            "shift" => new[] { "Difficulty", "Map" },
            "operations" => setup.operation == "journey" ? new[] { "Operation", "Difficulty", "Final Rival" } : new[] { "Operation", "Difficulty" },
            _ => new string[0],
        };

        void RefreshSetup()
        {
            Daily = selectedMode == "daily" ? SimMatches.DailyFor(DateTime.UtcNow) : null;
            var visible = VisibleOptions(selectedMode);
            int row = 0;
            foreach (var pair in options)
            {
                int index = Array.IndexOf(visible, pair.Key);
                pair.Value.gameObject.SetActive(index >= 0);
                if (index < 0) continue;
                ((RectTransform)pair.Value.transform).anchoredPosition = new Vector2(0, -205 - row++ * 92);
                SoloUi.Caption(pair.Value).text = pair.Key.ToUpperInvariant() + "   /   " + OptionValue(pair.Key) + "   ›";
            }
            var rival = GridRules.Rivals[setup.rival];
            var map = SimMaps.Get(selectedMode == "daily" ? Daily.Map : setup.map);
            string difficultyHint = setup.difficulty == "rookie" ? "Reacts later; speed stays equal." : setup.difficulty == "expert" ? "Reacts sooner; speed stays equal." : "Balanced reactions; speed stays equal.";
            switch (selectedMode)
            {
                case "duel":
                    setupTitle.text = "DUEL";
                    setupGuide.text = "First to three heats. Turn into open space and trap one rival. Hold FIRE to cut walls with 24 bullets.";
                    setupInfo.text = rival.Id + " / " + rival.Style.ToUpperInvariant() + " / " + rival.Cue + ". " + difficultyHint + "\n" + map.Cue + "\nBEST " + SoloProfile.Best(BestKey("duel", setup.map, setup.difficulty, "classic"));
                    break;
                case "arena":
                    setupTitle.text = "ARENA";
                    setupGuide.text = "Three heats against Hunter, Survivor, and Trapper AI. Survive at least two; every heat adds to your score.";
                    setupInfo.text = map.Cue + (setup.rule == "decay" ? " Advanced: closed walls pulse before fading after 10 seconds." : " Light walls remain until the heat ends.") +
                        "\nTrails: solid you, single band Hunter, low band Survivor, double band Trapper.\nBEST " + SoloProfile.Best(BestKey("arena", setup.map, setup.difficulty, setup.rule));
                    break;
                case "daily":
                    var dailyRival = GridRules.Rivals[Daily.Rival];
                    setupTitle.text = "DAILY SIGNAL";
                    setupGuide.text = "Offline first-to-three Duel with a fixed seed, map, rival, and Standard difficulty for the UTC date.";
                    int today = SoloProfile.Best("daily:" + Daily.Day);
                    setupInfo.text = "UTC " + Daily.Day + " / rules v" + DailyChallenge.RulesVersion + " / seed " + Daily.Seed + "\n" +
                        SimMaps.Get(Daily.Map).Name.ToUpperInvariant() + " / " + dailyRival.Id + " (" + dailyRival.Style.ToUpperInvariant() + ") / STANDARD\nTODAY'S BEST " + today;
                    break;
                case "shift":
                    setupTitle.text = "GRID SHIFT";
                    setupGuide.text = "Two forms. Ride to build and cut walls, then dismount to dodge and throw a returning disc. Your walls stay lethal.";
                    setupInfo.text = "Echo has the same 24-bullet gun, foot disc, and mounting rules. Two disc hits derez a rider.\n" + map.Cue +
                        "\nBEST " + SoloProfile.Best(BestKey("shift", setup.map, setup.difficulty, "classic"));
                    break;
                case "operations":
                    setupTitle.text = "OPERATIONS";
                    setupGuide.text = "Solo missions with timed gates, marked scanner beams, and shrinking rings. Follow the objective and its warning panel.";
                    var op = setup.operation == "journey" ? null : SimOperations.Get(setup.operation);
                    setupInfo.text = op != null
                        ? op.Briefing + "\nBEST " + SoloProfile.Operation(op.Id).best + (op.RewardLook != null ? "  /  REWARD: " + SoloProfile.LookLabel(op.RewardLook) + " TRAIL" : "")
                        : "Three linked operations with skippable briefings. The final Sentinel challenge is optional.\nBEST " + SoloProfile.Operation("journey").best;
                    break;
                default:
                    setupTitle.text = "PLAY LESSON";
                    setupGuide.text = "A short, skippable lesson: turn left or right, watch your light wall form, and stay clear of it.";
                    setupInfo.text = "Use LEFT and RIGHT (or A and D). Your trail stays dangerous to you too.";
                    break;
            }
        }

        string OptionValue(string key) => key switch
        {
            "Rival" => GridRules.Rivals[setup.rival].Id + " / " + GridRules.Rivals[setup.rival].Style.ToUpperInvariant(),
            "Difficulty" => GridRules.Difficulties[setup.difficulty].Label.ToUpperInvariant(),
            "Map" => SimMaps.Get(setup.map).Name.ToUpperInvariant(),
            "Rule" => setup.rule == "decay" ? "FADING WALLS" : "PERSISTENT WALLS",
            "Operation" => setup.operation == "journey" ? "JOURNEY / ALL THREE" : SimOperations.Get(setup.operation).Name.ToUpperInvariant(),
            "Final Rival" => setup.finalRival ? "SENTINEL ON" : "SENTINEL OFF",
            _ => "",
        };

        static string Next(string[] values, string current) => values[(Array.IndexOf(values, current) + 1 + values.Length) % values.Length];

        void CycleOption(string key)
        {
            switch (key)
            {
                case "Rival": setup.rival = Next(GridRules.RivalOrder, setup.rival); break;
                case "Difficulty": setup.difficulty = Next(GridRules.DifficultyOrder, setup.difficulty); break;
                case "Map": setup.map = Next(SimMaps.Order, setup.map); break;
                case "Rule": setup.rule = setup.rule == "decay" ? "classic" : "decay"; break;
                case "Operation": setup.operation = Next(new[] { "portal", "sweep", "collapse", "journey" }, setup.operation); break;
                case "Final Rival": setup.finalRival = !setup.finalRival; break;
            }
            SoloProfile.SaveSetup(setup);
            RefreshSetup();
            if (key == "Map") ShowPreview();
        }

        void EnterMode()
        {
            HideAll();
            runBadges.Clear();
            autoDrive = SoloProfile.AutoDrive;
            Match = null; flow = null; Daily = null;
            switch (selectedMode)
            {
                case "duel":
                case "daily":
                case "arena":
                    StartMatch(selectedMode);
                    break;
                case "shift":
                    SessionMode = "shift";
                    ShowBrief("GRID SHIFT / TRAINING", "TWO FORMS",
                        "Derez Echo before the lockout. Cycle: LEFT/RIGHT turns and leaves a lethal wall; hold FIRE to shoot up to 24 wall-breaking bullets; FORM dismounts into clear space. " +
                        "On foot: the pad moves you in arena directions (north is up), DISC throws a returning disc, FORM remounts when clear. Two disc hits derez either rider. Your own walls remain lethal.",
                        "START GRID SHIFT");
                    break;
                case "operations":
                    StartOperationFlow();
                    break;
                default:
                    SessionMode = "lesson";
                    lessonStage = 0;
                    BeginRound();
                    break;
            }
        }

        void StartMatch(string mode)
        {
            SessionMode = mode == "arena" ? "arena" : "duel";
            Daily = mode == "daily" ? SimMatches.DailyFor(DateTime.UtcNow) : null;
            Match = new Match
            {
                Arena = mode == "arena",
                Seed = Daily?.Seed ?? RandomSeed(),
                Rival = Daily?.Rival ?? setup.rival,
                Difficulty = Daily?.Difficulty ?? setup.difficulty,
                Map = Daily?.Map ?? setup.map,
                Rule = mode == "arena" ? setup.rule : "classic",
            };
            BeginRound();
        }

        void StartOperationFlow()
        {
            SessionMode = "operations";
            flow = new OperationFlow { Journey = setup.operation == "journey", Seed = RandomSeed(), Difficulty = setup.difficulty };
            if (flow.Journey)
            {
                flow.Sequence.AddRange(SimOperations.Journey);
                if (setup.finalRival) flow.Sequence.Add("sentinel");
            }
            else flow.Sequence.Add(setup.operation);
            ShowOperationBrief();
        }

        void ShowOperationBrief()
        {
            var mission = SimOperations.Get(flow.Sequence[flow.Index]);
            Round = GridRules.CreateOperationRound(mission.Id, SimMatches.OperationStageSeed(flow.Seed, flow.Index), flow.Difficulty);
            ApplyLook(Round);
            world.Clear();
            live.Capture(Round);
            world.Render(live, 0);
            string count = flow.Journey ? "OPERATION " + (flow.Index + 1) + "/" + flow.Sequence.Count : "OPERATION / SOLO";
            ShowBrief(count, mission.Name.ToUpperInvariant(), mission.Briefing + "\n\nObjective: " + mission.Objective + "\n" + mission.Tip, "LAUNCH");
        }

        void ShowBrief(string eyebrow, string title, string deck, string action)
        {
            HideAll();
            Phase = "brief";
            hud.Root.SetActive(false);
            briefEyebrow.text = eyebrow; briefTitle.text = title; briefDeck.text = deck;
            SoloUi.Caption(briefPrimary).text = action;
            briefPanel.SetActive(true);
            SoloUi.Select(briefPrimary.gameObject);
        }

        void Launch()
        {
            if (Phase != "brief") return;
            if (flow != null) flow.Status = "running";
            BeginRound();
        }

        void ChangeMode()
        {
            EndRound();
            OpenSelect();
        }

        void MainMenu()
        {
            EndRound();
            HideAll();
            Phase = "idle";
            hud.Root.SetActive(false);
            ShowPreview();
            returnToMenu();
        }

        void BackToMenu() => MainMenu();

        void EndRound()
        {
            hud?.ClearHolds();
            replay.Clear();
            pendingResultEvent = null;
            sound?.Music(null);
        }

        static uint RandomSeed()
        {
            var bytes = new byte[4];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        // ---- Round lifecycle -------------------------------------------------------------------

        void ApplyLook(Round round)
        {
            var player = round.Player;
            if (player != null) player.Color = SoloProfile.LookColor(SoloProfile.Look);
        }

        static string BestKey(string mode, string map, string difficulty, string rule) => mode + ":" + map + ":" + difficulty + ":" + rule;

        void LoadBest()
        {
            if (SessionMode == "operations") best = SoloProfile.Operation(flow.Journey ? "journey" : flow.Sequence[0]).best;
            else if (Daily != null) best = SoloProfile.Best("daily:" + Daily.Day);
            else if (SessionMode == "lesson") best = 0;
            else best = SoloProfile.Best(BestKey(SessionMode, Round.Map, Round.Difficulty, Round.Rule));
        }

        void BeginRound()
        {
            HideAll();
            switch (SessionMode)
            {
                case "shift": Round = SimShift.CreateShiftRound(RandomSeed(), setup.difficulty, setup.map); runBadges.Clear(); break;
                case "operations": Round = GridRules.CreateOperationRound(flow.Sequence[flow.Index], SimMatches.OperationStageSeed(flow.Seed, flow.Index), flow.Difficulty); break;
                case "lesson": Round = GridRules.CreateLessonRound(RandomSeed()); lessonClearAt = double.NaN; break;
                default: Round = SimMatches.CreateRoundFor(Match); break;
            }
            ApplyLook(Round);
            GridRules.SetAutoDrive(Round, "YOU", autoDrive);
            lastFire = lastThrottle = false;
            lastMove = Vector2.zero;
            world.Clear();
            live.Capture(Round);
            world.Render(live, 0);
            LoadBest();
            replay.Clear();
            CaptureReplay(true);
            Phase = "countdown";
            countdownRemaining = (flow != null && flow.Index > 0) || (Match != null && Match.RoundNumber > 1) ? 1.5f : 3f;
            countdownNumber = Mathf.CeilToInt(countdownRemaining);
            cameraIntro = 1;
            accumulator = 0;
            hud.Root.SetActive(true);
            hud.ClearMessages();
            hud.ClearHolds();
            hud.SetPaused(false);
            hud.SetAuto(autoDrive);
            hud.Countdown.text = countdownNumber.ToString();
            hud.Intent.text = SessionMode == "shift" ? "ECHO / READING LANES" : SessionMode == "duel" ? GridRules.Rivals[Match.Rival].Cue.ToUpperInvariant() : "";
            hud.Status.text = SessionMode == "lesson" ? "STEP 1 / TURN LEFT OR RIGHT TO CARVE A LIGHT WALL."
                : SessionMode == "shift" ? "FIRE CUTS A WALL; FORM DISMOUNTS WHEN CLEAR."
                : SessionMode == "arena" ? SimMaps.Get(Round.Map).Cue.ToUpperInvariant() : "";
            sound.Beep(440, .08, "triangle", .03);
            SnapCamera();
            UpdateHud();
            hud.DrawRadar(live, 0, true);
        }

        void LaunchRound()
        {
            Phase = "running";
            hud.Countdown.text = "GO";
            hideCountdownAt = Time.unscaledTime + .42f;
            accumulator = 0;
            sound.Beep(730, .2, "triangle", .06);
            sound.Motif("wall");
        }
        float hideCountdownAt;

        void Update()
        {
            if (Phase == "idle") return;
            float dt = Mathf.Min(Mathf.Max(Time.unscaledDeltaTime, 0), .25f);
            ReadKeyboard();
            if (Phase == "countdown")
            {
                countdownRemaining -= dt;
                int next = Mathf.CeilToInt(countdownRemaining);
                if (next <= 0) LaunchRound();
                else if (next != countdownNumber)
                {
                    countdownNumber = next;
                    hud.Countdown.text = next.ToString();
                    sound.Beep(440 + (3 - next) * 95, .08, "triangle", .03);
                }
            }
            else if (Phase == "running")
            {
                if (hideCountdownAt > 0 && Time.unscaledTime > hideCountdownAt) { hud.Countdown.text = ""; hideCountdownAt = 0; }
                SyncHeldInput();
                accumulator += dt;
                int steps = 0;
                while (accumulator >= GridRules.STEP && steps < 16 && Phase == "running")
                {
                    if (SessionMode == "shift") SimShift.StepShiftRound(Round); else GridRules.StepRound(Round);
                    CaptureReplay(false);
                    HandleEvents();
                    accumulator -= (float)GridRules.STEP;
                    steps++;
                }
                accumulator = Mathf.Min(accumulator, (float)GridRules.STEP * 16);
                if (Phase == "running" && SessionMode == "lesson") CheckLesson();
            }
            else if (Phase == "finishing")
            {
                finishDelay -= dt;
                if (finishDelay <= 0) ShowResult();
            }
            if (Phase == "replay") UpdateReplay(dt);
            else if (Round != null && Phase != "select" && Phase != "setup" && Phase != "records")
            {
                live.Capture(Round);
                world.Render(live, Phase == "paused" ? 0 : dt);
                if (hud.Root.activeSelf) hud.DrawRadar(live, dt);
            }
            if (hud.Root.activeSelf) UpdateHud();
            sound.Music(Phase == "running" || Phase == "countdown" ? MusicIntensity() : (float?)null);
        }

        float MusicIntensity()
        {
            if (Phase == "countdown" || Round == null) return .22f;
            var player = Round.Player;
            double nearest = 20;
            if (player != null)
                foreach (var r in Round.Riders)
                    if (!r.IsPlayer && r.Alive) nearest = Math.Min(nearest, JsMath.Hypot(r.X - player.X, r.Z - player.Z));
            double pressure = Math.Max(0, 1 - nearest / 16), duration = Math.Min(1, Round.Elapsed / 38);
            double stakes = SessionMode == "duel" && Match != null ? Math.Min(1, (Match.PlayerWins + Match.RivalWins) / 4.0) : 0;
            return (float)Math.Min(1, .25 + duration * .2 + pressure * .35 + stakes * .2);
        }

        // ---- Input -------------------------------------------------------------------------------

        void ReadKeyboard()
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P))
            {
                if (Phase == "replay") CompleteReplay();
                else if (Phase == "setup") OpenSelect();
                else if (Phase == "select" || Phase == "records") BackToMenu();
                else TogglePause();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (Phase == "replay") { CompleteReplay(); return; }
            }
            if (Phase != "running") return;
            bool foot = SessionMode == "shift" && Round.Player.Form == "foot";
            if (!foot)
            {
                if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) Turn(-1);
                if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) Turn(1);
            }
            if (Input.GetKeyDown(KeyCode.Q)) Flight("jet");
            if (Input.GetKeyDown(KeyCode.R)) Flight("sail");
            if (Input.GetKeyDown(KeyCode.F)) Form();
            if (Input.GetKeyDown(KeyCode.Space)) Disc();
        }

        void SyncHeldInput()
        {
            var player = Round.Player;
            if (player == null) return;
            bool throttle = hud.ForwardHeld || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            if (throttle != lastThrottle) { lastThrottle = throttle; GridRules.SetThrottle(Round, "YOU", throttle); }
            bool fire = SessionMode != "lesson" && (hud.FireHeld || Input.GetKey(KeyCode.E));
            if (fire != lastFire)
            {
                lastFire = fire;
                if (SessionMode == "shift") SimShift.SetFire(Round, "YOU", fire); else GridRules.SetFire(Round, "YOU", fire);
                HandleEvents();
            }
            if (SessionMode == "shift")
            {
                var move = hud.FootMove;
                if (player.Form == "foot")
                {
                    move.x += (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
                    move.y += (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
                }
                move.x = Mathf.Clamp(move.x, -1, 1); move.y = Mathf.Clamp(move.y, -1, 1);
                if (move != lastMove)
                {
                    lastMove = move;
                    // Screen up is north, which is -z in simulation coordinates.
                    SimShift.SetMove(Round, "YOU", move.x, -move.y);
                }
            }
        }

        public void Turn(int sign)
        {
            if (Phase != "running") return;
#if DEVELOPMENT_BUILD
            Debug.Log("SOLO_TURN sign " + sign + " tick " + Round.Tick + " mode " + SessionMode);
#endif
            if (SessionMode == "shift") SimShift.QueueTurn(Round, "YOU", sign);
            else GridRules.QueueTurn(Round, "YOU", sign);
            HandleEvents();
        }

        void Flight(string kind)
        {
            if (Phase != "running" || SessionMode == "lesson") return;
            var player = Round.Player;
            if (player == null || !player.Alive || player.Form == "foot") { hud.ShowToast("MOUNT TO FLY"); return; }
            if (!GridRules.RequestFlight(Round, "YOU", kind)) { hud.ShowToast(kind.ToUpperInvariant() + " NOT READY"); return; }
            HandleEvents();
        }

        void Form()
        {
            if (Phase != "running" || SessionMode != "shift") return;
            SimShift.RequestForm(Round, "YOU");
            HandleEvents();
        }

        void Disc()
        {
            if (Phase != "running" || SessionMode != "shift") return;
            if (!SimShift.ThrowDisc(Round, "YOU") && Round.Player.Form == "cycle") hud.ShowToast("DISMOUNT TO THROW");
            HandleEvents();
        }

        void ToggleAuto()
        {
            autoDrive = !autoDrive;
            hud.SetAuto(autoDrive);
            hud.ClearHolds();
            if (Round != null) GridRules.SetAutoDrive(Round, "YOU", autoDrive);
            hud.ShowToast(autoDrive ? "AUTO DRIVE ON" : "MANUAL DRIVE / HOLD FORWARD");
        }

        public void TogglePause()
        {
            if (Phase == "running" || Phase == "countdown")
            {
                pausedFrom = Phase;
                Phase = "paused";
                hud.ClearHolds();
                if (Round != null)
                {
                    if (SessionMode == "shift") { SimShift.SetFire(Round, "YOU", false); SimShift.SetMove(Round, "YOU", 0, 0); }
                    else GridRules.SetFire(Round, "YOU", false);
                    GridRules.SetThrottle(Round, "YOU", false);
                    GridRules.DrainEvents(Round);
                }
                lastFire = lastThrottle = false;
                lastMove = Vector2.zero;
                hud.SetPaused(true);
                hud.Countdown.text = "";
                pausePanel.SetActive(true);
                SoloUi.Select(GameObject.Find("Resume"));
            }
            else if (Phase == "paused")
            {
                Phase = pausedFrom;
                accumulator = 0;
                pausePanel.SetActive(false);
                hud.SetPaused(false);
                if (Phase == "countdown") hud.Countdown.text = Mathf.Max(1, Mathf.CeilToInt(countdownRemaining)).ToString();
                SoloUi.Select(null);
            }
        }

        public void Suspend()
        {
            if (Phase == "running" || Phase == "countdown") TogglePause();
            hud?.ClearHolds();
        }

        // ---- Events and feedback ---------------------------------------------------------------

        void HandleEvents()
        {
            if (Round == null) return;
            foreach (var e in GridRules.DrainEvents(Round))
            {
                bool you = e.RiderId == "YOU";
                switch (e.Type)
                {
                    case "flight-start" when you:
                        hud.ShowToast(e.Kind == "jet" ? "LIGHT JET / LIFT" : "SOLAR SAIL / GLIDE");
                        hud.ShowCaption(e.Kind == "jet" ? "LIGHT JET" : "SOLAR SAIL");
                        sound.Beep(e.Kind == "jet" ? 840 : 610, .15, "triangle", .025);
                        break;
                    case "turn-queued" when you:
                        hud.ShowToast(e.Sign < 0 ? "LEFT TURN QUEUED" : "RIGHT TURN QUEUED");
                        sound.Beep(340, .045, "triangle", .015);
                        break;
                    case "turn" when you:
                        sound.Motif("turn");
                        if (e.NearMiss) { hud.ShowToast("CLOSE CALL"); hud.ShowCaption("CLOSE CALL"); sound.Motif("near"); }
                        if (SessionMode == "lesson" && lessonStage < 2)
                        {
                            lessonStage++;
                            hud.Status.text = lessonStage == 1 ? "STEP 2 / YOUR GLOWING WALL STAYS BEHIND YOU. TURN ONCE MORE."
                                : "STEP 3 / KEEP CLEAR OF EVERY WALL FOR SIX SECONDS.";
                            if (lessonStage == 2) lessonClearAt = Round.Elapsed + 6;
                        }
                        break;
                    case "ai-intent" when SessionMode == "duel":
                        hud.Intent.text = e.RiderId + " / " + (e.Style ?? "").ToUpperInvariant() + " / TURNING " + (e.Sign < 0 ? "LEFT" : "RIGHT");
                        break;
                    case "shift-form":
                        hud.ShowToast(e.RiderId + (e.Form == "foot" ? " DISMOUNTED" : " REMOUNTED"));
                        sound.Beep(you ? 650 : 460, .18, "triangle", .03);
                        if (you)
                        {
                            runBadges.Add("form-switch");
                            hud.Status.text = e.Form == "foot" ? "ON FOOT / THE PAD MOVES YOU IN ARENA DIRECTIONS. DISC THROWS." : "CYCLE / YOUR TRAIL IS LIVE AGAIN. FORM DISMOUNTS WHEN CLEAR.";
                            lastMove = new Vector2(99, 99);
                        }
                        break;
                    case "shift-denied" when you:
                        hud.ShowToast(e.Reason.ToUpperInvariant());
                        break;
                    case "pickup-spawn":
                        hud.ShowToast(e.Pickup.ToUpperInvariant() + " SPAWNED / CHECK RADAR");
                        break;
                    case "pickup":
                        if (you) { hud.ShowToast(e.Pickup.ToUpperInvariant() + " PICKED UP"); sound.Beep(980, .12, "triangle", .03); }
                        var picker = Round.Find(e.RiderId);
                        if (picker != null) world.Ring(picker.X, picker.Z, e.Pickup == "ammo" ? new[] { .25f, .85f, 1f } : e.Pickup == "nitro" ? new[] { .25f, 1f, .4f } : new[] { 1f, .54f, .18f }, 1.4f);
                        break;
                    case "arena-shrink":
                        hud.ShowToast("GRID SHRINKING / " + e.Limit);
                        break;
                    case "rocket-explode":
                        world.Impact(e.X, e.Z);
                        world.Ring(e.X, e.Z, new[] { 1f, .48f, .13f }, 3.2f);
                        hud.ShowCaption("ROCKET IMPACT");
                        sound.Motif("crash");
                        break;
                    case "bullet-fire":
                        sound.Beep(you ? 930 : 690, .1, "sawtooth", .028);
                        break;
                    case "bullet-cut":
                        hud.ShowToast(e.RiderId + " CUT A WALL");
                        hud.ShowCaption("LIGHT WALL CUT");
                        world.Impact(e.X, e.Z);
                        sound.Motif("wall");
                        break;
                    case "bullet-blocked" when you:
                        hud.ShowToast("BULLET BLOCKED / " + e.Reason.ToUpperInvariant());
                        break;
                    case "disc-throw":
                        hud.ShowToast(e.RiderId + " DISC OUT");
                        sound.Beep(you ? 780 : 580, .13, "sawtooth", .035);
                        break;
                    case "disc-hit":
                        hud.ShowToast(e.RiderId + " HIT / " + e.Hp + " INTEGRITY");
                        world.Impact(e.X, e.Z);
                        sound.Motif("near");
                        break;
                    case "disc-return" when you:
                        hud.ShowToast("DISC RETURNED");
                        break;
                    case "operation-warning":
                        hud.ShowToast(e.Message.ToUpperInvariant(), 2.2f);
                        sound.Motif("wall");
                        break;
                    case "operation-checkpoint":
                        hud.ShowToast(e.Name.ToUpperInvariant() + " / " + e.Progress + "/" + e.Total);
                        sound.Beep(760 + e.Progress * 90, .12, "triangle", .045);
                        break;
                    case "skill":
                        runBadges.Add(e.Kind);
                        hud.ShowToast(e.Kind == "clean-trap" ? "CLEAN TRAP +" + e.Points : "CLOSE ESCAPE +" + e.Points);
                        break;
                    case "crash":
                        world.Impact(e.X, e.Z);
                        var rider = Round.Find(e.RiderId);
                        world.Derez(e.X, e.Z, rider?.Color ?? GridRules.Colors.Player, false);
                        sound.Motif("crash");
                        sound.Motif("derez");
                        if (!you) hud.ShowToast(e.RiderId + " DEREZZED" + (SessionMode == "shift" ? "" : " +250"));
                        break;
                    case "round-end":
                        Finish(e);
                        return;
                }
            }
        }

        // ---- Lesson ------------------------------------------------------------------------------

        void CheckLesson()
        {
            var player = Round.Player;
            if (!double.IsNaN(lessonClearAt) && player.Alive && Round.Elapsed >= lessonClearAt) FinishLesson(true, null);
        }

        void FinishLesson(bool success, Outcome outcome)
        {
            Phase = "result";
            resultAction = "lesson";
            if (success) RecordRun("won", 0);
            string message = success
                ? "You turned, built a light wall, and stayed clear of it. You are ready for Duel."
                : CrashMessage(outcome ?? Round.Outcome) + " Your own trail stays dangerous. Try another route.";
            OpenResult(success ? "LESSON COMPLETE" : "LESSON RESET", success ? "READY FOR THE GRID" : "STAY ALIVE", message, success ? "PLAY AGAIN" : "TRY LESSON");
            if (success) sound.Motif("win");
        }

        // ---- Finish ------------------------------------------------------------------------------

        static string CrashMessage(Outcome outcome)
        {
            if (outcome == null) return "Your cycle was derezzed.";
            switch (outcome.Reason)
            {
                case "boundary": return "You hit the arena edge.";
                case "collapse": return "The collapse ring caught you.";
                case "hazard": return (outcome.By ?? "A hazard") + " struck your cycle.";
                case "timeout": return "Time ran out before " + (outcome.By ?? "the objective") + ".";
                case "gap": return "You fell into the gap. Lift off before the edge and land beyond it.";
                case "obstacle": return "You hit a barrier.";
                case "rider": return "You collided with " + (outcome.By ?? "another rider") + ".";
                case "rocket": return (outcome.By ?? "A rival") + "'s rocket derezzed you.";
                case "disc": return (outcome.By ?? "A rival") + "'s disc derezzed you.";
                case "trail": return outcome.By == "YOU" ? "You crossed your own light wall." : "You crossed " + (outcome.By ?? "a rival") + "'s light wall.";
            }
            return "Your cycle was derezzed.";
        }

        string ScoreExplanation()
        {
            var b = Round.ScoreBreakdown;
            var parts = new List<string>();
            if (b.Derez > 0) parts.Add("rival derez +" + b.Derez);
            if (b.CloseCall > 0) parts.Add("close escape +" + b.CloseCall);
            if (b.Trap > 0) parts.Add("clean trap +" + b.Trap);
            if (b.Checkpoint > 0) parts.Add("gates +" + b.Checkpoint);
            if (b.Win > 0) parts.Add("completion +" + b.Win);
            return parts.Count > 0 ? string.Join(" / ", parts) : "No score this round";
        }

        void Finish(SimEvent e)
        {
            CaptureReplay(true);
            hud.Countdown.text = "";
            hud.ClearHolds();
            var outcome = e.Outcome;
            if (SessionMode == "lesson") { FinishLesson(false, outcome); return; }
            if (SessionMode == "shift") { FinishShift(outcome); return; }
            if (SessionMode == "operations") { FinishOperation(outcome); return; }
            bool duel = !Match.Arena;
            // An Arena heat that times out with the player alive counts as survived.
            string kind = Match.Arena && outcome.Kind == "draw" ? "won" : outcome.Kind;
            SimMatches.Record(Match, kind, Round.Score);
            int total = Match.Points;
            if (Match.Completed) RecordRun(Match.Won ? "won" : "lost", total);
            if (Daily != null) SoloProfile.SaveBest("daily:" + Daily.Day, total);
            else SoloProfile.SaveBest(BestKey(SessionMode, Round.Map, Round.Difficulty, Round.Rule), total);
            LoadBest();
            bool won = kind == "won";
            if (won) sound.Motif("win");
            string cause = won ? (duel ? GridRules.Rivals[Match.Rival].Id + " derezzed." : "All rivals derezzed.")
                : kind == "draw" ? "Time expired with both riders active." : CrashMessage(outcome);
            if (won && Match.Arena && outcome.Kind == "draw") cause = "You survived the full heat.";
            string scoreText = "Heat +" + Round.Score + " (" + ScoreExplanation() + ").";
            string scoreline = duel ? " Duel " + Match.PlayerWins + "-" + Match.RivalWins + "." : " Arena " + Match.PlayerWins + " of " + Match.RoundNumber + " heats survived.";
            string summary = Match.Completed
                ? duel ? " Match total " + total + " points." : " Three-heat total " + total + " points. " + (Match.Won ? "Objective complete." : "Survive two heats to clear Arena.")
                : "";
            string tip = won ? "Your route held." : outcome.Reason == "trail" ? "Look for open space before your next turn." : "Leave room to escape at the arena edge.";
            resultDeckText = cause + " " + scoreText + scoreline + summary + " " + tip;
            resultWon = won;
            resultAction = "match";
            pendingResultEvent = e;
            if (!won && replay.Count > 2) StartReplay("CRASH REPLAY / " + cause);
            else { Phase = "finishing"; finishDelay = .65f; }
        }
        string resultDeckText;
        bool resultWon;

        void ShowResult()
        {
            if (resultAction != "match") return;
            bool complete = Match.Completed, duel = !Match.Arena;
            bool success = complete ? Match.Won : resultWon;
            string eyebrow = complete ? success ? (duel ? (Daily != null ? "DAILY SIGNAL CLEARED" : "DUEL WON") : "ARENA CLEARED") : (duel ? "DUEL LOST" : "ARENA FAILED")
                : resultWon ? "HEAT WON" : "HEAT LOST";
            string title = complete ? success ? "GRID MASTER" : "END OF LINE" : resultWon ? "ROUND SECURED" : "ROUND LOST";
            OpenResult(eyebrow, title, resultDeckText, complete ? "FAST RETRY" : "NEXT ROUND");
        }

        void FinishShift(Outcome outcome)
        {
            SoloProfile.SaveBest(BestKey("shift", Round.Map, Round.Difficulty, Round.Rule), Round.Score);
            RecordRun(outcome.Kind, Round.Score);
            LoadBest();
            string cause = outcome.Reason == "disc" ? "A disc derezzed the rider."
                : outcome.Reason == "timeout" ? "The lockout ended; remaining integrity decided it."
                : outcome.Reason == "mutual" ? "Both riders derezzed together."
                : outcome.Kind == "won" ? "Echo derezzed." : CrashMessage(outcome);
            var player = Round.Player;
            Rider echo = null;
            foreach (var r in Round.Riders) if (!r.IsPlayer) echo = r;
            string deck = cause + " You " + Math.Max(0, player.Hp) + "/2, Echo " + Math.Max(0, echo.Hp) + "/2 integrity. Score " + Round.Score + ". Rematch or change mode.";
            resultAction = "shift";
            sound.Motif(outcome.Kind == "won" ? "win" : "crash");
            Phase = "result";
            OpenResult(outcome.Kind == "won" ? "MATCH WON" : outcome.Kind == "draw" ? "DRAW" : "MATCH LOST",
                outcome.Kind == "won" ? "GRID SECURED" : outcome.Kind == "draw" ? "GRID LOCKED" : "END OF LINE", deck, "REMATCH");
        }

        void FinishOperation(Outcome outcome)
        {
            var mission = SimOperations.Get(Round.Operation.Id);
            bool won = outcome.Kind == "won";
            if (won)
            {
                sound.Motif("win");
                flow.Points += Round.Score;
                SoloProfile.CompleteOperation(mission.Id, Round.Score);
                flow.Status = flow.Index + 1 < flow.Sequence.Count ? "next" : "complete";
                if (flow.Status == "complete" && flow.Journey) SoloProfile.CompleteOperation("journey", flow.Points);
            }
            else
            {
                flow.Status = "retry";
                sound.Motif("crash");
            }
            LoadBest();
            if (!flow.Journey || flow.Status == "complete" || !won) RecordRun(won ? "won" : "lost", flow.Points + (won ? 0 : Round.Score));
            string reason = won ? mission.Objective + " Complete." : CrashMessage(outcome);
            string result = won
                ? "Stage +" + Round.Score + " points. " + (mission.RewardLook != null ? SoloProfile.LookLabel(mission.RewardLook) + " trail look and archive fragment unlocked. " : "Archive fragment unlocked. ") +
                  (flow.Status == "next" ? "Next: " + SimOperations.Get(flow.Sequence[flow.Index + 1]).Name + "."
                    : flow.Journey ? "Journey complete. Total " + flow.Points + " points." : "Best " + best + " points.")
                : "Retry this operation or change mode. " + mission.Tip;
            resultAction = "operation";
            Phase = "result";
            OpenResult(won ? "OPERATION COMPLETE" : "OPERATION FAILED", won ? "ROUTE SECURED" : "SIGNAL LOST", reason + " " + result,
                won ? flow.Status == "next" ? "NEXT OPERATION" : "NEW RUN" : "RETRY OPERATION");
        }

        void OpenResult(string eyebrow, string title, string deck, string primary)
        {
            HideAll();
            Phase = "result";
            hud.Countdown.text = "";
            resultEyebrow.text = eyebrow; resultTitle.text = title; resultDeck.text = deck;
            resultPrimaryLabel.text = primary;
            resultPanel.SetActive(true);
            SoloUi.Select(resultPrimary.gameObject);
        }

        void ResultPrimary()
        {
            HideAll();
            switch (resultAction)
            {
                case "match":
                    if (Match.Completed)
                    {
                        string mode = Daily != null ? "daily" : Match.Arena ? "arena" : "duel";
                        runBadges.Clear();
                        StartMatch(mode);
                    }
                    else { SimMatches.Next(Match); BeginRound(); }
                    break;
                case "shift": BeginRound(); break;
                case "lesson": lessonStage = 0; BeginRound(); break;
                case "operation":
                    if (flow.Status == "complete") { runBadges.Clear(); StartOperationFlow(); }
                    else if (flow.Status == "retry") { flow.Status = "running"; BeginRound(); }
                    else { flow.Index++; flow.Status = "brief"; ShowOperationBrief(); }
                    break;
            }
        }

        void RecordRun(string outcome, int score)
        {
            var badges = new List<string>();
            if (outcome == "won") badges.Add("victor");
            if (runBadges.Contains("clean-trap")) badges.Add("trap");
            if (runBadges.Contains("close-call")) badges.Add("escape");
            if (SessionMode == "operations" && flow.Journey && outcome == "won") badges.Add("journey");
            if (SessionMode == "shift" && outcome == "won" && runBadges.Contains("form-switch")) badges.Add("shift");
            if (Daily != null && outcome == "won") badges.Add("daily");
            var earned = SoloProfile.RecordRun(new SoloProfile.RunEntry
            {
                date = DateTime.UtcNow.ToString("o"), mode = SessionMode, map = Round.Map,
                operation = SessionMode == "operations" ? (flow.Journey ? "journey" : Round.Operation?.Id) : null,
                difficulty = Round.Difficulty, outcome = outcome, score = Math.Max(0, score), day = Daily?.Day,
            }, badges);
            if (earned.Count > 0) hud.ShowToast("BADGE EARNED / " + string.Join(" / ", earned), 2.6f);
        }

        // ---- Crash replay ------------------------------------------------------------------------

        void CaptureReplay(bool force)
        {
            if (Round == null || (!force && Round.Tick % 12 != 0)) return;
            var frame = new SceneView();
            frame.Capture(Round);
            replay.Add(frame);
            while (replay.Count > 2 && replay[0].Elapsed < Round.Elapsed - 6) replay.RemoveAt(0);
        }

        void StartReplay(string label)
        {
            Phase = "replay";
            replayElapsed = 0;
            replayImpactShown = false;
            replayLabel.text = label;
            hud.Root.SetActive(false);
            replayBar.SetActive(true);
            SoloUi.Select(GameObject.Find("Skip Replay"));
        }

        void UpdateReplay(float dt)
        {
            const float duration = 6;
            replayElapsed += dt;
            float progress = Mathf.Min(1, replayElapsed / (duration - .45f)) * (replay.Count - 1);
            int index = Mathf.Min(replay.Count - 2, Mathf.FloorToInt(progress));
            replayScene.Interpolate(replay[index], replay[index + 1], progress - index);
            world.Render(replayScene, dt);
            if (!replayImpactShown && progress >= replay.Count - 1 - .001f)
            {
                replayImpactShown = true;
                foreach (var r in replay[replay.Count - 1].Riders)
                    if (r.IsPlayer) { world.Impact(r.X, r.Z); world.Derez(r.X, r.Z, r.Color, false); }
            }
            if (replayElapsed >= duration) CompleteReplay();
        }

        void CompleteReplay()
        {
            if (Phase != "replay") return;
            replayBar.SetActive(false);
            hud.Root.SetActive(true);
            ShowResult();
        }

        // ---- HUD -----------------------------------------------------------------------------------

        void UpdateHud()
        {
            if (Round == null) return;
            bool running = Phase == "running";
            hud.Sync(Round, SessionMode, running);
            int points = SessionMode == "operations" ? flow.Points + Round.Score
                : Match != null ? Match.Points + (Match.ResultRecorded ? 0 : Round.Score) : Round.Score;
            string mapName = SimMaps.Get(Round.Map).Name.ToUpperInvariant();
            if (SessionMode == "duel" && Match != null)
                hud.Matchline.text = (Daily != null ? "DAILY " + Daily.Day + " / " : "DUEL / ") + "YOU " + Match.PlayerWins + " - " + Match.RivalWins + " " +
                    GridRules.Rivals[Match.Rival].Id + " / R" + Match.RoundNumber + " / " + mapName;
            else if (SessionMode == "arena" && Match != null)
                hud.Matchline.text = "ARENA / HEAT " + Match.RoundNumber + "/3 / SURVIVE 2 / " + mapName;
            else if (SessionMode == "shift") hud.Matchline.text = "GRID SHIFT / ECHO / TWO FORMS / " + mapName;
            else if (SessionMode == "operations")
                hud.Matchline.text = "OPERATION / " + SimOperations.Get(Round.Operation.Id).Name.ToUpperInvariant() +
                    (flow.Journey ? " / " + (flow.Index + 1) + "/" + flow.Sequence.Count : "");
            else hud.Matchline.text = "LESSON / GRID BASICS";
            string third;
            if (SessionMode == "operations")
            {
                var op = Round.Operation;
                third = "PROGRESS " + (op.Progress > 0 ? op.Progress : op.PulseIndex > 0 ? op.PulseIndex : op.CollapseIndex).ToString("00");
            }
            else if (SessionMode == "shift")
            {
                Rider echo = null;
                foreach (var r in Round.Riders) if (!r.IsPlayer) echo = r;
                third = "ECHO HP " + Math.Max(0, echo?.Hp ?? 0).ToString("00");
            }
            else
            {
                int rivals = 0;
                foreach (var r in Round.Riders) if (!r.IsPlayer && r.Alive) rivals++;
                third = "RIVALS " + rivals.ToString("00");
            }
            hud.Score.text = "SCORE " + points.ToString("00000") + "\nBEST " + best.ToString("00000") + "\n" + third;
            if (SessionMode == "operations")
            {
                var mission = SimOperations.Get(Round.Operation.Id);
                var op = Round.Operation;
                string remaining = mission.HasTimeLimit ? " / " + Math.Max(0, Math.Ceiling(mission.TimeLimit - Round.Elapsed)) + "S LEFT" : "";
                string progress = mission.Kind == "checkpoints" ? "GATE " + Math.Min(op.Progress + 1, mission.Gates.Length) + "/" + mission.Gates.Length + remaining
                    : mission.Kind == "scanner" ? "SWEEPS " + op.PulseIndex + "/" + mission.Pulses.Length + remaining
                    : mission.Kind == "collapse" ? "RINGS " + op.CollapseIndex + "/" + mission.Steps.Length + remaining
                    : mission.Kind == "traversal" ? "SPANS " + op.Progress + "/" + mission.Gaps.Length + remaining
                    : "SENTINEL ACTIVE / ONE RIVAL";
                hud.SetObjective(true, mission.Name.ToUpperInvariant() + " / " + mission.Objective.ToUpperInvariant(), progress,
                    (string.IsNullOrEmpty(op.Warning) ? mission.HudTip : op.Warning).ToUpperInvariant());
            }
            else hud.SetObjective(false);
            if (SessionMode == "shift" && Round.Shift != null)
            {
                var player = Round.Player;
                Rider echo = null;
                foreach (var r in Round.Riders) if (!r.IsPlayer) echo = r;
                double left = Math.Max(0, Math.Ceiling(Round.Shift.TimeLimit - Round.Elapsed));
                hud.SetShiftPanel(true, "YOU " + Math.Max(0, player.Hp) + "/2 INTEGRITY  /  " + (player.Form == "cycle" ? "CYCLE" : "ON FOOT") +
                    "      ECHO " + Math.Max(0, echo.Hp) + "/2  /  " + echo.Form.ToUpperInvariant() + "      " + ((int)left / 60) + ":" + ((int)left % 60).ToString("00"));
                if (running) hud.Intent.text = "ECHO / " + Round.Shift.AiIntent.ToUpperInvariant();
            }
            else hud.SetShiftPanel(false);
        }

        // ---- Camera ---------------------------------------------------------------------------------

        void SnapCamera()
        {
            if (world.Player == null) return;
            Pose(world.Player, out var position, out var aim);
            view.transform.position = position;
            cameraAim = aim;
            view.transform.LookAt(aim);
        }

        void Pose(Transform player, out Vector3 position, out Vector3 aim)
        {
            if (world.PlayerOnFoot)
            {
                // North-up view on foot, matching the browser: arena directions never rotate.
                var p = player.position;
                position = new Vector3(p.x, 10.5f, p.z - 8f);
                aim = new Vector3(p.x, .6f, p.z + 1.5f);
                return;
            }
            ChaseCamera.Target(player.position, player.rotation, out position, out aim);
        }

        public void LateUpdateCamera()
        {
            if (Phase == "idle" || Phase == "select" || Phase == "setup" || Phase == "records" || world.Player == null) return;
            Transform player = world.Player;
            Pose(player, out var position, out var aim);
            float dt = Time.unscaledDeltaTime;
            if (Phase == "countdown" && cameraIntro > 0)
            {
                // Brief side-on arrival shot that settles behind the rider before launch.
                float duration = (flow != null && flow.Index > 0) || (Match != null && Match.RoundNumber > 1) ? 1.5f : 3f;
                float intro = Mathf.Clamp01((countdownRemaining - .45f) / (duration - .45f));
                var side = player.position + player.rotation * new Vector3(-6.5f, 3.2f, 2.5f);
                position = Vector3.Lerp(position, side, intro);
                aim = Vector3.Lerp(aim, player.position + Vector3.up * .8f, intro);
                view.transform.position = position;
                cameraAim = aim;
                view.transform.LookAt(aim);
                return;
            }
            if (Phase == "result" || Phase == "finishing")
            {
                position += Vector3.up * 2.5f;
                aim = player.position + Vector3.up * .6f;
            }
            float blend = 1 - Mathf.Exp(-6 * dt);
            view.transform.position = Vector3.Lerp(view.transform.position, position, blend);
            cameraAim = Vector3.Lerp(cameraAim, aim, blend);
            view.transform.LookAt(cameraAim);
        }
    }
}
