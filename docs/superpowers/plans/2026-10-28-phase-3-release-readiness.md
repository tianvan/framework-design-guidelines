# FDG 第三期：发布准备，迈向 1.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 `docs/phase-2-backlog.md` 中的每一项做完或写明理由移到发布之后，让 AI 收到的信息更准确、Stop 更快、发布工程可靠，最后经过 rc 发布 1.0.0。

**Architecture:** 先在 0.2.0 上测出 Stop 的基线（Task 1），然后依次做：低风险的机械改动（Task 2–6）、信息准确性（Task 7–12）、hook 与 CLI 的健壮性（Task 13–15）、Stop 提速（Task 16）、文档（Task 17）。最后是全分支终审和发布（Task 18）。规则数据仍是唯一来源，生成的文件只由 `generate` 写出。

**Tech Stack:** .NET SDK 10.0.402（global.json）。分析器为 netstandard2.0 + Microsoft.CodeAnalysis.CSharp 4.8.0；CLI 为 net8.0（RollForward Major）；生成器为 net10.0；测试用 xUnit。脚本用 PowerShell 7 和 bash。CI 用 GitHub Actions。

**Spec:** `docs/superpowers/specs/2026-10-28-phase-3-release-readiness-design.md`（以下简称“本期设计”，§ 编号指它）。总设计和第二期设计仍然有效。

## Global Constraints

- 前两期计划的 Global Constraints 继续有效：
  - 只改工作树文件；
  - 所有工具输出和 skill 正文用英文；
  - 外部进程用 `ArgumentList` 传参；
  - 严重级别不使用 `error`；
  - 自写分析器 `isEnabledByDefault: false`；
  - 对公开 API 改名永不自动修复。
- epub 只读，读取它的 Python 一律用 `python -I` 运行；`private-sources/` 永不提交。只有改动了规则 notes 的任务才运行 `check --book`。原书路径写作 `$BOOK`，即 `private-sources/fdg3-pilot/data/ex/OEBPS/`。
- 试点克隆（`D:\Repos\fdg-pilot\`）：只能通过 `eng/noise/Measure-Noise.ps1` 使用；不做 git 操作，不修改。用户的内部仓库不得修改。写进仓库的结果一律匿名：只出现 internal-a、internal-b 和计数，不出现项目名、命名空间、路径或域名。
- 不复制任何凭据或认证文件。不改用户的 Claude 和 Codex 配置。
- push、开 PR、合并、打 tag、发布之前，都必须先征得用户同意。0.2.0 的授权不延续到本期。
- `plugins/framework-design-guidelines/hooks/hooks.codex.json` 在本期只改一次，就在 Task 13。
- 不新增规则、章节或自写分析器。例外是为修复 backlog 问题而改动现有分析器。
- 最低 SDK 为 10.0.100。参考文件（`references/` 下的每个 `.md`，包括 `examples/`）不超过 32768 字节。fdg-write 的 SKILL.md 不超过 500 行。参考文件名一律小写。
- Stop 的目标：热启动单文件的中位数至少比 0.2.0 基线低 30%；冷启动低于 30 秒。基线和优化后在同一台机器、同一个样本副本上测量。
- 噪音门槛：误报不超过 1/20（第二期设计 §5）。
- 通用验证命令（每个任务提交前都要通过，与该任务无关的除外）：
  - `fdg-gen generate`，即 `dotnet run --project src/Fdg.Generator -- generate`。第二次运行必须输出 `0 written`。
  - `dotnet test -c Release -nologo`
  - `fdg-gen check`
  - `claude plugin validate --strict plugins/framework-design-guidelines` 和 `claude plugin validate --strict .`
  - 改动了规则 notes 时再加：`set -o pipefail; dotnet run --project src/Fdg.Generator -- check --book $BOOK`
- 代码风格沿用仓库现有的 .NET BCL 风格。

## Review Focus

1. **插件与工具版本不一致时 hook 静默失效**。例如插件已经是 1.0，工具还是 0.2.0；或者工具因为缺少运行时而无法启动。去掉 `--version` 探测之后，只要工具以非零码退出且 stdout 为空，包装脚本就必须输出一条 `systemMessage`，内容包含 stderr 的第一行。测试在 Task 13（`Wrapper_reports_a_tool_failure_without_output`，bash 和 pwsh 各一个）。
2. **跳过第二轮格式化后，可修复的问题既没被修复也没被报告**。构建结果中，改动文件里仍有 `FixDiagnostics` 的结果时，必须再格式化一轮并重新构建；修不掉的照常报告。测试在 Task 16（`Second_format_pass_runs_when_a_fixable_diagnostic_remains`）。
3. **只改大小写的重命名没有生效**。Windows 的文件系统和 `core.ignorecase=true` 下，`appA-coding-style.md` 改成 `appa-coding-style.md` 后，工作树或 git 索引可能仍是旧名字，导致 Linux 上的链接失效。生成器写文件时要更正大小写；skill 中的链接检查要按大小写精确比较。测试在 Task 4（`Write_fixes_the_case_of_an_existing_file`、`References_are_linked_with_exact_case`）。
4. **比较基类列表后，命名类诊断被当成新代码打回**。已有类型本轮新增了基类时，CA1710 这类命名诊断仍然算已有 API，不能让 AI 给已发布的类型改名。测试在 Task 7（`Naming_diagnostic_on_an_existing_type_with_a_new_base_stays_existing`）。
5. **路径中的特殊字符**。插件路径可能含空格、CJK 字符、`$`、反引号或 `'`，Codex 在 Windows 上可能用 PowerShell 或 cmd 作会话 shell；仓库路径可能含 `;` 或 `,`。这些情况下 hook 和构建都必须正常工作。测试在 Task 13（`Codex_windows_command_survives_special_characters`）和 Task 15（`Build_properties_escape_semicolons_and_commas`）。

---

## 文件结构

| 文件 | 职责 | 任务 |
|---|---|---|
| `eng/perf/Measure-Stop.ps1`（新） | 在开源样本的临时副本上测量 Stop | 1、16 |
| `docs/performance.md`（新） | Stop 基线、优化后的数据、加包后的构建开销 | 1、16 |
| `src/Fdg.Cli/Hooks/SelfCheck.cs`、`tests/Fdg.IntegrationTests/SampleWorkspace.cs`、`.github/workflows/ci.yml` | 最低 SDK 10.0.100，以及对应的 CI job | 2 |
| `.github/workflows/ci.yml`、`release.yml`、`tests/Fdg.Generator.Tests/WorkflowLintTests.cs`（新） | Actions 固定到 SHA | 3 |
| `src/Fdg.Generator/Emit/RuleText.cs`、`SkillReferenceEmitter.cs`、`GeneratedFiles.cs`、三个 skill 或 agent 文件 | 参考文件名小写 | 4、11 |
| `src/Fdg.Generator/Commands/CheckCommand.cs`、`Sdk/SdkDiagnosticCatalog.cs`、`Rules/RuleLoader.cs`、`Rules/RuleYaml.cs`、`Emit/DescriptorEmitter.cs`、`Emit/GlobalConfigEmitter.cs`、`rules/rules.schema.json` | 生成器健壮性 | 5 |
| `.editorconfig`、`src/FrameworkDesignGuidelines.CodeFixes/Layout/*`、`src/FrameworkDesignGuidelines.Analyzers/Layout/LineLength.cs` | 仓库卫生和 CodeFix 边界情况 | 6 |
| `src/Fdg.Cli/Hooks/ExistingApi.cs`、`DeclarationLocator.cs`、`HookRunner.cs` | 比较基类列表 | 7 |
| `src/Fdg.Cli/Hooks/DeclarationLocator.cs`、`StopReport.cs`、`HookRunner.cs` | 可见的 const 同时属于两种种类 | 8 |
| `rules/*.yaml`（CA1710 相关规则）、`docs/noise-samples.md` | CA1710 的 `exclude_indirect_base_types` | 9 |
| `plugins/framework-design-guidelines/skills/fdg-setup/SKILL.md`、`rules/appA-coding-style.yaml`（FDGA048 notes）、`docs/install.md` | IDE0280、IDE0250、IDE0251 的 `.editorconfig` | 10 |
| `src/Fdg.Generator/Emit/SkillReferenceEmitter.cs`、`rules/meta.yaml`、三个 skill 或 agent 文件 | 部分检查的规则进入参考文件 | 11 |
| `src/Fdg.Cli/Hooks/Autofix.cs`、`StateStore.cs`、`HookRunner.cs` | 自动修复记录的合并 | 12 |
| `plugins/framework-design-guidelines/hooks/*` | 包装脚本，以及 Codex 的引号 | 13 |
| `src/Fdg.Cli/Hooks/Onboarding.cs`、`Projects/ProjectInfoProvider.cs`、`Projects/ProjectFiles.cs`、`Hooks/SessionContext.cs` | 接入判断、marker、会话检查的边界情况 | 14 |
| `src/Fdg.Cli/Tooling/DotnetBuildRunner.cs`、`DotnetFormatRunner.cs`、`Hooks/HookRunner.cs`、`Hooks/StateStore.cs`、`Commands/FixCommand.cs`、`src/Fdg.Cli/IO/AtomicFile.cs`（新） | 构建和格式化调用的边界情况 | 15 |
| `src/Fdg.Cli/Hooks/HookRunner.cs`、`Tooling/IBuildRunner.cs`、`Projects/ProjectReferences.cs`（新）、`eng/noise/Measure-Noise.ps1` | Stop 提速，以及加包后的构建开销 | 16 |
| `docs/releasing.md`、`CHANGELOG.md`（新）、`README.md`、`docs/install.md`、`docs/hosts.md`、`docs/backlog.md`（由 `docs/phase-2-backlog.md` 改名而来） | 文档 | 17 |

