# SceneShift 中文入口

这份文档是给你和 Codex 协作时看的快速入口。当前项目不是空白 Unity 工程，而是一个已经接近 Phase 1 纵向切片的 **Meta Quest 混合现实办公室风格化原型**。

## 当前定位

项目名称：`SceneShift Office Room`

当前 canonical setting：

- `UNNC IEB` 的一个真实办公室房间
- 不宣称已经 production-ready 支持任意房间
- 但代码结构正在向“同一套 room-aware pipeline 支持多房间/多风格”靠近

当前优先级：

- **Phase 1：房间风格化**
- **Phase 2：NPC 学习伙伴** 仍然暂缓

Phase 1 目标是：

- 读取 MRUK 房间结构
- 识别墙、地、天、门、窗、家具 anchor
- 选择 built-in 或 custom Style
- 生成风格一致的 surface / furniture prompt
- 应用墙地天门窗材质、窗外景观、家具替换
- 在头显内看到状态、隐藏 debug 壳子、切换风格、后续进行 correction

## 当前项目状态

截至 `2026-06-05`，项目已经具备：

- `Assets/Scenes/MR_RoomStylization.unity` canonical scene
- MRUK room semantic bootstrap
- active room refresh，能处理 Quest 中存在多个 room 数据的情况
- Generic room style scaffold
- built-in Style：`Future Research Lab`、`Arcane Knowledge Chamber`
- custom user style intent
- deterministic style keyword fallback
- optional DeepSeek style keyword extraction
- wall / floor / ceiling / door / window / window vista surface pipeline
- `surface_texture_v3_room_scale_openings` prompt version
- 大尺度墙地天贴图重复，避免密集墙纸感
- 墙脚线、墙顶线、墙角 trim，用于遮墙缝和强化边界
- 门从 thin frame 改为 full door / portal panel
- 窗框保持 open center，窗外景观为 16:9 exterior vista
- generated furniture capture / image2 / upload / Seed3D / import / placement pipeline
- 支持的家具类别扩展到 `TABLE`、`STORAGE`、`SCREEN`、`COUCH`、`BED`、`LAMP`、`PLANT`、`OTHER`，其中 `COUCH` 在代码里按 `Seating` 家具类别处理
- request-locked generated prefab placement，避免旧 capture 错套到新物体
- per-object generation world status cards
- runtime main control panel
- clean view / object status / reapply / capture / auto target / rotate 90 / Submit+Load / Load Test GLB / accept / reject / reset 等运行时控制
- 左手 `Y` / keyboard `Y` 的纯透视安全视图，用于临时隐藏所有虚拟内容后再恢复
- standalone Quest 方向的 runtime GLB 加载雏形：固定测试 GLB URL -> `Application.persistentDataPath` -> glTFast runtime load -> fitting 到 `TABLE` anchor -> accept/reject/reset/correction review restore
- `PreDeviceRuntimeLoopValidator`、`PreDeviceSmokeReportRunner` 和 `PreDeviceBuildReadinessReportRunner` 已经提供 Mac 真机前验证入口
- `PreDeviceBuildReadinessReportRunner` 会检查 preflight/build 工具是否存在，并确认 Android Support recovery、terminal pre-package suite、Unity package build runner、local gate、APK/AAB 包校验、gate 自检、handoff bundle、headset evidence 采集/校验脚本具备预期能力标记
- MQDH/test-channel 交接工具已经补齐：MQDH evidence template、MQDH handoff preflight、终端 Android Support 检查、terminal pre-package suite、Unity MQDH package build runner、最终 APK/AAB gate 命令检查、终端 handoff 状态摘要、ADB/logcat/screenshot/persistent-file 证据采集脚本
- Unity `6000.4.3f1` 的 Android Build Support / SDK / NDK / OpenJDK 已在当前 Mac 上安装并通过文件系统检查
- 最新已验证 APK：`Builds/MQDH/SceneShiftQuest_20260526_154220.apk`
- 最新 package build report：`Library/MQDHPackageBuildReports/mqdh_package_build_20260526_154220.md`，状态为 `BuiltAndVerified`
- 最新 final local gate：`Library/MQDHHeadsetEvidence/predevice_local_gate_20260526_154335.md`，状态为 `Pass`
- 最新 ADB/headset evidence：`Library/MQDHHeadsetEvidence/adb_20260526_211143`，`Tools/verify_mqdh_headset_evidence.sh` 校验为 `Pass`
- 最新 Azure runtime backend smoke：`Library/RuntimeBackendSmokeReports/runtime_backend_azure_smoke_20260527_144851.md`，状态为 `Pass`，但它是 no-image smoke，不会触发付费 3D 任务

