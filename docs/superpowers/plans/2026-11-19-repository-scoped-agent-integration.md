# FDG 仓库级 Agent 接入实施计划

2026-11-19 起草，2026-11-20 根据选定方案修订。基线为 FDG 1.0.0 源码 `9e0ba99`。本文记录仓库级接入的设计与验收。

## 决策与边界

FDG 通过独立的 .NET CLI 向消费仓库登记 Claude Code 或 Codex 的项目技能和 hooks。**唯一的 Agent 接入范围是当前 Git 仓库**。同一用户打开另一个未接入仓库，即使里面有 SDK-style C# 项目，FDG 技能和 hooks 都应为零。仓库内的宿主安装记录固定工具发行版本，已准备的同版本可执行文件可在本机跨仓库复用。

沿用 Analyzer 和 `dotnet-fdg` 两个 NuGet 包。IDE 和普通构建通过 Analyzer 工作。Agent 集成使用各宿主的项目文件，发布流程不再创建 Marketplace 清单、插件安装命令或按版本命名的 marketplace。实施面向新接入，不迁移旧用户配置。

团队确认接入时应看清 Stop 的写入范围。它对本轮改动、属于已接入 SDK-style C# 项目且位于当前工作树内的**整个文件**做格式化和非破坏性修复。规则诊断按本轮改动行筛选，改动文件的编译错误按现有例外处理，剩余问题最多打回两次。沿用已有公开 API 的保护。 [原设计中的 Stop 决策](../specs/2026-10-21-framework-design-guidelines-design.md#54-hook-行为)

## 给使用者的流程

1. **首次接入。** 在目标仓库运行 `dotnet tool exec dotnet-fdg@<版本> -- init --host codex`，也可选 `claude` 或 `both`。支持 `--dry-run` 预览工作树 diff。交互模式先列出宿主、文件、工具版本、还原来源和自动写回行为，再确认应用。非交互模式显式传 `--yes`。`--host` 未指定时，交互模式让用户选择，非交互模式提示可选值并退出。
2. **配置 C# 项目。** 应用 `init` 时一并准备本机工具。重新打开目标仓库，在宿主中完成信任与必要的 hook 审核，再显式运行 `$fdg-setup`（Codex）或 `/fdg-setup`（Claude Code）。沿用现有的选项目、定 profile、处理中央包管理、预览 MSBuild 修改和构建验证流程。团队审阅并提交接入文件。其他成员 clone 后按宿主要求审核。指定版本尚未缓存时，执行一次项目启动脚本的 `prepare`。例如 Codex 在 macOS/Linux 上用 `bash .codex/hooks/fdg/fdg.sh prepare`，在 Windows 上用 `pwsh -File .codex/hooks/fdg/fdg.ps1 prepare`。脚本读取仓库指定的版本。
3. **升级或加宿主。** 用目标版本重复执行 `init`。同版本添加宿主只补齐对应入口。升级时，共享资料和已接入的全部宿主一起升到目标工具版本。团队在同一个 PR 中审阅版本及文件差异，再次执行相同操作得到空 diff。
4. **移除宿主。** 从已安装的项目脚本调用 `uninstall --host codex|claude|both`，预览和确认后移除该宿主管理的技能、hook 和专属资源。最后一个宿主移除时清理该仓库的共享 Agent 资源。IDE 和构建继续按项目内的 Analyzer 配置工作。卸载保留其他仓库可能使用的本机工具缓存。需要独立手动运行 CLI 时，可显式调用 `dotnet tool exec dotnet-fdg@<版本> -- fix`。

## 消费仓库的文件布局

