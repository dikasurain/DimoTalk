using DimoTalk.Maui.Config;
using DimoTalk.Maui.Services.AI;
using DimoTalk.Maui.Services.Voice;

namespace DimoTalk.Maui.Pages;

public partial class SettingsPage : ContentPage
{
    private UserAiConfig _config = UserAiConfig.Load();
    private DialectInfo? _currentDialect;
    private const string ScrollPosKey = "settings_scroll_y";

    // 唤醒词矫正测试用的依赖（DI 注入；为空时回退到无测试模式）
    private readonly IWakeWordDetector? _wakeDetector;
    private readonly ContinuousAudioCapture? _capture;
    // 测试期间累积最近一次 partial result 文本，供"用此字"按钮使用
    private string _lastHeard = string.Empty;
    private CancellationTokenSource? _testCts;

    public SettingsPage() : this(null, null) { }

    public SettingsPage(IWakeWordDetector? wakeDetector, ContinuousAudioCapture? capture)
    {
        _wakeDetector = wakeDetector;
        _capture = capture;
        InitializeComponent();

        // 服务商下拉
        ProviderPicker.ItemsSource = ProviderRegistry.All;
        var current = ProviderRegistry.FindByKey(_config.ProviderKey) ?? ProviderRegistry.OpenAI;
        ProviderPicker.SelectedItem = current;
        UpdateProviderDescription(current);

        // 方言/风格下拉
        DialectPicker.ItemsSource = DialectRegistry.All.ToList();
        var dialectKey = Preferences.Get("dialect", DialectRegistry.Mandarin.Key);
        _currentDialect = DialectRegistry.FindByKey(dialectKey);
        DialectPicker.SelectedItem = _currentDialect;
        DialectDescLabel.Text = _currentDialect.Description;
        DialectPicker.SelectedIndexChanged += (_, _) =>
        {
            if (DialectPicker.SelectedItem is DialectInfo d)
            {
                _currentDialect = d;
                DialectDescLabel.Text = d.Description;
            }
        };

        // API Key
        ApiKeyEntry.Text = _config.ApiKey;
        EndpointEntry.Text = _config.EndpointOverride;

        // 模型字段
        ChatModelEntry.Text = _config.ChatModel;
        EmbeddingModelEntry.Text = _config.EmbeddingModel;
        WhisperModelEntry.Text = _config.WhisperModel;
        TtsModelEntry.Text = _config.TtsModel;
        TtsVoicePicker.SelectedItem = _config.TtsVoice;

        // 语音设置
        WakeWordEntry.Text = Preferences.Get("wake_word", "滴墨");
        VoiceWakeSwitch.IsToggled = Preferences.Get("voice_wake_enabled", false);
        WakeAliasesEntry.Text = Preferences.Get("wake_word_aliases", string.Empty);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 恢复上次滚动位置
        var y = Preferences.Get(ScrollPosKey, 0.0);
        if (y > 0)
            _ = RootScrollView.ScrollToAsync(0, y, false);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // 保存滚动位置
        Preferences.Set(ScrollPosKey, RootScrollView.ScrollY);
        // 退出页面时确保测试已结束，释放麦克风
        StopWakeTest();
    }

