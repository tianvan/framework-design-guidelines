# FDG 第二期：逐章扩展到全书 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让第 2、4–9 章和附录 D 的 672 条准则以 `verified` 状态进入规则数据，通过 SDK 规则、自写分析器和 fdg-write/fdg-review 的参考文件，让 AI 写 C# 时遵守全书准则。

**Architecture:** 沿用第一期的链路：epub → 抽取器 → `rules/*.yaml` → 生成器 → 分析器包 / `rules.json` / skill 参考文件 / 文档。先修好抽取器和生成器的前置问题（Task 1–9），再搭噪音样本工具，并完成 Stop 的破坏性诊断处理（Task 10–12）。之后按书的顺序逐章完成映射、交叉核对、自写分析器和噪音记录（Task 13–24），最后登记非规则内容、重写 skill 并全书复核（Task 25–27）。

**Tech Stack:** .NET SDK 10.0.402（global.json）；分析器 netstandard2.0 + Microsoft.CodeAnalysis.CSharp 4.8.0；CLI net8.0（本期新增对 Microsoft.CodeAnalysis.CSharp 4.8.0 的引用，只做语法分析）；生成器 net10.0 + AngleSharp + YamlDotNet + JsonSchema.Net；测试 xUnit 2.9 + Microsoft.CodeAnalysis.CSharp.CodeFix.Testing 1.1.2；噪音样本脚本用 PowerShell 7。

**Spec:** `docs/superpowers/specs/2026-10-23-phase-2-chapter-expansion-design.md`（以下简称“本期设计”，§ 编号指它）。上位文档 `docs/superpowers/specs/2026-10-21-framework-design-guidelines-design.md`（简称“总设计”）仍然有效。

## Global Constraints

