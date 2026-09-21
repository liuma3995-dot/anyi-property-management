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

        private static readonly string[] BizTypes = { string.Empty, "payment", "expense", "refund" };

        public LedgerViewModel(IApiClient api) : base(api)
        {
            SearchCommand = new AsyncRelayCommand(LoadAsync);
            CloseDetailCommand = new RelayCommand(() => IsDetailVisible = false);
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

        public IAsyncRelayCommand SearchCommand { get; }

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

        public void OpenDetail(LedgerRow row)
        {
            if (row == null) { return; }
            SelectedRow = row;
            IsDetailVisible = true;
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
