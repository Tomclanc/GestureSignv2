using System;
using System.Collections.Generic;
using System.Linq;

namespace GestureSign.Foundation;

public readonly record struct TipTapContact(int Id, double X, double Y);
public readonly record struct TipTapResult(string? Started, string? Recognized, int HeldCount = 1);

// Coordinates are normalized touchpad coordinates. A held group of 1-3 contacts
// must settle before one additional finger taps outside that group.
public sealed class TouchPadTipTapRecognizer
{
    public const string Left = "TouchPadTipTap.Left";
    public const string Right = "TouchPadTipTap.Right";
    private const long HoldMs = 120, MinTapMs = 20, MaxTapMs = 300, StaleMs = 1000;
    private const double Travel = 0.025, Separation = 0.04, Dominance = 1.3;
    private List<TipTapContact> _anchors = new();
    private TipTapContact? _tap;
    private long _stableSince, _tapSince, _lastFrame = -1;
    private string? _direction;
    private bool _blocked;
    public bool HasContacts { get; private set; }
    public int HeldCount => _anchors.Count;

    public static string GestureName(int heldCount, string direction)
        => heldCount == 1 ? $"TouchPadTipTap.{direction}" : $"TouchPadTipTap.Hold{heldCount}.{direction}";

    public void Reset()
    {
        _anchors.Clear(); _tap = null; _direction = null;
        _blocked = HasContacts = false; _lastFrame = -1;
    }

    public TipTapResult Update(IReadOnlyList<TipTapContact> contacts, long now, IReadOnlyList<TipTapContact>? released = null)
    {
        if (_lastFrame >= 0 && (now < _lastFrame || now - _lastFrame > StaleMs)) Reset();
        _lastFrame = now;
        HasContacts = contacts.Count != 0;
        if (contacts.Count == 0) { Reset(); return default; }
        if (_blocked) return default;
        if (contacts.Count > 4 || contacts.Select(c => c.Id).Distinct().Count() != contacts.Count)
        { _blocked = true; return default; }
        if (_anchors.Count == 0)
        {
            if (contacts.Count > 3) { _blocked = true; return default; }
            _anchors = contacts.ToList(); _stableSince = now; return default;
        }
        if (_anchors.Any(a => !contacts.Any(c => c.Id == a.Id)))
        { _blocked = true; return default; }
        var held = contacts.Where(c => _anchors.Any(a => a.Id == c.Id)).ToList();
        var extra = contacts.Where(c => !_anchors.Any(a => a.Id == c.Id)).ToList();
        var moved = _anchors.Any(a => Moved(a, held.First(c => c.Id == a.Id)));
        if (_tap == null)
        {
            if (moved) { _anchors = held; _stableSince = now; }
            if (extra.Count == 0) return default;
            // HID may deliver the held group in staggered frames. Accumulate
            // those contacts before the settling interval; do not call it a tap.
            if (now - _stableSince < HoldMs && contacts.Count <= 3)
            { _anchors = contacts.ToList(); _stableSince = now; return default; }
            if (extra.Count != 1 || now - _stableSince < HoldMs)
            { _blocked = true; return default; }
            var direction = GetDirection(held, extra[0]);
            if (direction == null) { _blocked = true; return default; }
            _anchors = held; _tap = extra[0]; _tapSince = now;
            _direction = GestureName(held.Count, direction);
            return new TipTapResult(_direction, null, HeldCount);
        }
        var tap = _tap.Value;
        if (moved || now - _tapSince > MaxTapMs ||
            (released != null && released.Any(c => c.Id == tap.Id && Moved(tap, c))))
        { _blocked = true; return default; }
        if (extra.Count != 0)
        {
            if (extra.Count != 1 || extra[0].Id != tap.Id || Moved(tap, extra[0])) _blocked = true;
            return default;
        }
        var result = new TipTapResult(null, now - _tapSince >= MinTapMs ? _direction : null, HeldCount);
        _tap = null; _direction = null;
        return result;
    }

    private static string? GetDirection(IReadOnlyList<TipTapContact> held, TipTapContact tap)
    {
        var dx = tap.X - held.Average(c => c.X);
        var dy = tap.Y - held.Average(c => c.Y);
        if (Math.Abs(dx) >= Math.Abs(dy) * Dominance)
        {
            if (tap.X <= held.Min(c => c.X) - Separation) return "Left";
            if (tap.X >= held.Max(c => c.X) + Separation) return "Right";
        }
        if (Math.Abs(dy) >= Math.Abs(dx) * Dominance)
        {
            if (tap.Y <= held.Min(c => c.Y) - Separation) return "Up";
            if (tap.Y >= held.Max(c => c.Y) + Separation) return "Down";
        }
        // Diagonal or inside-group taps are ambiguous and must not pick a random axis.
        return null;
    }
    private static bool Moved(TipTapContact a, TipTapContact b)
        => Math.Abs(a.X - b.X) > Travel || Math.Abs(a.Y - b.Y) > Travel;
}
