using Capta.Capture;

namespace Capta.Services;

/// <summary>The most recent captures this session, newest first (the tray flyout's "Recent").</summary>
public sealed class CaptureHistory
{
    public const int Capacity = 6;

    private readonly List<CaptureResult> _items = [];

    public IReadOnlyList<CaptureResult> Items => _items;

    public void Add(CaptureResult result)
    {
        _items.Insert(0, result);
        if (_items.Count > Capacity)
            _items.RemoveRange(Capacity, _items.Count - Capacity);
    }
}
