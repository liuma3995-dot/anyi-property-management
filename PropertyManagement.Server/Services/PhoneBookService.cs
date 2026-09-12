using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.PhoneBook;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 便民电话簿服务（M6 D6-3，UC-TEL-001~006）。
    /// 业务规则：BR-TEL-01（员工条目来自人员组织、同步幂等）、BR-TEL-02（停用保留不展示）、
    /// BR-TEL-03（紧急号码 119/120/110 置顶禁删禁停用）、BR-TEL-04（号码格式校验）。
    /// </summary>
    public class PhoneBookService
    {
        /// <summary>紧急号码（BR-TEL-03）：去分隔符后比对。</summary>
        private static readonly string[] EmergencyNumbers = { "119", "120", "110" };

        private static readonly Regex MobileRegex = new Regex("^1\\d{10}$");
        private static readonly Regex LandlineRegex = new Regex("^(0\\d{2,3})?\\d{7,8}$");
        private static readonly Regex ShortNumberRegex = new Regex("^\\d{3,5}$");

        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IPhoneBookRepository _repo;

        public PhoneBookService()
            : this(new SqliteConnectionFactory(), new SqlPhoneBookRepository())
        {
        }

        public PhoneBookService(IDbConnectionFactory connectionFactory, IPhoneBookRepository repo)
        {
            _connectionFactory = connectionFactory;
            _repo = repo;
        }

        public List<PhoneCategoryDto> ListCategories() =>
            WithConnection(c => _repo.ListCategories(c));

        public PhoneCategoryDto SaveCategory(int id, PhoneCategoryRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Name)) throw ApiException.ValidationFailed("分类名称不能为空");
                string name = request.Name.Trim();
                var cats = _repo.ListCategories(c);
                if (cats.Any(x => x.Id != id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                    throw ApiException.Conflict("分类名称已存在：" + name);
                if (id > 0)
                {
                    var dto = new PhoneCategoryDto { Id = id, Name = name, Sort = request.Sort };
                    _repo.UpdateCategory(c, tx, dto);
                    return dto;
                }
                var created = new PhoneCategoryDto { Name = name, Sort = request.Sort };
                created.Id = _repo.InsertCategory(c, tx, created);
                return created;
            });

        /// <summary>删除分类（软删）：若仍有启用条目引用则拒绝，提示先停用；空分类可直接删除。</summary>
        public void DeleteCategory(int id) =>
            WithTransaction((c, tx) =>
            {
                var cat = _repo.ListCategories(c).FirstOrDefault(x => x.Id == id);
                if (cat == null) throw ApiException.NotFound("电话分类不存在");
                int refCount = _repo.CountEnabledEntriesByCategory(c, id);
                if (refCount > 0)
                    throw ApiException.Conflict("该分类下仍有 " + refCount + " 条启用条目，请先停用相关条目后再删除分类");
                _repo.DeleteCategory(c, tx, id);
                return true;
            });

        public List<PhoneTypeDto> ListTypes() =>
            WithConnection(c => _repo.ListTypes(c));

        public PhoneTypeDto SaveType(int id, PhoneTypeRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Name)) throw ApiException.ValidationFailed("类型名称不能为空");
                string name = request.Name.Trim();
                var types = _repo.ListTypes(c);
                if (types.Any(x => x.Id != id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                    throw ApiException.Conflict("类型名称已存在：" + name);
                if (id > 0)
                {
                    var dto = new PhoneTypeDto { Id = id, Name = name, Sort = request.Sort };
                    _repo.UpdateType(c, tx, dto);
                    return dto;
                }
                var created = new PhoneTypeDto { Name = name, Sort = request.Sort };
                created.Id = _repo.InsertType(c, tx, created);
                return created;
            });

        /// <summary>删除类型（软删）：若仍有启用条目引用则拒绝，提示先停用；空类型可直接删除。</summary>
        public void DeleteType(int id) =>
            WithTransaction((c, tx) =>
            {
                var type = _repo.ListTypes(c).FirstOrDefault(x => x.Id == id);
                if (type == null) throw ApiException.NotFound("电话类型不存在");
                int refCount = _repo.CountEnabledEntriesByType(c, id);
                if (refCount > 0)
                    throw ApiException.Conflict("该类型下仍有 " + refCount + " 条启用条目，请先停用相关条目后再删除类型");
                _repo.DeleteType(c, tx, id);
                return true;
            });

        public PageResult<PhoneEntryDto> QueryEntries(PhoneEntryQueryRequest query) =>
            WithConnection(c => _repo.QueryEntries(c, query ?? new PhoneEntryQueryRequest { PageIndex = 1, PageSize = 20 }, out int total));

        public PhoneEntryDto GetEntry(int id) =>
            WithConnection(c => _repo.GetEntry(c, id) ?? throw ApiException.NotFound("电话条目不存在"));

        public PhoneEntryDto SaveEntry(int id, PhoneEntryRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null) throw ApiException.ValidationFailed("请求内容不能为空");
                if (request.CategoryId <= 0) throw ApiException.ValidationFailed("请选择分类");
                if (string.IsNullOrWhiteSpace(request.Name)) throw ApiException.ValidationFailed("名称不能为空");
                if (string.IsNullOrWhiteSpace(request.Phone)) throw ApiException.ValidationFailed("号码不能为空");
                if (id <= 0 && request.EntryType == PhoneEntryType.Employee && !request.EmployeeId.HasValue)
                    throw ApiException.ValidationFailed("员工条目只能通过\"同步员工通讯录\"生成，不可手工新增（BR-TEL-01）");
                string name = request.Name.Trim();
                string phone = request.Phone.Trim();
                ValidatePhone(phone, request.EntryType == PhoneEntryType.Emergency);
                var cats = _repo.ListCategories(c);
                if (cats.Count == 0) throw ApiException.ValidationFailed("请先维护电话分类");
                if (cats.All(x => x.Id != request.CategoryId))
                    throw ApiException.ValidationFailed("所选分类不存在或已停用，请先维护电话分类");
                // 类型：优先按 type_id 解析（自定义类型归为普通），否则沿用请求的枚举
                PhoneEntryType entryType = request.EntryType;
                int? typeId = request.TypeId;
                if (request.TypeId.HasValue && request.TypeId.Value > 0)
                {
                    var type = _repo.ListTypes(c).FirstOrDefault(x => x.Id == request.TypeId.Value);
                    if (type != null) entryType = MapTypeName(type.Name);
                    else typeId = null;
                }
                var dup = _repo.GetEntryByPhone(c, phone, id);
                if (dup != null)
                    throw ApiException.Conflict("号码已存在：" + (string.IsNullOrWhiteSpace(dup.Name) ? "未知条目" : dup.Name));
                string namePinyin = ResolveNamePinyin(request.NamePinyin, name);
                string nameInitials = ResolveNameInitials(request.NameInitials, name, namePinyin);
                bool isEmergency = entryType == PhoneEntryType.Emergency || IsEmergencyNumber(NormalizePhone(phone));
                bool isTop = isEmergency || request.IsTop;
                if (id > 0)
                {
                    var existing = _repo.GetEntry(c, id) ?? throw ApiException.NotFound("电话条目不存在");
                    entryType = request.EntryType;
                    if (IsEmergencyEntry(existing))
                    {
                        entryType = PhoneEntryType.Emergency;
                        isTop = true;
                    }
                    if (entryType == PhoneEntryType.Emergency) typeId = typeId ?? ResolveTypeIdByEnum(c, PhoneEntryType.Emergency);
                    // 编辑保留原状态与停用来源（BR-TEL-02：离职"自动停用"条目改备注不得被擅自启用）
                    var dto = new PhoneEntryDto
                    {
                        Id = id, CategoryId = request.CategoryId, EntryType = entryType, TypeId = typeId,
                        EmployeeId = existing.EmployeeId, Name = name, Phone = phone, Note = request.Note ?? string.Empty,
                        IsTop = isTop, Status = existing.Status, DisableSource = existing.DisableSource,
                        NamePinyin = namePinyin, NameInitials = nameInitials
                    };
                    _repo.UpdateEntry(c, tx, dto);
                    return _repo.GetEntry(c, id);
                }
                var created = new PhoneEntryDto
                {
                    CategoryId = request.CategoryId, EntryType = entryType, TypeId = typeId, EmployeeId = request.EmployeeId,
                    Name = name, Phone = phone, Note = request.Note ?? string.Empty,
                    IsTop = isTop, Status = PhoneEntryStatus.Enabled, DisableSource = 0,
                    NamePinyin = namePinyin, NameInitials = nameInitials
                };
                created.Id = _repo.InsertEntry(c, tx, created);
                return _repo.GetEntry(c, created.Id);
            });

        public PhoneEntryDto SetEntryStatus(int id, PhoneEntryStatusRequest request) =>
            WithTransaction((c, tx) =>
            {
                var existing = _repo.GetEntry(c, id) ?? throw ApiException.NotFound("电话条目不存在");
                PhoneEntryStatus status = request != null ? request.Status : PhoneEntryStatus.Enabled;
                if (status == PhoneEntryStatus.Disabled && IsEmergencyEntry(existing))
                    throw ApiException.Conflict("紧急电话（119/120/110）不可停用（BR-TEL-03）");
                // 手工停用/启用：disable_source 归 0（离职联动停用只走同步与 ORG 离职事务路径）
                _repo.SetEntryStatus(c, tx, id, status, 0);
                return _repo.GetEntry(c, id);
            });

        public PhoneEntryDto SetEntryTop(int id, bool isTop) =>
            WithTransaction((c, tx) =>
            {
                var existing = _repo.GetEntry(c, id) ?? throw ApiException.NotFound("电话条目不存在");
                if (!isTop && IsEmergencyEntry(existing))
                    throw ApiException.Conflict("紧急电话默认置顶，不可取消（BR-TEL-03）");
                _repo.SetEntryTop(c, tx, id, isTop);
                return _repo.GetEntry(c, id);
            });

        /// <summary>批量停用（批量停用=删除口径）：选中的启用条目一律置为停用（含紧急号码）。</summary>
        public PhoneEntryBatchStatusResultDto BatchDisable(PhoneEntryBatchStatusRequest request) =>
            WithTransaction((c, tx) =>
            {
                var result = new PhoneEntryBatchStatusResultDto();
                var ids = (request?.Ids ?? new List<int>()).Where(id => id > 0).Distinct().ToList();
                foreach (int id in ids)
                {
                    var entry = _repo.GetEntry(c, id);
                    if (entry == null || entry.Status != PhoneEntryStatus.Enabled) continue;
                    _repo.SetEntryStatus(c, tx, id, PhoneEntryStatus.Disabled, 0);
                    result.Disabled++;
                }
                return result;
            });

        /// <summary>
        /// 同步员工通讯录（UC-TEL-005，BR-TEL-01 幂等）：
        /// 在岗员工按 employee_id 匹配（存量无 employee_id 条目按 name+entry_type=2 兜底匹配后回填），
        /// 命中即回写 name/phone/note（离职联动停用的条目随员工恢复在岗重新启用并清 disable_source；
        /// 手工停用的不擅自启用）；离职/离岗（del_flag=1 OR status&lt;&gt;0）→ 条目停用 + disable_source=1。
        /// 与 ORG 离职事务内直接 UPDATE t_phone_entry SET status=1, disable_source=1 WHERE employee_id=@id 的行为兼容：
        /// 已被联动停用的条目不会重复计数，也不会被重新启用。
        /// </summary>
        public EmployeeSyncResultDto SyncEmployees(EmployeeSyncRequest request) =>
            WithTransaction((c, tx) =>
            {
                var cats = _repo.ListCategories(c);
                if (cats.Count == 0) throw ApiException.ValidationFailed("请先维护电话分类");
                int requestedCategoryId = request != null ? request.CategoryId : 0;
                int categoryId;
                if (requestedCategoryId <= 0)
                {
                    var defaultCat = cats.FirstOrDefault(x => x.Name == "物业服务中心") ?? cats[0];
                    categoryId = defaultCat.Id;
                }
                else if (cats.All(x => x.Id != requestedCategoryId))
                {
                    throw ApiException.ValidationFailed("所选分类不存在或已停用，请先维护电话分类");
                }
                else
                {
                    categoryId = requestedCategoryId;
                }
                // 在岗：del_flag=0 AND status=0
                var onDuty = c.Query<EmpRow>(
                    "SELECT id AS Id, name AS Name, phone AS Phone FROM t_employee WHERE del_flag = 0 AND status = 0").ToList();
                int employeeTypeId = _repo.ListTypes(c).FirstOrDefault(x => x.Name == "员工通讯录")?.Id ?? 0;
                // 离职/离岗：del_flag=1 OR status<>0（status IS NULL 视为未明确在岗，不在两份清单中，条目保持原状）
                var leftEmployees = c.Query<EmpRow>(
                    "SELECT id AS Id, name AS Name FROM t_employee WHERE del_flag = 1 OR (status IS NOT NULL AND status <> 0)").ToList();
                int synced = 0, updated = 0, disabled = 0, skipped = 0;
                foreach (var emp in onDuty)
                {
                    string name = (emp.Name ?? string.Empty).Trim();
                    string phone = (emp.Phone ?? string.Empty).Trim();
                    if (name.Length == 0 || phone.Length == 0) { skipped++; continue; }
                    var existing = _repo.GetEntryByEmployeeId(c, emp.Id) ?? _repo.FindLegacyEmployeeEntryByName(c, name);
                    string namePinyin = BuildNamePinyin(name);
                    string nameInitials = BuildNameInitialsFromName(name, namePinyin);
                    if (existing == null)
                    {
                        _repo.InsertEntry(c, tx, new PhoneEntryDto
                        {
                            CategoryId = categoryId, EntryType = PhoneEntryType.Employee, TypeId = employeeTypeId > 0 ? employeeTypeId : (int?)null, EmployeeId = emp.Id,
                            Name = name, Phone = phone, Note = "员工通讯录", IsTop = false,
                            Status = PhoneEntryStatus.Enabled, DisableSource = 0,
                            NamePinyin = namePinyin, NameInitials = nameInitials
                        });
                        synced++;
                    }
                    else
                    {
                        // 回写 name/phone/note；仅离职联动停用（disable_source=1）且员工恢复在岗时重新启用
                        var dto = new PhoneEntryDto
                        {
                            Id = existing.Id, CategoryId = existing.CategoryId, EntryType = PhoneEntryType.Employee, TypeId = employeeTypeId > 0 ? employeeTypeId : (int?)null,
                            EmployeeId = emp.Id, Name = name, Phone = phone,
                            Note = string.IsNullOrWhiteSpace(existing.Note) ? "员工通讯录" : existing.Note,
                            IsTop = existing.IsTop,
                            Status = PhoneEntryStatus.Enabled,
                            DisableSource = 0,
                            NamePinyin = namePinyin, NameInitials = nameInitials
                        };
                        _repo.UpdateEntry(c, tx, dto);
                        updated++;
                    }
                }
                foreach (var emp in leftEmployees)
                {
                    var entry = _repo.GetEntryByEmployeeId(c, emp.Id);
                    if (entry == null)
                    {
                        // 存量未回填 employee_id 的员工条目按姓名兜底（在岗回填先行，避免同名误伤）
                        string name = (emp.Name ?? string.Empty).Trim();
                        if (name.Length == 0) continue;
                        entry = _repo.FindLegacyEmployeeEntryByName(c, name);
                    }
                    if (entry == null || entry.Status != PhoneEntryStatus.Enabled) continue;
                    _repo.SetEntryStatus(c, tx, entry.Id, PhoneEntryStatus.Disabled, 1);
                    disabled++;
                }
                return new EmployeeSyncResultDto
                {
                    TotalEmployees = onDuty.Count,
                    Synced = synced,
                    Updated = updated,
                    Disabled = disabled,
                    Skipped = skipped
                };
            });

        private class EmpRow
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Phone { get; set; }
        }

        /// <summary>号码校验（BR-TEL-04）：11 位手机 / 座机（可含 0xx-0xxx 区号）/ 3-5 位短号（95598/10086 类）；紧急号码豁免。</summary>
        private static void ValidatePhone(string phone, bool isEmergency)
        {
            string normalized = NormalizePhone(phone);
            if (normalized.Length == 0 || !normalized.All(char.IsDigit))
                throw ApiException.ValidationFailed("号码格式不正确（BR-TEL-04）");
            if (isEmergency || IsEmergencyNumber(normalized)) return; // 紧急短号（119/110/120）豁免
            if (MobileRegex.IsMatch(normalized) || LandlineRegex.IsMatch(normalized) || ShortNumberRegex.IsMatch(normalized)) return;
            throw ApiException.ValidationFailed("号码格式不正确（BR-TEL-04：支持 11 位手机、座机（可含区号）、3-5 位短号）");
        }

        /// <summary>保护判定（BR-TEL-03）：EntryType=紧急 OR 号码（去分隔符）∈{119,120,110}。</summary>
        private static bool IsEmergencyEntry(PhoneEntryDto entry)
        {
            return entry != null && (entry.EntryType == PhoneEntryType.Emergency || IsEmergencyNumber(NormalizePhone(entry.Phone)));
        }

        private static bool IsEmergencyNumber(string normalizedPhone)
        {
            return EmergencyNumbers.Contains(normalizedPhone);
        }

        /// <summary>号码归一化：去连字符与空格（"0571-8802 1234" 与 "057188021234" 视为同号）。</summary>
        private static string NormalizePhone(string phone)
        {
            return (phone ?? string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).Trim();
        }

        private static string ResolveNamePinyin(string requestPinyin, string name)
        {
            return !string.IsNullOrWhiteSpace(requestPinyin) ? requestPinyin.Trim().ToLowerInvariant() : BuildNamePinyin(name);
        }

        private static string ResolveNameInitials(string requestInitials, string name, string pinyin)
        {
            return !string.IsNullOrWhiteSpace(requestInitials) ? requestInitials.Trim().ToLowerInvariant() : BuildNameInitialsFromName(name, pinyin);
        }

        /// <summary>全拼兜底：无拼音库，仅保留 ASCII 字母（小写），汉字等其它字符跳过，保证列非 NULL。</summary>
        private static string BuildNamePinyin(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var sb = new StringBuilder(name.Length);
            foreach (char ch in name.Trim())
            {
                if (ch >= 'A' && ch <= 'Z') sb.Append((char)(ch + ('a' - 'A')));
                else if (ch >= 'a' && ch <= 'z') sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>首字母兜底：取名称中每段 ASCII 连续字母的首字母；无产出时回退全拼。</summary>
        private static string BuildNameInitialsFromName(string name, string pinyin)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                var sb = new StringBuilder();
                bool lastWasLetter = false;
                foreach (char ch in name.Trim())
                {
                    bool isLetter = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');
                    if (isLetter && !lastWasLetter) sb.Append(ch >= 'A' && ch <= 'Z' ? (char)(ch + ('a' - 'A')) : ch);
                    lastWasLetter = isLetter;
                }
                if (sb.Length > 0) return sb.ToString();
            }
            return pinyin ?? string.Empty;
        }

        /// <summary>类型名 → PhoneEntryType 枚举（自定义类型归为普通）。</summary>
        private static PhoneEntryType MapTypeName(string name)
        {
            if (name == "紧急") return PhoneEntryType.Emergency;
            if (name == "员工通讯录") return PhoneEntryType.Employee;
            return PhoneEntryType.Normal;
        }

        /// <summary>按枚举查找默认类型 id（供紧急条目编辑时回填 type_id）。</summary>
        private static int? ResolveTypeIdByEnum(IDbConnection c, PhoneEntryType enumType)
        {
            var types = c.Query<PhoneTypeDto>("SELECT id, name, sort FROM t_phone_type WHERE COALESCE(status,0)=0 AND del_flag=0").ToList();
            string target = enumType == PhoneEntryType.Emergency ? "紧急" : (enumType == PhoneEntryType.Employee ? "员工通讯录" : "普通");
            var t = types.FirstOrDefault(x => x.Name == target);
            return t?.Id;
        }

        private TResult WithConnection<TResult>(Func<IDbConnection, TResult> action)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return action(connection);
            }
        }

        private TResult WithTransaction<TResult>(Func<IDbConnection, IDbTransaction, TResult> action)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                var result = action(connection, transaction);
                transaction.Commit();
                return result;
            }
        }
    }
}
