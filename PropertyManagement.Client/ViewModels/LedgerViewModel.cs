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
    /// <summary>收支明细流水行（PG-FIN-08，UC-FIN-010：流水只读、错账红冲）。</summary>
    public class LedgerRow : ObservableObject
    {
        public LedgerEntryDto Dto { get; set; }

        public string NoText { get { return "LS-" + Dto.Id.ToString("D4"); } }

        public string DateText { get { return Dto.BizTime.ToString("MM-dd HH:mm"); } }

        public string TypeText { get { return Dto.InAmount > 0 ? "收" : "支"; } }

        public Brush TypeBrush { get { return Dto.InAmount > 0 ? OkBrush : DangerBrush; } }

        public Brush TypeBg { get { return Dto.InAmount > 0 ? OkBg : DangerBg; } }

        /// <summary>科目（T4F-7-1：收款→收费项目名、支出→支出分类名、退款→冲减）。</summary>
        public string Category { get { return string.IsNullOrEmpty(Dto.Subject) ? "—" : Dto.Subject; } }

        public string AmountText
        {
            get
            {
                if (Dto.InAmount > 0) { return "+¥" + Dto.InAmount.ToString("N2"); }
                if (Dto.InAmount < 0) { return "-¥" + (-Dto.InAmount).ToString("N2"); } // 红冲负数
                return "-¥" + Dto.OutAmount.ToString("N2");
            }
        }

        /// <summary>负数红字展示（错账冲正显示为负数记录，UC-FIN-010）。</summary>
        public Brush AmountBrush { get { return Dto.InAmount > 0 ? OkBrush : DangerBrush; } }

        public string BizNo { get { return string.IsNullOrEmpty(Dto.BizNo) ? "—" : Dto.BizNo; } }

        public string PayMethodText { get { return string.IsNullOrEmpty(Dto.PayMethod) ? "—" : Dto.PayMethod; } }

        public string OperatorName { get { return string.IsNullOrEmpty(Dto.OperatorName) ? "—" : Dto.OperatorName; } }

        public string OwnerNameText { get { return string.IsNullOrEmpty(Dto.OwnerName) ? "—" : Dto.OwnerName; } }

        /// <summary>CHG-v1.1.2-05：楼栋/房号（车位显示车位编号，支出行为空）。</summary>
        public string ObjectText { get { return string.IsNullOrEmpty(Dto.ObjectText) ? "—" : Dto.ObjectText; } }

        public string BizTypeText
        {
            get
            {
                switch (Dto.BizType)
                {
                    case "payment": return "收款";
                    case "refund": return "退款";
                    case "expense": return "支出";
                    case "reversed": return "红冲";
                    default: return Dto.BizType ?? "—";
                }
            }
        }

        private static readonly Brush OkBrush = Br("#12805C");
        private static readonly Brush OkBg = Br("#E8F7F1");
        private static readonly Brush DangerBrush = Br("#D64545");
        private static readonly Brush DangerBg = Br("#FDECEC");

        private static Brush Br(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    /// <summary>收支明细流水页（PG-FIN-08，UC-FIN-010/012：只读查询 + 科目/单据号筛选 + 行点击详情）。</summary>
    public class LedgerViewModel : FinancePageViewModel
    {
        private DateTime? _from;
        private DateTime? _to;
        private int _bizTypeFilter;
        private int _subjectFilter;
        private string _keyword = string.Empty;
        private string _totalText = "0 条";
        private LedgerRow _selectedRow;
        private bool _isDetailVisible;
        private LedgerEntryDetailDto _detail;

        private static readonly string[] BizTypes = { string.Empty, "payment", "expense", "refund" };

        public LedgerViewModel(IApiClient api) : base(api)
        {
            SearchCommand = new AsyncRelayCommand(LoadAsync);
            ViewCommand = new AsyncRelayCommand<LedgerRow>(OpenDetailAsync);
            CloseDetailCommand = new RelayCommand(() =>
            {
                IsDetailVisible = false;
                Detail = null;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "流水明细已关闭";
            });
            ExportExcelCommand = new AsyncRelayCommand(() => ExportAsync(ExportFormat.Excel));
            ExportPdfCommand = new AsyncRelayCommand(() => ExportAsync(ExportFormat.Pdf));
            _ = LoadSubjectsAsync();
            _ = LoadAsync();
        }

        public ObservableCollection<LedgerRow> Items { get; } = new ObservableCollection<LedgerRow>();

        public ObservableCollection<string> Subjects { get; } = new ObservableCollection<string> { "全部" };

        public DateTime? From { get { return _from; } set { SetProperty(ref _from, value); } }

        public DateTime? To { get { return _to; } set { SetProperty(ref _to, value); } }

        /// <summary>0=全部 1=收款 2=支出 3=退款（原型 PG-FIN-08 类型筛选）。</summary>
        public int BizTypeFilter { get { return _bizTypeFilter; } set { SetProperty(ref _bizTypeFilter, value); } }

        /// <summary>科目下拉（0=全部，其余为科目字典；T4F-7-1）。</summary>
        public int SubjectFilter { get { return _subjectFilter; } set { SetProperty(ref _subjectFilter, value); } }

        /// <summary>单据号搜索（T4F-7-1）。</summary>
        public string Keyword { get { return _keyword; } set { SetProperty(ref _keyword, value); } }

        public string TotalText { get { return _totalText; } private set { SetProperty(ref _totalText, value); } }

        public LedgerRow SelectedRow { get { return _selectedRow; } set { SetProperty(ref _selectedRow, value); } }

        public bool IsDetailVisible { get { return _isDetailVisible; } set { SetProperty(ref _isDetailVisible, value); } }

        public LedgerEntryDetailDto Detail
        {
            get { return _detail; }
            private set
            {
                if (!SetProperty(ref _detail, value)) { return; }
                OnPropertyChanged(nameof(HasBill));
                OnPropertyChanged(nameof(NoBillVisible));
                OnPropertyChanged(nameof(HasAdjustment));
                OnPropertyChanged(nameof(HasExpense));
                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(DetailBillNo));
                OnPropertyChanged(nameof(DetailChargeItem));
                OnPropertyChanged(nameof(DetailPayer));
                OnPropertyChanged(nameof(DetailObject));
                OnPropertyChanged(nameof(DetailCycle));
                OnPropertyChanged(nameof(DetailAmount));
                OnPropertyChanged(nameof(DetailPaid));
                OnPropertyChanged(nameof(DetailUnpaid));
                OnPropertyChanged(nameof(DetailBillStatus));
                OnPropertyChanged(nameof(DetailDueAt));
                OnPropertyChanged(nameof(DetailAdjustmentType));
                OnPropertyChanged(nameof(DetailAdjustmentDirection));
                OnPropertyChanged(nameof(DetailAdjustmentAmount));
                OnPropertyChanged(nameof(DetailAdjustmentMethod));
                OnPropertyChanged(nameof(DetailAdjustmentReason));
                OnPropertyChanged(nameof(DetailAdjustmentOperator));
                OnPropertyChanged(nameof(DetailExpenseCategory));
                OnPropertyChanged(nameof(DetailExpenseDate));
                OnPropertyChanged(nameof(DetailExpensePayee));
                OnPropertyChanged(nameof(DetailExpenseAmount));
                OnPropertyChanged(nameof(DetailExpenseNote));
                OnPropertyChanged(nameof(DetailNoBillReason));
            }
        }

        public IAsyncRelayCommand SearchCommand { get; }

        public IAsyncRelayCommand<LedgerRow> ViewCommand { get; }

        public IRelayCommand CloseDetailCommand { get; }

        /// <summary>CHG-v1.1.2-05：导出当前筛选条件下的收支明细流水（Excel / PDF）。</summary>
        public IAsyncRelayCommand ExportExcelCommand { get; }
        public IAsyncRelayCommand ExportPdfCommand { get; }

        private async Task ExportAsync(ExportFormat format)
        {
            await RunAsync(async () =>
            {
                string type = format == ExportFormat.Excel ? "Excel" : "PDF";
                string bizType = BizTypeFilter >= 0 && BizTypeFilter < BizTypes.Length ? BizTypes[BizTypeFilter] : string.Empty;
                string subject = SubjectFilter > 0 && SubjectFilter < Subjects.Count ? Subjects[SubjectFilter] : null;

                ReportLogDto log = await Api.ExportLedgerAsync(new LedgerExportRequest
                {
                    Format = format,
                    Query = new LedgerQueryRequest
                    {
                        From = From,
                        To = To,
                        BizType = string.IsNullOrEmpty(bizType) ? null : bizType,
                        Subject = subject,
                        Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim()
                    }
                });
                if (log == null || log.Id <= 0)
                {
                    throw new InvalidOperationException(type + " 导出失败：服务端未生成导出记录");
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "保存收支明细流水（" + type + "）",
                    Filter = format == ExportFormat.Excel ? "Excel 文件|*.xlsx" : "PDF 文件|*.pdf",
                    FileName = "收支明细流水_" + DateTime.Now.ToString("yyyyMMddHHmm") +
                        (format == ExportFormat.Excel ? ".xlsx" : ".pdf")
                };
                if (dialog.ShowDialog() != true)
                {
                    StatusText = DateTime.Now.ToString("HH:mm:ss ") + type + " 流水已在服务端生成（导出日志 " + log.Id + "），未另存到本机";
                    return;
                }

                await Api.DownloadReportFileAsync(log.Id, dialog.FileName);
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + type + " 流水已导出：" + dialog.FileName;
                System.Windows.MessageBox.Show(type + " 流水已导出到：" + dialog.FileName, "导出成功",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }, null);
        }

        public async Task OpenDetailAsync(LedgerRow row)
        {
            if (row == null) { return; }
            SelectedRow = row;
            await RunAsync(async () =>
            {
                Detail = await Api.GetLedgerEntryDetailAsync(row.Dto.BizType, row.Dto.SourceId);
                if (Detail == null)
                {
                    throw new InvalidOperationException("流水不存在或来源单据已删除");
                }
                IsDetailVisible = true;
            }, "流水明细已加载");
        }

        public bool HasBill { get { return Detail != null && Detail.Bill != null; } }
        public bool NoBillVisible { get { return Detail != null && Detail.Bill == null; } }
        public bool HasAdjustment { get { return Detail != null && Detail.Adjustment != null; } }
        public bool HasExpense { get { return Detail != null && Detail.Expense != null; } }

        public string DetailTitle
        {
            get
            {
                if (Detail == null || Detail.Entry == null) { return "流水明细"; }
                switch ((Detail.Entry.BizType ?? string.Empty).ToLowerInvariant())
                {
                    case "payment": return "收款流水与关联账单";
                    case "reversed": return "红冲流水与关联账单";
                    case "refund": return "退款／调整单与关联账单";
                    case "expense": return "支出流水明细";
                    default: return "流水明细";
                }
            }
        }

        public string DetailBillNo { get { return HasBill ? "BILL-" + Detail.Bill.Id.ToString("D4") : "—"; } }
        public string DetailChargeItem { get { return HasBill && !string.IsNullOrWhiteSpace(Detail.Bill.ChargeItemName) ? Detail.Bill.ChargeItemName : "—"; } }
        public string DetailPayer { get { return HasBill ? FirstNonEmpty(Detail.Bill.OwnerName, Detail.Bill.PayerName, Detail.Bill.PropertyNo) : "—"; } }
        public string DetailObject
        {
            get
            {
                if (!HasBill) { return "—"; }
                return FirstNonEmpty(
                    AddressText.Build(Detail.Bill.BuildingNo, Detail.Bill.UnitNo, Detail.Bill.RoomNo),
                    Detail.Bill.SpaceNo,
                    Detail.Bill.PropertyNo);
            }
        }
        public string DetailCycle { get { return HasBill && !string.IsNullOrWhiteSpace(Detail.Bill.CyclePeriod) ? Detail.Bill.CyclePeriod : "—"; } }
        public string DetailAmount { get { return HasBill ? Money(Detail.Bill.Amount) : "—"; } }
        public string DetailPaid { get { return HasBill ? Money(Detail.Bill.PaidAmount) : "—"; } }
        public string DetailUnpaid { get { return HasBill ? Money(Math.Max(0m, Detail.Bill.Amount - Detail.Bill.PaidAmount)) : "—"; } }
        public string DetailBillStatus { get { return HasBill ? BillStatusText(Detail.Bill.Status) : "—"; } }
        public string DetailDueAt { get { return HasBill ? Detail.Bill.DueAt.ToString("yyyy-MM-dd") : "—"; } }

        public string DetailAdjustmentType
        {
            get
            {
                if (!HasAdjustment) { return "—"; }
                switch (Detail.Adjustment.RefundType)
                {
                    case RefundType.Refund: return "退款";
                    case RefundType.Discount: return "费用减免";
                    default: return "财务调整";
                }
            }
        }
        public string DetailAdjustmentDirection
        {
            get
            {
                if (!HasAdjustment) { return "—"; }
                if (Detail.Adjustment.RefundType != RefundType.Adjustment) { return "—"; }
                return Detail.Adjustment.AdjustDir == 2 ? "调减冲正（−）" : "调增补收（＋）";
            }
        }
        public string DetailAdjustmentAmount { get { return HasAdjustment ? Money(Detail.Adjustment.Amount) : "—"; } }
        public string DetailAdjustmentMethod { get { return HasAdjustment ? FirstNonEmpty(Detail.Adjustment.Method, "—") : "—"; } }
        public string DetailAdjustmentReason { get { return HasAdjustment ? FirstNonEmpty(Detail.Adjustment.Reason, "—") : "—"; } }
        public string DetailAdjustmentOperator { get { return HasAdjustment ? FirstNonEmpty(Detail.Adjustment.OperatorName, "—") : "—"; } }

        public string DetailExpenseCategory { get { return HasExpense ? FirstNonEmpty(Detail.Expense.CategoryName, "—") : "—"; } }
        public string DetailExpenseDate { get { return HasExpense ? Detail.Expense.ExpenseDate.ToString("yyyy-MM-dd") : "—"; } }
        public string DetailExpensePayee { get { return HasExpense ? FirstNonEmpty(Detail.Expense.Payee, "—") : "—"; } }
        public string DetailExpenseAmount { get { return HasExpense ? Money(Detail.Expense.Amount) : "—"; } }
        public string DetailExpenseNote { get { return HasExpense ? FirstNonEmpty(Detail.Expense.Note, "—") : "—"; } }
        public string DetailNoBillReason { get { return Detail == null ? string.Empty : (Detail.NoBillReason ?? "该流水无关联账单"); } }

        private static string Money(decimal value)
        {
            return "¥" + value.ToString("N2");
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values ?? new string[0])
            {
                if (!string.IsNullOrWhiteSpace(value)) { return value.Trim(); }
            }
            return "—";
        }

        private static string BillStatusText(BillStatus status)
        {
            switch (status)
            {
                case BillStatus.Pending: return "待缴";
                case BillStatus.Partial: return "部分缴纳";
                case BillStatus.Overdue: return "逾期";
                case BillStatus.Paid: return "已缴";
                case BillStatus.Reversed: return "已冲正";
                case BillStatus.Draft: return "草稿";
                default: return "—";
            }
        }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                Items.Clear();
                string bizType = BizTypeFilter >= 0 && BizTypeFilter < BizTypes.Length ? BizTypes[BizTypeFilter] : string.Empty;
                string subject = SubjectFilter > 0 && SubjectFilter < Subjects.Count ? Subjects[SubjectFilter] : null;
                var page = await Api.GetLedgerAsync(new LedgerQueryRequest
                {
                    PageIndex = 1,
                    PageSize = 200,
                    From = From,
                    To = To,
                    BizType = string.IsNullOrEmpty(bizType) ? null : bizType,
                    Subject = string.IsNullOrWhiteSpace(subject) || subject == "全部" ? null : subject,
                    Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim()
                });
                foreach (var dto in page.Items.OrderByDescending(x => x.BizTime))
                {
                    Items.Add(new LedgerRow { Dto = dto });
                }
                TotalText = "共 " + page.Total + " 条记录";
            }, "流水已加载（流水只读，错账走红冲，不物理删除）");
        }

        /// <summary>科目下拉来源：收费项目名 + 支出分类名 + 冲减/红冲固定项（T4F-7-1）。</summary>
        public async Task LoadSubjectsAsync()
        {
            try
            {
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in await Api.GetChargeItemsAsync() ?? new List<ChargeItemDto>())
                {
                    if (!string.IsNullOrWhiteSpace(item.Name)) { names.Add(item.Name.Trim()); }
                }
                foreach (var cat in await Api.GetExpenseCategoriesAsync() ?? new List<ExpenseCategoryDto>())
                {
                    if (!string.IsNullOrWhiteSpace(cat.Name)) { names.Add(cat.Name.Trim()); }
                }
                names.Add("退款/冲减");
                names.Add("红冲");
                Subjects.Clear();
                Subjects.Add("全部");
                foreach (string name in names)
                {
                    Subjects.Add(name);
                }
                // T4R-8：异步填充完成后显式落位“全部”选中（否则 ComboBox 保持 -1，初次加载下拉显示空白）
                SubjectFilter = 0;
                OnPropertyChanged(nameof(SubjectFilter));
            }
            catch (Exception)
            {
                // 科目下拉失败不影响流水加载
            }
        }
    }
}
