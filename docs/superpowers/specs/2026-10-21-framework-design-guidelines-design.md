# framework-design-guidelines 设计文档

状态：待用户审阅
日期：2026-10-21

## 1. 意图

把 *Framework Design Guidelines: Conventions, Idioms, and Patterns for Reusable .NET Libraries*（第 3 版，2020，Cwalina / Barton / Abrams，以下简称 FDG3）提炼成一套开源工具，让 Claude Code 和 Codex 写 C# 代码时尽可能遵循书中的规范。适用范围是**所有 C# 代码**：对外发布的库 API、应用内部的层间契约（例如 DDD 各层之间的接口），以及具体实现。

**为什么要做。** AI 编码代理写的 C# 代码经常可读性差、API 设计随意。用户在试点仓库中观察到的实例：

- 代码挤成一行：一行多条语句，`catch {...}` 单行块，长参数列表不换行；
- 9 个位置参数的私有方法 `Failed(...)`，调用时还传了 `null`。

社区目前没有对 FDG3 的完整提炼。微软在线版只是第 2 版的节选，没有规则编号，也没有对应的分析器。

**最大风险是"不起作用"。** 用户装上以后，AI 写出的代码看不出变化。这比规则写得不完美更糟糕，是本项目头号要避免的失败。

**成功标准（用户设定）。** 公开发布后半年内 GitHub 星标超过 1k。由此推出几条要求：

- 安装门槛低；
- 效果肉眼可见；
- 误报少；
- 每条规则都能追溯到原书章节。

## 2. 目标、非目标、反目标

### 目标

- 覆盖全书：每条带图标的准则（DO / DO NOT / AVOID / CONSIDER）都有对应的规则条目；叙述性内容也有明确的去向。全书覆盖完成后才发布第一版。
- 能机器检查的规则交给确定性工具（分析器、`dotnet format`），并尽量自动修复。只有需要判断的规则才交给 AI。
- 同时支持 Claude Code 和 Codex，两端共用同一套规则和资产。
- 控制 token 消耗：宁可多花时间（本地工具运行），也要少花 token。
- 规范属于项目，而不是插件。没装插件的团队成员在 IDE 和构建中看到的警告，与 AI 看到的完全一致。

### 非目标（第一版不做）

- GitHub Copilot、Cursor 等其他宿主。目录结构上不排斥以后扩展，但不写任何适配代码。
- DDD 分层依赖检查（BannedApi、架构测试），因为书中没有这部分内容。
- 引入任何第三方分析器包（Meziantou、Roslynator、StyleCop、Sonar 等）。
- 第三方格式化工具，包括 CSharpier（理由见 §5.3）。
- 主动建议或修改目标项目的语言版本、polyfill 包（PolySharp 等）。
- AI 行为评测（遵循率、对照组）。以后单独立项。
- 老式（非 SDK 风格）csproj。整个项目（分析器包、CLI、hook）只支持 SDK 风格项目；会话开始时的自检检测到老式项目，会明确说明不支持（用户决定）。
- 排版基线、自动提交、`.git-blame-ignore-revs` 等用来降低 diff 的机制。接受老文件首次被改动时 diff 较大的代价（用户决定）。README 中给出一段可选做法，由用户自行决定是否采用。

### 反目标（技术上能跑通也算失败的情况）

- 装上后 AI 的行为没有可感知的变化，或者配置缺失时静默失效。
- 误报多、刷屏，干扰用户。
- 未经用户确认就修改目标仓库的文件，例如安装插件时自动写入配置、自动改写 AGENTS.md 或 CLAUDE.md。
- 自动修改已发布的公开 API（比如改名），破坏调用方。
- 替用户提交代码或创建分支。工具、插件命令和实施过程都**只修改工作树中的文件，不改动暂存区和提交历史**，由用户用 `git diff` 审阅后自己提交（用户决定）。术语见 §3。
- 对外宣称没有测量过的效果，例如 AI 遵循率。

## 3. 约束