- 第一期计划的 Global Constraints 全部继续有效：只改工作树文件；epub 只读，读取它的 Python 一律 `python -I`；`private-sources/` 永不提交；所有工具输出和 skill 正文用英文；外部进程用 `ArgumentList` 传参；严重级别不使用 `error`；自写分析器 `isEnabledByDefault: false`；对公开 API 改名永不自动修复。
- 原书路径：`private-sources/fdg3-pilot/data/ex/OEBPS/`（下文写作 `$BOOK`）。生成器命令统一写作 `fdg-gen <命令>`，即 `dotnet run --project src/Fdg.Generator -- <命令>`。
- 规则编号 `FDG{章号标记}{三位序号}`：第 2 章 `FDG2001`…，附录 D `FDGD001`…；诊断编号与规则编号相同，永不复用。
- 规则原文字段（id、strength、text、context、section、anchor、body）只由抽取器写入，AI 不得改写。
- 映射以固定 SDK 10.0.402 的 `fdg-gen sdk-catalog` 为准；`check` 必须通过。
- profile 默认值（§4）：library 和 application 全部启用；只对公开 API 有意义的规则只用于 library（application 和 test 设为 `none`）；test 只启用第 7、8 章和附录 A，其余章节的 test 设为 `none`，除非在 notes 中写明 `Test profile: <理由>`。
- 参考文件（`references/` 下每个 `.md`，包括 `examples/`）不超过 32768 字节；fdg-write 的 SKILL.md 不超过 500 行。
- 自写分析器门槛（§5）：能用语法或符号信息直接判定；在噪音样本上抽查误报不超过 1/20；有 CodeFix 时不改公开 API 的签名或名称，并支持 FixAll。每个分析器都要有正反例测试、C# 7.3 测试、CodeFix 测试（如果有），帮助页由生成器产生。
- “误报”的定义（§7）：报告的位置实际符合书中原文（含原文写明的例外）。不规范代码产生的真实警告不算误报，但单独计数。
- 试点克隆 `D:\Repos\fdg-pilot\`：只构建、统计和抽查。不做任何 git 操作，不修改源文件。写进仓库的结果全部匿名，不出现项目、命名空间或路径名。
- 不修改 `D:\DigitalTeachingPlatform-PC` 和 `D:\Repos\DigitalTeachingPlatform-PC`。
- 交叉核对代理由控制者派发，实施者不派发子代理。规则只有在交叉核对同意后才能改为 `verified`。
- 推送到远端前必须询问用户（第一期的推送授权不延续到本期）。
- `docs/phase-2-backlog.md` 中 “Hooks and CLI” 和 “Before the first release” 两节不在本期范围内，例外见 §2。

## Review Focus

1. **抽取器改动悄悄改变第一期已核对的原文**：重新抽取第 3 章和附录 A 时，除了修正压平和连字之外，任何字段都不能变化。`extract --update` 要逐条列出变化的规则，Task 1 和 Task 2 的回归步骤逐条判定。测试在 Task 1（`Update_keeps_mapping_and_reports_changed_ids`）。
2. **IDE1006 在代码无法解析或位置对不上时引用错误的规则**：例如文件已被删除、行号越界、列号指向空白、partial 声明或顶层语句。这些情况必须回退到“不引用规则”，不能猜。测试在 Task 7（`Unreadable_location_quotes_no_rule`）。
3. **Stop 把本轮新增的声明误判为已有 API**：新文件、重命名的类型，以及同名但种类不同的符号（字段改成属性）都必须按新增处理，照常打回。测试在 Task 11。
4. **参考文件拆分配置漏掉或重复覆盖某一节**：例如节号前缀 `5.1` 误匹配 `5.10`。生成器必须报错，不能静默丢掉规则。测试在 Task 5（`Section_prefix_matches_on_dot_boundary`）。
5. **噪音样本仓库自身的 `.editorconfig` 或 `TreatWarningsAsErrors` 压过或放大我们的配置**：统计结果要反映用户实际看到的情况，同时构建不能因为警告而失败。脚本固定传 `-p:TreatWarningsAsErrors=false`，并在记录中注明样本仓库有没有覆盖我们的配置。测试在 Task 10 的手工验证步骤。

---

## 文件结构

| 文件 | 职责 | 任务 |
|---|---|---|
| `src/Fdg.Generator/Book/BlockRenderer.cs` | 行内强调、链接、`<br>` 和 sidebar 中的列表、表格按 Markdown 保留 | 1 |
| `src/Fdg.Generator/Commands/ExtractCommand.cs` | 新增 `--update`：更新原文字段，保留映射 | 1 |
| `src/Fdg.Generator/Book/Normalizer.cs` | 新增 `Ligatures` | 2 |
| `src/Fdg.Generator/Commands/ToolErrors.cs`（新） | 维护工具的一行错误输出 | 3 |
| `src/Fdg.Generator/Emit/GeneratedFiles.cs` | 删除过期的 `references/` 文件 | 4 |
| `src/Fdg.Generator/Rules/RuleLoader.cs`、`Emit/SkillReferenceEmitter.cs` | `meta.yaml` 的 `references` 拆分配置，以及大小上限 | 5 |
| `src/Fdg.Generator/Rules/MappingValidator.cs`、`RuleLoader.cs` | `meta.yaml` 的 `testProfile`，以及 profile 默认值校验 | 6 |
| `rules/rules.schema.json`、`Rules/RuleMapping.cs`、`Rules/RuleYaml.cs`、`Emit/RulesJsonEmitter.cs` | 可选字段 `naming` | 7 |
| `src/Fdg.Cli/Hooks/SarifReader.cs`、`Hooks/StopReport.cs`、`Rules/RulesManifest.cs`、`Hooks/DeclarationLocator.cs`（新） | 按符号种类为 IDE1006 选规则 | 7 |
| `src/FrameworkDesignGuidelines.Analyzers/Layout/LongListWrappingAnalyzer.cs` 或 `src/Fdg.Cli/Hooks/SessionContext.cs` | FDGA023 的范围 | 8 |
| `src/Fdg.Cli/Hooks/SelfCheck.cs` | SDK 低于 10.0.100 的提示 | 9 |
| `eng/noise/Measure-Noise.ps1`、`eng/noise/Inject.props`、`eng/noise/Inject.targets`、`eng/noise/samples.json`（均为新文件） | 噪音样本：注入包、构建、按诊断计数 | 10 |
| `docs/noise-samples.md`（新） | 噪音记录 | 10、13–24 |
| `src/Fdg.Cli/Hooks/ExistingApi.cs`（新）、`Hooks/HookRunner.cs`、`Hooks/StopReport.cs` | 已有声明上的破坏性诊断不打回 | 11 |
| `src/FrameworkDesignGuidelines.Analyzers/Naming/*`（新） | 第 3 章低成本检查 | 12 |
| `rules/ch02-fundamentals.yaml` … `rules/appD-breaking-changes.yaml`（新） | 各章规则 | 13–24 |
| `src/FrameworkDesignGuidelines.Analyzers/<主题>/*` | 各章通过门槛的自写分析器 | 13–24 |
| `docs/phase-2-spot-check.md`（新） | 给用户的抽查清单 | 13–24、27 |
| `src/Fdg.Generator/Emit/CoverageEmitter.cs`、`RuleLoader.cs` | `meta.yaml` 的 `otherContent`：前言、第 1 章、附录 B 和 C 等 | 25 |
| `plugins/framework-design-guidelines/skills/fdg-write/SKILL.md`、`fdg-review/SKILL.md`、`agents/fdg-reviewer.md`、`src/Fdg.Cli/Hooks/SessionContext.cs` | 路由表、重点规则、评审按主题读取、会话提示 | 26 |
| `tests/Fdg.Generator.Tests/SkillLintTests.cs` | 新的上限和路由表校验 | 5、26 |

---

## 逐章流程（Task 13–24 共用）

每一轮都按下面的步骤执行。每个章节任务只写出本轮的参数：章号标记、文件、规则文件、小节范围、条目数、test profile、参考文件拆分和特别说明。

**R1. 登记章节**（只在该章的第一轮执行）：在 `rules/meta.yaml` 的 `chapters` 中按书的顺序加入条目，包括 `token`、`file`、`rules`、`title`、`testProfile`（Task 6），需要拆分时还有 `references`（Task 5）。

**R2. 抽取**（只在该章的第一轮执行）：运行 `fdg-gen extract --book $BOOK --chapter <token>`。
- 预期输出：`Wrote <N> rules (<N> icons)`，N 等于任务中给出的条目数。
- 检查骨架：sidebar 中的列表和表格完整，跨多个代码块的示例没有被截断。

**R3. 映射**：对本轮小节范围内的每条规则，写入映射字段，`status` 保持 `draft`。
- `check`：
  - 有对应的 SDK 诊断时为 `sdk`，诊断编号必须出现在 `fdg-gen sdk-catalog` 的输出中；
  - 能机器检查但 SDK 没有对应诊断时，先设为 `judgment`，并在 notes 中写一行 `Analyzer candidate: <判定方法>`；
  - 其余为 `judgment`。
  - `coverage` 按诊断实际能查出的范围填写 `full`、`partial` 或 `none`。
- `autofix`：
  - 会改变公开 API 的签名或名称时为 `breaking`；
  - SDK 或 CodeFix 能安全修复时为 `yes`；
  - 否则为 `no`。
- `severity`：
  - 默认 DO、DO NOT、AVOID 为 `warning`，CONSIDER 为 `suggestion`；
  - 只对公开 API 有意义的规则：application 和 test 设为 `none`；
  - test 按本章的 `testProfile` 设置。
- `config`：SDK 诊断需要的 `.editorconfig` 选项。
- `minLanguage` 和 `alternative`：规则依赖某个语言版本时填写，并给出低版本下的写法。
- `notes`：例外条件、映射收窄的原因、对其他规则的引用。

**R4. 自动校验**：
- `fdg-gen check --book $BOOK` 通过，包括条目数、Schema、原文一致和诊断存在；
- `fdg-gen generate` 后 `dotnet test` 全部通过。

**R5. 交叉核对**：
- 控制者派发一个独立代理，输入为：本轮的规则文件片段、`$BOOK` 中对应的 xhtml、`sdk-catalog` 输出、本期设计 §4–§5，以及本计划的 Global Constraints。
- 该代理逐条对照原书核对以下内容，并写入 `<工作区>/cross-check-<轮次>.md`，每条一行，格式为 `<id> AGREE` 或 `<id> DISAGREE: <理由>`：
  - 映射、profile、例外条件；
  - autofix 是否会破坏 API；
  - body 是否完整，尤其是跨多个代码块或带批注的条目；
  - 是否有漏掉的 SDK 诊断。
- 实施者处理所有 DISAGREE：修改映射；如果坚持原映射，在 notes 中写明理由，交给同一代理复核。
- 最后只把 AGREE 的规则改为 `verified`。本轮结束时，范围内的规则必须全部是 `verified`。

**R6. 自写分析器**：对本轮每个 `Analyzer candidate` 按门槛判断。
- 满足门槛 1 和门槛 3 的，在 `src/FrameworkDesignGuidelines.Analyzers/<主题>/` 实现，诊断编号即规则编号。
  - 可选的 CodeFix 放在 `src/FrameworkDesignGuidelines.CodeFixes/<主题>/`。
  - 测试放在 `tests/FrameworkDesignGuidelines.Analyzers.Tests/<主题>/`，包括正例、反例、`LanguageVersion.CSharp7_3` 用例，以及 CodeFix 和 FixAll 用例。
- 规则改为 `check.kind: fdg`，`diagnostics: [<规则编号>]`，并填写 `analyzer: {message, category}`。
- 再按 R7 测误报：
  - 超过门槛的：删除分析器代码，恢复为 `judgment`，notes 改为 `Analyzer candidate rejected: <原因和数字>`；
  - 不满足门槛 1 或门槛 3 的：同样写明原因。

**R7. 噪音记录**：
- 用 Task 10 的脚本，在 `eng/noise/samples.json` 列出的全部样本上运行，统计本轮新映射的 SDK 诊断和新分析器的诊断。
- 每个诊断最多抽查 20 处，抽查时对照原文判定是否误报。
- 在 `docs/noise-samples.md` 中追加本轮一节，表格每行一个诊断，列为：诊断、规则、各样本的计数、抽查数、误报数、真实警告数、处理。
- 误报超过 1/20 的 SDK 规则：把各 profile 降为 `suggestion`，或者收窄映射（改 coverage 或 config），并在 notes 中写明。

**R8. 非准则内容**：本轮范围内有叙述性内容的小节，在该章 `nonGuideline` 中逐节登记 `background`、`rationale` 或 `obsolete`，每条写明去向。

**R9. 抽查清单**：
- 在 `docs/phase-2-spot-check.md` 追加本轮一节，约 10 条（拆分章节按轮次分配，每章合计约 10 条）。
- 优先选择：判断最难的映射、带例外的规则、breaking 规则、新分析器对应的规则。
- 每条一行：`- [ ] <id> <STRENGTH> <text>`，下一行缩进写映射摘要和选择它的原因。

**R10. 收尾**：`fdg-gen generate`，`dotnet test` 全部通过，`fdg-gen check --book $BOOK` 通过，然后提交：

```bash
git add rules/ docs/ src/ tests/ plugins/
git commit -m "feat(rules): map chapter <N> <轮次范围> and verify it against the book"
```

---
## 前置工作

### Task 1: 抽取器保真与 `extract --update`

**Files:**
- Modify: `src/Fdg.Generator/Book/BlockRenderer.cs`（`Inline`、`Annotation`）
- Modify: `src/Fdg.Generator/Commands/ExtractCommand.cs`
- Create: `tests/fixtures/book/fidelity.xhtml`
- Test: `tests/Fdg.Generator.Tests/BookParserTests.cs`、`tests/Fdg.Generator.Tests/GeneratorCliTests.cs`
- Modify（回归后）：`rules/ch03-naming.yaml`、`rules/appA-coding-style.yaml` 及生成文件

**Interfaces:**
- Produces:
  - `extract --book <dir> --chapter <token> --update`：重新抽取已存在的规则文件，只替换原文字段，映射字段原样保留。输出 `Updated <k> of <N> rules: <id>, <id>, ...`，其中 k 为原文字段有变化的条目数。
  - 规则文件不存在，或新旧条目数、任一编号的 strength 不一致时，退出码为 1，并输出一行说明。成功时退出码为 0。
  - 不带 `--update` 时行为不变：已存在的文件仍然拒绝覆盖。
- 渲染约定（我的决定，Task 2 及之后的所有章节都依赖它）：
  - `em`、`i` 渲染为 `*文本*`。
  - `strong`、`b` 渲染为 `**文本**`。规则开头的强度词仍由 BookParser 单独去掉。
  - `code` 渲染为反引号（不变）。`code` 内部的强调不加标记。
  - 外链 `a[href^="http"]` 渲染为 `[文本](href)`。其他链接（书内交叉引用）只保留文本。
  - `br` 渲染为 `<br>`，段落和表格单元格中都一样。GFM 在这两处都认 `<br>`，`Normalizer.Collapse` 也不会吞掉它。
  - sidebar 中的 `ul`、`ol`、`table` 用现有的 `List`、`Table` 渲染逻辑，与相邻段落之间空一行，仍然放在同一个 `annotation` 块中。

- [ ] **Step 1: 写测试夹具和失败的测试**

`fidelity.xhtml` 包含一条规则：
- 正文段落中有 `<em>`、`<strong>`、`<code>`，外链和书内链接各一个，以及一个 `<br/>`。
- 其后跟一个 sidebar，内容依次为 `sb-title`、一段文字、`ul`（两项）、`ol`（两项）、一个 2×2 的 `table`。

在 `BookParserTests` 中新增：

```csharp
[Fact] public void Inline_emphasis_strong_and_external_links_are_kept_as_markdown()
// body[0].Text == "Use *this* and **that** with `Foo` per [the spec](https://example.com/spec), see section 3.1.<br>Next line."
[Fact] public void Internal_links_keep_only_their_text()
[Fact] public void Sidebar_lists_and_tables_are_rendered_inside_the_annotation()
// annotation.Text 依次包含 "- first"、"- second"、"1. one"、"2. two"、"| A | B |"、"| --- | --- |"，块之间以 "\n\n" 分隔
```

在 `GeneratorCliTests` 中新增：

```csharp
[Fact] public void Update_keeps_mapping_and_reports_changed_ids()
// 抽取 fidelity.xhtml，把第 1 条的 status 改为 verified、notes 改为 "keep"，body 改成旧的压平文本；再运行 --update：
// 退出码 0；stdout 含 "Updated 1 of" 和该编号；status 和 notes 不变；body 等于新的渲染结果
[Fact] public void Update_refuses_when_rule_count_differs()   // 退出码 1，stderr 只有一行
[Fact] public void Extract_without_update_still_refuses_to_overwrite()
```

- [ ] **Step 2: 运行，确认失败**

运行：`dotnet test tests/Fdg.Generator.Tests --filter "FullyQualifiedName~BookParserTests|FullyQualifiedName~GeneratorCliTests"`
预期：新增的 5 个测试失败。

- [ ] **Step 3: 实现渲染约定和 `--update`**

`--update` 的做法：
- 用 `RuleLoader.LoadRules` 读取旧文件，按编号与新抽取的规则配对。
- 用 `Rule` 的 `with` 表达式保留旧的 `Mapping`，原文字段取新值。
- 用 `RuleYaml.Serialize` 写回，编码为无 BOM 的 UTF-8。

- [ ] **Step 4: 运行，确认通过**

运行：`dotnet test tests/Fdg.Generator.Tests`
预期：全部通过。

- [ ] **Step 5: 在第一期的两章上回归**

运行：
- `fdg-gen extract --book $BOOK --chapter 3 --update`
- `fdg-gen extract --book $BOOK --chapter A --update`
- `git diff rules/`

对输出中列出的每个编号，在实施报告中写一行：`<id>: <字段> — <修正了哪种压平>`。

判定标准：
- 只允许以下几类变化：强调、链接、`<br>`，以及 sidebar 中的列表或表格从压平改为 Markdown。
- 出现任何其他变化，就停下来修复渲染。
- 映射字段不得有任何 diff。

- [ ] **Step 6: 第 4–9 章骨架检查（不提交）**

做法：
1. 把 `rules/` 复制到临时目录。
2. 在临时的 `meta.yaml` 中加入第 2、4–9 章和附录 D，文件名按 Task 13–24 的写法。
3. 对每章运行 `fdg-gen extract --book $BOOK --chapter <t> --rules <临时目录>`。
4. 用 `python -I` 统计每章的规则数。
5. 每章抽看 3 条含 `list` 或 `table` 块的规则，确认列表和表格完整。
6. 把结果写进实施报告，然后删除临时目录。

预期条目数：

| 章节 | 2 | 4 | 5 | 6 | 7 | 8 | 9 | D |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 条目数 | 32 | 70 | 133 | 22 | 70 | 107 | 175 | 63 |

- [ ] **Step 7: 生成并提交**

运行 `fdg-gen generate`，再运行 `dotnet test`，确认全部通过后提交：

```bash
git add src/Fdg.Generator tests/Fdg.Generator.Tests tests/fixtures/book rules docs plugins src/FrameworkDesignGuidelines.Package
git commit -m "feat(gen): keep inline markup and sidebar lists as markdown; add extract --update"
```

### Task 2: 连字残留修复

**Files:**
- Modify: `src/Fdg.Generator/Book/Normalizer.cs`，以及调用 `Normalizer.Glyphs` 的地方
- Test: `tests/Fdg.Generator.Tests/NormalizerTests.cs`（新建）

**Interfaces:**
- Produces: `public static string Ligatures(string text)`。所有经过 `Glyphs` 的文本也都经过它，包括标题，即 `section` 字段。

决定（我的）：
- 2026-10-23 逐个扫描了全书的 xhtml，被拆开的 fi、fl、ff 连字只有一处：“Conf licts”。它出现在 ch03 的 3.4.1 标题和 toc 中。
- 通用的启发式规则会把 “Krzysztof in”、“off the” 这类真实词组误合并，所以改用显式的已知拆分表：

```csharp
// Split ligatures found by scanning the whole book (2026-10-23); a heuristic would join real words such as "off the".
private static readonly (string Split, string Joined)[] SplitLigatures = [("Conf licts", "Conflicts")];
```

- [ ] **Step 1: 写失败的测试**

```csharp
[Theory]
[InlineData("3.4.1 Namespaces and Type Name Conf licts", "3.4.1 Namespaces and Type Name Conflicts")]
public void Ligatures_rejoins_known_split_words(string input, string expected)

[Theory]
[InlineData("Krzysztof in")] [InlineData("turn it off the")] [InlineData("stuff like")] [InlineData("diff view")]
public void Ligatures_leaves_real_words_alone(string input) // 输出 == 输入
```

- [ ] **Step 2: 运行，确认失败**

运行：`dotnet test tests/Fdg.Generator.Tests --filter "FullyQualifiedName~Ligatures"`
预期：失败，因为 `Ligatures` 方法还不存在。

- [ ] **Step 3: 实现**

在所有调用 `Glyphs` 的地方之后，紧接着调用 `Ligatures`。

- [ ] **Step 4: 全书回归**

运行：
- `fdg-gen extract --book $BOOK --chapter 3 --update`
- `fdg-gen extract --book $BOOK --chapter A --update`

预期：
- 第 3 章只有 3.4.1 节下的规则发生变化，而且只是 `section` 字段变为 “…Conflicts”。
- 附录 A 输出 `Updated 0 of 71 rules`。

再重复一次 Task 1 Step 6 的临时骨架抽取，与 Task 1 的结果做 diff。预期差异为零，因为其他章节没有这处拆分。

- [ ] **Step 5: 生成、测试、提交**

```bash
git add -A src/Fdg.Generator tests/Fdg.Generator.Tests rules docs plugins
git commit -m "fix(gen): rejoin the split ligature in 'Conflicts'"
```

### Task 3: 维护工具的一行错误输出

**Files:**
- Create: `src/Fdg.Generator/Commands/ToolErrors.cs`
- Modify: `src/Fdg.Generator/Commands/ExtractCommand.cs`、`VerifySourceCommand.cs`、`CheckCommand.cs`
- Test: `tests/Fdg.Generator.Tests/GeneratorCliTests.cs`

**Interfaces:**
- Produces:

```csharp
/// <summary>Runs a maintainer command and turns expected failures into one line on <paramref name="error"/>, exit code 2.</summary>
public static int Run(string command, TextWriter error, Func<int> body)
```

- 捕获以下异常：
  - `YamlException`、`FormatException`、`JsonException`、`BookFormatException`；
  - `IOException`、`UnauthorizedAccessException`；
  - `System.ComponentModel.Win32Exception`（找不到 `dotnet`）；
  - `InvalidOperationException`。
- 输出格式为 `<command> failed: <message>`。消息中的换行替换为空格，不打印栈追踪。
- 其他异常类型视为程序缺陷，照常抛出。

- [ ] **Step 1: 写失败的测试**

```csharp
[Theory]
[InlineData("check")] [InlineData("verify-source")] [InlineData("extract")]
public void Malformed_yaml_gives_one_line(string command)
// 规则文件内容为 "- id: [unclosed"；退出码 2；stderr.Trim() 不含 '\n'、不含 "   at "，并以 $"{command} failed: " 开头

[Fact] public void Check_with_a_missing_schema_gives_one_line()           // 删除临时 rules 目录中的 rules.schema.json
[Fact] public void Check_with_an_unusable_sdk_directory_gives_one_line()  // --sdk 指向一个空目录
```

- [ ] **Step 2: 运行，确认失败**

运行：`dotnet test tests/Fdg.Generator.Tests --filter "FullyQualifiedName~one_line"`

- [ ] **Step 3: 实现**

三个命令的 `Run` 主体都包在 `ToolErrors.Run` 里。命令中已有的、更具体的 catch 保持不变。

- [ ] **Step 4: 运行生成器的全部测试，确认通过，然后提交**

```bash
git commit -am "fix(gen): print one-line errors instead of stack traces in maintainer commands"
```

### Task 4: `generate` 删除过期的参考文件

**Files:**
- Modify: `src/Fdg.Generator/Emit/GeneratedFiles.cs`（`Write`）
- Test: `tests/Fdg.Generator.Tests/EmitterTests.cs`

**Interfaces:**
- Produces:
  - `GeneratedFiles.Write` 现在只删除过期的 `docs/rules/FDG*.md`。改动后，它还会递归删除 `plugins/framework-design-guidelines/skills/fdg-write/references/` 下所有不在本次生成集合中的 `*.md`。
  - 删除的文件计入返回值 changes。只删除 `.md` 文件。
  - CI 的 drift 步骤已经检查 `git status --porcelain`，过期文件被删除后会显示为 deleted，因此 CI 不用改。

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Write_removes_stale_reference_files()
// 先在临时根目录下写入 references/old.md、references/examples/old.md 和 references/keep.txt，再调用 Write(root, Create(input))：
// 两个 old.md 被删除；返回值 >= 2；keep.txt 仍然存在
```

- [ ] **Step 2: 运行，确认失败；实现；再运行，确认通过**

运行：`dotnet test tests/Fdg.Generator.Tests --filter "FullyQualifiedName~stale"`

- [ ] **Step 3: 提交**

```bash
git commit -am "fix(gen): remove stale skill reference files on generate"
```

### Task 5: 参考文件按主题拆分，并设大小上限

**Files:**
- Modify: `src/Fdg.Generator/Rules/RuleLoader.cs`（`ChapterMeta`、`ChapterDto`）
- Modify: `src/Fdg.Generator/Emit/SkillReferenceEmitter.cs`
- Test: `tests/Fdg.Generator.Tests/EmitterTests.cs`、`tests/Fdg.Generator.Tests/SkillLintTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ReferenceSplit(string File, IReadOnlyList<string> Sections);`
  - `ChapterMeta` 新增 `public IReadOnlyList<ReferenceSplit> References { get; init; }`，默认为空。

`meta.yaml` 中的写法：

```yaml
references:
  - file: ch05-members-general      # 生成 references/ch05-members-general.md 和 references/examples/ch05-members-general.md
    sections: ["5.1", "5.2", "5.3"]
```

规则如何归入文件：
- 取规则 `Section` 字段的第一个词，即节号，例如 `5.2.1`。
- 节号等于某个前缀，或者以“前缀 + `.`”开头，就归入该前缀所在的文件。
- 每条规则必须恰好归入一个文件。归不进或者归入两个时，抛出 `GenerationException`，例如：`Chapter 5: rule FDG5012 (section 5.10) matches no reference split.`

其他约定：
- `references` 为空时，行为与现在相同：一章一个文件，文件名为 `RuleText.FileStem(file)`。
- 拆分后，文件标题写章名加上本文件包含的小节，例如 `# Chapter 5: Member Design — 5.1–5.3`。
- 示例链接 `— examples: examples/<file>.md#<id>` 指向拆分后的文件。
- 大小上限为 `public const int MaxReferenceBytes = 32768;`。任何一个参考文件或 examples 文件按 UTF-8 编码超过上限时，抛出 `GenerationException`：`references/<file>.md is <n> bytes; the limit is 32768. Split the chapter with references in meta.yaml.`

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Splits_chapter_references_by_section_prefix()
// 两个拆分 ["9.1"] 和 ["9.2"]：共生成 4 个文件，每条规则只出现在对应的文件中
[Fact] public void Section_prefix_matches_on_dot_boundary()
// 拆分 ["5.1"] 不匹配 section "5.10 Foo"；抛出 GenerationException，消息含 "matches no reference split"
[Fact] public void Rule_matching_two_splits_fails()
[Fact] public void Oversized_reference_file_fails_generation()          // 一条 judgment 规则的 body 超过 32 KB
[Fact] public void Chapter_without_splits_keeps_one_file_per_chapter()   // ch03-naming.md 的路径不变
```

在 `SkillLintTests` 中新增 `Every_reference_file_is_at_most_32_KB`：直接检查仓库里生成好的 references 文件，作为生成器检查之外的第二道保险。

- [ ] **Step 2: 运行，确认失败；实现；再运行全部测试，确认通过**

运行 `fdg-gen generate` 后，现有两章的生成文件应该没有任何 diff。

`examples/ch03-naming.md` 目前为 26797 字节。如果 Task 1 的保真改动让它超过 32768 字节：
- 在 `meta.yaml` 中给第 3 章配置 `references`，拆成 `ch03-naming-general`（3.1–3.3）和 `ch03-naming-elements`（3.4–3.8）；
- 同步修改 fdg-write 和 fdg-review 中对 `ch03-naming.md` 的引用。

- [ ] **Step 3: 提交**

```bash
git commit -am "feat(gen): split skill references by section and cap each file at 32 KB"
```

### Task 6: 各章的 test profile 默认值与校验

**Files:**
- Modify: `src/Fdg.Generator/Rules/RuleLoader.cs`（`ChapterMeta`）
- Modify: `src/Fdg.Generator/Rules/MappingValidator.cs`，或 `CheckCommand` 中调用它的地方
- Modify: `rules/meta.yaml`：第 3 章设为 `testProfile: off`，附录 A 设为 `testProfile: on`
- Test: `tests/Fdg.Generator.Tests/CheckCommandTests.cs`

**Interfaces:**
- Produces:
  - `ChapterMeta` 新增 `public bool TestProfileOn { get; init; } = true;`。
  - 在 YAML 中的键为 `testProfile`，取值 `on` 或 `off`，省略时为 `on`。取其他值时抛出 `FormatException`：`meta.yaml: testProfile of chapter <t> must be on or off.`
  - `check` 新增一类错误：在 `testProfile: off` 的章节中，规则的 `severity.test` 不是 `none`，而且 notes 中没有 `Test profile:`。报告格式为：`<id>: chapter <t> is off in the test profile; set severity.test to none or explain it in notes with "Test profile:".`
  - `RuleMapping.CreateDefault(Strength strength, bool style, bool testProfileOn)`：`testProfileOn` 为 false 时，`severity.test` 为 `none`。抽取器按章节的 `TestProfileOn` 传值。这样，大章中尚未映射的 draft 规则也能通过 `check`。

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Test_severity_must_be_none_in_chapters_off_for_tests()
[Fact] public void Test_profile_exception_is_allowed_with_a_note()
[Fact] public void Test_profile_value_must_be_on_or_off()
[Fact] public void Extract_defaults_test_severity_to_none_in_chapters_off_for_tests()   // GeneratorCliTests
```

- [ ] **Step 2: 运行，确认失败；实现；再运行，确认通过**

然后运行 `fdg-gen check`，预期通过：第 3 章的规则目前在 test 下都是 `none`。如果有例外，按照报告补上 notes。

- [ ] **Step 3: 提交**

```bash
git commit -am "feat(gen): validate per-chapter test profile defaults"
```

### Task 7: IDE1006 按符号种类和失败类型选择规则

**Files:**
- Modify: `rules/rules.schema.json`、`src/Fdg.Generator/Rules/RuleMapping.cs`、`Rules/RuleYaml.cs`、`Emit/RulesJsonEmitter.cs`
- Modify: `rules/ch03-naming.yaml`、`rules/appA-coding-style.yaml`（按下表加 `naming`）
- Modify: `src/Fdg.Cli/Fdg.Cli.csproj`（加入 `Microsoft.CodeAnalysis.CSharp`，中央版本 4.8.0）
- Create: `src/Fdg.Cli/Hooks/DeclarationLocator.cs`
- Modify: `src/Fdg.Cli/Hooks/SarifReader.cs`、`Hooks/StopReport.cs`、`Hooks/HookRunner.cs`（`ToIssues`）、`Rules/RulesManifest.cs`
- Test: `tests/Fdg.Generator.Tests/RuleYamlTests.cs`、`EmitterTests.cs`、`tests/Fdg.Cli.Tests/Hooks/DeclarationLocatorTests.cs`（新）、`tests/Fdg.Cli.Tests/RulesManifestTests.cs` 或 `Hooks/StopHookTests.cs`

**Interfaces:**
- 规则数据（生成器侧）：
  - 新增可选的映射字段 `naming`，不放进 Schema 的 `required`。
  - `RuleMapping` 新增 `NamingTags? Naming`，其中 `public sealed record NamingTags(IReadOnlyList<string> Symbols, IReadOnlyList<string> Failures);`。
  - `RuleYaml.Serialize` 在值为 null 时不写这个字段，因此 830 条规则中多数不受影响。
  - Schema 约束：
    - `symbols` 的元素取值：`namespace`、`type`、`typeParameter`、`method`、`property`、`event`、`visibleField`、`privateField`、`constField`、`parameter`、`local`、`constLocal`；
    - `failures` 的元素取值：`casing`、`prefix`；
    - 两个数组都要求 `minItems: 1`。
  - `check` 新增一类错误：有 `naming` 的规则，其 diagnostics 必须包含 `IDE1006`。
  - `rules.json` 中每条规则输出 `"naming": {"symbols": [...], "failures": [...]}`，没有时为 `null`。协议版本保持 1，因为这是可选的新增字段，旧 CLI 读取时会忽略。
- CLI 侧：
  - `SarifDiagnostic` 增加 `int Column`，取自 SARIF 的 `region.startColumn`，缺省为 1。
  - `RuleEntry` 增加 `NamingTags? Naming`，用法与生成器侧相同，定义在 `Fdg.Cli.Rules`。
  - `DeclarationLocator`：

    ```csharp
    public enum NamingSymbolKind { Namespace, Type, TypeParameter, Method, Property, Event, VisibleField, PrivateField, ConstField, Parameter, Local, ConstLocal }
    public enum NamingFailure { Casing, Prefix }
    public static class DeclarationLocator
    {
        /// <summary>The kind of the symbol declared at the 1-based line and column, or null when no declaration name is there.</summary>
        public static NamingSymbolKind? NamingKindAt(string source, int line, int column);
        /// <summary>"Missing prefix" → Prefix; "begin with upper case" / "begin with lower case" → Casing; otherwise null.</summary>
        public static NamingFailure? ClassifyNamingFailure(string message);
    }
    ```

    - 种类判定：
      - const 字段归为 `ConstField`，const 局部变量归为 `ConstLocal`，二者都优先于可见性；
      - 字段的有效可见性为 `public`、`protected` 或 `protected internal` 时归为 `VisibleField`，否则归为 `PrivateField`（没有修饰符的字段按 private 处理）；
      - class、struct、record、interface、enum、delegate 都归为 `Type`。
  - `StopReport.RulesFor` 增加参数 `(NamingSymbolKind Kind, NamingFailure Failure)? naming`。当该诊断对应的规则中有任何一条带 `Naming` 时：
    - `naming` 为 null，即位置无法解析或失败类型不认识：返回空列表；
    - 否则只保留 `Naming.Symbols` 包含该种类、并且 `Naming.Failures` 包含该失败类型的规则，再按现有逻辑做 profile 过滤和排序；
    - 现有的“词重叠”筛选只用于不带标签的诊断。
  - `HookRunner.ToIssues` 中，诊断为 `IDE1006` 时：
    - 读取当前文件内容（已缓存在 `sourceLines` 中），调用 `NamingKindAt` 和 `ClassifyNamingFailure`；
    - 两者任一为 null 时，`naming` 传 null。

`naming` 标签表（我的决定，依据是规则原文和第一期 globalconfig 中的命名规则）：

| 规则 | symbols | failures |
|---|---|---|
| FDG3001 | namespace, type, typeParameter, method, property, event | casing |
| FDG3002 | parameter | casing |
| FDG3005 | parameter, local, privateField | casing |
| FDG3030 | namespace | casing |
| FDG3077 | visibleField | casing |
| FDG3079 | visibleField | casing |
| FDG3080 | parameter | casing |
| FDGA052 | namespace, type, typeParameter, method, property, event, visibleField | casing |
| FDGA053 | constField, constLocal | casing |
| FDGA054 | privateField | casing |
| FDGA055 | privateField | prefix |
| FDGA056 | local | casing |
| FDGA057 | parameter | casing |

- [ ] **Step 1: 写生成器侧的失败测试**

```csharp
[Fact] public void Naming_tags_round_trip_and_are_omitted_when_null()   // RuleYamlTests
[Fact] public void Rules_json_carries_naming_tags()                      // EmitterTests
[Fact] public void Naming_tags_require_IDE1006()                         // CheckCommandTests
```

- [ ] **Step 2: 写 CLI 侧的失败测试**

```csharp
// DeclarationLocatorTests
[Theory]
[InlineData("class C { private int Count; }", 1, 23, NamingSymbolKind.PrivateField)]
[InlineData("class C { int count; }", 1, 15, NamingSymbolKind.PrivateField)]
[InlineData("class C { protected int count; }", 1, 25, NamingSymbolKind.VisibleField)]
[InlineData("class C { private const int max = 1; }", 1, 29, NamingSymbolKind.ConstField)]
[InlineData("class C { void M(int Value) { } }", 1, 22, NamingSymbolKind.Parameter)]
[InlineData("class C { void M() { int X = 0; } }", 1, 26, NamingSymbolKind.Local)]
[InlineData("class C { void M() { const int x = 0; } }", 1, 32, NamingSymbolKind.ConstLocal)]
[InlineData("interface foo { }", 1, 11, NamingSymbolKind.Type)]
[InlineData("class C<t> { }", 1, 9, NamingSymbolKind.TypeParameter)]
public void Finds_the_kind_of_the_declared_name(string source, int line, int column, NamingSymbolKind expected)
[Fact] public void Position_outside_a_declaration_name_gives_null()     // 列号落在空白或方法体内的表达式上
[Fact] public void Unparsable_source_still_gives_null_not_an_exception()

// StopHookTests 或 RulesManifestTests，使用真实的嵌入 manifest
[Fact] public void Missing_prefix_on_a_private_field_quotes_only_FDGA055()
[Fact] public void Lower_case_method_quotes_FDG3001_and_FDGA052()
[Fact] public void Upper_case_parameter_quotes_parameter_rules_only()     // 返回的规则都在 {FDG3002, FDG3080, FDGA057, FDG3005} 中，最多 2 条
[Fact] public void Unreadable_location_quotes_no_rule()                  // naming 为 null → 空列表
[Fact] public void Sarif_start_column_is_read_and_defaults_to_1()        // SarifReader
```

- [ ] **Step 3: 运行，确认失败**

运行：
- `dotnet test tests/Fdg.Generator.Tests --filter "FullyQualifiedName~Naming"`
- `dotnet test tests/Fdg.Cli.Tests --filter "FullyQualifiedName~DeclarationLocator|FullyQualifiedName~quotes|FullyQualifiedName~start_column"`

- [ ] **Step 4: 实现生成器侧和 CLI 侧；按标签表写入 13 条规则的 `naming`**

这 13 条规则的映射改变了，所以 `status` 改回 `draft`。由控制者派发交叉核对代理，按逐章流程 R5 核对这 13 条，同意后再改回 `verified`。

- [ ] **Step 5: `fdg-gen generate`；运行全部测试，确认通过；`fdg-gen check` 通过**

- [ ] **Step 6: 提交**

```bash
git add rules src tests docs plugins Directory.Packages.props
git commit -m "feat: pick IDE1006 rules by symbol kind and failure type"
```

### Task 8: FDGA023 的范围

**Files:**
- 分支 A（扩大范围）：
  - Modify: `src/FrameworkDesignGuidelines.Analyzers/Layout/LongListWrappingAnalyzer.cs`
  - Modify: `src/FrameworkDesignGuidelines.CodeFixes/Layout/*`
  - Test: `tests/FrameworkDesignGuidelines.Analyzers.Tests/Layout/LongListWrappingTests.cs`
- 分支 B（措辞对齐）：
  - Modify: `src/Fdg.Cli/Hooks/SessionContext.cs` 第 29 行附近的措辞
  - Test: `tests/Fdg.Cli.Tests/Hooks/SessionContextTests.cs`
- 两个分支都要：
  - Modify: `rules/appA-coding-style.yaml`，在 FDGA023 的 `notes` 中写入判定。

**Interfaces:**
- 判定人：控制者派发的独立交叉核对代理。
- 判定依据：FDGA023 的原文、body，以及附录 A 的 A.1.3 节。
- 判定问题：手工折行、但没有在第一项之前断行的多行参数列表或实参列表，是否违反这条规则？
- 判定结果写入 FDGA023 的 notes，以 `Scope:` 开头，例如 `Scope: a list written on several lines is "exceedingly long" by the author's choice, so it must be chopped before the first item.`

- [ ] **Step 1: 派发判定代理，取得结论（A 或 B）和一句理由**

- [ ] **Step 2（分支 A）: 扩大分析器的范围**

先写失败的测试：

```csharp
[Fact] public async Task Multi_line_list_not_chopped_before_first_item_is_reported()
// void M(int a,\n    int b) → 报告 FDGA023
[Fact] public async Task Code_fix_chops_a_manually_wrapped_list()       // 包含 FixAll
[Fact] public async Task Single_line_list_within_limit_is_not_reported()
[Fact] public async Task Chopped_list_is_not_reported()
```

然后实现：多行列表的第一个参数或实参与左括号在同一行时报告。CodeFix 复用现有的折行逻辑。

- [ ] **Step 2（分支 B）: 让会话上下文的措辞与分析器一致**

先写失败的测试：

```csharp
[Fact] public void Session_context_states_the_long_list_rule_as_the_analyzer_checks_it()
```

然后修改 `SessionContext` 中这一句的措辞，写明只有超过行宽的列表才需要折行。

- [ ] **Step 3: 测试、generate、提交**

```bash
git commit -am "fix: settle the scope of FDGA023 against appendix A"
```

### Task 9: SessionStart 在 SDK 低于 10.0.100 时提示

**Files:**
- Modify: `src/Fdg.Cli/Hooks/SelfCheck.cs`
- Test: `tests/Fdg.Cli.Tests/Hooks/SessionContextTests.cs`，或已有的 SelfCheck 测试文件

**Interfaces:**
- Produces:
  - `public const string FullCoverageSdkVersion = "10.0.100";`
  - SDK 版本在 8.0.100 及以上、低于 10.0.100 时，增加一行：`FDG setup issue: .NET SDK <v> is older than 10.0.100: some rules need SDK 10.0.100 or later.`
  - 这一行不带 “Run /fdg-setup to fix.”。
  - SDK 低于 8.0.100 时，只输出已有的那一行，不重复提示。

- [ ] **Step 1: 写失败的测试**

```csharp
[Theory] [InlineData("8.0.100")] [InlineData("9.0.300")]
public void Sdk_below_10_0_100_notes_missing_rules(string version)
[Theory] [InlineData("10.0.100")] [InlineData("10.0.100-rc.2")] [InlineData("10.0.402")]
public void Sdk_10_0_100_or_later_has_no_note(string version)   // 预发布版本按数字部分比较，与现有的 IsOlderThanMinimum 一致
[Fact] public void Sdk_below_8_0_100_reports_only_the_minimum()
```

- [ ] **Step 2: 运行，确认失败；实现；运行，确认通过；提交**

```bash
git commit -am "feat(cli): note that some rules need SDK 10.0.100 or later"
```

### Task 10: 噪音样本工具与基线记录

**Files:**
- Create: `eng/noise/Measure-Noise.ps1`、`eng/noise/Inject.props`、`eng/noise/Inject.targets`、`eng/noise/samples.json`
- Create: `tests/fixtures/noise/Tiny/Tiny.csproj`、`tests/fixtures/noise/Tiny/Class1.cs`
- Create: `docs/noise-samples.md`
- 本地文件，不提交：`private-sources/noise/pilot.json`

**Interfaces:**
- `samples.json`（开源样本，我的决定，提交号为 2026-10-23 时各仓库的 HEAD）：

```json
[
  { "name": "serilog", "repository": "https://github.com/serilog/serilog", "commit": "81af75f3a5e28ec9f5be18ef74357a9914a51745",
    "project": "src/Serilog/Serilog.csproj", "framework": "netstandard2.0", "profile": "library" },
  { "name": "polly", "repository": "https://github.com/App-vNext/Polly", "commit": "3f3adad9d5af85658fc3585018f02a2d8c2173e8",
    "project": "src/Polly.Core/Polly.Core.csproj", "framework": "netstandard2.0", "profile": "library" },
  { "name": "newtonsoft-json", "repository": "https://github.com/JamesNK/Newtonsoft.Json", "commit": "52fa3aef1f2cadcd3a3f874251eddc98d3efbbaa",
    "project": "Src/Newtonsoft.Json/Newtonsoft.Json.csproj", "framework": "netstandard2.0", "profile": "library" }
]
```

  - 某个样本的 `framework` 在该提交中不存在时，改用它支持的最低 netstandard 或 net 目标框架，并在 ledger 中记录裁定。
  - 本地样本不写 `repository` 和 `commit`，改写 `"path": "<绝对路径>"`。
  - `pilot.json` 列出试点克隆中的一到两个项目，`profile` 用该项目实际适用的值。
- `Measure-Noise.ps1` 的参数：

```text
-Samples <json 文件路径数组> -Package <.nupkg 路径> -WorkRoot <仓库外的临时目录> -Out <结果 json 路径> [-Diagnostics <id 数组>]
```

脚本的流程：
1. 把 `.nupkg` 解压到 `WorkRoot/package`。
2. 远程样本：如果 `WorkRoot/<name>` 不存在，运行 `git init`，然后 `git fetch --depth 1 <repository> <commit>`，再 `git checkout FETCH_HEAD`。这些 git 操作只发生在 WorkRoot 中。
3. 本地样本：直接使用 `path`，不做任何 git 操作，也不写入源文件。
4. 构建：

   ```text
   dotnet build <project> -f <framework> --no-incremental
     -p:TreatWarningsAsErrors=false
     -p:CustomBeforeMicrosoftCommonProps=<Inject.props>
     -p:CustomAfterMicrosoftCommonTargets=<Inject.targets>
     -p:FdgNoisePackage=<WorkRoot/package>
     -p:FdgNoiseSarifDir=<WorkRoot/sarif/<name>>
     -p:FrameworkDesignGuidelinesProfile=<profile>
   ```

   所有参数用数组传入，不拼接字符串。
5. `Inject.props` 导入 `$(FdgNoisePackage)/build/FrameworkDesignGuidelines.Analyzers.props`。
6. `Inject.targets` 负责三件事：
   - 导入包里的 `.targets`；
   - 添加 `<Analyzer Include="$(FdgNoisePackage)/analyzers/dotnet/cs/*.dll" />`；
   - 设置 `<ErrorLog>$(FdgNoiseSarifDir)/$(MSBuildProjectName)-$(TargetFramework).sarif,version=2.1</ErrorLog>`。
7. 读取 SARIF：
   - 排除被抑制的结果；
   - 按 `(文件, 行, ruleId)` 去重，按 ruleId 计数；
   - 给了 `-Diagnostics` 时只统计这些诊断；
   - 结果写到 `-Out`，格式为 `{ "<sample>": { "<ruleId>": <count> } }`；
   - 所有位置写到 `WorkRoot/locations/<sample>.tsv`，列为 ruleId、相对路径、行、消息。
8. 把构建失败的样本记录为 `"<sample>": { "buildFailed": "<首个错误>" }`，然后继续处理下一个样本。
9. 每个样本检查是否存在 `.editorconfig`，以及其中是否覆盖了 `dotnet_diagnostic.*` 或 `dotnet_naming_*`。结果写进 `-Out` 的 `overrides` 字段。

- 原则：`-WorkRoot` 和所有输出都在仓库和试点克隆之外。`docs/noise-samples.md` 只写计数和匿名的结论。

- [ ] **Step 1: 写最小样本**

`Tiny.csproj` 面向 netstandard2.0。`Class1.cs` 只包含 `public class Class1 { private int count; }`，在 library profile 下应恰好产生 1 条 IDE1006（Missing prefix）。

- [ ] **Step 2: 实现脚本和两个注入文件**

- [ ] **Step 3: 在最小样本上验证**

```bash
dotnet pack src/FrameworkDesignGuidelines.Package -c Release -o artifacts/noise
pwsh eng/noise/Measure-Noise.ps1 -Samples <临时 json，path 指向 tests/fixtures/noise/Tiny> -Package artifacts/noise/FrameworkDesignGuidelines.Analyzers.0.1.0.nupkg -WorkRoot $env:TEMP/fdg-noise -Out $env:TEMP/fdg-noise/tiny.json
```

预期：
- `tiny.json` 中 IDE1006 为 1；
- `git status` 显示 `tests/fixtures/noise` 下没有新文件。如果出现了 `bin/` 或 `obj/`，就在 `.gitignore` 中忽略 `tests/fixtures/noise/**/bin` 和 `obj`。

- [ ] **Step 4: 基线运行，覆盖三个开源样本和试点克隆，使用当前的包（只含第 3 章和附录 A）**

在 `docs/noise-samples.md` 中写入以下内容：
- 开头的说明：
  - 样本名单和提交号；
  - 试点克隆的匿名说明：“an internal application, anonymized”；
  - 误报的定义；
  - 门槛为 1/20。
- 第一节 “Baseline: chapter 3 and appendix A”，表格列为：诊断、各样本计数、抽查数、误报数、真实警告数、处理。每个诊断最多抽查 20 处。
- 如果第一期的规则（含 FDGA003、FDGA023）超过门槛，按逐章流程 R6/R7 处理，并写进处理一列。

- [ ] **Step 5: 提交**

```bash
git add eng/noise tests/fixtures/noise docs/noise-samples.md .gitignore
git commit -m "feat(eng): measure diagnostic noise on open-source samples"
```

### Task 11: Stop 遇到已有 API 上的破坏性诊断时不打回

**Files:**
- Create: `src/Fdg.Cli/Hooks/ExistingApi.cs`
- Modify: `src/Fdg.Cli/Hooks/DeclarationLocator.cs`（新增声明键相关方法）
- Modify: `src/Fdg.Cli/Hooks/StopReport.cs`（`ReportedIssue`、`Format`）
- Modify: `src/Fdg.Cli/Hooks/HookRunner.cs`（`ToIssues` 和打回判断）
- Test: `tests/Fdg.Cli.Tests/Hooks/ExistingApiTests.cs`（新）、`Hooks/StopHookTests.cs`

**Interfaces:**
- Consumes：
  - Task 7 的 `DeclarationLocator`、`SarifDiagnostic.Column`；
  - `RulesManifest.BreakingFixDiagnostics`；
  - `Snapshot.WriteBaselineAsync`，它返回本轮开始时文件内容的路径；新文件的内容为空。
- Produces:

```csharp
public sealed record DeclarationKey(string Container, string Name, string Kind);
// DeclarationLocator:
/// <summary>The innermost type or member declaration (type, method, constructor, property, indexer, event, field variable, enum member, delegate) containing the position.</summary>
public static DeclarationKey? DeclarationAt(string source, int line, int column);
public static bool Declares(string source, DeclarationKey key);
// ExistingApi:
public static bool IsExisting(string currentSource, string baselineSource, int line, int column);
```

- `DeclarationKey` 的组成：
  - `Container`：外层命名空间和外层类型名，以 `.` 连接；
  - `Name`：声明名；
  - `Kind`：语法种类名，例如 `"MethodDeclaration"`。参数和类型参数归到它们所属的成员或类型。
- `ReportedIssue` 增加 `bool ExistingApi`：
  - 当诊断在 `BreakingFixDiagnostics` 中，并且 `IsExisting` 为真时，值为 true；
  - 读取基线失败时为 false，也就是照常打回。
- `StopReport.Format(issues, header)` 的输出：
  - 先列出 `ExistingApi == false` 的问题；
  - 再列出末尾一节，第一行固定为 `Existing API: do not change it to fix this; tell the user.`，下面列出 `ExistingApi == true` 的问题，格式与前面相同。
  - 两部分合计仍然最多 20 条。
- `HookRunner` 的行为：
  - 只有存在 `ExistingApi == false` 的问题时才打回，这时计数和标题只计算这些问题；
  - 全部都是已有 API 的问题时，不打回，输出 `SystemMessage`，标题为 `FDG: <n> issue(s) on existing API in lines you changed; not blocking. Tell the user instead of changing the API.`

- [ ] **Step 1: 写失败的测试**

```csharp
// ExistingApiTests
[Fact] public void Method_present_at_turn_start_is_existing()
[Fact] public void Parameter_of_existing_method_is_existing()
[Fact] public void New_method_in_existing_type_is_new()
[Fact] public void Renamed_type_is_new()
[Fact] public void Field_turned_into_property_with_same_name_is_new()
[Fact] public void Same_name_in_another_type_is_new()
[Fact] public void Empty_baseline_means_new()   // 本轮新建的文件