还没完成或还需要验证：

- 当前本机的 Android Support 阻塞已解决；只有在未来 `android_build_support_installed` 或 `Tools/check_android_support_recovery.sh` 失败时才需要重新安装 Unity Android 模块
- 真机 PCA capture 需要支持的 Quest runtime 继续验证
- surface v3 在真实办公室中的美观性还需要 Game View/MetaXRSimulator/头显多视角检查
- headset-side ADB evidence 已能看到 PCA capture、runtime backend job、`mesh_textured_pbr.glb`、runtime model local path 和 review records；但还需要一次按 runbook 记录的完整真机闭环：用户输入 Style -> capture -> backend full-chain -> 非 Box GLB 下载/加载 -> accept/reject/reset/correction -> app restart restore
- generated furniture 的 accept / reject / reset / correction 已有 Editor Play 和部分头显持久化证据，但还没整理成完整 MQDH/test-channel standalone 重启恢复通过结论
- UI 仍以稳定可用为主，官方 UISet 视觉完全迁移还没完成

## 关键文档怎么读

优先读这些：

- `AGENTS.md`
  Codex 项目规则、优先级、禁止事项、工作方式。
- `README.md`
  当前项目总览。
- `docs/08_PROGRESS_STATUS.md`
  当前最准确的滚动状态。
- `docs/11_SMOKE_TEST_AND_DEMO_CHECKLIST.md`
  Play 验证和 demo 前检查。

如果你要改对应模块，再读：

- `docs/01_PRODUCT_SCOPE_AND_SUCCESS.md`
  项目范围和成功标准。
- `docs/02_ROOMIFY_TO_META_MAPPING.md`
  Roomify 到 Meta-first pipeline 的映射。
- `docs/05_DATA_CONTRACTS.md`
  Theme、Style、Surface job、Generated object job 的数据契约。
- `docs/09_GENERATIVE_OBJECT_PIPELINE.md`
  家具 capture -> image2 -> Seed3D -> Unity placement 的链路。
- `docs/10_MANUAL_EXTERNAL_WORKER_RUNBOOK.md`
  外部 worker / API / 手工 fallback 流程。
- `docs/12_TRUE_DEVICE_VALIDATION_PLAN.md`
  Quest Link / 真机验证计划。
- `docs/14_MQDH_TEST_CHANNEL_RUNBOOK.md`
  从 Mac 真机前验证到 MQDH/test-channel 测试包和头显证据收集的操作清单。

## 当前最常用运行方式

1. 打开 Unity `6000.4.3f1`。
2. 打开 `Assets/Scenes/MR_RoomStylization.unity`。
3. 确认 Console 没有新的红色错误。
4. Play。
5. 等 MRUK room ready。
6. 在头显/Unity Game View 中看 `SceneShift Control` 面板。
7. 选择 Style，例如 `Future Research Lab`、`Arcane Knowledge Chamber`，或通过 runtime style intent 测 custom 风格。
8. 等 surface / furniture queue 状态变化。
9. 使用 `Clean View` 隐藏 MRUK 壳子和 object status cards，检查纯风格化效果。
10. 如果要 capture 家具，看向目标物体，等 HUD 显示有效 target、id 和 score 后触发 capture。
11. 如果生成家具方向不对，先选中/看向该生成家具，再用 `Rotate 90` 做当前 Play 会话内的旋转修正。
12. 如果要检查纯真实透视画面，用左手 `Y` 或 keyboard `Y` 临时隐藏所有虚拟内容，再按一次恢复。
13. 如果只是复用当前已验证 APK，使用 `Builds/MQDH/SceneShiftQuest_20260526_154220.apk`，对应 package report 是 `Library/MQDHPackageBuildReports/mqdh_package_build_20260526_154220.md`。
14. 上传或安装前运行 `bash Tools/verify_mqdh_package_build_report.sh` 和 `bash Tools/verify_predevice_local_gate.sh --require-package-artifact`，确认 latest package report / final local gate 仍为 `Pass`。
15. 如果代码、Player Settings、build target、backend 配置或证据文件有变化，重新运行 Unity 菜单 `SceneShift/Validation/Run MQDH Pre-Package Evidence Suite`，再运行 `bash Tools/run_mqdh_terminal_prepackage_suite.sh`。
16. 如需重新打包，用 Unity 菜单 `SceneShift/Validation/Build MQDH Test Package` 生成 `Builds/MQDH/` 下的 APK/AAB、写入 `Library/MQDHPackageBuildReports/mqdh_package_build_*.md`，并自动跑最终包校验 gate。
17. 如果报告显示 `android_build_support_installed` 失败，才通过 Unity Hub 或 `bash Tools/install_unity_android_support.sh --run --wait-for-close` 重新安装 Android Build Support、Android SDK & NDK Tools、OpenJDK。
18. 手动打出 APK/AAB 时，上传 MQDH/test-channel 前重跑 `bash Tools/run_predevice_local_gate.sh --package-artifact <apk-or-aab-path>`；它会把 `bash Tools/verify_mqdh_package_artifact.sh <apk-or-aab-path>` 的包校验纳入同一份 gate 报告。
19. 头显装包并打开 app 后，如果 ADB 可用，运行 `bash Tools/collect_mqdh_headset_evidence.sh --package com.mikusc.sceneshiftroom.comp4145 --template <latest Library/MQDHHeadsetEvidence/*.md>` 采集日志、截图和 persistent files，然后运行 `bash Tools/verify_mqdh_headset_evidence.sh` 校验采集目录。

