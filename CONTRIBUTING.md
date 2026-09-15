# 贡献指南

感谢你考虑为本项目贡献代码或文档。本文件说明参与协作前需要了解的基本约定。

## 一、提交问题与建议

- **缺陷报告**：请使用 Issue 模板，附上操作系统、程序版本、复现步骤、期望结果，以及数据目录 `logs` 下的当日日志（**请先脱敏**，不要粘贴真实住户的姓名、电话、身份证或房产信息）。
- **功能建议**：请先描述"要解决的问题"，再谈实现方式；涉及数据结构变更的建议请说明对既有数据的影响。
- **安全类问题**：请勿在公开 Issue 中披露细节，先通过仓库主页的联系方式私下沟通。

## 二、开发环境

环境准备、还原、编译、运行与测试步骤见[开发者部署操作指南](documentation/deployment/开发者部署操作指南.md)。最小闭环：

```powershell
nuget restore PropertyManagement.sln
msbuild PropertyManagement.sln /p:Configuration=Release /m
powershell -NoProfile -ExecutionPolicy Bypass -File PropertyManagement.Tests\run-tests.ps1
```

## 三、分支与提交

- 分支前缀：`feature/`、`fix/`、`docs/`（例如 `fix/bill-partial-payment-validation`）。
- 提交信息：中文，格式 `<范围>：<简述>`，例如 `财务收费：修正部分缴款金额校验`。
- 一次提交聚焦一件事，避免把格式化、重命名与功能改动混在一起。
- 不要提交 `bin/`、`obj/`、`packages/`、`output/`、`Installer/redist/` 等生成物；`.gitignore` 已覆盖。

## 四、代码规范

| 项 | 约定 |
|---|---|
| 语言与框架 | C# 7.3、.NET Framework 4.8（如需新增依赖，请选 net45/net46/netstandard2.0 兼容资产） |
| 格式 | 4 空格缩进；大括号另起一行（Allman） |
| 命名 | 类型 `PascalCase`；局部变量与参数 `camelCase`；常量 `SCREAMING_SNAKE`；表字段 `t_xxx` 下划线 ↔ DTO 驼峰 |
| 分层红线 | 契约层只放 DTO/枚举/错误码；业务规则只在后端领域层；客户端只通过 API 访问数据 |
| 数据 | 删除一律软删（`del_flag`），不做物理删除；关键操作写审计日志 |
| 序列化 | JSON 驼峰、枚举字符串、统一信封 `ApiResponse<T>` |

## 五、提交前自测清单

- [ ] `msbuild` 以 Release 编译通过，无新增警告
- [ ] 单元测试全部通过（181 例基线）
- [ ] 涉及界面改动时附截图（改动前后对比更佳）
- [ ] 涉及数据结构变更时提供迁移脚本与升级说明
- [ ] 文档同步更新（API 契约、数据库设计、功能说明中受影响的章节）

## 六、Pull Request

请使用 PR 模板填写变更说明、变更类型与自测情况，并关联相关 Issue。合并前请确认三点：是否符合分层约定、是否破坏既有数据兼容、是否补齐测试。

## 七、许可

提交即表示你同意以本项目的 [MIT License](LICENSE) 授权你的贡献。请注意：**品牌标识（名称、徽记、字标）不在 MIT 授权范围内**，详见[品牌与授权说明](documentation/legal/品牌与授权说明.md)。
