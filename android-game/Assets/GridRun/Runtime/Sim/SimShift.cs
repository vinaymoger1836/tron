using System;
using System.Collections.Generic;

namespace GridRun.Sim
{
    // Port of dist/grid-shift.js: cycle and on-foot forms, returning disc, Echo AI.
    public static class SimShift
    {
        const double EPS = 1e-8;
        const double SPEED_CYCLE = 10.6, SPEED_FOOT = 5.2, SPEED_DISC = 22, SPEED_BULLET = 48;
        const double RADIUS_CYCLE = 0.36, RADIUS_FOOT = 0.28;
        public const double TIME_LIMIT = GridRules.ROUND_END;
        const double FORM_COOLDOWN = 1.15, FORM_RECOVERY = 0.36, DISC_COOLDOWN = 1.45;
        const double FIRE_INTERVAL = .12, BULLET_RANGE = 20;
        const int ROUND_AMMO = 24;
        static readonly int[][] Dir = GridRules.Dir;

        static double Radius(Rider rider) => rider.Form == "foot" ? RADIUS_FOOT : RADIUS_CYCLE;
        static double Speed(Rider rider) => rider.Form == "foot" ? SPEED_FOOT : SPEED_CYCLE;
        static double Random(Round round) => GridRules.NextRandom(round);

        static double? BoxHit(double x, double z, double dx, double dz, double minX, double maxX, double minZ, double maxZ) =>
            GridRules.SweptBox(x, z, dx, dz, minX, maxX, minZ, maxZ);

        static GridRules.Hit WorldHit(Round round, Rider rider, double x, double z, double dx, double dz, double radius, int? skipId = null)
        {
            double bound = round.Limit - radius;
            GridRules.Hit hit = null;
            if (Math.Abs(x) > bound || Math.Abs(z) > bound) hit = new GridRules.Hit { T = 0, Reason = "boundary" };
            else
            {
                for (int axis = 0; axis < 2; axis++)
                {
                    double p = axis == 0 ? x : z, d = axis == 0 ? dx : dz;
                    double? t = d > EPS && p + d > bound ? (bound - p) / d
                        : d < -EPS && p + d < -bound ? (-bound - p) / d : (double?)null;
                    if (t.HasValue && (hit == null || t.Value < hit.T)) hit = new GridRules.Hit { T = t.Value, Reason = "boundary" };
                }
            }
            foreach (var wall in round.Obstacles)
            {
                var t = wall.Circle
                    ? GridRules.SweptCircle(x, z, dx, dz, wall.X, wall.Z, wall.Radius + radius)
                    : BoxHit(x, z, dx, dz, wall.MinX - radius, wall.MaxX + radius, wall.MinZ - radius, wall.MaxZ + radius);
                if (t.HasValue && (hit == null || t.Value < hit.T)) hit = new GridRules.Hit { T = t.Value, Reason = "obstacle", By = wall.Id };
            }
            foreach (var segment in round.Segments)
            {
                if (GridRules.Airborne(round, rider)) break;
                if (rider != null && segment == rider.Segment && rider.Form == "cycle") continue;
                if ((skipId.HasValue && segment.Id == skipId.Value) ||
                    (rider != null && rider.IgnoreSegmentId.HasValue && segment.Id == rider.IgnoreSegmentId.Value && rider.DistanceSinceTurn < 0.72))
                    continue;
                var t = BoxHit(x, z, dx, dz,
                    Math.Min(segment.X1, segment.X2) - radius - .09, Math.Max(segment.X1, segment.X2) + radius + .09,
                    Math.Min(segment.Z1, segment.Z2) - radius - .09, Math.Max(segment.Z1, segment.Z2) + radius + .09);
                if (t.HasValue && (hit == null || t.Value < hit.T))
                    hit = new GridRules.Hit { T = t.Value, Reason = "trail", By = segment.OwnerId, Segment = segment };
            }
            return hit;
        }