## API 环境变量

如果要跑自动生成链路，Unity 进程需要能读到：

- `DEEPSEEK_API_KEY`
  用于可选的 style keyword extraction。
- `APIMART_API_KEY`
  用于 APIMart `gpt-image-2`，包括家具图像生成和 surface texture 生成。
- `SCENESHIFT_UPLOAD_TOKEN`
  用于上传 PNG 到 `https://www.mikusc.top/api/scene-shift/upload`。
- `ARK_API_KEY`
  用于 Ark Seed3D 2.0。

不要把这些 key 写进 Git。

## 当前 pipeline 简图

Surface pipeline：

```text
MRUK wall/floor/ceiling/door/window anchors
-> SurfaceTexturePromptBuilder
-> APIMart gpt-image-2 surface jobs
-> SurfaceOverrideApplier
-> room-scale materials + trims + full door + window vista
```

Furniture pipeline：

```text
MRUK furniture anchor
-> best-view target scoring
-> capture/request JSON
-> APIMart gpt-image-2 stylized object image
-> hosted upload
-> Ark Seed3D
-> GeneratedObjectModelImporter
-> request-locked runtime placement
```

Style pipeline：

```text
built-in/custom user style
-> deterministic keywords or DeepSeek
-> style-aware prompt/cache identity
-> surfaces and furniture share the same visual intent
```

## 和 Codex 协作时的建议

不要让 Codex 一次“把所有东西做完”。更稳的方式是一次只做一个小目标：

- 修一个 Console error
- 改一个 UI 交互
- 验证一次 capture
- 改一个 surface 美术问题
- 同步一份文档
- 做一次 git commit/push

每次结束让 Codex 汇报：

- 改了哪些文件
- 是否编译通过
- Unity Console 是否有新错误
- 你在头显/Unity 里应该怎么验证
- 下一个最小任务是什么

## 当前最小后续任务

如果目标是更好看的房间效果：

- Play 后检查 wall/floor/ceiling 的 v3 room-scale 材质是否不再密集重复。
- 检查墙缝、墙地边界、墙天边界是否被 trim 缓解。
- 检查 door 是否像完整门/portal，而不是只有框。
- 检查 window frame 是否保留开口，window vista 是否只在窗外区域可见。

如果目标是 demo-ready：

- 使用最新已验证 APK：`Builds/MQDH/SceneShiftQuest_20260526_154220.apk`，或在有代码/配置变化后重新跑 `SceneShift/Validation/Build MQDH Test Package`。
- 上传或安装前确认 `bash Tools/verify_mqdh_package_build_report.sh`、`bash Tools/verify_predevice_local_gate.sh --require-package-artifact`、`bash Tools/verify_mqdh_headset_evidence.sh` 的当前输出。
- 按 `docs/14_MQDH_TEST_CHANNEL_RUNBOOK.md` 在头显里验证 runtime GLB / accept / reject / reset / correction / restart restore。
- 如果要验证真实后端链路，先运行 `bash Tools/check_runtime_backend_azure_smoke.sh`；它只证明部署端点和 provider boundary 可达，不会创建付费 3D 任务。
- 真正的 demo closure 需要记录一次完整头显操作：Style 输入/选择、`TABLE` capture、backend polling、非 Box `mesh_textured_pbr.glb` 下载/加载、review controls、重启后 review state restore。
- 如果之后修改 local gate 或 package verifier，运行 `bash Tools/test_predevice_gate_scripts.sh` 做一次 shell 自检。
- 头显安装并打开 app 后，用 `bash Tools/collect_mqdh_headset_evidence.sh ...` 采集 ADB evidence，再用 `bash Tools/verify_mqdh_headset_evidence.sh` 校验采集目录是否够用。
