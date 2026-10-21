# FDG 纵向切片 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用第 3 章和附录 A 打通整条链路：原书抽取 → YAML 规则 → 生成器 → 分析器包 → `dotnet fdg` CLI → 四个 skill 与 hook → 两端插件，并在试点仓库的本地克隆上跑通验收。

**Architecture:** `rules/*.yaml` 是唯一来源。开发期生成器 `Fdg.Generator` 从 epub 抽取原文骨架，再从 YAML 生成分析器描述符、各 profile 的 globalconfig、`rules.json`、skill 参考文件、规则文档页和覆盖率报告。分析器包把 globalconfig 和 `rules.json` 带进目标项目，并在 `obj/fdg/` 写标记文件。CLI 读取标记文件，驱动 `dotnet format` 和 `dotnet build`，服务于 `fix` 命令和三个 hook 入口。插件只是一层外壳。

**Tech Stack:** .NET SDK 10.0.402（global.json，rollForward latestFeature）；分析器 netstandard2.0 + Microsoft.CodeAnalysis.CSharp 4.8.0；CLI net8.0（RollForward Major）；生成器 net10.0 + AngleSharp + YamlDotNet + JsonSchema.Net；测试 xUnit 2.9 + Microsoft.CodeAnalysis.CSharp.CodeFix.Testing 1.1.2。

**Spec:** `docs/superpowers/specs/2026-10-21-framework-design-guidelines-design.md`（以下简称"设计文档"，§ 编号指它）

## Global Constraints

- 只修改工作区：任何工具、命令、实施步骤都不替用户提交，也不创建分支（试点仓库尤其如此，见 Task 12）。
- 原书 epub 与解析结果只放在被 git 忽略的 `private-sources/`；读取它们的任何 Python 都用 `python -I`，不执行其中任何内容。
- 规则原文、解释段落、示例代码一律取原书原文，由抽取器确定性写入，AI 不得改写这些字段。
- 规则编号格式（我的决定，满足 §6 要求）：`FDG` + 章号标记 + 三位序号，按书中出现顺序编号。第 2–9 章用数字（第 3 章为 `FDG3001`…`FDG3087`），附录用字母（附录 A 为 `FDGA001`…`FDGA071`，附录 D 为 `FDGD001`…），工具自身的诊断用 `FDG0001` 起。编号同时用作诊断编号，永不复用。
- MSBuild 属性名 `FrameworkDesignGuidelinesProfile`，取值 `library` / `application` / `tests`。
- 包 ID `FrameworkDesignGuidelines.Analyzers`（开发依赖，`PrivateAssets=all`）；工具包 ID `dotnet-fdg`，命令 `dotnet fdg`；插件名 `framework-design-guidelines`。三者版本号同为 `Directory.Build.props` 中的 `<Version>`，初始 `0.1.0`。
- 严重级别默认映射：DO / DO NOT / AVOID → `warning`，CONSIDER → `suggestion`；从不使用 `error`。
- globalconfig：`is_global = true`，`global_level = -100`（若 Task 5 的测试证明会被 SDK 自带配置压过，改为 `-1`，并更新设计文档 §5.2）。
- 自写分析器 `isEnabledByDefault: false`，只由 profile 的 globalconfig 打开；公开 API 改名永不自动修复。
- 行宽：读取 `.editorconfig` 的标准键 `max_line_length`，未设置时为 120（我的决定）。
- CLI 与包之间的协议版本 `FdgProtocolVersion = 1`。
- Stop：最多 20 条诊断，同一轮最多打回 2 次，总超时 60 秒。配置使用环境变量 `FDG_HOOK_MAX_BLOCKS`、`FDG_HOOK_TIMEOUT_SECONDS`、`FDG_HOOK_BUILD`（`0` 表示关闭构建步骤）。这是我的决定，两端都能用。
- 所有面向 AI 和用户的工具输出、skill 正文均为英文（我的决定：公开项目面向国际用户）。
- 外部进程一律用 `ProcessStartInfo.ArgumentList` 传参，不拼接命令行字符串。
- 只支持 SDK 风格项目：根元素 `<Project Sdk=...>`，或包含 `<Sdk Name=...>` 或 `<Import Sdk=...>`。

## Review Focus

1. **CRLF 仓库**（Windows 加 `core.autocrlf=true`）：基线内容必须用 `git cat-file --filters HEAD:<path>` 取得，diff 用 `--ignore-cr-at-eol`。否则整文件都会被当作改动行。测试在 Task 7。
2. **路径含空格或中文**（试点仓库里有中文目录和文件名）：`git status -z` 解析、`--include` 传参、SARIF 中的 URI 解码（`%20`、UTF-8 百分号编码）都必须正确。测试在 Task 6 和 Task 7。
3. **AI 改动了不在范围内的 `.cs`**：包括老式项目、没有设置 profile 的项目、不属于任何项目的文件、`obj/` 下的生成文件。这些文件必须静默跳过，不格式化也不报告。测试在 Task 7。
4. **非 git 目录、尚无提交的新仓库、`dotnet fdg` 未安装**：
   - 非 git 目录：SessionStart 给出一行"Stop 检查已停用"；
   - 尚无提交：基线视为空文件；
   - 未安装：hook 外壳给出安装命令。
   都不能抛出异常，也不能静默。测试在 Task 7、8、10。
5. **构建跳过编译导致没有 SARIF**：上一轮已经打回、这一轮 AI 没有再改，项目被判定为最新而跳过编译，于是不会生成 SARIF。必须检测到 SARIF 缺失，并对该项目用 `--no-incremental` 重建，不能误判为"没有问题"。测试在 Task 7。

---

## 文件结构

```
global.json  Directory.Build.props  Directory.Packages.props  FrameworkDesignGuidelines.slnx
.editorconfig  LICENSE  NOTICE  .github/workflows/ci.yml
rules/
  rules.schema.json          ← JSON Schema（YAML 先转 JSON 再校验）
  meta.yaml                  ← repositoryUrl、各章源文件与图标数
  profiles.yaml              ← 每个 profile 的基础选项（api_surface 等）
  ch03-naming.yaml  appA-coding-style.yaml
src/
  Fdg.Generator/             ← extract / verify-source / check / generate
    Book/ (BookParser, BlockRenderer, Normalizer)
    Rules/ (RuleFile, Rule 模型, RuleLoader, SchemaValidator)
    Emit/ (DescriptorEmitter, GlobalConfigEmitter, RulesJsonEmitter, SkillReferenceEmitter, RuleDocEmitter, CoverageEmitter)
    Sdk/SdkDiagnosticCatalog.cs
  FrameworkDesignGuidelines.Analyzers/   ← 分析器（不引用 Workspaces）
    Generated/FdgDescriptors.g.cs
    Layout/ (SingleLineCatchFinallyAnalyzer, LongListWrappingAnalyzer, LineLength)
  FrameworkDesignGuidelines.CodeFixes/   ← CodeFix（引用 Workspaces）
  FrameworkDesignGuidelines.Package/     ← 打包项目，PackageId=FrameworkDesignGuidelines.Analyzers
    build/FrameworkDesignGuidelines.Analyzers.props|.targets
    build/globalconfigs/fdg.{library,application,tests}.globalconfig   ← 生成
    data/rules.json                                                    ← 生成
  Fdg.Cli/
    Program.cs  Commands/ (FixCommand, HookCommands)
    Projects/ (PathResolver, ProjectInfo, ProjectInfoProvider, SdkStyleDetector)
    Tooling/ (IFormatRunner, IBuildRunner, DotnetFormatRunner, DotnetBuildRunner, ProcessRunner)
    Hooks/ (HookInput, HostKind, HookOutput, StateStore, Snapshot, ChangedLines, SarifReader, DiagnosticFilter, StopReport, SessionContext)
    Rules/RulesManifest.cs
plugins/framework-design-guidelines/
  .claude-plugin/plugin.json  .codex-plugin/plugin.json
  skills/{fdg-write,fdg-setup,fdg-fix,fdg-review}/SKILL.md
  skills/fdg-write/references/  ← 生成
  agents/fdg-reviewer.md  hooks/hooks.json  hooks/fdg-hook.sh
.claude-plugin/marketplace.json
docs/rules/FDG*.md  docs/coverage.md   ← 生成
tests/
  Fdg.Generator.Tests/  FrameworkDesignGuidelines.Analyzers.Tests/  Fdg.Cli.Tests/
  Fdg.IntegrationTests/      ← 打包后在临时项目中构建与格式化，Trait Category=Integration
  fixtures/book/*.xhtml      ← 手写的仿原书标记的小样本
  fixtures/hooks/{claude,codex}/*.json   ← 实测采集的 hook 输入
```

