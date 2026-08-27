# 滴墨讲（DimoTalk）- Flutter 客户端

语音驱动的 AI 交流系统，自建三层记忆系统。

## 快速开始

```bash
flutter pub get
flutter run
```

首次启动后，在「设置」中填入 OpenAI API Key。

## 三层记忆系统

| 层级 | 存储 | 容量 | 生命周期 |
|------|------|------|----------|
| 短期 | 内存 FIFO | 最近 20 条 | 会话内 |
| 中期 | SQLite 摘要 | 每次会话压缩 | 跨会话持久 |
| 长期 | sqlite-vec 向量 | 无限（带遗忘） | 永久 |

## 目录结构

```
lib/
├── main.dart                     # 入口
├── app.dart                      # 应用根（底部导航）
├── config/app_config.dart        # 常量配置
├── models/                       # Message / Conversation / MemoryHit
├── providers/providers.dart      # Riverpod 依赖注入
├── services/
│   ├── memory/                   # 三层记忆
│   │   ├── short_term.dart
│   │   ├── mid_term.dart
│   │   ├── long_term.dart
│   │   └── memory_manager.dart
│   ├── ai/                       # OpenAI 封装
│   │   ├── openai_client.dart
│   │   └── prompt_builder.dart
│   └── chat_service.dart         # 对话编排
└── pages/
    ├── chat_page.dart
    └── settings_page.dart
```
