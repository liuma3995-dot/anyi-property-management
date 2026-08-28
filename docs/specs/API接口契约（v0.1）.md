# API 接口契约（v0.1）

> 版本：v0.1 ｜ 日期：2026-08-28 ｜ 状态：已定稿（D1-3 评审通过，2026-08-28）
> 依据：[模型基线 v1.0](../models/模型基线-v1.0/README.md)＋ [DM-02 组件与子系统设计](../models/设计模型-v0.1/DM-02-组件与子系统设计.md)＋ [DM-03 设计类设计](../models/设计模型-v0.1/DM-03-设计类设计.md)＋ [DM-04 数据库设计](../models/设计模型-v0.1/DM-04-数据库设计.md)＋ [技术选型建议（确认稿）](../models/设计模型-v0.1/技术选型建议.md)
> 契约代码：`PropertyManagement.Contract`（前后端同源引用，173 个类/枚举）
> 里程碑：M1 契约先行（D1-1/D1-2/D1-3）

## 一、通用规范

### 1.1 基址与路由

- 基址：`http://127.0.0.1:5210/api/v1`（后端仅绑定回环地址，默认端口 5210 可配置）；
- 路由：`/{group}/{resource}[/{id}[/{action}]]`，资源名复数、小写下划线；
- 所有接口统一返回信封 `ApiResponse<T>`：`{ code, message, data }`；`code = 0` 表示成功，其余为错误码（见 §四）。

### 1.2 鉴权

- 除 `POST /auth/login`、`GET /health` 外，所有请求头携带 `Authorization: Bearer {token}`；
- token 无效/过期返回 HTTP 401 + `code = 40100`；无权限返回 403 + `code = 40300`；
- 登录失败锁定：连续失败 5 次锁定 30 分钟（P-02，参数可配），返回 `code = 40101`；
- 密码使用 BCrypt 加盐哈希，前端不保存明文。

### 1.3 分页/筛选/排序

- 列表接口请求继承 `PageRequest`：`pageIndex`（默认 1）、`pageSize`（默认 20，上限 200）、`keyword`；
- 列表接口响应统一 `PageResult<T>`：`{ pageIndex, pageSize, total, items }`；
- 筛选条件以扁平查询参数/查询 DTO 表达；排序暂由后端默认（创建时间倒序），特殊排序后续契约扩展。

### 1.4 时间与枚举

- 时间统一 ISO 8601 本地时间（如 `2026-08-28T17:53:37+08:00`）；
- 枚举序列化为**字符串**（M2 Startup 配置 `StringEnumConverter`），如 `"status": "Paid"`；
- 金额统一 `decimal`，两位小数；前端显示与打印由前端处理。

### 1.5 删除与审计

- 删除统一为**软删除**（`del_flag`），不做物理删除（DM-07 §二）；
- 敏感操作（收款/退款/支出/结案/导入/备份/字典修改等）由后端写入 `t_audit_log`，前端不可修改；
- 导出/打印留痕：`t_export_log` / `t_print_log`，补打保留原收据号并递增打印次数（BR-FIN-08）。

### 1.6 导入/导出/文件

- 导入：`multipart/form-data` 上传 Excel（模块类型见 `ImportModule`）；校验不通过不入库，错误清单可下载重传（BR-INF-05）；
- 导出：创建导出任务（写 `t_export_log`/`t_report_log`）后返回记录，文件经下载端点获取；
- 文件下载端点：`GET /{group}/files/{logId}` 返回文件流（Content-Disposition 携带文件名）。

## 二、60 用例 → 端点映射表

> 覆盖核对：INF 7 + FIN 12 + EMG 8 + ORG 7 + TEL 6 + DIS 7 + EQP 7 + COM 6 = **60**。

### 2.1 auth（认证，公共）

| 用例 | 方法 | 端点 | 请求 DTO | 响应 DTO |
|---|---|---|---|---|
| UC-COM-001 登录/退出 | POST | /auth/login | LoginRequest | LoginResult |
| UC-COM-001 登录/退出 | POST | /auth/logout | — | — |
| UC-COM-002 修改密码 | POST | /auth/change-password | ChangePasswordRequest | — |

### 2.2 baseinfo（基础信息）