---

### Task 1: 仓库骨架、规则模型与原书抽取器

**Files:**
- Create: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `FrameworkDesignGuidelines.slnx`, `.editorconfig`, `LICENSE`, `NOTICE`
- Create: `rules/rules.schema.json`, `rules/meta.yaml`
- Create: `src/Fdg.Generator/**`（Book/, Rules/, `Program.cs`）
- Test: `tests/Fdg.Generator.Tests/BookParserTests.cs`, `tests/fixtures/book/sample.xhtml`

**Interfaces:**
- Produces:
  - `record Rule(string Id, Strength Strength, string Text, string? Context, string Section, string Anchor, IReadOnlyList<Block> Body, RuleMapping Mapping)`。
  - `enum Strength { Do, DoNot, Avoid, Consider }`，YAML 中写作 `DO` / `DO NOT` / `AVOID` / `CONSIDER`。
  - `record Block(BlockKind Kind, string Text, string? Author)`；`BlockKind` 取 `Paragraph | Code | Annotation | List | Table`。
  - `BookParser.Parse(string xhtmlPath, string chapterToken) -> BookChapter(IReadOnlyList<Rule> Rules, int IconCount)`。
  - CLI：
    - `fdg-gen extract --book <OEBPS 目录> --chapter <3|A>`：写出 `rules/<file>.yaml` 骨架，`Mapping` 为默认值、`status: draft`；目标文件已存在时退出码 1，提示 `already exists; extraction never overwrites`。
    - `fdg-gen verify-source --book <OEBPS 目录>`：对 `meta.yaml` 中列出的每一章重新抽取，比较条目数和原文字段。
  - YAML 规则字段（schema 与之一致，**原文字段**为 `strength/text/context/section/anchor/body`，其余为**映射字段**）：

```yaml
- id: FDG3001
  strength: DO
  text: use PascalCasing for the names of namespaces, types, members, and generic type parameters.
  context: null              # 表格内条目所在行的首列，例如 System.Attribute
  section: 3.1.1 Capitalization Rules for Identifiers
  anchor: sec3_1_1
  body:
    - kind: paragraph
      text: For example, use `TextColor` rather than ...
  status: draft              # draft | verified
  style: false               # 附录 A 风格约定为 true
  check:
    kind: judgment           # judgment | sdk | fdg | none
    diagnostics: []          # 例如 [IDE1006] 或 [FDGA003]
    coverage: none           # full | partial | none
  autofix: no                # yes | no | breaking
  severity: { library: warning, application: warning, tests: warning }   # none|silent|suggestion|warning
  config: []                 # 写入 globalconfig 的 editorconfig 行，见 Task 2
  minLanguage: null          # 例如 "C# 9"
  alternative: null          # 老版本下的替代写法
  analyzer: null             # 仅 kind=fdg：{ message: "...", category: Layout }
  notes: null
```

- [ ] **Step 1: 建骨架**
  - `Directory.Build.props`：`<Version>0.1.0</Version>`、`Nullable=enable`、`LangVersion=latest`、`TreatWarningsAsErrors=true`、`ImplicitUsings=enable`。
  - `Directory.Packages.props`：开启 CPM。
  - `.editorconfig`：本仓库自用的 BCL 风格。
  - `LICENSE`：MIT。
  - `NOTICE`：照抄设计文档 §3 许可证一条的三点声明。
  - `rules/meta.yaml`：

```yaml
repositoryUrl: https://github.com/tianvan/framework-design-guidelines
chapters:
  - { token: "3", file: ch03.xhtml, rules: ch03-naming.yaml, title: Naming Guidelines }
  - { token: "A", file: app01.xhtml, rules: appA-coding-style.yaml, title: C# Coding Style Conventions }
```

  生成器只读取这里的值。

- [ ] **Step 2: 写手工样本 `tests/fixtures/book/sample.xhtml`**
  - 照原书标记写一个小章节：
    - `h3#sec9_1` 与 `h4#sec9_1_1`；
    - 3 个 `p.check`：DO 用 `check.jpg`，DO NOT 用 `cross.jpg`，CONSIDER 用 `check.jpg`；
    - 每条后跟 `p.check-p`、`p.codelink1`、`<pre>`（内容含 `ʘ` 和 `&#13;`）；
    - 一个 `div.sidebar`（`p.sb-title > strong` 为作者，后跟 `p.sb-noindent`）；
    - 一个 `ul.bullet`；
    - 一个表格行 `<td>System.Attribute</td><td><p class="tab-para1">… check1.jpg … <strong>DO</strong> add the suffix …</p></td>`。
  - 再写一个变体 `mismatch.xhtml`：`DO NOT` 配 `check.jpg`。

- [ ] **Step 3: 写失败测试 `BookParserTests`**

```csharp
[Fact] public void Parses_all_icon_items_in_book_order()
// rules.Select(r => r.Id) == ["FDG9001","FDG9002","FDG9003","FDG9004"]; IconCount == 4
[Fact] public void Strength_comes_from_strong_and_must_match_icon()
// Parse("mismatch.xhtml") throws BookFormatException，消息含 "FDG9002" 与 "cross"
[Fact] public void Text_excludes_strength_and_icon()
// rules[0].Text == "use PascalCasing for ..."（与样本一致，首尾无空白）
[Fact] public void Code_blocks_normalize_glyph_zero_and_carriage_returns()
// body 中 Kind==Code 的 Text 不含 'ʘ' 与 '\r'，含 "0"；行尾无空白
[Fact] public void Codelink_paragraphs_are_dropped()
// 所有 Block.Text 都不含 "Click here to view code image"
[Fact] public void Sidebar_becomes_annotation_with_author()
// Kind==Annotation，Author=="Krzysztof Cwalina"（样本中的作者），Text 不含作者行
[Fact] public void Body_stops_at_next_guideline_or_heading()
[Fact] public void Table_items_get_row_context_and_empty_body()
// rules[3].Context == "System.Attribute"; rules[3].Body 为空
[Fact] public void Inline_code_becomes_backticks()
// "`TextColor`"
[Fact] public void Section_and_anchor_track_nearest_heading()
// rules[0].Section == "9.1.1 ..."; Anchor == "sec9_1_1"
```

- [ ] **Step 4: 运行，确认失败**
  Run: `dotnet test tests/Fdg.Generator.Tests --filter BookParserTests`
  Expected: FAIL（类型不存在）

- [ ] **Step 5: 实现 `BookParser`、`BlockRenderer`、`Normalizer`，以及 `extract` / `verify-source` 命令**
  - 用 AngleSharp 的 HTML 解析器（文件含 `&#13;` 和命名实体，XmlReader 不可靠），在 `body` 内按文档顺序遍历；
  - 图标识别：`img[src$="check.jpg" | "cross.jpg" | "check1.jpg" | "cross1.jpg"]` 所在的 `p.check` 或 `p.tab-para1`；
  - 表格条目的 `Context` 取同一 `tr` 第一个 `td` 的文本；
  - 段落内空白折叠，`pre` 保留换行；
  - 表格渲染为 Markdown 表格。
  - YAML 用 YamlDotNet 输出，多行文本使用 literal block。

- [ ] **Step 6: 运行，确认通过**
  Run: `dotnet test tests/Fdg.Generator.Tests --filter BookParserTests`
  Expected: PASS

- [ ] **Step 7: 在真书上抽取**
  Run: `dotnet run --project src/Fdg.Generator -- extract --book private-sources/fdg3-pilot/data/ex/OEBPS --chapter 3`，然后对 `--chapter A` 再运行一次。
  Expected: `rules/ch03-naming.yaml` 共 87 条（76 个 `p.check` 加 11 个表格条目），`rules/appA-coding-style.yaml` 共 71 条；`verify-source` 退出码 0。
  再抽查：
  - `FDGA003` 是 "place the closing brace on its own line, except the end of a do..while statement."；
  - `FDGA023` 是 "chop before the first argument or parameter, ..."；
  - 全部 YAML 中 `grep 'ʘ'` 无结果。

- [ ] **Step 8: Commit**
  `git add global.json Directory.*.props *.slnx .editorconfig LICENSE NOTICE rules src/Fdg.Generator tests/Fdg.Generator.Tests tests/fixtures/book && git commit -m "feat: extract chapter 3 and appendix A guidelines from the book"`