        static double? ActorHit(Rider a, Rider b, double dxA, double dzA, double dxB, double dzB)
        {
            double px = a.X - b.X, pz = a.Z - b.Z;
            double vx = dxA - dxB, vz = dzA - dzB;
            double radius = Radius(a) + Radius(b);
            double c = px * px + pz * pz - radius * radius;
            if (c <= 0) return 0;
            double aa = vx * vx + vz * vz;
            if (aa < EPS) return null;
            double bb = 2 * (px * vx + pz * vz);
            double det = bb * bb - 4 * aa * c;
            if (det < 0) return null;
            double t = (-bb - Math.Sqrt(det)) / (2 * aa);
            return t >= 0 && t <= 1 ? t : (double?)null;
        }

        static void CloseTrail(Round round, Rider rider)
        {
            if (rider.Segment == null || !rider.Segment.Active) return;
            rider.Segment.Active = false;
            rider.Segment.ClosedAt = round.Elapsed;
        }

        static void OpenTrail(Round round, Rider rider)
        {
            var segment = new Segment
            {
                Id = round.NextSegmentId++, OwnerId = rider.Id, Rider = rider,
                X1 = rider.X, Z1 = rider.Z, X2 = rider.X, Z2 = rider.Z, Active = true, ClosedAt = -1,
            };
            rider.Segment = segment;
            round.Segments.Add(segment);
        }

        static bool SafeMount(Round round, Rider rider)
        {
            int[] v = Dir[rider.Dir];
            if (WorldHit(round, rider, rider.X, rider.Z, v[0] * 1.25, v[1] * 1.25, RADIUS_CYCLE) != null) return false;
            foreach (var other in round.Riders)
                if (other != rider && other.Alive && !(JsMath.Hypot(other.X - rider.X, other.Z - rider.Z) > 1.55)) return false;
            return true;
        }

        public static Round CreateShiftRound(uint seed, string difficulty = "standard", string map = "classic")
        {
            var layout = SimMaps.Get(map);
            var riders = new[]
            {
                new RiderSpec("YOU", layout.Duel[0], true, "none"),
                new RiderSpec("ECHO", layout.Duel[1], false, "none", "hunter", new[] { 1f, .47f, .18f }),
            };
            var round = GridRules.CreateRound(seed, riders, difficulty, layout.Id);
            round.Shift = new ShiftState { TimeLimit = TIME_LIMIT };
            foreach (var rider in round.Riders)
            {
                rider.Form = "cycle";
                rider.Hp = 2;
                rider.FormReadyAt = 0;
                rider.RecoveryUntil = 0;
                rider.DiscReadyAt = 0;
                rider.Ammo = ROUND_AMMO;
                rider.NextShotAt = 0;
                rider.FireHeld = false;
                rider.FormChangedAt = -10;
                rider.InputX = 0; rider.InputZ = 0;
                rider.FootTime = 0;
                rider.AiNextAt = .28 + Random(round) * .28;
                var tactic = new ShiftTactic();
                tactic.Aggression = .35 + Random(round) * .5;
                tactic.FootAt = .75 + Random(round) * .8;
                tactic.PreferCycle = Random(round) < .36;
                tactic.Dodge = .35 + Random(round) * .45;
                tactic.Strafe = Random(round) < .5 ? -1 : 1;
                rider.Tactic = tactic;
            }
            return round;
        }

        public static bool SetMove(Round round, string id, double x, double z)
        {
            var rider = round.Find(id);
            if (rider == null || !rider.Alive || round.Shift == null) return false;
            double length = JsMath.Hypot(x, z);
            if (length > 1) { rider.InputX = x / length; rider.InputZ = z / length; }
            else { rider.InputX = x; rider.InputZ = z; }
            return true;
        }

        public static bool QueueTurn(Round round, string id, int sign)
        {
            var rider = round.Find(id);
            if (rider == null || !rider.Alive || rider.Form != "cycle" || round.Outcome != null || (sign != -1 && sign != 1)) return false;
            rider.PendingTurn = new PendingTurn { Sign = sign, ExpiresAt = round.Elapsed + .22 };
            round.ActionCount++;
            return true;
        }