`IBuildRunner` 现在定义在哪个文件，以实际代码为准。上表的文件名只表示职责。

---

### Task 1: Stop 基线（0.2.0）

本期设计 §4.3 第 1 步。必须在任何 hook 改动之前完成。这个任务不改产品代码。

**Files:**
- Create: `eng/perf/Measure-Stop.ps1`
- Create: `docs/performance.md`

**Interfaces:**
- Produces: `eng/perf/Measure-Stop.ps1`，参数如下：
  - `-Sample <name>`：`eng/noise/samples.json` 中的样本名。
  - `-Package <nupkg>`：要测的分析器包。
  - `-Cli <path>`：`dotnet-fdg` 可执行文件或 `.dll`。如果是 `.dll`，用 `dotnet <dll>` 运行。
  - `-WorkRoot <dir>`：一个在本仓库之外的目录。
  - `-Out <json>`：结果文件。
  - `[-Runs 5]`，`[-Projects 1]`（本轮改动的项目数，1 到 3），`[-Cold]`。
- 输出 JSON：`{ sample, package, cli, projects, cold, runs: [秒…], median, machine }`。`machine` 写操作系统、CPU 型号和逻辑核数。
- Task 16 用同一个脚本、同一个样本重新测量。

- [ ] **Step 1: 写脚本**
  - 把样本按固定提交克隆到 `<WorkRoot>/perf/<sample>`，复用 `Measure-Noise.ps1` 中取样本的写法。在这个副本里接入包：
    - 在项目中写入 PackageReference 和 `FrameworkDesignGuidelinesProfile`；
    - 加一个指向包所在目录的 `NuGet.config`；
    - 在副本里 `git commit`。这是我们自己的临时克隆，允许 git 操作。
  - `NUGET_PACKAGES` 设为 `<WorkRoot>/perf/packages/<包文件名去掉扩展名>`。基线和改动后的包版本号可能相同，独立的缓存可以避免互相串用。
  - 先完整构建一次。之后每轮的步骤：
    1. 用 `tests/fixtures/hooks/claude/` 中的 payload（把 `cwd` 和会话 id 换掉）运行 `hook turn-start`；
    2. 写入改动；
    3. 计时运行 `hook turn-stop`；
    4. 用 `git checkout -- .` 和 `git clean -fd` 还原。
  - 改动沿用试点的做法：在一个文件中加入带单行 `try { … } catch (Exception ex) { … }` 的方法。具体用哪个文件由实现者决定，写进 `docs/performance.md`。
  - `-Projects 2` 或 `3` 时，在同一轮中改动 2 到 3 个相互引用的项目，每个项目各一个文件。建议单项目用 serilog，多项目用 polly。
  - `-Cold` 时，每次计时前运行 `dotnet build-server shutdown`。
  - 正式计时前先跑一轮预热，不计入结果。
- [ ] **Step 2: 在 HEAD（0.2.0 代码）上打包并发布 CLI**
  - 包：`dotnet pack src/FrameworkDesignGuidelines.Package -c Release -p:Version=0.2.0-perfbase.1 -o <WorkRoot>/perf/baseline`
  - CLI：`dotnet publish src/Fdg.Cli -c Release -o <WorkRoot>/perf/baseline/cli`
  - 这两份产物留在 WorkRoot 中，Task 16 会再次用到。
- [ ] **Step 3: 测量**，共三组：
  - 单项目热启动：`-Runs 5`；
  - 单项目冷启动：`-Runs 3 -Cold`；
  - 多项目热启动：`-Projects 2 -Runs 3`。
- [ ] **Step 4: 写 `docs/performance.md`**
  - 新建 “Stop time” 一节，内容包括：方法、样本和提交、改动所在的文件、机器信息，以及三组数据，并列出每次运行的秒数和中位数。
  - 写明这是 0.2.0 的基线。
  - 只出现开源样本，不提试点。
- [ ] **Step 5: 提交**

```bash
git add eng/perf/Measure-Stop.ps1 docs/performance.md
git commit -m "perf: measure the 0.2.0 Stop baseline on an open-source sample"
```

### Task 2: 最低 SDK 改为 10.0.100

本期设计 §4.1 第 1 项。

**Files:**
- Modify: `src/Fdg.Cli/Hooks/SelfCheck.cs`、`tests/Fdg.Cli.Tests/Hooks/SessionContextTests.cs`
- Modify: `tests/Fdg.IntegrationTests/SampleWorkspace.cs`
- Modify: `.github/workflows/ci.yml`
- Modify: `README.md`、`docs/install.md`、`plugins/framework-design-guidelines/skills/fdg-setup/SKILL.md`

**Interfaces:**
- Produces：
  - `SelfCheck.MinimumSdkVersion = "10.0.100"`；删除 `FullCoverageSdkVersion`。
  - 环境变量 `FDG_TEST_SDK`：设置后，`SampleWorkspace` 不再复制仓库的 `global.json`，而是写入 `{"sdk":{"version":"<值>","rollForward":"disable"}}`。

- [ ] **Step 1: 写失败的测试**（`SessionContextTests`，改写原有的 SDK 用例）

```csharp
[Theory] [InlineData("9.0.300")] [InlineData("8.0.100")]
public void Sdk_older_than_the_minimum_is_an_issue_without_a_setup_hint(string version)
// issue == $"FDG setup issue: .NET SDK {version} is older than 10.0.100, the oldest SDK FDG supports; some rules do not run."
// 不含 "/fdg-setup"

[Theory] [InlineData("10.0.100")] [InlineData("10.0.402")] [InlineData("11.0.100-preview.1")]
public void Supported_sdk_is_no_issue(string version)

[Fact] public void Failed_dotnet_version_names_the_new_minimum()
// "... FDG needs .NET SDK 10.0.100 or later. Run /fdg-setup to fix."
```

- [ ] **Step 2: 运行，确认失败；实现；再运行，确认通过**

运行：`dotnet test tests/Fdg.Cli.Tests --filter "FullyQualifiedName~SessionContextTests"`

- [ ] **Step 3: `SampleWorkspace` 读取 `FDG_TEST_SDK`**
  - 本地用已安装的 10.0.1xx SDK 代替 10.0.100 验证：`FDG_TEST_SDK=10.0.114 dotnet test tests/Fdg.IntegrationTests -c Release`。
  - 依赖新 SDK 行为的测试有两种处理办法：改成与 SDK 版本无关；或者在 `FDG_TEST_SDK` 设置时跳过，并在跳过原因里写明需要哪个 SDK 版本。
- [ ] **Step 4: CI 增加 job `integration-minimum-sdk`**
  - 运行在 `ubuntu-latest`；
  - `setup-dotnet` 同时安装 `10.0.402`（构建仓库自身）和 `10.0.100`；
  - 设置 `env: FDG_TEST_SDK: 10.0.100`；
  - 运行 `dotnet test tests/Fdg.IntegrationTests -c Release -nologo`。
  - Actions 的写法与现有 job 一致；Task 3 再统一固定 SHA。
