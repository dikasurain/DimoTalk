using DimoTalk.Maui.Config;
using DimoTalk.Maui.Services;
using DimoTalk.Maui.Services.AI;
using DimoTalk.Maui.Services.Memory;
using Microsoft.Maui.ApplicationModel;

namespace DimoTalk.Maui.Pages;

public partial class MemoryPage : ContentPage
{
    private readonly MemoryManager? _memory;
    private readonly AutobiographyService? _autobiography;
    private readonly ProtagonistProfileService? _profileService;
    private readonly Func<string> _getApiKey;

    private List<ChapterInfo> _chapters = new();
    private CancellationTokenSource? _generateCts;

    public MemoryPage(MemoryManager? memory, AutobiographyService? autobiography, OpenAIClient? ai, Func<string> getApiKey)
    {
        InitializeComponent();
        _memory = memory;
        _autobiography = autobiography;
        _getApiKey = getApiKey;
        if (memory != null && ai != null)
            _profileService = new ProtagonistProfileService(memory, ai);

        // Soul 模板 Picker：4 预设 + 自定义
        var soulItems = new List<string>(SoulRegistry.All.Select(s => s.Name)) { "自定义灵魂" };
        SoulPicker.ItemsSource = soulItems;
        var current = SoulRegistry.Current();
        var idx = SoulRegistry.All.ToList().FindIndex(s => s.Key == current.Key);
        SoulPicker.SelectedIndex = idx >= 0 ? idx : soulItems.Count - 1;
        SoulPicker.SelectedIndexChanged += OnSoulPickerChanged;
        ApplySoulSelection();

        // 逐章进度回调
        if (_autobiography != null)
            _autobiography.ChapterProgress += OnChapterProgress;
    }

