using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using RenderCore.Animation;

namespace CharacterManager;

internal enum FaceMode { Off, Manual, Ksp }

/// KSP's rules for when a Kerbal's face and idles change (kerbalExpressionSystem). A number, Expression (-1 panic ... +1 whee),
/// follows what the Kerbal is going through: weightlessness cheers (KSP counts the g-force only while the ship is descending),
/// speed, spin and sustained high g frighten, a jetpack on EVA cheers, and each Kerbal's courage and stupidity change how much.
/// The speed KSP uses is its physics body's, which its Krakensbane keeps below 5 m/s once the ship is above about 150-300 m: so
/// speed only frightens near the ground (lift-off, landing); the g-force frightens through the G-LOC counter, which only builds
/// up above roughly 6 g. KSA's characters have neither, so a Kerbal's two numbers (and whether they are
/// a "badass", who enjoy speed instead of fearing it) come from a hash of their name, the same in a seat and on EVA. A Kerbal
/// sitting calmly (|Expression| <= 0.06) goes idle: every 10 s there is a 1 in 10 chance of one of the idle clips, and the face
/// blend goes neutral while idle.
internal static class ExpressionAI
{
    private const double StandardGravity = 9.80665;
    /// KSP's Krakensbane: above this height over the ground (its 200 m threshold, or 50 m for a ship alone, plus 100 m) the speed
    /// beyond 5 m/s is taken off the body. A loaded ship is usually alone at lift-off, a Kerbal on EVA usually near others.
    private const double SeatedKrakensbaneAltitude = 150, EvaKrakensbaneAltitude = 300, KrakensbaneSpeed = 5;
    /// Whether time warp is on: the animations run faster with it, so the idle clips wait for normal speed.
    internal static bool Warped => Universe.GetSimulationSpeed() > 1.0001;

    /// What the game's vehicle says about the Kerbal, refreshed every frame by the two hooks below.
    internal sealed class Inputs
    {
        public bool Seated, Jetpack, GroundedIdle, Falling, Contact;
        public string Kitten = "";
        /// Speed as KSP's physics body has it (see the class comment), the real surface speed, the climb rate, the altitude above
        /// the ground, the g-force (proper acceleration over standard gravity) and the body's rotation rate.
        public double Speed, SurfaceSpeed, VerticalSpeed, RadarAltitude, Gee, Rates;
        /// The game's acceleration measurement (gravity taken out, in m/s2, in the body's frame)
        public Vector3 Acceleration;
    }

    internal static readonly ConditionalWeakTable<KittenRenderable, Inputs> ByRenderable = new();

    internal static bool ExpressionInputs(KittenRenderable r, out Inputs? inputs) => ByRenderable.TryGetValue(r, out inputs);

    private static void Fill(KittenRenderable? r, Vehicle v, bool seated, string? kitten, bool jetpack, bool groundedIdle, bool falling)
    {
        if (r is null)
            return;
        try
        {
            Inputs i = ByRenderable.GetOrCreateValue(r);
            i.Seated = seated;
            i.Kitten = kitten ?? "";
            i.SurfaceSpeed = v.GetSurfaceSpeed();
            i.RadarAltitude = v.GetRadarAltitude();
            i.Contact = v.Situation.HasAnyContact();
            i.VerticalSpeed = double3.Dot(v.Orbit.StateVectors.VelocityCci, v.Orbit.StateVectors.PositionCci.Normalized());
            // KSP's Krakensbane takes the speed above 5 m/s off its physics body once clear of the ground
            bool nearGround = i.Contact || i.RadarAltitude < (seated ? SeatedKrakensbaneAltitude : EvaKrakensbaneAltitude);
            i.Speed = nearGround ? i.SurfaceSpeed : Math.Min(i.SurfaceSpeed, KrakensbaneSpeed);
            // as the game's own G-FORCE readout: its acceleration measurement (gravity taken out) in standard gravities
            double3 acceleration = v.AccelerationBody;
            i.Acceleration = new Vector3((float)acceleration.X, (float)acceleration.Y, (float)acceleration.Z);
            i.Gee = acceleration.Length() / StandardGravity;
            i.Rates = v.BodyRates.Length();
            i.Jetpack = jetpack;
            i.GroundedIdle = groundedIdle;
            i.Falling = falling;
        }
        catch (Exception e)
        {
            Log.Error("could not read the vehicle's state for the Kerbal's expression", e);
        }
    }