- [ ] **Step 5: 修改文案**
  - README 的 Requirements、`docs/install.md` 第 8 行附近、fdg-setup 第 3 步，都改为 SDK 10.0.100 或更高。
  - fdg-setup 遇到旧 SDK 时，说明 FDG 支持 10.0.100 及以上、部分规则不会运行，然后询问用户是否继续，不再直接停止。
- [ ] **Step 6: 运行通用验证，然后提交**

```bash
git commit -am "feat: raise the minimum SDK to 10.0.100 and test it in CI"
```

### Task 3: Actions 固定到提交 SHA

本期设计 §4.1 第 2 项。

**Files:**
- Modify: `.github/workflows/ci.yml`、`.github/workflows/release.yml`
- Create: `tests/Fdg.Generator.Tests/WorkflowLintTests.cs`

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Every_action_is_pinned_to_a_commit_sha_with_its_version()
// .github/workflows/*.yml 中每个 `uses:` 都匹配 ^[\w.-]+/[\w.-]+(/[\w./-]+)?@[0-9a-f]{40} # v\d+(\.\d+){0,2}$
// 本地可复用工作流（uses: ./.github/workflows/...）除外
```

- [ ] **Step 2: 运行，确认失败**

运行：`dotnet test tests/Fdg.Generator.Tests --filter "FullyQualifiedName~WorkflowLintTests"`

- [ ] **Step 3: 选版本并固定**
  - 每个 action 取最新的稳定标签，查法：`git -c http.proxy=http://127.0.0.1:10808 ls-remote --tags https://github.com/<owner>/<repo>`。该标签的 `action.yml` 中，`runs.using` 必须是 `node24` 或 `composite`。
  - 写成 `uses: owner/repo@<40 位 sha> # vX.Y.Z`。
  - 涉及的 action：`actions/checkout`、`actions/setup-dotnet`、`actions/setup-node`、`actions/upload-artifact`、`NuGet/login`。
  - 某个 action 没有 node24 版本时，固定到最新版本，并在 ledger 中记下来；Task 17 会把它写进 `docs/backlog.md`。
  - 新版本的输入名如果有变化，按 `action.yml` 修改。
- [ ] **Step 4: 运行测试，确认通过，然后提交**
  - Node.js 20 弃用警告是否消失，要等下次 CI 运行（push 需要用户同意），在 ledger 中记为待验证。

```bash
git commit -am "ci: pin actions to commit SHAs on Node.js 24 versions"
```

### Task 4: 参考文件名统一为小写

本期设计 §4.1 第 3 项。

**Files:**
- Modify: `src/Fdg.Generator/Emit/RuleText.cs`（`FileStem`）、`src/Fdg.Generator/Emit/SkillReferenceEmitter.cs`、`src/Fdg.Generator/Emit/GeneratedFiles.cs`（`Write`）
- Modify: `plugins/framework-design-guidelines/skills/fdg-write/SKILL.md`、`skills/fdg-review/SKILL.md`、`agents/fdg-reviewer.md`
- Test: `tests/Fdg.Generator.Tests/EmitterTests.cs`、`SkillLintTests.cs`

**Interfaces:**
- Produces：
  - 参考文件的文件名一律小写。`RuleText.FileStem` 返回小写；`meta.yaml` 的 `references` 中，`file` 含大写字母时，`generate` 和 `check` 报错：`reference file name '<x>' must be lowercase`。
  - `rules/appA-coding-style.yaml` 不改名。

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Reference_stems_are_lowercase()                   // EmitterTests：章节文件 appA-coding-style.yaml 生成 references/appa-coding-style.md
[Fact] public void Uppercase_reference_split_name_is_an_error()      // EmitterTests：references 中的 file 为 "ch09-Patterns" 时，GenerationException 的消息含 "must be lowercase"
[Fact] public void Write_fixes_the_case_of_an_existing_file()        // EmitterTests：已有 X.md，写入 x.md 后，Directory.GetFiles 返回的名字是 "x.md"（逐字节比较）
[Fact] public void References_are_linked_with_exact_case()           // SkillLintTests：skill 和 agent 中的每个 references/ 链接，在目录列表中都有大小写完全一致的文件
[Fact] public void Reference_file_names_are_lowercase()              // SkillLintTests
```

- [ ] **Step 2: 运行，确认失败；实现**
  - `GeneratedFiles.Write` 处理只有大小写不同的已有文件：先删除旧文件，再写入新文件。
- [ ] **Step 3: 重命名参考文件**
  - 用 `git mv` 重命名 `references/appA-coding-style.md` 和 `references/examples/appA-coding-style.md`。只改大小写的重命名，在 `core.ignorecase=true` 下必须用 `git mv`。
  - 然后运行 `generate`，确认输出 `0 written`。
  - 更新三个 skill 或 agent 文件中的链接和路由表。
- [ ] **Step 4: 验证**
  - `git ls-files plugins/framework-design-guidelines/skills/fdg-write/references | grep '[A-Z]'` 没有输出；
  - 通用验证全部通过。
- [ ] **Step 5: 提交**

```bash
git commit -am "fix(gen): use lowercase reference file names"
```

### Task 5: 生成器健壮性

本期设计 §4.5 全部，以及 §4.2 第 6 项（globalconfig 键冲突，与生成器改动放在一起）。

**Files:**
- Modify: `src/Fdg.Generator/Commands/CheckCommand.cs`、`src/Fdg.Generator/Sdk/SdkDiagnosticCatalog.cs`
- Modify: `src/Fdg.Generator/Rules/RuleLoader.cs`、`RuleYaml.cs`
- Modify: `rules/rules.schema.json`、`src/Fdg.Generator/Rules/RuleMapping.cs`（`AnalyzerInfo`）、`src/Fdg.Generator/Emit/DescriptorEmitter.cs`
- Modify: `rules/ch05-members.yaml`（FDG5077）、`rules/ch06-extensibility.yaml`（FDG6019）的 `analyzer` 映射（文件名以实际为准）
- Modify: `src/FrameworkDesignGuidelines.Analyzers/FrameworkDesignGuidelines.Analyzers.csproj`、`Directory.Packages.props`
- Modify: `src/Fdg.Generator/Emit/GlobalConfigEmitter.cs`
- Test: `tests/Fdg.Generator.Tests/CheckCommandTests.cs`、`GeneratorCliTests.cs`、`EmitterTests.cs`、`RuleYamlTests.cs`

**Interfaces:**
- Produces：
  - `record AnalyzerInfo(string Message, string Category, bool CompilationEnd = false)`，对应 YAML 中可选的 `analyzer.compilationEnd: true`。
  - YAML 错误的消息格式：`<文件路径>(<行>,<列>): <原消息>`，行号和列号取自 `YamlException.Start`。

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Check_resolves_the_sdk_from_the_current_directory()
// --rules 指向仓库外的临时副本；SDK 定位收到的目录是当前目录，而不是 --rules 的父目录（为此给 CheckCommand 注入定位函数）
[Fact] public void Unrecognized_sdk_layout_names_the_expected_path()
// --sdk 指向空目录：一行错误，含 Path.Combine(<sdk>, "DotnetTools", "dotnet-format")
[Theory] [InlineData("check")] [InlineData("generate")]
public void Malformed_yaml_names_file_line_and_column(string command)
// 规则文件内容为 "- id: FDG3001\n  text: [unclosed"：stderr 含文件名，并匹配 @"\(\d+,\d+\): "
[Fact] public void Compilation_end_rule_descriptor_has_the_custom_tag()
// FdgDescriptors.g.cs 中，FDG6019 的描述符含 "customTags: new[] { WellKnownDiagnosticTags.CompilationEnd }"，其他描述符不含
[Fact] public void Conflicting_globalconfig_values_fail_with_both_rule_ids()
// 两条规则把同一个键写成不同的值：GenerationException 的消息含这个键和两条规则的 id；值相同时不报错
```

- [ ] **Step 2: 运行，确认失败；实现**
  - `check` 改为从当前目录定位 SDK：`SdkLocator.FindSdkDirectory(Directory.GetCurrentDirectory())`。
  - 布局不符时，错误消息写明期望的路径和 `--sdk` 选项。
  - YAML 错误由 `RuleLoader` 和 `LoadMeta` 包装，再由 `ToolErrors` 输出。
  - `GlobalConfigEmitter.AppendNew` 遇到同键不同值时抛出 `GenerationException`。
