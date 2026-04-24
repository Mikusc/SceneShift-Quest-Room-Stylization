# 07 Codex 工作流提示词（中文可直接复制）

## 使用原则
这些 prompt 不要一次性全发。

最稳的方式是：
- 一次只做一个小任务
- 每次都要求 Codex 先读 `AGENTS.md`
- 每次都要求它先检查当前状态，再改代码
- 每次结束都给出修改文件列表、验证步骤和下一步建议

---

## Prompt 1：项目盘点（第一条就用这个）

```text
Read AGENTS.md and docs/01, docs/02, docs/04 first.
Then inspect the Unity project state, current package setup, existing scenes, and Unity console.
Do not change anything yet.
Summarize:
1) what already exists,
2) what is missing for Phase 1 room stylization,
3) the biggest technical risks,
4) the smallest next task.
Reply in Chinese.
```

---

## Prompt 2：建立项目基础结构

```text
Read AGENTS.md first.
Create the minimum folder structure and canonical scene required by docs/03 and docs/04.
Do not add advanced features.
Only do:
- create missing folders,
- create Assets/Scenes/MR_RoomStylization.unity if missing,
- create root GameObjects according to docs/03.
After changes, inspect Unity console and fix any errors introduced by your changes.
Then summarize modified files, scene objects created, and manual verification steps in Chinese.
```

---

## Prompt 3：先只做 MRUK 房间语义调试层

```text
Read AGENTS.md and docs/03, docs/04 first.
Implement only Milestone 1.
Focus on:
- MRUK room bootstrap,
- RoomSemanticBootstrap,
- semantic debug overlay,
- a simple debug panel listing room semantic counts.
Do not implement stylization yet.
After coding, inspect Unity console and resolve introduced errors.
Then explain:
- what scripts were added,
- what scene wiring is needed,
- how I verify on device or in simulation,
- what the next smallest task should be.
Reply in Chinese.
```

---

## Prompt 4：接入 Image Segmentation / perception 层

```text
Read AGENTS.md and docs/02, docs/03, docs/04 first.
Implement only the smallest useful part of Milestone 2.
Use Meta official tools first.
Goal:
- verify whether Image Segmentation is available and practical in this project,
- if yes, create ObservedObjectCollector for it,
- if not, fall back to Object Detection and clearly document the fallback.
Do not yet implement full stylization.
After changes, inspect Unity console and fix introduced errors.
Summarize modified files, scene wiring, fallback decisions, and manual verification steps in Chinese.
```

---

## Prompt 5：实现 ThemeProfile 和 StylizationPlan 数据层

```text
Read AGENTS.md and docs/05 first.
Implement only the data contracts and minimal planner scaffolding needed for Milestone 3.
Create:
- ThemeProfile data model,
- RoomObjectRecord if missing,
- RoomSemanticSnapshot if missing,
- StylizationPlan and StylizationPlanEntry,
- two starter theme assets: FutureResearchLab and ArcaneKnowledgeChamber.
Do not yet apply scene changes.
After coding, inspect Unity console and fix introduced errors.
Then explain the created data assets and how they should be used.
Reply in Chinese.
```

---

## Prompt 6：只做规则式 stylization planner

```text
Read AGENTS.md and docs/02, docs/05 first.
Implement a deterministic StylizationPlanner for the current project.
The planner should map at least these semantics:
- wall
- floor
- table
- screen
- storage
- seating
Use rule-based logic, not cloud generation.
Do not apply the plan to the scene yet.
Also create a debug UI or log output that lists generated plan entries and warnings.
After changes, inspect Unity console and fix introduced errors.
Reply in Chinese with modified files, planner behavior, and manual verification steps.
```

---

## Prompt 7：只做主题应用，不做修正模式

```text
Read AGENTS.md and docs/03, docs/04, docs/05 first.
Implement only the smallest useful part of Milestone 4.
Create an AnchorThemeApplier that can:
- apply wall/floor material changes,
- apply a screen treatment,
- fit at least one table proxy,
- keep collision-sensitive objects footprint-aware.
Do not implement correction mode yet.
After coding, inspect Unity console and fix introduced errors.
Then describe exactly how I trigger stylization in the scene and how I verify that it worked.
Reply in Chinese.
```

---

## Prompt 8：加入 correction mode

```text
Read AGENTS.md and docs/03, docs/04 first.
Implement only the smallest correction workflow for Milestone 5.
I need:
- select one applied stylized object,
- inspect its original semantic and replacement info,
- nudge position,
- yaw rotate,
- reset.
Keep it minimal and inspector-friendly.
After changes, inspect Unity console and fix introduced errors.
Reply in Chinese with modified files, scene setup, and manual verification steps.
```