---

### Task 2: 映射校验（`check`）与第 3 章、附录 A 的映射

**Files:**
- Create: `rules/profiles.yaml`, `src/Fdg.Generator/Rules/SchemaValidator.cs`, `src/Fdg.Generator/Sdk/SdkDiagnosticCatalog.cs`, `src/Fdg.Generator/Commands/CheckCommand.cs`
- Modify: `rules/ch03-naming.yaml`, `rules/appA-coding-style.yaml`（只改映射字段）
- Test: `tests/Fdg.Generator.Tests/CheckCommandTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `Rule`、`RuleLoader.LoadAll(string rulesDir) -> IReadOnlyList<RuleFile>`
- Produces:
  - `SdkDiagnosticCatalog.Load(string sdkDirectory) -> IReadOnlyDictionary<string, SdkDiagnostic(Id, Title, DefaultSeverity, HasCodeFix)>`；
  - `fdg-gen check [--book <dir>]`：带 `--book` 时同时执行 `verify-source`；
  - `rules/profiles.yaml`：

```yaml
library:     ["dotnet_code_quality.api_surface = public"]
application: ["dotnet_code_quality.api_surface = all"]
tests:       ["dotnet_code_quality.api_surface = all"]
```

- [ ] **Step 1: 写失败测试**

```csharp
[Fact] public void Schema_rejects_unknown_strength()
[Fact] public void Unknown_sdk_diagnostic_is_reported()          // diagnostics: [CA9999] → 错误含 "FDG3001" "CA9999"
[Fact] public void Fdg_kind_must_include_own_id()                  // kind=fdg 但 diagnostics 不含自身编号 → 错误（可以同时含 SDK 编号）
[Fact] public void Fdg_kind_requires_analyzer_message()
[Fact] public void Duplicate_rule_id_is_reported()
[Fact] public void Consider_with_warning_severity_is_allowed_but_default_is_suggestion()  // 默认值生成测试
[Fact] public void Catalog_loads_netanalyzers_and_codestyle_from_installed_sdk()
// 包含 CA1707、CA1716、IDE1006、IDE0011、IDE0055；IDE1006.HasCodeFix == true
```

  最后一个测试从 `dotnet --list-sdks` 中与 global.json 匹配的 SDK 目录加载：
  - `Sdks/Microsoft.NET.Sdk/analyzers/Microsoft.CodeAnalysis.NetAnalyzers.dll`、`Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll`；
  - `Sdks/Microsoft.NET.Sdk/codestyle/cs/*.dll`。

  通过 `AnalyzerFileReference` 读取 `SupportedDiagnostics`；用 `ExportCodeFixProvider` 特性加上 `FixableDiagnosticIds` 判断是否有 CodeFix。

- [ ] **Step 2: 运行，确认失败**
  Run: `dotnet test tests/Fdg.Generator.Tests --filter CheckCommandTests`
  Expected: FAIL

- [ ] **Step 3: 实现 `SchemaValidator`、`SdkDiagnosticCatalog`、`CheckCommand`**
  - 所有错误汇总后统一输出，格式为 `<file>:<ruleId>: <message>`，有错误时退出码 1。

- [ ] **Step 4: 运行，确认通过**
  Run: `dotnet test tests/Fdg.Generator.Tests --filter CheckCommandTests`
  Expected: PASS

- [ ] **Step 5: AI 映射 158 条规则（只改映射字段）**
  按以下准则逐条填写：
  - `check.kind`：
    - 有 SDK 规则能精确或部分判定时用 `sdk`，同时填 `diagnostics` 和 `coverage`；
    - 需要自写分析器时用 `fdg`，`diagnostics` 写自身编号。本切片只有 `FDGA003`（catch/finally 单行块）和 `FDGA023`（长参数列表折行）两条；
    - 其他情况用 `judgment`。可以写分析器但留到第 2 阶段的规则，在 `notes` 中写 `planned-analyzer`。
  - 排版类规则（A.1.1–A.1.4）：在 `config` 中写对应的 `csharp_*` 和 `dotnet_*` 选项，例如 `csharp_new_line_before_open_brace = all`、`csharp_preserve_single_line_statements = false`、`csharp_preserve_single_line_blocks = false`、`indent_size = 4`、`indent_style = space`，并映射到 `IDE0055`。
  - 命名类规则：在 `config` 中写 `dotnet_naming_rule.<ruleid小写>_*`、`dotnet_naming_symbols.*`、`dotnet_naming_style.*`，命名规则名以 FDG 编号开头，并映射到 `IDE1006`。tests profile 的 `severity.tests` 放宽为 `none` 或 `suggestion`，依据是设计文档 §5.1。
  - `autofix`：
    - SDK 规则有 CodeFix，且修复不改变公开签名或名字时为 `yes`；
    - 修复会改名或改签名时为 `breaking`（CLI 会排除这类修复）；
    - 其余为 `no`。
  - `severity`：按 Global Constraints 中的默认映射。API 设计类规则在 tests 档为 `none`。
  - `minLanguage` 和 `alternative`：依赖较新语法的规则才填，例如 A.1.1 的 `await using`、A.1.6 的 `nameof` 是 C# 6。
  - `style`：附录 A 的全部规则为 `true`。

- [ ] **Step 6: 运行校验**
  Run: `dotnet run --project src/Fdg.Generator -- check --book private-sources/fdg3-pilot/data/ex/OEBPS`
  Expected: 退出码 0

- [ ] **Step 7: 独立代理交叉核对**
  派一个全新的子代理，给它三份材料：设计文档 §5.8 第 4 步、两份 YAML、原书 `ch03.xhtml` 和 `app01.xhtml` 的路径。要求它逐条核对：
  - 映射是否合理；
  - profile 和例外条件是否与原文一致；
  - `body` 是否完整，尤其是跨多个代码块或带批注的条目。

  核对代理只输出问题清单。主代理修正后，把通过的条目改为 `status: verified`，然后再次运行 Step 6。最后挑 10 条交给用户抽查，在交付说明中列出这 10 条的编号。

- [ ] **Step 8: Commit**
  `git add rules src/Fdg.Generator tests/Fdg.Generator.Tests && git commit -m "feat: map chapter 3 and appendix A rules to analyzers"`

---

### Task 3: 生成器 `generate`

**Files:**
- Create: `src/Fdg.Generator/Emit/*.cs`, `src/Fdg.Generator/Commands/GenerateCommand.cs`
- Generates:
  - `src/FrameworkDesignGuidelines.Analyzers/Generated/FdgDescriptors.g.cs`
  - `src/FrameworkDesignGuidelines.Package/build/globalconfigs/fdg.{library,application,tests}.globalconfig`
  - `src/FrameworkDesignGuidelines.Package/data/rules.json`
  - `plugins/framework-design-guidelines/skills/fdg-write/references/{ch03-naming.md, appA-coding-style.md, examples/ch03-naming.md, examples/appA-coding-style.md}`
  - `docs/rules/<ID>.md`
  - `docs/coverage.md`
- Test: `tests/Fdg.Generator.Tests/EmitterTests.cs`（输入为 `tests/fixtures/rules/` 下的小型 YAML）

**Interfaces:**
- Consumes: Task 1、2 的模型和 `meta.yaml`、`profiles.yaml`
- Produces（后续任务依赖的格式）：
  - `FdgDescriptors`：`internal static class FdgDescriptors { public static readonly DiagnosticDescriptor FDGA003; ... }`。
    - `title` 为 `"<STRENGTH> <text>"`，`messageFormat` 取 `analyzer.message`；
    - `category` 为 `"FrameworkDesignGuidelines." + analyzer.category`；
    - `defaultSeverity: Warning`，`isEnabledByDefault: false`；
    - `helpLinkUri` 为 `{repositoryUrl}/blob/main/docs/rules/{id}.md`。
  - globalconfig：
    - 首两行 `is_global = true`、`global_level = -100`；
    - 接着是 profiles.yaml 中的基础选项；
    - 然后是各规则的 `config` 行，去重；同一键出现不同值时生成失败并报出冲突规则；
    - 最后是 `dotnet_diagnostic.<diag>.severity = <级别>`，同一诊断取映射到它的所有规则在该 profile 下的最高级别，`none` 最低。
  - `rules.json`：

```json
{ "protocol": 1, "version": "0.1.0",
  "rules": [ { "id": "FDG3001", "strength": "DO", "text": "...", "section": "3.1.1 ...",
               "diagnostics": ["IDE1006"], "autofix": "no",
               "severity": { "library": "warning", "application": "warning", "tests": "none" } } ],
  "registeredDiagnostics": ["CA1707", "FDGA003", "IDE0055", "IDE1006"],
  "breakingFixDiagnostics": ["CA1710"] }
```

  - skill 参考文件：
    - 主文件只含 `check.kind == judgment` 的规则，以及 `strength == CONSIDER` 且有分析器的规则；
    - 每条一行：`- **FDG3012** DO NOT ... [C# 9+] (alt: ...) — examples: examples/ch03-naming.md#fdg3012`；
    - 按小节分组；
    - examples 文件放 `body` 全文。
  - coverage.md：每章一行，列出条目数、`sdk` 数、`fdg` 数、`judgment` 数、`verified` 数。
- `fdg-gen generate`：生成全部产物；输出与磁盘一致时不改动文件，保持 mtime。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact] public void Severity_for_shared_diagnostic_is_max_across_rules()   // 两条规则映射 IDE1006: suggestion + warning → warning
[Fact] public void Conflicting_config_values_fail_with_both_rule_ids()
[Fact] public void Globalconfig_starts_with_is_global_and_level()
[Fact] public void Tests_profile_omits_rules_with_none()
// 生成 "dotnet_diagnostic.CA1716.severity = none"，而不是省略：显式关闭，避免 SDK 默认值生效
[Fact] public void Skill_reference_excludes_tool_checked_do_rules()
[Fact] public void Skill_reference_includes_consider_rules_with_analyzers()
[Fact] public void Skill_reference_tags_min_language()                      // "[C# 9+]"
[Fact] public void Descriptor_uses_help_link_from_meta()
[Fact] public void Rules_json_lists_breaking_fix_diagnostics()
[Fact] public void Generate_is_idempotent()                                  // 连续运行两次，第二次没有文件写入
```

- [ ] **Step 2: 运行，确认失败**
  Run: `dotnet test tests/Fdg.Generator.Tests --filter EmitterTests`
  Expected: FAIL
- [ ] **Step 3: 实现各 Emitter 与 `GenerateCommand`**
- [ ] **Step 4: 运行，确认通过**
  Run: `dotnet test tests/Fdg.Generator.Tests`
  Expected: PASS
- [ ] **Step 5: 对真实规则生成**
  Run: `dotnet run --project src/Fdg.Generator -- generate`
  Expected: 各生成物出现在上面列出的位置；`docs/coverage.md` 中第 3 章为 87 条，附录 A 为 71 条。
- [ ] **Step 6: Commit**
  `git add src tests docs plugins && git commit -m "feat: generate globalconfigs, rules manifest, skill references and docs"`

---

### Task 4: 两个排版分析器与 FixAll CodeFix

**Files:**
- Create: `src/FrameworkDesignGuidelines.Analyzers/Layout/SingleLineCatchFinallyAnalyzer.cs`, `LongListWrappingAnalyzer.cs`, `LineLength.cs`
- Create: `src/FrameworkDesignGuidelines.CodeFixes/Layout/SingleLineCatchFinallyCodeFix.cs`, `LongListWrappingCodeFix.cs`
- Test: `tests/FrameworkDesignGuidelines.Analyzers.Tests/Layout/*.cs`

**Interfaces:**
- Consumes: `FdgDescriptors.FDGA003`、`FdgDescriptors.FDGA023`（Task 3 生成）
- Produces: 诊断 `FDGA003` 和 `FDGA023`，两者都有 FixAll，使用 `WellKnownFixAllProviders.BatchFixer`。`LineLength.Get(AnalyzerConfigOptions) -> int` 读取 `max_line_length`，默认值 120。

**判定规则（我的决定，依据附录 A [3] 和 [23]）：**
- **FDGA003**：`CatchClause` 或 `FinallyClause` 的 `Block` 中，`{` 与 `}` 在同一行时报告，位置为 `catch` 或 `finally` 关键字。修复后：
  - 关键字（以及 `catch (...)`）单独一行，`{` 和 `}` 各占一行，与关键字对齐；
  - 块内语句一行一条，缩进一级；
  - 空块修复为两行 `{` `}`。
- **FDGA023**：
  - 检查对象：
    - 方法、构造函数、委托、本地函数、运算符、索引器的 `ParameterList` / `BracketedParameterList`；
    - 调用、对象创建、构造函数初始化器的 `ArgumentList`。
  - 报告条件：列表项不少于 2 个；左括号所在行的长度超过阈值；该列表不是折行形式；一行有多个候选时，只报告最外层的那个。
  - 折行形式：
    - 左括号是行尾最后一个字符；
    - 每项单独一行，缩进为语句起始行的缩进加 4 个空格；
    - 右括号紧跟最后一项，不单独成行。
  - 修复：改写为折行形式，项内文本保持原样。内层的超长列表留给下一次运行。

- [ ] **Step 1: 写失败测试**（`CSharpCodeFixTest<..., DefaultVerifier>`，`ReferenceAssemblies` 分别跑 `NetFramework.Net472.Default` 和 `Net.Net80`，`LanguageVersion` 跑 `CSharp7_3` 和 `Latest`）

```csharp
[Fact] public Task Single_line_catch_is_expanded()
// before: "try\n{\n    Run();\n}\ncatch (IOException ex) { Log(ex); }"
// after:  "...\ncatch (IOException ex)\n{\n    Log(ex);\n}"
[Fact] public Task Empty_single_line_finally_is_expanded()
[Fact] public Task Multiline_catch_is_not_reported()
[Fact] public Task Long_invocation_is_chopped_before_first_argument()
// 一行 130 列、9 个参数的调用 Failed(a, b, null, ...)，期望：
// "Failed(\n        a,\n        b,\n ...        i);"（缩进 = 语句缩进 + 4）
[Fact] public Task Long_parameter_list_is_chopped()
[Fact] public Task Single_argument_is_not_reported()
[Fact] public Task Line_within_limit_is_not_reported()
[Fact] public Task Max_line_length_from_editorconfig_is_respected()     // max_line_length = 80
[Fact] public Task Already_chopped_list_is_not_reported()
[Fact] public Task Only_outermost_list_on_line_is_reported()
[Fact] public Task FixAll_in_document_fixes_all_occurrences()
[Fact] public Task Descriptors_are_disabled_by_default()                // 不配置时无诊断
```

- [ ] **Step 2: 运行，确认失败**
  Run: `dotnet test tests/FrameworkDesignGuidelines.Analyzers.Tests`
  Expected: FAIL
- [ ] **Step 3: 实现两个分析器与两个 CodeFix**
  - 分析器用 `RegisterSyntaxNodeAction`，并开启 `EnableConcurrentExecution`、关闭生成代码分析；
  - CodeFix 构造新的 trivia，并加上 `Formatter.Annotation`。
- [ ] **Step 4: 运行，确认通过**
  Run: `dotnet test tests/FrameworkDesignGuidelines.Analyzers.Tests`
  Expected: PASS
- [ ] **Step 5: Commit**
  `git add src/FrameworkDesignGuidelines.Analyzers src/FrameworkDesignGuidelines.CodeFixes tests/FrameworkDesignGuidelines.Analyzers.Tests && git commit -m "feat: add catch/finally expansion and long list wrapping analyzers"`

---

### Task 5: 分析器包（props/targets、标记文件）与 globalconfig 集成测试

**Files:**
- Create: `src/FrameworkDesignGuidelines.Package/FrameworkDesignGuidelines.Package.csproj`, `build/FrameworkDesignGuidelines.Analyzers.props`, `build/FrameworkDesignGuidelines.Analyzers.targets`
- Test: `tests/Fdg.IntegrationTests/PackageFixture.cs`, `PackageTests.cs`

**Interfaces:**
- Produces（CLI 依赖）：
  - props 中定义：
    - `EnableNETAnalyzers=true`、`EnforceCodeStyleInBuild=true`（用户已显式设置时不覆盖）；
    - `FdgPackageVersion=$(版本)`、`FdgProtocolVersion=1`；
    - `<CompilerVisibleProperty Include="FrameworkDesignGuidelinesProfile" />`。
  - targets 中：
    - profile 有效时，加入 `GlobalAnalyzerConfigFiles` `build/globalconfigs/fdg.$(FrameworkDesignGuidelinesProfile).globalconfig`；
    - profile 为空时发出 `warning FDG0001: FrameworkDesignGuidelinesProfile is not set; FDG rules are inactive. Set it to library, application or tests.`；
    - profile 非法时发出 `warning FDG0002: Unknown FrameworkDesignGuidelinesProfile '<值>'. Use library, application or tests.`；
    - `FdgSarifDir` 非空时，设置 `ErrorLog=$(FdgSarifDir)/$(MSBuildProjectName)-$(TargetFramework).sarif,version=2.1`。
  - 标记文件：target `FdgWriteMarker` 在 `BeforeTargets="CoreCompile"` 时写入 `$(BaseIntermediateOutputPath)fdg/marker.json`，内容不变时不重写：

```json
{ "protocol": 1, "packageVersion": "0.1.0", "profile": "application",
  "langVersion": "7.3", "targetFramework": "netstandard2.0",
  "rulesPath": "<包目录>/data/rules.json", "projectPath": "<csproj 绝对路径>" }
```

    其中 `langVersion` 取 `$(LangVersion)`，为空时取 `$(MaxSupportedLangVersion)`。多目标项目每个 TFM 写一份 `marker.<tfm>.json`，同时写一份 `marker.json`。
  - 包布局：`analyzers/dotnet/cs/` 下放两个 dll，`build/`、`buildTransitive/` 不放（开发依赖，不传递），另有 `data/rules.json`、`developmentDependency=true`。

- [ ] **Step 1: 写失败测试**
  测试夹具：
  - 运行 `dotnet pack src/FrameworkDesignGuidelines.Package -o <临时>/feed -p:Version=0.1.0-test.<时间戳>`；
  - 每个测试使用独立的 `NUGET_PACKAGES` 目录，以及指向该 feed 的 nuget.config；
  - 临时项目为 netstandard2.0，`LangVersion=7.3`。

```csharp
[Fact] public void Build_reports_layout_and_naming_warnings_for_application_profile()
// 源文件含 catch { } 单行块、130 列调用、类名 textColor
// 断言输出含 "warning FDGA003"、"warning FDGA023"、"warning IDE1006"
[Fact] public void Build_reports_mapped_ca_rule()      // CA1707（标识符含下划线）在 application 档报告，用来验证 global_level 没有被 SDK 配置压过
[Fact] public void Tests_profile_does_not_report_api_design_rules()
[Fact] public void Missing_profile_emits_FDG0001_and_no_fdg_diagnostics()
[Fact] public void Invalid_profile_emits_FDG0002()
[Fact] public void User_editorconfig_overrides_package_severity()   // .editorconfig 设 IDE1006 = none → 不报告
[Fact] public void Dotnet_format_fixes_package_codefixes()
// 运行 dotnet format <proj> --no-restore；断言 catch 块被展开、长调用被折行、一行多语句被拆开
[Fact] public void Marker_file_is_written_with_profile_and_lang_version()
[Fact] public void Sarif_is_written_when_FdgSarifDir_is_set()
[Fact] public void Package_does_not_flow_to_consumers()             // 引用测试项目的另一个项目中无 FDG 诊断
```

- [ ] **Step 2: 运行，确认失败**
  Run: `dotnet test tests/Fdg.IntegrationTests --filter FullyQualifiedName~PackageTests`
  Expected: FAIL
- [ ] **Step 3: 实现打包项目、props、targets**
- [ ] **Step 4: 运行，确认通过**
  Run: `dotnet test tests/Fdg.IntegrationTests --filter FullyQualifiedName~PackageTests`
  Expected: PASS
  如果 `Build_reports_mapped_ca_rule` 失败，且原因是 SDK 的 AnalysisLevel 配置优先级更高：把 `GlobalConfigEmitter` 的 `global_level` 改为 `-1`，重新生成，并在设计文档 §5.2 记录实测结论。
- [ ] **Step 5: 记录设计文档 §8 的实测结论**
  把 `global_level` 与 `AnalysisLevel` 的关系、FixAll 能否被 `dotnet format` 应用这两项结论写进设计文档 §8。
- [ ] **Step 6: Commit**
  `git add src/FrameworkDesignGuidelines.Package tests/Fdg.IntegrationTests docs && git commit -m "feat: package analyzers with per-profile globalconfigs and project marker"`

---

### Task 6: CLI `dotnet fdg fix`

**Files:**
- Create: `src/Fdg.Cli/Program.cs`, `Commands/FixCommand.cs`, `Projects/*.cs`, `Tooling/*.cs`, `Rules/RulesManifest.cs`
- Test: `tests/Fdg.Cli.Tests/PathResolverTests.cs`, `ProjectInfoProviderTests.cs`, `FixCommandTests.cs`；`tests/Fdg.IntegrationTests/FixEndToEndTests.cs`

**Interfaces:**
- Consumes: Task 5 的标记文件格式与 `rules.json`
- Produces（Task 7、8 复用）：
  - `PathResolver.Resolve(IReadOnlyList<string> paths, string cwd) -> IReadOnlyList<FixTarget(string ProjectPath, IReadOnlyList<string>? IncludeFiles)>`：
    - `IncludeFiles == null` 表示整个项目；
    - 目录：递归查找 `*.csproj`，跳过 `bin/`、`obj/` 和以 `.` 开头的目录；该目录本身位于某个项目内部时，`IncludeFiles = [目录]`；
    - `.sln` / `.slnx`：用 `dotnet sln <file> list` 列出项目；
    - `.cs` 文件：取向上最近的 `.csproj`；
    - 多个路径的结果按项目合并。
  - `SdkStyleDetector.IsSdkStyle(string csprojPath) -> bool`。
  - `ProjectInfoProvider.Get(string csprojPath) -> ProjectInfo(string ProjectPath, string? Profile, string? PackageVersion, int? Protocol, string? LangVersion, string? TargetFramework, string? RulesPath, ProjectState State)`：
    - `ProjectState` 取 `InScope | NotSdkStyle | NoPackage | NoProfile | NotRestored`；
    - 先读 `obj/fdg/marker.json`，读不到时退回 `dotnet msbuild <proj> -getProperty:FrameworkDesignGuidelinesProfile -getProperty:FdgPackageVersion -getProperty:FdgProtocolVersion`；
    - `obj/project.assets.json` 不存在时为 `NotRestored`。
  - `IFormatRunner.FormatAsync(string projectPath, IReadOnlyList<string>? includeFiles, IReadOnlyList<string> excludeDiagnostics, CancellationToken) -> Task<ToolResult(int ExitCode, string Output, bool TimedOut)>`：
    - 实现为 `DotnetFormatRunner`，命令是 `dotnet format <proj> --no-restore --severity warning [--include ...] [--exclude-diagnostics ...]`；
    - 不加子命令，即依次执行 whitespace、style、analyzers。
  - `RulesManifest.Load(string path)`。
- 行为（照抄设计文档 §5.9）：
  - 只处理 `InScope` 的项目；`NotRestored` 的项目先执行 `dotnet restore` 再判断。
  - 排除 `breakingFixDiagnostics` 中的诊断；不出报告，也没有任何选项。
  - 每个跳过的项目输出一行 `skipped <相对路径>: <原因>`，处理过的项目输出 `fixed <相对路径>`。
  - 退出码：0 表示成功；1 表示有格式化失败，同时输出失败的项目和原因的前 20 行。
  - `dotnet fdg --version` 输出版本号。

- [ ] **Step 1: 写失败测试**（单元测试用临时目录树、手写的 csproj，`ProjectInfoProvider` 和 `IFormatRunner` 用假实现）

```csharp
[Fact] public void Directory_resolves_all_projects_skipping_bin_obj_and_hidden()
[Fact] public void Directory_inside_project_becomes_include_filter()
[Fact] public void Cs_file_resolves_to_nearest_project_with_include()
[Fact] public void Solution_and_slnx_list_projects()                 // 真实调用 dotnet sln list，放在 Integration 中
[Fact] public void Multiple_paths_merge_per_project()
[Fact] public void Paths_with_spaces_and_cjk_characters_are_passed_intact()  // 假 runner 收到的 include 等于 "src/中文 目录/A.cs"
[Fact] public void Legacy_project_is_detected()                      // <Project ToolsVersion="15.0" xmlns=...> → false
[Fact] public void Marker_takes_precedence_over_msbuild_query()
[Fact] public async Task Out_of_scope_projects_are_skipped_with_reason()
[Fact] public async Task Breaking_fix_diagnostics_are_excluded()
[Fact] public async Task Format_failure_returns_exit_code_1_with_message()
```

  集成测试 `FixEndToEndTests`：
  - 临时解决方案含两个项目：一个设置了 profile，一个没有引用包；
  - 预先埋好违规：一行多条语句、单行 catch、长调用、省略大括号的 if；
  - 运行 `dotnet fdg fix <sln>`。
  - 断言：设置了 profile 的项目全部被修复；另一个项目的文件字节不变；输出含 `skipped`。

- [ ] **Step 2: 运行，确认失败**
  Run: `dotnet test tests/Fdg.Cli.Tests`
  Expected: FAIL
- [ ] **Step 3: 实现**
  - `Fdg.Cli.csproj`：`PackAsTool=true`、`ToolCommandName=dotnet-fdg`、`PackageId=dotnet-fdg`、`TargetFramework=net8.0`、`RollForward=Major`；
  - 命令行手写解析，不引入 System.CommandLine，因为只有 `fix`、`hook <event>`、`--version` 三种形式。
- [ ] **Step 4: 运行，确认通过**
  Run: `dotnet test tests/Fdg.Cli.Tests`，然后运行 `dotnet test tests/Fdg.IntegrationTests --filter FullyQualifiedName~FixEndToEndTests`
  Expected: PASS
- [ ] **Step 5: Commit**
  `git add src/Fdg.Cli tests && git commit -m "feat: add dotnet fdg fix"`

---

### Task 7: Hook 核心（输入解析、快照、Stop 流程）

**Files:**
- Create: `src/Fdg.Cli/Commands/HookCommands.cs`, `src/Fdg.Cli/Hooks/*.cs`, `src/Fdg.Cli/Tooling/DotnetBuildRunner.cs`
- Create: `tests/fixtures/hooks/claude/{session-start,user-prompt-submit,stop}.json`, `tests/fixtures/hooks/codex/{session-start,user-prompt-submit,stop}.json`
- Test: `tests/Fdg.Cli.Tests/Hooks/*.cs`

**Interfaces:**
- Consumes: Task 6 的 `ProjectInfoProvider`、`IFormatRunner`、`RulesManifest`
- Produces（Task 8、10 依赖）：
  - 命令：`dotnet fdg hook session-start|turn-start|turn-stop`，从 stdin 读取 JSON，向 stdout 写 JSON，退出码始终为 0（除非 stdin 无法解析）。
  - `HookInput.Parse(string json) -> HookInput(HostKind Host, string SessionId, string Cwd, string EventName)`。`HostKind` 取 `Claude | Codex`，依据 Step 1 采集的真实字段区分；字段无法区分时，退回检查环境变量 `CLAUDE_PLUGIN_ROOT` 是否存在。
  - `IBuildRunner.BuildAsync(string projectPath, string sarifDir, bool noIncremental, CancellationToken) -> Task<ToolResult>`：
    - 命令为 `dotnet build <proj> --no-restore -p:FdgSarifDir=<dir>`；
    - `noIncremental` 为 true 时加 `--no-incremental --no-dependencies`。
  - `StateStore`：
    - 状态目录为 `<git-dir>/fdg/<session_id>/`，用 `git rev-parse --git-dir` 取得 git 目录；
    - 文件包括 `snapshot.json`、`baseline/<sha1(相对路径)>`、`blocks.txt`、`autofix.json`。
  - `HookOutput`：
    - 打回：`{"decision":"block","reason":"..."}`；
    - 放行但有提示：`{"systemMessage":"..."}`；
    - 注入上下文：`{"hookSpecificOutput":{"hookEventName":"<事件>","additionalContext":"..."}}`；
    - 无输出时向 stdout 写空字符串。

**Stop 报告格式**（每条诊断一项，按文件、行号排序，最多 20 条）：

```
FDG found 3 issue(s) in lines you changed this turn. Fix them before finishing.
src/Order/OrderService.cs:42 IDE1006 Naming rule violation: Missing prefix: '_'
    private int count;
    FDGA055 DO use a prefix "_" (underscore) for private and internal instance fields, ...
src/Order/OrderService.cs:57 CS0103 The name 'x' does not exist in the current context
```

同一诊断映射到多条规则时，最多列出 2 条规则原文。

- [ ] **Step 1: 采集两端真实 hook 输入**
  1. 在临时目录放一个只有 `hooks.json` 的最小插件，三个事件的命令都是 `cat > <临时文件>`（Codex 端按其文档等价配置）；
  2. 两端各进行一次真实会话，触发全部三个事件；
  3. 去掉其中的个人路径后，存为上面列出的 6 个夹具文件。

  同时记录以下事项，写入 `docs/hosts.md`：
  - 两端用什么 shell 执行 hook 命令（尤其是 Codex 在 Windows 上）；
  - hook 超时字段的名称和单位；
  - Codex 首次运行 hook 是否需要用户批准（对应设计文档 §8）。

- [ ] **Step 2: 写失败测试**
  使用真实的临时 git 仓库，包含一个设置了 profile 的项目（项目信息用假的 `ProjectInfoProvider` 提供），format 和 build 也用假 runner，SARIF 使用手写的夹具。

```csharp
// HookInput
[Theory] [InlineData("claude")] [InlineData("codex")] public void Parses_fixture_payloads(string host)
// Snapshot / ChangedLines
[Fact] public void Turn_start_copies_dirty_in_scope_files_to_baseline()
[Fact] public void Clean_file_baseline_is_head_content_via_cat_file_filters()
[Fact] public void Crlf_working_tree_with_autocrlf_yields_only_real_changed_lines()   // Review Focus 1
[Fact] public void Repository_without_commits_uses_empty_baseline()                   // Review Focus 4
[Fact] public void File_created_via_shell_is_detected_as_changed()
[Fact] public void Changed_lines_ignore_whitespace_only_reformatting()
// 用 git diff --no-index -U0 --ignore-all-space --ignore-cr-at-eol 解析 "@@ -a,b +c,d @@"
[Fact] public void Git_status_paths_with_spaces_and_cjk_are_parsed_from_z_output()   // Review Focus 2
// Stop
[Fact] public async Task Out_of_scope_and_obj_files_are_not_formatted_or_reported()   // Review Focus 3
[Fact] public async Task No_changes_this_turn_outputs_nothing()
[Fact] public async Task Only_registered_diagnostics_on_changed_lines_are_reported()
[Fact] public async Task Compile_errors_in_changed_files_are_reported_on_any_line()
[Fact] public async Task Suggestions_and_unregistered_warnings_are_ignored()
[Fact] public async Task Report_is_truncated_to_20_with_remaining_count()
// 第 21 条起合并为一行 "... and 7 more."
[Fact] public async Task Third_stop_in_same_turn_allows_finish_with_system_message()
// blocks.txt 中计数为 2 → 输出 systemMessage，而不是 decision:block
[Fact] public async Task Missing_sarif_triggers_no_incremental_rebuild()              // Review Focus 5
[Fact] public async Task Sarif_uri_percent_encoding_is_decoded()                      // Review Focus 2
[Fact] public async Task Build_failure_without_sarif_reports_one_line_and_allows_finish()
[Fact] public async Task Timeout_reports_one_line_and_allows_finish()                 // FDG_HOOK_TIMEOUT_SECONDS=1，假 runner 延迟 5 秒
[Fact] public async Task FDG_HOOK_BUILD_0_skips_build_but_still_formats()
[Fact] public async Task Autofixed_ranges_are_recorded_for_next_turn()
[Fact] public async Task Not_a_git_repository_outputs_nothing_for_turn_hooks()         // Review Focus 4
[Fact] public async Task Turn_start_resets_block_counter()
```

- [ ] **Step 3: 运行，确认失败**
  Run: `dotnet test tests/Fdg.Cli.Tests --filter FullyQualifiedName~Hooks`
  Expected: FAIL

- [ ] **Step 4: 实现**
  Stop 流程严格按设计文档 §5.4 的六步执行：
  1. 对比快照，得出本轮改动的 `.cs` 文件，只保留范围内的；
  2. 按项目分组，项目之间并行（`Parallel.ForEachAsync`），各自执行 format；
  3. 增量构建；SARIF 写到 `<状态目录>/sarif/<turn>/`；
  4. 读取 SARIF 并过滤；
  5. 生成报告，必要时打回；
  6. 记录本轮自动修改过的范围。

  其他要求：
  - 整个流程共用一个 `CancellationTokenSource`，超时取自 `FDG_HOOK_TIMEOUT_SECONDS`；
  - 自动修复范围通过 diff "AI 版本"与"格式化后版本"得出。AI 版本在 format 之前复制到状态目录。

- [ ] **Step 5: 运行，确认通过**
  Run: `dotnet test tests/Fdg.Cli.Tests`
  Expected: PASS

- [ ] **Step 6: Commit**
  `git add src/Fdg.Cli tests docs/hosts.md && git commit -m "feat: add turn snapshot and stop hook pipeline"`

---

### Task 8: SessionStart 注入与 turn-start 摘要

**Files:**
- Create: `src/Fdg.Cli/Hooks/SessionContext.cs`, `src/Fdg.Cli/Hooks/SelfCheck.cs`
- Modify: `src/Fdg.Cli/Commands/HookCommands.cs`
- Test: `tests/Fdg.Cli.Tests/Hooks/SessionContextTests.cs`, `TurnStartSummaryTests.cs`

**Interfaces:**
- Consumes: Task 6 的 `ProjectInfoProvider`、`SdkStyleDetector`；Task 7 的 `StateStore`、`HookOutput`
- Produces:
  - `SessionContext.Build(string repoRoot) -> string?`：返回 `null` 时不输出任何内容。
  - 项目发现：
    - 用 `git ls-files -co --exclude-standard "*.csproj" "Directory.Build.props" "Directory.Packages.props"` 列出文件；
    - 文件文本中含 `FrameworkDesignGuidelines.Analyzers` 才视为接入；
    - 同时读取各项目的 `obj/fdg/marker.json`。
  - 输出文本（英文，照设计文档 §5.5 的四条内容，加上从项目中实际读取的事实）：

```
This repository follows Framework Design Guidelines (3rd ed.).
- Profiles: src/** = application, tests/** = tests. C# 7.3; targets net472, netstandard2.0. Do not use newer syntax or APIs.
- Write in .NET BCL style: Allman braces, one statement per line, long parameter/argument lists chopped before the first item, one per line.
- Layout and machine-checkable rules are auto-fixed and checked at the end of each turn; only fix the FDG/CA/IDE issues reported to you.
- Before designing or changing types, members, or public APIs, use the fdg-write skill; use fdg-review to review an API.
```

    "Profiles" 一行的写法：按目录前缀合并（同一目录下的项目 profile 相同时写成 `<dir>/**`）；有多种语言版本或 TFM 时全部列出。
  - 自检，每个问题追加一行，以 `FDG setup issue:` 开头，并提示 `Run /fdg-setup to fix.`：
    - `dotnet --version` 低于 `8.0.100`；
    - 已接入但 profile 未设置；
    - 已接入但从未构建（没有 marker）；
    - marker 的 protocol 与 CLI 不一致，此时提示 `dotnet tool update -g dotnet-fdg`；
    - 存在老式项目，写成 `N legacy (non-SDK) projects are not supported and are ignored.`；
    - 非 git 目录，写成 `Not a git repository: end-of-turn checks are disabled.`。
  - turn-start 摘要：只在 Codex 上输出，内容为 `FDG auto-formatted at the end of the last turn: src/A.cs (lines 12-30, 41-44); src/B.cs (lines 3-9). Re-read those regions before editing them.`。输出后清空 `autofix.json`。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact] public void No_package_reference_anywhere_returns_null()
[Fact] public void Profiles_are_collapsed_by_directory()           // src/A, src/B = application; tests/T = tests → "src/** = application, tests/** = tests"
[Fact] public void Mixed_profiles_in_same_directory_are_listed_per_project()
[Fact] public void Lang_version_and_frameworks_come_from_markers()
[Fact] public void Missing_profile_adds_setup_issue_line()
[Fact] public void Never_built_adds_setup_issue_line()
[Fact] public void Protocol_mismatch_suggests_tool_update()
[Fact] public void Legacy_projects_are_counted_once()
[Fact] public void Non_git_directory_adds_disabled_line()
[Fact] public void Output_stays_under_200_tokens_for_pilot_shaped_repo()   // 24 个项目、2 个 profile → 字符数 < 900
[Fact] public void Turn_start_summary_only_for_codex()
[Fact] public void Turn_start_summary_is_cleared_after_emit()
```

- [ ] **Step 2: 运行，确认失败**
  Run: `dotnet test tests/Fdg.Cli.Tests --filter "FullyQualifiedName~SessionContextTests|FullyQualifiedName~TurnStartSummaryTests"`
  Expected: FAIL
- [ ] **Step 3: 实现**
- [ ] **Step 4: 运行，确认通过**
  Run: `dotnet test tests/Fdg.Cli.Tests`
  Expected: PASS
- [ ] **Step 5: Commit**
  `git add src/Fdg.Cli tests/Fdg.Cli.Tests && git commit -m "feat: add session start context and self-check"`

---

### Task 9: 四个 skill 与评审子代理

**Files:**
- Create: `plugins/framework-design-guidelines/skills/fdg-write/SKILL.md`, `skills/fdg-setup/SKILL.md`, `skills/fdg-fix/SKILL.md`, `skills/fdg-review/SKILL.md`, `agents/fdg-reviewer.md`
- Test: `tests/Fdg.Generator.Tests/SkillLintTests.cs`

**Interfaces:**
- Consumes: Task 3 生成的 `references/`；Task 6 的 `dotnet fdg fix`
- Produces: skill 名称 `fdg-write`、`fdg-setup`、`fdg-fix`、`fdg-review`；子代理名称 `fdg-reviewer`

**内容要求**（全部英文，正文照设计文档对应小节）：
- **fdg-write**：
  - frontmatter 包含 `name`、`description`、`paths: "**/*.cs"`。
  - description 写得主动一些，要覆盖这些关键词：C#、.NET、class、interface、struct、enum、member、method、property、public API、library、naming、exception、writing or modifying C# code。
  - 正文最前面是第 3 章和附录 A 中最重要的判断类规则，最多 40 条，每条带编号和原文，按重要性排序：
    - 先放命名语义（3.2–3.6）；
    - 再放 A.1.6 中工具查不出的条目。
  - 然后是"何时读取哪个参考文件"的对照表。
  - 再然后是一条说明：项目的语言版本以会话注入为准，遇到 `[C# N+]` 标记时，版本不满足就使用 alt 写法。