| 用例 | 方法 | 端点 | 请求 DTO | 响应 DTO |
|---|---|---|---|---|
| UC-INF-001 小区/楼栋/单元 | GET/POST/PUT/DELETE | /baseinfo/communities[/{id}] | CommunityRequest | CommunityDto |
| UC-INF-001 小区/楼栋/单元 | GET/POST/PUT/DELETE | /baseinfo/buildings[/{id}]?communityId= | BuildingRequest | BuildingDto |
| UC-INF-001 小区/楼栋/单元 | GET/POST/PUT/DELETE | /baseinfo/units[/{id}]?buildingId= | UnitRequest | UnitDto |
| UC-INF-002 房产档案 | GET/POST/PUT/DELETE | /baseinfo/properties[/{id}] | PropertyRequest | PropertyDto |
| UC-INF-003 业主档案 | GET/POST/PUT/DELETE | /baseinfo/owners[/{id}] | OwnerRequest | OwnerDto |
| UC-INF-003 变更历史 | GET | /baseinfo/owners/{id}/change-logs | — | List\<BaseChangeLogDto\> |
| UC-INF-004 业主-房产关系 | GET/POST/PUT/DELETE | /baseinfo/owner-property-relations[/{id}] | OwnerPropertyRelationRequest | OwnerPropertyRelationDto |
| UC-INF-005 车位档案 | GET/POST/PUT/DELETE | /baseinfo/parking-spaces[/{id}] | ParkingSpaceRequest | ParkingSpaceDto |
| UC-INF-006 导入 | POST | /baseinfo/imports | ImportRequest | ImportResultDto |
| UC-INF-006 导入批次 | GET | /baseinfo/imports/{id} | — | ImportLogDto |
| UC-INF-006 模板 | GET | /baseinfo/imports/template?module= | — | 文件流 |
| UC-INF-006 错误清单 | GET | /baseinfo/imports/{id}/errors | — | 文件流 |
| UC-INF-007 查询（房产） | GET | /baseinfo/properties | BaseInfoQueryRequest | PageResult\<PropertyDto\> |
| UC-INF-007 查询（业主） | GET | /baseinfo/owners | BaseInfoQueryRequest | PageResult\<OwnerDto\> |
| UC-INF-007 查询（车位） | GET | /baseinfo/parking-spaces | BaseInfoQueryRequest | PageResult\<ParkingSpaceDto\> |
| UC-INF-007 导出 | POST | /baseinfo/exports | BaseInfoExportRequest | ExportLogDto |

### 2.3 org（人员组织）

| 用例 | 方法 | 端点 | 请求 DTO | 响应 DTO |
|---|---|---|---|---|
| UC-ORG-001 部门架构 | GET/POST/PUT/DELETE | /org/departments[/{id}] | DepartmentRequest | DepartmentDto |
| UC-ORG-001 岗位 | GET/POST/PUT/DELETE | /org/positions[/{id}] | PositionRequest | PositionDto |
| UC-ORG-002 员工档案 | GET/POST/PUT/DELETE | /org/employees[/{id}] | EmployeeRequest | EmployeeDto |
| UC-ORG-003 排班 | GET/POST | /org/schedules | ScheduleGenerateRequest | SchedulePlanDto |
| UC-ORG-003 发布 | POST | /org/schedules/publish | SchedulePublishRequest | SchedulePlanDto |
| UC-ORG-004 考勤登记 | POST | /org/attendances | AttendanceRequest | AttendanceDto |
| UC-ORG-004 考勤查询 | GET | /org/attendances | AttendanceQueryRequest | PageResult\<AttendanceDto\> |
| UC-ORG-004 异常审核 | POST | /org/attendances/{id}/review | AttendanceReviewRequest | AttendanceDto |
| UC-ORG-005 在岗状态 | POST | /org/employees/{id}/status | EmployeeStatusRequest | EmployeeStatusLogDto |
| UC-ORG-005 状态历史 | GET | /org/employees/{id}/status-logs | — | List\<EmployeeStatusLogDto\> |
| UC-ORG-006 员工查询 | GET | /org/employees | EmployeeQueryRequest | PageResult\<EmployeeDto\> |
| UC-ORG-007 账号查询/维护 | GET/PUT | /org/accounts[/{id}] | UserAccountRequest | UserAccountDto |
| UC-ORG-007 密码重置 | POST | /org/accounts/{id}/reset-password | ResetPasswordRequest | — |

