# Codex 项目文档使用说明

这套文档的目标不是“介绍项目”，而是**让 Codex 在 Unity + Meta Quest + Unity MCP 的工作流里尽量少跑偏**。

你的当前最重要目标不是一次做完整系统，而是先稳定做出：

**房间风格化原型（Phase 1）**

也就是：
- 读取真实房间的空间信息
- 用 Meta 官方工具补充可见物体理解
- 按用户主题意图做风格化映射
- 在 MR 中把风格变化稳定地贴回真实房间
- 允许用户手动修正

NPC 互动放到 **Phase 2**。

---

## 文档结构

### 1. `AGENTS.md`
最重要。

这是给 Codex 看的“项目规则”。只要把它放到你的项目根目录，Codex 会在开始工作前自动读取。它会告诉 Codex：
- 项目当前优先级是什么
- 哪些事现在不能做
- 哪些 Meta 工具优先使用
- 改代码时应该遵守什么规则

### 2. `docs/01_PRODUCT_SCOPE_AND_SUCCESS.md`
定义项目范围、研究问题、当前阶段目标、验收标准。

### 3. `docs/02_ROOMIFY_TO_META_MAPPING.md`
这是最核心的“技术翻译文档”。

它把 Roomify 的四段式 pipeline 翻译成你现在能在 Quest / Unity / Meta 官方工具里实现的版本。

### 4. `docs/03_ARCHITECTURE_AND_SCENE_LAYOUT.md`
项目架构、模块职责、Unity 场景层级建议、脚本分层建议。

### 5. `docs/04_BACKLOG_AND_MILESTONES.md`
按阶段拆好的开发路线图。你之后和 Codex 协作，最好一轮只做这里面的一个小任务。

### 6. `docs/05_DATA_CONTRACTS.md`
定义 ThemeProfile、RoomObjectRecord、StylizationPlan 等数据结构。

### 7. `docs/06_CUSTOM_MCP_TOOLS.md`
建议你后续在 Unity 里自己注册的高层 MCP tools。这样 Codex 调 Unity 时不必总是靠底层 GameObject 操作。

### 8. `docs/07_CODEX_WORKFLOW_PROMPTS_CN.md`
可以直接复制到 Codex 里的中文提示词模板。

### 9. `docs/08_PROGRESS_STATUS.md`
项目当前完成进度、正在做的事情、主要风险、下一步最小任务。

### 10. `docs/09_GENERATIVE_OBJECT_PIPELINE.md`
如果你想让家具更接近 Roomify 论文里的“先生成风格图，再生成 3D 模型”的路线，这份文档就是接入方案。当前工程已经接入到 `DeepSeek -> APIMart gpt-image-2 -> hosted upload -> Seed3D` 的自动链路，但它仍然是 Phase 1 的可选生成物支线，不是确定性房间风格化主路径。

### 11. `docs/10_MANUAL_EXTERNAL_WORKER_RUNBOOK.md`
外部 worker 操作手册。现在包含两种用途：一是保留 `ExternalFileProtocol` 手工兜底流程，二是说明 APIMart 自动图像 worker 如何从 `CaptureReady` 走到 `StylizedImageReady`。

### 12. `docs/11_SMOKE_TEST_AND_DEMO_CHECKLIST.md`
每次演示或回归验证前使用的检查清单。它说明 Play 前、Play 中、按键、Console、输出文件应该如何确认。

### 13. `docs/12_TRUE_DEVICE_VALIDATION_PLAN.md`
从 `MetaXRSimulator` 切到 Quest 真机验证时使用。它区分 simulator 能验证什么、MQDH 能帮什么、哪些功能必须真机确认。

### 14. `.codex/config.toml.example`
一个项目级 Codex 配置示例。你已经连上 Unity MCP 了，这个文件主要是给你之后整理项目级配置用。

---

## 最推荐的使用方式

不要一上来对 Codex 说：

“帮我把整个项目做完。”

这样最容易跑偏。

你应该按下面顺序推进：

### 第一步：把文档放进你的 Unity 项目根目录
建议最终结构类似：

```text
YourUnityProject/
├─ AGENTS.md
├─ .codex/
│  └─ config.toml
├─ docs/
│  ├─ 01_PRODUCT_SCOPE_AND_SUCCESS.md
│  ├─ 02_ROOMIFY_TO_META_MAPPING.md
│  ├─ 03_ARCHITECTURE_AND_SCENE_LAYOUT.md
│  ├─ 04_BACKLOG_AND_MILESTONES.md
│  ├─ 05_DATA_CONTRACTS.md
│  ├─ 06_CUSTOM_MCP_TOOLS.md
│  ├─ 07_CODEX_WORKFLOW_PROMPTS_CN.md
│  ├─ 08_PROGRESS_STATUS.md
│  ├─ 09_GENERATIVE_OBJECT_PIPELINE.md
│  ├─ 10_MANUAL_EXTERNAL_WORKER_RUNBOOK.md
│  ├─ 11_SMOKE_TEST_AND_DEMO_CHECKLIST.md
│  └─ 12_TRUE_DEVICE_VALIDATION_PLAN.md
└─ Assets/
```

### 第二步：先让 Codex 只做“项目盘点”
先不要让它写代码。

你对 Codex 的第一句话建议是：

> Read AGENTS.md and docs/01, docs/02, docs/04 first. Then inspect the Unity project state, current package setup, existing scenes, and console. Do not change anything yet. Summarize the current state, risks, and the smallest next task.

### 第三步：一次只做一个 milestone 里的一个任务
最稳的节奏是：
- 先做 MRUK 语义可视化
- 再做 Image Segmentation / detection 融合
- 再做 ThemeProfile + stylization planner
- 再做房间风格应用
- 再做 correction mode