- **fdg-setup**：
  - frontmatter 加 `disable-model-invocation: true`，Codex 会忽略这个字段；
  - 按设计文档 §5.9 的四步执行，结尾提示 `dotnet tool install -g dotnet-fdg`；
  - 明确写出：只修改工作区，绝不提交；修改前列出将要改动的文件，等用户确认后再改；
  - 中央包管理：存在 `Directory.Packages.props` 时，`PackageVersion` 写在其中，`PackageReference` 不带版本；
  - `PrivateAssets="all"`。
  - `library` 档询问是否启用 PublicApiAnalyzers，默认不启用。
- **fdg-fix**：按设计文档 §5.9 的五步执行。
  - 路径取 `$ARGUMENTS`；在 Codex 上，从用户消息中读取路径。
  - 剩余问题的收集方式：执行 `dotnet build <proj> --no-restore -p:FdgSarifDir=<临时目录>`，读取 SARIF，只保留 `rules.json` 中 `registeredDiagnostics` 列出的规则以及 CS 错误。`rules.json` 的路径从 `obj/fdg/marker.json` 读取。
  - 公开 API 的破坏性修改先列清单，等用户确认后再改。
- **fdg-review**：只给意见，不改代码。
  - 不带路径时审查 `git diff` 加上未跟踪的 `.cs` 文件；
  - 输出格式固定为：`<file>:<line> — FDG#### <STRENGTH> <原文>` 加一句理由；
  - 不编造没有依据的建议；结束时询问是否应用其中某几条；
  - 在 Claude 上委派给 `fdg-reviewer` 子代理；在 Codex 上直接在主会话中执行。