- **原书内容**（用户决定）：规则和示例代码**尽量采用原书原文**，因为原文已经写得清楚凝练；发布内容中可以包含原文和原书示例代码。不做转述，也不做相似度检查。原书 epub 文件本身仍然只放在被 git 忽略的 `private-sources/` 中，作为抽取来源，不提交。
- **许可证**：本项目编写的代码和内容采用 MIT。README 和 `NOTICE` 中写明：本项目是独立整理，与作者、微软、Pearson 无关；原书摘录的权利属于原权利人；推荐读者阅读原书。
- **目标项目的范围**：所有 **SDK 风格**的 C# 项目，不限语言版本和目标框架（包括以 net472 / netstandard2.0、C# 7.3 为目标的项目）。规则按项目实际可用的语言版本和框架"能用尽用"（见 §5.6）。分析器在编译器中运行，与目标框架无关，但会受到 Roslyn 版本的限制（见 §6）。
- **工具链**：用户机器上必须有 .NET SDK。CLI 工具依赖 .NET SDK（具体最低版本由实现阶段确定）。
- **两端宿主的差异**（调研结论，以 Claude Code 2.1.30x、Codex rust-v0.163 为准）：
  - Codex 的 skill 只解析 `name` 和 `description`，不支持 Claude 的 `paths` 自动触发；
  - Codex 插件不能分发子代理；
  - Codex 的 Stop hook 不支持 `additionalContext`，只能 `decision: block` 加 `reason`；
  - Codex 不会察觉文件被外部修改，patch 上下文对不上时直接失败；
  - 两端都支持 SessionStart、UserPromptSubmit、Stop 三个 hook 事件。
- 规则提炼以第 3 版为准。第 2 版在线摘要和模型记忆都不能作为来源依据。

**术语**（用户决定）：
- **工作树**（working tree）：git 签出到磁盘上的文件。工具只修改这里的文件。
- **暂存区**（index）：工具从不改动。不执行 `git add`、`commit`、`stash`、`checkout`，也不创建分支。
- **工作目录**（current working directory）：进程当前所在的目录，例如 `dotnet fdg fix` 不带路径时的默认范围。
- 工具还会写入两处，但都不属于需要用户审阅的改动：
  - `.git/fdg/`：hook 的快照和状态，位于 git 目录内，不出现在 `git status` 中；
  - `obj/fdg/marker.json`：构建产物，由分析器包写入。
- 非 git 目录没有工作树，统称"磁盘上的文件"；这种目录下 Stop 检查停用。

## 4. 总体架构

规则通过三条渠道送达 AI，每条渠道承担不同类型的规则：

| 规则类型 | 送达方式 | token 成本 |
|---|---|---|
| 能机器检查的规则（试点中约 3–5 成） | 分析器诊断：只在违规时出现，附带规则编号、出错行和该规则的原书原文 | 只在违规时产生 |
| 需要判断的规则（约 5–7 成：类型选择、扩展点、异常设计、命名语义等） | Skill：写 C# 时按需加载，细节按主题读取 | 每会话一次，外加按需读取 |
| 项目事实和行为约定 | 会话开始时 hook 有条件地注入几行文字 | 每会话约 100–200 token |

确定性层由三件产物组成，三者版本号同步发布：

1. **分析器包 `FrameworkDesignGuidelines.Analyzers`**（NuGet，开发依赖）：自写的分析器和 CodeFix、按 profile 划分的 globalconfig、props/targets。
2. **CLI 工具 `dotnet-fdg`**（NuGet .NET 工具，调用方式为 `dotnet fdg ...`）：面向用户只有一个 `fix` 命令；另有几个供 hook 调用的内部入口。由使用 AI 的开发者全局安装，不放进目标仓库。
3. **插件 `framework-design-guidelines`**：三个用户命令（`/fdg-setup`、`/fdg-fix`、`/fdg-review`）、一个自动触发的 skill（`fdg-write`）、一个子代理、hooks.json，以及 Claude 和 Codex 两份清单。

## 5. 已确定的设计决定

以下内容已与用户逐项讨论确定。标注"（我的决定）"的是设计者自行决定的部分，用户可以在审阅时推翻。

### 5.1 Profile（代码类型配置档）

- 共三种，取值均为单数名词，描述项目的类别（用户决定）：
  - `library`：对外发布的库，规则最严格；
  - `application`：应用内部代码，包括 DDD 各层，API 设计规则照常适用，但不做二进制兼容检查；
    - 与 `library` 的区别（我的决定）：`library` 使用 `api_surface = public`，并开启书中关于版本演进和兼容性的规则；可以选择配合 PublicApiAnalyzers。第一版不引入 ApiCompat 或 Package Validation。
  - `test`：测试代码，命名规则放宽（例如允许 `Method_WhenX_DoesY`），关闭 API 设计类规则。
