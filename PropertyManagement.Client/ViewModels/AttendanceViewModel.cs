using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Org;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>考勤行（PG-ORG-03）：状态彩色标签（正常绿/迟到橙/缺卡红/异常红）+ 待审核行显示「补卡审核」。</summary>
    public class AttendanceRow : ObservableObject
    {
        public AttendanceDto Dto { get; set; }

        public string EmpNo { get { return Dto.EmpNo ?? ("YG-" + Dto.EmployeeId.ToString("000")); } }
        public string EmpName { get { return Dto.EmpName ?? string.Empty; } }
        public string ShiftName { get { return string.IsNullOrEmpty(Dto.ShiftName) ? "—" : Dto.ShiftName; } }
        public string DateText { get { return string.IsNullOrEmpty(Dto.WorkDateText) ? Dto.WorkDate.ToString("MM-dd") : Dto.WorkDateText; } }
        public string CheckIn { get { return string.IsNullOrEmpty(Dto.CheckIn) ? "—" : Dto.CheckIn; } }
        public string CheckOut { get { return string.IsNullOrEmpty(Dto.CheckOut) ? "—" : Dto.CheckOut; } }

        public string ResultText
        {
            get
            {
                if (!string.IsNullOrEmpty(Dto.ResultText)) { return Dto.ResultText; }
                switch (Dto.Result)
                {
                    case AttendanceResult.Recorded: return "已记录";
                    case AttendanceResult.Normal: return "正常";
                    case AttendanceResult.Abnormal: return "异常";
                    case AttendanceResult.Reviewed: return "已审核";
                    case AttendanceResult.Late: return "迟到";
                    case AttendanceResult.Absent: return "旷工";
                    case AttendanceResult.Leave: return "请假";
                    default: return "异常";
                }
            }
        }

        public Brush ResultBg
        {
            get
            {
                switch (Dto.Result)
                {
                    case AttendanceResult.Normal: return Solid("#E8F7F1");   // 正常 绿
                    case AttendanceResult.Late: return Solid("#FFF5DC");     // 迟到 橙
                    case AttendanceResult.Absent: return Solid("#FDECEC");   // 旷工 红
                    case AttendanceResult.Leave: return Solid("#EFEEFF");   // 请假 紫
                    case AttendanceResult.Abnormal: return Solid("#FDECEC");   // 异常 红
                    case AttendanceResult.Reviewed: return Solid("#E9F0EE");   // 已审核 蓝
                    default: return Solid("#F2F4F8");                          // 已记录 灰
                }
            }
        }

        public Brush ResultFg
        {
            get
            {
                switch (Dto.Result)
                {
                    case AttendanceResult.Normal: return Solid("#12805C");
                    case AttendanceResult.Late: return Solid("#B76E00");
                    case AttendanceResult.Absent: return Solid("#D64545");
                    case AttendanceResult.Leave: return Solid("#5B3DF5");
                    case AttendanceResult.Abnormal: return Solid("#D64545");
                    case AttendanceResult.Reviewed: return Solid("#1F4B43");
                    default: return Solid("#98A2B3");
                }
            }
        }

        /// <summary>审核状态（服务端口径：无 review_by 且异常/迟到/缺卡/缺下班卡 → 待审核；已审 → 已确认）。</summary>
        public string ReviewStatusText
        {
            get
            {
                if (!string.IsNullOrEmpty(Dto.ReviewStatusText)) { return Dto.ReviewStatusText; }
                return Dto.ReviewBy.HasValue ? "已确认" : "待审核";
            }
        }

        /// <summary>「补卡审核」入口：仅待审核（异常/迟到/缺卡）行显示（审计 3.9）。</summary>
        public bool IsReviewable { get { return ReviewStatusText == "待审核"; } }
        public bool IsNotReviewable { get { return !IsReviewable; } }

        /// <summary>审核弹窗展示的异常类型（迟到/缺卡等，t_attendance.abnormal_type）。</summary>
        public string AbnormalTypeText { get { return string.IsNullOrEmpty(Dto.AbnormalType) ? "—" : Dto.AbnormalType; } }

        /// <summary>申诉/备注（原打卡说明）。</summary>
        public string AppealText { get { return string.IsNullOrEmpty(Dto.ReviewNote) && string.IsNullOrEmpty(Dto.ReviewText) ? "—" : (Dto.ReviewNote ?? Dto.ReviewText); } }

        private static Brush Solid(string hex)
        {
            SolidColorBrush b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }

    /// <summary>考勤记录页（PG-ORG-03，UC-ORG-004，BR-ORG-05）：统计卡、年月/部门/异常类型筛选、
    /// 补卡审核（通过回写打卡闭环 / 驳回必填意见）、月报 CSV 导出（UTF-8）。</summary>
    /// <summary>审核状态标识选项（正常/迟到/旷工/请假），含前后台配色。</summary>
    public class AttendanceStatusOptionVm : ObservableObject
    {
        public AttendanceResult Result { get; set; }
        public string Label { get; set; }
        public string Bg { get; set; }
        public string Fg { get; set; }
        public Brush BgBrush { get { return Solid(Bg); } }
        public Brush FgBrush { get { return Solid(Fg); } }
        private static Brush Solid(string hex)
        {
            SolidColorBrush b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }

    public class AttendanceViewModel : BaseInfoPageViewModel
    {
        private const int PageSize = 20;

        private DateTime? _selectedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private int? _deptFilter;
        private int _abnormalFilter;
        private string _keyword = string.Empty;
        private int _pageIndex = 1;
        private int _total;
        private bool _isBatchReviewVisible;
        private readonly DispatcherTimer _searchTimer;
        private AttendanceRow _statusPickerRow;
        private bool _isStatusPickerVisible;
        private double _attendanceRate;
        private int _lateCount;
        private int _absentCount;
        private int _leaveCount;
        private int _pendingReviewCount;
        private bool _hasRateDelta;
        private string _rateDeltaText = string.Empty;

        public AttendanceViewModel(IApiClient api) : base(api)
        {
            _selectedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); _ = SearchAsync(); };
            StatusFilterItems.Add(new OrgFilterItem { Value = 0, Label = "状态标识：全部" });
            StatusFilterItems.Add(new OrgFilterItem { Value = 1, Label = "状态标识：正常" });
            StatusFilterItems.Add(new OrgFilterItem { Value = 2, Label = "状态标识：迟到" });
            StatusFilterItems.Add(new OrgFilterItem { Value = 3, Label = "状态标识：旷工" });
            StatusFilterItems.Add(new OrgFilterItem { Value = 4, Label = "状态标识：请假" });
            StatusOptions.Add(new AttendanceStatusOptionVm { Result = AttendanceResult.Normal, Label = "正常", Bg = "#E8F7F1", Fg = "#12805C" });
            StatusOptions.Add(new AttendanceStatusOptionVm { Result = AttendanceResult.Late, Label = "迟到", Bg = "#FFF5DC", Fg = "#B76E00" });
            StatusOptions.Add(new AttendanceStatusOptionVm { Result = AttendanceResult.Absent, Label = "旷工", Bg = "#FDECEC", Fg = "#D64545" });
            StatusOptions.Add(new AttendanceStatusOptionVm { Result = AttendanceResult.Leave, Label = "请假", Bg = "#EFEEFF", Fg = "#5B3DF5" });
            QueryCommand = new AsyncRelayCommand(() => SearchAsync());
            SearchCommand = new AsyncRelayCommand(() => SearchAsync()); // 回车触发
            ReviewCommand = new RelayCommand<AttendanceRow>(OpenStatusPicker);
            SetStatusCommand = new AsyncRelayCommand<AttendanceStatusOptionVm>(SetStatusAsync);
            CancelStatusCommand = new RelayCommand(() => { IsStatusPickerVisible = false; StatusPickerRow = null; });
            BatchReviewCommand = new RelayCommand(OpenBatchReview);
            BatchReviewConfirmCommand = new AsyncRelayCommand(ConfirmBatchReviewAsync);
            BatchReviewCancelCommand = new RelayCommand(() => IsBatchReviewVisible = false);
            ExportCommand = new AsyncRelayCommand(ExportCsvAsync);
            PrevPageCommand = new RelayCommand(() => { if (_pageIndex > 1) { _pageIndex--; _ = LoadAsync(); } });
            NextPageCommand = new RelayCommand(() => { if (_pageIndex < TotalPages) { _pageIndex++; _ = LoadAsync(); } });
            _ = ReloadAllAsync();
        }

        public ObservableCollection<AttendanceRow> Items { get; } = new ObservableCollection<AttendanceRow>();
        public ObservableCollection<OrgFilterItem> DeptFilterItems { get; } = new ObservableCollection<OrgFilterItem>();
        public ObservableCollection<OrgFilterItem> StatusFilterItems { get; } = new ObservableCollection<OrgFilterItem>();

        public int Year { get { return (_selectedMonth ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Year; } }
        public int Month { get { return (_selectedMonth ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Month; } }

        /// <summary>月份日历（合并原 年/月 下拉）：点击日历任一天，按其年+月查询。</summary>
        public DateTime? SelectedMonth
        {
            get { return _selectedMonth; }
            set
            {
                DateTime v = value == null || value.Value == default
                    ? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
                    : value.Value.Date;
                if (SetProperty(ref _selectedMonth, v))
                {
                    OnPropertyChanged(nameof(Year));
                    OnPropertyChanged(nameof(Month));
                    _pageIndex = 1;
                    _ = ReloadAllAsync();
                }
            }
        }

        public int? DeptFilter
        {
            get { return _deptFilter; }
            set
            {
                if (SetProperty(ref _deptFilter, value))
                {
                    _pageIndex = 1;
                    _ = ReloadAllAsync();
                }
            }
        }

        /// <summary>0 全部 / 1 迟到 / 2 缺卡 / 3 异常（服务端 Result 为单值过滤）。</summary>
        public int AbnormalFilter { get { return _abnormalFilter; } set { if (SetProperty(ref _abnormalFilter, value)) { _pageIndex = 1; _ = LoadAsync(); } } }
        public string Keyword
        {
            get { return _keyword; }
            set
            {
                if (SetProperty(ref _keyword, value))
                {
                    // 输入即自动搜索（防抖），参考财务收费搜索框逻辑
                    _searchTimer.Stop();
                    _searchTimer.Start();
                }
            }
        }

        public int Total
        {
            get { return _total; }
            private set { SetProperty(ref _total, value); OnPropertyChanged(nameof(TotalText)); OnPropertyChanged(nameof(TotalPages)); }
        }

        public int TotalPages { get { return Math.Max(1, (int)Math.Ceiling(_total / (double)PageSize)); } }
        public string TotalText { get { return "共 " + _total + " 条记录 · 第 " + _pageIndex + "/" + TotalPages + " 页"; } }

        // ---- 统计卡（PG-ORG-03 四卡） ----
        public string RateText { get { return _attendanceRate.ToString("0.0", CultureInfo.InvariantCulture) + "%"; } }
        public string RateDeltaText { get { return _rateDeltaText; } private set { SetProperty(ref _rateDeltaText, value); } }
        public bool HasRateDelta { get { return _hasRateDelta; } private set { SetProperty(ref _hasRateDelta, value); } }
        public string LateText { get { return _lateCount + " 人次"; } }
        public string AbsentText { get { return _absentCount + " 人次"; } }
        public string LeaveText { get { return _leaveCount + " 人次"; } }
        public string PendingText { get { return _pendingReviewCount.ToString(); } }

        public AttendanceRow StatusPickerRow { get { return _statusPickerRow; } set { SetProperty(ref _statusPickerRow, value); } }
        public bool IsStatusPickerVisible { get { return _isStatusPickerVisible; } set { SetProperty(ref _isStatusPickerVisible, value); } }
        public ObservableCollection<AttendanceStatusOptionVm> StatusOptions { get; } = new ObservableCollection<AttendanceStatusOptionVm>();

        // ---- 批量审核 ----
        public bool IsBatchReviewVisible { get { return _isBatchReviewVisible; } set { SetProperty(ref _isBatchReviewVisible, value); } }
        public int PendingReviewCount { get { return Items.Count(i => i.IsReviewable); } }
        public string BatchReviewCountText { get { return "待审核 " + PendingReviewCount + " 条记录将批量通过并闭环为正常"; } }

        public IAsyncRelayCommand QueryCommand { get; }
        public IAsyncRelayCommand SearchCommand { get; }
        public IRelayCommand<AttendanceRow> ReviewCommand { get; }
        public IAsyncRelayCommand<AttendanceStatusOptionVm> SetStatusCommand { get; }
        public IRelayCommand CancelStatusCommand { get; }
        public IRelayCommand BatchReviewCommand { get; }
        public IAsyncRelayCommand BatchReviewConfirmCommand { get; }
        public IRelayCommand BatchReviewCancelCommand { get; }
        public IAsyncRelayCommand ExportCommand { get; }
        public IRelayCommand PrevPageCommand { get; }
        public IRelayCommand NextPageCommand { get; }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                DateTime from = new DateTime(Year, Month, 1);
                DateTime to = from.AddMonths(1).AddDays(-1);
                var query = new AttendanceQueryRequest
                {
                    PageIndex = _pageIndex,
                    PageSize = PageSize,
                    Keyword = Keyword,
                    WorkDateFrom = from,
                    WorkDateTo = to
                };
                if (_deptFilter.HasValue) { query.DeptId = _deptFilter.Value; }
                if (_abnormalFilter == 1) { query.Result = AttendanceResult.Normal; }
                else if (_abnormalFilter == 2) { query.Result = AttendanceResult.Late; }
                else if (_abnormalFilter == 3) { query.Result = AttendanceResult.Absent; }
                else if (_abnormalFilter == 4) { query.Result = AttendanceResult.Leave; }
                var page = await Api.QueryAttendanceAsync(query);
                Items.Clear();
                foreach (var dto in page.Items) { Items.Add(new AttendanceRow { Dto = dto }); }
                Total = page.Total;
                OnPropertyChanged(nameof(PendingReviewCount));
                OnPropertyChanged(nameof(BatchReviewCountText));
                if (DeptFilterItems.Count == 0)
                {
                    DeptFilterItems.Add(new OrgFilterItem { Value = null, Label = "部门：全部" });
                    foreach (var d in await Api.GetDepartmentsAsync()) { DeptFilterItems.Add(new OrgFilterItem { Value = d.Id, Label = "部门：" + d.Name }); }
                }
            }, null);
        }

        /// <summary>统计卡：当月 + 上月（计算出勤率环比）。</summary>
        private async Task LoadSummaryAsync()
        {
            int? deptId = _deptFilter.HasValue ? _deptFilter.Value : (int?)null;
            AttendanceSummaryDto summary = await Api.GetAttendanceSummaryAsync(Year, Month, deptId);
            DateTime last = new DateTime(Year, Month, 1).AddMonths(-1);
            AttendanceSummaryDto lastSummary = await Api.GetAttendanceSummaryAsync(last.Year, last.Month, deptId);
            _attendanceRate = summary?.AttendanceRate ?? 0;
            _lateCount = summary?.LateCount ?? 0;
            _absentCount = summary?.AbsentCount ?? 0;
            _leaveCount = summary?.LeaveCount ?? 0;
            _pendingReviewCount = summary?.PendingReviewCount ?? 0;
            if (lastSummary != null && lastSummary.TotalCount > 0)
            {
                double delta = _attendanceRate - lastSummary.AttendanceRate;
                RateDeltaText = "环比 " + (delta >= 0 ? "+" : string.Empty) + delta.ToString("0.0", CultureInfo.InvariantCulture) + "%";
                HasRateDelta = true;
            }
            else
            {
                RateDeltaText = string.Empty;
                HasRateDelta = false;
            }
            OnPropertyChanged(nameof(RateText));
            OnPropertyChanged(nameof(LateText));
            OnPropertyChanged(nameof(AbsentText));
            OnPropertyChanged(nameof(LeaveText));
            OnPropertyChanged(nameof(PendingText));
        }

        private async Task ReloadAllAsync()
        {
            await RunAsync(async () =>
            {
                await LoadSummaryAsync();
            }, null);
            await LoadAsync();
        }

        /// <summary>回车搜索：重置到第一页。</summary>
        private async Task SearchAsync()
        {
            _pageIndex = 1;
            await LoadAsync();
        }

        /// <summary>操作栏「审核」：打开员工状态标识选择弹窗。</summary>
        private void OpenStatusPicker(AttendanceRow row)
        {
            if (row == null) { return; }
            StatusPickerRow = row;
            ErrorText = string.Empty;
            IsStatusPickerVisible = true;
        }

        /// <summary>审核：选择状态标识（正常/迟到/旷工/请假）→ 提交并标记「已确认」。</summary>
        private async Task SetStatusAsync(AttendanceStatusOptionVm option)
        {
            var row = StatusPickerRow;
            if (row == null || option == null) { return; }
            await RunAsync(async () =>
            {
                await Api.ReviewAttendanceAsync(row.Dto.Id, new AttendanceReviewRequest
                {
                    Result = option.Result,
                    ReviewNote = "状态标识审核：" + option.Label
                });
                IsStatusPickerVisible = false;
                StatusPickerRow = null;
                await LoadAsync();
                await LoadSummaryAsync();
            }, "审核已确认：" + option.Label);
        }

        /// <summary>打开批量审核确认：勾选待审记录后一键通过。</summary>
        private void OpenBatchReview()
        {
            if (PendingReviewCount == 0)
            {
                ErrorText = "当前页面没有待审核的考勤记录";
                return;
            }
            ErrorText = string.Empty;
            IsBatchReviewVisible = true;
        }

        /// <summary>批量审核：对当前页面全部待审核（异常/待补卡）记录一键通过闭环，返回审核条数。</summary>
        private async Task ConfirmBatchReviewAsync()
        {
            var ids = Items.Where(i => i.IsReviewable).Select(i => i.Dto.Id).ToList();
            if (ids.Count == 0) { ErrorText = "当前页面没有待审核的考勤记录"; return; }
            int count = 0;
            await RunAsync(async () =>
            {
                count = await Api.BatchReviewAttendanceAsync(new AttendanceBatchReviewRequest
                {
                    Ids = ids,
                    IsApproved = true,
                    ReviewNote = "批量审核"
                });
                IsBatchReviewVisible = false;
                await LoadAsync();
                await LoadSummaryAsync();
            }, null);
            StatusText = DateTime.Now.ToString("HH:mm:ss ") + "批量审核完成，共 " + count + " 条记录";
        }

        /// <summary>导出月报：SaveFileDialog + 服务端 CSV + UTF-8 写盘。</summary>
        private async Task ExportCsvAsync()
        {
            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                string csv = await Api.ExportAttendanceCsvAsync(Year, Month, _deptFilter.HasValue ? _deptFilter.Value : (int?)null);
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV 文件|*.csv",
                    FileName = "考勤月报_" + Year.ToString("0000") + Month.ToString("00") + ".csv"
                };
                if (dialog.ShowDialog() != true) { return; }
                System.IO.File.WriteAllText(dialog.FileName, csv ?? string.Empty, System.Text.Encoding.UTF8);
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "考勤月报已导出：" + dialog.FileName;
            }
            catch (ApiClientException ex) { ErrorText = ex.Message; }
            catch (Exception ex) { ErrorText = "导出失败：" + ex.Message; }
            finally { IsBusy = false; }
        }

    }
}