- **fdg-reviewer.md**：frontmatter 包含 `name`、`description`、`tools: Read, Grep, Glob, Bash`；读取 `fdg-write/references/`；输出格式同上。

- [ ] **Step 1: 写失败测试 `SkillLintTests`**

```csharp
[Fact] public void Every_skill_has_name_matching_directory_and_description()
[Fact] public void Skill_md_is_at_most_500_lines()
[Fact] public void References_are_linked_one_level_deep_and_exist()
[Fact] public void Every_rule_id_mentioned_in_skills_exists_in_rules()
[Fact] public void Fdg_write_has_paths_cs_glob()
[Fact] public void Command_skills_disable_model_invocation()
```

- [ ] **Step 2: 运行，确认失败**
  Run: `dotnet test tests/Fdg.Generator.Tests --filter SkillLintTests`
  Expected: FAIL
- [ ] **Step 3: 编写四个 SKILL.md 和 fdg-reviewer.md**
- [ ] **Step 4: 运行，确认通过**
  Run: `dotnet test tests/Fdg.Generator.Tests --filter SkillLintTests`
  Expected: PASS
- [ ] **Step 5: Commit**
  `git add plugins tests/Fdg.Generator.Tests && git commit -m "feat: add fdg skills and reviewer agent"`

---

### Task 10: 插件清单、hooks 外壳与 marketplace