| 内容 | Codex | Claude Code |
|---|---|---|
| 四个技能入口 | `.agents/skills/fdg-{write,setup,fix,review}/SKILL.md` | `.claude/skills/fdg-{write,setup,fix,review}/SKILL.md` |
| 三个生命周期事件 | 合并进 `.codex/hooks.json` | 合并进 `.claude/settings.json` 的 `hooks` |
| 技能调用与评审 | `fdg-write` 可隐式触发。三个手动技能各带 `agents/openai.yaml`，设 `policy.allow_implicit_invocation: false`。评审在当前会话完成 | 保留手动技能的 `disable-model-invocation`。评审调用项目内的 `.claude/agents/fdg-reviewer.md` |
| 启动脚本、安装记录 | `.codex/hooks/` 下的 FDG 脚本和 `fdg-install.json` | `.claude/hooks/` 下的 FDG 脚本和 `fdg-install.json` |
| 工具、参考资料 | 安装记录内的 `toolVersion` 固定工具版本，参考资料共用一份 `fdg-write/references/` | 与 Codex 相同，位置规则见下 |

**工具版本。** 各宿主的安装记录 `fdg-install.json` 同时保存精确的 NuGet 发行版本 `toolVersion`。单宿主读取自己的记录。双宿主由同一次安装或升级写入相同的值。Agent 接入无需创建根目录文件或 `.config/`，也不修改团队已有的 .NET 工具清单。

**参考资料。** 将 56 份 Markdown（28 篇章节参考、28 篇示例）随 `fdg-write` 放在已选宿主的技能目录。只选 Claude 时放 `.claude/skills/fdg-write/references/`，只选 Codex 时放 `.agents/skills/fdg-write/references/`。首次同时选两端时放后者。之后增加宿主沿用已有资料位置，生成相对于各技能入口的链接。移除承载资料的一端时，将资料移入保留端的 `fdg-write/references/`，在同一份变更预览中列出迁移及链接调整。仓库始终只有一份可直接阅读的资料。用普通文件保留章节索引、`examples/` 和锚点，链接随 clone/worktree 可用。

## 版本与 Git

- **工具与 Analyzer。** FDG CLI 和宿主资产配套发布。项目脚本按安装记录的 `toolVersion` 选择 CLI。Analyzer 的版本按项目现有 NuGet 做法保存在 `.csproj` 或中央 `Directory.Packages.props`，用 `dotnet restore` 还原。首次 setup 推荐与工具同一发行版。调整已有 Analyzer 版本时单列预览，继续校验现有协议兼容性。
- **随仓库提交。** Analyzer 接入改动、宿主技能与 hook 声明、脚本、Claude reviewer（如启用）、一份参考资料和各宿主的安装记录。记录包括精确版本、受管路径、内容哈希和共享资料位置。使用仓库相对路径、稳定顺序与规范化文本内容哈希，避免机器路径、时间戳或 Git 自动换行造成无效 diff。
- **本机状态。** 启动脚本从 Git 工作树读取指定版本，按本机平台和架构在用户缓存中定位该版本的可执行文件。仅显式执行 `init` 或 `prepare` 时可从包源安装，缓存目录不加入 PATH，也不登记用户级插件或工具。hook 审批由宿主管理。快照、SARIF、工作树安装锁及中断恢复记录继续使用工作树专属的 Git 元数据位置。
- **回退与本地编辑。** 回退升级提交会同时还原指定版本、技能、参考资料、安装记录和事件声明。本机缓存了旧版本即可直接运行，缓存缺失时执行项目脚本的 `prepare`。同次变更若包含 Analyzer，再还原 NuGet 依赖。更新前对照上次安装的哈希，发现手改则先报告具体冲突。

## 实施顺序

### 1. 把宿主资产纳入版本化 NuGet 交付

- 把四个技能、Claude reviewer、启动脚本、模板和生成的参考资料随 `dotnet-fdg` 工具包分发。复用当前文本和生成器，给 CLI 一份可枚举的资产清单，版本来自工具的实际发行版。安装时直接读取随包资产，不从远端分支拼装内容。
- 调整生成器输出目录、技能 lint 和发行校验。核对现有 56 个参考文件的章节、示例链接与锚点，覆盖单宿主和双宿主输出。更新 `fdg-review` 和 reviewer 的规则寻址，移除全局插件目录搜索和插件名前缀。评审读取当前仓库固定版本的资料。
- 清理发布用的插件清单和 CI 的 marketplace 校验，保留历史发布记录。替换成从实际打出的两个 `.nupkg` 在全新消费仓库安装、还原和执行的测试。验证当前承诺的最低消费 SDK 及源码构建 SDK。