- 通过 **MSBuild 项目属性** `FrameworkDesignGuidelinesProfile` 声明，通常写在目录级 `Directory.Build.props` 中，例如在 `src/` 和 `tests/` 下各放一份。不在仓库根目录添加插件专属文件。
- 作用范围：**引用了分析器包、并且设置了该属性的项目**。没有引用分析器包的项目（例如试点仓库中 `<legacy-dir>/` 下的老代码）完全不受影响，hook 也会跳过这些项目中的文件。
- 分析器包的 targets 根据该属性选择对应的 globalconfig；分析器通过 `CompilerVisibleProperty` 读取该属性。
- 各 profile 中 CA 设计和命名规则的检查面：
  - `library` 使用 `dotnet_code_quality.api_surface = public`；
  - `application` 使用 `all`，这样内部层间契约同样受约束；
  - `test` 关闭 API 设计类规则。

### 5.2 分析器与配置

- 只使用 .NET SDK 自带的 CA/IDE 规则加上我们自写的分析器。SDK 自带规则中**能对应到 FDG 准则的全部开启**；对应不上的一律不开。
- SDK 自带规则的严重级别通过**分析器包内置的 globalconfig** 配置（每个 profile 一份），这和 SDK 的 `AnalysisLevel` 是同一种官方机制：
  - 不依赖 CA 或 IDE 规则包。依赖这些包只能锁定版本，无法替代配置，反而会抬高用户需要的最低工具链版本，还可能让 IDE 与构建的行为不一致；
  - globalconfig 设置 `global_level = -100`（我的决定），让用户自己的 `.editorconfig` 和 globalconfig 优先生效；
  - 用户的自定义覆盖写在他们自己的 `.editorconfig` 中，规则升级只需升级包版本。
- **已实测确认**（临时 netstandard2.0、C# 7.3 项目，SDK 10.0.402）：通过 `GlobalAnalyzerConfigFiles` 引入的 globalconfig，其中的排版选项（`csharp_preserve_single_line_statements/blocks = false`）、命名规则（IDE1006）、IDE0011、IDE0055 严重级别，在 `dotnet build`（配合 `EnforceCodeStyleInBuild`）和 `dotnet format` 中都会生效。
- props 文件负责打开 `EnableNETAnalyzers` 和 `EnforceCodeStyleInBuild`。
- 包标记为开发依赖（`PrivateAssets=all`），规则不会传递给目标库的下游使用者。
- 默认严重级别是 `warning`，不使用 `error`，否则老项目一引入就无法构建。是否在 CI 中把警告当作错误，由用户自己决定。
- **严重级别与原始强度的默认对应**（我的决定，可在 YAML 中逐条覆盖）：
  - DO、DO NOT、AVOID 设为 `warning`；
  - CONSIDER 设为 `suggestion`。这类规则在 IDE 中可见，但 Stop 不会因此打回。
  - 对应了分析器的 CONSIDER 规则，同时以一行的形式收录进 skill 参考文件，确保 AI 能看到。
  - Stop 只报告本轮改动行上的诊断（见 §5.4），所以 `application` 档即使 `api_surface = all`，老代码里原有的警告也不会打扰 AI。
- `library` profile 可以选择配合官方的 PublicApiAnalyzers 管控公开 API 变更，默认不引入。由 `/fdg-setup` 询问是否启用。
- **自写分析器**负责填补 SDK 规则的缺口。是否需要为某条规则写分析器，在每章映射时决定。标准是：判定精确、确定性强、误报率低。
  - 已知必须实现的：
    - `catch {...}`、`finally {...}` 单行块的展开。实测 Roslyn 格式化器不会展开这两种块。
    - 附录 A 规定的长参数列表折行：参数列表或调用过长时，在第一个参数前断行，缩进一级，每行一个参数，右括号紧跟最后一个参数。行宽阈值默认 120（我的决定，依据是 Jeremy Barton 在书中批注的软上限），可以配置。
  - 第 3 章试点中识别出的低成本检查：只用 ASCII 字符、缩写词大小写（使用书中词表）、过于通用的类型名、事件处理器参数命名为 `sender` 和 `e`、非事件委托以 Callback 结尾、布尔属性不使用否定式、集合属性命名、不公开可本地化资源等。
- 自写分析器应尽量提供支持 FixAll 的 CodeFix，这样 `dotnet format analyzers` 就能批量应用。**对公开 API 改名永远不自动修复。**
- 诊断编号使用 `FDG` 前缀，与规则编号一致（编号格式见 §6）。诊断的 helpLink 指向该规则的文档页。
- 分析器从编译信息中读取语言版本和目标框架（见 §5.6）。
- 分析器必须足够快，避免拖慢构建：以符号级分析为主，避免对整个编译做重复遍历。

