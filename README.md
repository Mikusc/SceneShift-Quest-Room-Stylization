# SceneShift · MR 房间风格化

**用 AI 辅助开发，把“改变房间风格”的想法做成可交互的 Meta Quest 原型。**

SceneShift 将房间语义、风格意图、可选图像/3D 生成和人工复核串成一条工作流：识别房间与家具，选择风格，在保留空间结构的前提下呈现候选效果，再由用户接受、拒绝或修正。当前围绕 **UNNC IEB 的一个真实办公室**开展原型迭代。

*Scene-aware mixed-reality room stylization for Meta Quest. This repository shares an AI-assisted prototype, its implementation, and its validation boundaries.*

[快速开始](#快速开始) · [AI 如何参与](#ai-如何参与) · [实现与边界](#实现与边界) · [中文上手指南](START_HERE_CN.md) · [工程进度](docs/08_PROGRESS_STATUS.md)

## 项目解决什么问题

生成一张风格图之后，还需要回答：它对应房间里的哪个对象？尺寸与朝向是否合适？换风格后能否重用正确的素材？生成失败或结果不合适时怎样恢复？

SceneShift 将这些问题放进同一个 MR 交互原型中：

| 用户环节 | 已实现的工程路径 | 可检查的结果 |
| --- | --- | --- |
| 读取房间 | MRUK 房间与家具语义；Editor 提供房间 prefab fallback | 房间状态、语义计数和目标对象 |
| 表达风格 | 内置/自定义风格；确定性关键词解析，可选 LLM 解析 | 共享的风格标识、提示词与缓存身份 |
| 呈现效果 | 表面材质覆盖、家具代理、氛围；可选图像与 3D 生成 | 可继续操作的场景及生成任务状态 |
| 放置候选 | 生成请求与房间/对象绑定；运行时 GLB 加载、尺寸适配 | 可追溯到目标对象的候选模型 |
| 人工复核 | 接受、拒绝、重置、旋转/位置修正及复核记录 | 可编辑和恢复的结果；完整头显重启验证仍待补齐 |

```text
房间与家具语义 → 风格意图 → 表面/对象候选 → 按目标放置 → 用户复核与修正
                     ↘ 无生成素材时保留确定性材质与代理 fallback
```

## 实际画面

<img src="docs/images/simulator-clean-view-2026-05-24.png" alt="SceneShift 早期 Unity / Meta XR Simulator 运行画面：办公室房间、控制面板和控制器，2026 年 5 月 24 日" width="420">

这是 **2026-05-24 的 Unity / Meta XR Simulator 运行截图**，展示早期房间与控制面板，原图未改绘。它早于当前 compact / `Details` 面板迭代，不代表最新 UI 的头显验收。

## AI 如何参与

- **辅助原型开发**：将房间风格化需求拆解为场景、数据契约、交互脚本和验证步骤，使用 AI 辅助实现、排查与文档整理。仓库保留了 [Codex 工作流](docs/07_CODEX_WORKFLOW_PROMPTS_CN.md) 和 [工程约束](AGENTS.md)，便于检查实现过程。
- **连接生成能力**：风格意图可经过 LLM 解析；可选的图像、表面纹理和 3D 模型生成由独立适配器或 HTTPS 后端承担。生成结果还要经过目标绑定、尺寸适配和用户复核。
- **组织可恢复的工作流**：任务状态、提示词、缓存身份和复核记录帮助定位失败环节。没有可用生成素材时，保留确定性的场景展示路径。

项目重点是把生成能力接入可检查、可修正的产品流程。公开源码不包含服务密钥、私人技能、设备会话标识或完整生成素材库。

## 实现与边界

截至 **2026-10-20 本次源码整理**：

- 同步本地已实现的 dashboard 更新：主面板突出风格、捕获、生成/加载、进度和复核；诊断与恢复操作集中到 `Details`。
- canonical scene 保存可在 Play 前检查的 UISet 层级；未选中生成候选时禁用相应复核按钮。
- Meta XR 主要包与本地已解析的 **205.0.0** 对齐；保留现有音频包及其他依赖版本。实际版本以 `Packages/manifest.json` 与 lock 文件为准。
- 本次完成了改动 C# 文件的独立编译检查、本地 fixed-url 协议 smoke、脚本语法、场景引用结构和打包输入凭证检查。独立 C# 检查使用已有 Unity/包程序集，不等同于全项目重新导入。

**当前仍是原型。** 历史 Editor/Simulator、Quest Link 和头显支持证据记录在进度文档中；本次没有重新启动 Unity、构建 APK 或进行头显验收。仍需补齐：

- 当前 UI 的官方 ray/poke 操作与头显视觉检查；
- 一次完整记录的独立头显流程：风格输入、捕获、生成、GLB 加载、复核、重启恢复；
- 真实办公室中的表面效果和原生相机捕获可靠性。

任意房间适配、完整视觉分割融合及 NPC 交互均不属于已完成范围。

## 快速开始

1. 用 **Unity `6000.4.3f1`** 打开仓库，等待 Package Manager 按 lock 文件恢复依赖。
2. 打开 `Assets/Scenes/MR_RoomStylization.unity`。先使用 `MetaXRSimulator` / Editor 路径检查房间和面板；场景配置了 MRUK 设备房间优先、`Office00` prefab fallback。
3. 若只检查模型加载与复核流程，在 Play 前选择 `SceneShift → Runtime Backend → Configure LocalTestModelUrl Mode`。这会在本地场景中设置测试模式，使用公开 Box GLB，**不调用生成服务**；下载测试模型仍需网络。
4. 进入 Play，检查房间初始化、选择内置风格、查看目标与进度。通过 `Details` 查看诊断、恢复和测试操作；按 `Y` 可切换纯透视安全视图。
5. 确定性房间效果与测试模型流程可用于了解结构；正式生成需要单独配置自己的后端与服务权限，按 [后端/头显 runbook](docs/14_MQDH_TEST_CHANNEL_RUNBOOK.md) 操作。

仓库中的历史演示后端地址不是公共服务承诺。启用真实生成前，请在自己的配置中替换后端地址并确认费用；密钥仅放在服务端环境或本地安全配置，不能写入 Unity 资源、场景或提交历史。

## 验证

无需启动 Unity 的本地协议与凭证检查：

```bash
# 只检查打包输入，不输出匹配到的凭证值
bash Tools/scan_predevice_secrets.sh --packaged-only

# 本机 fixed-url 协议测试：模拟提交与轮询，不发起付费生成
SCENESHIFT_SMOKE_BACKEND_PORT=18789 bash Tools/run_runtime_backend_protocol_smoke.sh
```

后者验证 HTTP 协议和任务状态，不验证模型生成、Unity 加载或头显交互。构建和设备验证应继续使用 [回归清单](docs/11_SMOKE_TEST_AND_DEMO_CHECKLIST.md)、[真机验证计划](docs/12_TRUE_DEVICE_VALIDATION_PLAN.md) 与 [MQDH runbook](docs/14_MQDH_TEST_CHANNEL_RUNBOOK.md)；这些流程需要相应 Unity、Android 和设备环境。

## 继续了解

| 内容 | 入口 |
| --- | --- |
| 产品范围与成功标准 | [Product scope](docs/01_PRODUCT_SCOPE_AND_SUCCESS.md) |
| 场景结构与模块 | [Architecture](docs/03_ARCHITECTURE_AND_SCENE_LAYOUT.md) |
| 数据契约 | [Data contracts](docs/05_DATA_CONTRACTS.md) |
| 可选生成对象链路 | [Generative pipeline](docs/09_GENERATIVE_OBJECT_PIPELINE.md) |
| 当前进度与历史证据 | [Progress tracker](docs/08_PROGRESS_STATUS.md) |
| 本次更新前的完整 README | [Historical snapshot](docs/README_BEFORE_PUBLIC_SYNC_2026-10-20.md) |

主要技术：Unity / C#、Meta XR、MRUK、OpenXR、Interaction SDK UISet、URP、运行时 GLB 加载、Python 后端协议。第三方 SDK、服务与素材遵循其各自许可和使用条件。