**Files:**
- Create: `plugins/framework-design-guidelines/.claude-plugin/plugin.json`, `.codex-plugin/plugin.json`, `hooks/hooks.json`, `hooks/fdg-hook.sh`, `.claude-plugin/marketplace.json`
- Test: `tests/Fdg.Cli.Tests/HookShellTests.cs`（只在有 bash 的环境运行）

**Interfaces:**
- Consumes: Task 7 的 `dotnet fdg hook <event>`；Task 7 Step 1 写下的 `docs/hosts.md`
- Produces：
  - `hooks.json` 的三个事件：
    - `SessionStart` 不设 matcher，压缩后同样会触发；
    - `UserPromptSubmit` 调用 `turn-start`；
    - `Stop` 调用 `turn-stop`。
  - 命令形式为 `bash "${CLAUDE_PLUGIN_ROOT}/hooks/fdg-hook.sh" <event>`。Codex 的插件根目录变量和 shell 以 `docs/hosts.md` 的实测为准。
    - 如果 Codex 在 Windows 上不能执行 bash：在 `.codex-plugin/plugin.json` 中指向 `hooks/hooks.codex.json`，改用 `pwsh -NoProfile -File fdg-hook.ps1`，脚本行为与 sh 版本相同。
  - 超时：Stop 为 90 秒，比内部的 60 秒更长；其余两个事件为 15 秒。字段名按 `docs/hosts.md`。
  - `fdg-hook.sh` 的行为：
    - 找不到 `dotnet`，或者 `dotnet fdg --version` 失败时，输出 `{"systemMessage":"FDG: dotnet-fdg is not installed. Run: dotnet tool install -g dotnet-fdg"}`，退出码 0；
    - 否则把 stdin 原样传给 `dotnet fdg hook "$1"`。
  - marketplace.json：名称 `framework-design-guidelines`，其中一个插件，`source` 为 `./plugins/framework-design-guidelines`。
  - 两份 plugin.json 中都有 `name`、`version`（同步 0.1.0）、`description`、`author`、`license: MIT`、`repository`、`keywords`。各自的字段以当前官方文档为准。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact] public void Missing_tool_prints_install_hint_json_and_exits_zero()   // 用只含 bash 内置命令的 PATH 运行