    /// <summary>"听 5 秒" 按钮：启动临时 Vosk 测试，让用户看到 Vosk 实际听到了什么字</summary>
    private async void OnTestWakeClicked(object? sender, EventArgs e)
    {
        if (_wakeDetector == null || _capture == null)
        {
            await DisplayAlert("提示", "当前平台不支持唤醒词测试（仅配置了 Vosk 的平台可用）", "确定");
            return;
        }
        if (_testCts != null) return;  // 已在测试中

        _lastHeard = string.Empty;
        HeardLabel.Text = "正在聆听… 喊一声唤醒词";
        AddHeardBtn.IsEnabled = false;
        TestWakeBtn.IsEnabled = false;

        // 用当前唤醒词 + 已有 aliases 做 grammar 词表（保持与主对话一致的语境）
        var wakeWord = string.IsNullOrWhiteSpace(WakeWordEntry.Text) ? "滴墨" : WakeWordEntry.Text.Trim();
        _wakeDetector.WakeWord = wakeWord;
        if (_wakeDetector is VoskWakeWordDetector vosk)
        {
            vosk.Aliases.Clear();
            vosk.Aliases.Add(wakeWord);
            var existing = WakeAliasesEntry.Text?.Split(',', ';')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s)) ?? Array.Empty<string>();
            foreach (var a in existing) vosk.Aliases.Add(a);
        }

        EventHandler<string> handler = (_, text) =>
        {
            // 主线程更新 UI；过滤 [unk] 噪声
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (string.IsNullOrEmpty(text) || text == "[unk]")
                {
                    HeardLabel.Text = "（噪声/未识别）";
                }
                else
                {
                    _lastHeard = text;
                    HeardLabel.Text = $"听到：{text}";
                    AddHeardBtn.IsEnabled = true;
                }
            });
        };
        _wakeDetector.PartialResultReceived += handler;

        _testCts = new CancellationTokenSource();
        try
        {
            await _capture.StartAsync();
            await _wakeDetector.StartAsync(() => Task.CompletedTask, _testCts.Token);

            // 5 秒后自动停止
            await Task.Delay(5000, _testCts.Token);
        }
        catch (OperationCanceledException) { /* 正常结束 */ }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"唤醒词测试失败: {ex.Message}");
            await DisplayAlert("测试失败", ex.Message, "确定");
        }
        finally
        {
            _wakeDetector.PartialResultReceived -= handler;
            try { await _wakeDetector.StopAsync(); } catch { }
            // 不停 _capture：让其他订阅者继续可用，麦克风由 ConversationManager 统一管理
            _testCts?.Dispose();
            _testCts = null;
            TestWakeBtn.IsEnabled = true;
            if (string.IsNullOrEmpty(_lastHeard))
                HeardLabel.Text = "（未识别到内容）";
        }
    }

    private void StopWakeTest()
    {
        if (_testCts == null) return;
        try { _testCts.Cancel(); } catch { }
    }

    /// <summary>"用此字" 按钮：把 Vosk 听到的字加入候选同音字列表</summary>
    private void OnAddHeardClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_lastHeard))
        {
            DisplayAlert("提示", "还没听到内容，先点'听 5 秒'", "确定");
            return;
        }
        // 避免重复
        var existing = (WakeAliasesEntry.Text ?? string.Empty)
            .Split(',', ';').Select(s => s.Trim()).ToList();
        if (existing.Contains(_lastHeard))
        {
            DisplayAlert("已存在", $"候选词中已有「{_lastHeard}」", "确定");
            return;
        }
        WakeAliasesEntry.Text = string.IsNullOrWhiteSpace(WakeAliasesEntry.Text)
            ? _lastHeard
            : $"{WakeAliasesEntry.Text.Trim()}, {_lastHeard}";
        AddHeardBtn.IsEnabled = false;
        _lastHeard = string.Empty;
        HeardLabel.Text = "已加入候选，保存后生效";
    }

    private void OnProviderChanged(object? sender, EventArgs e)
    {
        if (ProviderPicker.SelectedItem is not AiProvider provider) return;
        _config.ProviderKey = provider.Key;

        UpdateProviderDescription(provider);

        // 仅 custom 显示 Endpoint 输入
        var isCustom = provider.Key == "custom";
        EndpointLabel.IsVisible = isCustom;
        EndpointBorder.IsVisible = isCustom;

        // 切换服务商时，用对应默认值填充模型字段（用户仍可编辑覆盖）
        if (!isCustom)
        {
            if (!string.IsNullOrEmpty(provider.DefaultChatModel) && string.IsNullOrEmpty(ChatModelEntry.Text))
                ChatModelEntry.Text = provider.DefaultChatModel;
            if (!string.IsNullOrEmpty(provider.DefaultEmbeddingModel))
                EmbeddingModelEntry.Text = provider.DefaultEmbeddingModel;
            if (!string.IsNullOrEmpty(provider.DefaultWhisperModel))
                WhisperModelEntry.Text = provider.DefaultWhisperModel;
            if (!string.IsNullOrEmpty(provider.DefaultTtsModel))
                TtsModelEntry.Text = provider.DefaultTtsModel;
            if (!string.IsNullOrEmpty(provider.DefaultTtsVoice))
                TtsVoicePicker.SelectedItem = provider.DefaultTtsVoice;
        }
    }

    private void OnChatModelChanged(object? sender, TextChangedEventArgs e)
    {
        // 输入模型名时自动路由到对应服务商
        var text = e.NewTextValue?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var guess = ProviderRegistry.GuessByModelName(text);
        if (guess == null) return;

        // 静默更新 Provider（不触发 OnProviderChanged 的默认填充）
        _config.ProviderKey = guess.Key;
        var idx = Array.FindIndex(ProviderRegistry.All, p => p.Key == guess.Key);
        if (idx >= 0) ProviderPicker.SelectedIndex = idx;
        UpdateProviderDescription(guess);
    }

    private void UpdateProviderDescription(AiProvider provider)
    {
        ProviderDescription.Text = provider.Description;
        WebsiteLink.Text = provider.Website;
        WebsiteLink.IsVisible = !string.IsNullOrEmpty(provider.Website);
    }

    private async void OnWebsiteTapped(object? sender, EventArgs e)
    {
        var url = WebsiteLink.Text;
        if (string.IsNullOrEmpty(url)) return;
        try { await Browser.OpenAsync(url, BrowserLaunchMode.External); }
        catch { /* 平台不支持 */ }
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        _config.ApiKey = ApiKeyEntry.Text?.Trim() ?? string.Empty;
        _config.EndpointOverride = EndpointEntry.Text?.Trim() ?? string.Empty;
        _config.ChatModel = ChatModelEntry.Text?.Trim() ?? _config.ChatModel;
        _config.EmbeddingModel = EmbeddingModelEntry.Text?.Trim() ?? _config.EmbeddingModel;
        _config.WhisperModel = WhisperModelEntry.Text?.Trim() ?? _config.WhisperModel;
        _config.TtsModel = TtsModelEntry.Text?.Trim() ?? _config.TtsModel;
        _config.TtsVoice = TtsVoicePicker.SelectedItem as string ?? "alloy";

        _config.Save();

        // 兼容旧字段
        Preferences.Set("openai_api_key", _config.ApiKey);

        // 语音设置
        var wakeWord = WakeWordEntry.Text?.Trim();
        Preferences.Set("wake_word", string.IsNullOrEmpty(wakeWord) ? "滴墨" : wakeWord);
        Preferences.Set("voice_wake_enabled", VoiceWakeSwitch.IsToggled);
        Preferences.Set("wake_word_aliases", WakeAliasesEntry.Text?.Trim() ?? string.Empty);

        // 方言/风格
        if (DialectPicker.SelectedItem is DialectInfo d)
            Preferences.Set("dialect", d.Key);

        DisplayAlert("提示", "配置已保存", "确定");
    }

    private void OnResetClicked(object? sender, EventArgs e)
    {
        _config = new UserAiConfig();
        ProviderPicker.SelectedItem = ProviderRegistry.OpenAI;
        ApiKeyEntry.Text = string.Empty;
        EndpointEntry.Text = string.Empty;
        ChatModelEntry.Text = _config.ChatModel;
        EmbeddingModelEntry.Text = _config.EmbeddingModel;
        WhisperModelEntry.Text = _config.WhisperModel;
        TtsModelEntry.Text = _config.TtsModel;
        TtsVoicePicker.SelectedItem = _config.TtsVoice;
        WakeWordEntry.Text = "滴墨";
        VoiceWakeSwitch.IsToggled = false;
        WakeAliasesEntry.Text = string.Empty;
        DialectPicker.SelectedItem = DialectRegistry.Mandarin;

        Preferences.Set("wake_word", "滴墨");
        Preferences.Set("voice_wake_enabled", false);
        Preferences.Set("wake_word_aliases", string.Empty);
        Preferences.Set("dialect", DialectRegistry.Mandarin.Key);
    }

    /// <summary>语音链路自测：选一段音频直接走 ASR → GPT → TTS → 播放，用于定位闪退</summary>
    private async void OnAudioTestClicked(object? sender, EventArgs e)
    {
        try
        {
            byte[] bytes;
            string? quick = null;
#if ANDROID
            // adb push 落在 app 外部专属目录（无需权限，app 自己可读）
            quick = Path.Combine(
                Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath ?? "",
                "test.mp3");
#endif
            if (quick != null && File.Exists(quick))
            {
                bytes = await File.ReadAllBytesAsync(quick);
            }
            else
            {
                var customType = new FilePickerFileType(
                    new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        [DevicePlatform.Android] = new[] { "audio/mpeg", "audio/wav", "audio/x-wav" },
                        [DevicePlatform.WinUI] = new[] { ".mp3", ".wav" },
                    });
                var file = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "选择测试音频",
                    FileTypes = customType,
                });
                if (file == null) return;

                await using var stream = await file.OpenReadAsync();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            var manager = Handler?.MauiContext?.Services.GetService<Services.Voice.VoiceConversationManager>();
            if (manager == null)
            {
                await DisplayAlert("提示", "语音服务未就绪", "好");
                return;
            }

            await DisplayAlert("自测开始", $"音频 {bytes.Length / 1024} KB，正在走完整链路：识别 → 回复 → 播放", "好");
            await manager.TestWithAudioAsync(bytes);
            await DisplayAlert("自测完成", "全链路跑完未崩溃", "好");
        }
        catch (Exception ex)
        {
            await DisplayAlert("自测异常", ex.Message, "好");
        }
    }
}
