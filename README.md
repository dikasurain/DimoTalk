# 滴墨讲
<p align="center">
  <img src="https://img.shields.io/badge/功能-聊天-pink" alt="React"/>
  <img src="https://img.shields.io/badge/Programming_Language-NiFU-blue" alt="React"/>
  <br>
  <img src="https://img.shields.io/github/last-commit/dikasurain/DimoTalk" alt="last-commit" />
</p>
<h1 align="center">功能描述</h1>

## 一、项目概述

滴墨讲（DimoTalk）是一个语音驱动的 AI 交流系统：用户通过手机客户端或硬件设备进行语音输入，经语音识别后接入 OpenAI API 获取回复，最终语音输出给另一端用户。核心差异化在于**自建三层记忆系统**，让 AI 具备跨会话的长期记忆能力。

## 二、整体架构

```
┌─────────────┐     ┌──────────────────────┐     ┌─────────────┐
│  用户A       │────>│  手机客户端 (Flutter)  │────>│  OpenAI API │
│ (语音输入)   │<────│  ├─ 三层记忆系统        │<────│  (GPT-4o等) │
└─────────────┘     │  ├─ 语音识别/合成       │     └─────────────┘
                    │  └─ 硬件设备接入(后续)   │
                    └──────────────────────┘
```

当前阶段：
- **手机客户端（Flutter）**：当前开发重点，自建记忆系统，本地存储。
- **服务器端**：暂不自建，直接接入 OpenAI API。后续可能增加自有服务器做记忆同步。
- **硬件 WiFi 接入**：后续配合硬件设备完善，客户端主动发现并连接硬件。

## 三、技术选型

| 模块 | 选型 | 说明 |
|------|------|------|
| 客户端框架 | Flutter | 跨平台（iOS + Android），Dart 单语言 |
| 语音识别 | 先 OpenAI Whisper API，后续可离线 | 文本输入可先跑通闭环 |
| TTS | OpenAI TTS / 系统 TTS | 合成语音输出 |
| 记忆系统 | **自建三层** | 短期上下文 + 中期摘要 + 长期向量 |
| 短期记忆 | 内存 FIFO 窗口（最近 N 轮） | 随会话结束释放 |
| 中期记忆 | SQLite 结构化存储（会话摘要） | 定期压缩写入 |
| 长期记忆 | sqlite-vec 本地向量数据库 | 用户偏好/关键事实，向量检索 |
| AI 接入 | OpenAI API（GPT-4o / Whisper / TTS） | 直接调用，不建中转服务 |

## 四、记忆系统设计（核心）

> 记忆系统是本项目的差异化核心。三层设计确保 AI 既记得住当前对话，也记得住过去的重要信息。

### 4.1 三层架构总览

```
用户输入
  │
  ├─> [短期记忆] 上下文窗口（内存，最近 20 条 message）
  │    └─ 直接拼接到 prompt，保持对话连贯
  │
  ├─> [中期记忆] 会话摘要（SQLite，每次会话结束生成）
  │    └─ 将整段对话压缩为 200-500 字摘要，会话恢复时注入
  │
  ├─> [长期记忆] 向量库（sqlite-vec，跨会话持久）
  │    ├─ 写入：从对话中提取用户偏好/身份/事实，生成 embedding 存入
  │    └─ 检索：每次请求时，用用户当前输入做向量相似度搜索，Top-K 注入 prompt
  │
  └─> 组装最终 prompt → 发送 OpenAI API
```

### 4.2 短期记忆（ShortTermMemory）

- **存储**：内存 `List<Message>`，环形缓冲区
- **容量**：默认 20 条消息（约 8K tokens），可配置
- **生命周期**：随 `Conversation` 对象创建/销毁
- **职责**：保持对话的即时连贯性，避免上下文丢失

```dart
class ShortTermMemory {
  final List<Message> _window = [];
  final int maxSize;

  ShortTermMemory({this.maxSize = 20});

  void add(Message msg) {
    _window.add(msg);
    if (_window.length > maxSize) _window.removeAt(0);
  }

  List<Message> get context => List.unmodifiable(_window);
}
```

### 4.3 中期记忆（MidTermMemory）

- **存储**：SQLite，表 `conversation_summaries`
- **触发**：会话结束（用户关闭 / 超过 30 分钟无交互）
- **内容**：整段对话 → 调用 GPT-4o mini 压缩为 200-500 字摘要
- **注入时机**：会话恢复时，将上次会话摘要作为 system 消息的一部分

```sql
CREATE TABLE conversation_summaries (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  conversation_id TEXT NOT NULL,
  user_id TEXT NOT NULL,
  summary TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  topic TEXT
);
CREATE INDEX idx_summaries_user_time ON conversation_summaries(user_id, created_at);
```