[Fact] public void Stdin_is_forwarded_to_dotnet_fdg()                       // PATH 中放一个假 dotnet，把 stdin 回显出来
```

  另在 `CheckCommandTests` 中加 `Versions_in_manifests_match_directory_build_props`：两份 plugin.json 和 marketplace.json 的 version 与 `<Version>` 不一致时报错；`check` 命令执行这项检查。

- [ ] **Step 2: 运行，确认失败**
  Run: `dotnet test tests/Fdg.Cli.Tests --filter HookShellTests`
  Expected: FAIL
- [ ] **Step 3: 编写清单、hooks.json 和外壳脚本**
- [ ] **Step 4: 运行测试和校验**
  Run:
  - `dotnet test tests/Fdg.Cli.Tests --filter HookShellTests`
  - `claude plugin validate --strict plugins/framework-design-guidelines`
  - `claude plugin validate --strict .`

  Expected: PASS，校验无错误。
- [ ] **Step 5: 两端实装验证**
  1. 打包 CLI：`dotnet pack src/Fdg.Cli -o artifacts/packages`；
  2. 安装：`dotnet tool install -g dotnet-fdg --add-source artifacts/packages --version 0.1.0`；
  3. Claude 端：`claude plugin marketplace add D:/Repos/framework-design-guidelines`，然后安装插件；Codex 端按其文档从本仓库路径安装；
  4. 在 Task 5 的临时项目中各开一次会话。

  确认以下几点，并把结果写进 `docs/hosts.md`：
  - SessionStart 注入出现；
  - 卸载 CLI 后出现安装提示；
  - 用户命令的实际调用形式，例如 `/framework-design-guidelines:fdg-fix` 和 `$fdg-fix`。
- [ ] **Step 6: Commit**
  `git add plugins .claude-plugin tests/Fdg.Cli.Tests docs/hosts.md && git commit -m "feat: add plugin manifests, hooks and marketplace"`

---

### Task 11: CI

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: 前面所有任务的命令

- [ ] **Step 1: 编写工作流**
  在 `windows-latest` 和 `ubuntu-latest` 上运行：
  1. `actions/setup-dotnet`，使用 global.json；
  2. `dotnet build -c Release`；
  3. `dotnet test -c Release`，包括 Integration；
  4. `dotnet run --project src/Fdg.Generator -- generate`，然后 `git diff --exit-code`，防止生成物漂移；
  5. `dotnet run --project src/Fdg.Generator -- check`（CI 没有原书，不带 `--book`）；
  6. `npm i -g @anthropic-ai/claude-code`，然后 `claude plugin validate --strict .`。
- [ ] **Step 2: 本地验证**
  Run: 依次运行上面 2–5 步的命令
  Expected: 全部退出码 0，`git diff --exit-code` 无输出。
- [ ] **Step 3: Commit**
  `git add .github && git commit -m "ci: build, test, generator drift and plugin validation"`

---

### Task 12: 试点验收（试点仓库的本地克隆）

**Files:** 本仓库只新增 `docs/pilot-2026-10.md`（验收记录）

**约束：**
- 原始仓库 `<pilot-repo>` 不做任何写操作；
- 所有试验都在本地克隆中进行，不创建分支、不提交，克隆时 `--branch` 只检出已有分支；
- 开始前向用户确认以下三件事：
  - 克隆路径，默认 `<pilot-clone>`；
  - 本机全局安装 `dotnet-fdg`；
  - 两端各进行一次真实 AI 会话（按量计费）。

- [ ] **Step 1: 建克隆**
  Run: `git clone --local --branch <pilot-branch> <pilot-repo> <pilot-clone>`
  Expected: 克隆成功，`git status` 干净。
- [ ] **Step 2: 在 Claude 会话中运行 `/fdg-setup`**
  - 本地包源通过克隆中 `Directory.Build.props` 的 `RestoreAdditionalProjectSources` 指向 `D:/Repos/framework-design-guidelines/artifacts/packages`。这是试点专用的临时设置，在验收记录中注明。
  - 验收：`src/**` 为 application，`tests/**` 为 tests；`<legacy-dir>/` 下的老代码没有改动；构建成功，并出现预期的 FDG、CA、IDE 警告；`git status` 只显示配置文件的改动。
- [ ] **Step 3: 测量耗时**
  - 记录开启全部规则后，单文件 Stop 流程的耗时（format 加增量构建），预热后测 5 次，取中位数，记入验收记录；
  - 超过 30 秒时，在验收记录中提出常驻进程方案（设计文档 §5.4），不在本计划中实现。
- [ ] **Step 4: 在一个 src 项目中埋违规**
  - 违规内容：单行 catch、9 个位置参数的长调用、一行多条语句、命名违规；
  - 运行 `dotnet fdg fix src`；
  - 验收：排版和风格违规被修复，命名违规作为警告保留，老项目跳过且输出 `skipped`。
- [ ] **Step 5: 两端真实会话冒烟**
  - 让 AI "add a method that logs a failed upload with 9 details"；
  - 验收：
    - SessionStart 注入内容正确；
    - Stop 自动修复了排版；
    - 剩余问题按 Task 7 的报告格式打回，最多 2 次；
    - Codex 下一轮开始时出现自动修复摘要。
- [ ] **Step 6: 记录验收结果**
  - 把每条验收标准的结果和遇到的问题写进 `docs/pilot-2026-10.md`；
  - 根据暴露的问题，列出要调整的规则格式和流程，作为第 2 阶段计划的输入；
  - 告诉用户克隆路径，由用户决定保留还是删除。
- [ ] **Step 7: Commit（本仓库）**
  `git add docs/pilot-2026-10.md && git commit -m "docs: record vertical slice pilot results"`
