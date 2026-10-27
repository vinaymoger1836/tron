using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GridRun
{
    // Native client for the existing authoritative Node room API. No local match rules run here.
    public sealed class OnlineRoomClient : MonoBehaviour
    {
        [Serializable] public sealed class PlayerView { public string id, name; public bool connected; public int wins; }
        [Serializable] public sealed class RiderView
        {
            public string id, form, style, flightKind; public float x, z, nitroUntil; public float[] color; public int dir, ammo, hp; public bool alive, autoDrive, rocketReady;
            public float flightStartedAt, flightUntil, jetReadyAt, sailReadyAt, formReadyAt, discReadyAt, recoveryUntil, formChangedAt;
        }
        [Serializable] public sealed class DiscView { public string ownerId; public float x, z; public bool returning; }
        [Serializable] public sealed class ShiftView { public float timeLimit; public DiscView[] discs; }
        [Serializable] public sealed class BulletView { public string ownerId; public float x, z; public int vx, vz; public bool rocket; }
        [Serializable] public sealed class PickupView { public int id; public string type; public float x, z; public bool active; }
        // Pickup, pickup-spawn, rocket-explode, and arena-shrink events the server keeps for about a second.
        [Serializable] public sealed class EffectView { public int id; public string type, riderId, pickup; public float x, z, limit; }
        [Serializable] public sealed class SegmentView
        {
            public int id; public string ownerId; public float x1, z1, x2, z2, closedAt; public bool active;
        }
        [Serializable] public sealed class RoundView
        {
            public int tick; public float elapsed, limit, trailLifetime; public string map, rule;
            public RiderView[] riders; public SegmentView[] segments;
            public BulletView[] bullets; public PickupView[] pickups; public EffectView[] effects; public ShiftView shift;
        }
        // The server names riders whose seat was released, since they are no longer in the player list.
        [Serializable] public sealed class CrashView { public string riderId, reason, name; }
        [Serializable] public sealed class ResultView
        {
            public string winnerId, winnerName, matchWinnerId, matchWinnerName;
            public bool matchDraw; public CrashView[] crashes;
        }
        [Serializable] public sealed class RoomView
        {
            public string code, phase, selfId, hostId, map, mode, rule;
            public int maxPlayers, roundNumber, countdownMs, waitMs;
            public PlayerView[] players; public RoundView round; public ResultView result;
        }
        // Online protocol this build speaks; matches PROTOCOL in lan-server.mjs. Raise both together
        // when a change would break installed builds, so older apps are told to update.
        public const int Protocol = 1;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Verification only: lets a desktop check play the part of an outdated build.
        public static int SpokenProtocol = Protocol;
#else
        public const int SpokenProtocol = Protocol;
#endif
        [Serializable] private sealed class CreateBody
        { public string name, map = "classic", mode = "arena", rule = "classic"; public int maxPlayers = 2, protocol = SpokenProtocol; }
        [Serializable] private sealed class JoinBody { public string name; public int protocol = SpokenProtocol; }
        // One body for every room action; the server reads only the fields its type needs.
        [Serializable] public sealed class ActionBody
        { public string type, mode, map, rule, kind; public int sign; public bool held, enabled; public float x, z; }
        [Serializable] private sealed class EntryResult { public string code, playerId, error; }
        [Serializable] private sealed class ApiError { public string error; }
        [Serializable] private sealed class StatusResult { public bool available, online; public int maxPlayers, protocol, minProtocol, heartbeatMs, reconnectMs; }

        private readonly ConcurrentQueue<string> snapshots = new ConcurrentQueue<string>();
        private HttpClient client;
        private CancellationTokenSource streamCancellation;
        private HttpResponseMessage liveStream;
        private Uri origin;
        // Servers that send heartbeats report the interval; a stream silent for three of them is treated
        // as dead (a phone that changed networks can keep a socket that never errors). Zero: no watchdog.
        private int heartbeatMs;
        // Each stream loop owns one generation, so a loop ending late cannot clear a newer stream's state.
        private int generation;
        private bool suspended;
        private readonly System.Diagnostics.Stopwatch offline = new System.Diagnostics.Stopwatch();
        public string Code { get; private set; }
        public string SelfId { get; private set; }
        public RoomView View { get; private set; }
        public string Status { get; private set; } = "Choose Create or Join.";
        // Tracks the live stream itself: a snapshot queued before a drop must not mark the seat connected.
        private volatile bool streamOpen;
        public bool Connected => streamOpen && View != null;
        public bool Supported { get; private set; } = true;
        public bool Busy { get; private set; }
        public bool IsHost => View != null && View.selfId == View.hostId;
        // The room is gone or this seat was released; reconnecting cannot help.
        public bool Unavailable { get; private set; }
        // How long the server holds a dropped seat; servers from before it was reported held 15 s.
        public int HoldSeconds { get; private set; } = 15;
        public int OfflineSeconds => (int)(offline.ElapsedMilliseconds / 1000);

        private static Uri ValidateOrigin(string value)
        {
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps &&
                 !(uri.Scheme == Uri.UriSchemeHttp && (uri.Host == "localhost" || uri.Host == "127.0.0.1"))) ||
                uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException("Enter the game server HTTPS origin, with no path. Localhost may use HTTP.");
            return uri;
        }

        private void NewSession(Uri server)
        {
            CloseSession();
            origin = server;
            var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };
            client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        }

        private async Task<string> RequestAsync(string path, object body, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, new Uri(origin, path)))
            {
                if (body != null) request.Content = new StringContent(JsonUtility.ToJson(body), Encoding.UTF8, "application/json");
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token))
                {
                    string content = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        var error = JsonUtility.FromJson<ApiError>(content);
                        throw new InvalidOperationException(string.IsNullOrWhiteSpace(error?.error) ?
                            "Room request failed (" + (int)response.StatusCode + ")." : error.error);
                    }
                    return content;
                }
            }
        }

        private async Task<string> TimedRequestAsync(string path, object body)
        {
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                return await RequestAsync(path, body, deadline.Token);
        }

        public async Task CreateAsync(string server, string name, int seats = 2)
        {
            if (Busy) return;
            Busy = true;
            try
            {
                NewSession(ValidateOrigin(server));
                await CheckServerAsync();
                Status = "Creating room...";
                var entry = JsonUtility.FromJson<EntryResult>(await TimedRequestAsync("/api/rooms",
                    new CreateBody { name = name?.Trim(), maxPlayers = seats }));
                Enter(entry);
            }
            catch (Exception error) { Status = error.Message; CloseSession(); throw; }
            finally { Busy = false; }
        }

        public async Task JoinAsync(string server, string code, string name)
        {
            if (Busy) return;
            Busy = true;
            try
            {
                if (code == null || code.Length != 6 || !System.Text.RegularExpressions.Regex.IsMatch(code, "^[0-9]{6}$"))
                    throw new InvalidOperationException("Enter the six-digit room code.");
                NewSession(ValidateOrigin(server));
                await CheckServerAsync();
                Status = "Joining room...";
                var entry = JsonUtility.FromJson<EntryResult>(await TimedRequestAsync("/api/rooms/" + code + "/join",
                    new JoinBody { name = name?.Trim() }));
                Enter(entry);
            }
            catch (Exception error) { Status = error.Message; CloseSession(); throw; }
            finally { Busy = false; }
        }

        // Fails with a clear message before entering a room the two sides could not play together.
        // A server from before versioning reports no protocol; it speaks protocol 1.
        private async Task CheckServerAsync()
        {
            Status = "Checking game server...";
            var server = JsonUtility.FromJson<StatusResult>(await TimedRequestAsync("/api/lan/status", null));
            if (server == null || !server.available || !server.online)
                throw new InvalidOperationException("This server does not offer online numeric rooms.");
            int serverProtocol = server.protocol > 0 ? server.protocol : 1, minimum = server.minProtocol > 0 ? server.minProtocol : 1;
            if (SpokenProtocol < minimum)
                throw new InvalidOperationException("This app is too old for this game server. Update Grid Run to play online.");
            if (SpokenProtocol > serverProtocol)
                throw new InvalidOperationException("This game server is older than your app. Try again after the server is updated.");
            heartbeatMs = Math.Max(0, server.heartbeatMs);
            HoldSeconds = server.reconnectMs > 0 ? Mathf.CeilToInt(server.reconnectMs / 1000f) : 15;
        }

        private void Enter(EntryResult entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.code) || string.IsNullOrEmpty(entry.playerId))
                throw new InvalidOperationException("The room server returned an incomplete seat.");
            Code = entry.code;
            SelfId = entry.playerId;
            Status = "Connecting to room " + Code + "...";
            OpenStream();
        }

        private void OpenStream()
        {
            streamCancellation?.Dispose();
            streamCancellation = new CancellationTokenSource();
            _ = StreamLoopAsync(streamCancellation.Token, ++generation);
        }

        // The app went to the background: close the stream so the server holds the heat for everyone
        // instead of letting this rider drive blind. Resume reconnects to the same seat.
        public void Suspend()
        {
            if (string.IsNullOrEmpty(Code) || suspended || Unavailable) return;
            suspended = true;
            streamCancellation?.Cancel();
            liveStream?.Dispose();
            streamOpen = false;
            if (!offline.IsRunning) offline.Restart();
            Status = "Paused in the background. Reconnecting when you return.";
        }

        public void Resume()
        {
            if (string.IsNullOrEmpty(Code) || !suspended || client == null) return;
            suspended = false;
            Status = "Reconnecting to room " + Code + "...";
            OpenStream();
        }

        public Task<string> SendAsync(string type, int sign = 0, bool held = false, bool enabled = false, string map = null, string rule = null) =>
            SendAsync(new ActionBody { type = type, sign = sign, held = held, enabled = enabled, map = map, rule = rule });

        // Returns null on success, or the server's reason (for example why FORM or DISC was refused).
        public async Task<string> SendAsync(ActionBody body)
        {
            if (string.IsNullOrEmpty(Code) || client == null) return "Not in a room.";
            try
            {
                await TimedRequestAsync("/api/rooms/" + Code + "/action", body);
                return null;
            }
            catch (Exception error) { Status = error.Message; return error.Message; }
        }

        public async Task LeaveAsync()
        {
            if (string.IsNullOrEmpty(Code) || client == null) { CloseSession(); return; }
            string oldCode = Code;
            try { await TimedRequestAsync("/api/rooms/" + oldCode + "/leave", new ActionBody()); }
            catch { /* Local exit still succeeds if the server is unavailable. */ }
            CloseSession();
            Status = "Left the room.";
        }

        private async Task StreamLoopAsync(CancellationToken token, int loop)
        {
            string path = "/api/rooms/" + Code + "/stream";
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, path)))
                    using (var connecting = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        // A server that accepts the connection but never answers must not stall reconnection.
                        connecting.CancelAfter(TimeSpan.FromSeconds(8));
                        HttpResponseMessage response;
                        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connecting.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("The room server did not answer."); }
                        using (response)
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Forbidden)
                                {
                                    string reason = null;
                                    try { reason = JsonUtility.FromJson<ApiError>(await response.Content.ReadAsStringAsync().ConfigureAwait(false))?.error; }
                                    catch { /* Fall back to the generic message. */ }
                                    if (loop != generation) return;
                                    Unavailable = true;
                                    Status = string.IsNullOrWhiteSpace(reason) ? "This room is no longer available." : reason;
                                    return;
                                }
                                throw new IOException("Room stream returned " + (int)response.StatusCode + ".");
                            }
                            liveStream = response;
                            streamOpen = true;
                            offline.Reset();
                            Status = "Connected to room.";
                            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var reader = new StreamReader(stream))
                            {
                                int silence = heartbeatMs > 0 ? heartbeatMs * 3 : Timeout.Infinite;
                                while (!token.IsCancellationRequested)
                                {
                                    var read = reader.ReadLineAsync();
                                    if (silence != Timeout.Infinite)
                                        using (var quiet = CancellationTokenSource.CreateLinkedTokenSource(token))
                                        {
                                            var first = await Task.WhenAny(read, Task.Delay(silence, quiet.Token)).ConfigureAwait(false);
                                            quiet.Cancel();
                                            if (first != read)
                                            {
                                                if (token.IsCancellationRequested) break;
                                                throw new IOException("The room stream went silent.");
                                            }
                                        }
                                    string line = await read.ConfigureAwait(false);
                                    if (line == null) throw new IOException("Room stream closed.");
                                    if (line.Length > 0) snapshots.Enqueue(line);
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception error)
                {
                    if (token.IsCancellationRequested) break;
                    // The reason goes to the log; players see one plain message.
                    Debug.Log("Online stream interrupted: " + error.Message);
                    if (loop == generation) Status = "Connection interrupted. Reconnecting...";
                }
                finally
                {
                    if (loop == generation)
                    {
                        liveStream = null; streamOpen = false;
                        if (!token.IsCancellationRequested && !offline.IsRunning) offline.Restart();
                    }
                }
                try { await Task.Delay(1000, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        private void Update()
        {
            // Apply snapshots on Unity's main thread; drop superseded frames after a stall.
            string newest = null;
            while (snapshots.TryDequeue(out var line)) newest = line;
            if (newest == null) return;
            try
            {
                var view = JsonUtility.FromJson<RoomView>(newest);
                if (view == null || view.code != Code || view.roundNumber < 0 || view.players == null)
                    throw new InvalidOperationException("Invalid room snapshot.");
                // JsonUtility turns "round": null into an empty RoundView; a real round always names its map.
                if (view.round != null && string.IsNullOrEmpty(view.round.map)) view.round = null;
                View = view;
                Supported = (view.mode == "arena" || view.mode == "duel" || view.mode == "shift") && Array.IndexOf(Sim.SimMaps.Order, view.map) >= 0 &&
                    view.maxPlayers >= 2 && view.maxPlayers <= 4 && (view.rule == "classic" || view.rule == "decay");
                Status = !Supported ? "This room uses a mode the app does not support online yet. Online play supports Arena, Duel, and Grid Shift for 2-4 riders on any map." :
                    !streamOpen ? Status :
                    view.phase == "connection-wait" ? "Waiting for a rider to reconnect..." : "Connected to room " + Code + ".";
            }
            catch (Exception error) { Status = "Room data error: " + error.Message; }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void BreakStreamForVerification() { liveStream?.Dispose(); }
#endif

        private void CloseSession()
        {
            streamCancellation?.Cancel();
            liveStream?.Dispose();
            streamCancellation?.Dispose();
            streamCancellation = null; liveStream = null;
            client?.Dispose(); client = null;
            Code = SelfId = null; View = null; streamOpen = false; Supported = true;
            Unavailable = suspended = false; heartbeatMs = 0; offline.Reset(); generation++;
            while (snapshots.TryDequeue(out _)) { }
        }
        private void OnDestroy() { CloseSession(); }
    }
}
