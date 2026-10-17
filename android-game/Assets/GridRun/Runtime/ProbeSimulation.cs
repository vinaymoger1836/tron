using System;

namespace GridRun
{
    // Phase 0 movement probe only. Full browser rule parity belongs to Phase 1.
    // No Unity dependencies: the fixed-step rules can later run outside presentation.
    public sealed class ProbeSimulation
    {
        public const float ArenaLimit = 48f;
        public const float Speed = 12f;
        public float X { get; private set; }
        public float Z { get; private set; }
        public int Heading { get; private set; }
        public bool Crashed { get; private set; }
        public float Elapsed { get; private set; }
        private float turnCooldown;

        public void Reset()
        {
            X = 0; Z = -20; Heading = 0; Elapsed = 0;
            Crashed = false; turnCooldown = 0;
        }

        public void Turn(int direction)
        {
            if (Crashed || turnCooldown > 0 || direction == 0) return;
            Heading = (Heading + (direction < 0 ? 3 : 1)) % 4;
            turnCooldown = .12f;
        }

        public void Step(float seconds, bool forward)
        {
            if (Crashed || seconds <= 0) return;
            Elapsed += seconds;
            turnCooldown = Math.Max(0, turnCooldown - seconds);
            if (!forward) return;
            float distance = Speed * seconds;
            if (Heading == 0) Z += distance;
            else if (Heading == 1) X += distance;
            else if (Heading == 2) Z -= distance;
            else X -= distance;
            if (Math.Abs(X) > ArenaLimit || Math.Abs(Z) > ArenaLimit)
            {
                X = Math.Max(-ArenaLimit, Math.Min(ArenaLimit, X));
                Z = Math.Max(-ArenaLimit, Math.Min(ArenaLimit, Z));
                Crashed = true;
            }
        }
    }
}