- [ ] **Step 3: 为 FDG5077、FDG6019 写入 `compilationEnd: true`，然后运行 `generate`**
- [ ] **Step 4: RS1037 设为 error**
  - 分析器项目和 CodeFix 项目引用 `Microsoft.CodeAnalysis.Analyzers`，版本取包含 RS1037 的版本，记入 `Directory.Packages.props`，并设置 `PrivateAssets="all"`。
  - 在项目的 globalconfig 或 `.editorconfig` 中设置 `dotnet_diagnostic.RS1037.severity = error`。
  - 引入后报出的其他 RS 诊断，能修的就修；不修的在同一配置中逐条关闭，每条附一行注释写明理由。例如 RS2008（release tracking）：诊断编号由生成器管理。
  - 手工验证：临时删去 FDG6019 的 `compilationEnd`，运行 `generate` 再构建，确认出现 RS1037 错误；然后还原。
- [ ] **Step 5: 运行通用验证，然后提交**

```bash
git commit -am "fix(gen): resolve the SDK from the current directory, report YAML positions, tag compilation-end descriptors"
```

### Task 6: 仓库卫生，以及 FDGA003 和 FDGA023 的 CodeFix 边界情况

本期设计 §4.6。

**Files:**
- Modify: `.editorconfig`
- Modify: `src/FrameworkDesignGuidelines.CodeFixes/Layout/SingleLineCatchFinallyCodeFix.cs`、`LongListWrappingCodeFix.cs`、`TokenEdits.cs`
- Modify: `src/FrameworkDesignGuidelines.Analyzers/Layout/LineLength.cs`
- Test: `tests/FrameworkDesignGuidelines.Analyzers.Tests/Layout/SingleLineCatchFinallyTests.cs`、`LongListWrappingTests.cs`、`LineLengthTests.cs`

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Comment_in_an_empty_catch_moves_into_the_block()        // catch { /* ignore */ } → 注释在块内单独一行
[Fact] public void Comment_before_a_list_item_stays_before_that_item()     // f(a, /* c */ b) 换行后，"/* c */ b" 在同一行
[Fact] public void Switch_section_with_two_blocks_is_fixed_in_one_pass()   // 一次 FixAll 后没有剩余的 FDGA003
[Theory] [InlineData(4)] [InlineData(8)]
public void Tab_counts_as_tab_width_columns(int tabWidth)                 // .editorconfig 设置 tab_width；未设置时按 4 计
```

- [ ] **Step 2: 运行，确认失败；实现；再运行，确认通过**

运行：`dotnet test tests/FrameworkDesignGuidelines.Analyzers.Tests --filter "FullyQualifiedName~Layout"`

- [ ] **Step 3: 修改仓库 `.editorconfig`**：私有字段命名规则对 `const` 加例外，做法与包内 globalconfig 一致。确认 `dotnet build -c Release` 没有新的警告。
- [ ] **Step 4: 在噪音样本上复跑**
  - 先 `dotnet pack` 改动前（HEAD~）和改动后的包，再分别运行：

```powershell
pwsh -NoProfile -File eng/noise/Measure-Noise.ps1 -Samples eng/noise/samples.json,private-sources/noise/pilot.json -Package <包> -WorkRoot $env:TEMP/fdg-noise -Out $env:TEMP/fdg-noise/p3-t6-<before|after>.json -Diagnostics FDGA003,FDGA023
```

  - 新增和消失的位置各抽查最多 20 个。新增位置中的误报不得超过 1/20。
  - 结果写进 `docs/noise-samples.md` 的 FDGA023 一行或注释，内部样本只写计数。
- [ ] **Step 5: 运行通用验证，然后提交**

```bash
git commit -am "fix(analyzers): FDGA003 and FDGA023 code fix edge cases, tab width in line length"
```

### Task 7: ExistingApi 比较基类列表

本期设计 §4.2 第 1 项。

**Files:**
- Modify: `src/Fdg.Cli/Hooks/DeclarationLocator.cs`、`ExistingApi.cs`、`HookRunner.cs`（`ToIssuesAsync` 和 `IsExistingApiAsync`）
- Test: `tests/Fdg.Cli.Tests/Hooks/ExistingApiTests.cs`、`StopHookTests.cs`

**Interfaces:**
- Produces：

```csharp
/// <summary>A baseline file's declarations, and the base list of each type declaration in it.</summary>
public sealed record BaselineDeclarations(IReadOnlySet<DeclarationKey> Keys, IReadOnlyDictionary<DeclarationKey, string> BaseLists);

public static BaselineDeclarations? DeclarationLocator.Baseline(SyntaxTree tree);   // 有语法错误时为 null
public static bool ExistingApi.IsExisting(SyntaxTree current, BaselineDeclarations? baseline, int line, int column, bool compareBaseList);
public static bool ExistingApi.ComparesBaseList(string ruleId);  // IDE1006、CA1700–CA1727、FDG3xxx（命名类）为 false，其余为 true
```

- 基类列表的规范形式：把每个基类型的文本去掉空白，按 ordinal 排序，再用 `,` 连接。没有基类列表时为空串。
- 位置所在的声明是类型（`Part` 为空、种类是类型种类），并且 `compareBaseList` 为 true 时，当前基类列表与基线不同就判为新代码。命名类诊断不比较基类列表：改名会破坏调用方（反目标 1）。
- `HookRunner` 原来按文件缓存 `baselineKeys`，现在改为缓存 `BaselineDeclarations`。原有的字符串重载和 `SyntaxTree` 重载保留，`compareBaseList` 默认为 false。

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void New_IDisposable_on_an_existing_type_makes_CA1063_new()                           // 基线 class Foo { }；当前 class Foo : IDisposable {...}；CA1063 位置在 Foo 上 → false
[Fact] public void Unchanged_base_list_keeps_CA1063_existing()                                       // 基线和当前都是 : IDisposable → true
[Fact] public void Reordered_base_list_is_unchanged()                                                // : IA, IB → : IB, IA → true
[Fact] public void Naming_diagnostic_on_an_existing_type_with_a_new_base_stays_existing()           // CA1710：基线 class Foo；当前 class Foo : Exception → true
[Fact] public void Member_diagnostic_ignores_the_base_list()                                         // 位置在方法上，基类列表有变化 → 仍按原规则判断
[Fact] public void Stop_blocks_CA1063_after_IDisposable_is_added_to_an_existing_type()              // StopHookTests：端到端，打回
```

- [ ] **Step 2: 运行，确认失败；实现；再运行，确认通过**

运行：`dotnet test tests/Fdg.Cli.Tests --filter "FullyQualifiedName~ExistingApi|FullyQualifiedName~StopHook"`

- [ ] **Step 3: 运行通用验证，然后提交**

```bash
git commit -am "fix(hooks): a type-level diagnostic is new when the turn changed the type's base list"
```

### Task 8: 可见的 const 同时属于两种种类

本期设计 §4.2 第 2 项（Ruling 54）。

**Files:**
- Modify: `src/Fdg.Cli/Hooks/DeclarationLocator.cs`（`NamingKindAt` 改为 `NamingKindsAt`，以及 `FieldKind`）、`StopReport.cs`（`RulesFor`）、`HookRunner.cs`（`NamingOf`）
- Test: `tests/Fdg.Cli.Tests/Hooks/DeclarationLocatorTests.cs`、`NamingRulesTests.cs`

**Interfaces:**
- Produces：

```csharp
public static IReadOnlyList<NamingSymbolKind>? DeclarationLocator.NamingKindsAt(SyntaxTree tree, int line, int column);  // 字符串重载同样改
// 可见的 const 字段 → [ConstField, VisibleField]；私有 const → [ConstField]；其余为单个种类；取不到时为 null
public static IReadOnlyList<RuleEntry> StopReport.RulesFor(..., (IReadOnlyList<NamingSymbolKind> Kinds, NamingFailure Failure)? naming);
// 规则的 Symbols 含任一种类就算匹配；排序和 MaxRulesPerIssue = 2 不变
```

