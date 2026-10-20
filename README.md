# framework-design-guidelines

将 *Framework Design Guidelines: Conventions, Idioms, and Patterns for Reusable .NET Libraries (Addison-Wesley Microsoft Technology Series)* 第三版（2020）的规范整理为 Codex 与 Claude Code 共用的插件。

本项目面向可复用 .NET 库及组件的设计、实现和审查。公共 API 设计是核心范围，C# 编码风格是其中一个主题。规则资料覆盖全书，每次任务只加载适用内容，并将可机械验证的要求交给格式工具、分析器和兼容性检查。

## 当前状态

项目已初始化，处于设计阶段。项目名已批准；[设计草案](docs/superpowers/specs/2026-10-20-framework-design-guidelines-design.md) 尚待审阅。当前没有可安装的插件、已核对的全书规则集或双端遵循率评测结果。

## 已确认的要求

- 规范来源为第三版（2020），保留规则强度、适用范围、理由、例外及章节定位。
- 同时支持 Codex 和 Claude Code，共用规则正文、Skills 与验证资产。
- 全书覆盖与单次上下文加载分开管理，避免整书或全部规则常驻上下文。
- 可读性问题需要在生成、检查和交付阶段共同处理。
- 规则加载、实际遵循、自动检查覆盖和最终交付分别验证，不用安装成功代替行为验证。

## 来源与版本

参考书作者：Krzysztof Cwalina、Jeremy Barton、Brad Abrams。

[出版社第三版页面及目录](https://www.informit.com/store/framework-design-guidelines-conventions-idioms-and-9780135896501)。全书正文的本机来源待确认；目前尚未开始逐章提炼。

[Microsoft Learn 的在线 Framework Design Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/) 明确摘自第二版（2008）。可用作背景资料，但不能替代第三版的完整来源或证明第三版覆盖。

## 工作方式

先核对来源并审阅设计，再编写实施计划。规则摘要、宿主适配和工具检查逐步建立；真实任务评测记录首次生成、修正后和最终交付的结果。

原书正文及大段原文不作为插件分发内容。提炼资料以独立表述、来源定位和必要的短示例为主。项目代码与提炼资料的分发许可待确定。

## 开发入口

贡献指令维护在 [AGENTS.md](AGENTS.md)。[CLAUDE.md](CLAUDE.md) 引入相同入口。产品规则和使用项目的兼容性配置将按设计分开维护。