    private static string GetUserId() =>
        Preferences.Default.Get("user_id", Guid.NewGuid().ToString());

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadProfile();
        LoadChapters();
    }

    // ═══ 主人公画像 ═══

    private void LoadProfile()
    {
        if (_profileService == null) return;
        var profile = _profileService.Get(GetUserId());
        RenderProfile(profile);
    }

    private void RenderProfile(ProtagonistProfile? profile)
    {
        SkillsWrap.Children.Clear();
        if (profile == null)
        {
            ProfileSummaryLabel.Text = "尚未判定——先和我聊聊天，攒些记忆。";
            ProfileUpdatedLabel.IsVisible = false;
            return;
        }

        ProfileSummaryLabel.Text = string.IsNullOrWhiteSpace(profile.Summary) ? "画像为空。" : profile.Summary;
        foreach (var skill in profile.Skills)
        {
            var badge = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(4) },
                BackgroundColor = (Color)Application.Current!.Resources["CinnabarLight"],
                Padding = new Thickness(10, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Content = new Label
                {
                    Text = skill.Name,
                    FontSize = 12,
                    TextColor = (Color)Application.Current.Resources["AccentRed"],
                },
            };
            SkillsWrap.Children.Add(badge);
        }
        ProfileUpdatedLabel.IsVisible = true;
        ProfileUpdatedLabel.Text = $"判定时间：{profile.UpdatedAt:MM-dd HH:mm}";
    }

    private async void OnAnalyzeProfileClicked(object? sender, EventArgs e)
    {
        if (!CheckReady()) return;
        try
        {
            AnalyzeProfileButton.IsEnabled = false;
            ProfileSummaryLabel.Text = "正在翻阅记忆，为主人公画像……";
            var profile = await _profileService!.AnalyzeAsync(GetUserId());
            RenderProfile(profile);
        }
        catch (Exception ex)
        {
            await DisplayAlert("判定失败", ex.Message, "知道了");
            LoadProfile();
        }
        finally
        {
            AnalyzeProfileButton.IsEnabled = true;
        }
    }

    // ═══ 自传 ═══

    private void LoadChapters()
    {
        if (_memory == null) return;
        _chapters = _memory.Autobiography.LoadChapters(GetUserId());
        RenderChapters();
    }

    private void RenderChapters()
    {
        ChaptersList.Children.Clear();
        if (_chapters.Count == 0)
        {
            AutoStatusLabel.Text = "还没有自传。点「生成自传」，我会从记忆里为你写成一本小书。";
            ExportButton.IsEnabled = false;
            return;
        }

        foreach (var ch in _chapters)
        {
            var row = new Border
            {
                StrokeThickness = 1,
                Stroke = (Color)Application.Current!.Resources["Divider"],
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(6) },
                BackgroundColor = (Color)Application.Current.Resources["PageBackground"],
                Padding = new Thickness(12, 8),
                Content = new Label
                {
                    Text = $"第{ch.Index}章　{ch.Title}",
                    FontSize = 13,
                    TextColor = (Color)Application.Current.Resources["InkMedium"],
                },
            };
            var captured = ch;
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await OpenChapterAsync(captured);
            row.GestureRecognizers.Add(tap);
            ChaptersList.Children.Add(row);
        }

        AutoStatusLabel.Text = $"已成书 {_chapters.Count} 章。点章节阅读全文，或导出 docx。";
        ExportButton.IsEnabled = true;
    }

    private async void OnChapterProgress(int current, int total, string title)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            AutoStatusLabel.Text = $"正在写第 {current}/{total} 章：{title}……";
            GenProgressBar.Progress = (double)current / total;
        });
    }

    private async void OnGenerateClicked(object? sender, EventArgs e)
    {
        if (_autobiography == null || !CheckReady()) return;

        var confirm = await DisplayAlert("生成自传", "我会根据记忆判定你的画像，然后按章写成自传。\n每章都要调用 AI，大约需要几分钟，继续吗？", "开始", "再等等");
        if (!confirm) return;

        _generateCts = new CancellationTokenSource();
        GenerateButton.IsEnabled = false;
        GenProgressBar.IsVisible = true;
        GenProgressBar.Progress = 0;
        AutoStatusLabel.Text = "正在翻阅记忆、判定画像、搭章节大纲……";

        try
        {
            _chapters = await _autobiography.GenerateBookAsync(GetUserId(), _generateCts.Token);
            RenderChapters();
            await DisplayAlert("自传完成", $"共 {_chapters.Count} 章，已收录进记忆馆。", "好");
        }
        catch (OperationCanceledException)
        {
            AutoStatusLabel.Text = "已取消。";
        }
        catch (InvalidOperationException ex)
        {
            AutoStatusLabel.Text = ex.Message;
        }
        catch (Exception ex)
        {
            await DisplayAlert("生成失败", ex.Message, "知道了");
            AutoStatusLabel.Text = "生成中断，可重试。";
        }
        finally
        {
            GenerateButton.IsEnabled = true;
            GenProgressBar.IsVisible = false;
            _generateCts.Dispose();
            _generateCts = null;
        }
    }

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        if (_chapters.Count == 0) return;
        try
        {
            ExportButton.IsEnabled = false;
            var profile = _profileService?.Get(GetUserId());
            var path = DocxExporter.Export("我的自述", _chapters, profile?.Summary);
            var open = await DisplayAlert("导出成功", $"已生成：\n{Path.GetFileName(path)}\n\n现在打开吗？", "打开", "完成");
            if (open)
                await Launcher.OpenAsync(new OpenFileRequest { File = new ReadOnlyFile(path) });
        }
        catch (Exception ex)
        {
            await DisplayAlert("导出失败", ex.Message, "知道了");
        }
        finally
        {
            ExportButton.IsEnabled = true;
        }
    }

    /// <summary>章节全文页（轻量 C# 构建，阅读 + 复制）</summary>
    private async Task OpenChapterAsync(ChapterInfo chapter)
    {
        var contentLabel = new Label
        {
            Text = chapter.Content,
            FontSize = 15,
            LineHeight = 1.6d,
            TextColor = (Color)Application.Current!.Resources["TextPrimary"],
        };
        var scroll = new ScrollView { Content = new StackLayout { Children = { contentLabel } }, Padding = new Thickness(18, 14) };

        var page = new ContentPage
        {
            Title = $"第{chapter.Index}章 {chapter.Title}",
            BackgroundColor = (Color)Application.Current.Resources["PageBackground"],
            Content = new Grid
            {
                RowDefinitions = new RowDefinitionCollection { new(), new(GridLength.Auto) },
                Children =
                {
                    scroll,
                    new Button
                    {
                        Text = "复制本章",
                        Margin = new Thickness(16, 6, 16, 12),
                        CornerRadius = 6,
                        BackgroundColor = (Color)Application.Current.Resources["InkWash"],
                        TextColor = (Color)Application.Current.Resources["InkMedium"],
                        Command = new Command(async () =>
                        {
                            await Clipboard.SetTextAsync(chapter.Content);
                            await DisplayAlert("已复制", "本章全文已复制到剪贴板。", "好");
                        }),
                    },
                },
            },
        };
        // 按钮放第二行
        if (page.Content is Grid g && g.Children.Count > 1)
            Grid.SetRow((BindableObject)g.Children[1], 1);

        await Shell.Current.Navigation.PushAsync(page);
    }

    // ═══ Soul ═══

    private void OnSoulPickerChanged(object? sender, EventArgs e) => ApplySoulSelection();

    private void ApplySoulSelection()
    {
        var idx = SoulPicker.SelectedIndex;
        if (idx < 0) return;

        if (idx < SoulRegistry.All.Count)
        {
            var preset = SoulRegistry.All[idx];
            SoulDescLabel.Text = preset.Description;
            SoulPersonaEditor.Text = preset.Persona;
            SoulToneEntry.Text = preset.Tone;
        }
        else
        {
            // 自定义：加载已保存的自定义内容
            SoulDescLabel.Text = "手写你想要的灵魂";
            SoulPersonaEditor.Text = Preferences.Default.Get(SoulRegistry.CustomPersonaKey, "");
            SoulToneEntry.Text = Preferences.Default.Get(SoulRegistry.CustomToneKey, "");
        }
    }

    private void OnSaveSoulClicked(object? sender, EventArgs e)
    {
        var idx = SoulPicker.SelectedIndex;
        if (idx < 0) return;

        var persona = SoulPersonaEditor.Text?.Trim() ?? "";
        var tone = SoulToneEntry.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(persona))
        {
            DisplayAlert("灵魂是空的", "至少写一句本体人格。", "好");
            return;
        }

        if (idx < SoulRegistry.All.Count)
        {
            // 预设：允许用户微调后保存（写入自定义，保留预设选择入口）
            var preset = SoulRegistry.All[idx];
            SoulRegistry.SetCurrent(preset.Key, persona, tone);
        }
        else
        {
            SoulRegistry.SetCurrent(SoulRegistry.CustomKey, persona, tone);
        }
        DisplayAlert("灵魂已注入", "从下一句话开始，我就是这个灵魂了。", "好");
    }

    // ═══ 公共 ═══

    private bool CheckReady()
    {
        if (_getApiKey() is not { Length: > 10 } key)
        {
            DisplayAlert("缺钥匙", "请先在「设置」里填入 API Key。", "好");
            return false;
        }
        if (_memory == null || _profileService == null || _autobiography == null)
        {
            DisplayAlert("服务未就绪", "记忆服务未初始化，请重启应用。", "好");
            return false;
        }
        return true;
    }
}