### 2.4 billing（财务-账单）

| 用例 | 方法 | 端点 | 请求 DTO | 响应 DTO |
|---|---|---|---|---|
| UC-FIN-001 收费项目 | GET/POST/PUT/DELETE | /billing/charge-items[/{id}] | ChargeItemRequest | ChargeItemDto |
| UC-FIN-001 计费周期 | GET/POST/PUT/DELETE | /billing/cycles[/{id}] | BillingCycleRequest | BillingCycleDto |
| UC-FIN-002 生成账单 | POST | /billing/bills/generate | BillGenerateRequest | BillGenerateLogDto |
| UC-FIN-002 批次查询 | GET | /billing/bills/generate-logs/{id} | — | BillGenerateLogDto |
| UC-FIN-002 发布 | POST | /billing/bills/publish | BillPublishRequest | BillGenerateLogDto |
| UC-FIN-002 失败清单 | GET | /billing/bills/generate-logs/{id}/failures | — | List\<BillDto\> |
| UC-FIN-002 账单列表 | GET | /billing/bills | BillQueryRequest | PageResult\<BillDto\> |
| UC-FIN-007 欠费台账 | GET | /billing/bills/arrears | BillQueryRequest（arrearsOnly） | PageResult\<ArrearDto\> |
| UC-FIN-008 已缴/未缴统计 | GET | /billing/statistics/payment | — | PaymentStatisticsDto |

### 2.5 payments（财务-收款）

| 用例 | 方法 | 端点 | 请求 DTO | 响应 DTO |
|---|---|---|---|---|
| UC-FIN-003 收款登记 | POST | /payments | PaymentCreateRequest | PaymentDto |
| UC-FIN-003 收款历史 | GET | /payments | PageRequest | PageResult\<PaymentDto\> |
| UC-FIN-003 收款查询 | GET | /payments/{id} | — | PaymentDto |
| UC-FIN-003 预存款 | GET | /payments/pre-deposits/{ownerId} | — | PreDepositDto |
| UC-FIN-003 预存退还 | POST | /payments/pre-deposits/refund | PreDepositRefundRequest | PreDepositDto |
| UC-FIN-004 退款/减免/调整 | POST | /payments/refunds | RefundAdjustmentRequest | RefundAdjustmentDto |
| UC-FIN-004 记录查询 | GET | /payments/refunds | PageRequest | PageResult\<RefundAdjustmentDto\> |
| UC-FIN-011 收据查询 | GET | /payments/receipts/{id} | — | ReceiptDto |
| UC-FIN-011 收据打印/补打 | POST | /payments/receipts/{id}/print | ReceiptPrintRequest | ReceiptDto |

### 2.6 expenses（财务-支出）

| 用例 | 方法 | 端点 | 请求 DTO | 响应 DTO |
|---|---|---|---|---|
| UC-FIN-006 支出分类 | GET/POST/PUT/DELETE | /expenses/categories[/{id}] | ExpenseCategoryRequest | ExpenseCategoryDto |
| UC-FIN-005 支出登记 | POST | /expenses | ExpenseCreateRequest | ExpenseDto |
| UC-FIN-005 支出查询 | GET | /expenses | PageRequest | PageResult\<ExpenseDto\> |
| UC-FIN-005 支出修改/软删 | PUT/DELETE | /expenses/{id} | ExpenseCreateRequest | ExpenseDto |

### 2.7 reports（财务-报表）

| 用例 | 方法 | 端点 | 请求 DTO | 响应 DTO |
|---|---|---|---|---|
| UC-FIN-010 收支流水 | GET | /reports/ledger | LedgerQueryRequest | PageResult\<LedgerEntryDto\> |
| UC-FIN-009 财务报表 | GET | /reports/financial | FinancialReportQueryRequest | FinancialReportDto |
| UC-FIN-012 导出报表 | POST | /reports/export | ReportExportRequest | ReportLogDto |
| UC-FIN-012 文件下载 | GET | /reports/files/{logId} | — | 文件流 |

