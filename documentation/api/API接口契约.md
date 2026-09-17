# API 接口契约

> 适用版本：安怡物业管理系统 1.0.0 ｜ 基址：`http://127.0.0.1:5210/api/v1`（仅绑定回环地址）
> 契约代码：`PropertyManagement.Contract`（前后端同源引用，DTO/枚举/错误码）
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
- 「批量删除」同样只置 `del_flag = 1`（软删留痕），记录立即从列表/流水账消失，物理清理由
  `POST /system/cleanup/soft-deleted`（一键清理残余数据）统一执行：逐表物理删除 `del_flag = 1` 行，
  并同步清理「随父行作废、自身无 `del_flag` 列」的纯子记录（导入错误行、支出关联对象）；
- 敏感操作（收款/退款/支出/结案/导入/备份/字典修改等）由后端写入 `t_audit_log`，前端不可修改；
- **删除入口的跨模块引用校验（v1.1.0 R1）**：房产/业主/车位/设备删除前先在服务层校验其它模块的在用引用
  （房产：业主关系 / 未删除账单 / 绑定车位；业主：关系 / 名下车位 / 预存款 / 纠纷当事人；
  车位：未删除账单；设备：保养/年检/故障/自定义记录、支出引用），命中即 `409 / 40900` 并返回中文处置提示，
  以保证「能删 ⇒ 必无在用子行」，使一键清理回收留痕父行后不产生孤儿引用；
- 导出/打印留痕：`t_export_log` / `t_print_log`，补打保留原收据号并递增打印次数（BR-FIN-08）。

### 1.6 导入/导出/文件

- 导入：`multipart/form-data` 上传 Excel（模块类型见 `ImportModule`）；校验不通过不入库，错误清单可下载重传（BR-INF-05）；
  - **模板必填矩阵（v1.1.0）**：房产＝楼栋号/房号/建筑面积（**单元号选填**、**已移除「用途」列**）；车位＝车位编号；业主＝姓名（**联系电话选填**）；业主-房产关系＝楼栋号/房号/业主姓名（**单元号选填**）；
  - **表头驱动**：导入按表头名识别列（列序可调整、V1.0.0 旧模板继续可用）；表头不匹配返回 `42200` 并提示缺少列；
  - **示例行**：模板第 2 行为示例（首列「示例：」开头），导入时自动跳过、不计入批次 `total`；另附「填写说明」工作表；
  - **面积口径**：`area` 保留原始精度（前端最多 2 位小数录入、展示不四舍五入）；计费按全精度面积 × 单价后对金额四舍五入到分；
  - **数值往返**：`POST/PUT /baseinfo/properties` 的 `area` 原值写入、原值回读（不做四舍五入）；
  - **软删留痕与唯一性**：楼栋/单元/房产/账单的唯一约束均为 `del_flag = 0` 部分唯一索引，软删留痕后可重建同键；唯一约束冲突返回 `40900`（HTTP 409）并给出可读提示；
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
| UC-INF-006 批次记录批量删除（v1.1.0） | POST | /baseinfo/imports/batch-delete | RecordBatchDeleteRequest | RecordBatchDeleteResultDto |
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
| UC-FIN-001 收费项目（含 **objectCode** 缴费对象字典编码：property/parking/owner 为系统固定项、其余为自定义项；objectType 0 房产/1 车位/2 业主/3 自定义，null 时按计价方式派生） | GET/POST/PUT/DELETE | /billing/charge-items[/{id}] | ChargeItemRequest | ChargeItemDto |
| UC-FIN-001 计费周期 | GET/POST/PUT/DELETE | /billing/cycles[/{id}] | BillingCycleRequest | BillingCycleDto |
| UC-FIN-001 删除计费周期（仅自定义周期；内置周期或已被账单引用 → 42200） | DELETE | /billing/cycles/{id} | — | — |
| UC-FIN-002 生成账单（**校验缴费对象类型与收费项目一致**，不一致返回 42200；**同对象同周期同项目允许重复出账**） | POST | /billing/bills/generate | BillGenerateRequest | BillGenerateLogDto |
| UC-FIN-002 生成账单（**自定义缴费对象**：`customPayerNames` 手工填写名称，一行一张账单；与房产/车位/业主口径互斥） | POST | /billing/bills/generate | BillGenerateRequest | BillGenerateLogDto |
| UC-FIN-002 缴费对象候选 | GET | /billing/bill-objects?kind=property/parking/owner&keyword= | BillObjectQueryRequest | BillObjectQueryResult |
| UC-FIN-002 批次查询 | GET | /billing/bills/generate-logs/{id} | — | BillGenerateLogDto |
| UC-FIN-002 批次缴费对象 | GET | /billing/bills/generate-logs/{id}/objects | — | BillObjectSelectionDto |
| UC-FIN-002 发布 | POST | /billing/bills/publish | BillPublishRequest | BillGenerateLogDto |
| UC-FIN-002 失败清单 | GET | /billing/bills/generate-logs/{id}/failures | — | List\<BillDto\> |
| UC-FIN-002 失败对象重推（FL-FIN-01：按失败清单重新出账，并收敛源批次失败清单；全部成功 → 源批次状态「已重推」Retried） | POST | /billing/bills/generate-logs/{id}/retry | BillRetryRequest | BillGenerateLogDto |
| UC-FIN-002 账单列表 | GET | /billing/bills（可按 ownerId / **payerOwnerId** / **payerName** 取账单；payerOwnerId＝按缴费人取全部欠费，payerName＝按自定义缴费对象名称取全部欠费） | BillQueryRequest | PageResult\<BillDto\> |
| UC-FIN-007 欠费台账 | GET | /billing/bills/arrears | BillQueryRequest（arrearsOnly） | PageResult\<ArrearDto\> |
| UC-FIN-008 已缴/未缴统计 | GET | /billing/statistics/payment | — | PaymentStatisticsDto |