        public static bool RequestForm(Round round, string id)
        {
            var rider = round.Find(id);
            if (rider == null || !rider.Alive || round.Outcome != null || rider.FlightKind != null) return false;
            if (round.Elapsed < rider.FormReadyAt || round.Elapsed < rider.RecoveryUntil)
            {
                round.Events.Add(new SimEvent { Type = "shift-denied", RiderId = id, Reason = "Form system recharging" });
                return false;
            }
            if (rider.Form == "cycle")
            {
                int[] v = Dir[rider.Dir];
                double dx = v[0] * .82, dz = v[1] * .82;
                if (WorldHit(round, rider, rider.X, rider.Z, dx, dz, RADIUS_FOOT, rider.Segment.Id) != null)
                {
                    round.Events.Add(new SimEvent { Type = "shift-denied", RiderId = id, Reason = "No clear dismount lane" });
                    return false;
                }
                foreach (var other in round.Riders)
                    if (other != rider && other.Alive && JsMath.Hypot(other.X - (rider.X + dx), other.Z - (rider.Z + dz)) < 1)
                    {
                        round.Events.Add(new SimEvent { Type = "shift-denied", RiderId = id, Reason = "Rival blocks dismount" });
                        return false;
                    }
                CloseTrail(round, rider);
                rider.FireHeld = false;
                rider.X += dx; rider.Z += dz;
                rider.Form = "foot";
                rider.Segment = null;
                rider.FootTime = 0;
                rider.IgnoreSegmentId = null;
            }
            else
            {
                foreach (var disc in round.Shift.Discs)
                    if (disc.OwnerId == id)
                    {
                        round.Events.Add(new SimEvent { Type = "shift-denied", RiderId = id, Reason = "Recall your disc before mounting" });
                        return false;
                    }
                if (!SafeMount(round, rider))
                {
                    round.Events.Add(new SimEvent { Type = "shift-denied", RiderId = id, Reason = "No clear space to mount" });
                    return false;
                }
                rider.Form = "cycle";
                rider.InputX = 0; rider.InputZ = 0;
                OpenTrail(round, rider);
            }
            rider.FormChangedAt = round.Elapsed;
            rider.FormReadyAt = round.Elapsed + FORM_COOLDOWN;
            rider.RecoveryUntil = round.Elapsed + FORM_RECOVERY;
            round.Events.Add(new SimEvent { Type = "shift-form", RiderId = id, Form = rider.Form, X = rider.X, Z = rider.Z });
            round.ActionCount++;
            return true;
        }

        public static bool ThrowDisc(Round round, string id)
        {
            var rider = round.Find(id);
            if (rider == null || !rider.Alive || rider.Form != "foot" || round.Outcome != null ||
                round.Elapsed < rider.DiscReadyAt || round.Elapsed < rider.RecoveryUntil) return false;
            foreach (var d in round.Shift.Discs) if (d.OwnerId == id) return false;
            Rider target = null;
            double targetDistance = 0;
            foreach (var other in round.Riders)
            {
                if (other == rider || !other.Alive) continue;
                double distance = JsMath.Hypot(other.X - rider.X, other.Z - rider.Z);
                if (target == null || distance < targetDistance) { target = other; targetDistance = distance; }
            }
            double vx = target != null ? target.X - rider.X : Dir[rider.Dir][0];
            double vz = target != null ? target.Z - rider.Z : Dir[rider.Dir][1];
            double length = JsMath.Hypot(vx, vz);
            if (length == 0) length = 1;
            round.Shift.Discs.Add(new Disc { OwnerId = id, X = rider.X, Z = rider.Z, Vx = vx / length, Vz = vz / length });
            rider.DiscReadyAt = round.Elapsed + DISC_COOLDOWN;
            round.Events.Add(new SimEvent { Type = "disc-throw", RiderId = id });
            round.ActionCount++;
            return true;
        }