---

## Prompt 9：为项目添加自定义 Unity MCP tools

```text
Read AGENTS.md and docs/06 first.
Implement only the first two custom project MCP tools:
- edr_validate_setup
- edr_export_room_semantics
Use typed parameter classes and keep the implementation safe and minimal.
Do not add more tools yet.
After coding, inspect Unity console and fix introduced errors.
Then explain how I can ask Codex to call these tools in future sessions.
Reply in Chinese.
```

---

## Prompt 10：做一个 demo-ready 的最小 UI

```text
Read AGENTS.md and docs/04 first.
Implement only the minimum demo UI for Milestone 6.
I need buttons or a simple panel for:
- room ready status,
- theme selection,
- stylize,
- reset,
- correction mode toggle,
- debug overlay toggle.
Keep the UI simple and recording-friendly.
After coding, inspect Unity console and fix introduced errors.
Reply in Chinese with modified files and how I should test the full demo flow.
```

---

## Prompt 11：让 Codex 先提方案，不直接改代码

```text
Read AGENTS.md and docs/01 to docs/05 first.
Do not change code yet.
I want you to propose the smallest safe implementation plan for the next milestone.
Your answer must include:
1) task breakdown,
2) files likely to be created or modified,
3) biggest risks,
4) manual verification steps,
5) rollback strategy.
Reply in Chinese.
```

---

## Prompt 12：准备 NPC 阶段，但先不做 NPC

```text
Read AGENTS.md and docs/03 first.
Do not implement the NPC yet.
Only prepare extension points so the future themed NPC can use:
- current theme context,
- key mapped room objects,
- a designated screen/whiteboard target,
- room mood state.
Keep the work minimal and Phase-1-safe.
After changes, inspect Unity console and fix introduced errors.
Reply in Chinese.
```

---

## Prompt 13：验证手工 ExternalFileProtocol 生成链路

```text
Read AGENTS.md and docs/09, docs/10 first.
Do not change code unless a blocking bug is found.
Verify the current generated-object file protocol:
- confirm LocalGeneratedObjectBackendAdapter is in ExternalFileProtocol mode,
- enter Play only if needed,
- press C once or ask me to press C,
- inspect Library/GeneratedObjectJobs and Library/GeneratedObjectBackendInbox,
- explain which .submission.json fields I should use for the manual image worker,
- if an external screenshot is needed, ask the operator to take it from the same Play-session view and set BestViewCaptureService.externalScreenshotPath; Codex should only read Console/files unless explicitly asked to operate the UI.
Reply in Chinese with the exact paths I should open and the expected next state.
```

---

## Prompt 14：跑 demo / smoke test 检查

```text
Read AGENTS.md and docs/08, docs/11 first.
Do not add features.
Run the smallest safe smoke-test inspection for the canonical scene:
- check current git/workspace state,
- check relevant scene objects and component wiring,
- read Unity Console without taking screenshots after Play,
- verify the expected demo path and generated-object artifacts if present.
Summarize pass/fail items, blocking issues, accepted simulator warnings, and the next smallest fix in Chinese.
```

---

## Prompt 15：准备 Quest 真机验证

```text
Read AGENTS.md and docs/12 first.
Do not implement new features yet.
Inspect current XR/Meta project setup and explain what is ready or missing for Quest true-device validation.
Focus on:
- deterministic room stylization,
- MRUK room semantics,
- file artifacts/log collection,
- future passthrough/camera capture constraints.
Do not assume simulator behavior proves true-device camera access.
Reply in Chinese with a staged validation plan and the smallest next device test.
```

---

## 最稳的推荐顺序
如果是从零开始，建议你按这个顺序用：

1. Prompt 1
2. Prompt 3
3. Prompt 4
4. Prompt 5
5. Prompt 6
6. Prompt 7
7. Prompt 8
8. Prompt 9
9. Prompt 10

这样最不容易失控。

如果是在当前项目进度继续开发，先读：
- `docs/08_PROGRESS_STATUS.md`
- `docs/09_GENERATIVE_OBJECT_PIPELINE.md`
- `docs/10_MANUAL_EXTERNAL_WORKER_RUNBOOK.md`
- `docs/11_SMOKE_TEST_AND_DEMO_CHECKLIST.md`

然后优先使用：
- Prompt 13 验证手工生成链路
- Prompt 14 做 demo 前检查
- Prompt 15 做真机验证准备