```dart
class MidTermMemory {
  Future<void> summarize(String conversationId, List<Message> fullHistory) async {
    final summary = await _gptCompress(fullHistory);
    await db.insert('conversation_summaries', {
      'conversation_id': conversationId,
      'summary': summary,
      'created_at': DateTime.now().millisecondsSinceEpoch,
    });
  }

  Future<List<String>> recall(String userId, {int limit = 3}) async {
    // 返回最近 N 次会话摘要，注入当前 prompt
  }
}
```

### 4.4 长期记忆（LongTermMemory）

- **存储**：sqlite-vec（SQLite 扩展，本地向量数据库）
- **写入**：每条用户消息后，异步判断是否包含可持久化信息（偏好、身份、事实）
- **Embedding**：优先 OpenAI `text-embedding-3-small`，后续可换本地小模型
- **检索**：余弦相似度 Top-K（默认 K=5），相似度阈值 ≥ 0.7 才注入
- **遗忘策略**：超过 90 天未被检索的记忆标记为衰减，低置信度记忆可被新信息覆盖

```sql
CREATE TABLE long_term_memories (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  user_id TEXT NOT NULL,
  content TEXT NOT NULL,
  embedding BLOB NOT NULL,
  source_message_id TEXT,
  confidence REAL DEFAULT 1.0,
  last_accessed_at INTEGER,
  created_at INTEGER NOT NULL
);
CREATE INDEX idx_ltm_user ON long_term_memories(user_id);
-- sqlite-vec 自动创建向量索引
```

```dart
class LongTermMemory {
  Future<void> store(String userId, String content, {double confidence = 1.0}) async {
    final embedding = await _embed(content);
    await db.insert('long_term_memories', {
      'user_id': userId,
      'content': content,
      'embedding': embedding,
      'confidence': confidence,
      'created_at': DateTime.now().millisecondsSinceEpoch,
    });
  }

  Future<List<MemoryHit>> recall(String userId, String query, {int topK = 5}) async {
    final queryEmbedding = await _embed(query);
    // sqlite-vec 向量相似度搜索
    final rows = await db.rawQuery('''
      SELECT *, vec_distance_cosine(embedding, ?) AS distance
      FROM long_term_memories
      WHERE user_id = ? AND distance < 0.3
      ORDER BY distance ASC
      LIMIT ?
    ''', [queryEmbedding, userId, topK]);
    return rows.map((r) => MemoryHit.fromMap(r)).toList();
  }
}
```

### 4.5 Prompt 组装

```dart
String buildPrompt({
  required String userInput,
  required ShortTermMemory shortTerm,
  required MidTermMemory midTerm,
  required LongTermMemory longTerm,
  required String systemPrompt,
}) {
  final buffer = StringBuffer(systemPrompt);

  // 1. 长期记忆注入（身份/偏好/事实）
  final ltmHits = await longTerm.recall(userId, userInput);
  if (ltmHits.isNotEmpty) {
    buffer.writeln('\n[你知道关于用户的以下信息，请在回答中自然运用]');
    for (final hit in ltmHits) {
      buffer.writeln('- ${hit.content}');
    }
  }

  // 2. 中期记忆注入（上次会话摘要）
  final recentSummaries = await midTerm.recall(userId);
  if (recentSummaries.isNotEmpty) {
    buffer.writeln('\n[上次对话回顾]');
    buffer.writeln(recentSummaries.join('\n'));
  }

  // 3. 短期上下文 + 当前用户输入
  buffer.writeln('\n[当前对话]');
  for (final msg in shortTerm.context) {
    buffer.writeln('${msg.role}: ${msg.content}');
  }
  buffer.writeln('user: $userInput');

  return buffer.toString();
}
```

### 4.6 记忆提取流程

```
用户发送消息
    │
    ▼
短期记忆.add(userMsg)
    │
    ├─> [同步] 判断是否值得写入长期记忆
    │     ├─ 规则启发式：包含"我叫"、"我喜欢"、"我住在"等关键词
    │     └─ GPT 判断（低成本）："这条消息是否包含用户的偏好/身份/事实？"
    │           是 → 提取要点 → LongTermMemory.store()
    │
    ▼
调用 OpenAI API
    │
    ▼
shortTerm.add(assistantReply)
    │
    └─> [异步] 会话结束时 → MidTermMemory.summarize()
```

## 五、手机客户端功能模块

### 5.1 核心模块
| 模块 | 说明 |
|------|------|
| **Chat** | 对话界面，消息气泡、流式回复、语音输入按钮 |
| **Memory** | 三层记忆系统（见第四章） |
| **AI** | OpenAI API 封装（GPT / Whisper / TTS / Embedding） |
| **Voice** | 语音识别（microphone + Whisper）与语音合成（TTS） |
| **Conversation** | 会话管理：新建/切换/历史列表/恢复 |
| **Settings** | API Key 配置、记忆开关、语音参数 |

