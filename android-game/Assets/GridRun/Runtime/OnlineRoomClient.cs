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
            public string id, form; public float x, z; public int dir, ammo; public bool alive, autoDrive, rocketReady;
        }
        [Serializable] public sealed class SegmentView
        {
            public int id; public string ownerId; public float x1, z1, x2, z2; public bool active;
        }
        [Serializable] public sealed class RoundView
        {
            public int tick; public float elapsed, limit; public string map;
            public RiderView[] riders; public SegmentView[] segments;
        }
        [Serializable] public sealed class CrashView { public string riderId, reason; }
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
        [Serializable] private sealed class CreateBody
        { public string name, map = "classic", mode = "arena", rule = "classic"; public int maxPlayers = 2; }
        [Serializable] private sealed class JoinBody { public string name; }
        [Serializable] private sealed class ActionBody
        { public string type, mode, map, rule; public int sign; public bool held, enabled; }
        [Serializable] private sealed class EntryResult { public string code, playerId, error; }
        [Serializable] private sealed class ApiError { public string error; }
        [Serializable] private sealed class StatusResult { public bool available, online; public int maxPlayers; }

        private readonly ConcurrentQueue<string> snapshots = new ConcurrentQueue<string>();
        private HttpClient client;
        private CancellationTokenSource streamCancellation;
        private HttpResponseMessage liveStream;
        private Uri origin;
        public string Code { get; private set; }
        public string SelfId { get; private set; }
        public RoomView View { get; private set; }
        public string Status { get; private set; } = "Choose Create or Join.";
        public bool Connected { get; private set; }
        public bool Supported { get; private set; } = true;
        public bool Busy { get; private set; }
        public bool IsHost => View != null && View.selfId == View.hostId;

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
                var target = ValidateOrigin(server);
                NewSession(target);
                Status = "Checking game server...";
                var availability = JsonUtility.FromJson<StatusResult>(await TimedRequestAsync("/api/lan/status", null));
                if (availability == null || !availability.available || !availability.online)
                    throw new InvalidOperationException("This server does not offer online numeric rooms.");
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
                Status = "Joining room...";
                var entry = JsonUtility.FromJson<EntryResult>(await TimedRequestAsync("/api/rooms/" + code + "/join",
                    new JoinBody { name = name?.Trim() }));
                Enter(entry);
            }
            catch (Exception error) { Status = error.Message; CloseSession(); throw; }
            finally { Busy = false; }
        }

        private void Enter(EntryResult entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.code) || string.IsNullOrEmpty(entry.playerId))
                throw new InvalidOperationException("The room server returned an incomplete seat.");
            Code = entry.code;
            SelfId = entry.playerId;
            Status = "Connecting to room " + Code + "...";
            streamCancellation = new CancellationTokenSource();
            _ = StreamLoopAsync(streamCancellation.Token);
        }

        public async Task SendAsync(string type, int sign = 0, bool held = false, bool enabled = false)
        {
            if (string.IsNullOrEmpty(Code) || client == null) return;
            try
            {
                await TimedRequestAsync("/api/rooms/" + Code + "/action",
                    new ActionBody { type = type, sign = sign, held = held, enabled = enabled });
            }
            catch (Exception error) { Status = error.Message; }
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

        private async Task StreamLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "/api/rooms/" + Code + "/stream")))
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Forbidden)
                            {
                                Status = "Room expired or this seat is unavailable.";
                                return;
                            }
                            throw new IOException("Room stream returned " + (int)response.StatusCode + ".");
                        }
                        liveStream = response;
                        Status = "Connected to room.";
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var reader = new StreamReader(stream))
                        {
                            while (!token.IsCancellationRequested)
                            {
                                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                                if (line == null) throw new IOException("Room stream closed.");
                                if (line.Length > 0) snapshots.Enqueue(line);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException)
                {
                    if (token.IsCancellationRequested) break;
                    Status = "Connection interrupted. Reconnecting...";
                }
                catch (Exception error) { Status = "Connection interrupted. Reconnecting: " + error.Message; }
                finally { liveStream = null; Connected = false; }
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
                View = view;
                Connected = true;
                Supported = view.mode == "arena" && view.map == "classic" && view.maxPlayers == 2 && view.rule == "classic";
                Status = !Supported ? "This preview supports only two-rider Arena on Classic Grid with persistent walls." :
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
            Code = SelfId = null; View = null; Connected = false; Supported = true;
            while (snapshots.TryDequeue(out _)) { }
        }
        private void OnDestroy() { CloseSession(); }
    }
}
