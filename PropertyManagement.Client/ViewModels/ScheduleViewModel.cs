using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Org;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>排班网格单元格（PG-ORG-02）：早/中/晚/休 彩色标签、冲突 ⚠、缺员红标、点击改班。</summary>
    public class ScheduleCellVm : ObservableObject
    {
        /// <summary>服务端该员工该日的排班（草稿+已发布）。</summary>
        internal List<ScheduleDto> Dtos = new List<ScheduleDto>();

        /// <summary>未保存的本地修改：null=无；-1=改休；&gt;0=改为该班次。</summary>
        internal int? PendingShiftId;

        public int EmployeeId { get; set; }
        public DateTime WorkDate { get; set; }
        public string DateText { get { return WorkDate.ToString("MM-dd"); } }

        private string _cellText = "休";
        private Brush _cellBackground;
        private Brush _cellForeground;
        private Brush _cellBorder = Brushes.Transparent;
        private string _tooltipText = "休";

        public string CellText { get { return _cellText; } private set { SetProperty(ref _cellText, value); } }
        public Brush CellBackground { get { return _cellBackground; } private set { SetProperty(ref _cellBackground, value); } }
        public Brush CellForeground { get { return _cellForeground; } private set { SetProperty(ref _cellForeground, value); } }
        public Brush CellBorder { get { return _cellBorder; } private set { SetProperty(ref _cellBorder, value); } }
        public string TooltipText { get { return _tooltipText; } private set { SetProperty(ref _tooltipText, value); } }

        internal void Refresh(List<ShiftDto> shifts)
        {
            var shorts = new List<string>();
            bool conflict = false;
            bool published = false;
            if (PendingShiftId == -1)
            {
                // 本地改为休：按无排班展示
            }
            else if (PendingShiftId.HasValue && PendingShiftId.Value > 0)
            {
                shorts.Add(ShortShiftName(shifts, PendingShiftId.Value));
            }
            else
            {
                foreach (ScheduleDto d in Dtos)
                {
                    shorts.Add(ShortName(d.ShiftName));
                    if (d.Status == ScheduleStatus.Published) { published = true; }
                    if (d.HasConflict) { conflict = true; }
                }
                if (Dtos.Select(d => d.ShiftId).Distinct().Count() > 1) { conflict = true; }
            }

            if (shorts.Count == 0)
            {
                CellText = "休";
                CellBackground = Brush("#EEF2F7");
                CellForeground = Brush("#667085");
                CellBorder = Brushes.Transparent;
                TooltipText = WorkDate.ToString("yyyy-MM-dd") + " 休";
            }
            else if (conflict)
            {
                CellText = string.Join("+", shorts.Distinct()) + " ⚠";
                CellBackground = Brush("#FDECEC");
                CellForeground = Brush("#D64545");
                CellBorder = Brush("#D64545");
                TooltipText = WorkDate.ToString("yyyy-MM-dd") + " 同日多班冲突：先删除多余班次";
            }
            else
            {
                string name = shorts[0];
                // 原型 PG-ORG-02 配色：早=主色浅蓝、中=紫、晚=警示黄、休=浅灰
                if (name.Contains("早")) { CellBackground = Brush("#EAF3FF"); CellForeground = Brush("#2B7DE9"); }
                else if (name.Contains("晚") || name.Contains("夜")) { CellBackground = Brush("#FFF5DC"); CellForeground = Brush("#B76E00"); }
                else if (name.Contains("休")) { CellBackground = Brush("#EEF2F7"); CellForeground = Brush("#667085"); }
                else { CellBackground = Brush("#F1EDFD"); CellForeground = Brush("#7A5AF8"); }
                CellText = name;
                CellBorder = Brushes.Transparent;
                TooltipText = WorkDate.ToString("yyyy-MM-dd") + " " + FullShiftName(shifts, Dtos, name) + (published ? "（已发布）" : string.Empty);
            }
        }

        private static string ShortShiftName(List<ShiftDto> shifts, int shiftId)
        {
            ShiftDto s = shifts.FirstOrDefault(x => x.Id == shiftId);
            return s == null ? "班" : ShortName(s.Name);
        }

        private static string FullShiftName(List<ShiftDto> shifts, List<ScheduleDto> dtos, string shortName)
        {
            ScheduleDto d = dtos.FirstOrDefault(x => ShortName(x.ShiftName) == shortName);
            if (d != null && !string.IsNullOrEmpty(d.ShiftName)) { return d.ShiftName; }
            return shortName;
        }

        internal static string ShortName(string shiftName)
        {
            if (string.IsNullOrWhiteSpace(shiftName)) { return "班"; }
            return shiftName.EndsWith("班") && shiftName.Length > 1 ? shiftName.Substring(0, shiftName.Length - 1) : shiftName;
        }

        private static Brush Brush(string hex)
        {
            SolidColorBrush b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }

    /// <summary>排班网格行：首列「员工（姓名 · 岗位）」+ 7 天单元格。</summary>
    public class ScheduleGridRowVm
    {
        public int EmployeeId { get; set; }
        public string EmpName { get; set; }
        public string PositionName { get; set; }
        public string EmployeeText { get { return EmpName + " · " + (string.IsNullOrEmpty(PositionName) ? "员工" : PositionName); } }
        public List<ScheduleCellVm> Cells { get; set; }

        /// <summary>该员工在周期内是否已排班（含早/中/晚等工作班次）。</summary>
        public bool IsScheduled { get; set; }
    }

    /// <summary>班次选择弹窗选项（早/中/晚/休）。</summary>
    public class ShiftOptionVm
    {
        /// <summary>null=休。</summary>
        public int? ShiftId { get; set; }
        public string Label { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>班次时间设置行：可编辑开始/结束时间（早/中/晚班；休不设时间窗）。</summary>
    public class EditableShiftVm : ObservableObject
    {
        public int Id { get; set; }
        public string Name { get; set; }
        private string _startTime = string.Empty;
        private string _endTime = string.Empty;
        public string StartTime { get { return _startTime; } set { SetProperty(ref _startTime, value); } }
        public string EndTime { get { return _endTime; } set { SetProperty(ref _endTime, value); } }
    }

    /// <summary>批量删除排班：班次筛选行（勾选要删除的班次）。</summary>
    public class ShiftSelectItemVm : ObservableObject
    {
        private bool _isSelected = true;
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsSelected { get { return _isSelected; } set { SetProperty(ref _isSelected, value); } }
        public string Display { get { return Name; } }
    }

    /// <summary>自动排班复选框行：可选员工（自定义选择员工）。</summary>
    public class EmployeeSelectItemVm : ObservableObject
    {
        private bool _isSelected;
        public int Id { get; set; }
        public string Name { get; set; }
        public string PositionName { get; set; }
        public bool IsSelected { get { return _isSelected; } set { SetProperty(ref _isSelected, value); } }
        public string Display { get { return string.IsNullOrEmpty(PositionName) ? Name : Name + " · " + PositionName; } }
    }

    /// <summary>排班模板下拉项（自动排班表单选择/删除）。</summary>
    public class ScheduleTemplateVm : ObservableObject
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int ItemCount { get; set; }
        public List<ScheduleTemplateItemDto> Items { get; set; }
        public string PeriodText { get { return FromDate.ToString("MM-dd") + "~" + ToDate.ToString("MM-dd") + " · " + ItemCount + " 条"; } }
        public string Display { get { return Name + "（" + PeriodText + "）"; } }
    }

    /// <summary>排班表页（PG-ORG-02，UC-ORG-003，BR-ORG-03/04）：
    /// 员工×周矩阵网格、周一锚定周导航（ISO 周数）+ 自定义日期跳转、
    /// 自动排班（自定义员工/日期/班次表单，空选回退服务端轮转）、保存排班（自定义保存周期并同步日历）、
    /// 点击单元格改班（草稿替换=删除旧草稿+新增，随保存批量提交）。
    /// 已下线：一键补班、新增排班、发布排班/缺员强制发布。</summary>
    public class ScheduleViewModel : BaseInfoPageViewModel
    {
        /// <summary>跨页面/跨实例保留的最近排班周期（导航切页会重建 VM，借此避免周期被重置为默认周）。</summary>
        private static DateTime _persistedPeriodStart;
        private static DateTime _persistedPeriodEnd;

        private DateTime _periodStart;
        private DateTime _periodEnd;
        private bool _isPickerVisible;
        private ScheduleCellVm _pickerCell;
        private string _pickerTitle = string.Empty;

        // 自动排班表单
        private bool _isAutoPlanVisible;
        private DateTime? _autoPlanFromDate;
        private DateTime? _autoPlanToDate;

        // 保存周期
        private bool _isSaveVisible;
        private DateTime? _saveFromDate;
        private DateTime? _saveToDate;

        // 班次时间设置
        private bool _isShiftSettingVisible;

        // 批量删除排班
        private bool _isBatchDeleteVisible;
        private DateTime? _batchDeleteFromDate;
        private DateTime? _batchDeleteToDate;

        // 排班图过滤（0=全部，1=已排班，2=未排班）
        private int _gridFilter;
        private List<ScheduleGridRowVm> _allGridRows = new List<ScheduleGridRowVm>();

        // 保存后生成模板确认
        private bool _isSaveTemplateVisible;
        private DateTime _lastSaveFrom;
        private DateTime _lastSaveTo;

        // 自动排班模板
        private int _selectedTemplateId;

        private readonly List<int> _pendingDeletes = new List<int>();
        private readonly List<ScheduleRequest> _pendingAdds = new List<ScheduleRequest>();

        public ScheduleViewModel(IApiClient api) : base(api)
        {
            if (_persistedPeriodStart != default && _persistedPeriodEnd >= _persistedPeriodStart)
            {
                _periodStart = _persistedPeriodStart.Date;
                _periodEnd = _persistedPeriodEnd.Date;
            }
            else
            {
                _periodStart = StartOfWeek(DateTime.Today);
                _periodEnd = _periodStart.AddDays(6);
            }
            GenerateCommand = new RelayCommand(OpenAutoPlan);
            AutoPlanConfirmCommand = new AsyncRelayCommand(GenerateAsync);
            AutoPlanCancelCommand = new RelayCommand(() => IsAutoPlanVisible = false);
            SaveCommand = new RelayCommand(OpenSaveDialog);
            SaveConfirmCommand = new AsyncRelayCommand(SaveAsync);
            SaveCancelCommand = new RelayCommand(() => IsSaveVisible = false);
            ShiftSettingCommand = new RelayCommand(OpenShiftSetting);
            ShiftSettingSaveCommand = new AsyncRelayCommand(SaveShiftSettingAsync);
            ShiftSettingCancelCommand = new RelayCommand(() => IsShiftSettingVisible = false);
            BatchDeleteCommand = new RelayCommand(OpenBatchDelete);
            BatchDeleteConfirmCommand = new AsyncRelayCommand(DeleteBatchAsync);
            BatchDeleteCancelCommand = new RelayCommand(() => IsBatchDeleteVisible = false);
            AutoPlanSelectAllCommand = new RelayCommand(ToggleAllEmployees);
            SaveTemplateConfirmCommand = new AsyncRelayCommand(SaveTemplateFromLastSaveAsync);
            SaveTemplateCancelCommand = new RelayCommand(() => IsSaveTemplateVisible = false);
            DeleteTemplateCommand = new AsyncRelayCommand(DeleteSelectedTemplateAsync);
            CellClickCommand = new RelayCommand<ScheduleCellVm>(OpenPicker);
            PickShiftCommand = new RelayCommand<ShiftOptionVm>(ApplyPick);
            ClosePickerCommand = new RelayCommand(() => IsPickerVisible = false);
            _ = LoadAsync();
        }

        public ObservableCollection<ScheduleGridRowVm> GridRows { get; } = new ObservableCollection<ScheduleGridRowVm>();
        /// <summary>全部班次（用于网格班次名解析）。</summary>
        public ObservableCollection<ShiftDto> Shifts { get; } = new ObservableCollection<ShiftDto>();
        /// <summary>图例班次：早/中/晚/休（去重并按原型顺序，避免脏数据多显示）。</summary>
        public ObservableCollection<ShiftDto> LegendShifts { get; } = new ObservableCollection<ShiftDto>();
        /// <summary>可排班次（需求人数&gt;0，用于自动排班班次选择）。</summary>
        public ObservableCollection<ShiftDto> AssignShifts { get; } = new ObservableCollection<ShiftDto>();
        public ObservableCollection<EmployeeDto> Employees { get; } = new ObservableCollection<EmployeeDto>();
        public ObservableCollection<EmployeeSelectItemVm> AutoPlanEmployees { get; } = new ObservableCollection<EmployeeSelectItemVm>();
        public ObservableCollection<ShiftOptionVm> ShiftOptions { get; } = new ObservableCollection<ShiftOptionVm>();
        /// <summary>班次时间设置行（早/中/晚班）。</summary>
        public ObservableCollection<EditableShiftVm> EditableShifts { get; } = new ObservableCollection<EditableShiftVm>();
        /// <summary>批量删除排班：可勾选班次。</summary>
        public ObservableCollection<ShiftSelectItemVm> BatchDeleteShifts { get; } = new ObservableCollection<ShiftSelectItemVm>();
        /// <summary>自动排班：可勾选班次（多选周期内所有班次）。</summary>
        public ObservableCollection<ShiftSelectItemVm> AutoPlanShiftItems { get; } = new ObservableCollection<ShiftSelectItemVm>();
        /// <summary>自动排班：排班模板列表（保存排班后生成）。</summary>
        public ObservableCollection<ScheduleTemplateVm> AutoPlanTemplates { get; } = new ObservableCollection<ScheduleTemplateVm>();

        /// <summary>排班周期开始（自定义，作为排班表页面周期）。</summary>
        public DateTime PeriodStart
        {
            get { return _periodStart; }
            set
            {
                DateTime v = value == default ? StartOfWeek(DateTime.Today) : value.Date;
                if (SetProperty(ref _periodStart, v))
                {
                    EnsurePeriodOrder();
                    PersistPeriod();
                    _ = LoadAsync();
                }
            }
        }

        /// <summary>排班周期结束。</summary>
        public DateTime PeriodEnd
        {
            get { return _periodEnd; }
            set
            {
                DateTime v = value == default ? PeriodStart.AddDays(6) : value.Date;
                if (SetProperty(ref _periodEnd, v))
                {
                    EnsurePeriodOrder();
                    PersistPeriod();
                    _ = LoadAsync();
                }
            }
        }

        /// <summary>周期天数（含端点）。</summary>
        public int PeriodDays { get { return (PeriodEnd.Date - PeriodStart.Date).Days + 1; } }

        /// <summary>周期文本「2026-09-07 ~ 09-13」。</summary>
        public string PeriodText
        {
            get { return PeriodStart.ToString("yyyy-MM-dd") + " ~ " + PeriodEnd.ToString("MM-dd"); }
        }

        /// <summary>周期是否有效（结束不早于开始）。</summary>
        public bool IsPeriodValid { get { return PeriodEnd.Date >= PeriodStart.Date; } }

        /// <summary>周期每日列标题（周一 09-07…）。</summary>
        public ObservableCollection<string> DayHeaders { get; } = new ObservableCollection<string>();

        private string DayHeader(int offset)
        {
            DateTime d = PeriodStart.AddDays(offset);
            string[] weeks = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
            return weeks[((int)d.DayOfWeek + 6) % 7] + " " + d.ToString("MM-dd");
        }

        private void EnsurePeriodOrder()
        {
            if (PeriodEnd.Date < PeriodStart.Date)
            {
                _periodEnd = _periodStart;
                OnPropertyChanged(nameof(PeriodEnd));
                OnPropertyChanged(nameof(PeriodDays));
                OnPropertyChanged(nameof(PeriodText));
                OnPropertyChanged(nameof(IsPeriodValid));
            }
        }

        private void PersistPeriod()
        {
            _persistedPeriodStart = _periodStart.Date;
            _persistedPeriodEnd = _periodEnd.Date;
        }

        public bool IsEmpty { get { return GridRows.Count == 0; } }

        public bool IsPickerVisible { get { return _isPickerVisible; } set { SetProperty(ref _isPickerVisible, value); } }
        public string PickerTitle { get { return _pickerTitle; } private set { SetProperty(ref _pickerTitle, value); } }

        public bool IsAutoPlanVisible { get { return _isAutoPlanVisible; } set { SetProperty(ref _isAutoPlanVisible, value); } }
        public DateTime? AutoPlanFromDate { get { return _autoPlanFromDate; } set { SetProperty(ref _autoPlanFromDate, value); } }
        public DateTime? AutoPlanToDate { get { return _autoPlanToDate; } set { SetProperty(ref _autoPlanToDate, value); } }

        public bool IsSaveVisible { get { return _isSaveVisible; } set { SetProperty(ref _isSaveVisible, value); } }
        public DateTime? SaveFromDate { get { return _saveFromDate; } set { SetProperty(ref _saveFromDate, value); } }
        public DateTime? SaveToDate { get { return _saveToDate; } set { SetProperty(ref _saveToDate, value); } }

        public bool IsShiftSettingVisible { get { return _isShiftSettingVisible; } set { SetProperty(ref _isShiftSettingVisible, value); } }

        public bool IsBatchDeleteVisible { get { return _isBatchDeleteVisible; } set { SetProperty(ref _isBatchDeleteVisible, value); } }
        public DateTime? BatchDeleteFromDate { get { return _batchDeleteFromDate; } set { SetProperty(ref _batchDeleteFromDate, value); } }
        public DateTime? BatchDeleteToDate { get { return _batchDeleteToDate; } set { SetProperty(ref _batchDeleteToDate, value); } }

        /// <summary>排班图过滤：0=全部，1=已排班，2=未排班。</summary>
        public int GridFilter
        {
            get { return _gridFilter; }
            set { if (SetProperty(ref _gridFilter, value)) { ApplyGridFilter(); } }
        }

        /// <summary>自动排班：是否全选员工。</summary>
        public bool IsAutoPlanAllEmployeesSelected
        {
            get { return AutoPlanEmployees.Count > 0 && AutoPlanEmployees.All(e => e.IsSelected); }
            set
            {
                foreach (EmployeeSelectItemVm e in AutoPlanEmployees) { e.IsSelected = value; }
                OnPropertyChanged(nameof(IsAutoPlanAllEmployeesSelected));
            }
        }

        public bool IsSaveTemplateVisible { get { return _isSaveTemplateVisible; } set { SetProperty(ref _isSaveTemplateVisible, value); } }

        public int SelectedTemplateId
        {
            get { return _selectedTemplateId; }
            set
            {
                if (SetProperty(ref _selectedTemplateId, value))
                {
                    // 选中模板后，把表单周期同步为模板自身的周期（如 09-01~09-30）
                    ScheduleTemplateVm t = AutoPlanTemplates.FirstOrDefault(x => x.Id == value);
                    if (t != null && t.FromDate != default && t.ToDate != default)
                    {
                        AutoPlanFromDate = t.FromDate.Date;
                        AutoPlanToDate = t.ToDate.Date;
                    }
                }
            }
        }

        public IRelayCommand GenerateCommand { get; }
        public IAsyncRelayCommand AutoPlanConfirmCommand { get; }
        public IRelayCommand AutoPlanCancelCommand { get; }
        public IRelayCommand SaveCommand { get; }
        public IAsyncRelayCommand SaveConfirmCommand { get; }
        public IRelayCommand SaveCancelCommand { get; }
        public IRelayCommand ShiftSettingCommand { get; }
        public IAsyncRelayCommand ShiftSettingSaveCommand { get; }
        public IRelayCommand ShiftSettingCancelCommand { get; }
        public IRelayCommand BatchDeleteCommand { get; }
        public IAsyncRelayCommand BatchDeleteConfirmCommand { get; }
        public IRelayCommand BatchDeleteCancelCommand { get; }
        public IRelayCommand AutoPlanSelectAllCommand { get; }
        public IAsyncRelayCommand SaveTemplateConfirmCommand { get; }
        public IRelayCommand SaveTemplateCancelCommand { get; }
        public IAsyncRelayCommand DeleteTemplateCommand { get; }
        public IRelayCommand<ScheduleCellVm> CellClickCommand { get; }
        public IRelayCommand<ShiftOptionVm> PickShiftCommand { get; }
        public IRelayCommand ClosePickerCommand { get; }

        private static DateTime StartOfWeek(DateTime d)
        {
            return d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        }

        /// <summary>加载当前周期排班并重建网格（不管理忙碌态，供其他操作复用）。</summary>
        private async Task LoadCoreAsync()
        {
            List<ScheduleDto> list = await Api.QuerySchedulesAsync(PeriodStart, PeriodEnd);
            RebuildDayHeaders();
            if (Shifts.Count == 0)
            {
                foreach (ShiftDto s in await Api.GetShiftsAsync()) { Shifts.Add(s); }
                RebuildLegendShifts();
            }
            var employeesPage = await Api.QueryEmployeesAsync(new EmployeeQueryRequest { PageIndex = 1, PageSize = 200, Status = EmployeeStatus.Active });
            Employees.Clear();
            foreach (EmployeeDto e in employeesPage.Items.OrderBy(e => e.Id)) { Employees.Add(e); }
            AutoPlanEmployees.Clear();
            foreach (EmployeeDto e in Employees) { AutoPlanEmployees.Add(new EmployeeSelectItemVm { Id = e.Id, Name = e.Name, PositionName = e.PositionName }); }
            WireAutoPlanEmployeeSelection();

            _allGridRows.Clear();
            var byEmpDate = list.GroupBy(s => s.EmployeeId + "|" + s.WorkDate.Date.ToString("yyyy-MM-dd"))
                .ToDictionary(g => g.Key, g => g.ToList());
            // 在岗员工为主体；另有排班但不在在岗列表的员工（休假/离岗）也保留其排班行
            var rowEmployees = Employees.ToList();
            var knownIds = new HashSet<int>(rowEmployees.Select(e => e.Id));
            foreach (ScheduleDto s in list)
            {
                if (knownIds.Contains(s.EmployeeId)) { continue; }
                knownIds.Add(s.EmployeeId);
                rowEmployees.Add(new EmployeeDto { Id = s.EmployeeId, EmpNo = s.EmpNo, Name = s.EmpName });
            }
            int restShiftId = Shifts.FirstOrDefault(x => string.Equals(x.Name, "休", StringComparison.Ordinal))?.Id ?? -1;
            foreach (EmployeeDto emp in rowEmployees.OrderBy(e => e.Id))
            {
                var row = new ScheduleGridRowVm
                {
                    EmployeeId = emp.Id,
                    EmpName = string.IsNullOrEmpty(emp.Name) ? emp.EmpNo : emp.Name,
                    PositionName = emp.PositionName,
                    Cells = new List<ScheduleCellVm>()
                };
                for (int i = 0; i < PeriodDays; i++)
                {
                    DateTime day = PeriodStart.AddDays(i);
                    var cell = new ScheduleCellVm { EmployeeId = emp.Id, WorkDate = day };
                    byEmpDate.TryGetValue(emp.Id + "|" + day.ToString("yyyy-MM-dd"), out var dtos);
                    cell.Dtos = dtos ?? new List<ScheduleDto>();
                    cell.Refresh(Shifts.ToList());
                    row.Cells.Add(cell);
                }
                row.IsScheduled = row.Cells.Any(c =>
                    (c.PendingShiftId.HasValue && c.PendingShiftId.Value > 0 && c.PendingShiftId.Value != restShiftId) ||
                    c.Dtos.Any(d => d.ShiftId != restShiftId));
                _allGridRows.Add(row);
            }
            ApplyGridFilter();
            OnPropertyChanged(nameof(PeriodText));
            OnPropertyChanged(nameof(PeriodDays));
            OnPropertyChanged(nameof(IsPeriodValid));
        }

        /// <summary>重建周期每日列标题。</summary>
        private void RebuildDayHeaders()
        {
            DayHeaders.Clear();
            for (int i = 0; i < PeriodDays; i++) { DayHeaders.Add(DayHeader(i)); }
        }

        /// <summary>按 全部/已排班/未排班 过滤行展示。</summary>
        private void ApplyGridFilter()
        {
            GridRows.Clear();
            foreach (ScheduleGridRowVm row in _allGridRows)
            {
                bool show = _gridFilter == 0
                    || (_gridFilter == 1 && row.IsScheduled)
                    || (_gridFilter == 2 && !row.IsScheduled);
                if (show) { GridRows.Add(row); }
            }
            OnPropertyChanged(nameof(IsEmpty));
        }

        private void ToggleAllEmployees()
        {
            bool all = AutoPlanEmployees.Count > 0 && AutoPlanEmployees.All(e => e.IsSelected);
            foreach (EmployeeSelectItemVm e in AutoPlanEmployees) { e.IsSelected = !all; }
            OnPropertyChanged(nameof(IsAutoPlanAllEmployeesSelected));
        }

        private void WireAutoPlanEmployeeSelection()
        {
            foreach (EmployeeSelectItemVm e in AutoPlanEmployees)
            {
                e.PropertyChanged -= OnAutoPlanEmployeeSelChanged;
                e.PropertyChanged += OnAutoPlanEmployeeSelChanged;
            }
            OnPropertyChanged(nameof(IsAutoPlanAllEmployeesSelected));
        }

        private void OnAutoPlanEmployeeSelChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EmployeeSelectItemVm.IsSelected))
            {
                OnPropertyChanged(nameof(IsAutoPlanAllEmployeesSelected));
            }
        }

        /// <summary>按原型顺序构建去重图例（早班/中班/晚班/休），并过滤可排班次。</summary>
        private void RebuildLegendShifts()
        {
            string[] order = { "早班", "中班", "晚班", "休" };
            LegendShifts.Clear();
            foreach (string name in order)
            {
                ShiftDto s = Shifts.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));
                if (s != null) { LegendShifts.Add(s); }
            }
            AssignShifts.Clear();
            foreach (ShiftDto s in LegendShifts.Where(x => (x.MinRequired ?? 0) > 0)) { AssignShifts.Add(s); }
            if (AssignShifts.Count == 0) { foreach (ShiftDto s in LegendShifts) { AssignShifts.Add(s); } }
        }

        public async Task LoadAsync()
        {
            await RunAsync(LoadCoreAsync, null);
        }

        /// <summary>点击单元格 → 班次选择弹窗（早/中/晚/休）。</summary>
        private void OpenPicker(ScheduleCellVm cell)
        {
            if (cell == null) { return; }
            bool locked = cell.PendingShiftId == null && cell.Dtos.Any(d => d.Status == ScheduleStatus.Published);
            if (locked)
            {
                ErrorText = "已发布排班不可修改，仅可调整草稿排班。";
                return;
            }
            _pickerCell = cell;
            ScheduleGridRowVm row = GridRows.FirstOrDefault(r => r.EmployeeId == cell.EmployeeId);
            string[] weeks = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            PickerTitle = (row == null ? "员工" : row.EmpName) + " · " + cell.WorkDate.ToString("MM-dd") + "（" + weeks[(int)cell.WorkDate.DayOfWeek] + "）";
            ShiftOptions.Clear();
            foreach (ShiftDto s in LegendShifts)
            {
                bool isRest = string.Equals(s.Name, "休", StringComparison.Ordinal);
                ShiftOptions.Add(new ShiftOptionVm
                {
                    ShiftId = s.Id,
                    // 需求：班次表单中「休 -」标识字改为「请假」，且不需要补充说明（Detail 留空）
                    Label = isRest ? "请假" : ScheduleCellVm.ShortName(s.Name),
                    Detail = isRest ? string.Empty : s.Name + " " + s.StartTime + "-" + s.EndTime
                });
            }
            ShiftOptions.Add(new ShiftOptionVm { ShiftId = null, Label = "休", Detail = "休息日" });
            IsPickerVisible = true;
        }

        /// <summary>选择班次：草稿=替换（删除旧草稿+新增）；休=移除草稿；已发布不动。</summary>
        private void ApplyPick(ShiftOptionVm option)
        {
            var cell = _pickerCell;
            IsPickerVisible = false;
            if (cell == null || option == null) { return; }
            int? target = option.ShiftId; // null=休

            _pendingAdds.RemoveAll(a => a.EmployeeId == cell.EmployeeId && a.WorkDate.Date == cell.WorkDate.Date);

            if (target.HasValue)
            {
                if (cell.Dtos.Any(d => d.Status == ScheduleStatus.Draft && d.ShiftId == target.Value))
                {
                    if (cell.PendingShiftId == null) { return; } // 本就是该班次
                    _pendingDeletes.RemoveAll(id => cell.Dtos.Any(d => d.Id == id && d.Status == ScheduleStatus.Draft));
                    cell.PendingShiftId = null;
                    ErrorText = string.Empty;
                    StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已还原为原班次（未保存）";
                    cell.Refresh(Shifts.ToList());
                    return;
                }
                foreach (ScheduleDto d in cell.Dtos.Where(d => d.Status == ScheduleStatus.Draft))
                {
                    if (!_pendingDeletes.Contains(d.Id)) { _pendingDeletes.Add(d.Id); }
                }
                _pendingAdds.Add(new ScheduleRequest { EmployeeId = cell.EmployeeId, ShiftId = target.Value, WorkDate = cell.WorkDate.Date });
                cell.PendingShiftId = target.Value;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已加入未保存修改，点击「保存排班」提交";
            }
            else
            {
                if (cell.Dtos.Count == 0 && cell.PendingShiftId == null) { return; } // 本就是休
                foreach (ScheduleDto d in cell.Dtos.Where(d => d.Status == ScheduleStatus.Draft))
                {
                    if (!_pendingDeletes.Contains(d.Id)) { _pendingDeletes.Add(d.Id); }
                }
                cell.PendingShiftId = -1;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已标记为休（未保存），点击「保存排班」提交";
            }
            ErrorText = string.Empty;
            cell.Refresh(Shifts.ToList());
        }

        /// <summary>打开保存排班周期对话框（默认当前周）。</summary>
        private void OpenSaveDialog()
        {
            SaveFromDate = PeriodStart;
            SaveToDate = PeriodEnd;
            ErrorText = string.Empty;
            IsSaveVisible = true;
        }

        /// <summary>保存排班：提交草稿并同步周期；无未保存修改时也允许直接从已有排班生成模板。</summary>
        private async Task SaveAsync()
        {
            DateTime from = (SaveFromDate ?? PeriodStart).Date;
            DateTime to = (SaveToDate ?? PeriodEnd).Date;
            if (to < from) { ErrorText = "保存周期结束日期不能早于开始日期"; return; }
            IsSaveVisible = false;
            if (_pendingDeletes.Count == 0 && _pendingAdds.Count == 0)
            {
                // 无未保存修改：不再阻断，允许用已有排班数据生成模板
                _lastSaveFrom = from;
                _lastSaveTo = to;
                IsSaveTemplateVisible = true;
                return;
            }
            await RunGuardedAsync(async () =>
            {
                foreach (int id in _pendingDeletes.ToList()) { await Api.DeleteScheduleAsync(id); }
                if (_pendingAdds.Count > 0)
                {
                    await Api.GenerateSchedulesAsync(new ScheduleGenerateRequest
                    {
                        Items = _pendingAdds.ToList(),
                        FromDate = from,
                        ToDate = to
                    });
                }
                _pendingDeletes.Clear();
                _pendingAdds.Clear();
                foreach (ScheduleGridRowVm row in GridRows)
                {
                    foreach (ScheduleCellVm cell in row.Cells) { cell.PendingShiftId = null; }
                }
                // 同步工具栏日历周期至保存后的周期（避免 setter 重复加载）
                _periodStart = from;
                _periodEnd = to;
                OnPropertyChanged(nameof(PeriodStart));
                OnPropertyChanged(nameof(PeriodEnd));
                OnPropertyChanged(nameof(PeriodDays));
                OnPropertyChanged(nameof(PeriodText));
                OnPropertyChanged(nameof(IsPeriodValid));
                PersistPeriod();
                await LoadCoreAsync();
                _lastSaveFrom = from;
                _lastSaveTo = to;
                IsSaveTemplateVisible = true;
            }, "排班已保存");
        }

        /// <summary>保存成功后确认生成模板：从刚保存的周期提取排班模板数据。</summary>
        private async Task SaveTemplateFromLastSaveAsync()
        {
            if (_lastSaveTo < _lastSaveFrom) { ErrorText = "模板周期无效"; return; }
            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                await Api.SaveScheduleTemplateAsync(new ScheduleTemplateSaveRequest
                {
                    Name = "排班模板 " + _lastSaveFrom.ToString("MM-dd") + "~" + _lastSaveTo.ToString("MM-dd"),
                    FromDate = _lastSaveFrom,
                    ToDate = _lastSaveTo
                });
                IsSaveTemplateVisible = false;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "排班模板已生成";
            }
            catch (ApiClientException ex) { ErrorText = ex.Message; }
            catch (Exception ex) { ErrorText = "操作失败：" + ex.Message; }
            finally { IsBusy = false; }
        }

        /// <summary>打开自动排班表单：默认本周、默认勾选全部可排班次，可自定义员工/日期/班次/模板。</summary>
        private void OpenAutoPlan()
        {
            AutoPlanFromDate = PeriodStart;
            AutoPlanToDate = PeriodEnd;
            AutoPlanShiftItems.Clear();
            foreach (ShiftDto s in AssignShifts)
            {
                AutoPlanShiftItems.Add(new ShiftSelectItemVm { Id = s.Id, Name = s.Name, IsSelected = false });
            }
            SelectedTemplateId = 0;
            ErrorText = string.Empty;
            IsAutoPlanVisible = true;
            _ = LoadTemplatesAsync();
        }

        /// <summary>加载排班模板（自动排班表单选择用）。</summary>
        private async Task LoadTemplatesAsync()
        {
            try
            {
                List<ScheduleTemplateDto> templates = await Api.GetScheduleTemplatesAsync();
                AutoPlanTemplates.Clear();
                foreach (ScheduleTemplateDto t in templates)
                {
                    AutoPlanTemplates.Add(new ScheduleTemplateVm
                    {
                        Id = t.Id,
                        Name = t.Name,
                        FromDate = t.FromDate,
                        ToDate = t.ToDate,
                        ItemCount = t.ItemCount
                    });
                }
            }
            catch { /* 模板加载失败不阻塞自动排班 */ }
        }

        /// <summary>删除自动排班中选中的模板。</summary>
        private async Task DeleteSelectedTemplateAsync()
        {
            ScheduleTemplateVm t = AutoPlanTemplates.FirstOrDefault(x => x.Id == SelectedTemplateId);
            if (t == null) { ErrorText = "请先选择一个排班模板"; return; }
            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                await Api.DeleteScheduleTemplateAsync(t.Id);
                AutoPlanTemplates.Remove(t);
                SelectedTemplateId = 0;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "模板已删除";
            }
            catch (ApiClientException ex) { ErrorText = ex.Message; }
            catch (Exception ex) { ErrorText = "操作失败：" + ex.Message; }
            finally { IsBusy = false; }
        }

        /// <summary>自动排班：模板优先；否则选中员工×日期×轮换班次生成草稿；未选员工则回退服务端自动轮转。</summary>
        private async Task GenerateAsync()
        {
            var items = new List<ScheduleRequest>();
            var selected = AutoPlanEmployees.Where(e => e.IsSelected).Select(e => e.Id).ToList();
            DateTime from = (AutoPlanFromDate ?? PeriodStart).Date;
            DateTime to = (AutoPlanToDate ?? PeriodEnd).Date;
            if (to < from) { ErrorText = "排班结束日期不能早于开始日期"; return; }
            List<int> shiftIds = AutoPlanShiftItems.Where(x => x.IsSelected).Select(x => x.Id).ToList();

            ScheduleTemplateVm tmpl = AutoPlanTemplates.FirstOrDefault(x => x.Id == SelectedTemplateId);
            if (tmpl != null)
            {
                // 模板套用：以模板自身的排班周期作为目标周期，避免被默认周覆盖
                from = tmpl.FromDate.Date;
                to = tmpl.ToDate.Date;
                // 模板优先：仅从「保存排班」生成的模板套用到目标周期
                ScheduleTemplateDto detail;
                try { detail = await Api.GetScheduleTemplateAsync(tmpl.Id); }
                catch (ApiClientException ex) { ErrorText = ex.Message; return; }
                catch (Exception ex) { ErrorText = "操作失败：" + ex.Message; return; }
                if (detail == null || detail.Items == null || detail.Items.Count == 0)
                {
                    ErrorText = "所选模板没有排班数据，无法套用";
                    return;
                }
                foreach (ScheduleTemplateItemDto it in detail.Items)
                {
                    DateTime date = from.AddDays(it.DayOffset);
                    if (date > to) { continue; }
                    items.Add(new ScheduleRequest { EmployeeId = it.EmployeeId, ShiftId = it.ShiftId, WorkDate = date });
                }
                if (items.Count == 0) { ErrorText = "目标周期与模板排班不匹配，无法生成排班"; return; }
            }
            else if (selected.Count > 0)
            {
                if (shiftIds.Count == 0) { ErrorText = "请至少选择一个班次"; return; }
                // 自定义：员工 × 日期 × 轮换班次（避免同人同日多班）
                for (DateTime d = from; d <= to; d = d.AddDays(1))
                {
                    int dayIdx = (int)(d.Date - from.Date).TotalDays;
                    foreach (int empId in selected)
                    {
                        int empIdx = selected.IndexOf(empId);
                        int shiftId = shiftIds[(dayIdx + empIdx) % shiftIds.Count];
                        items.Add(new ScheduleRequest { EmployeeId = empId, ShiftId = shiftId, WorkDate = d.Date });
                    }
                }
            }
            else
            {
                // 未选员工：回退服务端自动轮转（覆盖 from~to 缺口）
                items = null;
            }

            await RunGuardedAsync(async () =>
            {
                SchedulePlanDto plan = await Api.GenerateSchedulesAsync(new ScheduleGenerateRequest
                {
                    Items = items,
                    FromDate = from,
                    ToDate = to,
                    ShiftIds = items == null && shiftIds.Count > 0 ? shiftIds : null
                });
                IsAutoPlanVisible = false;
                // 生成成功后：同步工具栏/排班周期与自动排班日历至本次生成周期
                _periodStart = from;
                _periodEnd = to;
                OnPropertyChanged(nameof(PeriodStart));
                OnPropertyChanged(nameof(PeriodEnd));
                OnPropertyChanged(nameof(PeriodDays));
                OnPropertyChanged(nameof(PeriodText));
                OnPropertyChanged(nameof(IsPeriodValid));
                PersistPeriod();
                await LoadCoreAsync();
                if (plan != null && plan.Conflicts != null && plan.Conflicts.Count > 0)
                {
                    ErrorText = "自动排班完成，但检测到同人同日多班冲突，请调整后再排：" +
                                string.Join("；", plan.Conflicts.Select(c => c.Detail).Distinct());
                    return;
                }
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "自动排班已生成草稿";
            }, null);
        }

        /// <summary>打开班次时间设置：仅早/中/晚班（需求人数&gt;0，AssignShifts），休不设时间窗。</summary>
        private void OpenShiftSetting()
        {
            EditableShifts.Clear();
            foreach (ShiftDto s in AssignShifts)
            {
                EditableShifts.Add(new EditableShiftVm
                {
                    Id = s.Id,
                    Name = s.Name,
                    StartTime = s.StartTime ?? string.Empty,
                    EndTime = s.EndTime ?? string.Empty
                });
            }
            ErrorText = string.Empty;
            IsShiftSettingVisible = true;
        }

        /// <summary>保存班次时间设置：逐班次更新开始/结束时间（MinRequired 传 null 保留原值），成功后刷新图例/班次。</summary>
        private async Task SaveShiftSettingAsync()
        {
            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                foreach (EditableShiftVm e in EditableShifts)
                {
                    if (string.IsNullOrWhiteSpace(e.Name)) { continue; }
                    await Api.UpdateShiftAsync(e.Id, new ShiftRequest
                    {
                        Name = e.Name,
                        StartTime = string.IsNullOrWhiteSpace(e.StartTime) ? null : e.StartTime.Trim(),
                        EndTime = string.IsNullOrWhiteSpace(e.EndTime) ? null : e.EndTime.Trim(),
                        MinRequired = null // 保留原需求人数
                    });
                }
                IsShiftSettingVisible = false;
                Shifts.Clear();
                foreach (ShiftDto s in await Api.GetShiftsAsync()) { Shifts.Add(s); }
                RebuildLegendShifts();
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "班次时间已更新";
            }
            catch (ApiClientException ex) { ErrorText = ex.Message; }
            catch (Exception ex) { ErrorText = "操作失败：" + ex.Message; }
            finally { IsBusy = false; }
        }

        /// <summary>打开批量删除排班：默认当前周期，勾选全部班次。</summary>
        private void OpenBatchDelete()
        {
            BatchDeleteFromDate = PeriodStart;
            BatchDeleteToDate = PeriodEnd;
            BatchDeleteShifts.Clear();
            foreach (ShiftDto s in Shifts)
            {
                BatchDeleteShifts.Add(new ShiftSelectItemVm { Id = s.Id, Name = s.Name });
            }
            ErrorText = string.Empty;
            IsBatchDeleteVisible = true;
        }

        /// <summary>批量删除排班：按周期（可限定班次）软删，成功后刷新网格。</summary>
        private async Task DeleteBatchAsync()
        {
            DateTime from = (BatchDeleteFromDate ?? PeriodStart).Date;
            DateTime to = (BatchDeleteToDate ?? PeriodEnd).Date;
            if (to < from) { ErrorText = "删除周期结束日期不能早于开始日期"; return; }
            List<int> shiftIds = BatchDeleteShifts.Where(x => x.IsSelected).Select(x => x.Id).ToList();
            if (shiftIds.Count == 0) { ErrorText = "请至少勾选一个班次"; return; }

            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                int deleted = await Api.DeleteSchedulesBatchAsync(new ScheduleBatchDeleteRequest
                {
                    FromDate = from,
                    ToDate = to,
                    ShiftIds = shiftIds
                });
                IsBatchDeleteVisible = false;
                await LoadCoreAsync();
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已批量删除 " + deleted + " 条排班";
            }
            catch (ApiClientException ex) { ErrorText = ex.Message; }
            catch (Exception ex) { ErrorText = "操作失败：" + ex.Message; }
            finally { IsBusy = false; }
        }

        /// <summary>本地忙碌/异常保护（区别于基类 RunAsync：成功文案由 action 自行控制）。</summary>
        private async Task RunGuardedAsync(Func<Task> action, string successMessage)
        {
            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                await action();
                if (!string.IsNullOrEmpty(successMessage))
                {
                    StatusText = DateTime.Now.ToString("HH:mm:ss ") + successMessage;
                }
            }
            catch (ApiClientException ex)
            {
                ErrorText = ex.Message;
                try { await LoadCoreAsync(); } catch { /* 恢复失败时保留原网格 */ }
                ClearPending();
            }
            catch (Exception ex)
            {
                ErrorText = "操作失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ClearPending()
        {
            _pendingDeletes.Clear();
            _pendingAdds.Clear();
        }
    }
}
