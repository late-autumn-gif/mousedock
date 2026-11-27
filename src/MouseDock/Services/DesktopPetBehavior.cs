using System;

namespace MouseDock.Services;

internal sealed class DesktopPetBehavior(Random random)
{
    internal enum State { Idle, MicroAction, RandomAction, Interaction, Moving, Sleep, WakeUp, Transition }
    internal enum Gesture { Lean, Hop, Dizzy, Wave, Look }
    internal readonly record struct Sample(State State, double Tilt = 0, double Lift = 0, bool Dizzy = false, bool Blink = false, bool Wink = false, Gesture? Animation = null);
    internal const int DizzyDuration = 1600;
    private sealed record Action(Gesture Kind, int Weight, int Duration, int Cooldown);
    private static readonly Action[] Actions =
    [
        new(Gesture.Lean, 6, 1450, 18000),
        new(Gesture.Hop, 2, 780, 30000),
        new(Gesture.Dizzy, 2, DizzyDuration, 90000),
        new(Gesture.Wave, 3, 1400, 23000),
        new(Gesture.Look, 4, 1550, 18000)
    ];

    private readonly long[] _readyAt = new long[Actions.Length];
    private long _idleAt = -1;
    private long _nextAction;
    private long _nextBlink;
    private long _extraBlink;
    private bool _wink;
    private long _flightEnded = -1;
    private long _wakeAt = -1;
    private long _started;
    private int _active = -1;
    private int _last = -1;
    private double _direction;

    internal void Reset()
    {
        _idleAt = _flightEnded = _wakeAt = -1;
        _active = -1;
        _extraBlink = _blinkUntil = 0;
    }

    internal void Land(long now) => _flightEnded = now;

    internal Sample Update(long now, bool pointer, bool moving, bool blocked, bool autonomous,
        bool rest, bool blink, int speedPercent, int frequencyPercent, int restAfter = 240000, int restLength = int.MaxValue)
    {
        double speed = Math.Clamp(speedPercent, 50, 150) / 100d;
        double frequency = Math.Clamp(frequencyPercent, 50, 150) / 100d;
        if (moving)
        {
            Reset();
            return new(State.Moving);
        }
        if (blocked)
        {
            Reset();
            return new(State.Transition);
        }
        if (pointer)
        {
            if (_idleAt >= 0 && rest && now - _idleAt >= restAfter) _wakeAt = now;
            _idleAt = -1;
            _active = -1;
            _extraBlink = _blinkUntil = 0;
            return _wakeAt >= 0 && (now - _wakeAt) * speed < 520
                ? new(State.WakeUp, Lift: -2 * Envelope((now - _wakeAt) * speed / 520))
                : new(State.Interaction);
        }
        if (_flightEnded >= 0)
        {
            double t = (now - _flightEnded) * speed / 440;
            if (t < 1) return new(State.Transition, Lift: -6 * (1 - t) * (1 - t) + 2 * Envelope(t) * t);
            _flightEnded = -1;
        }
        if (_idleAt < 0)
        {
            _idleAt = now;
            _nextAction = now + Delay(frequency);
            _nextBlink = now + random.Next(2900, 6600);
        }
        if (rest && now - _idleAt >= restAfter)
        {
            _active = -1;
            if (now - _idleAt - restAfter < restLength) return new(State.Sleep);
            _idleAt = -1;
            _wakeAt = now;
            return new(State.WakeUp, Lift: -2);
        }

        if (!autonomous) _active = -1;
        else if (_active >= 0)
        {
            Action action = Actions[_active];
            double t = (now - _started) * speed / action.Duration;
            if (t < 1)
            {
                double easing = Envelope(t);
                return action.Kind switch
                {
                    Gesture.Lean => new(State.RandomAction, _direction * 4 * easing),
                    Gesture.Hop => new(State.RandomAction, Lift: -6 * easing, Animation: Gesture.Hop),
                    Gesture.Wave => new(State.RandomAction, 4 * Math.Sin(t * Math.Tau * 2) * easing, Animation: Gesture.Wave),
                    Gesture.Look => new(State.RandomAction, 4 * Math.Sin(t * Math.Tau) * easing, Animation: Gesture.Look),
                    _ => new(State.RandomAction, DizzyTilt(t) * _direction, Dizzy: true)
                };
            }
            _active = -1;
        }
        else if (now >= _nextAction)
        {
            int total = 0;
            for (int i = 0; i < Actions.Length; i++)
                if (i != _last && now >= _readyAt[i]) total += Actions[i].Weight;
            if (total > 0)
            {
                int roll = random.Next(total);
                for (int i = 0; i < Actions.Length; i++)
                {
                    if (i == _last || now < _readyAt[i]) continue;
                    if ((roll -= Actions[i].Weight) >= 0) continue;
                    _active = _last = i;
                    _started = now;
                    _direction = random.Next(2) == 0 ? -1 : 1;
                    _readyAt[i] = now + (long)(Actions[i].Duration / speed) + Actions[i].Cooldown;
                    break;
                }
            }
            _nextAction = now + Delay(frequency);
        }

        if (blink && now >= _nextBlink)
        {
            _blinkUntil = now + 140;
            int pattern = random.Next(12);
            _wink = pattern == 2;
            _extraBlink = pattern < 2 ? now + 235 : 0;
            _nextBlink = now + random.Next(3100, 7900);
        }
        bool blinking = blink && (now < _blinkUntil ||
            (_extraBlink > 0 && now >= _extraBlink && now < _extraBlink + 140));
        return new(blinking ? State.MicroAction : State.Idle, Blink: blinking && !_wink, Wink: blinking && _wink);
    }

    private long _blinkUntil;
    private long Delay(double frequency) => (long)(random.Next(14000, 32001) / frequency);
    internal static double DizzyTilt(double t) => 4 * Math.Sin(t * Math.PI * 5) * Math.Sin(t * Math.PI);
    private static double Envelope(double t) => Math.Pow(Math.Sin(Math.PI * t), 2);
}
