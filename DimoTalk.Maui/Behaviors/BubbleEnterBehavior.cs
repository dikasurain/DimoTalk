using DimoTalk.Maui.Services;

namespace DimoTalk.Maui.Behaviors;

/// <summary>
/// 气泡浮入动效：气泡进入视口时上浮 + 淡入
/// CollectionView 虚拟化回收后重新挂载也会触发（滚动回看时有轻微动效）
/// </summary>
public class BubbleEnterBehavior : Behavior<Frame>
{
    private bool _animated;

    protected override void OnAttachedTo(Frame bindable)
    {
        base.OnAttachedTo(bindable);
        _animated = false;
        _ = InkAnimations.BubbleFloatInAsync(bindable);
    }

    protected override void OnDetachingFrom(Frame bindable)
    {
        // 复位，避免回收复用时残留偏移
        bindable.CancelAnimations();
        bindable.TranslationY = 0;
        bindable.Opacity = 1;
        base.OnDetachingFrom(bindable);
    }
}

/// <summary>
/// 印章晕染动效：空状态的朱砂描边大印章
/// 描边浓淡缓慢循环 + 轻微缩放，似墨在宣纸上慢慢晕开
/// </summary>
public class SealInkBloomBehavior : Behavior<Border>
{
    private CancellationTokenSource? _cts;

    protected override void OnAttachedTo(Border bindable)
    {
        base.OnAttachedTo(bindable);
        _cts = new CancellationTokenSource();
        _ = RunAsync(bindable, _cts.Token);
    }

    protected override void OnDetachingFrom(Border bindable)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        bindable.CancelAnimations();
        base.OnDetachingFrom(bindable);
    }

    private async Task RunAsync(Border seal, CancellationToken token)
    {
        // 初始进场：印章钤盖（按下回弹）
        try
        {
            seal.Scale = 0.6;
            seal.Opacity = 0;
            await Task.WhenAll(
                seal.ScaleTo(1, length: 500, easing: Easing.SpringOut),
                seal.FadeTo(1, length: 400, easing: Easing.SinIn));
        }
        catch { return; }

        // 持续晕染：描边呼吸 + 极缓缩放
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.WhenAll(
                    seal.ScaleTo(1.04, length: 1600, easing: Easing.SinInOut),
                    seal.FadeTo(0.75, length: 1600, easing: Easing.SinInOut));
                await Task.WhenAll(
                    seal.ScaleTo(1.0, length: 1600, easing: Easing.SinInOut),
                    seal.FadeTo(1, length: 1600, easing: Easing.SinInOut));
            }
            catch { break; }
        }
    }
}
