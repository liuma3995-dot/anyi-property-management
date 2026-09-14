# Repository Guidelines

> 安怡物业管理系统：单机、前后端分离（WPF 客户端 + 本地 OWIN 后端）。架构与决策见模型基线；文档位于 `docs/`（已排除版本控制）。

## Project Structure & Module Organization（项目结构）

```
PropertyManagement.sln
├─ PropertyManagement.Contract   # 共享契约：DTO/枚举/错误码，无业务依赖
├─ PropertyManagement.Server     # 后端：Api/ Services/ Domain/ Infrastructure/
├─ PropertyManagement.Client     # 前端：Views/ ViewModels/ Services/ Assets/
└─ Installer/                    # Inno Setup 安装脚本
```

- `docs/`（模型基线、计划、原型）不在版本控制内，其改动不会出现在 git 面板；
- 分层遵循 DM-01：前端只调 API，业务规则只在 Server 领域层，前后端只共享 Contract。

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

测试：当前无测试工程；后端领域规则（AM-06 BR）在 M7 前以 xUnit/NUnit（net48 兼容版）补充。

## Coding Style & Naming Conventions（编码风格）

- C# 7.3（.NET Framework 4.8）；4 空格缩进；大括号另起一行（Allman）；
- 命名：类型 PascalCase、局部变量/参数 camelCase、常量 SCREAMING_SNAKE；表字段 `t_xxx` 下划线 ↔ DTO 驼峰；
- 契约层只放 DTO/枚举，业务实现不得进入 Contract；新依赖只选 net45/net46/netstandard2.0 兼容资产（Win7 红线）；
- JSON 驼峰、枚举字符串序列化、统一信封 `ApiResponse<T>`、删除一律软删（`del_flag`）。

## Testing Guidelines（测试规范）

- 领域规则优先覆盖；测试类命名 `XxxTests`，方法 `方法名_场景_期望`（如 `Pay_OverAmount_Rejects`）；
- 验收依据 60 用例追溯矩阵，测试工程加入后以 `MSBuild /t:Test` 运行。

## Commit & Pull Request Guidelines（提交规范）

- **必须经项目负责人明确指令后方可提交**，禁止私自 git add/commit/rm；
- 提交信息中文，格式 `<里程碑/范围>：<简述>`（如 `M1 契约先行：…`）；
- 当前为本地单仓（main），无 PR 流程；推送远程或多人协作前先与负责人确认。

## Agent-Specific Instructions（代理注意事项）

- 改动文件后留在工作区供审阅，等负责人指示"提交"再执行提交；
- 敏感操作（备份恢复、删除、基线变更）先征求负责人同意；模型/契约变更走 CHG 流程。