### 5.2 目录结构（Flutter）
```
Hardwaver/                    # Flutter 项目根（手机客户端）
├── lib/
│   ├── main.dart
│   ├── app.dart
│   ├── config/               # API key、模型名等
│   ├── models/               # Message / Conversation / MemoryHit
│   ├── services/             # 业务逻辑
│   │   ├── memory/           # ★ 三层记忆系统
│   │   │   ├── short_term.dart
│   │   │   ├── mid_term.dart
│   │   │   ├── long_term.dart
│   │   │   └── memory_manager.dart
│   │   ├── ai/               # OpenAI API 封装
│   │   │   ├── openai_client.dart
│   │   │   └── prompt_builder.dart
│   │   └── conversation.dart
│   ├── pages/                # UI
│   │   ├── chat_page.dart
│   │   ├── conversation_list_page.dart
│   │   └── settings_page.dart
│   └── widgets/
├── pubspec.yaml
└── README.md
```

## 六、后续规划

### Phase 1：最小闭环（当前）
- [ ] Flutter 项目骨架搭建
- [ ] OpenAI API 封装（GPT-4o chat）
- [ ] 短期记忆（上下文窗口）
- [ ] Chat 基础界面（文本输入 + 显示回复）
- [ ] 跑通：输入 → API → 回复

### Phase 2：记忆系统
- [ ] 中期记忆（会话摘要 + SQLite）
- [ ] 长期记忆（sqlite-vec + Embedding 检索）
- [ ] 记忆提取与遗忘策略
- [ ] Prompt 组装器

### Phase 3：语音能力

> 当前的核心功能模块。三层架构：唤醒词 → 问答处理 → TTS 语音输出。

#### 3.1 语音交互流程

```
┌────────────────────────────────────────────────────────────┐
│  状态机：Idle → Listening → WakeWord → Processing → Reply │
└────────────────────────────────────────────────────────────┘

[Idle 待机]
  │ 持续监听麦克风（短帧缓冲）
  ▼
[Listening 监听中]
  │ 用本地唤醒词检测器持续识别关键词（如"滴墨"、"DimoTalk"）
  │   ├─ Vosk 离线 Kaldi 模型（首选，无网络延迟，零成本）
  │   └─ 退化为周期性 Whisper API（成本高，仅测试用）
  ▼ 识别到唤醒词
[WakeWord 触发]
  │ 1. 短提示音反馈（如"叮"）确认唤醒
  │ 2. 切换到主动录音模式（持续录音直到静音 2 秒或超过 30 秒）
  ▼
[Processing 处理中]
  │ 1. ASR：将录音发送至 OpenAI Whisper API 转为文本
  │ 2. 走 ChatService.SendMessageAsync（三层记忆 + OpenAI GPT）
  ▼
[Reply 回复]
  │ 1. TTS：将 AI 回复文本发送至 OpenAI TTS API 合成语音
  │ 2. 播放合成音频
  │ 3. 回到 Listening 状态继续监听唤醒词

[中断条件]
  - 用户说"停"/"暂停" → 切换到 Idle
  - 应用进入后台 → Idle
  - 长时间无唤醒（如 5 分钟） → Idle
```

#### 3.2 唤醒词检测方案

| 方案 | 优点 | 劣势 | 适用场景 |
|------|------|------|----------|
| **Vosk 离线（推荐）** | 零延迟、零成本、离线可用 | 需打包中文声学模型（约 50MB） | 生产环境 |
| Whisper API 周期检测 | 简单、准确 | 高延迟、高成本、需联网 | 仅开发期验证 |
| 平台原生 SpeechRecognizer | 系统级、零依赖 | 各平台 API 不一致、关键词过滤麻烦 | 备选 |

#### 3.3 语音能力服务设计

```
Services/
├── Voice/
│   ├── IWakeWordDetector.cs          # 唤醒词检测接口（便于换实现）
│   ├── VoskWakeWordDetector.cs       # Vosk 实现
│   ├── WhisperAsrService.cs          # OpenAI Whisper ASR
│   ├── OpenAITtsService.cs           # OpenAI TTS
│   ├── AudioPlayer.cs                # 音频播放（含提示音）
│   └── VoiceConversationManager.cs  # 语音状态机编排
```

#### 3.4 任务清单
- [ ] 录音能力（Plugin.Maui.Audio 或平台原生）
- [ ] Vosk 唤醒词检测集成
- [ ] Whisper ASR 客户端
- [ ] OpenAI TTS 客户端
- [ ] 状态机编排（Idle/Listening/WakeWord/Processing/Reply）
- [ ] 唤醒提示音与回复播放
- [ ] Settings 中加入唤醒词与语音开关

### Phase 4：硬件接入
- [ ] 设备发现（UDP / mDNS）
- [ ] TCP 控制通道 + UDP 音频通道
- [ ] 硬件端语音转发

### Phase 5：云端同步
- [ ] 自有服务器（记忆同步 / 多设备）
- [ ] 用户账号系统