        static bool ShootBullet(Round round, Rider rider)
        {
            if (!rider.Alive || rider.Form != "cycle" || round.Outcome != null || round.Elapsed < rider.RecoveryUntil ||
                (rider.Ammo <= 0 && !rider.RocketReady) || round.Elapsed + EPS < rider.NextShotAt) return false;
            int[] v = Dir[rider.Dir];
            var bullet = new Bullet { OwnerId = rider.Id, X = rider.X + v[0] * .8, Z = rider.Z + v[1] * .8, Vx = v[0], Vz = v[1], Rocket = rider.RocketReady };
            round.Shift.Bullets.Add(bullet);
            if (rider.RocketReady) rider.RocketReady = false;
            else rider.Ammo--;
            rider.NextShotAt = round.Elapsed + FIRE_INTERVAL;
            if (rider.Ammo == 0) rider.FireHeld = false;
            round.Events.Add(new SimEvent { Type = "bullet-fire", RiderId = rider.Id, Ammo = rider.Ammo, Rocket = bullet.Rocket });
            round.ActionCount++;
            return true;
        }

        public static bool SetFire(Round round, string id, bool held)
        {
            var rider = round.Find(id);
            if (rider == null || !rider.Alive || round.Outcome != null) return false;
            rider.FireHeld = held && rider.Form == "cycle" && (rider.Ammo > 0 || rider.RocketReady);
            bool fired = rider.FireHeld && ShootBullet(round, rider);
            return rider.FireHeld || fired || !held;
        }

        static bool MoveBullet(Round round, Bullet bullet)
        {
            double travel = Math.Min(SPEED_BULLET * GridRules.STEP, BULLET_RANGE - bullet.Traveled);
            if (travel <= 0) return false;
            double dx = bullet.Vx * travel, dz = bullet.Vz * travel;
            var hit = WorldHit(round, null, bullet.X, bullet.Z, dx, dz, .12);
            double? riderHit = null;
            foreach (var target in round.Riders)
            {
                if (target.Id == bullet.OwnerId || !target.Alive) continue;
                double r = Radius(target);
                var t = BoxHit(bullet.X, bullet.Z, dx, dz, target.X - r - .12, target.X + r + .12, target.Z - r - .12, target.Z + r + .12);
                if (t.HasValue && (riderHit == null || t.Value < riderHit.Value)) riderHit = t;
            }
            if (riderHit.HasValue && (hit == null || riderHit.Value < hit.T))
            {
                if (bullet.Rocket) ExplodeRocket(round, bullet, bullet.X + dx * riderHit.Value, bullet.Z + dz * riderHit.Value);
                else round.Events.Add(new SimEvent { Type = "bullet-blocked", RiderId = bullet.OwnerId, Reason = "rider" });
                return false;
            }
            if (hit != null)
            {
                if (bullet.Rocket)
                {
                    ExplodeRocket(round, bullet, bullet.X + dx * hit.T, bullet.Z + dz * hit.T);
                    return false;
                }
                if (hit.Segment != null)
                {
                    double x = bullet.X + dx * hit.T, z = bullet.Z + dz * hit.T;
                    GridRules.CutTrail(round, hit.Segment, x, z);
                    round.Events.Add(new SimEvent { Type = "bullet-cut", RiderId = bullet.OwnerId, By = hit.Segment.OwnerId, X = x, Z = z });
                }
                else round.Events.Add(new SimEvent { Type = "bullet-blocked", RiderId = bullet.OwnerId, Reason = hit.Reason });
                return false;
            }
            bullet.X += dx; bullet.Z += dz; bullet.Traveled += travel;
            if (bullet.Rocket && bullet.Traveled >= BULLET_RANGE - EPS)
            {
                ExplodeRocket(round, bullet, bullet.X, bullet.Z);
                return false;
            }
            return bullet.Traveled < BULLET_RANGE - EPS;
        }

