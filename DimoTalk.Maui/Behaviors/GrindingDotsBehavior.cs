namespace DimoTalk.Maui.Behaviors;

/// <summary>
/// 研墨墨点动画：附加到思考态的墨点容器
/// 三个墨点依次浓→淡循环，模拟研墨的往复节奏
/// </summary>
public class GrindingDotsBehavior : Behavior<HorizontalStackLayout>
{
    private HorizontalStackLayout? _owner;
    private CancellationTokenSource? _cts;
    private readonly List<View> _dots = new();

    protected override void OnAttachedTo(HorizontalStackLayout bindable)
    {
        base.OnAttachedTo(bindable);
        _owner = bindable;
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    protected override void OnDetachingFrom(HorizontalStackLayout bindable)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _owner = null;
        _dots.Clear();
        base.OnDetachingFrom(bindable);
    }

    private async Task RunAsync(CancellationToken token)
    {
        // 等待子元素加载完成
        await Task.Delay(150, token);
        if (_owner == null) return;

        foreach (var child in _owner.Children)
            if (child is Border b) _dots.Add(b);

        if (_dots.Count == 0) return;

        var idx = 0;
        while (!token.IsCancellationRequested && _owner != null)
        {
            try
            {
                var current = idx;
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (current < _dots.Count)
                    {
                        await _dots[current].FadeTo(1.0, length: 180, easing: Easing.SinIn);
                        await _dots[current].FadeTo(0.15, length: 260, easing: Easing.SinOut);
                    }
                });
                idx = (idx + 1) % _dots.Count;
            }
            catch (OperationCanceledException) { break; }
            catch { break; }
        }
    }
}
