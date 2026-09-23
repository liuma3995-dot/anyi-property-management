using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>财务报表科目汇总行（PG-FIN-07，T4F-8-1：科目/本期金额/上期金额/环比/本季累计/备注）。</summary>
    public class FinancialSummaryRow : ObservableObject
    {
        public FinancialSummaryItemDto Dto { get; set; }

        public string Category { get { return string.IsNullOrEmpty(Dto.Category) ? "—" : Dto.Category; } }

        public string CurrentAmountText { get { return "¥" + Dto.CurrentAmount.ToString("N2"); } }

        public string PreviousAmountText { get { return "¥" + Dto.PreviousAmount.ToString("N2"); } }

        public string MoMText
        {
            get
            {
                if (!Dto.MoM.HasValue) { return "—"; }
                string sign = Dto.MoM.Value >= 0 ? "+" : string.Empty;
                return sign + Dto.MoM.Value.ToString("0.0") + "%";
            }
        }

        public Brush MoMBrush { get { return Dto.MoM.HasValue && Dto.MoM.Value < 0 ? DangerBrush : OkBrush; } }

        public string QuarterTotalText { get { return "¥" + Dto.QuarterTotal.ToString("N2"); } }

        public string Remark { get { return string.IsNullOrEmpty(Dto.Remark) ? "—" : Dto.Remark; } }

        private static readonly Brush OkBrush = Br("#12805C");
        private static readonly Brush DangerBrush = Br("#D64545");

        private static Brush Br(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    /// <summary>财务报表页（PG-FIN-07，UC-FIN-009/012：月/季报表 + 对比期间 + 科目汇总 + Excel/PDF 导出留痕）。</summary>
    /// <summary>
    /// 报表/导出留痕清单行（CHG-v1.3.1-05）：财务报表模块「报表与导出留痕」用。
    /// 删除 = 留痕软删 + 物理删除服务端生成文件（缓存清理，不涉及任何账目）。
    /// </summary>
    public class ExportTraceRow : ObservableObject
    {
        private bool _isChecked;
        public ExportTraceDto Dto { get; set; }
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }
        public string SourceText { get { return Dto.SourceText ?? string.Empty; } }
        public string KindText { get { return string.IsNullOrEmpty(Dto.Kind) ? "—" : Dto.Kind; } }
        public string PeriodText { get { return string.IsNullOrEmpty(Dto.Period) ? "—" : Dto.Period; } }
        public string FormatText { get { return string.IsNullOrEmpty(Dto.Format) ? "—" : Dto.Format; } }
        public string FileNameText { get { return string.IsNullOrEmpty(Dto.FileName) ? "—" : Dto.FileName; } }
        public string SizeText { get { return string.IsNullOrEmpty(Dto.FileSizeText) ? "—" : Dto.FileSizeText; } }
        public string CreatedText { get { return Dto.CreatedAt == default ? "—" : Dto.CreatedAt.ToString("yyyy-MM-dd HH:mm"); } }
        /// <summary>服务端生成文件是否仍在（不在 = 只剩留痕行）。</summary>
        public string FileStateText { get { return Dto.HasFile ? "已生成" : "文件已不在"; } }
    }

    /// <summary>财务报表页（PG-FIN-07，UC-FIN-009/012：月/季报表 + 对比期间 + 科目汇总 + Excel/PDF 导出留痕）。</summary>
    /// <summary>
    /// 财务报表的统计口径记忆（CHG-v1.3.1-06）：报表类型 + 会计期间 + 对比期间。
    /// 口径与仪表盘一致（CHG-v1.2.0-28）：**本次登录期间跨页面记住**，点「回到本期」回到当前期，**登出清空**。
    /// 记忆由 ShellViewModel 持有（随登录会话生灭），页面 VM 只负责注入初值与回传变更。
    /// </summary>
    public class FinancialReportPeriodState
    {
        /// <summary>0 月报 / 1 季报 / 2 年报。</summary>
        public int PeriodType { get; set; }
        public string Period { get; set; }
        public string ComparePeriod { get; set; }
    }

    /// <summary>财务报表页（PG-FIN-07，UC-FIN-009/012：月/季报表 + 对比期间 + 科目汇总 + Excel/PDF 导出留痕）。</summary>
    public class FinancialReportViewModel : FinancePageViewModel
    {
        private int _periodType;
        private string _period = DateTime.Now.ToString("yyyy-MM");
        private string _comparePeriod = string.Empty;
        private int _chargeItemFilter;
        private bool _includeRefund = true;
        private string _incomeText = "¥0";
        private string _incomeSubText = "环比 —";
        private string _expenseText = "¥0";
        private string _expenseSubText = "环比 —";
        private string _balanceText = "¥0";
        private string _balanceSubText = "结余率 —";
        private string _unpaidText = "—";
        private string _tableTitle = "收支汇总表";
        // CHG-v1.3.1-05：报表与导出留痕清单（缓存清理）
        private bool _isTracePanelVisible;
        private bool _isTraceSelectAll;
        private bool _isTraceConfirmVisible;
        private string _traceConfirmText = string.Empty;
        private string _traceSummaryText = "共 0 条";
        /// <summary>统计口径变化回调（CHG-v1.3.1-06；由 ShellViewModel 记录，供下次进入本页复用）。</summary>
        private readonly Action<FinancialReportPeriodState> _periodStateChanged;

        /// <param name="initialPeriodState">
        /// 上次的统计口径（CHG-v1.3.1-06，由 ShellViewModel 传入；空 = 首次进入，用默认口径：当月 + 上月）。
        /// </param>
        /// <param name="periodStateChanged">口径变化回调（ShellViewModel 记录，供下次进入本页复用）。</param>
        public FinancialReportViewModel(IApiClient api,
            FinancialReportPeriodState initialPeriodState = null,
            Action<FinancialReportPeriodState> periodStateChanged = null) : base(api)
        {
            _periodStateChanged = periodStateChanged;
            GenerateCommand = new AsyncRelayCommand(GenerateAsync);
            ExportExcelCommand = new AsyncRelayCommand(() => ExportAsync(ExportFormat.Excel));
            ExportPdfCommand = new AsyncRelayCommand(() => ExportAsync(ExportFormat.Pdf));
            GoToCurrentPeriodCommand = new AsyncRelayCommand(GoToCurrentPeriodAsync);
            // CHG-v1.3.1-05：报表与导出留痕（缓存）清单
            OpenTracePanelCommand = new AsyncRelayCommand(OpenTracePanelAsync);
            CloseTracePanelCommand = new RelayCommand(() => IsTracePanelVisible = false);
            RequestDeleteTracesCommand = new RelayCommand(RequestDeleteTraces);
            ConfirmDeleteTracesCommand = new AsyncRelayCommand(ConfirmDeleteTracesAsync);
            CancelDeleteTracesCommand = new RelayCommand(() => IsTraceConfirmVisible = false);
            // CHG-v1.3.1-06：优先用上次的统计口径（本次登录期间记住），否则用默认（当月 / 上月）
            if (initialPeriodState != null && !string.IsNullOrWhiteSpace(initialPeriodState.Period))
            {
                _periodType = initialPeriodState.PeriodType;
                _period = initialPeriodState.Period.Trim();
                _comparePeriod = string.IsNullOrWhiteSpace(initialPeriodState.ComparePeriod)
                    ? DefaultComparePeriod(TypeCode(_periodType), _period)
                    : initialPeriodState.ComparePeriod.Trim();
            }
            else
            {
                _comparePeriod = DefaultComparePeriod("month", _period);
            }
            _ = LoadChargeItemsAsync();
            _ = LoadAsync();
        }

        public ObservableCollection<FinancialSummaryRow> SummaryRows { get; } = new ObservableCollection<FinancialSummaryRow>();

        public ObservableCollection<ChargeItemDto> ChargeItems { get; } = new ObservableCollection<ChargeItemDto>();

        /// <summary>CHG-v1.3.1-05：报表与导出留痕清单（报表留痕 / 导出留痕 / 孤立生成文件）。</summary>
        public ObservableCollection<ExportTraceRow> ExportTraces { get; } = new ObservableCollection<ExportTraceRow>();

        /// <summary>清单浮层是否可见。</summary>
        public bool IsTracePanelVisible { get { return _isTracePanelVisible; } private set { SetProperty(ref _isTracePanelVisible, value); } }

        /// <summary>清单「全选」：勾选/取消勾选全部留痕行（作用域 = 当前清单全部）。</summary>
        public bool IsTraceSelectAll
        {
            get { return _isTraceSelectAll; }
            set
            {
                if (SetProperty(ref _isTraceSelectAll, value))
                {
                    foreach (var row in ExportTraces) { row.IsChecked = value; }
                }
            }
        }

        /// <summary>删除确认浮层（弹窗提醒：先备份归档）。</summary>
        public bool IsTraceConfirmVisible { get { return _isTraceConfirmVisible; } private set { SetProperty(ref _isTraceConfirmVisible, value); } }

        public string TraceConfirmText { get { return _traceConfirmText; } private set { SetProperty(ref _traceConfirmText, value); } }

        public string TraceSummaryText { get { return _traceSummaryText; } private set { SetProperty(ref _traceSummaryText, value); } }

        public IRelayCommand OpenTracePanelCommand { get; }
        public IRelayCommand CloseTracePanelCommand { get; }
        public IRelayCommand RequestDeleteTracesCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteTracesCommand { get; }
        public IRelayCommand CancelDeleteTracesCommand { get; }

        public int PeriodType
        {
            get { return _periodType; }
            set
            {
                if (!SetProperty(ref _periodType, value)) { return; }
                string type = TypeCode(value);
                string current = Period == null ? string.Empty : Period.Trim();
                if ((value == 0 && !IsMonthText(current)) ||
                    (value == 1 && !IsQuarterText(current)) ||
                    (value == 2 && !IsYearText(current)))
                {
                    Period = DefaultPeriodText(type);
                }
                ComparePeriod = DefaultComparePeriod(type, Period);
                OnPropertyChanged(nameof(IsAnnualReport));
                OnPropertyChanged(nameof(PeriodCumulativeLabel));
                RaisePeriodState();
            }
        }

        /// <summary>T4R-7：是否年报（用于表头“本年累计”与提示文案）。</summary>
        public bool IsAnnualReport { get { return PeriodType == 2; } }

        public string PeriodCumulativeLabel { get { return PeriodType == 2 ? "本年累计" : "本季累计"; } }

        public string Period
        {
            get { return _period; }
            set
            {
                if (SetProperty(ref _period, value)) { RaisePeriodState(); }
            }
        }

        /// <summary>对比期间（T4F-8-1：2026-07 或 2026-Q2，默认上一期）。</summary>
        public string ComparePeriod
        {
            get { return _comparePeriod; }
            set
            {
                if (SetProperty(ref _comparePeriod, value)) { RaisePeriodState(); }
            }
        }

        /// <summary>收费项目筛选（0=全部，T4F-8-1）。</summary>
        public int ChargeItemFilter { get { return _chargeItemFilter; } set { SetProperty(ref _chargeItemFilter, value); } }

        public bool IncludeRefund { get { return _includeRefund; } set { SetProperty(ref _includeRefund, value); } }

        public string IncomeText { get { return _incomeText; } private set { SetProperty(ref _incomeText, value); } }

        public string IncomeSubText { get { return _incomeSubText; } private set { SetProperty(ref _incomeSubText, value); } }

        public string ExpenseText { get { return _expenseText; } private set { SetProperty(ref _expenseText, value); } }

        public string ExpenseSubText { get { return _expenseSubText; } private set { SetProperty(ref _expenseSubText, value); } }

        public string BalanceText { get { return _balanceText; } private set { SetProperty(ref _balanceText, value); } }

        public string BalanceSubText { get { return _balanceSubText; } private set { SetProperty(ref _balanceSubText, value); } }

        public string UnpaidText { get { return _unpaidText; } private set { SetProperty(ref _unpaidText, value); } }

        public string TableTitle { get { return _tableTitle; } private set { SetProperty(ref _tableTitle, value); } }

        public IAsyncRelayCommand GenerateCommand { get; }

        public IAsyncRelayCommand ExportExcelCommand { get; }

        public IAsyncRelayCommand ExportPdfCommand { get; }

        /// <summary>CHG-v1.3.1-06：「回到本期」——按当前报表类型回到当前期，对比期回到上一期（口径同仪表盘「回到本月」）。</summary>
        public IAsyncRelayCommand GoToCurrentPeriodCommand { get; }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                await LoadReportAsync();
            }, "财务报表已生成");
        }

        private async Task LoadChargeItemsAsync()
        {
            try
            {
                ChargeItems.Clear();
                ChargeItems.Add(new ChargeItemDto { Id = 0, Name = "全部" });
                var items = await Api.GetChargeItemsAsync() ?? new List<ChargeItemDto>();
                foreach (var item in items.Where(x => x.Status == 0))
                {
                    ChargeItems.Add(item);
                }
                OnPropertyChanged(nameof(ChargeItemFilter));
            }
            catch (Exception)
            {
                // 收费项目下拉失败不影响报表加载
            }
        }

        private FinancialReportQueryRequest BuildQuery()
        {
            string periodType = TypeCode(PeriodType);
            string period = Period == null ? string.Empty : Period.Trim();
            if (string.IsNullOrEmpty(period))
            {
                period = DefaultPeriodText(periodType);
            }
            string compare = ComparePeriod == null ? string.Empty : ComparePeriod.Trim();
            int? chargeItemId = ChargeItemFilter > 0 && ChargeItemFilter <= ChargeItems.Count
                ? ChargeItems[ChargeItemFilter].Id
                : (int?)null;
            return new FinancialReportQueryRequest
            {
                PeriodType = periodType,
                Period = period,
                ComparePeriod = string.IsNullOrEmpty(compare) ? null : compare,
                ChargeItemId = chargeItemId,
                IncludeRefund = IncludeRefund
            };
        }

        private async Task GenerateAsync()
        {
            await RunAsync(async () => { await LoadReportAsync(); }, "报表已按所选期间生成");
        }

        private async Task LoadReportAsync()
        {
            var query = BuildQuery();
            var report = await Api.GetFinancialReportAsync(query);

            SummaryRows.Clear();
            foreach (var dto in report.SummaryItems ?? new List<FinancialSummaryItemDto>())
            {
                SummaryRows.Add(new FinancialSummaryRow { Dto = dto });
            }

            // CHG-v1.1.2-55：财务报表金额统一保留 2 位小数（与仪表盘/收款登记/账单/台账/导出统一；
            // 原 N0 会把 1,143.45 显示成 ¥1,143，与仪表盘的 ¥1,144 看起来「对不上」）
            IncomeText = "¥" + report.IncomeTotal.ToString("N2");
            ExpenseText = "¥" + report.ExpenseTotal.ToString("N2");
            BalanceText = "¥" + report.Balance.ToString("N2");
            decimal rate = report.IncomeTotal > 0 ? report.Balance / report.IncomeTotal * 100m : 0m;
            BalanceSubText = "结余率 " + rate.ToString("0.0") + "%";

            string compare = string.IsNullOrEmpty(report.ComparePeriod) ? PrevPeriodText(query.PeriodType, query.Period) : report.ComparePeriod;
            string momLabel = string.Equals(query.PeriodType, "year", StringComparison.OrdinalIgnoreCase) ? "同比" : "环比";
            IncomeSubText = momLabel + " " + PercentText(report.IncomeMomPercent) + "（对比 " + compare + "）";
            ExpenseSubText = momLabel + " " + PercentText(report.ExpenseMomPercent) + "（对比 " + compare + "）";
            TableTitle = "收支汇总表（" + query.Period + (query.ChargeItemId.HasValue ? " · 按收费项目筛选" : string.Empty) + "）";

            try
            {
                var stats = await Api.GetPaymentStatisticsAsync();
                UnpaidText = "¥" + stats.UnpaidAmount.ToString("N2");
            }
            catch (Exception)
            {
                UnpaidText = "—";
            }
        }

        private static string PercentText(decimal value)
        {
            string sign = value >= 0 ? "+" : string.Empty;
            return sign + value.ToString("0.0") + "%";
        }

        // ==================== CHG-v1.3.1-06：统计口径记忆（口径同仪表盘） ====================

        /// <summary>把当前口径回传给 ShellViewModel（本次登录期间跨页面记住）。</summary>
        private void RaisePeriodState()
        {
            if (_periodStateChanged == null) { return; }
            _periodStateChanged(new FinancialReportPeriodState
            {
                PeriodType = PeriodType,
                Period = Period,
                ComparePeriod = ComparePeriod
            });
        }

        /// <summary>「回到本期」：按当前报表类型回到当前期，对比期回到上一期，并立即重新生成报表。</summary>
        private async Task GoToCurrentPeriodAsync()
        {
            string type = TypeCode(PeriodType);
            Period = DefaultPeriodText(type);
            ComparePeriod = DefaultComparePeriod(type, Period);
            await RunAsync(async () => { await LoadReportAsync(); }, "已回到本期（" + Period + "）");
        }

        // ==================== CHG-v1.3.1-05：报表与导出留痕（缓存清理） ====================

        /// <summary>
        /// 打开清单：列出报表留痕 / 导出留痕 / 未被任何留痕引用的孤立生成文件。
        /// 口径（负责人 2026-09-23）：这里删的是**导出缓存**（留痕软删 + 服务端文件物理删除），
        /// 与账单、收款、财务报表金额没有任何关系；删除前弹窗提醒先备份归档。
        /// </summary>
        private async Task OpenTracePanelAsync()
        {
            IsTracePanelVisible = true;
            await LoadTracesAsync();
        }

        private async Task LoadTracesAsync()
        {
            await RunAsync(async () =>
            {
                List<ExportTraceDto> traces = await Api.ListExportTracesAsync() ?? new List<ExportTraceDto>();
                ExportTraces.Clear();
                foreach (ExportTraceDto dto in traces)
                {
                    var row = new ExportTraceRow { Dto = dto };
                    row.PropertyChanged += OnTraceRowPropertyChanged;
                    ExportTraces.Add(row);
                }
                _isTraceSelectAll = false;
                OnPropertyChanged(nameof(IsTraceSelectAll));
                long totalBytes = traces.Sum(x => x.FileSize);
                TraceSummaryText = "共 " + traces.Count + " 条 · 占用 " + SizeText(totalBytes)
                    + "（其中孤立文件 " + traces.Count(x => x.IsOrphan) + " 个）";
            }, "留痕清单已加载");
        }

        private static string SizeText(long bytes)
        {
            if (bytes >= 1024 * 1024) { return (bytes / 1024.0 / 1024.0).ToString("0.00") + " MB"; }
            if (bytes >= 1024) { return (bytes / 1024.0).ToString("0.0") + " KB"; }
            return bytes + " B";
        }

        private void RequestDeleteTraces()
        {
            var picked = ExportTraces.Where(x => x.IsChecked).ToList();
            if (picked.Count == 0)
            {
                ErrorText = "请先勾选要清理的报表/导出留痕（可用表头「全选」）";
                return;
            }
            ErrorText = string.Empty;
            int files = picked.Count(x => x.Dto.HasFile);
            TraceConfirmText =
                "将清理 " + picked.Count + " 条留痕（其中服务端生成文件 " + files + " 个）：\n" +
                "· 删除动作 = 留痕软删 + 服务端生成文件物理删除（释放磁盘）；\n" +
                "· 只清理**导出缓存**，账单、收款、退款、财务报表金额均不受影响；\n" +
                "· 建议先把还需要留档的报表下载/归档到本机后再清理。\n\n" +
                "确认继续？";
            IsTraceConfirmVisible = true;
        }

        private async Task ConfirmDeleteTracesAsync()
        {
            var picked = ExportTraces.Where(x => x.IsChecked).ToList();
            IsTraceConfirmVisible = false;
            if (picked.Count == 0) { return; }
            ExportTraceDeleteResultDto result = null;
            await RunAsync(async () =>
            {
                result = await Api.DeleteExportTracesAsync(new ExportTraceDeleteRequest
                {
                    Items = picked.Select(x => new ExportTraceKey
                    {
                        Source = x.Dto.Source,
                        Id = x.Dto.Id,
                        FileName = x.Dto.FileName
                    }).ToList()
                });
                await LoadTracesQuietAsync();
            }, null);

            string message = result == null || string.IsNullOrWhiteSpace(result.Message)
                ? "已清理 " + picked.Count + " 条留痕"
                : result.Message;
            StatusText = DateTime.Now.ToString("HH:mm:ss ") + message;
            if (result != null && result.SkippedItems != null && result.SkippedItems.Count > 0)
            {
                ErrorText = string.Join("；", result.SkippedItems);
            }
        }

        /// <summary>删除后静默刷新清单（不覆盖状态栏里的清理结果提示）。</summary>
        private async Task LoadTracesQuietAsync()
        {
            List<ExportTraceDto> traces = await Api.ListExportTracesAsync() ?? new List<ExportTraceDto>();
            ExportTraces.Clear();
            foreach (ExportTraceDto dto in traces)
            {
                var row = new ExportTraceRow { Dto = dto };
                row.PropertyChanged += OnTraceRowPropertyChanged;
                ExportTraces.Add(row);
            }
            _isTraceSelectAll = false;
            OnPropertyChanged(nameof(IsTraceSelectAll));
            TraceSummaryText = "共 " + traces.Count + " 条 · 占用 " + SizeText(traces.Sum(x => x.FileSize))
                + "（其中孤立文件 " + traces.Count(x => x.IsOrphan) + " 个）";
        }

        /// <summary>全选状态与行勾选同步（与其它页面批量删除口径一致）。</summary>
        private void OnTraceRowPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(ExportTraceRow.IsChecked), StringComparison.Ordinal)) { return; }
            bool all = ExportTraces.Count > 0 && ExportTraces.All(x => x.IsChecked);
            SetProperty(ref _isTraceSelectAll, all, nameof(IsTraceSelectAll));
        }

        /// <summary>报表类型码（T4R-7：0 月报 / 1 季报 / 2 年报）。</summary>
        private static string TypeCode(int periodType)
        {
            switch (periodType)
            {
                case 1: return "quarter";
                case 2: return "year";
                default: return "month";
            }
        }

        private static bool IsMonthText(string p)
        {
            if (string.IsNullOrEmpty(p) || p.Length != 7 || p[4] != '-') { return false; }
            int y;
            int m;
            return int.TryParse(p.Substring(0, 4), out y) &&
                   int.TryParse(p.Substring(5, 2), out m) && m >= 1 && m <= 12;
        }

        private static bool IsQuarterText(string p)
        {
            if (string.IsNullOrEmpty(p) || p.Length != 7 || p[4] != '-' || (p[5] != 'Q' && p[5] != 'q')) { return false; }
            int q;
            return int.TryParse(p.Substring(6, 1), out q) && q >= 1 && q <= 4;
        }

        private static bool IsYearText(string p)
        {
            if (string.IsNullOrEmpty(p) || p.Length != 4) { return false; }
            int y;
            return int.TryParse(p, out y) && y >= 1900 && y <= 2200;
        }

        private static string DefaultPeriodText(string periodType)
        {
            if (string.Equals(periodType, "quarter", StringComparison.OrdinalIgnoreCase))
            {
                int q = ((DateTime.Now.Month - 1) / 3) + 1;
                return DateTime.Now.Year + "-Q" + q;
            }
            if (string.Equals(periodType, "year", StringComparison.OrdinalIgnoreCase))
            {
                return DateTime.Now.Year.ToString();
            }
            return DateTime.Now.ToString("yyyy-MM");
        }

        /// <summary>默认对比期间 = 上一期（月报→上月、季报→上季、年报→上年）。</summary>
        private static string DefaultComparePeriod(string periodType, string period)
        {
            try
            {
                if (string.Equals(periodType, "year", StringComparison.OrdinalIgnoreCase) && IsYearText(period))
                {
                    return (int.Parse(period) - 1).ToString();
                }
                if (string.Equals(periodType, "quarter", StringComparison.OrdinalIgnoreCase) && IsQuarterText(period))
                {
                    int year = int.Parse(period.Substring(0, 4));
                    int quarter = int.Parse(period.Substring(6, 1));
                    int prev = quarter == 1 ? 4 : quarter - 1;
                    int prevYear = quarter == 1 ? year - 1 : year;
                    return prevYear + "-Q" + prev;
                }
                if (IsMonthText(period))
                {
                    int year = int.Parse(period.Substring(0, 4));
                    int month = int.Parse(period.Substring(5, 2));
                    DateTime first = new DateTime(year, month, 1).AddMonths(-1);
                    return first.ToString("yyyy-MM");
                }
            }
            catch (Exception)
            {
                // 解析失败返回空，服务端按上一期兜底
            }
            return string.Empty;
        }

        private static string PrevPeriodText(string periodType, string period)
        {
            string def = DefaultComparePeriod(periodType, period);
            if (!string.IsNullOrEmpty(def)) { return def; }
            if (string.Equals(periodType, "quarter", StringComparison.OrdinalIgnoreCase)) { return "上季"; }
            if (string.Equals(periodType, "year", StringComparison.OrdinalIgnoreCase)) { return "上年"; }
            return "上月";
        }

        private async Task ExportAsync(ExportFormat format)
        {
            await RunAsync(async () =>
            {
                // v1.1.0 第 7 轮：服务端生成报表文件（Excel=ClosedXML / PDF=PDFsharp）后，
                // 由客户端选择保存位置并下载到本机，导出留痕仍写 t_report_log（UC-FIN-010）。
                string type = format == ExportFormat.Excel ? "Excel" : "PDF";
                ReportLogDto log = await Api.ExportReportAsync(new ReportExportRequest
                {
                    Query = BuildQuery(),
                    Format = format
                });
                if (log == null || log.Id <= 0)
                {
                    throw new InvalidOperationException(type + " 导出失败：服务端未生成导出记录");
                }

                string ext = format == ExportFormat.Excel ? ".xlsx" : ".pdf";
                string suggested = "财务报表_" + (Period == null ? string.Empty : Period.Trim().Replace("-", "")) + ext;
                var dialog = new SaveFileDialog
                {
                    Title = "保存" + type + "报表",
                    Filter = format == ExportFormat.Excel ? "Excel 文件|*.xlsx" : "PDF 文件|*.pdf",
                    FileName = suggested
                };
                if (dialog.ShowDialog() != true)
                {
                    StatusText = DateTime.Now.ToString("HH:mm:ss ") + type + " 报表已在服务端生成（导出日志 " + log.Id + "），未另存到本机";
                    return;
                }

                await Api.DownloadReportFileAsync(log.Id, dialog.FileName);
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + type + " 已导出：" + dialog.FileName;
                MessageBox.Show(type + " 报表已导出到：" + dialog.FileName, "导出成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }, null);
        }
    }
}