### 5.3 排版：不使用 CSharpier

决定使用官方工具链（`dotnet format` 加 `.editorconfig`/globalconfig 选项）和我们自写的排版分析器。理由如下：

- CSharpier 把右括号单独放一行（已对照其 README 核实），这与 FDG3 附录 A 的示例冲突，而且无法配置。
- CSharpier 要求关闭 IDE0055，会和没装 CSharpier 的团队成员在 VS 中的格式化结果冲突。
- 附录 A 描述的正是 .NET BCL 团队的风格，也就是 Roslyn 格式化器的默认风格。
- 实测 `dotnet format` 能修复：一行多条语句、方法体以及 try、using、foreach 的单行块、省略大括号的 `if`（IDE0011）。修不了的两处由自写分析器补上（见 §5.2）。

FDG3 明确说明附录 A 是建议，不是强制要求。因此附录 A 的规则默认开启，但在规则数据和文档中标注为"风格约定"，用户可以覆盖。

### 5.4 Hook 行为

只使用三个事件，两端共用 `hooks/hooks.json`，逻辑全部在 `dotnet fdg` 中实现：

| 事件 | 行为 | 给 AI 的输出 |
|---|---|---|
| **SessionStart**（包括长会话压缩后，如果宿主支持重新触发） | 检测当前仓库是否有项目引用了分析器包。没有就什么也不做。有就注入项目事实和行为约定（见 §5.5），同时运行轻量自检：检查 SDK 版本、profile 是否已设置、是否存在老式项目、CLI 与包的协议版本是否兼容。配置有问题时追加一行明确提示，并告诉用户可以用 `/fdg-setup` 修复 | 0，或约 100–200 token |
| **UserPromptSubmit**（每轮开始） | 用 `git status` 记录范围内 `.cs` 文件的快照，对已修改的文件记录内容哈希。如果上一轮结束时自动修改过文件，用一行摘要告知 AI 哪些文件的哪些区域变了。Claude 自己会提醒文件变更，所以在 Claude 上不重复输出；Codex 需要这一行 | 0–1 行 |
| **Stop**（每轮结束） | 见下方流程 | 没有问题就不输出，直接结束 |

**Stop 流程：**

1. 对比快照，得出本轮改动的 `.cs` 文件，包括 AI 通过 shell 新建或修改的文件；只保留范围内的文件，按项目分组。
2. 对这些文件运行 `dotnet format`，依次执行 whitespace、style、analyzers，用 `--include` 限定文件。自写分析器的修复也在这一步自动应用。目的是用时间换 token：能自动修的问题绝不交给 AI。
3. 对涉及的项目做增量构建（`--no-restore`），并输出 SARIF 诊断日志（我的决定：比解析构建输出文本更可靠）。
4. 只保留**本轮改动行**上、级别为 warning 及以上、并且修不掉的诊断，最多 20 条。改动行由快照与当前内容的 diff 得出。每条包含：规则编号、文件和行号、出错的那一行代码，以及该规则的原书原文（一句话的 DO/AVOID 条目本身）。
   - 只报告规则数据中登记过的规则（FDG 自写规则，以及映射到的 CA/IDE 规则），外加改动文件中的**编译错误**（不限行）。其他来源的警告不报告，比如用户自己启用的其他规则、编译器的可空性警告（我的决定）。
5. 有剩余问题就阻止本轮结束，让 AI 修正。**同一轮最多打回 2 次**（我的决定，可配置），之后只报告剩余问题并允许结束，避免陷入反复修改。没有问题就静默结束，不让 AI 多跑一轮。
6. 记录本轮被自动修改过的文件和行范围，供下一轮开始时生成摘要。

**设计依据：** 轮次进行中不改动任何文件，Codex 的 patch 就不会因为上下文对不上而失败，两端行为一致。用快照对比而不是监听编辑工具，可以捕获 AI 通过 shell 写入的文件。

**整文件格式化**（用户决定）：范围内的文件一旦被 AI 改动，Stop 时对整个文件执行格式化和自动修复，不做按行限定。老文件首次被改动时 diff 会较大，这是接受的代价，工具不做任何补偿处理。README 中给出一段可选做法：先运行 `dotnet fdg fix`，单独提交，再把这次提交登记到 `.git-blame-ignore-revs`。是否采用由用户自己决定。