### 2. 实现接入、升级和卸载的同一套变更计划

- 从调用目录定位当前 Git worktree 根。接入前用现有 SDK-style 判定列出候选项目，找不到合适项目时说明原因且工作树零变更。卸载只依赖已安装记录，即使最后一个 C# 项目已删除也能清理。
- 从各宿主的安装记录读取当前版本、FDG 拥有的相对文件、哈希和共享配置中的事件 handler，承载资料的一端同时登记参考文件。两端都启用时统一预检。两份记录的版本不一致时指出具体记录与版本，显式升级到选定版本时在同一预览中列出两端修复及手改冲突。
- 在写入前完成整批预检。用户文件占用同名路径、上次生成的文件被手改、配置损坏、同一 FDG 事件已在 Codex TOML 和 JSON 中重复登记、路径经符号链接逃逸工作树时，列出冲突和具体解决位置。其他宿主设置及现有 handler 的内容和顺序应保留。不能可靠合并的配置停在预览阶段。
- 预览、执行和卸载复用同一份计划。用户确认应用后，先准备并校验本机工具，再逐文件原子替换，应用前复核旧内容，对安装状态变更串行化。用可恢复的事务记录或等效机制覆盖中断和重试，成功后才登记新状态。工具准备失败时输出可重试的命令。准备失败、预览、拒绝和冲突均不得改工作树。
- 卸载只移除 FDG 管理的条目。对用户修改过的生成文件给出明细，保留用户内容。只卸载一个宿主时，另一宿主的技能、hooks、reviewer（若适用）和共享资料仍可用。

### 3. 让 hooks 与技能只运行当前仓库的工具