        static void ExplodeRocket(Round round, Bullet bullet, double x, double z)
        {
            foreach (var segment in round.Segments.ToArray())
            {
                double px = Math.Max(Math.Min(x, Math.Max(segment.X1, segment.X2)), Math.Min(segment.X1, segment.X2));
                double pz = Math.Max(Math.Min(z, Math.Max(segment.Z1, segment.Z2)), Math.Min(segment.Z1, segment.Z2));
                if (JsMath.Hypot(px - x, pz - z) <= 3) GridRules.CutTrail(round, segment, px, pz);
            }
            foreach (var rider in round.Riders)
            {
                if (!rider.Alive || rider.Id == bullet.OwnerId || JsMath.Hypot(rider.X - x, rider.Z - z) > 3.2) continue;
                rider.Hp -= 2;
                if (rider.Hp > 0) continue;
                rider.Alive = false;
                CloseTrail(round, rider);
                round.Events.Add(new SimEvent { Type = "crash", RiderId = rider.Id, Reason = "rocket", By = bullet.OwnerId, X = rider.X, Z = rider.Z });
                if (!round.Multiplayer && !rider.IsPlayer) round.Score += 250;
            }
            round.Events.Add(new SimEvent { Type = "rocket-explode", RiderId = bullet.OwnerId, X = x, Z = z });
        }

        static void Turn(Round round, Rider rider, int sign)
        {
            if (rider.Segment != null && JsMath.Hypot(rider.X - rider.Segment.X1, rider.Z - rider.Segment.Z1) < EPS)
                JsMath.SpliceOne(round.Segments, round.Segments.IndexOf(rider.Segment));
            else if (rider.Segment != null)
            {
                CloseTrail(round, rider);
                rider.IgnoreSegmentId = rider.Segment.Id;
                rider.DistanceSinceTurn = 0;
            }
            rider.Dir = (rider.Dir + sign + 4) % 4;
            rider.LastTurnAt = round.Elapsed;
            if (!GridRules.Airborne(round, rider)) OpenTrail(round, rider);
            round.Events.Add(new SimEvent { Type = "turn", RiderId = rider.Id, Sign = sign });
        }

        static double SpaceAhead(Round round, Rider rider, int dir, double length = 10)
        {
            int[] v = Dir[dir];
            var hit = WorldHit(round, rider, rider.X, rider.Z, v[0] * length, v[1] * length, Radius(rider));
            return hit != null ? hit.T * length : length;
        }