**实现方式与耗时**（我的决定）：第一版由 CLI 直接调用 `dotnet format` 和 `dotnet build`，多个项目尽量并行处理。默认超时 60 秒，可以配置；超时时输出明确提示，并允许本轮结束。在纵向切片中测量开启全部规则后的实际耗时。只有当常见情况明显超过 30 秒时，才考虑改为常驻进程（长期持有 MSBuildWorkspace）。为此，CLI 内部要把"执行格式化、修复和构建"抽象成可替换的实现。

**版本兼容**（我的决定）：
- 规则原文等规则数据随分析器包分发，CLI 从目标仓库所引用版本的包中读取，**以仓库锁定的包版本为准**。
- CLI 与包之间约定一个协议版本，例如 obj 标记文件的格式。只有协议不兼容时才提示升级 CLI；版本号不同但协议兼容时不告警。

**健壮性要求：** 构建失败、超时、工具未安装、包版本与 CLI 版本不一致时，都要输出一行明确的提示，**绝不静默跳过**。用户可以关闭构建步骤、调整打回上限和超时时间；配置机制由实现者决定，两端都要能用。

**实测耗时参考**（试点仓库，SDK 10.0.402，已预热，项目当时几乎没有启用分析器）：

- 改动一个文件后的增量构建：1.1–2.5 秒；
- `dotnet format analyzers --include` 单个文件：2.6–6.1 秒；
- `dotnet format style --include` 单个文件：约 9.3 秒。

启用全部规则后的耗时要重新测量。

### 5.5 Skill 与会话注入

**一个自动触发的 skill 和三个用户命令**（命令设计见 §5.9）。SKILL.md 手写，参考文件由规则数据生成：

- `fdg-write`：写或修改 C# 代码时由 AI 自动使用，用户不需要手动调用。
  - SKILL.md 只放判断类规则中最重要的几十条，按重要性排序，最关键的放在最前面，因为长会话压缩时每个 skill 只保留开头部分。正文中写明"什么情况下读哪个参考文件"。
  - `references/` 按书的章节和主题拆分，**只收录判断类规则**；分析器已经覆盖的规则不收录。规则使用原书原文；示例代码和较长的解释放进按需读取的二级文件，不放进主参考文件，以控制 token。依赖新语法的规则标注版本标记，例如 `[C# 9+]`，并附上替代写法。
  - 在 Claude 上加 `paths: **/*.cs`。
  - 触发描述要写得主动一些，覆盖 C#、.NET、类型、成员、公开 API、库设计等关键词。
- `fdg-setup`、`fdg-fix`、`fdg-review`：用户命令，见 §5.9。
- SKILL.md 遵循 Agent Skills 开放规范：不超过 500 行，参考文件只引用一层。

**会话注入**：不写入目标仓库的 AGENTS.md 或 CLAUDE.md，改由 SessionStart hook 有条件地注入。内容示例：

```
本仓库遵循 Framework Design Guidelines（第 3 版）。
- src/** 为 application 档，tests/** 为 test 档；C# 7.3，目标 net472/netstandard2.0，不要使用更新的语法或 API。
- 按 .NET BCL 风格书写：Allman 大括号，一行一条语句，长参数列表在第一个参数前断行、每行一个。
- 排版和可机检规则会在每轮结束时自动修复和检查，只需处理报告给你的 FDG/CA/IDE 问题。
- 设计或修改类型、成员、公开接口前，先使用 fdg-write skill；评审 API 时使用 fdg-review。
```

- profile 分布、语言版本和目标框架是从目标项目中实际读取的。
- 文档另外提供同样一段文字，供不使用插件的用户自行贴进 AGENTS.md。

### 5.6 语言版本适配

- 只按目标项目现有的语言版本和目标框架适配，**不涉及 polyfill，也不建议提高语言版本**。
- 每条规则在数据中记录最低 C# 版本或目标框架要求，以及老版本下的替代写法。
- 分析器读取实际的语言版本和目标框架：条件不满足时不报告依赖新特性的建议，或者改为给出替代写法。
- Skill 依靠版本标记，加上会话注入中告知的项目版本，由 AI 自行判断，不为每个版本单独生成一套内容。

### 5.7 规则源数据与生成

- **唯一来源**：`rules/` 下按章节拆分的 YAML 文件（全书约 10–20 个），用 JSON Schema 校验。
- 每条规则的字段至少包括：
  - 编号；
  - 原始强度（DO / DO NOT / AVOID / CONSIDER）；
  - **原书原文**：条目本身，以及紧随其后的解释段落和示例代码（由抽取脚本确定性地取出）；
  - 可选的中文说明（由实现者决定是否增加）；
  - 章节出处；
  - 适用的 profile，以及每个 profile 下的严重级别；
  - 语言版本要求和替代写法；
  - 检查方式：对应的 CA/IDE 规则编号、自写分析器编号，或"需要判断"；
  - 是否能自动修复：可修复 / 不可修复 / 修复属于破坏性改动；
  - 核对状态：`draft` 或 `verified`；
  - 是否属于附录 A 的风格约定。