### 2.8 emergency（应急处置）

| 用例 | 方法 | 端点 | 请求 DTO | 响应 DTO |
|---|---|---|---|---|
| UC-EMG-001 应急场景 | GET/POST/PUT/DELETE | /emergency/scenes[/{id}] | EmergencySceneRequest | EmergencySceneDto |
| UC-EMG-002 处置步骤 | GET | /emergency/scenes/{id}/steps | — | List\<EmergencyStepDto\> |
| UC-EMG-002 步骤维护 | POST/PUT/DELETE | /emergency/steps[/{id}] | EmergencyStepRequest | EmergencyStepDto |
| UC-EMG-003 发起事件 | POST | /emergency/events | EmergencyEventCreateRequest | EmergencyEventDto |
| UC-EMG-004 责任/值班匹配 | POST | /emergency/events/{id}/assign | EmergencyAssignRequest | List\<EmergencyAssignDto\> |
| UC-EMG-005 处置记录 | POST | /emergency/events/{id}/records | EmergencyRecordRequest | EmergencyRecordDto |
| UC-EMG-005/008 事件详情 | GET | /emergency/events/{id} | — | EmergencyEventDetailDto |
| UC-EMG-006 结案 | POST | /emergency/events/{id}/close | EmergencyCloseRequest | EmergencyEventDto |
| UC-EMG-007 复盘 | POST | /emergency/events/{id}/review | EmergencyReviewRequest | EmergencyReviewDto |
| UC-EMG-008 历史查询 | GET | /emergency/events | EmergencyEventQueryRequest | PageResult\<EmergencyEventDto\> |

### 2.9 phonebook（便民电话簿）

| 用例 | 方法 | 端点 | 请求 DTO | 响应 DTO |
|---|---|---|---|---|
| UC-TEL-001 电话分类 | GET/POST/PUT/DELETE | /phonebook/categories[/{id}] | PhoneCategoryRequest | PhoneCategoryDto |
| UC-TEL-002 电话条目 | GET/POST/PUT | /phonebook/entries[/{id}] | PhoneEntryRequest | PhoneEntryDto |
| UC-TEL-003 查询/搜索 | GET | /phonebook/entries | PhoneEntryQueryRequest | PageResult\<PhoneEntryDto\> |
| UC-TEL-004 常用/置顶 | PUT | /phonebook/entries/{id}/top | — | PhoneEntryDto |
| UC-TEL-005 员工同步 | POST | /phonebook/sync-employees | EmployeeSyncRequest | EmployeeSyncResultDto |
| UC-TEL-006 停用条目 | PUT | /phonebook/entries/{id}/status | PhoneEntryStatusRequest | PhoneEntryDto |

### 2.10 disputes（民事纠纷调解）

| 用例 | 方法 | 端点 | 请求 DTO | 响应 DTO |
|---|---|---|---|---|
| UC-DIS-001 登记纠纷 | POST | /disputes/cases | DisputeCaseCreateRequest | DisputeCaseDto |
| UC-DIS-002 维护信息 | PUT | /disputes/cases/{id} | DisputeCaseUpdateRequest | DisputeCaseDto |
| UC-DIS-002/003 案件详情 | GET | /disputes/cases/{id} | — | DisputeCaseDetailDto |
| UC-DIS-003 处理方案 | POST | /disputes/cases/{id}/records | DisputeRecordRequest | DisputeRecordDto |
| UC-DIS-004 更新进度 | PUT | /disputes/cases/{id}/status | DisputeCaseStatusRequest | DisputeCaseDto |
| UC-DIS-005 结案归档 | POST | /disputes/cases/{id}/close | DisputeCloseRequest | DisputeCaseDto |
| UC-DIS-006 查询 | GET | /disputes/cases | DisputeQueryRequest | PageResult\<DisputeCaseDto\> |
| UC-DIS-006 统计 | GET | /disputes/statistics | — | DisputeStatisticsDto |
| UC-DIS-007 结案后补录 | POST | /disputes/cases/{id}/records | DisputeRecordRequest（IsSupplement=true） | DisputeRecordDto |

### 2.11 equipment（设备资产台账）

