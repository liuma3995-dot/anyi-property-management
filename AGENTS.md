# Repository Guidelines

> 安怡物业管理系统：单机、前后端分离（WPF 客户端 + 本地 OWIN 后端）。对外文档见 `documentation/`，贡献约定见 `CONTRIBUTING.md`。

## Project Structure & Module Organization（项目结构）

```
PropertyManagement.sln
├─ PropertyManagement.Contract   # 共享契约：DTO/枚举/错误码，无业务依赖
├─ PropertyManagement.Server     # 后端：Api/ Services/ Domain/ Infrastructure/
├─ PropertyManagement.Client     # 前端：Views/ ViewModels/ Services/ Assets/
└─ Installer/                    # Inno Setup 安装脚本
```

- 分层约定：前端只调 API，业务规则只在 Server 领域层，前后端只共享 Contract；
- 架构与选型说明见 [documentation/architecture](documentation/architecture/)。

## Build, Test, and Development Commands（构建/测试/开发）

```powershell
# 依赖还原（首次或 packages.config 变更后）
nuget restore PropertyManagement.sln

# 编译（VS MSBuild，推荐）
& 'F:\.NET\MSBuild\Current\Bin\MSBuild.exe' PropertyManagement.sln /p:Configuration=Debug /m

# 运行后端（127.0.0.1:5210）与前端
PropertyManagement.Server\bin\Debug\PropertyManagement.Server.exe
PropertyManagement.Client\bin\Debug\PropertyManagement.Client.exe

# 健康检查
Invoke-WebRequest http://127.0.0.1:5210/api/v1/health
```

测试：`PropertyManagement.Tests`（xUnit + 控制台 runner），执行 `PowerShell -File PropertyManagement.Tests\run-tests.ps1`。

## Coding Style & Naming Conventions（编码风格）

- C# 7.3（.NET Framework 4.8）；4 空格缩进；大括号另起一行（Allman）；
- 命名：类型 PascalCase、局部变量/参数 camelCase、常量 SCREAMING_SNAKE；表字段 `t_xxx` 下划线 ↔ DTO 驼峰；
- 契约层只放 DTO/枚举，业务实现不得进入 Contract；新依赖只选 net45/net46/netstandard2.0 兼容资产（Win7 红线）；
- JSON 驼峰、枚举字符串序列化、统一信封 `ApiResponse<T>`、删除一律软删（`del_flag`）。

## Versioning（版本控制：产品版本与文档版本）

**全仓只有一个版本号：产品版本。** 文档不设独立版本号——不得再出现「产品版本 1.1.1 + 文档版本 v0.1」并存的写法。

### 产品版本（唯一来源）

- 唯一手写来源：`Build\Version.props` 的 `<AnyiVersion>`（三位，如 `1.1.1`）；程序集版本由它派生（`1.1.1.0`）；
- 源码、界面、脚本、文档**不得硬编码版本串**；运行期统一经 `Build\AppVersionInfo.cs` 读取（`ShellViewModel`／`LoginViewModel`／`GET /health` 即此口径）；
- 生成物 `obj\<配置>\ProductInfo.g.cs` 不纳管；版本相关只保留 `Version.props`、`ProductInfo.targets`、`AppVersionInfo.cs` 三个源文件；
- 打包只走 `Installer\build-setup.ps1`（默认取版本真值，内含「产物新鲜度」与「版本元数据」两道闸门，不得用 `-SkipBuild` 绕过）；
- 改版本 = 改一处 + 一条命令：改 `Version.props` → 编译 → `build-setup.ps1`；升版后同步对外文档（`documentation\` 各文档「适用版本」与 `README.md`「当前版本」）。

### 文档版本（不设版本号）

- 抬头只写「〔制品／编号 ＋〕状态 ＋ 产品版本 ＋ 日期」（字段口径见下「文档元信息块」）；**不得写「文档版本 vX.Y」**；
- 文件名**不带文档版本号**（不得出现 `（v0.1）` 一类后缀）；迭代计划可按**产品版本**归属（目录名或文件名前缀，如 `…\安怡物业管理系统v1.1.1\v1.1.1-xxx计划.md`）；确需区分轮次时用日期或序号；
- 修订痕迹写在文末**修订记录表**（`docs\` 不纳入版本控制，无法依赖 git 历史）；
- 历史引用（`v1.1.0 第 18 轮`、`V1.0.0 旧模板`）保留，但不得写入抬头当作当前版本；
- 变更留痕统一用 `CHG-<产品版本>-NN`（编号，不是第二个版本号）。

### 文档元信息块（抬头）

**抬头只回答三问**：这是什么（制品／编号）→ 什么状态 → 属于哪个产品版本、什么时候定的。读一段话不是抬头的职责，解释与出处放正文。

**形态**：`>` 块引用，**一行**；`键：值` 成对，分隔符用全角 `｜`，键名固定中文；不加粗、不带链接与 emoji——保证纯文本可解析、diff 干净。

**字段白名单（顺序固定，不得增删）**

| 文档类型 | 抬头 |
|---|---|
| 制品（`AM-*`／`DM-*`）与变更记录 | `制品 ｜ 状态 ｜ 产品版本 ｜ 日期`（变更记录用 `编号`） |
| 计划／迭代／目录／评审／规格／基线／README | `状态 ｜ 产品版本 ｜ 日期` |

示例：`> 制品：AM-02 ｜ 状态：已冻结 ｜ 产品版本：1.0.0 ｜ 日期：2026-08-26`

**取值约束**

- `状态` 用封闭词表六项：草案／已确认／执行中／已完成／已封存／已冻结；
- `产品版本` 只取 `Build\Version.props` 的值（历史归档用归档批次号，如 `1.0.0`），不得自造；
- `日期` 为 `YYYY-MM-DD`，写**该状态的确立日**，不是「最后打开日」。

**其余元信息 → 正文首节「适用与依据」**：`依据`／`来源`／`上游输入`／`前置状态`／`归档前状态`／`说明`／`输入`／`修订`／`关联`／`迁移` 等非白名单字段，一律写成该节条目（`- **依据**：…`），紧随抬头、位于第一个 `##` 章节之前。

