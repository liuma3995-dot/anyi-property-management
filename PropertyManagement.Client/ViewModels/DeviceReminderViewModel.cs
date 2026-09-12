using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Equipment;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Org;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>设备提醒行（PG-EQP-04；R6 起含已处理记录，支持批量删除勾选）。</summary>
    public class ReminderRow : ObservableObject
    {
        public EquipmentReminderDto Dto { get; set; }
        private bool _isChecked;
        /// <summary>批量删除勾选（R6；仅多选模式可见）。</summary>
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }
        public string DeviceText
        {
            get
            {
                string no = string.IsNullOrEmpty(Dto.DeviceNo) ? "EQP-" + Dto.DeviceId.ToString("0000") : Dto.DeviceNo;
                return no + " " + (Dto.DeviceName ?? string.Empty);
            }
        }
        public string ItemText
        {
            get
            {
                string text = !string.IsNullOrEmpty(Dto.ItemText) ? Dto.ItemText : DefaultItemText();
                // 原型 PG-EQP-04 行-5「年检（逾期）」：逾期项在事项上标注
                return IsOverdue && text.EndsWith("到期") ? text.Substring(0, text.Length - 2) + "（逾期）" : text;
            }
        }
        private string DefaultItemText()
        {
            switch (Dto.Type)
            {
                case "maint_due": case "maintenance": case "maint": return "保养到期";
                case "inspect_due": case "inspection": case "inspect": return "年检到期";
                case "warranty_due": case "warranty": return "质保到期";
                case "contract_due": case "contract": return "合同到期";
                default: return string.IsNullOrEmpty(Dto.Kind) ? "到期" : Dto.Kind;
            }
        }
        public string DueText { get { return Dto.DueAt.ToString("yyyy-MM-dd"); } }
        /// <summary>剩余天数（客户端按 DueAt 计算，Mock/真实后端一致；负=逾期）。</summary>
        public int RemainingDays { get { return Dto.DueAt.Date.Subtract(DateTime.Today).Days; } }
        /// <summary>已处理记录不再展示剩余天数（R6）。</summary>
        public string RemainingText { get { return IsHandled ? "—" : (RemainingDays < 0 ? "逾期 " + (-RemainingDays) + " 天" : RemainingDays + " 天"); } }
        public bool IsOverdue { get { return RemainingDays < 0; } }
        /// <summary>是否已处理（R6：处置后记录保留在列表中）。</summary>
        public bool IsHandled { get { return Dto.Status == ReminderStatus.Processed; } }
        /// <summary>处置时间（已处理记录；未处理显示「—」）。</summary>
        public string HandledText { get { return string.IsNullOrEmpty(Dto.HandledAt) ? "—" : Dto.HandledAt; } }
        /// <summary>责任班组（后端口径：逾期升级物业办，其余工程部，T6-5-7）。</summary>
        public string TeamText { get { return string.IsNullOrEmpty(Dto.ResponsibleTeam) ? "—" : Dto.ResponsibleTeam; } }
        private bool _isTeamEditing;
        private string _teamDraft = string.Empty;
        /// <summary>R7：责任班组行内编辑态（单元格自定义选择）。</summary>
        public bool IsTeamEditing
        {
            get { return _isTeamEditing; }
            set { SetProperty(ref _isTeamEditing, value); OnPropertyChanged(nameof(ShowTeamText)); }
        }
        public bool ShowTeamText { get { return !_isTeamEditing; } }
        /// <summary>责任班组编辑草稿（下拉可选/可直接输入）。</summary>
        public string TeamDraft { get { return _teamDraft; } set { SetProperty(ref _teamDraft, value); } }
        /// <summary>是否已被人工指派（R7；未指派=按默认规则）。</summary>
        public bool HasCustomTeam { get { return !string.IsNullOrWhiteSpace(Dto.ResponsibleTeamOverride); } }
        public string TeamTip
        {
            get
            {
                return HasCustomTeam
                    ? "责任班组：人工指派（可点【改】调整或【默认】恢复规则）"
                    : "责任班组：默认规则（逾期→物业办，其余→工程部；点【改】可自定义调配）";
            }
        }
        public string StatusText
        {
            get
            {
                if (!string.IsNullOrEmpty(Dto.StatusText)) return Dto.StatusText;
                if (Dto.Status == ReminderStatus.Processed) return "已处理";
                return RemainingDays < 0 ? "逾期" : (RemainingDays <= 30 ? "待处理" : "正常");
            }
        }
        /// <summary>未处理行（待处理/逾期/正常）：操作列「标记已处理」（R6：一键催办下线，处置记录保留在列表）。</summary>
        public bool ShowMarkHandled { get { return !IsHandled; } }
        /// <summary>已处理行：操作列「—」。</summary>
        public bool ShowNoAction { get { return IsHandled; } }
        /// <summary>已处理行状态标签用「done」色（灰）；未处理沿用 逾期/待处理/正常。</summary>
        public string StatusKey
        {
            get
            {
                if (IsHandled) return "done";
                string t = StatusText;
                if (t == "逾期") return "overdue";
                if (t == "待处理") return "pending";
                return "normal";
            }
        }
        public Brush StatusBg
        {
            get
            {
                switch (StatusKey)
                {
                    case "overdue": return Solid("#FDECEC");
                    case "pending": return Solid("#FFF5DC");
                    case "normal": return Solid("#E8F7F1");
                    default: return Solid("#F2F4F8");
                }
            }
        }
        public Brush StatusFg
        {
            get
            {
                switch (StatusKey)
                {
                    case "overdue": return Solid("#D64545");
                    case "pending": return Solid("#B76E00");
                    case "normal": return Solid("#12805C");
                    default: return Solid("#667085");
                }
            }
        }
        private static Brush Solid(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>到期提醒（PG-EQP-04，UC-EQP-006，BR-EQP-02，T6-5-7）。</summary>
    public class DeviceReminderViewModel : BaseInfoPageViewModel
    {
        /// <summary>范围页签 → t_reminder.type（仓储 QueryReminders 过滤值，EquipmentService.NormalizeReminderType 直通）。</summary>
        private static readonly string[] ScopeTypes = { null, "maint_due", "inspect_due", "warranty_due", "contract_due" };

        private int _scopeIndex;
        private int _statusIndex; // R6：0 全部 / 1 待处理 / 2 已处理
        private int _within30;
        private int _within7;
        private int _monthHandled;
        private int _overdueUnhandled;
        private string _within30Sub = "—";
        private bool _isBatchMode;
        private bool _isAllChecked;
        private bool _isBatchConfirmVisible;
        private bool _isBlockedVisible;
        private string _batchConfirmText = string.Empty;
        private string _blockedText = string.Empty;

        public DeviceReminderViewModel(IApiClient api) : base(api)
        {
            RefreshCommand = new AsyncRelayCommand(LoadAsync);
            SetScopeCommand = new RelayCommand<string>(s =>
            {
                int i;
                if (int.TryParse(s, out i) && i >= 0 && i < ScopeTypes.Length && _scopeIndex != i)
                {
                    _scopeIndex = i;
                    OnPropertyChanged(nameof(ScopeIndex));
                    OnPropertyChanged(nameof(IsScopeAll));
                    OnPropertyChanged(nameof(IsScopeMaintenance));
                    OnPropertyChanged(nameof(IsScopeInspection));
                    OnPropertyChanged(nameof(IsScopeWarranty));
                    OnPropertyChanged(nameof(IsScopeContract));
                    _ = LoadAsync();
                }
            });
            // R6 状态筛选项签：全部（含已处理）/ 待处理 / 已处理
            SetStatusCommand = new RelayCommand<string>(s =>
            {
                int i;
                if (int.TryParse(s, out i) && i >= 0 && i <= 2 && _statusIndex != i)
                {
                    _statusIndex = i;
                    OnPropertyChanged(nameof(StatusIndex));
                    OnPropertyChanged(nameof(IsStatusAll));
                    OnPropertyChanged(nameof(IsStatusPending));
                    OnPropertyChanged(nameof(IsStatusHandled));
                    _ = LoadAsync();
                }
            });
            MarkHandledCommand = new AsyncRelayCommand<ReminderRow>(HandleAsync);
            // R7：责任班组行内自定义（指派/恢复默认）
            EditTeamCommand = new RelayCommand<ReminderRow>(StartEditTeam);
            SaveTeamCommand = new AsyncRelayCommand<ReminderRow>(SaveTeamAsync);
            ResetTeamCommand = new AsyncRelayCommand<ReminderRow>(ResetTeamAsync);
            CancelTeamCommand = new RelayCommand<ReminderRow>(row => { if (row != null) row.IsTeamEditing = false; });
            StartBatchDeleteCommand = new RelayCommand(StartBatchDelete);
            RequestBatchConfirmCommand = new RelayCommand(RequestBatchConfirm);
            CancelBatchDeleteCommand = new RelayCommand(CancelBatchDelete);
            ConfirmBatchDeleteCommand = new AsyncRelayCommand(ConfirmBatchDeleteAsync);
            CloseBlockedCommand = new RelayCommand(() => IsBlockedVisible = false);
            _ = LoadAsync();
        }

        public ObservableCollection<ReminderRow> Items { get; } = new ObservableCollection<ReminderRow>();
        /// <summary>责任班组候选（R7）：部门字典 + 默认班组（物业办/工程部），去重排序。</summary>
        public ObservableCollection<string> TeamOptions { get; } = new ObservableCollection<string>();

        /// <summary>范围页签索引：0 全部 1 保养到期 2 年检到期 3 质保到期 4 合同到期。</summary>
        public int ScopeIndex { get { return _scopeIndex; } private set { SetProperty(ref _scopeIndex, value); } }
        public int Within30 { get { return _within30; } private set { SetProperty(ref _within30, value); } }
        public int Within7 { get { return _within7; } private set { SetProperty(ref _within7, value); } }
        public int MonthHandled { get { return _monthHandled; } private set { SetProperty(ref _monthHandled, value); } }
        public int OverdueUnhandled { get { return _overdueUnhandled; } private set { SetProperty(ref _overdueUnhandled, value); } }
        public string Within30Sub { get { return _within30Sub; } private set { SetProperty(ref _within30Sub, value); } }
        public string Within7Sub { get { return "需立即处理"; } }
        public string MonthHandledSub { get { return "按期完成"; } }
        public string OverdueSub { get { return "年检超期"; } }
        /// <summary>范围页签选中态（原型胶囊按钮：选中蓝底白字）。</summary>
        public bool IsScopeAll { get { return _scopeIndex == 0; } }
        public bool IsScopeMaintenance { get { return _scopeIndex == 1; } }
        public bool IsScopeInspection { get { return _scopeIndex == 2; } }
        public bool IsScopeWarranty { get { return _scopeIndex == 3; } }
        public bool IsScopeContract { get { return _scopeIndex == 4; } }

        /// <summary>状态页签索引（R6）：0 全部（含已处理）1 待处理 2 已处理。</summary>
        public int StatusIndex { get { return _statusIndex; } }
        public bool IsStatusAll { get { return _statusIndex == 0; } }
        public bool IsStatusPending { get { return _statusIndex == 1; } }
        public bool IsStatusHandled { get { return _statusIndex == 2; } }

        // ---------- 批量删除（R6：仅已处理可删；命中待处理整批拒绝） ----------
        public bool IsBatchMode
        {
            get { return _isBatchMode; }
            set { if (SetProperty(ref _isBatchMode, value)) OnPropertyChanged(nameof(IsNotBatchMode)); }
        }
        public bool IsNotBatchMode { get { return !_isBatchMode; } }
        public bool IsAllChecked
        {
            get { return _isAllChecked; }
            set
            {
                if (!SetProperty(ref _isAllChecked, value)) return;
                foreach (var row in Items) row.IsChecked = value;
                OnPropertyChanged(nameof(CheckedCountText));
            }
        }
        public string CheckedCountText { get { return "已选 " + Items.Count(x => x.IsChecked) + " / " + Items.Count + " 条"; } }
        public bool IsBatchConfirmVisible { get { return _isBatchConfirmVisible; } set { SetProperty(ref _isBatchConfirmVisible, value); } }
        public string BatchConfirmText { get { return _batchConfirmText; } set { SetProperty(ref _batchConfirmText, value); } }
        /// <summary>禁止删除提示浮层（待处理/逾期记录命中时展示，含逐条明细）。</summary>
        public bool IsBlockedVisible { get { return _isBlockedVisible; } set { SetProperty(ref _isBlockedVisible, value); } }
        public string BlockedText { get { return _blockedText; } set { SetProperty(ref _blockedText, value); } }

        public IAsyncRelayCommand RefreshCommand { get; }
        public IRelayCommand<string> SetScopeCommand { get; }
        public IRelayCommand<string> SetStatusCommand { get; }
        /// <summary>标记已处理（R6 唯一操作）：本期闭环，记录保留在列表中并可通过批量删除清理。</summary>
        public IAsyncRelayCommand<ReminderRow> MarkHandledCommand { get; }
        /// <summary>责任班组：进入行内编辑 / 保存指派 / 恢复默认规则 / 取消。</summary>
        public IRelayCommand<ReminderRow> EditTeamCommand { get; }
        public IAsyncRelayCommand<ReminderRow> SaveTeamCommand { get; }
        public IAsyncRelayCommand<ReminderRow> ResetTeamCommand { get; }
        public IRelayCommand<ReminderRow> CancelTeamCommand { get; }
        public IRelayCommand StartBatchDeleteCommand { get; }
        /// <summary>工具栏「确认删除」→ 打开二次确认浮层（参照设备列表批量删除）。</summary>
        public IRelayCommand RequestBatchConfirmCommand { get; }
        public IRelayCommand CancelBatchDeleteCommand { get; }
        public IAsyncRelayCommand ConfirmBatchDeleteCommand { get; }
        public IRelayCommand CloseBlockedCommand { get; }

        public async Task LoadAsync()
        {
            await RunAsync(LoadCoreAsync, "提醒已加载");
        }

        private async Task LoadCoreAsync()
        {
            // 统计卡走后端 summary 分桶（修复"本月已处理"硬编码 0 与 30 天/逾期重复计数）
            var summary = await Api.GetEquipmentReminderSummaryAsync();
            Within30 = summary.Within30;
            Within7 = summary.Within7;
            MonthHandled = summary.MonthHandled;
            OverdueUnhandled = summary.OverdueUnhandled;
            string type = _scopeIndex >= 1 && _scopeIndex < ScopeTypes.Length ? ScopeTypes[_scopeIndex] : null;
            // R6：列表展示全部计划项（含已处理记录，原型含"年检 2027-05-17 剩余 262 天"）；统计卡仍按后端 30 天分桶
            int status = _statusIndex == 1 ? 0 : (_statusIndex == 2 ? 1 : -1);
            var list = await Api.GetEquipmentRemindersAsync(3650, type, status);
            Items.Clear();
            foreach (var r in list)
            {
                var row = new ReminderRow { Dto = r };
                // 勾选状态变化 → 同步"已选 N / 全选"（参照设备列表批量删除）
                row.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ReminderRow.IsChecked))
                    {
                        OnPropertyChanged(nameof(CheckedCountText));
                        OnPropertyChanged(nameof(IsAllChecked));
                    }
                };
                Items.Add(row);
            }
            _isAllChecked = false;
            OnPropertyChanged(nameof(IsAllChecked));
            OnPropertyChanged(nameof(CheckedCountText));
            int typeCount = list.Where(x => x.RemainingDays >= 0 && x.RemainingDays <= 30)
                .Select(x => x.TypeName).Where(n => !string.IsNullOrEmpty(n)).Distinct().Count();
            Within30Sub = typeCount > 0 ? typeCount + " 类设备" : "—";
            await LoadTeamOptionsAsync();
        }

        /// <summary>责任班组候选：部门字典（人员组织）+ 默认班组（物业办/工程部），首次加载一次。</summary>
        private async Task LoadTeamOptionsAsync()
        {
            if (TeamOptions.Count > 0) return;
            var names = new List<string> { "物业办", "工程部" };
            try
            {
                var depts = await Api.GetDepartmentsAsync();
                foreach (var d in depts ?? new List<DepartmentDto>())
                    if (!string.IsNullOrWhiteSpace(d.Name) && !names.Contains(d.Name)) names.Add(d.Name);
            }
            catch (Exception) { /* 部门字典不可用时退回默认班组候选 */ }
            foreach (var n in names) TeamOptions.Add(n);
        }

        /// <summary>进入责任班组编辑态（草稿=当前值）。</summary>
        private void StartEditTeam(ReminderRow row)
        {
            if (row == null) return;
            row.TeamDraft = row.HasCustomTeam ? row.Dto.ResponsibleTeamOverride : (row.Dto.ResponsibleTeam ?? string.Empty);
            row.IsTeamEditing = true;
            ErrorText = string.Empty;
        }

        /// <summary>保存责任班组指派（R7）：写入 t_reminder.responsible_team，逾期/默认规则随之被人工值覆盖。</summary>
        private async Task SaveTeamAsync(ReminderRow row)
        {
            if (row == null) return;
            string team = row.TeamDraft == null ? string.Empty : row.TeamDraft.Trim();
            if (team.Length == 0) { ErrorText = "请选择或输入责任班组（如需恢复默认请点【默认】）"; return; }
            await RunAsync(async () =>
            {
                await Api.SaveEquipmentReminderTeamAsync(row.Dto.ReminderId, new ReminderTeamRequest { Team = team });
                row.IsTeamEditing = false;
                await LoadCoreAsync();
            }, "责任班组已指派为：" + team);
        }

        /// <summary>恢复默认规则（清空人工指派）。</summary>
        private async Task ResetTeamAsync(ReminderRow row)
        {
            if (row == null) return;
            await RunAsync(async () =>
            {
                await Api.SaveEquipmentReminderTeamAsync(row.Dto.ReminderId, new ReminderTeamRequest { Team = string.Empty });
                row.IsTeamEditing = false;
                await LoadCoreAsync();
            }, "责任班组已恢复默认规则");
        }

        /// <summary>标记已处理（R6）：status=1 + 处置时间，记录保留在列表中；本期不再重复提醒。</summary>
        private async Task HandleAsync(ReminderRow row)
        {
            if (row == null) return;
            await RunAsync(async () =>
            {
                await Api.HandleEquipmentReminderAsync(row.Dto.ReminderId);
                await LoadCoreAsync();
            }, "已标记处理：" + row.DeviceText + "（" + row.ItemText + "，记录保留在列表中）");
        }

        private void StartBatchDelete()
        {
            foreach (var row in Items) row.IsChecked = false;
            _isAllChecked = false;
            OnPropertyChanged(nameof(IsAllChecked));
            OnPropertyChanged(nameof(CheckedCountText));
            ErrorText = string.Empty;
            IsBatchMode = true;
        }

        /// <summary>工具栏「确认删除」：先做一次勾选校验并打开二次确认浮层。</summary>
        private void RequestBatchConfirm()
        {
            int count = Items.Count(x => x.IsChecked);
            if (count == 0) { ErrorText = "请先勾选要删除的提醒记录"; return; }
            BatchConfirmText = "确认删除已勾选的 " + count + " 条提醒记录？\n"
                + "· 仅「已处理」记录可删除（待处理/逾期需先标记已处理）\n"
                + "· 删除为软删，保留审计追溯，不影响设备台账与统计口径";
            IsBatchConfirmVisible = true;
        }

        private void CancelBatchDelete()
        {
            IsBatchMode = false;
            IsBatchConfirmVisible = false;
            foreach (var row in Items) row.IsChecked = false;
            _isAllChecked = false;
            OnPropertyChanged(nameof(IsAllChecked));
            OnPropertyChanged(nameof(CheckedCountText));
        }

        /// <summary>批量删除（R6/D2/D2b）：仅已处理可删；命中待处理/逾期时整批拒绝并弹窗列出明细。</summary>
        private async Task ConfirmBatchDeleteAsync()
        {
            var checkedRows = Items.Where(x => x.IsChecked).ToList();
            if (checkedRows.Count == 0) { ErrorText = "请先勾选要删除的提醒记录"; IsBatchConfirmVisible = false; return; }
            var pending = checkedRows.Where(x => !x.IsHandled).ToList();
            if (pending.Count > 0)
            {
                IsBatchConfirmVisible = false;
                BlockedText = "以下提醒仍为待处理/逾期，禁止删除（请先标记已处理）：\n"
                    + string.Join("\n", pending.Select(x => "· " + x.DeviceText + "（" + x.ItemText + "·" + x.StatusText + "）"));
                IsBlockedVisible = true;
                return;
            }
            string msg = null;
            await RunAsync(async () =>
            {
                var result = await Api.BatchDeleteEquipmentRemindersAsync(new ReminderBatchDeleteRequest
                {
                    Ids = checkedRows.Select(x => x.Dto.ReminderId).ToList()
                });
                if (result != null && result.Blocked != null && result.Blocked.Count > 0)
                {
                    IsBatchConfirmVisible = false;
                    BlockedText = "以下提醒仍为待处理/逾期，禁止删除（请先标记已处理）：\n"
                        + string.Join("\n", result.Blocked.Select(b => "· " + b.Device + "：" + b.Reason));
                    IsBlockedVisible = true;
                    return;
                }
                int deleted = result == null ? 0 : result.Deleted;
                IsBatchConfirmVisible = false;
                IsBatchMode = false;
                msg = "已删除 " + deleted + " 条已处理提醒记录（软删，可审计追溯）";
                await LoadCoreAsync();
            }, null);
            if (!string.IsNullOrEmpty(msg)) StatusText = DateTime.Now.ToString("HH:mm:ss ") + msg;
        }
    }
}
