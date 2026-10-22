#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GridRun
{
    // Opt-in art review capture (--look-capture-dir). Saves screenshots of every scene's
    // presentation; it asserts nothing about gameplay, which the phase verifiers cover.
    public sealed class DevelopmentLookCapture : MonoBehaviour
    {
        private string output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfRequested()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--look-capture-dir");
            if (index < 0 || index + 1 >= args.Length) return;
            var runner = new GameObject("Look capture").AddComponent<DevelopmentLookCapture>();
            DontDestroyOnLoad(runner.gameObject);
            runner.output = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(runner.output);
            Application.runInBackground = true;
            runner.StartCoroutine(runner.Run());
        }

        private IEnumerator Shot(string file, int width = 1280, int height = 720)
        {
            if (Screen.width != width || Screen.height != height)
            {
                Screen.SetResolution(width, height, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(.5f);
            }
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(output, file), texture.EncodeToPNG());
            Destroy(texture);
            Debug.Log("LOOK_CAPTURE " + file);
        }

        private static GameObject Control(string name)
        {
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (button.name == name) return button.gameObject;
            throw new InvalidOperationException("Missing control: " + name);
        }

        private static void Submit(string name) =>
            ExecuteEvents.Execute(Control(name), new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);

        private static void Press(string name) =>
            ExecuteEvents.Execute(Control(name), new PointerEventData(EventSystem.current), ExecuteEvents.pointerDownHandler);

        private static IEnumerator Hero(Behaviour game, Transform subject, Vector3 offset, string file, DevelopmentLookCapture self)
        {
            game.enabled = false;
            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (var canvas in canvases) canvas.enabled = false;
            var camera = Camera.main.transform;
            Vector3 position = camera.position;
            Quaternion rotation = camera.rotation;
            camera.position = subject.position + subject.rotation * offset * subject.lossyScale.y;
            camera.LookAt(subject.position + Vector3.up * .7f * subject.lossyScale.y);
            yield return self.Shot(file);
            camera.SetPositionAndRotation(position, rotation);
            foreach (var canvas in canvases) canvas.enabled = true;
            game.enabled = true;
        }

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Shot("debug-warmup.png", 1170, 540);
            yield return Shot("look-showroom-desktop.png");
            yield return Shot("look-showroom-phone.png", 1170, 540);
            Submit("Sets");
            Submit("Next Style");
            yield return Shot("look-showroom-solar.png");

            SceneManager.LoadScene("PhaseOneArena");
            yield return new WaitForSecondsRealtime(.8f);
            yield return Shot("look-arena-landing.png");
            var arena = FindFirstObjectByType<ArenaGame>();
            arena.StartMatch();
            yield return new WaitForSecondsRealtime(1.3f);
            Press("Left");
            yield return new WaitForSecondsRealtime(1.1f);
            Press("Right");
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("look-arena-play.png");
            if (Shader.GetGlobalTexture("_GridReflectionTex") is RenderTexture reflection)
            {
                var previous = RenderTexture.active;
                RenderTexture.active = reflection;
                var copy = new Texture2D(reflection.width, reflection.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, reflection.width, reflection.height), 0, 0);
                copy.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(Path.Combine(output, "debug-reflection.png"), copy.EncodeToPNG());
                Destroy(copy);
            }
            arena.TogglePause();
            var main = Camera.main;
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = false;
            main.GetComponent<PlanarReflection>().enabled = false;
            yield return Shot("debug-no-reflection.png");
            main.GetComponent<PlanarReflection>().enabled = true;
            var cameraData = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(main);
            cameraData.renderPostProcessing = false;
            yield return Shot("debug-no-post.png");
            cameraData.renderPostProcessing = true;
            var trailRenderers = new System.Collections.Generic.List<Renderer>();
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (renderer.name.Contains("trail")) { renderer.enabled = false; trailRenderers.Add(renderer); }
            yield return Shot("debug-no-trails.png");
            foreach (var renderer in trailRenderers) renderer.enabled = true;
            var floor = GameObject.Find("Grid floor");
            floor.SetActive(false);
            yield return Shot("debug-no-floor.png");
            floor.SetActive(true);
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = true;
            var player = GameObject.Find("Player cycle").transform;
            Debug.Log("LOOK_PLAYER " + player.position.ToString("F2") + " camera " + Camera.main.transform.position.ToString("F2"));
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (renderer.name.Contains("trail"))
                    Debug.Log("LOOK_TRAIL " + renderer.name + " mat=" + renderer.sharedMaterial?.name +
                        " center=" + renderer.bounds.center.ToString("F2") + " size=" + renderer.bounds.size.ToString("F2"));
            yield return Hero(arena, player, new Vector3(3.2f, 1.6f, 3.6f), "look-arena-hero-front.png", this);
            yield return Hero(arena, player, new Vector3(-4.5f, 2.4f, -5.5f), "look-arena-hero-rear.png", this);
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (renderer.name == "Light trail" && renderer.sharedMaterial == GridLook.Trail(true))
                {
                    var marker = new GameObject("Trail probe").transform;
                    marker.position = renderer.bounds.center - Vector3.up * .55f;
                    yield return Hero(arena, marker, new Vector3(2.5f, 3.5f, -4f), "debug-trail-close.png", this);
                    Destroy(marker.gameObject);
                    break;
                }
            var rival = GameObject.Find("Rival cycle").transform;
            yield return Hero(arena, rival, new Vector3(4f, 1.2f, -.5f), "look-arena-hero-rival.png", this);
            arena.TogglePause();
            yield return new WaitForSecondsRealtime(.3f);
            yield return Shot("look-arena-play-phone.png", 1170, 540);
            GridLook.HighQuality = false;
            GridLook.ApplyQuality(Camera.main);
            yield return Shot("look-arena-low.png");
            GridLook.HighQuality = true;
            GridLook.ApplyQuality(Camera.main);

            SceneManager.LoadScene("PhaseThreeTransit");
            yield return new WaitForSecondsRealtime(.8f);
            yield return Shot("look-transit-landing.png");
            Submit("Start Transit");
            yield return new WaitForSecondsRealtime(.4f);
            Submit("Light Jet");
            yield return new WaitForSecondsRealtime(.35f);
            yield return Shot("look-transit-jet.png");
            var transit = FindFirstObjectByType<TransitGame>();
            var cycle = GameObject.Find("Transit cycle").transform;
            transit.TogglePause();
            yield return Hero(transit, cycle, new Vector3(5f, 1.5f, -2f), "look-transit-jet-hero.png", this);
            transit.TogglePause();
            transit.StartCourse();
            yield return new WaitForSecondsRealtime(.4f);
            Submit("Solar Sail");
            yield return new WaitForSecondsRealtime(.55f);
            yield return Shot("look-transit-sail.png");
            transit.TogglePause();
            yield return Hero(transit, cycle, new Vector3(4.5f, 2.5f, -4f), "look-transit-sail-hero.png", this);
            Application.Quit(0);
        }
    }
}
#endif