**不得出现**：作者类（制定人／执行人／验收人／审批人／记录人／确认人）与纯流程信息（修订次数一类）**直接删除**；文档版本（`v0.1` 一类）与敏感信息（账号／令牌／内网路径）更不得出现。能从别处推导的留给 `git log`。

**本仓例外**：`docs\` 不纳入版本控制、无 git 历史可查 → 抬头**必须手写日期**，修订痕迹写文末**修订记录表**（追加式，不删行；`CHG-*` 变更记录本身即记录，不加表）。

**阈值**：抬头 **≤2 行、≤200 字符、单行 ≤160 字符**。实施记录：2026-09-18 对 `models`／`项目开发计划封存文档V1.0.0`／`优化迭代`／`前端原型` 共 96 份完成对齐（一次性治理脚本与快照已随治理结束清理；新增文档按本节规则手写即可，自检项：结构／取值／阈值／章节／作者字段／正文零改动／信息零丢失／死链）。

> 业界参照（选型依据）：PEP 1（必需／可选字段分层 + 封闭状态词表）、MADR（状态与决策者分离）、Kubernetes KEP（`creation-date`／`last-updated`／`superseded-by` 前置块）、IETF RFC／Internet-Draft（类别 + 发布日 + 失效期）、Dublin Core／HTML `<meta>`（通用元素集）、ISO 文件控制（唯一标识 + 状态 + 修订记录）。共性结论：**字段少而固定、取值封闭、可推导的不手写、留痕追加不改行**。

### 例外

- 内部资产（模型、原型）确需独立版本时，集中登记在 `docs\README.md`，且目录名／文件名／文档抬头三处必须一致。

## Testing Guidelines（测试规范）

- 领域规则优先覆盖；测试类命名 `XxxTests`，方法 `方法名_场景_期望`（如 `Pay_OverAmount_Rejects`）；
- 单元测试使用 `%TEMP%` 下的隔离库，不得读写 `%ProgramData%\PropertyManagement`；
- **测试资产按属性归入 `tmp\测试目录\` 对应子目录**（`临时测试`／`单元测试`／`集成测试`／`系统测试`／`验收测试`／`测试工具`），不得散落在 `tmp\` 根或仓库其它位置；各类口径见 `tmp\测试目录\README.md`；
- `测试工具\` 只放跨阶段共用的外部可执行文件与配置（如 `nuget.exe`），不放脚本逻辑；非测试资产（`docs\`、`tmp\品牌设计资产\`）不进本目录；
- 移动测试资产须同步改引用（如 `PropertyManagement.Tests\run-tests.ps1`、`Installer\build-setup.ps1`）并在 `tmp\测试目录\README.md` 引用关系表登记；
- 提交前须保证编译通过且测试全绿。

## Commit & Pull Request Guidelines（提交规范）

- 提交信息中文，格式 `<范围>：<简述>`（如 `财务收费：修正部分缴款金额校验`）；
- 主分支为 `main`；外部贡献走分支 + Pull Request 流程，约定见 [CONTRIBUTING.md](CONTRIBUTING.md)；
- 不提交生成物（`bin/`、`obj/`、`packages/`、`output/`、`Installer/redist/`）。

## AI 辅助开发说明

本项目在开发过程中使用了 AI 辅助编程工具（本文件即其协作约定）。对贡献者的期望一致：

- 改动后先在本地自测（编译 + 单元测试），再提交；
- 敏感操作（数据备份恢复、批量删除、数据结构变更）须有明确说明与迁移方案；
- 契约（DTO/枚举/错误码）变更须同步更新 API 契约与数据库设计文档。