        static void AiDecision(Round round, Rider rider)
        {
            var target = round.Player;
            if (target == null || !target.Alive || round.Elapsed < rider.AiNextAt) return;
            double interval = round.Difficulty == "rookie" ? .43 : round.Difficulty == "expert" ? .19 : .29;
            rider.AiNextAt = round.Elapsed + interval + Random(round) * .13;
            double dist = JsMath.Hypot(target.X - rider.X, target.Z - rider.Z);
            Disc enemyDisc = null;
            foreach (var d in round.Shift.Discs) if (d.OwnerId != rider.Id && !d.Returning) { enemyDisc = d; break; }
            if (rider.Form == "cycle")
            {
                int[] v = Dir[rider.Dir];
                var forwardHit = WorldHit(round, rider, rider.X, rider.Z, v[0] * 10, v[1] * 10, RADIUS_CYCLE);
                double front = forwardHit != null ? forwardHit.T * 10 : 10;
                double left = SpaceAhead(round, rider, (rider.Dir + 3) % 4, 10);
                double right = SpaceAhead(round, rider, (rider.Dir + 1) % 4, 10);
                if (forwardHit != null && forwardHit.Reason == "trail" && front > 3.7 && front < 10 &&
                    rider.Ammo > 0 && SetFire(round, rider.Id, true))
                {
                    round.Shift.AiIntent = "firing at a wall";
                    return;
                }
                SetFire(round, rider.Id, false);
                if (forwardHit != null && forwardHit.Reason == "trail" && front > 1.1 && front < 3.6 &&
                    left < front + 2 && right < front + 2 &&
                    GridRules.RequestFlight(round, rider.Id, round.Elapsed >= rider.JetReadyAt ? "jet" : "sail"))
                {
                    round.Shift.AiIntent = "lifting over a wall";
                    return;
                }
                if (round.Elapsed > rider.Tactic.FootAt + (rider.Tactic.PreferCycle ? 2.1 : 0) && dist < 29 &&
                    (front < 8 || round.Elapsed > rider.Tactic.FootAt + 1.3 ||
                     Random(round) < (target.Form == "foot" ? .55 : .38) + rider.Tactic.Aggression * .25) &&
                    RequestForm(round, rider.Id))
                {
                    round.Shift.AiIntent = "dismounting to throw";
                    return;
                }
                if (front < 3.6 || (front < 7.5 && Math.Max(left, right) > front + 2))
                {
                    int sign = left > right ? -1 : right > left ? 1 : Random(round) < .5 ? -1 : 1;
                    QueueTurn(round, rider.Id, sign);
                    round.Shift.AiIntent = "turning for open lane";
                }
                else round.Shift.AiIntent = "riding for position";
                return;
            }
            rider.FireHeld = false;
            rider.FootTime += interval;
            bool ownDisc = false;
            foreach (var d in round.Shift.Discs) if (d.OwnerId == rider.Id) { ownDisc = true; break; }
            if (!ownDisc && round.Elapsed >= rider.DiscReadyAt && dist < 24 && Random(round) < .58 + rider.Tactic.Aggression * .3)
            {
                ThrowDisc(round, rider.Id);
                round.Shift.AiIntent = "disc thrown";
            }
            if (rider.FootTime > 4 + rider.Tactic.Aggression * 3 && round.Elapsed >= rider.FormReadyAt &&
                target.Form == "cycle" && RequestForm(round, rider.Id))
            {
                round.Shift.AiIntent = "remounting";
                return;
            }
            double towardX = (target.X - rider.X) / (dist != 0 ? dist : 1);
            double towardZ = (target.Z - rider.Z) / (dist != 0 ? dist : 1);
            double[][] candidates =
            {
                new[] { towardX, towardZ }, new[] { -towardZ, towardX }, new[] { towardZ, -towardX },
                new[] { -towardX, -towardZ }, new[] { 0.0, 0.0 },
            };
            double bestScore = double.NegativeInfinity, bestX = 0, bestZ = 0;
            foreach (var c in candidates)
            {
                double x = c[0], z = c[1];
                var probe = WorldHit(round, rider, rider.X, rider.Z, x * 2.7, z * 2.7, RADIUS_FOOT);
                double free = probe != null ? probe.T * 2.7 : 2.7;
                double score = free * 2 + (dist > 10 ? x * towardX + z * towardZ : -(x * towardX + z * towardZ) * .4);
                score += (x * -towardZ + z * towardX) * rider.Tactic.Strafe * .3;
                if (enemyDisc != null && JsMath.Hypot(enemyDisc.X - rider.X, enemyDisc.Z - rider.Z) < 8)
                {
                    double fromDiscX = rider.X - enemyDisc.X, fromDiscZ = rider.Z - enemyDisc.Z;
                    score += (x * fromDiscX + z * fromDiscZ) * rider.Tactic.Dodge;
                }
                score += Random(round) * .18;
                if (score > bestScore) { bestScore = score; bestX = x; bestZ = z; }
            }
            SetMove(round, rider.Id, bestX, bestZ);
            round.Shift.AiIntent = enemyDisc != null ? "dodging disc" : "seeking a firing lane";
        }

