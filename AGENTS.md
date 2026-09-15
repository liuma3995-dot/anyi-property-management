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

## Testing Guidelines（测试规范）

- 领域规则优先覆盖；测试类命名 `XxxTests`，方法 `方法名_场景_期望`（如 `Pay_OverAmount_Rejects`）；
- 单元测试使用 `%TEMP%` 下的隔离库，不得读写 `%ProgramData%\PropertyManagement`；
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
