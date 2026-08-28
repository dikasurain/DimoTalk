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
        LoadDiaries();
    }

    // ═══ 日记本（日历视图）═══

    private DateTime _calendarMonth = DateTime.Now;
    private Dictionary<string, DiaryInfo> _diaryMap = new();

    private void LoadDiaries()
    {
        if (_memory == null) return;
        var diaries = _memory.Autobiography.LoadDiaryList(GetUserId(), limit: 365);
        _diaryMap = diaries.ToDictionary(d => d.Date, d => d);
        RebuildCalendar();
    }

    /// <summary>重建日历：‹ 年月 › 头 + 七列网格，有日记的日子圆底可点</summary>
    private void RebuildCalendar()
    {
        DiaryList.Children.Clear();

        // ── 月份切换头 ──
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto),
            },
        };
        var prevBtn = new Button
        {
            Text = "‹", FontSize = 18, CornerRadius = 6, WidthRequest = 36, HeightRequest = 32, Padding = 0,
            BackgroundColor = (Color)Application.Current!.Resources["InkWash"],
            TextColor = (Color)Application.Current.Resources["InkMedium"],
        };
        prevBtn.Clicked += (_, _) => { _calendarMonth = _calendarMonth.AddMonths(-1); RebuildCalendar(); };
        var nextBtn = new Button
        {
            Text = "›", FontSize = 18, CornerRadius = 6, WidthRequest = 36, HeightRequest = 32, Padding = 0,
            BackgroundColor = (Color)Application.Current.Resources["InkWash"],
            TextColor = (Color)Application.Current.Resources["InkMedium"],
        };
        nextBtn.Clicked += (_, _) => { _calendarMonth = _calendarMonth.AddMonths(1); RebuildCalendar(); };
        var monthLabel = new Label
        {
            Text = $"{_calendarMonth.Year}年{_calendarMonth.Month}月",
            FontSize = 16, FontAttributes = FontAttributes.Bold,
            TextColor = (Color)Application.Current.Resources["InkHeavy"],
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
        };
        header.Add(prevBtn); Grid.SetColumn(prevBtn, 0);
        header.Add(monthLabel); Grid.SetColumn(monthLabel, 1);
        header.Add(nextBtn); Grid.SetColumn(nextBtn, 2);
        DiaryList.Children.Add(header);

        // ── 星期头 ──
        string[] weekNames = { "一", "二", "三", "四", "五", "六", "日" };
        var weekRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(
                Enumerable.Range(0, 7).Select(_ => new ColumnDefinition(GridLength.Star)).ToArray()),
            Margin = new Thickness(0, 8, 0, 0),
        };
        for (int i = 0; i < 7; i++)
        {
            weekRow.Add(new Label
            {
                Text = weekNames[i],
                FontSize = 11,
                TextColor = (Color)Application.Current.Resources["InkFaint"],
                HorizontalOptions = LayoutOptions.Center,
            }, i, 0);
        }
        DiaryList.Children.Add(weekRow);

        // ── 日历网格（6 行 × 7 列）──
        var today = DateTime.Today;
        var daysInMonth = DateTime.DaysInMonth(_calendarMonth.Year, _calendarMonth.Month);
        var startCol = ((int)new DateTime(_calendarMonth.Year, _calendarMonth.Month, 1).DayOfWeek + 6) % 7; // 周一=0

        var dayGrid = new Grid
        {
            RowDefinitions = new RowDefinitionCollection(
                Enumerable.Range(0, 6).Select(_ => new RowDefinition(40)).ToArray()),
            ColumnDefinitions = new ColumnDefinitionCollection(
                Enumerable.Range(0, 7).Select(_ => new ColumnDefinition(GridLength.Star)).ToArray()),
            Margin = new Thickness(0, 4, 0, 0),
        };

        for (int day = 1; day <= daysInMonth; day++)
        {
            int pos = startCol + day - 1;
            int row = pos / 7, col = pos % 7;
            var date = new DateTime(_calendarMonth.Year, _calendarMonth.Month, day);
            string dateKey = date.ToString("yyyy-MM-dd");

            View cellContent;
            if (_diaryMap.TryGetValue(dateKey, out var diary))
            {
                // 有日记：朱砂圆底白字，点击进详情
                var btn = new Border
                {
                    WidthRequest = 34, HeightRequest = 34,
                    StrokeThickness = 0,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
                    BackgroundColor = (Color)Application.Current.Resources["Cinnabar"],
                    HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                    Content = new Label
                    {
                        Text = day.ToString(),
                        FontSize = 13, FontAttributes = FontAttributes.Bold,
                        TextColor = Colors.White,
                        HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                    },
                };
                var captured = diary;
                var tap = new TapGestureRecognizer();
                tap.Tapped += async (_, _) => await OpenDiaryAsync(captured);
                btn.GestureRecognizers.Add(tap);
                cellContent = btn;
            }
            else if (date == today)
            {
                // 今天：墨色描边圆
                cellContent = new Border
                {
                    WidthRequest = 34, HeightRequest = 34,
                    StrokeThickness = 1.5f,
                    Stroke = (Color)Application.Current.Resources["InkMedium"],
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
                    BackgroundColor = Colors.Transparent,
                    HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                    Content = new Label
                    {
                        Text = day.ToString(),
                        FontSize = 13, FontAttributes = FontAttributes.Bold,
                        TextColor = (Color)Application.Current.Resources["InkMedium"],
                        HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                    },
                };
            }
            else
            {
                cellContent = new Label
                {
                    Text = day.ToString(),
                    FontSize = 13,
                    TextColor = (Color)Application.Current.Resources["InkFaint"],
                    HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                };
            }

            dayGrid.Add(cellContent, col, row);
        }
        DiaryList.Children.Add(dayGrid);

        // 提示行
        DiaryList.Children.Add(new Label
        {
            Text = _diaryMap.Count == 0
                ? "还没有日记。聊天后离开对话页，我会自动把当天写成日记。"
                : $"朱砂圆标 = 有日记的日子，共 {_diaryMap.Count} 篇。",
            FontSize = 11,
            TextColor = (Color)Application.Current.Resources["InkFaint"],
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 4, 0, 0),
        });
    }

    private static string FormatDiaryDate(string date)
    {
        if (DateTime.TryParse(date, out var dt))
            return $"{dt:M月d日} {dt:ddd}";
        return date;
    }

    /// <summary>日记详情：装饰头 + 日期 + 今日对话栏 + 正文卡片 + 页脚</summary>
    private async Task OpenDiaryAsync(DiaryInfo diary)
    {
        // 日期大字：8月28日 · 周四
        string bigDate = diary.Date;
        string week = "";
        if (DateTime.TryParse(diary.Date, out var dt))
        {
            bigDate = $"{dt.Month}月{dt.Day}日";
            week = $" · {dt:dddd}";
        }

        // 今日对话统计栏
        string chatMeta = "";
        var stats = _memory?.Autobiography.LoadDayStats(GetUserId(), diary.Date);
        if (stats != null && stats.Count > 0)
        {
            string span = "";
            if (DateTime.TryParse(stats.FirstTime, out var ft) && DateTime.TryParse(stats.LastTime, out var lt))
                span = ft.ToString("HH:mm") == lt.ToString("HH:mm") ? $" · {ft:HH:mm}" : $" · {ft:HH:mm} 至 {lt:HH:mm}";
            chatMeta = $"这一天我们聊了 {stats.Count} 句{span}";
        }

        var bodyLabel = new Label
        {
            Text = diary.Content,
            FontSize = 15,
            LineHeight = 1.7d,
            TextColor = (Color)Application.Current!.Resources["TextPrimary"],
        };
        var cardChildren = new List<IView>
        {
            // 顶部墨点装饰行
            new HorizontalStackLayout
            {
                Spacing = 5,
                Children =
                {
                    MakeInkDot("Cinnabar"),
                    MakeInkDot("InkMedium"),
                    MakeInkDot("Divider"),
                },
            },
            // 日期大标题
            new Label
            {
                Text = $"{bigDate}{week}",
                FontSize = 22,
                FontAttributes = FontAttributes.Bold,
                TextColor = (Color)Application.Current.Resources["InkHeavy"],
            },
        };

        // 今日对话栏（有聊天记录才显示）
        if (chatMeta.Length > 0)
        {
            cardChildren.Add(new Border
            {
                StrokeThickness = 1,
                Stroke = (Color)Application.Current.Resources["Divider"],
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(6) },
                BackgroundColor = (Color)Application.Current.Resources["InkWash"],
                Padding = new Thickness(12, 7),
                Content = new HorizontalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        new Label { Text = "对话", FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = (Color)Application.Current.Resources["Cinnabar"], VerticalOptions = LayoutOptions.Center },
                        new Label { Text = chatMeta, FontSize = 12, TextColor = (Color)Application.Current.Resources["TextSecondary"], VerticalOptions = LayoutOptions.Center },
                    },
                },
            });
        }

        cardChildren.Add(new BoxView { HeightRequest = 1, BackgroundColor = (Color)Application.Current.Resources["Divider"] });
        cardChildren.Add(bodyLabel);

        // 页脚
        if (DateTime.TryParse(diary.UpdatedAt, out var ua))
        {
            cardChildren.Add(new Label
            {
                Text = $"—— 滴墨讲 研墨记于 {ua:HH:mm}",
                FontSize = 11,
                TextColor = (Color)Application.Current.Resources["InkFaint"],
                HorizontalOptions = LayoutOptions.End,
            });
        }

        var card = new Border
        {
            StrokeThickness = 1,
            Stroke = (Color)Application.Current.Resources["Divider"],
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            BackgroundColor = (Color)Application.Current.Resources["Surface"],
            Padding = new Thickness(20, 18),
            Margin = new Thickness(16, 12),
            Content = new VerticalStackLayout { Spacing = 12, Children = { cardChildren[0], cardChildren[1], cardChildren[2] } },
        };
        // 追加剩余元素
        var body = (VerticalStackLayout)card.Content!;
        for (int i = 3; i < cardChildren.Count; i++)
            body.Children.Add(cardChildren[i]);

        await Shell.Current.Navigation.PushAsync(new ContentPage
        {
            Title = "日记",
            BackgroundColor = (Color)Application.Current.Resources["PageBackground"],
            Content = new ScrollView { Content = card },
        });
    }

    private static BoxView MakeInkDot(string resourceKey)
    {
        return new BoxView
        {
            WidthRequest = 8, HeightRequest = 8, CornerRadius = 4,
            Color = (Color)Application.Current!.Resources[resourceKey],
        };
    }

    private async void OnWriteDiaryClicked(object? sender, EventArgs e)
    {
        if (_autobiography == null || !CheckReady()) return;

        // 素材：当前短期记忆里的消息
        var msgs = _memory!.ShortTerm.Context;
        var userMsgs = msgs.Where(m => m.Role == DimoTalk.Maui.Models.MessageRole.User).Select(m => m.Content).ToList();
        var aiMsgs = msgs.Where(m => m.Role == DimoTalk.Maui.Models.MessageRole.Assistant).Select(m => m.Content).ToList();

        if (userMsgs.Count == 0)
        {
            await DisplayAlert("没素材", "当前会话还没有聊过天。先去对话页聊几句，或等会话自动收尾。", "好");
            return;
        }

        try
        {
            WriteDiaryButton.IsEnabled = false;
            var diary = await _autobiography.GenerateDiaryAsync(GetUserId(), userMsgs, aiMsgs);
            LoadDiaries();
            await DisplayAlert("日记已写", $"{FormatDiaryDate(diary.Date)} 的日记已入册。", "好");
        }
        catch (Exception ex)
        {
            await DisplayAlert("写日记失败", ex.Message, "知道了");
        }
        finally
        {
            WriteDiaryButton.IsEnabled = true;
        }
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