        static bool MoveDisc(Round round, Disc disc)
        {
            var owner = round.Find(disc.OwnerId);
            if (owner == null) return false;
            disc.Age += GridRules.STEP;
            if (!disc.Returning && disc.Age >= 1.15) disc.Returning = true;
            if (disc.Returning)
            {
                double rx = owner.X - disc.X, rz = owner.Z - disc.Z;
                double length = JsMath.Hypot(rx, rz);
                if (length < .55)
                {
                    owner.DiscReadyAt = Math.Max(owner.DiscReadyAt, round.Elapsed + .3);
                    round.Events.Add(new SimEvent { Type = "disc-return", RiderId = owner.Id });
                    return false;
                }
                disc.Vx = rx / length; disc.Vz = rz / length;
            }
            double dx = disc.Vx * SPEED_DISC * GridRules.STEP, dz = disc.Vz * SPEED_DISC * GridRules.STEP;
            var impact = disc.Returning ? null : WorldHit(round, null, disc.X, disc.Z, dx, dz, .16);
            Rider hitRider = null;
            double hitT = 0;
            if (!disc.Returning)
            {
                foreach (var rider in round.Riders)
                {
                    if (rider == owner || !rider.Alive) continue;
                    double r = Radius(rider);
                    var t = BoxHit(disc.X, disc.Z, dx, dz, rider.X - r - .24, rider.X + r + .24, rider.Z - r - .24, rider.Z + r + .24);
                    if (t.HasValue && (hitRider == null || t.Value < hitT)) { hitRider = rider; hitT = t.Value; }
                }
            }
            var target = hitRider != null && (impact == null || hitT <= impact.T + EPS) ? hitRider : null;
            if (target != null)
            {
                target.Hp--;
                disc.Returning = true;
                round.Events.Add(new SimEvent { Type = "disc-hit", RiderId = target.Id, By = owner.Id, Hp = target.Hp, X = target.X, Z = target.Z });
                if (owner.IsPlayer) round.Score += 150;
                if (target.Hp <= 0)
                {
                    target.Alive = false;
                    CloseTrail(round, target);
                    round.Events.Add(new SimEvent { Type = "crash", RiderId = target.Id, Reason = "disc", By = owner.Id, X = target.X, Z = target.Z });
                }
            }
            if (impact != null) disc.Returning = true;
            else { disc.X += dx; disc.Z += dz; }
            return true;
        }

        sealed class Move { public Rider Rider; public double Dx, Dz; public GridRules.Hit Hit; }