// StopHookTests
[Fact] public void Breaking_diagnostic_on_existing_api_does_not_block()        // 输出为 SystemMessage，含 "Tell the user"
[Fact] public void Existing_api_issues_are_listed_after_blocking_ones()        // 打回原因中，"Existing API: do not change it to fix this; tell the user." 位于新问题之后
[Fact] public void Non_breaking_diagnostic_on_existing_member_still_blocks()   // 例如 IDE0011
```

- [ ] **Step 2: 运行，确认失败**

运行：`dotnet test tests/Fdg.Cli.Tests --filter "FullyQualifiedName~ExistingApi|FullyQualifiedName~existing"`

- [ ] **Step 3: 实现**

`ToIssues` 需要基线内容。在 `HookRunner` 中，按文件调用 `Snapshot.WriteBaselineAsync` 得到路径后读取内容，并按文件缓存。

- [ ] **Step 4: 运行 CLI 和集成测试的全部用例，确认通过；然后提交**

```bash
git commit -am "feat(hooks): do not block on breaking diagnostics of API that existed at turn start"
```

### Task 12: 第 3 章低成本检查的自写分析器

**Files:**
- Create: `src/FrameworkDesignGuidelines.Analyzers/Naming/*.cs`
- Create: `tests/FrameworkDesignGuidelines.Analyzers.Tests/Naming/*.cs`
- Modify: `rules/ch03-naming.yaml`

**Interfaces:**
- 候选（来自总设计 §5.2），由实施者在 `ch03-naming.yaml` 中找到对应的规则编号：
  1. 标识符只用 ASCII 字母和数字；
  2. 缩写词的大小写，使用书中给出的词表；
  3. 过于通用的类型名；
  4. 事件处理器的参数命名为 `sender` 和 `e`；
  5. 非事件委托的后缀（以规则原文为准）；
  6. 布尔属性不使用否定式；
  7. 集合属性的命名；
  8. 不公开可本地化资源。
- 这些检查都涉及名称，因此都不提供 CodeFix，门槛 3 自然满足。
- 分析器的 `category` 统一为 `Naming`。只检查外部可见的符号，这与第 3 章的范围一致。
- 判定方法写在实施报告中。判定方法需要猜测意图的候选（例如“过于通用”没有书中列出的词表），直接按门槛 1 拒绝。

- [ ] **Step 1: 对每个候选判断门槛 1，把结论写进实施报告**

- [ ] **Step 2: 对通过门槛 1 的每个候选，按逐章流程 R6 实现**

先写测试，确认失败；再实现，确认通过。测试包括正例、反例和 `LanguageVersion.CSharp7_3` 用例。

- [ ] **Step 3: 按逐章流程 R7 测误报，在 `docs/noise-samples.md` 中写一节 “Chapter 3 analyzers”**

超过门槛的分析器不发布，在 notes 中写明原因和数字。

- [ ] **Step 4: 规则映射改为 `fdg`**

把对应规则的 `status` 改回 `draft`，由控制者按 R5 派发交叉核对，同意后改为 `verified`。

- [ ] **Step 5: generate、测试、check，然后提交**

```bash
git commit -m "feat(analyzers): add chapter 3 naming checks that pass the noise gate"
```

## 逐章扩展

以下各任务都按“逐章流程”R1–R10 执行，这里只列出每一轮的参数。

`references` 拆分是建议值。生成时如果某个文件超过 32768 字节，就在同一章内按小节继续细分；文件名格式为 `<章前缀>-<主题>`，只用小写字母和连字符。

### Task 13: 第 2 章 Framework Design Fundamentals

- R1 参数：token `"2"`，file `ch02.xhtml`，rules `ch02-fundamentals.yaml`，title `Framework Design Fundamentals`，`testProfile: off`，不拆分。
- 条目数：32（`FDG2001`–`FDG2032`）。
- 说明：本章多为设计过程和原则，预计绝大多数规则为 `judgment`。凡是讲“框架或公开 API 的使用者体验”的规则，application 档也启用，因为总设计 §5.1 规定 application 档覆盖层间契约。

### Task 14: 第 4 章 Type Design Guidelines

- R1 参数：token `"4"`，file `ch04.xhtml`，rules `ch04-types.yaml`，title `Type Design Guidelines`，`testProfile: off`。
- 条目数：70。
- 映射时注意：
  - 类型选择（class、struct、interface）多为 `judgment`；
  - 静态类、枚举、嵌套类型等规则，要逐条查找 CA 诊断，例如 CA1008、CA1027、CA1034、CA1052、CA1714、CA1815，以 `sdk-catalog` 为准；
  - 会改变类型种类或公开签名的修复标 `breaking`。Task 11 已经保证，已有 API 上的这类诊断不会打回。
- 如果单个参考文件超过上限，按 4.1–4.4 和 4.5 之后拆分。

### Task 15: 第 5 章 Member Design（第 1 轮：5.1–5.3）

- R1 参数：token `"5"`，file `ch05.xhtml`，rules `ch05-members.yaml`，title `Member Design`，`testProfile: off`。
- `references` 拆分：

```yaml
references:
  - { file: ch05-members-general, sections: ["5.1"] }
  - { file: ch05-members-properties-constructors, sections: ["5.2", "5.3"] }
  - { file: ch05-members-events-fields-operators, sections: ["5.4", "5.5", "5.6", "5.7"] }
  - { file: ch05-members-parameters, sections: ["5.8", "5.9"] }
```

- 本轮执行 R1、R2，一次抽取全章 133 条。
- R3–R9 只处理 5.1–5.3，共 55 条（5.1 有 25 条，5.2 有 17 条，5.3 有 13 条）。
- 5.4–5.9 的规则保持抽取时的 draft 默认映射，到 Task 16、17 再处理。

### Task 16: 第 5 章（第 2 轮：5.4–5.7）

- 跳过 R1、R2。R3–R9 处理 5.4–5.7，共 44 条（事件 9、字段 4、扩展方法 12、运算符重载 19）。
- 运算符重载要逐条查找 CA 诊断，例如 CA1046、CA2225、CA2231，以 `sdk-catalog` 为准。

### Task 17: 第 5 章（第 3 轮：5.8–5.9）

- 跳过 R1、R2。R3–R9 处理 5.8–5.9，共 34 条（参数 27、元组 7）。
- 结束时第 5 章 133 条全部为 `verified`。

### Task 18: 第 6 章 Designing for Extensibility

- R1 参数：token `"6"`，file `ch06.xhtml`，rules `ch06-extensibility.yaml`，title `Designing for Extensibility`，`testProfile: off`，不拆分。
- 条目数：22。
- 本章多数规则只对公开 API 有意义：application 和 test 设为 `none`，并在 notes 中写一句 `Library only: <理由>`。

### Task 19: 第 7 章 Exceptions

- R1 参数：token `"7"`，file `ch07.xhtml`，rules `ch07-exceptions.yaml`，title `Exceptions`，`testProfile: on`。
- 条目数：70。
- 映射时注意：
  - 异常相关的 CA 诊断很多，例如 CA1031、CA1032、CA1064、CA1065、CA2200、CA2201、CA2208、CA2219，逐条以 `sdk-catalog` 为准；
  - 只适用于公开 API 的规则（例如异常类型的公开构造函数），application 档按 §4 决定，test 档逐条决定。
- 如果超过上限，按 7.1–7.2 和 7.3 之后拆分。

### Task 20: 第 8 章 Usage Guidelines

- R1 参数：token `"8"`，file `ch08.xhtml`，rules `ch08-usage.yaml`，title `Usage Guidelines`，`testProfile: on`。
- 条目数：107。
- `references` 拆分：

```yaml
references:
  - { file: ch08-usage-arrays-attributes-collections, sections: ["8.1", "8.2", "8.3"] }
  - { file: ch08-usage-types, sections: ["8.4", "8.5", "8.6", "8.7", "8.8", "8.9", "8.10", "8.11", "8.12", "8.13"] }
```

- R5 交叉核对分两批派发：8.1–8.3（43 条）和 8.4–8.13（64 条）。

### Task 21: 第 9 章 Common Design Patterns（第 1 轮：9.1–9.3）

- R1 参数：token `"9"`，file `ch09.xhtml`，rules `ch09-patterns.yaml`，title `Common Design Patterns`，`testProfile: off`。
- `references` 拆分：

```yaml
references:
  - { file: ch09-patterns-aggregate-async, sections: ["9.1", "9.2", "9.3"] }
  - { file: ch09-patterns-dispose-factories-linq, sections: ["9.4", "9.5", "9.6", "9.7", "9.8"] }
  - { file: ch09-patterns-template-timeouts-xaml, sections: ["9.9", "9.10", "9.11"] }
  - { file: ch09-patterns-buffers, sections: ["9.12"] }
```

- 本轮执行 R1、R2，一次抽取全章 175 条。
- R3–R9 只处理 9.1–9.3，共 61 条（聚合组件 17、async 33、依赖属性 11）。

### Task 22: 第 9 章（第 2 轮：9.4–9.8）

- 跳过 R1、R2。R3–R9 处理 9.4–9.8，共 62 条（Dispose 22、工厂 9、LINQ 15、可选功能 3、协变逆变 13）。
- Dispose 模式要逐条查找 CA 诊断，例如 CA1063、CA1816、CA2213、CA2215、CA1001，以 `sdk-catalog` 为准。

### Task 23: 第 9 章（第 3 轮：9.9–9.12）

- 跳过 R1、R2。R3–R9 处理 9.9–9.12，共 52 条（模板方法 5、超时 4、XAML 4、缓冲区 39）。
- 9.12 的规则依赖 `Span<T>` 等类型，记录 `minLanguage` 和 `alternative`。
- 结束时第 9 章 175 条全部为 `verified`。

### Task 24: 附录 D Breaking Changes

- R1 参数：token `"D"`，file `app04.xhtml`，rules `appD-breaking-changes.yaml`，title `Breaking Changes`，`testProfile: off`，不拆分。
- 条目数：63（`FDGD001`–`FDGD063`）。
- 映射（§8a）：
  - 全部为 `judgment`；severity 为 library 按强度默认值，application 和 test 为 `none`；不映射 RS 诊断；
  - 每条规则的 notes 都以同一句结尾：`PublicApiAnalyzers (optional in /fdg-setup) can flag changes to the public API.`
- 本任务跳过 R6；R7 只记录“无新诊断”。

## 收尾

### Task 25: 前言、第 1 章、附录 B、附录 C 等非规则部分的登记

**Files:**
- Modify: `src/Fdg.Generator/Rules/RuleLoader.cs`（`MetaFile.OtherContent`）
- Modify: `src/Fdg.Generator/Emit/CoverageEmitter.cs`
- Modify: `src/Fdg.Generator/Rules/MappingValidator.cs` 或 `CheckCommand`：检查 note 中引用的规则编号确实存在
- Modify: `rules/meta.yaml`、`docs/coverage.md`（生成）
- Test: `tests/Fdg.Generator.Tests/EmitterTests.cs`、`CheckCommandTests.cs`

**Interfaces:**
- `meta.yaml` 新增顶层键：

```yaml
otherContent:
  - source: pref04.xhtml
    title: Preface
    entries:
      - section: Preface
        disposition: background
        note: "The seven icons in the preface are the legend of DO, CONSIDER, AVOID and DO NOT; they are not guidelines."
```

- 对应的记录类型：
  - `public sealed record OtherContent(string Source, string Title, IReadOnlyList<NonGuidelineEntry> Entries);`
  - `MetaFile` 增加 `IReadOnlyList<OtherContent> OtherContent`，默认为空。
- 覆盖率文档在 “Non-guideline content” 之后新增一节 `## Other parts of the book`，每个部分一个 `###` 标题，条目格式与现有的非准则内容相同。
- `check` 新增一类错误：nonGuideline 和 otherContent 的 note 中出现的 `FDG[1-9A-Z]\d{3}` 编号，必须是已存在的规则。
- 需要登记的部分：
  - `pref01.xhtml`–`pref07.xhtml`（前言、序、致谢、作者介绍）；
  - `ch01.xhtml`（第 1 章，无准则）；
  - `app02.xhtml`（附录 B）；
  - `app03.xhtml`（附录 C）；
  - `app05.xhtml`；
  - `gloss01.xhtml`。
- 附录 B 按书中的每一小节登记为 `obsolete`。note 写明被哪条现行规则取代（`Superseded by FDGxxxx.`）；没有替代时写 `No current rule replaces it.`。
- 附录 C 登记为 `background`。note 写明 fdg-review 评审库的公开 API 时以它为背景。

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Coverage_lists_other_parts_of_the_book()
[Fact] public void Unknown_rule_id_in_a_note_is_an_error()
```

- [ ] **Step 2: 运行，确认失败；实现；运行，确认通过**

- [ ] **Step 3: 写入 `otherContent`，处理附录 B 的取代关系**

逐节阅读 `$BOOK/app02.xhtml`，对照现行规则找出取代关系，写入 `otherContent`。

然后由控制者派发一个交叉核对代理，核对附录 B 的每一条取代关系，结果写入 `<工作区>/cross-check-appB.md`。处理完其中的所有 DISAGREE。

- [ ] **Step 4: generate、test、check，然后提交**

```bash
git commit -am "feat(gen): register the preface, chapter 1 and appendices B and C in the coverage"
```

### Task 26: fdg-write 路由表、重点规则，fdg-review 按主题评审，会话提示

**Files:**
- Modify: `plugins/framework-design-guidelines/skills/fdg-write/SKILL.md`
- Modify: `plugins/framework-design-guidelines/skills/fdg-review/SKILL.md`
- Modify: `plugins/framework-design-guidelines/agents/fdg-reviewer.md`
- Modify: `src/Fdg.Cli/Hooks/SessionContext.cs`
- Test: `tests/Fdg.Generator.Tests/SkillLintTests.cs`、`tests/Fdg.Cli.Tests/Hooks/SessionContextTests.cs`

**Interfaces:**
- `SkillLintTests` 的修改：
  - `MaxTopRules` 改为 60，`MaxFdgWriteBodyLines` 改为 250（我的决定：几十条重点规则加路由表，远低于 500 行）。
  - 删除 `Fdg_write_top_rules_are_listed_rules_naming_first`，改为 `Fdg_write_top_rules_are_distinct_listed_rules`：规则不重复，并且 `IsListed` 为真；不再要求按章节排序，因为重点规则按重要性排序。
  - 新增 `Fdg_write_routes_to_every_reference_file`：`references/` 下的每个顶层 `.md` 文件名都出现在 fdg-write 的 SKILL.md 中。
  - 新增 `Fdg_review_routes_to_every_reference_file`：fdg-review 的 SKILL.md 中，每个文件名以 `../fdg-write/references/<file>.md` 的形式出现。
- fdg-write 的 SKILL.md：
  - 保留现有的范围说明段落，测试 `Fdg_write_states_its_scope_before_the_rules` 不变；
  - 加入一张 `| When you are… | Read |` 路由表，主题包括：
    - 设计类型（第 4 章）；
    - 成员、属性、构造函数、事件、字段、运算符（第 5 章各文件）；
    - 参数；
    - 可扩展性（第 6 章）；
    - 异常（第 7 章）；
    - 集合、数组、特性、Equals、`object` 成员（第 8 章）；
    - async、Dispose、工厂、LINQ、缓冲区（第 9 章各文件）；
    - 修改已发布的公开 API（附录 D）；
    - 命名（第 3 章）；
    - 排版（附录 A）。
  - 一段说明 test 项目中哪些规则启用：第 7、8 章和附录 A。
  - 重点规则最多 60 条，按重要性排序，原文逐字引用。
- fdg-review 的 SKILL.md 和 `fdg-reviewer.md`：
  - 按被评审代码涉及的主题，读取路由表中对应的文件；
  - 评审库的改动时，基线为用户指定的值，默认为 `git merge-base HEAD <默认分支>`，其中默认分支取 `git symbolic-ref refs/remotes/origin/HEAD`，没有时用 `main`；按附录 D 逐条对照；
  - 附录 C 的原文不打包进插件，因为它不是准则，没有规则数据作为来源（用户 2026-10-24 同意）。
  - fdg-review 用我们自己的话写 3–5 行要点，一句原书原文都不复制。
    - 内容：一份 API 规范应包含场景示例代码、目标与非目标、API 与行为、问答。
    - 用途：评审库的公开 API 时，按第 2 章中相应的规则检查这几部分是否齐全，并引用规则编号。
    - 校验：这几行没有规则原文，也就不受逐字校验约束；`SkillLintTests` 的 `Every_rule_id_mentioned_in_skills_exists_in_rules` 会检查其中引用的编号确实存在。
- `SessionContext`：在现有的 fdg-write 提示后加一句 `Before designing or changing types, members, exception handling or common patterns, read the fdg-write skill.`。

- [ ] **Step 1: 修改 SkillLintTests 和 SessionContextTests，运行，确认失败**

`SessionContextTests` 新增 `Context_points_to_fdg_write_before_design_work`，并确认现有的长度上限测试仍然成立。

- [ ] **Step 2: 写 SKILL.md、fdg-review、`fdg-reviewer.md` 和 SessionContext；运行全部测试，确认通过**

- [ ] **Step 3: `claude plugin validate --strict plugins/framework-design-guidelines` 通过，然后提交**

```bash
git commit -am "feat(skills): route fdg-write and fdg-review to the reference files of the whole book"
```

### Task 27: 全书复核与交付

**Files:**
- Modify: `rules/ch03-naming.yaml`、`rules/appA-coding-style.yaml`（只改 status，如果核对后需要）
- Modify: `docs/phase-2-spot-check.md`、`docs/phase-2-backlog.md`

- [ ] **Step 1: 重新校验第一期的 158 条规则**

由控制者派发交叉核对代理，分两批：第 3 章 87 条，附录 A 71 条。核对内容与 R5 相同，重点看抽取器修复后的 body，以及 Task 7 的 `naming` 标签。处理完所有 DISAGREE。

- [ ] **Step 2: 统计**

用 `python -I` 统计 `rules/*.yaml`。

预期：
- 规则共 830 条，全部为 `verified`；
- 各章条目数与本期设计 §2 的表格一致。

- [ ] **Step 3: 全量验证**

- `fdg-gen generate` 后 `git status --porcelain` 为空；
- `fdg-gen check --book $BOOK` 通过；
- `dotnet test -c Release` 全部通过。

- [ ] **Step 4: 整理文档**

- `docs/phase-2-spot-check.md`：在开头写明用法（逐条打勾；发现问题时写在条目下方），以及各章的条目数。
- `docs/phase-2-backlog.md`：
  - 删除本期已完成的项：“Before extending to more chapters” 中除 `check` SDK 布局之外的 4 项，以及 “Rule mapping” 的前两项；
  - 其余项保留，交给第三期。

- [ ] **Step 5: 提交**

```bash
git commit -am "docs: deliver the phase 2 spot-check list and trim the backlog"
```

- [ ] **Step 6: 推送前询问用户**

用户同意后执行 `git push`，然后用 `gh run watch` 确认 CI 在 windows-latest 和 ubuntu-latest 上都是绿的。