- 按标签越窄越靠前的排序，加上每条问题最多 2 条规则，写错大小写的 `public const` 引用 FDG3077 和 FDGA053，FDG3001 被上限截掉。本期设计写的是“能引用 FDG3077 和 FDG3001”，本质要求是“引用可见字段的规则”。保留上限，按此实现，在 ledger 中注明。

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Visible_const_has_both_kinds()                  // public const int minValue = 0; → [ConstField, VisibleField]；protected 同样
[Fact] public void Private_const_is_only_const_field()             // private const int x = 0; → [ConstField]
[Fact] public void Miscased_public_const_quotes_FDG3077()          // NamingRulesTests，使用真实的 rules.json：结果含 FDG3077
[Fact] public void Miscased_private_const_quotes_only_FDGA053()    // 结果的 id 列表 == ["FDGA053"]
```

- [ ] **Step 2: 运行，确认失败；实现；再运行，确认通过**
- [ ] **Step 3: 运行通用验证，然后提交**

```bash
git commit -am "fix(hooks): classify a visible const as both a const and a visible field"
```

### Task 9: CA1710 的 `exclude_indirect_base_types`

本期设计 §4.2 第 3 项（Ruling 48）。这个任务会改规则 notes，控制者要另派交叉核对。

**Files:**
- Modify（视结果而定）：CA1710 相关规则所在的 `rules/*.yaml`（FDG3048–3050、3053、3056–3059、3074、3076、7057、8005、8038、8039），以及 `docs/noise-samples.md`

- [ ] **Step 1: 打两个包**
  - 一个是当前 HEAD；
  - 另一个临时在 CA1710 规则的映射选项中写入 `dotnet_code_quality.CA1710.exclude_indirect_base_types = false`，先运行 `generate` 再打包。
  - 两个包用不同的预发布版本号。
- [ ] **Step 2: 两个包分别运行 `Measure-Noise.ps1 -Diagnostics CA1710`**，样本为 `eng/noise/samples.json,private-sources/noise/pilot.json`。
- [ ] **Step 3: 对比两份 `locations/*.tsv`**，按 `docs/noise-samples.md` 的抽查方法判定新增的报告。
- [ ] **Step 4: 按门槛决定**
  - **不超过 1/20：**
    - 保留选项；
    - FDG3049、3053、3056、3059、3076 的 coverage 改为 `full`；
    - 修改这些规则的 notes（第二期 Ruling 48 的说明），写明数据；
    - 在 `docs/noise-samples.md` 增加一行。
  - **超过 1/20：**
    - 撤销临时改动；
    - 在相关规则的 notes 和 `docs/noise-samples.md` 中写明数据和结论。
- [ ] **Step 5: 验证并提交**
  - 运行通用验证，加上 `check --book`。

```bash
git commit -am "rules: decide CA1710 exclude_indirect_base_types from a noise run"
```

### Task 10: IDE0280、IDE0250、IDE0251 需要 `.editorconfig`

本期设计 §4.2 第 4 项。会改 FDGA048 的 notes，控制者要另派交叉核对。

**Files:**
- Modify: `plugins/framework-design-guidelines/skills/fdg-setup/SKILL.md`
- Modify: FDGA048、FDG4028、FDG4029 所在的 `rules/*.yaml`（notes）
- Modify: `docs/install.md`
- Test: `tests/Fdg.Generator.Tests/SkillLintTests.cs`

- [ ] **Step 1: probe**
  - 在临时项目中，用 SDK 10.0.402 依次试下列做法，看能否让 `dotnet format --diagnostics IDE0280 --severity warn` 应用修复：
    - (a) 只用包内 globalconfig 中的 `dotnet_diagnostic` 条目（预期不行，复核 backlog 的结论）；
    - (b) 包通过 `EditorConfigFiles` 或 `GlobalAnalyzerConfigFiles` 加入一个 `.editorconfig` 格式的文件；
    - (c) 仓库根 `.editorconfig` 的 `[*.cs]` 节中写入严重级别。
  - 把结果写进 ledger。
  - (b) 可行时改用 (b)：在包里实现，并配一个集成测试（`Fix_applies_IDE0280_without_a_repository_editorconfig`），跳过 Step 2。
- [ ] **Step 2: (b) 不可行时，修改 fdg-setup 的确认清单**
  - fdg-setup 提议在根 `.editorconfig` 的 `[*.cs]` 节写入这三条诊断的严重级别，值取规则数据中的 profile 值，用户确认后才写入。
  - 某条诊断在仓库用到的各个 profile 中取值不同时（例如 IDE0251 在 test 中为 `none`），`[*.cs]` 节写多数项目的值。其余项目在根 `.editorconfig` 中各加一个 `[<项目目录>/**.cs]` 节写自己的值。
  - 已有同名条目时不覆盖，而是列出差异，请用户决定。
- [ ] **Step 3: 写测试并通过**

```csharp
[Fact] public void Fdg_setup_offers_editorconfig_severities_for_option_less_ide_fixes()   // SKILL.md 提到 IDE0280、IDE0250、IDE0251、.editorconfig，以及用户确认
```

- [ ] **Step 4: 修改 notes 和 install.md**
  - 修改 FDGA048、FDG4028、FDG4029 的 notes 和 `docs/install.md`，说明这三条修复需要 `.editorconfig`，由 fdg-setup 提议写入。
  - 运行通用验证，加上 `check --book`，然后提交。

```bash
git commit -am "feat(setup): offer .editorconfig severities so dotnet format applies IDE0280, IDE0250 and IDE0251"
```

### Task 11: 参考文件列出部分检查的规则

本期设计 §4.2 第 5 项（Ruling 22）。依赖 Task 9 和 Task 10 的 coverage 结论，所以排在它们之后。

实测：只把 sdk 和 fdg 的 `partial` 规则加进参考文件，不做别的改动，以下四个 examples 文件会超过或逼近上限。第 9.2 节一节就有 35 KB，必须按小节拆分。

| examples 文件 | 当前大小 | 加入后 |
|---|---:|---:|
| `ch09-patterns-aggregate-async.md` | 24.9 KB | 45.1 KB |
| `ch03-naming.md` | 20.4 KB | 41.1 KB |
| `ch07-exceptions.md` | 28.7 KB | 38.9 KB |
| `ch04-types-4-5-on.md` | 17.9 KB | 32.3 KB |

**Files:**
- Modify: `src/Fdg.Generator/Emit/SkillReferenceEmitter.cs`（`IsListed`、`Index`、`Line`）
- Modify: `rules/meta.yaml`（`references` 拆分）
- Modify: `plugins/framework-design-guidelines/skills/fdg-write/SKILL.md`、`skills/fdg-review/SKILL.md`、`agents/fdg-reviewer.md`
- Test: `tests/Fdg.Generator.Tests/EmitterTests.cs`、`SkillLintTests.cs`

**Interfaces:**
- Produces：
  - `public const string PartlyCheckedMarker = "(partly checked)";`
  - `IsListed(rule)` 新增一种情况：`kind is Sdk or Fdg && Coverage == CoverageLevel.Partial`。
  - 所有列出的 sdk 和 fdg partial 规则，行内紧跟在规则文本之后写这个标记，排在 `(library only)` 等标记之前。CONSIDER 规则也一样。
- 拆分。新文件名全部小写，所有规则（包括没列出的）都必须恰好落入一个拆分：

| 章 | 新的拆分（`file`：节） |
|---|---|
| 3 | `ch03-naming-3-1-to-3-2`：3.1、3.2；`ch03-naming-3-3-on`：3.3–3.8 |
| 4 | `ch04-types-4-1-to-4-4` 不变；`ch04-types-4-5-to-4-7`：4.5–4.7；`ch04-types-4-8-on`：4.8–4.11 |
| 7 | `ch07-exceptions-7-1-to-7-2`：7.1、7.2；`ch07-exceptions-7-3-on`：7.3–7.5 |
| 9 | `ch09-patterns-aggregate-async`：9.1、9.2.1–9.2.3；`ch09-patterns-async-consistency`：9.2.4 起 9.2 的其余小节；`ch09-patterns-dependency-properties`：9.3；其余 ch09 拆分不变 |

- 第 9.2 节本身如果有规则，用 `=9.2` 归入 `ch09-patterns-aggregate-async`。
- 拆分后任何文件仍超过 32768 字节时，按同样的命名方式继续拆分，并在 ledger 中说明。

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Partial_sdk_rule_is_listed_with_the_partly_checked_marker()   // EmitterTests：DO、sdk、partial → 行内含 "(partly checked)"，并有 examples 段
[Fact] public void Full_sdk_rule_is_not_listed()
[Fact] public void Consider_full_sdk_rule_is_listed_without_the_marker()
[Fact] public void Skills_explain_the_partly_checked_marker()                    // SkillLintTests：fdg-write、fdg-review、fdg-reviewer 都提到 "(partly checked)"
```

- [ ] **Step 2: 运行，确认失败；实现 emitter 和 meta.yaml 拆分；运行 `generate`**
- [ ] **Step 3: 修改三个 skill 或 agent 文件**
  - 路由表改为新的文件名。fdg-reviewer 第 20 行用来定位目录的 glob 锚点，改成一个不会再拆分的文件，例如 `ch02-fundamentals.md`。
  - 说明标记的含义：工具只检查这条规则的一部分。写代码时遵守整条规则；评审时只报告工具查不到的那部分，同一行已有诊断报告的内容不重复报告。
  - 索引文件的开头说明也改为：列出的有判断类规则、只给建议的 CONSIDER 规则，以及标为 “(partly checked)” 的部分检查规则。
- [ ] **Step 4: 验证并提交**
  - 运行通用验证。所有参考文件不超过 32768 字节，SKILL.md 不超过 500 行。

```bash
git commit -am "feat(skills): list partly checked rules in the references, marked (partly checked)"
```

### Task 12: 一轮内的自动修复记录

本期设计 §4.2 第 7 项（试点问题 8、9）。

**Files:**
- Modify: `src/Fdg.Cli/Hooks/Autofix.cs`、`StateStore.cs`、`HookRunner.cs`（`RecordAutofixAsync`）
- Test: `tests/Fdg.Cli.Tests/Hooks/TurnStartSummaryTests.cs`、`StopHookTests.cs`

**Interfaces:**
- Produces：

```csharp
public sealed record AutofixFile(string Path, IReadOnlyList<LineRange> Ranges, int LineCount = 0);
public const int AutofixRecord.MaxListedRanges = 10;
public static AutofixRecord AutofixRecord.Merge(AutofixRecord? previous, AutofixRecord current);
// Turn 相同时按路径合并，范围取并集并合并相邻的范围；Turn 不同时返回 current
```

- `Summary()`：某个文件的范围数超过 `MaxListedRanges`，或者 `LineCount > 0` 且覆盖的行数 × 2 > `LineCount` 时，写成 `path (reformatted throughout)`。
- 旧的 `autofix.json` 没有 `LineCount`，读出来为 0，按原样列出范围。

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Second_stop_in_a_turn_merges_the_autofix_record()        // 两次 Stop 各修复不同的文件 → 下一轮开头的摘要两者都列出
[Fact] public void Stop_in_a_new_turn_replaces_the_record()
[Fact] public void Many_ranges_are_summarized_as_reformatted_throughout()   // 11 个范围
[Fact] public void Half_the_file_is_summarized_as_reformatted_throughout()  // LineCount = 10，覆盖 6 行
```

- [ ] **Step 2: 运行，确认失败；实现；再运行，确认通过；运行通用验证，然后提交**

```bash
git commit -am "fix(hooks): merge auto-fix records within a turn and summarize large reformatting"
```

### Task 13: 包装脚本和 Codex 的引号（本期唯一一次 `hooks.codex.json` 变更）

本期设计 §4.4 第 1、2 项。

**Files:**
- Modify: `plugins/framework-design-guidelines/hooks/fdg-hook.sh`、`fdg-hook.ps1`、`hooks.codex.json`
- Modify: `docs/hosts.md`（“Shell that runs hook commands” 一节）
- Test: `tests/Fdg.Cli.Tests/HookShellTests.cs`

**Interfaces:**
- Produces：
  - `hooks.codex.json` 中每个事件的 `commandWindows` 改为：

    ```
    pwsh -NoProfile -ExecutionPolicy Bypass -Command "& (Join-Path ([Environment]::GetEnvironmentVariable('PLUGIN_ROOT')) 'hooks/fdg-hook.ps1') <event>"
    ```

    这条命令不含 `$` 和反引号，PowerShell 和 cmd 作会话 shell 都能原样传给 pwsh。插件路径来自环境变量，展开后不会再被解析。Codex 为插件 hook 设置 `PLUGIN_ROOT`，依据见 `docs/hosts.md`。
  - `command`（bash）不变；`hooks.json`（Claude）不变。
  - 如果测试证明这种形式在某种 shell 中不能转发 stdin 或 stdout，实现者可以换用等价的形式。条件是：仍然不把插件路径写进会被会话 shell 解析的字符串，并且在 ledger 中说明。
- 包装脚本（sh 和 ps1 的行为相同）：
  1. 工具的查找顺序：
     - (a) 从当前目录向上找 `.config/dotnet-tools.json` 或 `dotnet-tools.json`，其中列出了 `dotnet-fdg` 时，运行 `dotnet tool run dotnet-fdg hook <event>`；
     - (b) PATH 上的 `dotnet-fdg`；
     - (c) `$DOTNET_CLI_HOME/.dotnet/tools`；
     - (d) Windows 上的 `$USERPROFILE/.dotnet/tools`（sh 脚本在 Git Bash 下也用它），其他系统上的 `$HOME/.dotnet/tools`。
  2. 不再运行 `--version` 探测。直接运行工具一次，stdin 原样传入。
  3. 工具以 0 退出时，原样输出它的 stdout。以非零码退出且 stdout 为空时，输出 `{"systemMessage":"FDG: dotnet-fdg failed: <stderr 第一行>"}`，退出码为 0。以非零码退出但有 stdout 时，原样输出 stdout，并返回原退出码。
  4. stderr 先转成合法的 UTF-8：sh 有 `iconv` 时用 `iconv -f UTF-8 -t UTF-8 -c`，没有时删除 0x80–0xFF 字节；ps1 用 UTF-8 解码，无效字节替换为 U+FFFD。然后转义成 JSON 字符串。
  5. 找不到工具时，仍然输出现有的安装提示。

- [ ] **Step 1: 写失败的测试**

```csharp
[BashFact] public void Wrapper_runs_the_tool_once_without_a_version_probe()              // 假工具记录参数：只有一次调用，参数为 "hook session-start"
[PwshFact] public void Pwsh_wrapper_runs_the_tool_once_without_a_version_probe()
[BashFact] public void Wrapper_reports_a_tool_failure_without_output()                   // 假工具向 stderr 写 "Unrecognized command 'hook'"，exit 1 → systemMessage 含这一行
[PwshFact] public void Pwsh_wrapper_reports_a_tool_failure_without_output()
[BashFact] public void Wrapper_reports_non_utf8_stderr_as_valid_json()                   // stderr 含 0xB4 0xED：stdout 可被严格的 UTF-8 JSON 解析
[BashFact] public void Wrapper_uses_the_local_tool_manifest()                            // 当前目录有 .config/dotnet-tools.json 列出 dotnet-fdg；假 dotnet 收到 "tool run dotnet-fdg hook turn-stop"
[PwshFact] public void Pwsh_wrapper_uses_the_local_tool_manifest()
[BashFact] public void Wrapper_finds_the_tool_under_DOTNET_CLI_HOME()
[PwshFact] public void Codex_windows_command_survives_special_characters()
// 插件根目录为 "a b$c`d'e 中文"，设置 PLUGIN_ROOT；分别以 pwsh -NoProfile -Command <commandWindows> 和 cmd /d /s /c "<commandWindows>" 运行；
// 两种 shell 下假工具都收到 stdin payload，stdout 原样返回
[Fact] public void Hooks_run_the_wrapper_for_each_event_with_its_timeout()               // 改写现有测试中 commandWindows 的期望值
```

- [ ] **Step 2: 运行，确认失败；实现；再运行，确认通过**

运行：`dotnet test tests/Fdg.Cli.Tests --filter "FullyQualifiedName~HookShellTests"`

- [ ] **Step 3: 修改 `docs/hosts.md`**：写明新的 `commandWindows` 形式和理由；写明从 0.2.0 升级时 Codex 需要重新批准一次 hook。
- [ ] **Step 4: 运行通用验证（包括 `claude plugin validate --strict`），然后提交**

```bash
git commit -am "fix(hooks): one process per hook, local tool manifests, safe plugin root quoting on Codex for Windows"
```

### Task 14: 接入判断、marker 和会话检查的边界情况

本期设计 §4.4 第 3 项，以及第 4 项中的 marker、LangVersion、Inspect 和 `FindOwningProjects`。

**Files:**
- Modify: `src/Fdg.Cli/Hooks/Onboarding.cs`、`src/Fdg.Cli/Projects/ProjectInfoProvider.cs`、`src/Fdg.Cli/Projects/ProjectFiles.cs`、`src/Fdg.Cli/Hooks/SessionContext.cs`、`src/Fdg.Cli/Hooks/HookRunner.cs`（`SessionStartAsync`）
- Test: `tests/Fdg.Cli.Tests/Hooks/OnboardingTests.cs`、`ProjectInfoProviderTests.cs`、`SessionContextTests.cs`、`PathResolverTests.cs` 或新的 `ProjectFilesTests.cs`

**Interfaces:**
- Produces：
  - `ProjectFiles.FindOwningProjects(string directory, string? stopAt = null)`：`stopAt` 默认为用户主目录。向上查找时，遇到含 `.csproj` 的目录、`.git`，或者到达 `stopAt`（包含它本身，不再往上），就停下。起点不在 `stopAt` 之下时，行为与现在相同。
  - `SessionContext.Inspect(string repoRoot, CancellationToken cancellationToken)`：SessionStart 用一个 10 秒的超时令牌调用它。超时时返回已收集的部分，并附一行 `FDG: session inspection timed out; some checks were skipped.`
  - marker 的选择：分别读 `obj/fdg` 和 `artifacts` 下属于本项目的 marker（判断归属的方法与 `SessionContext.CurrentMarkers` 相同，两处共用一个方法），取写入时间较新的那个。
  - 符号形式的 LangVersion（`latest`、`latestMajor`、`preview`、`default`）：用已有的 `ILanguageVersionSource.Resolve` 解析为实际的版本号。解析失败时显示 `C# <原值> (unresolved)`。

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Assets_listing_the_package_onboard_a_project_without_a_declared_reference()   // OnboardingTests
[Fact] public void Assets_without_the_package_still_veto_a_declared_reference()                  // 保持现有行为
[Fact] public void Provider_ignores_a_marker_of_another_project()                                // ProjectInfoProviderTests
[Fact] public void Newer_artifacts_marker_wins_over_a_stale_obj_marker()
[Fact] public void Symbolic_lang_version_is_shown_resolved()                                     // SessionContextTests："latest" 解析为 "14.0" → 显示 "C# 14.0"
[Fact] public void Unresolved_symbolic_lang_version_says_so()                                     // → "C# latest (unresolved)"
[Fact] public void Inspect_stops_at_the_timeout_with_a_note()                                    // 假的进程运行器一直阻塞，直到令牌被取消
[Fact] public void Owning_projects_search_stops_at_the_boundary()                                // stopAt 之上有 .csproj → 不返回它
```

- [ ] **Step 2: 运行，确认失败；实现；再运行，确认通过；运行通用验证，然后提交**

```bash
git commit -am "fix(cli): onboard through project.assets.json, pick the right marker, bound session inspection"
```

### Task 15: 构建和格式化调用的边界情况

本期设计 §4.4 第 4 项中剩下的条目。

**Files:**
- Create: `src/Fdg.Cli/IO/AtomicFile.cs`
- Modify: `src/Fdg.Cli/Tooling/DotnetBuildRunner.cs`、`DotnetFormatRunner.cs`
- Modify: `src/Fdg.Cli/Hooks/StateStore.cs`、`HookRunner.cs`（`IsForeign`，以及 AI 版本副本的写入）
- Modify: `src/Fdg.Cli/Commands/FixCommand.cs`
- Modify: 包内写 `ErrorLog` 的 targets 文件（在 `src/FrameworkDesignGuidelines.Package/` 下，以实际为准）
- Test: `tests/Fdg.Cli.Tests/DotnetBuildRunnerTests.cs`、`DotnetFormatRunnerTests.cs`、`FixCommandTests.cs`、`Hooks/StopHookTests.cs`、新的 `AtomicFileTests.cs`；`tests/Fdg.IntegrationTests/HookEndToEndTests.cs`

**Interfaces:**
- Produces：

```csharp
public static class AtomicFile
{
    /// <summary>Writes to a temporary file next to <paramref name="path"/>, then moves it over <paramref name="path"/>.</summary>
    public static void Write(string path, Action<Stream> write);
    public static void WriteAllText(string path, string contents);
    public static void Copy(string source, string destination);
}
```

- `-p:` 属性值中的 `%`、`;`、`,` 按 MSBuild 规则转义为 `%25`、`%3B`、`%2C`。`ErrorLog` 中的路径要在逗号存在时仍能被正确解析，具体做法由实现者决定，以集成测试为准。
- `DotnetFormatRunner` 的命令行总长超过 30000 个字符时，`--include` 改用响应文件。先 probe `dotnet format @file.rsp` 是否可用；不可用时改为分批调用，每批的命令行不超过 30000 个字符。
- `IsForeign`：项目文件中静态的 `<Compile Include=…>` 项（不含通配符）所指的文件，也算本项目的文件。
- `fix`：有语法错误的文件不显示 `fixed`，而是显示 `skipped <path>: it has syntax errors (first at line N); dotnet format does not change it`。

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Build_properties_escape_semicolons_and_commas()           // DotnetBuildRunnerTests：sarif 目录为 "a;b,c" → 参数中为 "a%3Bb%2Cc"
[Fact] public void Long_include_list_does_not_exceed_the_command_line_limit() // DotnetFormatRunnerTests：2000 个长路径 → 每次调用的总长 ≤ 30000
[Fact] public void Failed_write_leaves_the_previous_file()                   // AtomicFileTests：write 中途抛异常 → 原内容不变，不留临时文件
[Fact] public void Linked_compile_results_do_not_force_a_rebuild()           // StopHookTests：结果全在链接的 Compile 项中 → 只构建一次
[Fact] public void Fix_reports_files_with_syntax_errors_as_skipped()         // FixCommandTests
[Fact] public void Hook_works_in_a_repository_path_with_semicolon_and_comma() // HookEndToEndTests：样本目录名含 ";" 和 ","，能读到 SARIF
```

- [ ] **Step 2: 运行，确认失败；实现**
  - `StateStore.WriteJson`、`ai-lines.json`、`formatted/` 副本，以及 AI 版本副本，都改用 `AtomicFile`。
- [ ] **Step 3: 运行，确认通过；运行通用验证，然后提交**

```bash
git commit -am "fix(cli): escape build properties, atomic state writes, long include lists, linked files, fix on broken files"
```

### Task 16: Stop 提速，以及加包后的构建开销

本期设计 §4.3 第 2–4 步。

**Files:**
- Create: `src/Fdg.Cli/Projects/ProjectReferences.cs`
- Modify: `src/Fdg.Cli/Hooks/HookRunner.cs`、`src/Fdg.Cli/Tooling/FormatPasses.cs`、`DotnetBuildRunner.cs`、`IBuildRunner`
- Modify: `tests/Fdg.Cli.Tests/Hooks/HookFakes.cs`
- Modify: `eng/noise/Measure-Noise.ps1`、`docs/performance.md`
- Test: `tests/Fdg.Cli.Tests/Hooks/StopHookTests.cs`、`DotnetBuildRunnerTests.cs`、新的 `ProjectReferencesTests.cs`

**Interfaces:**
- Produces：

```csharp
Task<ToolResult> IBuildRunner.BuildAsync(string projectPath, string sarifDirectory, bool noIncremental, bool noDependencies, CancellationToken cancellationToken);
// noIncremental 的含义不变（--no-incremental --no-dependencies）；noDependencies 只加 --no-dependencies
public static Task<ToolResult> FormatPasses.RunAsync(IFormatRunner formatter, string projectPath, IReadOnlyList<string>? includeFiles,
    IReadOnlyList<string> diagnostics, Func<string> hashSources, int maxPasses, CancellationToken cancellationToken);
public static IReadOnlySet<string>? ProjectReferences.Closure(string projectPath);
// 静态读取 ProjectReference Include，递归，忽略 Condition；遇到含 "$(" 的 Include 或读不了的文件时返回 null（视为“可能有改动”）
```

- `HookBuildCall` 增加 `bool NoDependencies`。
- 新的 Stop 流程：
  1. 每个项目只格式化一轮（`maxPasses: 1`）。
  2. 构建。项目的引用闭包可以得到（不为 null），并且本轮所有改动和删除的文件（接入过滤之前）都不在闭包中的项目目录下时，用 `noDependencies: true` 构建。
  3. `noDependencies` 的构建失败，或者没有写出 SARIF 时，去掉它重新构建一次。
  4. 构建的 SARIF 中，本项目改动的文件里仍有 `project.Manifest.FixDiagnostics` 中的诊断时，再用 `maxPasses: FormatPasses.MaxPasses - 1` 格式化这个项目，然后重新构建它。用重新构建的结果做后续的筛选和报告。
- 多项目：用 Task 1 的脚本测 `-Projects 2`。串行构建比单项目多出一倍以上时，实现者可以改为一次 MSBuild 调用构建多个项目（例如临时的 traversal 项目）。不改时，在 `docs/performance.md` 中写明数据和理由。
- `FixCommand` 仍然用 `FormatPasses.MaxPasses`。

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Second_format_pass_is_skipped_when_no_fixable_diagnostic_remains()   // 格式化 1 次，构建 1 次
[Fact] public void Second_format_pass_runs_when_a_fixable_diagnostic_remains()
// 第一次构建的 SARIF 在改动文件中有 FDGA003 → 再格式化，再构建一次；第二次构建仍有 FDGA003，且在改动行上 → 打回的原因中含 FDGA003
[Fact] public void Build_uses_no_dependencies_when_no_referenced_project_changed()
[Fact] public void Build_keeps_dependencies_when_a_referenced_project_changed()
[Fact] public void No_dependencies_build_failure_falls_back_to_a_full_build()
[Fact] public void Closure_is_null_for_an_msbuild_property_in_a_reference()             // ProjectReferencesTests
```

- [ ] **Step 2: 运行，确认失败；实现；再运行，确认通过**
- [ ] **Step 3: 测量**
  - 用新代码打包，版本号与基线不同；`dotnet publish` 新的 CLI。
  - 用 Task 1 的脚本，在同一个样本上重新测三组数据。
  - 同一时段用 Task 1 留下的基线产物再测一次单项目热启动，排除机器状态变化的影响。
- [ ] **Step 4: 给 `Measure-Noise.ps1` 加计时模式**
  - 新增 `-Timing` 开关：每个样本先不加包、再加包，各做 3 次干净构建（每次先删除 `bin` 和 `obj`），取中位数。
  - 输出中增加 `timing: { <sample>: { withoutPackageSeconds, withPackageSeconds } }`。
  - 本地样本原地构建时，只能删除脚本自己产生的输出目录，不能删除样本的其他文件。如果做不到，本地样本只做加包的构建，并写明原因。
  - 在 5 个样本上运行。
- [ ] **Step 5: 更新 `docs/performance.md`**
  - 写入优化后的三组数据、与基线的对比，以及是否达到目标（热启动至少低 30%，冷启动低于 30 秒）。
  - 加包前后的构建时间：内部样本只写 internal-a、internal-b 的时间。
  - 达不到目标时，写明原因和数据。最终报告中把常驻进程作为待用户决定的事项提出。
- [ ] **Step 6: 运行通用验证，然后提交**

```bash
git commit -am "perf(hooks): skip the verification format pass unless needed, build without dependencies when safe"
```

### Task 17: 文档

本期设计 §4.1 第 4–6 项、§4.4 第 5 项、§4.7 的验证清单、§4.8。

**Files:**
- Modify: `docs/releasing.md`、`README.md`、`docs/install.md`、`docs/hosts.md`
- Create: `CHANGELOG.md`
- Rename: `docs/phase-2-backlog.md` → `docs/backlog.md`（用 `git mv`）

- [ ] **Step 1: `docs/releasing.md` 新增 “Versioning and compatibility” 一节**，按本期设计 §4.1 第 4 项写。插件与工具的匹配规则按现有的协议检查（`SupportedProtocol`）写明。
- [ ] **Step 2: 写 `CHANGELOG.md`**
  - 标题下是 `## 1.0.0 - Unreleased`，Task 18 发布时填入日期。
  - 一段话概括 0.1.0 到 1.0.0 的变化，再列出本期的主要变化。
  - 单独一小节列出可能让 `TreatWarningsAsErrors` 构建失败的变化，例如 Task 9 的结论。
- [ ] **Step 3: README**
  - 去掉 “Preview” 段落。
  - 加一个前后对比：一段自己写的 C# 代码，Stop 自动修复和报告前后的样子，以及 fdg-review 的一条输出。示例中的报告文本必须来自真实运行：在临时目录中用 Task 16 的 CLI 和包实际跑一次，再贴出输出，不能凭记忆写。
  - 核对安装步骤、覆盖率数字（用 `docs/coverage.md` 或生成的数据）、声明和 NOTICE。
  - 安装或升级一节写明：从 0.2.0 升级时，Codex 需要重新批准一次 hook（本期设计 §4.4 第 2 项）。
- [ ] **Step 4: `docs/install.md`** 加入以下内容：
  - hook 会在仓库中运行 `dotnet build` 和 `dotnet format`，只在可信的仓库中使用；
  - 从 0.2.0 升级时，Codex 需要重新批准一次 hook；
  - 本地工具清单中固定的版本会被 hook 使用；
  - 已知限制中 `UseArtifactsOutput` 的部分改为 Task 14 之后的行为。
- [ ] **Step 5: `docs/hosts.md` 新增 “1.0 verification” 一节**
  - 写成逐步清单，对应本期设计 §4.7 的表格，交互部分合并成用户的一次操作，大约 30 分钟。
  - 清单包括：临时仓库的创建脚本或步骤（一个小类库、一个测试项目、预先埋好的违规），以及每项的预期结果。
  - 附一张待填写的结果表：日期、宿主版本、通过或失败、观察到的行为。
- [ ] **Step 6: 改名 `docs/backlog.md`**
  - 标题改为 “Backlog”。只保留移到 1.0 之后的条目，每条写明理由，例如常驻进程（视 Task 16 的结果而定）、Rider 验证、已知限制、没有 node24 版本的 action、Unix 上插件路径中的 `$`。
  - 更新 `docs/superpowers/` 以外所有指向旧文件名的链接和路径。`docs/superpowers/` 下的设计和计划是历史记录，保留原文。
- [ ] **Step 7: 运行通用验证，然后提交**

```bash
git commit -am "docs: compatibility promise, changelog, 1.0 README, install notes, verification checklist, backlog"
```

### Task 18: 终审与发布

本期设计 §5 第 6–8 步。每个对外动作之前都必须先问用户。

- [ ] **Step 1: 全分支终审**
  - 派一个新的评审者，在最强的模型上审查 `main..phase-3` 的全部改动，对照本期设计 §6 的完成标准逐条检查。
  - 修复发现的问题，再审一轮。
  - 本地运行全部通用验证，再加 `FDG_TEST_SDK=10.0.114` 的集成测试。
- [ ] **Step 2: 问用户是否 push 并开 PR**。得到同意后执行；CI 全部通过后，由用户决定是否合并。
- [ ] **Step 3: 问用户是否发布 1.0.0-rc.1**
  - 得到同意后，按 `docs/releasing.md` 把版本号改为 `1.0.0-rc.1`（Directory.Build.props、marketplace.json 中的两处、两个 plugin.json），推送，打 tag。
  - 确认 nuget.org 上两个包和 GitHub release 都标为 prerelease。
- [ ] **Step 4: 宿主验证**
  - 按 `docs/hosts.md` 的 “1.0 verification” 清单进行。能用无界面模式（`claude -p`、`codex exec`）完成的部分由我来做，其余由用户操作。
  - 结果写进 `docs/hosts.md`。
  - 发现的问题在小分支上修复。如果影响发布的内容，经用户同意后发布 rc.2。
- [ ] **Step 5: 问用户是否发布 1.0.0**
  - 得到同意后，把版本号改为 `1.0.0`，在 `CHANGELOG.md` 中填入日期，推送，打 tag。
  - 确认两个包和 GitHub release 都不是 prerelease，README 中没有 “Preview”。