        public static Round StepShiftRound(Round round)
        {
            if (round.Outcome != null) return round;
            GridRules.UpdateArenaRules(round);
            foreach (var rider in round.Riders) if (rider.Alive) GridRules.SettleFlight(round, rider);
            Rider ai = null;
            if (!round.Multiplayer) foreach (var rider in round.Riders) if (!rider.IsPlayer) { ai = rider; break; }
            if (ai != null && ai.Alive) AiDecision(round, ai);
            foreach (var rider in round.Riders)
            {
                if (!rider.Alive || rider.Form != "cycle" || rider.PendingTurn == null) continue;
                if (rider.PendingTurn.ExpiresAt < round.Elapsed) rider.PendingTurn = null;
                else if (round.Elapsed - rider.LastTurnAt >= .16)
                {
                    int sign = rider.PendingTurn.Sign;
                    rider.PendingTurn = null;
                    Turn(round, rider, sign);
                }
            }
            foreach (var rider in round.Riders) if (rider.FireHeld) ShootBullet(round, rider);
            var bullets = new List<Bullet>();
            foreach (var bullet in round.Shift.Bullets) if (MoveBullet(round, bullet)) bullets.Add(bullet);
            round.Shift.Bullets = bullets;

            var moves = new List<Move>();
            foreach (var rider in round.Riders)
            {
                if (!rider.Alive) continue;
                double dirX, dirZ;
                if (rider.Form == "cycle") { dirX = Dir[rider.Dir][0]; dirZ = Dir[rider.Dir][1]; }
                else { dirX = rider.InputX; dirZ = rider.InputZ; }
                if (rider.Form == "foot" && JsMath.Hypot(dirX, dirZ) > .15)
                    rider.Dir = Math.Abs(dirX) > Math.Abs(dirZ) ? (dirX > 0 ? 1 : 3) : (dirZ > 0 ? 2 : 0);
                bool canMove = rider.Form != "cycle" || rider.AutoDrive || rider.ThrottleHeld;
                double speed = round.Elapsed < rider.RecoveryUntil || !canMove ? 0
                    : Speed(rider) * (round.Elapsed < rider.NitroUntil ? 1.5 : 1) *
                      (GridRules.Airborne(round, rider) ? GridRules.Flight[rider.FlightKind].Speed : 1);
                double dx = dirX * speed * GridRules.STEP, dz = dirZ * speed * GridRules.STEP;
                moves.Add(new Move { Rider = rider, Dx = dx, Dz = dz, Hit = WorldHit(round, rider, rider.X, rider.Z, dx, dz, Radius(rider)) });
            }
            var collisions = new List<(int a, int b, double t)>();
            for (int a = 0; a < moves.Count; a++)
                for (int b = a + 1; b < moves.Count; b++)
                {
                    var hit = ActorHit(moves[a].Rider, moves[b].Rider, moves[a].Dx, moves[a].Dz, moves[b].Dx, moves[b].Dz);
                    if (hit.HasValue && !GridRules.Airborne(round, moves[a].Rider) && !GridRules.Airborne(round, moves[b].Rider))
                        collisions.Add((a, b, hit.Value));
                }
            for (int k = 1; k < collisions.Count; k++)
            {
                var item = collisions[k];
                int m = k - 1;
                while (m >= 0 && collisions[m].t > item.t) { collisions[m + 1] = collisions[m]; m--; }
                collisions[m + 1] = item;
            }
            foreach (var (a, b, t) in collisions)
                if ((moves[a].Hit == null || t <= moves[a].Hit.T + EPS) && (moves[b].Hit == null || t <= moves[b].Hit.T + EPS))
                {
                    moves[a].Hit = new GridRules.Hit { T = t, Reason = "rider", By = moves[b].Rider.Id };
                    moves[b].Hit = new GridRules.Hit { T = t, Reason = "rider", By = moves[a].Rider.Id };
                }
            foreach (var move in moves)
            {
                var rider = move.Rider;
                double fraction = move.Hit != null ? Math.Max(0, Math.Min(1, move.Hit.T)) : 1;
                rider.X += move.Dx * fraction; rider.Z += move.Dz * fraction;
                rider.DistanceSinceTurn += JsMath.Hypot(move.Dx, move.Dz) * fraction;
                if (rider.Form == "cycle" && rider.Segment != null) { rider.Segment.X2 = rider.X; rider.Segment.Z2 = rider.Z; }
                if (move.Hit == null) continue;
                rider.Alive = false;
                CloseTrail(round, rider);
                round.Events.Add(new SimEvent { Type = "crash", RiderId = rider.Id, Reason = move.Hit.Reason, By = move.Hit.By, X = rider.X, Z = rider.Z });
                if (!rider.IsPlayer) round.Score += 250;
            }
            var discs = new List<Disc>();
            foreach (var disc in round.Shift.Discs) if (MoveDisc(round, disc)) discs.Add(disc);
            round.Shift.Discs = discs;
            round.Tick++;
            round.Elapsed = round.Tick * GridRules.STEP;

            var player = round.Player;
            Rider rival = null;
            foreach (var r in round.Riders) if (!r.IsPlayer) { rival = r; break; }
            if (!player.Alive && !rival.Alive) round.Outcome = new Outcome { Kind = "draw", Reason = "mutual" };
            else if (!player.Alive)
            {
                SimEvent crash = null;
                for (int i = round.Events.Count - 1; i >= 0; i--)
                    if (round.Events[i].Type == "crash" && round.Events[i].RiderId == player.Id) { crash = round.Events[i]; break; }
                round.Outcome = new Outcome { Kind = "lost", Reason = crash?.Reason ?? "unknown", By = crash?.By };
            }
            else if (!rival.Alive)
            {
                round.Score += 800 + (int)Math.Max(0, Math.Floor((TIME_LIMIT - round.Elapsed) * 5));
                round.Outcome = new Outcome { Kind = "won", Reason = "rival" };
            }
            else if (round.Elapsed >= TIME_LIMIT)
            {
                string kind = player.Hp > rival.Hp ? "won" : player.Hp < rival.Hp ? "lost" : "draw";
                if (kind == "won") round.Score += 500;
                round.Outcome = new Outcome { Kind = kind, Reason = "timeout" };
            }
            if (round.Outcome != null) round.Events.Add(new SimEvent { Type = "round-end", Outcome = round.Outcome, Score = round.Score });
            return round;
        }
    }
}
