using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>收款登记行（PG-FIN-03 应缴明细，7 列 + 状态标签）。</summary>
    public class PaymentBillRow : ObservableObject
    {
        public BillListItemDto Dto { get; set; }

        public string NoText { get { return "BILL-" + Dto.Id.ToString("D4"); } }

        public decimal UnpaidAmount { get { return Dto.Amount - Dto.PaidAmount; } }

        public string StatusText
        {
            get
            {
                switch (Dto.Status)
                {
                    case BillStatus.Paid: return "已结清";
                    case BillStatus.Partial: return "部分缴";
                    case BillStatus.Overdue: return "逾期";
                    case BillStatus.Draft: return "草稿";
                    default: return "未缴";
                }
            }
        }

        public Brush StatusBrush
        {
            get
            {
                switch (Dto.Status)
                {
                    case BillStatus.Paid: return Br("#12805C");
                    case BillStatus.Overdue: return Br("#D64545");
                    case BillStatus.Draft: return Br("#98A2B3");
                    default: return Br("#2B7DE9");
                }
            }
        }

        public Brush StatusBg
        {
            get
            {
                switch (Dto.Status)
                {
                    case BillStatus.Paid: return Br("#E8F7F1");
                    case BillStatus.Overdue: return Br("#FDECEC");
                    case BillStatus.Draft: return Br("#F1F3F7");
                    default: return Br("#EAF3FF");
                }
            }
        }

        private static Brush Br(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    /// <summary>缴费房产选项（PG-FIN-03 左表单，T4F-3-1）。</summary>
    public class PropertyPaymentOption
    {
        public int PropertyId { get; set; }

        public string PropertyNo { get; set; }

        public string OwnerName { get; set; }

        public int DueCount { get; set; }

        public string DisplayText
        {
            get { return PropertyNo + " · " + OwnerName + " · 欠费 " + DueCount + " 笔"; }
        }
    }

    /// <summary>收款登记页（PG-FIN-03，UC-FIN-003/011，P-06，BR-FIN-02/08；T4F-3-1 三合计/经手人/收款日期/超额提示/HP-58 收据/确认弹窗）。</summary>
    public class PaymentEntryViewModel : FinancePageViewModel
    {
        private PropertyPaymentOption _selectedProperty;
        private PaymentBillRow _selectedBill;
        private decimal _payAmount;
        private int _payMethod;
        private DateTime _payDate;
        private bool _printReceipt = true;
        private ReceiptDto _receipt;
        private string _receiptNo = "—";
        private string _printCountText = "—";
        private string _preDepositText = "—";
        private string _preDepositHintText = string.Empty;
        private string _receivableTotalText = "¥0.00";
        private string _paidTotalText = "¥0.00";
        private string _unpaidTotalText = "¥0.00";
        private string _receiptPayeeText = "—";
        private string _receiptItemText = "—";
        private string _receiptMethodText = "现金";
        private string _receiptUnitText = "澜庭物业服务中心";
        private string _receiptHandlerText;
        private string _receiptAmountCapitalText = "零元整";
        private string _receiptAmountLowerText = "¥0.00";
        private bool _isConfirmVisible;
        private string _confirmText = string.Empty;
        private string _propertySearchKeyword = string.Empty;
        private string _remark = string.Empty;
        private readonly System.Collections.Generic.List<PropertyPaymentOption> _allProperties = new System.Collections.Generic.List<PropertyPaymentOption>();

        public PaymentEntryViewModel(IApiClient api) : base(api)
        {
            _payDate = DateTime.Today;
            var session = SessionManager.Instance.Current;
            _receiptHandlerText = session == null ? "系统管理员" : session.DisplayName;
            ConfirmCommand = new RelayCommand(RequestConfirm);
            ConfirmPaymentCommand = new AsyncRelayCommand(ConfirmPaymentAsync);
            CancelConfirmCommand = new RelayCommand(() => IsConfirmVisible = false);
            PrintCommand = new AsyncRelayCommand(PrintAsync);
            _ = LoadAsync();
        }

        public ObservableCollection<PropertyPaymentOption> Properties { get; } = new ObservableCollection<PropertyPaymentOption>();

        public ObservableCollection<PaymentBillRow> Bills { get; } = new ObservableCollection<PaymentBillRow>();

        public string PropertySearchKeyword
        {
            get { return _propertySearchKeyword; }
            set
            {
                if (SetProperty(ref _propertySearchKeyword, value))
                {
                    ApplyPropertyFilter();
                }
            }
        }

        public string Remark
        {
            get { return _remark; }
            set { SetProperty(ref _remark, value); }
        }

        public PropertyPaymentOption SelectedProperty
        {
            get { return _selectedProperty; }
            set
            {
                if (SetProperty(ref _selectedProperty, value))
                {
                    OnPropertyChanged(nameof(BillsTitleText));
                    _ = LoadPropertyBillsAsync(value);
                }
            }
        }

        /// <summary>应缴明细区块标题（原型：应缴明细（1栋1单元102））。</summary>
        public string BillsTitleText
        {
            get { return "应缴明细（" + (_selectedProperty == null ? "—" : _selectedProperty.PropertyNo) + "）"; }
        }

        public PaymentBillRow SelectedBill
        {
            get { return _selectedBill; }
            set
            {
                if (SetProperty(ref _selectedBill, value))
                {
                    OnBillSelected();
                }
            }
        }

        public decimal PayAmount
        {
            get { return _payAmount; }
            set
            {
                if (SetProperty(ref _payAmount, value))
                {
                    UpdatePreDepositHint();
                    UpdateReceiptPreview();
                }
            }
        }

        public int PayMethod
        {
            get { return _payMethod; }
            set
            {
                if (SetProperty(ref _payMethod, value))
                {
                    OnPropertyChanged(nameof(PayMethodText));
                    UpdateReceiptPreview();
                }
            }
        }

        public DateTime PayDate
        {
            get { return _payDate; }
            set { SetProperty(ref _payDate, value); }
        }

        public bool PrintReceipt { get { return _printReceipt; } set { SetProperty(ref _printReceipt, value); } }

        public bool HasReceipt { get { return _receipt != null; } }

        public string PayMethodText
        {
            get
            {
                switch (PayMethod)
                {
                    case 0: return "现金";
                    case 2: return "微信";
                    case 3: return "银行转账";
                    case 4: return "POS";
                    default: return "转账";
                }
            }
        }

        public string ReceivableTotalText { get { return _receivableTotalText; } private set { SetProperty(ref _receivableTotalText, value); } }

        public string PaidTotalText { get { return _paidTotalText; } private set { SetProperty(ref _paidTotalText, value); } }

        public string UnpaidTotalText { get { return _unpaidTotalText; } private set { SetProperty(ref _unpaidTotalText, value); } }

        public string PreDepositHintText { get { return _preDepositHintText; } private set { SetProperty(ref _preDepositHintText, value); } }

        public string ReceiptNo { get { return _receiptNo; } private set { SetProperty(ref _receiptNo, value); } }

        public string PrintCountText { get { return _printCountText; } private set { SetProperty(ref _printCountText, value); } }

        public string PreDepositText { get { return _preDepositText; } private set { SetProperty(ref _preDepositText, value); } }

        public string ReceiptPayeeText { get { return _receiptPayeeText; } private set { SetProperty(ref _receiptPayeeText, value); } }

        public string ReceiptItemText { get { return _receiptItemText; } private set { SetProperty(ref _receiptItemText, value); } }

        public string ReceiptMethodText { get { return _receiptMethodText; } private set { SetProperty(ref _receiptMethodText, value); } }

        public string ReceiptUnitText { get { return _receiptUnitText; } private set { SetProperty(ref _receiptUnitText, value); } }

        public string ReceiptHandlerText { get { return _receiptHandlerText; } private set { SetProperty(ref _receiptHandlerText, value); } }

        public string ReceiptAmountCapitalText { get { return _receiptAmountCapitalText; } private set { SetProperty(ref _receiptAmountCapitalText, value); } }

        public string ReceiptAmountLowerText { get { return _receiptAmountLowerText; } private set { SetProperty(ref _receiptAmountLowerText, value); } }

        public bool IsConfirmVisible
        {
            get { return _isConfirmVisible; }
            private set { SetProperty(ref _isConfirmVisible, value); }
        }

        public string ConfirmText
        {
            get { return _confirmText; }
            private set { SetProperty(ref _confirmText, value); }
        }

        public IRelayCommand ConfirmCommand { get; }

        public IAsyncRelayCommand ConfirmPaymentCommand { get; }

        public IRelayCommand CancelConfirmCommand { get; }

        public IAsyncRelayCommand PrintCommand { get; }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                await ReloadPropertiesAsync();
                if (Properties.Count == 0)
                {
                    ReceivableTotalText = PaidTotalText = UnpaidTotalText = "¥0.00";
                }
            }, "待缴账单已加载");
        }

        /// <summary>重建待缴房产下拉（收款后同步欠费笔数/移除已结清房产，避免下拉框显示状态滞后）。</summary>
        private async Task ReloadPropertiesAsync()
        {
            _allProperties.Clear();
            var page = await Api.QueryBillsAsync(new BillQueryRequest { ArrearsOnly = true, PageSize = 1000 });
            var groups = page.Items
                .Where(x => x.Amount > x.PaidAmount && x.PropertyId.HasValue)
                .GroupBy(x => x.PropertyId.Value);
            foreach (var g in groups.OrderBy(g => g.First().PropertyNo))
            {
                var first = g.First();
                _allProperties.Add(new PropertyPaymentOption
                {
                    PropertyId = g.Key,
                    PropertyNo = first.PropertyNo ?? ("房产-" + g.Key),
                    OwnerName = first.OwnerName ?? "",
                    DueCount = g.Count()
                });
            }
            ApplyPropertyFilter();
        }

        private void ApplyPropertyFilter()
        {
            string kw = (PropertySearchKeyword ?? string.Empty).Trim();
            var source = _allProperties.AsEnumerable();
            if (!string.IsNullOrEmpty(kw))
            {
                source = source.Where(x =>
                    (x.PropertyNo ?? string.Empty).Contains(kw) ||
                    (x.OwnerName ?? string.Empty).Contains(kw));
            }
            var matches = source.OrderBy(x => x.PropertyNo).ToList();
            PropertyPaymentOption keep = SelectedProperty != null && matches.Any(x => x.PropertyId == SelectedProperty.PropertyId)
                ? matches.First(x => x.PropertyId == SelectedProperty.PropertyId)
                : null;

            Properties.Clear();
            foreach (var p in matches)
            {
                Properties.Add(p);
            }

            if (keep != null)
            {
                SelectedProperty = keep;
            }
            else if (Properties.Count > 0)
            {
                SelectedProperty = Properties[0];
            }
            else
            {
                SelectedProperty = null;
                Bills.Clear();
                ReceivableTotalText = PaidTotalText = UnpaidTotalText = "¥0.00";
            }
        }

        private async Task LoadPropertyBillsAsync(PropertyPaymentOption option)
        {
            if (option == null) { return; }
            await RunAsync(async () =>
            {
                Bills.Clear();
                var page = await Api.QueryBillsAsync(new BillQueryRequest { PropertyId = option.PropertyId, PageSize = 200 });
                foreach (var dto in page.Items.OrderBy(x => x.DueAt).ThenBy(x => x.Id))
                {
                    Bills.Add(new PaymentBillRow { Dto = dto });
                }
                ReceivableTotalText = "¥" + Bills.Sum(x => x.Dto.Amount).ToString("N2");
                PaidTotalText = "¥" + Bills.Sum(x => x.Dto.PaidAmount).ToString("N2");
                UnpaidTotalText = "¥" + Bills.Sum(x => x.UnpaidAmount).ToString("N2");
                // 部分缴默认冲抵最早账单（T4F-3-1）
                var earliest = Bills.FirstOrDefault(x => x.Dto.Amount > x.Dto.PaidAmount && x.Dto.Status != BillStatus.Draft);
                SelectedBill = earliest;
                if (earliest == null)
                {
                    PayAmount = 0m;
                    PreDepositHintText = string.Empty;
                }
            }, "账单已加载");
        }

        private void OnBillSelected()
        {
            if (_selectedBill == null)
            {
                PayAmount = 0m;
                return;
            }
            PayAmount = _selectedBill.UnpaidAmount;
        }

        private void UpdatePreDepositHint()
        {
            if (_selectedBill == null)
            {
                PreDepositHintText = string.Empty;
                return;
            }
            if (PayAmount > _selectedBill.UnpaidAmount)
            {
                var excess = PayAmount - _selectedBill.UnpaidAmount;
                PreDepositHintText = "多缴 ¥" + excess.ToString("0.00") + " 自动转入预存账户";
            }
            else
            {
                PreDepositHintText = string.Empty;
            }
        }

        private void UpdateReceiptPreview()
        {
            if (_selectedBill == null)
            {
                ReceiptPayeeText = "—";
                ReceiptItemText = "—";
                ReceiptAmountLowerText = "¥0.00";
                ReceiptAmountCapitalText = "零元整";
                return;
            }
            ReceiptPayeeText = _selectedBill.Dto.OwnerName;
            ReceiptItemText = _selectedBill.Dto.ChargeItemName + " " + _selectedBill.Dto.CyclePeriod;
            ReceiptMethodText = PayMethodText;
            ReceiptUnitText = "澜庭物业服务中心";
            ReceiptHandlerText = _receiptHandlerText;
            ReceiptAmountLowerText = "¥" + PayAmount.ToString("N2");
            ReceiptAmountCapitalText = ToChineseCapital(PayAmount);
        }

        private void RequestConfirm()
        {
            if (_selectedBill == null)
            {
                ErrorText = "请先选择待缴账单";
                return;
            }
            if (PayAmount <= 0)
            {
                ErrorText = "收款金额必须大于 0";
                return;
            }
            ConfirmText = "金额 ¥" + PayAmount.ToString("0.00") + "，方式：" + PayMethodText + "，确认入账？";
            IsConfirmVisible = true;
        }

        private async Task ConfirmPaymentAsync()
        {
            PaymentBillRow bill = _selectedBill;
            decimal amount = PayAmount;
            bool print = PrintReceipt;
            IsConfirmVisible = false;
            if (bill == null || amount <= 0) { return; }

            PaymentDto payment = null;
            await RunAsync(async () =>
            {
                payment = await Api.CreatePaymentAsync(new PaymentCreateRequest
                {
                    BillId = bill.Dto.Id,
                    Amount = amount,
                    PayMethod = (PayMethod)PayMethod,
                    PrintReceipt = print,
                    Remark = (Remark ?? string.Empty).Trim()
                });

                await ReloadPropertiesAsync();
                Remark = string.Empty;
                if (print && payment != null)
                {
                    var receipt = await Api.GetReceiptByPaymentAsync(payment.Id);
                    ApplyReceipt(receipt, bill, amount);
                }
            }, null);

            if (payment != null)
            {
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "收款已入账" +
                    (payment.ToPreDeposit > 0
                        ? "，超额 ¥" + payment.ToPreDeposit.ToString("0.00") + " 已转预存（P-06）"
                        : "");
            }
        }

        private async Task PrintAsync()
        {
            if (_receipt == null) { return; }
            await RunAsync(async () =>
            {
                ReceiptDto printed = await Api.PrintReceiptAsync(_receipt.Id);
                ApplyReceipt(printed, _selectedBill, PayAmount);
            }, "收据已打印（补打保留原号，BR-FIN-08）");
        }

        private void ApplyReceipt(ReceiptDto receipt, PaymentBillRow bill, decimal amount)
        {
            _receipt = receipt;
            ReceiptNo = receipt.ReceiptNo;
            PrintCountText = "已打印 " + receipt.PrintCount + " 次";
            PreDepositText = "查询业主预存余额（P-06）";
            ReceiptPayeeText = bill == null ? "—" : bill.Dto.OwnerName;
            ReceiptItemText = bill == null ? "—" : bill.Dto.ChargeItemName + " " + bill.Dto.CyclePeriod +
                (amount < bill.UnpaidAmount ? "（部分缴）" : "");
            ReceiptMethodText = PayMethodText;
            ReceiptUnitText = "澜庭物业服务中心";
            ReceiptHandlerText = _receiptHandlerText;
            ReceiptAmountLowerText = "¥" + amount.ToString("N2");
            ReceiptAmountCapitalText = ToChineseCapital(amount);
            OnPropertyChanged(nameof(HasReceipt));
        }

        /// <summary>金额转中文大写（HP-58 收据字段，T4F-3-1）。</summary>
        public static string ToChineseCapital(decimal amount)
        {
            if (amount <= 0) { return "零元整"; }
            string[] digits = { "零", "壹", "贰", "叁", "肆", "伍", "陆", "柒", "捌", "玖" };
            string[] intUnits = { "", "拾", "佰", "仟", "万", "拾", "佰", "仟", "亿", "拾", "佰", "仟", "万亿" };

            long yuan = (long)Math.Floor(amount);
            int jiao = (int)((long)Math.Floor(amount * 10m) % 10);
            int fen = (int)((long)Math.Floor(amount * 100m) % 10);

            var sb = new StringBuilder();
            if (yuan == 0)
            {
                sb.Append("零");
            }
            else
            {
                string s = yuan.ToString();
                int len = s.Length;
                bool zero = false;
                for (int i = 0; i < len; i++)
                {
                    int digit = s[i] - '0';
                    int pos = len - i - 1;
                    if (digit == 0)
                    {
                        if (pos == 4 || pos == 8 || pos == 12)
                        {
                            sb.Append(intUnits[pos]);
                        }
                        else
                        {
                            zero = true;
                        }
                    }
                    else
                    {
                        if (zero)
                        {
                            sb.Append("零");
                            zero = false;
                        }
                        sb.Append(digits[digit]);
                        sb.Append(intUnits[pos]);
                    }
                }
            }
            sb.Append("元");

            if (jiao == 0 && fen == 0)
            {
                sb.Append("整");
            }
            else
            {
                if (jiao > 0)
                {
                    sb.Append(digits[jiao]).Append("角");
                }
                else if (fen > 0)
                {
                    sb.Append("零");
                }
                if (fen > 0)
                {
                    sb.Append(digits[fen]).Append("分");
                }
            }
            return sb.ToString();
        }
    }
}
