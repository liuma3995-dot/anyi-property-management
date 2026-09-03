using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        public FinancialReportViewModel(IApiClient api) : base(api)
        {
            GenerateCommand = new AsyncRelayCommand(GenerateAsync);
            ExportExcelCommand = new AsyncRelayCommand(() => ExportAsync(ExportFormat.Excel));
            ExportPdfCommand = new AsyncRelayCommand(() => ExportAsync(ExportFormat.Pdf));
            _comparePeriod = DefaultComparePeriod("month", _period);
            _ = LoadChargeItemsAsync();
            _ = LoadAsync();
        }

        public ObservableCollection<FinancialSummaryRow> SummaryRows { get; } = new ObservableCollection<FinancialSummaryRow>();

        public ObservableCollection<ChargeItemDto> ChargeItems { get; } = new ObservableCollection<ChargeItemDto>();

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
            }
        }

        /// <summary>T4R-7：是否年报（用于表头“本年累计”与提示文案）。</summary>
        public bool IsAnnualReport { get { return PeriodType == 2; } }

        public string PeriodCumulativeLabel { get { return PeriodType == 2 ? "本年累计" : "本季累计"; } }

        public string Period { get { return _period; } set { SetProperty(ref _period, value); } }

        /// <summary>对比期间（T4F-8-1：2026-07 或 2026-Q2，默认上一期）。</summary>
        public string ComparePeriod { get { return _comparePeriod; } set { SetProperty(ref _comparePeriod, value); } }

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

            IncomeText = "¥" + report.IncomeTotal.ToString("N0");
            ExpenseText = "¥" + report.ExpenseTotal.ToString("N0");
            BalanceText = "¥" + report.Balance.ToString("N0");
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
                UnpaidText = "¥" + stats.UnpaidAmount.ToString("N0");
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
                var log = await Api.ExportReportAsync(new ReportExportRequest
                {
                    Query = BuildQuery(),
                    Format = format
                });
                string type = format == ExportFormat.Excel ? "Excel" : "PDF";
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + type + " 导出成功："
                    + (string.IsNullOrEmpty(log.FilePath) ? "（导出日志 " + log.Id + "）" : log.FilePath);
            }, null);
        }
    }
}