### 第四步：每次做完都让 Codex 汇报 4 件事
每一轮结束都让它明确写：
- 改了哪些文件
- 当前是否可编译
- 你现在要在 Unity 里如何手动验证
- 下一轮最小任务是什么

---

## 当前项目状态怎么读

这个项目现在已经不是空白起点。它已经有：
- `MR_RoomStylization.unity` canonical scene
- MRUK room semantic bootstrap
- theme / planner / surface stylization
- table proxy path
- `BestViewCaptureService`
- `DevicePassthroughCaptureService`
- `RuntimeStyleIntentController`
- `DeepSeekStyleIntentProvider`
- `GenerativeObjectCoordinator`
- `LocalGeneratedObjectBackendAdapter`
- `ApimartImageBackendAdapter`
- `HostedImageUploadBridge`
- `Seed3DBackendAdapter`
- `ExternalFileProtocol` 手工外部 worker 边界
- 一次已跑通的 `TABLE` 生成物支线：透明底 stylized image -> Seed3D 2.0 GLB -> Unity imported generated table prefab -> Simulator 中 `source=generated_import` 放置
- 一个已接入但仍待完整 live 验证的自动链路：freeform style intent -> DeepSeek 关键词拆解 -> APIMart `gpt-image-2` 生图 -> `www.mikusc.top` 上传 -> Ark Seed3D 2.0 -> Unity 导入 / 替换

但生成家具仍然不是 demo-ready 主路径：
- APIMart 到 Seed3D 的完整 Unity Play 闭环还需要用真实 `CaptureReady` job 再验证一次
- 生成家具的 accept / reject / reset 控制还没做
- Quest Link / Editor Play 可以验证 MRUK、HUD、best angle、job 状态和落位，但不能把 Quest Pro + Link 当成 native passthrough camera capture 的通过标准
- 当前还需要从目标视角做视觉审查，确认桌子尺度、贴地、遮挡和可读性

所以以后不要再把 “Milestone 1：MRUK 房间语义调试视图” 当作当前第一任务。
当前真实进度以 `docs/08_PROGRESS_STATUS.md` 为准。

---

## 关于 Image Segmentation 的定位

你截图里现在 Meta Building Blocks 里已经出现了 **Image Segmentation**。

在这套文档里，我把它定义为：

**房间风格化中的“补充感知层”**，不是主骨架层。

也就是说：
- **MRUK** 负责稳定的房间结构和语义 anchor
- **Image Segmentation** 负责补充可见物体区域、世界空间中的小物体提议、以及更细的前景分割

不要把 Image Segmentation 当成整个 SpatialLM 的替代品。
它更像是：

**Meta 官方工具下，对 Roomify“scene understanding”阶段的可实现近似补强。**

---

## 关于 MQDH 的定位

`MQDH`（Meta Quest Developer Hub）适合放在**真机开发和验证流程**里，不是用来替代 `Unity`、`MRUK`、`MetaXRSimulator` 或 `Unity MCP` 的主工具。

对这个项目，它最有价值的用途是：
- 往 Quest 头显快速安装 APK
- 投屏、截图、录屏，方便看 MR 风格化结果和录 demo
- 看 device logs、metrics、traces，排查性能和运行时问题
- 把真机上导出的截图、录像、采集文件拉回电脑

所以你可以把它理解成：

- `MetaXRSimulator`：当前日常开发主路径
- `MQDH`：真机部署、观察、采集、性能分析的辅助工具

尤其是后面如果你开始做：
- passthrough 真机验证
- `DevicePassthroughCaptureService` 的真实房间采集
- demo 录制
- 性能 profiling

那 `MQDH` 就应该成为固定工作流的一部分。

注意：Quest Link / Editor Play 对当前项目很有价值，因为它能快速验证 MRUK 房间、HUD、best angle、替换落位和生成物 job 状态。但 native passthrough camera capture 依赖支持的头显和 runtime；Quest Pro + Link 不能作为当前 PCA capture 路径的成功/失败标准。

---

## 关于课程方向

虽然你现在在搭一个更完整的技术系统，但你提交 coursework 时，叙事仍然应该写成：

**一个面向图书馆讨论室的风格化 mixed-reality discussion experience**

而不是：

**一个能处理任意房间的通用生成平台**

因为课程最看重的是：
- 和具体 setting 的关系
- 可测试的核心 mixed-reality interaction
- research / prototype / testing / reflection 的平衡

---

## 你接下来该怎么用这套文档

最实用的方法是：

1. 把这些文件复制进 Unity 项目根目录
2. 启动 Unity，确认 Unity Bridge 还在 Running
3. 用 Codex 打开这个项目目录
4. 先看 `docs/08_PROGRESS_STATUS.md` 判断当前最小任务
5. 如果要继续自动生成物链路，看 `docs/09_GENERATIVE_OBJECT_PIPELINE.md` 和 `docs/10_MANUAL_EXTERNAL_WORKER_RUNBOOK.md`
6. 如果要跑 APIMart / upload / Seed3D，先确认 Unity 进程能读到 `APIMART_API_KEY`、`SCENESHIFT_UPLOAD_TOKEN`、`ARK_API_KEY`
7. 如果要录 demo 或确认功能没坏，看 `docs/11_SMOKE_TEST_AND_DEMO_CHECKLIST.md`
8. 如果要上 Quest 真机，看 `docs/12_TRUE_DEVICE_VALIDATION_PLAN.md`

最稳的协作方式仍然是：一次只让 Codex 做一个小任务，做完后同步 `docs/08_PROGRESS_STATUS.md`。