### 2.5 payments（财务-收款）

| 用例 | 方法 | 端点 | 请求 DTO | 响应 DTO |
|---|---|---|---|---|
| UC-FIN-003 收款登记 | POST | /payments | PaymentCreateRequest | PaymentDto |
| UC-FIN-003 统一收款（多账单） | POST | /payments/batch | PaymentBatchCreateRequest | PaymentBatchResultDto |
| UC-FIN-003 收款历史 | GET | /payments | PageRequest | PageResult\<PaymentDto\> |
| UC-FIN-003 收款查询 | GET | /payments/{id} | — | PaymentDto |
| UC-FIN-003 预存款 | GET | /payments/pre-deposits/{ownerId} | — | PreDepositDto |
| UC-FIN-003 预存退还 | POST | /payments/pre-deposits/refund | PreDepositRefundRequest | PreDepositDto |
| UC-FIN-004 退款/减免/调整 | POST | /payments/refunds | RefundAdjustmentRequest | RefundAdjustmentDto |
| UC-FIN-004 批量退款/减免/调整 | POST | /payments/refunds/batch | RefundAdjustmentRequest（billIds 多账单） | RefundBatchResultDto |
| UC-FIN-004 记录查询 | GET | /payments/refunds | PageRequest | PageResult\<RefundAdjustmentDto\> |
| UC-FIN-011 收据查询（v1.1.0 第 15 轮起下线） | ~~GET /payments/receipts/{id}~~ | 收据号下线，改为导出打印模板 | — | — |
| UC-FIN-011 收据打印/补打（v1.1.0 第 15 轮起下线） | ~~POST /payments/receipts/{id}/print~~ | 同上 | — | — |

### 2.6 expenses（财务-支出）

| 用例 | 方法 | 端点 | 请求 DTO | 响应 DTO |
|---|---|---|---|---|
| UC-FIN-006 支出分类 | GET/POST/PUT/DELETE | /expenses/categories[/{id}] | ExpenseCategoryRequest | ExpenseCategoryDto |
| UC-FIN-005 支出登记 | POST | /expenses | ExpenseCreateRequest | ExpenseDto |
| UC-FIN-005 支出查询 | GET | /expenses | PageRequest | PageResult\<ExpenseDto\> |
| UC-FIN-005 支出修改/软删 | PUT/DELETE | /expenses/{id} | ExpenseCreateRequest | ExpenseDto |
| UC-FIN-005 支出批量删除（v1.1.0） | POST | /expenses/batch-delete | RecordBatchDeleteRequest | RecordBatchDeleteResultDto |

### 2.7 reports（财务-报表）

| 用例 | 方法 | 端点 | 请求 DTO | 响应 DTO |
|---|---|---|---|---|
| UC-FIN-010 收支流水（keyword 支持 **单据号/流水号 + 付款人 + 项目**；付款人列优先取自定义缴费对象名称） | GET | /reports/ledger | LedgerQueryRequest | PageResult\<LedgerEntryDto\> |
| UC-FIN-009 财务报表 | GET | /reports/financial | FinancialReportQueryRequest | FinancialReportDto |
| UC-FIN-012 导出报表 | POST | /reports/export | ReportExportRequest | ReportLogDto |
| UC-FIN-011 导出收据打印模板（**hideObjectColumn=true 且全部明细对象文本＝缴款人时过滤「缴费对象」列**，四列布局） | POST | /reports/receipt-template | ReceiptTemplateRequest | ReportLogDto |
| 导出文件下载（报表/收据模板） | GET | /reports/files/{logId} | — | 文件流 |
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
| UC-COM-004 缴费对象字典（`charge_object`：房产/车位/业主＝**系统固定项**，不可停用/删除；其余自定义项在被收费项目引用时不可删除） | GET/POST | /dicts/charge_object · /dicts/charge_object/items · /system/dict-types/charge_object/items · /system/dict-items/{id}/status · /system/dict-items/batch-delete | DictItemRequest | DictItemDto |
| UC-COM-004 参数查询 | GET | /common/params | — | List\<ParamDto\> |
| UC-COM-004 参数修改 | PUT | /common/params | ParamUpdateRequest | ParamDto |
| UC-COM-005 手动备份 | POST | /common/backups | BackupCreateRequest | BackupDto |
| UC-COM-005 备份列表 | GET | /common/backups | PageRequest | PageResult\<BackupDto\> |
| UC-COM-005 恢复 | POST | /common/backups/{id}/restore | BackupRestoreRequest | — |
| UC-COM-006 仪表盘 | GET | /common/dashboard | — | DashboardDto |

## 三、关键业务规则落点（契约校验要求）

> 规则明细见 AM-06 业务规则清单；后端实现（M2）逐条落地，契约层仅声明约束。

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
