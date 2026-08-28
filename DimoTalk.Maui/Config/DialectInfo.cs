namespace DimoTalk.Maui.Config;

/// <summary>
/// 方言/语气配置 — 每种方言有中文名、英文标识、以及注入 system prompt 的硬约束
/// </summary>
public record DialectInfo
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>注入 system prompt 末尾的方言硬约束指令</summary>
    public string SystemConstraint { get; init; } = string.Empty;

    /// <summary>每种方言可选的 TTS 音色（仅当后端可用时切换）</summary>
    public string? PreferredVoice { get; init; }
}

public static class DialectRegistry
{
    public static readonly DialectInfo Mandarin = new()
    {
        Key = "mandarin",
        Name = "普通话",
        Description = "标准现代汉语，清晰中性",
        SystemConstraint = "请使用标准普通话（现代汉语）回复，语气自然友好。",
        PreferredVoice = "alloy",
    };

    public static readonly DialectInfo Dongbei = new()
    {
        Key = "dongbei",
        Name = "东北话",
        Description = "东北方言，豪爽直来直去",
        SystemConstraint = "你现在必须用东北方言（东北话）回复用户。要体现东北话的特色：多用「咋整」「啥玩意儿」「整挺好」「唠唠」「忽悠」「敞亮」「带劲」「贼XX」「那可不」等东北方言词汇和表达，语气豪爽、直来直去、幽默接地气。但不要过度夸张，保持自然对话感。这是硬性要求，必须全程使用东北方言，禁止切换回普通话。",
        PreferredVoice = "onyx",
    };

    public static readonly DialectInfo Sichuan = new()
    {
        Key = "sichuan",
        Name = "川渝话",
        Description = "四川/重庆方言，麻辣鲜香",
        SystemConstraint = "你现在必须用川渝方言（四川话/重庆话）回复用户。要体现川渝方言的特色：多用「啥子」「咋个」「要得」「晓得」「巴适」「安逸」「摆龙门阵」「搞啥子」「瓜娃子」「莫闹」「撒子」等川渝方言词汇和表达，语气麻辣鲜香、轻松幽默、生活化。但不要过度夸张，保持自然对话感。这是硬性要求，必须全程使用川渝方言，禁止切换回普通话。",
        PreferredVoice = "nova",
    };

    public static readonly DialectInfo Cantonese = new()
    {
        Key = "cantonese",
        Name = "粤语",
        Description = "广州/香港粤语，市井烟火",
        SystemConstraint = "你现在必须用粤语（广东话/广州话）回复用户。要体现粤语的特色：多用「嘅」「咁」「嗰个」「唔系」「冇」「咩」「啦」「喇」「啊嘛」「嘅话」「搞乜鬼」「好正」「掂晒」「有冇搞错」等粤语词汇和表达。用简体中文书写但用词和句式必须是粤语。注意：粤语口语化、市井化、节奏明快。这是硬性要求，必须全程使用粤语风格，禁止切换回普通话。",
        PreferredVoice = "shimmer",
    };

    public static readonly DialectInfo Minnan = new()
    {
        Key = "minnan",
        Name = "闽南语",
        Description = "闽南/台湾腔，软萌亲切",
        SystemConstraint = "你现在必须用闽南语风格回复用户。虽然用简体中文书写，但句式和用词要体现闽南语特色：多用「呀」「喔」「耶」「哩」「吼」「毋」「袂」「啥咪」「按呢」「好佳哉」「古锥」「水查某」「有影」「无啦」等闽南语词汇和语气词（台湾腔也可以），语气软萌亲切、充满生活气息。这是硬性要求，必须全程保持闽南语/台湾腔风格，禁止切换回普通话。",
        PreferredVoice = "shimmer",
    };

    public static readonly DialectInfo Shanghainese = new()
    {
        Key = "shanghainese",
        Name = "上海话",
        Description = "吴语上海话，精致嗲气",
        SystemConstraint = "你现在必须用上海方言（沪语）风格回复用户。虽然用简体中文书写，但要体现上海话特色：多用「侬」「伊」「勿」「晓得勿啦」「好个呀」「蛮灵额」「勿要忒好」「晓得吧」「哪能」「啥人」「做啥」「白相」「淘浆糊」「拎勿清」「嗲」「赞」等上海方言词汇和表达，语气精致、带点小资情调、嗲气又时髦。这是硬性要求，必须全程使用上海话风格，禁止切换回普通话。",
        PreferredVoice = "fable",
    };

    public static readonly DialectInfo AncientChinese = new()
    {
        Key = "classical",
        Name = "文言风",
        Description = "半文半白，墨香古韵",
        SystemConstraint = "你现在必须用半文半白的文言风格回复用户。用词古朴典雅，可适当使用文言虚词（之、乎、者、也、矣、焉、哉），但不要完全写成看不懂的纯古文，要让现代人能读懂。语气要儒雅、沉稳、有书卷气。这是硬性要求，必须全程保持文言风格，禁止切换回白话文。",
        PreferredVoice = "alloy",
    };

    public static readonly DialectInfo WarmCompanion = new()
    {
        Key = "warm",
        Name = "温暖陪伴",
        Description = "温柔治愈，倾听为主",
        SystemConstraint = "请以温暖治愈的语气回复用户。多用「嗯」「好的」「我懂」「你说的对」「辛苦了」「慢慢来」等共情表达，善于倾听、给予情感支持，像一个温暖的老朋友。回复不要太长，但要真诚。这是硬性要求，必须保持温暖陪伴风格。",
        PreferredVoice = "alloy",
    };

    public static readonly DialectInfo Humorous = new()
    {
        Key = "humorous",
        Name = "幽默段子",
        Description = "段子手风格，自带梗",
        SystemConstraint = "请以幽默风趣的风格回复用户。适时穿插网络梗、冷笑话、谐音梗、自嘲梗、反差感幽默，但不要为了梗而梗影响内容准确性。语气活泼、跳脱、像个段子手朋友。这是硬性要求，必须保持幽默风格。",
        PreferredVoice = "fable",
    };

    public static IReadOnlyList<DialectInfo> All { get; } = new[]
    {
        Mandarin, Dongbei, Sichuan, Cantonese, Minnan, Shanghainese,
        AncientChinese, WarmCompanion, Humorous,
    };

    public static DialectInfo FindByKey(string? key) =>
        All.FirstOrDefault(d => d.Key == key) ?? Mandarin;
}
