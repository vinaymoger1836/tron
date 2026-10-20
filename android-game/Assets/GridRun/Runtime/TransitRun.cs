using System;

namespace GridRun
{
    // Fixed-step rules for the straight Solar Transit course. Rendering and input stay outside this class.
    public sealed class TransitRun
    {
        public enum State { Ready, Running, Crashed, TimedOut, Won }
        public enum Flight { None, Jet, Sail }

        public const float StartZ = -22f;
        public const float FinishZ = 32f;
        public const float TimeLimit = 12f;
        public const float Speed = 10.6f;
        public const float RiderRadius = .3f;
        public const float JetGapStart = -15f;
        public const float JetGapEnd = -10f;
        public const float SailGapStart = 2f;
        public const float SailGapEnd = 12f;

        public State Outcome { get; private set; } = State.Ready;
        public Flight ActiveFlight { get; private set; }
        public string Failure { get; private set; } = "";
        public float Z { get; private set; } = StartZ;
        public float Elapsed { get; private set; }
        public float FlightStartedAt { get; private set; }
        public float FlightEndsAt { get; private set; }
        public float JetReadyAt { get; private set; }
        public float SailReadyAt { get; private set; }
        public int GapsCleared { get; private set; }

        public void Start()
        {
            Outcome = State.Running;
            ActiveFlight = Flight.None;
            Failure = "";
            Z = StartZ;
            Elapsed = FlightStartedAt = FlightEndsAt = JetReadyAt = SailReadyAt = 0;
            GapsCleared = 0;
        }

        public bool Launch(Flight kind)
        {
            if (Outcome != State.Running || ActiveFlight != Flight.None || kind == Flight.None) return false;
            float readyAt = kind == Flight.Jet ? JetReadyAt : SailReadyAt;
            if (Elapsed < readyAt) return false;
            ActiveFlight = kind;
            FlightStartedAt = Elapsed;
            FlightEndsAt = Elapsed + (kind == Flight.Jet ? .8f : 2f);
            if (kind == Flight.Jet) JetReadyAt = Elapsed + 5f;
            else SailReadyAt = Elapsed + 7f;
            return true;
        }

        public void Step(float dt, bool moving)
        {
            if (Outcome != State.Running || dt <= 0) return;
            Elapsed += dt;
            if (Elapsed >= FlightEndsAt) ActiveFlight = Flight.None;
            if (moving)
            {
                float speedMultiplier = ActiveFlight == Flight.Jet ? 1.7f :
                    ActiveFlight == Flight.Sail ? 1.22f : 1f;
                float nextZ = Z + Speed * speedMultiplier * dt;
                if (TouchesGap(Z, nextZ, JetGapStart, JetGapEnd) && ActiveFlight != Flight.Jet)
                    Crash("JET GAP: USE LIGHT JET BEFORE THE EDGE");
                else if (TouchesGap(Z, nextZ, SailGapStart, SailGapEnd) && ActiveFlight != Flight.Sail)
                    Crash("SAIL GAP: USE SOLAR SAIL BEFORE THE EDGE");
                else Z = nextZ;
            }
            if (Outcome != State.Running) return;
            if (Z > JetGapEnd + RiderRadius) GapsCleared = Math.Max(GapsCleared, 1);
            if (Z > SailGapEnd + RiderRadius) GapsCleared = 2;
            if (GapsCleared == 2 && Z >= FinishZ) Outcome = State.Won;
            else if (Elapsed >= TimeLimit)
            {
                Outcome = State.TimedOut;
                Failure = "TIME EXPIRED BEFORE THE FINISH";
            }
        }

        private static bool TouchesGap(float from, float to, float start, float end)
        {
            return to + RiderRadius >= start && from - RiderRadius <= end;
        }

        private void Crash(string reason)
        {
            Outcome = State.Crashed;
            Failure = reason;
        }

        public float FlightFraction =>
            ActiveFlight == Flight.None ? 0 : Math.Max(0, Math.Min(1,
                (Elapsed - FlightStartedAt) / (FlightEndsAt - FlightStartedAt)));
        public float JetCooldown => Math.Max(0, JetReadyAt - Elapsed);
        public float SailCooldown => Math.Max(0, SailReadyAt - Elapsed);
    }
}
