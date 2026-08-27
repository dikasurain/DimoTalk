using DimoTalk.Maui.Config;
using DimoTalk.Maui.Models;

namespace DimoTalk.Maui.Services.Memory;

public class ShortTermMemory
{
    private readonly List<Message> _window = new();
    private readonly int _maxSize;

    public ShortTermMemory(int maxSize = AppConfig.ShortTermMaxMessages) => _maxSize = maxSize;

    public void Add(Message msg)
    {
        _window.Add(msg);
        while (_window.Count > _maxSize) _window.RemoveAt(0);
    }

    public IReadOnlyList<Message> Context => _window.AsReadOnly();
    public int Count => _window.Count;
    public void Clear() => _window.Clear();
}