- **生成器**（`src/Fdg.Generator`，只在开发时使用，不发布）从 YAML 生成：
  1. 自写分析器的描述符和消息资源（参照 Roslynator 从 XML 生成的做法）；
  2. 每个 profile 一份 globalconfig；
  3. skill 参考文件（只含判断类规则）；
  4. 每条规则的文档页（作为诊断 helpLink 的目标）；
  5. 覆盖率报告：按章节统计条目数、工具检查数、需判断数、已核对数。
- 生成物提交进仓库，CI 重新生成后用 `git diff` 检查是否漂移。
- CI 还要验证映射：引用的 CA/IDE 规则编号必须真实存在于当前的分析器中。
- **规则编号一经发布永不复用。** 删除的规则标记为 deprecated。

### 5.8 全书提炼流程

逐章进行，每章一轮：

1. **抽取**：用脚本从 `private-sources/` 中的 epub 解析出该章所有带图标的准则，连同章节、强度、原文、解释段落和示例代码，直接生成 YAML 骨架。这一步是确定性的，原文不经过 AI 改写。
2. **映射**：由 AI 查找对应的 CA/IDE 规则，标注能否机器检查、能否自动修复、是否需要自写分析器、适用的 profile 和语言版本要求，状态为 `draft`。
3. **自动校验**：
   - YAML 条目数必须等于该章的图标数（试点中第 3 章正好是 87 条对 87 个图标）；
   - Schema 校验；
   - 原文字段与重新抽取的结果一致（防止手工误改）；
   - 映射的规则编号必须存在。
4. **交叉核对**：由一个独立的 AI 代理对照原书逐条核对：映射是否合理、profile 和例外条件是否与原文一致、示例代码是否抽取完整（尤其是跨多个代码块或带批注的条目）。核对通过后改为 `verified`。**用户只做抽查。**
5. **非准则内容**：叙述、专家批注、历史说明、已过时的建议，在覆盖率文档中逐节登记去向：作为某条规则的理由、背景说明，或已过时。

**覆盖边界与发布门槛**（我的决定）：
- 第 2 到 9 章和附录 A 中带图标的条目，全部建立规则。
- 附录 B（已过时的准则）只在覆盖率文档中登记，不建立规则。
- 附录 C（API 规范示例）作为 `fdg-review` 的参考背景。
- 附录 D（破坏性变更）中带图标的条目建立规则，适用于 `library` 档。
- 首次发布要求所有规则都处于 `verified` 状态。
- 按图标数粗略估算（未精确去重），全书约 700 到 800 条规则。

第 3 章的试点产物（抽取脚本、解析后的 epub、参考资料下载）保存在 `private-sources/fdg3-pilot/`，可以复用。

### 5.9 命令设计

所有命令都遵守同一条原则：**只修改工作树中的文件，不改动暂存区和提交历史，也不创建分支**，用户用 `git diff` 自行审阅。

**CLI：只有一个面向用户的命令**（用户决定）

```
dotnet fdg fix [<路径>...]
```

- `<路径>` 可以是目录、`.csproj`、`.sln` 或 `.slnx`、单个 `.cs` 文件，可以传多个。默认是当前目录。
- 只处理引用了分析器包、并设置了 profile 的 SDK 风格项目，其余项目跳过。
- 依次完成三件事：改排版、应用 IDE 风格修复、应用非破坏性的分析器修复（等价于对范围内的文件依次运行 `dotnet format whitespace / style / analyzers`）。
- **不出报告，不提供规则筛选或其他选项。** 剩余问题通过正常构建，在 IDE 中或通过 `/fdg-fix` 查看。
- 破坏性修复（改公开 API 的名字或签名）永远不执行。

另外有几个供 hook 调用的内部入口（会话开始、每轮开始、每轮结束），负责解析两端宿主传入的 hook 输入。这些入口不写进面向用户的文档。插件中的 hook 只是一层很薄的外壳，检测到 `dotnet fdg` 未安装时，要明确提示安装命令。

**插件：三个用户命令，Claude Code 和 Codex 共用**（用户同意）