- 显式准备时，使用 `dotnet tool install dotnet-fdg --version <精确版本> --tool-path <本机版本目录>`。CLI 和项目脚本共用缓存位置与版本的解析约定。解析到合法、精确的 NuGet 版本后，按平台、架构和版本选目录。同一版本跨仓库并发准备时只安装一次，成功校验版本后才允许执行。安装中断可重试。项目脚本的 `prepare` 从当前仓库取版本，读取该仓库的包源设置，重复准备同一版本直接命中缓存。
- 生成 `SessionStart`、`UserPromptSubmit`、`Stop` 三个事件。由宿主适配器显式提供宿主身份和事件，启动脚本从当前 hook 工作目录解析 Git worktree 根，校验该宿主的 FDG 安装记录，按绝对路径调用已准备的 `dotnet-fdg hook …`。保留当前工作树、原始 stdin、CLI 输出、退出码和超时。手动技能经同一入口执行 CLI。验证 Windows、Unix、Git Bash 和 PowerShell 上包含空格的路径。
- 已接入仓库缺指定工具或兼容运行时时，会话开始给出一次适用的准备命令或运行时诊断。常规 hooks 不联网安装、不使用 PATH 上的同名全局工具。缺失项目接入记录时立即退出。同一仓库内，继续按 Analyzer 引用、有效 profile 和文件归属决定实际工作。仅改前端时，不启动格式化和构建。
- 给工具调用和配置写入都做物理路径边界检查。外部链接不能导致改写仓库外文件。项目内 MSBuild 链接的共享源码若无法可靠归属，应报告未检查。setup、fix 和 review 按 MSBuild 的实际中间输出路径定位 marker，覆盖 `UseArtifactsOutput`。
- Codex 0.169.0 的 linked worktree 有实测限制：只在 linked worktree 写 `.codex/hooks.json` 时，该树未发现 hook。两棵树均有 `.codex/` 项目层时，都会发现主 checkout 的声明。当前工作树缺少 `.codex/` 时，Codex 不会创建该项目层。因此 `init` 只改调用者的工作树，在这种布局提示需要把声明合入主 checkout 并重新打开会话。两个版本的启动声明应尽量稳定，运行时从当前工作树找脚本和工具。即使宿主读到另一棵树的声明，缺本地安装记录时也应静默退出。 [Codex 对 linked worktree 的处理](https://github.com/openai/codex/blob/rust-v0.169.0/codex-rs/config/src/loader/mod.rs#L1166-L1175)

### 4. 做真实宿主验收并替换快速开始

| 场景 | 发布门槛 |
|---|---|
| 同一用户、两个独立可信 Git 仓库 | A 接入后，宿主列出 4 个技能、3 个项目来源的 hooks。无关 Python 仓库和未接入 C# 仓库均为 0 / 0，提交提示不会启动 FDG。用户配置无 FDG 登记 |
| 新成员 clone | 冷缓存时会话给出可复制的准备命令，执行后从真实 NuGet 包准备指定版本。热缓存时直接启动该版本。区分技能发现、仓库信任和 hook 审批。审批后三个事件各执行一次，验证 Stop 实际格式化、诊断与有限次返工。Claude reviewer 能从项目内读取指定规则及示例 |
| 混合仓库与 worktree | 从根和子目录启动，覆盖其他工具清单或 PATH 上存在不同 FDG 版本、路径中有空格、源码目录链接、外部共享文件和仅修改前端。两个仓库交替运行不同版本时，各自使用固定工具和参考资料 |
| 版本控制与仓库布局 | 在最低支持 SDK 10.0.100 和构建 SDK 上验证无工具清单的完整接入。已有工具清单应逐字节相同，全新仓库不生成 `dotnet-tools.json` 或 `.config/`。回退升级提交后使用旧版工具和配套规则。不同操作系统的 Git checkout 换行差异不应改变安装记录中的哈希 |
| 本机工具缓存 | 两个仓库并发准备同一版本，覆盖中断、失败重试和并排安装不同版本。移走包源后，热缓存的 hook 仍可直接执行。清空缓存后 hook 给出提示，不从网络恢复也不调用全局版本。卸载任一仓库不影响另一仓库 |
| 生命周期与失败恢复 | 安装一端、追加第二端、同版重复、跨版升级、单端和最后一端卸载，逐次检查 diff。移除参考资料所在宿主后，保留端的技能链接和 reviewer 仍可用。覆盖用户已有 hooks、手改技能、并发/中断、下载失败重试及已删除 C# 项目的清理 |
| 生成资产与发布链 | Windows、Linux、macOS 校验参考链接、包内容和目标工作树。分别记录 Codex CLI、Codex 桌面端与 Claude Code 的实际版本、发现结果和运行结果，据此更新最低版本说明 |

README 按“新团队接入”“成员 clone 后恢复”“升级及移除”三条用户路径编写，附安装前后的文件清单。明确只在已接入项目执行、格式化作用于整文件，以及 linked worktree 的宿主限制。

## 已有验证和依据

- 2026-11-20，在 Codex CLI 0.169.0 的隔离用户配置下创建两个受信任的 C# Git 仓库。仅给 A 放入 FDG 的四个项目技能和项目 hooks。真实 app-server 返回 A **4 / 3**、B **0 / 0**，三个 hooks 均为 `source=project`、无插件 ID、等待审批，用户配置逐字节相同。这个实验验证发现边界。另用 SDK 10.0.402 和本地 NuGet 源验证了 FDG 1.0.0 的工具还原、版本查询和 `dotnet tool run dotnet-fdg -- hook turn-start` 的参数转发。这些实验在新安装器实现前完成。
- 在无工具清单的 Git 仓库，用 SDK 10.0.402 的 `dotnet tool install dotnet-fdg --version 1.0.0 --tool-path <仓库外版本目录>` 安装真实 FDG 包。移走本地包源和 NuGet 包缓存后，从仓库的 `src/` 按绝对路径运行仍返回 `1.0.0`，`hook turn-start` 可以读取 stdin。`git status` 干净，隔离用户的全局工具列表为空。另测 `dotnet tool exec dotnet-fdg@1.0.0`：同一缓存、同一配置下移走源中的包后，再次执行失败。因此日常 hooks 直接执行已准备好的工具。 [指定安装目录](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install#options) · [tool exec 的版本解析](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-exec#description)
- 采用标准清单也能固定版本。CSharpier 的文档使用 `.config/dotnet-tools.json`，其 CI 运行 `dotnet tool restore`。FDG 亦用 SDK 10.0.402 验证过根 `dotnet-tools.json` 的清单发现与还原。希望自行管理手动 CLI 的团队可沿用这种 .NET 工作流。 [CSharpier 本地安装](https://github.com/belav/csharpier/blob/ff692a2f88e83baab616f3888b3d8f26f5ba451a/docs/Installation.md#L5-L21) · [CI 还原](https://github.com/belav/csharpier/blob/ff692a2f88e83baab616f3888b3d8f26f5ba451a/.github/workflows/ValidatePullRequest.yml#L32-L38)
- GitHub Spec Kit 将受管文件路径和 SHA-256 记入项目清单，升级时检查本地编辑。OpenSpec 的项目模式把宿主入口和技能提交进 Git，按 CLI 版本选择技能来源，并关闭额外的技能锁文件。Vercel Skills 管理来自不同 Git 来源的技能，会在根 `skills-lock.json` 分别记录来源提交与内容哈希。FDG 的技能随同一 NuGet 包发布，由宿主安装记录同时保存发行版本和受管文件信息。 [Spec Kit 清单](https://github.com/github/spec-kit/blob/980b3d027f4bd7958c35f97f618d854b41320e3f/src/specify_cli/integrations/manifest.py#L147-L182) · [Spec Kit 升级](https://github.com/github/spec-kit/blob/980b3d027f4bd7958c35f97f618d854b41320e3f/docs/upgrade.md#L102-L119) · [OpenSpec 项目文件](https://github.com/Fission-AI/OpenSpec/blob/8bd4b3557b5bf1d231cd2f165b28f24e2d90a2a0/docs-lab/start/setup.md#L67-L81) · [OpenSpec 安装实现](https://github.com/Fission-AI/OpenSpec/blob/8bd4b3557b5bf1d231cd2f165b28f24e2d90a2a0/src/core/skill-installer.ts#L54-L76) · [Vercel Skills 版本记录](https://github.com/vercel-labs/skills/blob/50894c1d98bc45ca1ab127213e59d6525065b5e7/README.md#L87-L112)
- GitHub Spec Kit 的项目安装覆盖了两个宿主的技能发现路径，BMAD 的稳定版由项目清单选择宿主。CodeGraph 则区分项目范围的宿主登记和项目索引初始化。FDG 借鉴前两者的默认接入范围，同时保留自己的自动修复语义。 [Spec Kit 的两端实现](https://github.com/github/spec-kit/tree/980b3d027f4bd7958c35f97f618d854b41320e3f/src/specify_cli/integrations) · [BMAD 宿主目录](https://github.com/bmad-code-org/BMAD-METHOD/blob/bab9082c3e884e0d72c759bcd1a6d90bcc01be84/tools/installer/ide/platform-codes.yaml#L59-L78) · [CodeGraph 安装范围](https://github.com/colbymchenry/codegraph/blob/67178d8f42469995bbba9ec760207efd30dfa696/README.md#L397-L433)

- 实施后从实际打包的两个 NuGet 包接入临时仓库。Codex CLI 0.169.0 在接入仓发现 4 个项目技能和 3 个待审批 hooks，在同一用户下的对照仓发现 0 个，用户配置逐字节相同。用隔离宿主配置和本地模型响应验证了 Claude Code 2.1.303、Codex CLI 0.169.0 和桌面端随附的 Codex 0.169.1 执行 Stop 后的实际格式化。Linux arm64 容器中也用 Codex CLI 0.169.0、Claude Code 2.1.303 与本地模型响应验证了 Stop 格式化。桌面 GUI 的接入操作、真实模型交互和 Windows 原生宿主仍需验收。
- macOS 与 Linux arm64 的完整测试各通过 1,259 项。最低消费 SDK 10.0.100 的 34 项集成测试通过。重新生成时 892 个文件一致，56 份迁移后的规则资料逐字节一致。
