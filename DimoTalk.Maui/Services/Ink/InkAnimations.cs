namespace DimoTalk.Maui.Services;
/// <summary>
/// 水墨主题动画集合
/// - 墨滴涟漪：点击落墨时从按钮散出的同心墨环
/// - 研墨脉冲：等待回复时"研墨构思中"气泡的呼吸墨晕
/// - 印章钤盖：印章按下回弹动效
/// - 气泡浮入：新气泡上浮淡入
/// </summary>
public static class InkAnimations
{
    /// <summary>默认动画速率（毫秒）</summary>
    private const uint Quick = 180;
    private const uint Normal = 320;
    private const uint Slow = 550;

    private static readonly Color InkWash = Color.FromArgb("#D8D3C4");
    private static readonly Color InkFaint = Color.FromArgb("#A9A496");
    private static readonly Color Cinnabar = Color.FromArgb("#C0392B");

    /// <summary>
    /// 墨滴涟漪：从指定视觉元素中心扩散 3 圈墨环
    /// 调用方式：InkAnimations.InkRippleAsync(SendButton)
    /// </summary>
    public static async Task InkRippleAsync(View anchor, int rings = 3)
    {
        if (anchor.Width <= 0 || anchor.Height <= 0) return;

        var parent = anchor.Parent as Layout;
        if (parent == null) return;

        var cx = anchor.X + anchor.Width / 2;
        var cy = anchor.Y + anchor.Height / 2;

        // 在父容器上叠加墨环层
        for (int i = 0; i < rings; i++)
        {
            var ring = new Border
            {
                WidthRequest = 12,
                HeightRequest = 12,
                StrokeThickness = 2,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
                Stroke = InkFaint,
                BackgroundColor = Colors.Transparent,
                Opacity = 0.8,
                TranslationX = cx - 6,
                TranslationY = cy - 6,
                InputTransparent = true,
                IsVisible = true,
            };
            parent.Children.Add(ring);

            // 依次错开启动
            _ = AnimateRingAsync(ring, delayMs: (uint)(i * 90));
        }

        await Task.CompletedTask;
    }

    private static async Task AnimateRingAsync(Border ring, uint delayMs)
    {
        try
        {
            if (delayMs > 0) await Task.Delay((int)delayMs);

            // 扩散 + 淡出
            await ring.RelScaleTo(4.5, length: Slow, easing: Easing.SinOut);
        }
        catch { /* 页面切换时忽略 */ }
        finally
        {
            var parent = ring.Parent as Layout;
            parent?.Children.Remove(ring);
        }
    }

    /// <summary>
    /// 研墨脉冲：呼吸式墨晕（用于"研墨构思中"气泡持续动效）
    /// 返回 CancellationTokenSource，取消即停止
    /// </summary>
    public static CancellationTokenStartInfo GrindingPulse(View target)
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            // 呼吸节奏：浓 → 淡 → 浓 循环，模拟研墨的往复动作
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await target.FadeTo(0.45, length: 700, easing: Easing.SinInOut);
                        await target.FadeTo(1.0, length: 700, easing: Easing.SinInOut);
                    });
                }
                catch { return; }
            }
        }, token);

        return new CancellationTokenStartInfo { Source = cts };
    }

    /// <summary>
    /// 印章钤盖：按下缩小 + 回弹，似盖章用力
    /// </summary>
    public static async Task SealStampAsync(View seal)
    {
        const double original = 1.0;
        try
        {
            await seal.ScaleTo(0.88, length: Quick, easing: Easing.CubicOut);
            await seal.ScaleTo(1.06, length: Normal, easing: Easing.SpringOut);
            await seal.ScaleTo(original, length: Quick, easing: Easing.SinInOut);
        }
        catch { /* 页面切换时忽略 */ }
    }

    /// <summary>
    /// 气泡浮入：透明 + 下移 8px → 上浮淡入到位
    /// </summary>
    public static async Task BubbleFloatInAsync(View bubble)
    {
        bubble.TranslationY = 10;
        bubble.Opacity = 0;
        try
        {
            await Task.WhenAll(
                bubble.TranslateTo(0, 0, length: Normal, easing: Easing.SinOut),
                bubble.FadeTo(1, length: Normal, easing: Easing.SinIn));
        }
        catch { /* 页面切换时忽略 */ }
    }

    /// <summary>
    /// 墨点旋转指示：让"研墨构思中"三个墨点依次淡入淡出
    /// </summary>
    public static CancellationTokenStartInfo InkDotsCycling(IReadOnlyList<Label> dots)
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            var idx = 0;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var current = idx;
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (current < dots.Count)
                        {
                            await dots[current].FadeTo(1, length: 200, easing: Easing.SinIn);
                            await dots[current].FadeTo(0.15, length: 200, easing: Easing.SinOut);
                        }
                    });
                    idx = (idx + 1) % Math.Max(dots.Count, 1);
                }
                catch { return; }
            }
        }, token);

        return new CancellationTokenStartInfo { Source = cts };
    }

    /// <summary>印章呼吸微光（顶栏印章常驻动效：极轻微的缩放循环）</summary>
    public static CancellationTokenStartInfo SealBreathing(View seal)
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await seal.ScaleTo(1.03, length: 1400, easing: Easing.SinInOut);
                        await seal.ScaleTo(1.0, length: 1400, easing: Easing.SinInOut);
                    });
                }
                catch { return; }
            }
        }, token);

        return new CancellationTokenStartInfo { Source = cts };
    }

    /// <summary>停止并释放动画</summary>
    public static void Stop(CancellationTokenStartInfo? info)
    {
        try { info?.Source.Cancel(); info?.Source.Dispose(); }
        catch { }
    }
}

/// <summary>动画句柄：持有取消令牌</summary>
public class CancellationTokenStartInfo
{
    public required CancellationTokenSource Source { get; init; }
}
