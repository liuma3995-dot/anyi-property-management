# 澜庭物业管理系统

> 单机、前后端分离的物业管理系统。模型基线 v1.0 冻结，前端原型 v1.0 冻结，详见 [docs/models/模型基线-v1.0/README.md](docs/models/模型基线-v1.0/README.md) 与 [docs/prototypes/前端原型-v0.1/原型页面索引.md](docs/prototypes/前端原型-v0.1/原型页面索引.md)。

## 工程结构

```
PropertyManagement.sln
├─ PropertyManagement.Contract        # 前后端共享契约（DTO、枚举、常量），无外部业务依赖
├─ PropertyManagement.Server          # 本地后端服务（OWIN 自承载 HTTP API）
├─ PropertyManagement.Client          # WPF 前端（.NET Framework 4.8 + HandyControl）
└─ Installer/                         # Inno Setup 安装脚本（M8 完善）
```

## 构建方式（当前工具链）

本机未安装 Visual Studio / .NET SDK，使用 .NET Framework 4.8 自带 MSBuild 与 NuGet.exe 构建：

```powershell
# 1. 依赖还原（首次，需要网络）
nuget restore PropertyManagement.sln

# 2. 编译
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" PropertyManagement.sln /p:Configuration=Debug /m
```

> 说明：依赖包还原到 `packages/`（已在 .gitignore 中忽略）；引用程序集由 NuGet 包 `Microsoft.NETFramework.ReferenceAssemblies.net48` 提供，无需安装 Developer Pack。

## 分层依赖方向（DM-01）

- 表现层（Client）：只调 API，不碰数据库；
- API 层 / 应用服务层 / 领域层 / 基础设施层（Server）：后端拥有全部业务逻辑；
- Contract：前后端共享 DTO，Client 与 Server 均引用，Contract 不得引用任何业务工程；
- 前端与后端唯一耦合点是 API 契约，契约变更走 CHG。

## 数据目录规范（DZ-3）

运行期数据统一存放 `%ProgramData%\PropertyManagement\`（SQLite 库、日志、备份、导出），安装包预建目录并设置权限；**不得写入 Program Files**（Win7/权限兼容）。Server 路径设计与 Installer 脚本按此执行。

## 分支与提交约定

- `main`：发布基线分支，禁止直接提交业务代码，合入须经过评审；
- 功能开发使用 `feature/模块-简述` 分支，完成后合入 `main`；
- 提交遵循"小步提交、可编译可运行"，每个切片完成即达可演示状态。