    /// FNV-1a then a few rounds of mixing: three stable numbers for a name.
    internal static (float Courage, float Stupidity, bool Badass) Stats(string name)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in name)
            h = (h ^ c) * 1099511628211UL;
        float Next()
        {
            h += 0x9E3779B97F4A7C15UL;
            ulong z = h;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (float)((z ^ (z >> 31)) >> 40) / (1UL << 24);
        }
        return (Next(), Next(), Next() > 0.9f);
    }

    /// A plain average over the last few seconds of updates, weighted by their length (KSP averages a fixed number of its 50 Hz
    /// physics frames; the game here updates at its own frame rate).
    private sealed class Boxcar
    {
        private readonly Queue<(float Dt, Vector3 Value)> _samples = new();
        private readonly float _span;
        private float _total;
        private Vector3 _sum;

        internal Boxcar(float span) => _span = span;

        internal Vector3 Mean => _total > 0f ? _sum / _total : Vector3.Zero;

        internal void Add(Vector3 value, float dt)
        {
            if (dt <= 0f)
                return;
            _samples.Enqueue((dt, value));
            _total += dt;
            _sum += value * dt;
            while (_samples.Count > 1 && _total - _samples.Peek().Dt >= _span)
            {
                (float oldDt, Vector3 oldValue) = _samples.Dequeue();
                _total -= oldDt;
                _sum -= oldValue * oldDt;
            }
        }
    }

    internal sealed class Brain
    {
        internal float Courage = 0.5f, Stupidity = 0.5f;
        internal bool Badass;
        internal bool Female;
        internal string Kitten = "";
        internal float Expression, Whee, Panic, Fear, Variance = 0.5f, Secondary = 0.5f;
        /// KSP's G-LOC counter (kerbalGExperienced) and it as a share of what knocks the Kerbal out
        internal float GExperienced, GNormalized, Gee = 1f;
        /// Seated only: the Kerbal has been calm for a while, so KSP's animator is in its idle state; and the idle's clip choice
        internal bool IsIdle, ActionRequested;
        /// Whether an idle clip may play now: the Kerbal is calm (KSP's rule, and then the face goes neutral) or cheerful and not
        /// frightened (KSA's addition: a Kerbal floating in orbit keeps its cheer and still fidgets now and then)
        internal bool CanIdleAction;
        internal float IdleTimer;
        private float _targetVariance, _targetSecondary, _varianceTimer, _secondaryTimer;
        /// KSP's smoothing windows: the acceleration over its last 20 physics frames, the seated expression target over 20 updates (0.4 s)
        private readonly Boxcar _acceleration = new(0.4f), _target = new(0.4f);
        private bool _known;

        /// Takes the Kerbal's two numbers from their name, once it is known.
        internal void Identify(string name)
        {
            if (_known || name.Length == 0)
                return;
            (Courage, Stupidity, Badass) = Stats(name);
            Kitten = name;
            _known = true;
        }

        /// The values KSP's Kerbal prefabs set on kerbalExpressionSystem (not the class's own defaults, which are 1 s for both
        /// intervals and 1 for the expression's rate): the face blend wanders slowly, a new target every 4 and 5 seconds.
        private const float ExpressionLerpRate = 3f, VarianceInterval = 4f, SecondaryInterval = 5f;

        /// KSP's variances: a new random target every few seconds, approached at 1 per second.
        internal void WanderVariance(float dt)
        {
            Step(ref Variance, ref _targetVariance, ref _varianceTimer, VarianceInterval, dt);
            Step(ref Secondary, ref _targetSecondary, ref _secondaryTimer, SecondaryInterval, dt);
        }

        private static void Step(ref float value, ref float target, ref float timer, float interval, float dt)
        {
            timer += dt;
            if (timer >= interval)
            {
                timer = 0f;
                target = Random.Shared.NextSingle();
            }
            value += (target - value) * Math.Min(dt, 1f);
        }

        internal void Update(float dt, Inputs? i)
        {
            bool seated = i?.Seated ?? false;
            if (i is not null)
            {
                _acceleration.Add(i.Acceleration, dt);  // the vector is averaged, then its length taken, as KSP does
                Gee = _acceleration.Mean.Length() / (float)StandardGravity;
            }
            AccumulateG(i is null ? 1f : Gee, dt);
            // KSP counts the g-force only while the ship descends, so its Kerbals cheer in free fall but not while climbing, and in a
            // steady orbit (a climb rate flipping around zero) they flicker between the two. Here the g-force counts whenever the
            // vehicle is off the ground: weightless is cheerful, whichever way it is moving. On the ground it is taken as 1 g.
            float gee = i is null || i.Contact ? 1f : Gee;
            float speed = (float)(i?.Speed ?? 0), rates = Math.Min((float)(i?.Rates ?? 0), 100f);
            float whee = (1f - Math.Clamp(Math.Abs(gee), 0f, 1f)) * 0.6f;
            whee += Badass ? speed / 100f : 0f;
            whee += Math.Clamp(rates / 40f, 0f, 1f) * Stupidity;
            whee -= Fear;
            if (!seated && i is { Jetpack: true })
                whee += 0.3f;
            whee *= 1f + Stupidity * 2f;
            float panic = (Badass ? 0f : speed / 100f * (1f - Stupidity)) + (Badass ? rates / 100f : rates / 50f) + Fear / (1f + Stupidity)
                          + MathF.Sqrt(Math.Max(0f, GNormalized - (Badass ? 0.75f : 0.5f))) * 0.5f;
            if (!seated && i is not null)
            {
                if (i.GroundedIdle)  // standing still on the ground: a pleasant, calm face
                {
                    whee = Female ? 0.1f : 0.35f;
                    panic = 0f;
                    Variance = Female ? 0.1f : 0f;
                    Secondary = Female ? 0.9f : 0f;
                }
                else if (i.Falling)
                {
                    float fall = Math.Clamp(speed / 100f, 0f, 1f);
                    if (Badass)
                        whee += fall;
                    else
                        Fear = fall;
                }
            }
            Fear *= 1f - Courage * (panic < 0.3f ? 0.5f : 0.2f);
            Whee = Math.Clamp(whee, 0f, 1f);
            Panic = Math.Clamp(panic, 0f, 1f);
            float target = (Whee * 0.5f - Panic * 0.5f) * 2f;
            if (seated)  // inside, the target is averaged over the last 20 updates first
            {
                _target.Add(new Vector3(target, 0f, 0f), dt);
                target = _target.Mean.X;
            }
            Expression += (target - Expression) * Math.Min(dt * ExpressionLerpRate, 1f);
            if (seated)
                TickIdle(dt);
            else
                IsIdle = CanIdleAction = false;
        }

        /// KSP's G tolerance: braver Kerbals take more, "badass" ones more again.
        private float Tolerance => (1f + 0.5f * Courage) * (Badass ? 1.5f : 1f);

        /// kerbalGExperienced: grows with g^4 above 1500 (about 6.2 g), and drains fast below it.
        private void AccumulateG(float gee, float dt)
        {
            dt = Math.Min(dt, 0.1f);
            float tolerance = Tolerance;
            float increment = (MathF.Pow(Math.Min(gee, 20f), 4f) - 1500f * tolerance) * dt;
            if (increment < 0f)
                increment = -(increment * increment);
            increment *= 1f + (Random.Shared.NextSingle() * 0.1f - 0.05f);
            GExperienced = Math.Clamp(GExperienced + increment, 0f, 60000f * tolerance);
            GNormalized = GExperienced / (60000f * tolerance);
        }

        private void TickIdle(float dt)
        {
            bool calm = Math.Abs(Expression) <= 0.06f;
            CanIdleAction = calm || (Expression > 0.06f && Panic < 0.2f);
            if (!calm)
                IsIdle = false;
            if (!CanIdleAction)
                return;
            if (Warped)  // the clips would play at warp speed: no new idle until normal speed
            {
                IdleTimer = 0f;
                return;
            }
            IdleTimer += dt;
            if (IdleTimer < Settings.Current.IdleSeconds)
                return;
            IdleTimer = 0f;
            if (calm)
                IsIdle = true;
            ActionRequested = Random.Shared.Next(0, 100) < Settings.Current.IdleChancePercent;
        }

        internal bool TakeActionRequest()
        {
            bool asked = ActionRequested;
            ActionRequested = false;
            return asked;
        }
    }

    // Both hooks run before the renderable is updated, which is when the face is posed.
    [HarmonyPatch(typeof(Vehicle), nameof(Vehicle.UpdateRenderData), new[] { typeof(IViewport), typeof(int) })]
    internal static class SeatedStats
    {
        static void Prefix(Vehicle __instance)
        {
            if (!GameMembers.ExpressionOk || __instance is KittenEva)
                return;
            ReadOnlySpan<IVASeat> crew = __instance.Crew;
            for (int s = 0; s < crew.Length; s++)
            {
                IVASeat seat = crew[s];
                if (seat.Renderable is { } r)
                    Fill(r, __instance, true, Universe.KittenRoster.Find(seat.AssignedKittenHash)?.Name, false, false, false);
            }
        }
    }

    [HarmonyPatch(typeof(KittenEva), nameof(KittenEva.UpdateRenderData), new[] { typeof(IViewport), typeof(int) })]
    internal static class EvaStats
    {
        private static readonly AccessTools.FieldRef<KittenEva, LocomotionState> Locomotion = AccessTools.FieldRefAccess<KittenEva, LocomotionState>("_locomotionState");

        static void Prefix(KittenEva __instance)
        {
            if (!GameMembers.ExpressionOk)
                return;
            LocomotionState l = Locomotion(__instance);
            bool idle = l.Mode == LocomotionMode.Grounded && l.GroundSpeed < KittenLocomotionTuning.Current.IdleSpeedThreshold;
            bool falling = l.Mode == LocomotionMode.Airborne && l.GravityMagnitude > 0.5f;
            Fill(__instance.Renderable, __instance, false, Universe.KittenRoster.Find(__instance.Id)?.Name,
                 __instance.FlightComputer.RCSMode == FlightComputerRCSMode.Enabled, idle, falling);
        }
    }

    /// KSP's seated idle schedule instead of the game's (an action every 45-75 s): see Brain.Update.
    [HarmonyPatch(typeof(KittenRenderable), "AdvanceIdleActionSchedule")]
    internal static class SeatedIdleSchedule
    {
        private static readonly AccessTools.FieldRef<KittenRenderable, bool> Seated = AccessTools.FieldRefAccess<KittenRenderable, bool>("_seated");
        private static readonly AccessTools.FieldRef<KittenRenderable, bool> Playing = AccessTools.FieldRefAccess<KittenRenderable, bool>("_isPlayingIdleAction");
        private static readonly AccessTools.FieldRef<KittenRenderable, double> EndTime = AccessTools.FieldRefAccess<KittenRenderable, double>("_idleActionEndTime");

        static bool Prefix(KittenRenderable __instance, AnimatedRenderable model, double time, AnimationAssetRef? defaultAnim, List<AnimationAssetRef>? actions)
        {
            if (!GameMembers.ExpressionOk || !Seated(__instance) || Settings.Current.FaceMode != FaceMode.Ksp || actions is null || actions.Count == 0
                || !FaceBlend.TryGet(__instance, out FaceBlend.Processor? face))
                return true;
            if (Playing(__instance))
            {
                if (time >= EndTime(__instance) || !face.Brain.CanIdleAction || Warped)  // done, the Kerbal is no longer calm or cheerful, or time warp is on
                {
                    Playing(__instance) = false;
                    if (defaultAnim != null)
                        model.SetAnimation(defaultAnim);
                }
            }
            else if (face.Brain.CanIdleAction && !Warped && face.Brain.TakeActionRequest())
            {
                AnimationAssetRef action = actions[Random.Shared.Next(actions.Count)];
                Playing(__instance) = true;
                EndTime(__instance) = time + action.AnimLength;
                model.SetAnimation(action);
            }
            return false;
        }
    }
}