| 用例 | 方法 | 端点 | 请求 DTO | 响应 DTO |
|---|---|---|---|---|
| UC-EQP-001 设备台账 | GET/POST/PUT/DELETE | /equipment/devices[/{id}] | DeviceRequest | DeviceDto |
| UC-EQP-001 设备类型 | GET/POST/PUT/DELETE | /equipment/device-types[/{id}] | DeviceTypeRequest | DeviceTypeDto |
| UC-EQP-001 状态变更 | POST | /equipment/devices/{id}/status | DeviceStatusRequest | DeviceDto |
| UC-EQP-002 保养登记 | POST | /equipment/maintenance-records | MaintenanceRecordRequest | MaintenanceRecordDto |
| UC-EQP-002 保养查询 | GET | /equipment/devices/{id}/maintenance | — | List\<MaintenanceRecordDto\> |
| UC-EQP-003 年检登记 | POST | /equipment/inspection-records | InspectionRecordRequest | InspectionRecordDto |
| UC-EQP-003 年检查询 | GET | /equipment/devices/{id}/inspections | — | List\<InspectionRecordDto\> |
| UC-EQP-004 故障登记 | POST | /equipment/fault-records | FaultRecordRequest | FaultRecordDto |
| UC-EQP-004 故障查询 | GET | /equipment/devices/{id}/faults | — | List\<FaultRecordDto\> |
| UC-EQP-005 维保单位 | GET/POST/PUT/DELETE | /equipment/vendors[/{id}] | VendorRequest | VendorDto |
| UC-EQP-006 到期提醒 | GET | /equipment/reminders | — | List\<EquipmentReminderDto\> |
| UC-EQP-007 查询档案 | GET | /equipment/devices | DeviceQueryRequest | PageResult\<DeviceDto\> |

### 2.12 common（公共支撑）

| 用例 | 方法 | 端点 | 请求 DTO | 响应 DTO |
|---|---|---|---|---|
| UC-COM-003 审计日志 | GET | /common/audit-logs | AuditLogQueryRequest | PageResult\<AuditLogDto\> |
| UC-COM-004 字典类型 | GET | /common/dict-types | — | List\<DictTypeDto\> |
| UC-COM-004 字典项 | GET/POST/PUT/DELETE | /common/dict-items[/{id}]?typeCode= | DictItemRequest | DictItemDto |
| UC-COM-004 参数查询 | GET | /common/params | — | List\<ParamDto\> |
| UC-COM-004 参数修改 | PUT | /common/params | ParamUpdateRequest | ParamDto |
| UC-COM-005 手动备份 | POST | /common/backups | BackupCreateRequest | BackupDto |
| UC-COM-005 备份列表 | GET | /common/backups | PageRequest | PageResult\<BackupDto\> |
| UC-COM-005 恢复 | POST | /common/backups/{id}/restore | BackupRestoreRequest | — |
| UC-COM-006 仪表盘 | GET | /common/dashboard | — | DashboardDto |

## 三、关键业务规则落点（契约校验要求）

> 规则明细见 [AM-06 业务规则清单](../models/分析模型-v0.1/AM-06-业务规则清单.md)；后端实现（M2）逐条落地，契约层仅声明约束。