| 命令 | 用途 | 是否修改代码 |
|---|---|---|
| `/fdg-setup` | 为项目接入 FDG | 改配置，留在工作树 |
| `/fdg-fix [<路径>...]` | 让指定代码通过所有工具检查 | 改代码，留在工作树 |
| `/fdg-review [<路径>...]` | 判断类规则的设计审查 | 不改代码 |

- **`/fdg-setup`**：由 AI 完成以下步骤：
  1. 识别仓库中的 SDK 风格项目，发现老式项目时说明不支持；
  2. 询问用户各目录属于哪个 profile，并给出推荐，例如 `tests/` 推荐为 test；`library` 档询问是否启用 PublicApiAnalyzers；
  3. 添加分析器包引用（兼容中央包管理 `Directory.Packages.props`），在目录级 `Directory.Build.props` 中写入 profile；
  4. 构建一次，确认规则已经生效，并提示 CLI 的安装命令。
- **`/fdg-fix [<路径>...]`**：
  1. 运行 `dotnet fdg fix <路径>`，先完成所有能自动完成的修改；
  2. 构建涉及的项目，收集剩余问题。只收集规则数据中登记过的 FDG/CA/IDE 规则，以及编译错误；
  3. AI 按项目分批修复，每批完成后构建验证，以控制上下文；
  4. 非公开符号的改名，AI 直接在整个解决方案范围内重命名。**公开 API 的破坏性修改（改名、改签名、改类型）先列出清单，等用户确认后再改**；
  5. 最后重新运行一次 CLI 和构建，输出简短总结：自动修复了多少、AI 修改了多少、还剩哪些需要用户决定。
  - `/fdg-fix` **不做判断类规则的审查**（用户同意）：对整个目录做设计审查，成本高且不可预测，大部分意见也需要人来决定。这部分交给 `/fdg-review`，由用户按需对小范围使用。
- **`/fdg-review [<路径>...]`**：
  - 不带路径时，默认审查当前的 git 改动。
  - 按判断类规则逐项审查，输出格式固定：每条意见附规则编号和原书原文，不编造没有依据的建议，可参照 aspnetcore 的 `review-public-api`。
  - 只给意见，不改代码；结束时询问是否应用其中某几条。
  - 在 Claude 上交给 `agents/fdg-reviewer.md` 子代理执行，主会话只接收结论。Codex 插件不能分发子代理，所以在主会话中执行。
- **两端调用方式的差异**（实施时实测）：在 Claude 上以 `/fdg-fix src` 这样的形式调用（可能需要带插件名前缀）；在 Codex 上通过 `$fdg-fix` 或自然语言触发，路径从用户消息中读取。skill 内容两端共用。

### 5.10 仓库结构

```
framework-design-guidelines/
├── .claude-plugin/marketplace.json        ← 仓库本身就是插件市场（Codex 也能识别）
├── plugins/framework-design-guidelines/
│   ├── .claude-plugin/plugin.json
│   ├── .codex-plugin/plugin.json          ← 与上面指向同一套内容
│   ├── skills/fdg-write/                  ← 自动触发：SKILL.md（手写）+ references/（生成）
│   ├── skills/fdg-setup/                  ← 用户命令
│   ├── skills/fdg-fix/                    ← 用户命令
│   ├── skills/fdg-review/                 ← 用户命令
│   ├── agents/fdg-reviewer.md             ← 仅 Claude 使用
│   └── hooks/hooks.json                   ← 两端共用，调用 dotnet fdg
├── rules/                                 ← 按章节拆分的 YAML + JSON Schema
├── src/
│   ├── FrameworkDesignGuidelines.Analyzers/   ← 分析器、CodeFix、globalconfig、props/targets
│   ├── Fdg.Cli/                           ← dotnet-fdg
│   └── Fdg.Generator/                     ← 仅开发时使用
├── tests/
├── docs/                                  ← 设计文档、每条规则的文档页、覆盖率报告
└── private-sources/                       ← 已被 git 忽略：原书和试点产物
```

插件名不能以 `claude-` 或 `anthropic-` 开头。清单字段以当前的官方文档为准，并在两端分别实际安装验证。

### 5.11 测试

第一版**只做确定性层的测试**，全部不调用模型，每个 PR 都运行：

- 分析器和 CodeFix 测试：用 Microsoft.CodeAnalysis.Testing，覆盖 net472 引用程序集等目标；
- globalconfig 集成测试：参照本次实测，在临时项目中验证构建警告和 `dotnet format` 的修复结果；
- 生成器测试，以及生成物漂移检查；
- CLI 测试：`fix` 对各种路径形式（目录、项目、解决方案、单文件、多个路径）的处理，以及跳过不在范围内的项目；两端 hook 输入的解析、快照对比、诊断过滤和截断、各种失败路径的提示信息；
- 插件清单校验，例如 `claude plugin validate --strict`。