| 规则 | 落点端点 | 校验要求 |
|---|---|---|
| BR-FIN-01 同对象同周期唯一 | /billing/bills/generate | 重复生成返回 40900 |
| BR-FIN-06 退款不超实缴 | /payments/refunds | 超额返回 42200 |
| BR-INF-02 一房一业主 | /baseinfo/owner-property-relations | 冲突返回 40900 |
| BR-INF-03 固定车位唯一绑定 | /baseinfo/parking-spaces | 冲突返回 40900 |
| BR-EMG-03 值班匹配 | /emergency/events/{id}/assign | 无匹配不阻塞（P-07），发起人为默认第一处置人 |
| P-06 预存款简单版 | /payments、/payments/pre-deposits/* | 超额自动转存、自动抵扣、余额退还 |
| P-09 备份保留 30 份 | /common/backups | 超量自动清理 |
| 敏感操作审计（BR-COM-01 等） | 收款/退款/支出/结案/导入/备份/字典 | 后端写 t_audit_log |

## 四、错误码清单（契约）

| HTTP | code | 含义 |
|---|---|---|
| 200 | 0 | 成功 |
| 400 | 40000 | 请求格式错误/参数缺失 |
| 401 | 40100 | token 无效或过期 |
| 401 | 40101 | 登录失败次数超限，账号锁定（P-02） |
| 403 | 40300 | 无权限（唯一管理员体系下保留扩展） |
| 404 | 40400 | 资源不存在 |
| 409 | 40900 | 业务冲突（唯一性/状态不允许） |
| 422 | 42200 | 业务校验失败（规则 BR 不通过） |
| 500 | 50000 | 服务器内部错误 |
| 503 | 50300 | 服务不可用（数据库/文件异常） |

> 业务细分错误码在 M2 实现时按"子系统码(2位) + 错误码(3位)"扩展（如 FIN-001），保持 `code != 0` 即失败、前端按 code 提示的统一约定。

## 五、契约与基线对齐说明

- **与 DM-03 对齐**：每个领域实体（ChargeItem/Bill/Payment/Receipt/RefundAdjustment/PreDeposit/Expense/EmergencyEvent/DisputeCase/Device 等）均有对应 DTO，字段取自 DM-03 类图职责；
- **与 DM-04 对齐**：DTO 字段名/类型与 60 张表关键字段一致（下划线转驼峰），删除统一 `del_flag`、审计时间 `created_at/updated_at`；
- **与 AM-05 对齐**：27 个枚举覆盖 7 类状态模型（账单/应急/纠纷/设备/员工/考勤/电话条目）；
- **状态日志表**：60 张表中 6 张"只追加"状态日志表均有对应 DTO（BillStatusLogDto / EmergencyEventStatusLogDto / DisputeStatusLogDto / DeviceStatusLogDto / EmployeeStatusLogDto / BaseChangeLogDto）；
- **与 AM-02 对齐**：60 个用例每个至少映射一个端点（§二覆盖核对通过）；
- **枚举序列化**：契约约定枚举以字符串传输，M2 Startup 增加 `StringEnumConverter`。

## 六、评审确认清单

1. 60 用例 → 端点映射是否覆盖完整、命名是否认可？
2. 通用规范（鉴权/分页/时间/枚举/删除/审计/导入导出）是否认可？
3. 错误码约定是否认可？
4. DTO 命名与字段是否与界面原型（PG-XXX）可对应？
5. 契约确认后进入实施：按 DZ-2 选定先做 M2（后端）或 M3（前端）；契约变更走 CHG。

## 七、评审记录（D1-3，2026-08-28）

> 评审方式：由 AI 代项目负责人评审（项目负责人授权），对照模型基线逐项核查。

| # | 评审发现 | 结论/处置 |
|---|---|---|
| 1 | 60 用例 → 端点映射 | ✅ 程序化核对 60/60 覆盖，无缺失 |
| 2 | 通用规范（鉴权/分页/时间/枚举/删除/审计/导入导出） | ✅ 符合技术选型与 DM-07；枚举序列化约定在 M2 落地 |
| 3 | 错误码约定 | ✅ 与异常路径核对清单口径一致（公共段 4xxxx/5xxxx，业务细分码 M2 扩展） |
| 4 | DTO 与 DM-04 对齐 | ✅ 60 表均有对应 DTO；本次补充 3 个状态日志 DTO（EmergencyEventStatusLogDto/DisputeStatusLogDto/DeviceStatusLogDto） |
| 5 | 列表查询端点缺口 | ✅ 本次补充：GET /baseinfo/owners、GET /baseinfo/parking-spaces、GET /payments、AttendanceQueryRequest |
| 6 | 账号/考勤 DTO 完整 | ✅ 本次补充：UserAccountDto、AttendanceQueryRequest |
| 7 | 收费项目停用 | ✅ ChargeItemRequest 增加 Status（停用不影响已出账单） |
| 8 | 原型页面对应 | ✅ 36 页关键页抽查（收款/应急发起/备份/导入/到期提醒/报表/欠费/电话查询）均有对应端点 |
| 9 | 契约文档引用完整性 | ✅ 全部引用的 DTO/Request 类型在 Contract 代码中存在（程序化核对） |

**评审结论：通过，契约定稿（v0.1）。** 后续契约变更走 CHG。