README 中如实写明：目前只有确定性层经过测试，尚无 AI 遵循率数据。

## 6. 留给实现者决定的部分

- 规则编号的具体格式。要求：稳定、永不复用、能看出所属章节，并且同时用作诊断编号（`FDG` 前缀）。
- CLI 内部 hook 入口的命名和参数，以及 hook 的配置机制（打回上限、超时、关闭构建步骤）。
- hook 如何低成本地判断一个文件属于哪个 profile。例如由包的 targets 在构建时往 `obj/` 写入一个标记文件，读不到时再退回 `dotnet msbuild -getProperty`。
- 每章中哪些缺口值得写自写分析器（按 §5.2 的标准判断）。
- 文档站的形式。
- YAML 是否增加中文说明字段。
- 分析器包要求的最低 Roslyn 版本，以及由此决定的用户最低 VS/SDK 版本（必须在 README 中写明）。

## 7. 分阶段推进

全书覆盖完成后才发布第一版。内部按以下顺序推进：

1. **纵向切片**：用第 3 章打通整条链路，包括 YAML 加 Schema、生成器、分析器包（含 globalconfig 和首批自写分析器，例如两个排版分析器）、CLI（`fix` 和 hook 入口）、四个 skill、两端插件清单，并在试点仓库上实际跑通。根据切片暴露的问题调整规则格式和流程。
   - 试点仓库是 `<pilot-repo>`，`src/` 下有 SDK 风格项目的分支（2026-10-21 时为 `<pilot-branch>`）。注意 `<another-local-copy>` 是另一份副本，不是试点仓库。
   - 实施过程**不在试点仓库中创建分支或提交**（用户决定）。需要的改动（包引用、`Directory.Build.props` 中的 profile）只留在工作树中，交给用户审阅；或者在用户同意的本地副本中进行。具体做法在实施计划中与用户确认。
   - 验收标准（我的决定）：
     - 可确定性检查的部分：构建时出现预期的 FDG/CA/IDE 警告；`dotnet fdg fix` 对预先埋好的排版和风格违规能正确修复，并跳过不在范围内的项目；对预先埋好违规的改动，Stop 流程能完成自动修复并正确报告剩余问题；SessionStart 注入的内容正确。
     - `/fdg-setup` 在试点仓库上生成的配置改动正确（只留在工作树中）。
     - 两端各做一次真实 AI 会话的冒烟测试，只用来确认链路能跑通，不作为效果评测。
2. **逐章扩展**：按 §5.8 的流程完成其余章节和附录，同时补充自写分析器。
3. **发布准备**：同步版本号，编写 README（安装方式、前后对比、覆盖率、声明）和 NOTICE。

## 8. 实现阶段必须验证的事项

- VS 和 Rider 的实时分析是否识别包内的 globalconfig。目前只验证了命令行。
- globalconfig 的 `global_level` 与用户设置的 `AnalysisLevel` / `AnalysisMode` 之间的优先级关系。
- 自写分析器的 FixAll 修复能否被 `dotnet format analyzers --include` 正确应用。
- 两端 SessionStart 在长会话压缩后是否会重新触发，以及 additionalContext 是否生效。
- 两端 Stop 阻止结束时的实际行为，以及连续阻止次数的上限。
- 全部规则开启后的构建和格式化耗时。
- Codex 的 hook 是否需要用户信任或批准、插件更新后是否需要重新批准，以及安装文档要怎么写。

## 9. 参考资料

- FDG3：出版社页面 https://www.informit.com/store/framework-design-guidelines-conventions-idioms-and-9780135896501
- Claude Code 文档：skills、hooks、plugins-reference、memory（code.claude.com/docs）
- Codex 源码：openai/codex rust-v0.163.0（hooks schema、skills parser、plugin manifest）
- Agent Skills 规范：https://agentskills.io/specification
- dotnet/skills（双端插件结构与评测参考）：https://github.com/dotnet/skills
- aspnetcore 的 review-public-api skill（评审输出格式参考）
- Roslynator `src/Analyzers.xml` 与生成器；Meziantou.Analyzer 的文档和配置生成
- NetAnalyzers 规则清单与 CodeFix 标注（dotnet/sdk）
- CSharpier README（右括号单独成行的示例）
- IFScale（arXiv:2507.11538）：指令数量与遵循率
