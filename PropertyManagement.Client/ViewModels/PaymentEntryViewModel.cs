using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Auth;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>收款登记行（PG-FIN-03 应缴明细，7 列 + 状态标签）。</summary>
    public class PaymentBillRow : ObservableObject
    {
        private bool _isChecked;
        public BillListItemDto Dto { get; set; }

        /// <summary>CHG-v1.1.0-12：批量选择标记（支持单选/多选/全选统一收款）。</summary>
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }

        public string NoText { get { return "BILL-" + Dto.Id.ToString("D4"); } }

        /// <summary>
        /// CHG-v1.1.0-24：账单期间紧凑显示（同年省略结束年份、去空格）——
        /// 应缴明细「账单期间」列宽有限（含 12px 单元格内边距），原始 `YYYY-MM-DD ~ YYYY-MM-DD` 会被裁切。
        /// </summary>
        public string CyclePeriodText
        {
            get
            {
                string raw = Dto == null || string.IsNullOrWhiteSpace(Dto.CyclePeriod) ? "—" : Dto.CyclePeriod.Trim();
                if (raw == "—") { return raw; }
                string compact = raw.Replace(" ", string.Empty);
                string[] parts = compact.Split('~');
                if (parts.Length == 2 && parts[0].Length >= 10 && parts[1].Length >= 10 &&
                    parts[0].Substring(0, 4) == parts[1].Substring(0, 4))
                {
                    return parts[0] + "~" + parts[1].Substring(5);
                }
                return compact;
            }
        }

        /// <summary>CHG-v1.1.0-24：完整账单期间（悬停提示）。</summary>
        public string CyclePeriodFull
        {
            get { return Dto == null || string.IsNullOrWhiteSpace(Dto.CyclePeriod) ? "—" : Dto.CyclePeriod; }
        }

        /// <summary>
        /// CHG-v1.1.0-13：本行账单对应的缴费对象（应缴明细按业主聚合后，
        /// 表格需逐行标明该欠费属于哪套房产/哪个车位/业主直缴）。
        /// </summary>
        public string ObjectText
        {
            get
            {
                if (Dto == null) { return "—"; }
                if (Dto.OwnerId.HasValue && !Dto.PropertyId.HasValue && !Dto.ParkingId.HasValue) { return "业主直缴"; }
                if (Dto.ParkingId.HasValue) { return "车位 " + (string.IsNullOrEmpty(Dto.SpaceNo) ? "—" : Dto.SpaceNo); }
                string building = string.IsNullOrEmpty(Dto.BuildingNo) ? string.Empty : Dto.BuildingNo + " ";
                string room = string.IsNullOrEmpty(Dto.RoomNo) ? (Dto.PropertyNo ?? "—") : Dto.RoomNo;
                return (building + room).Trim();
            }
        }

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
                    default: return Br("#1F4B43");
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
                    default: return Br("#E9F0EE");
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
        /// <summary>
        /// CHG-v1.1.0-13：收款登记改为**按业主聚合**（一个业主一行，右侧列出其全部欠费项目）。
        /// 该字段为主口径；为空表示账单找不到缴费人（无主对象），退化为按对象分组。
        /// </summary>
        public int? PayerOwnerId { get; set; }

        /// <summary>无缴费人时的兜底对象：0 房产／1 车位／2 业主直缴。</summary>
        public int FallbackObjectKind { get; set; }
        public int FallbackObjectId { get; set; }

        /// <summary>
        /// CHG-v1.1.0-18：自定义缴费对象名称（租户/广告商/外部单位等无档案对象，账单 payer_name）。
        /// 非空时按名称聚合与取应缴明细，不使用房产/车位/业主口径。
        /// </summary>
        public string PayerName { get; set; }

        public string OwnerName { get; set; }

        /// <summary>名下缴费对象摘要：如「1号楼 101、车位 B1-01」；无房产/车位时为「业主直缴」。</summary>
        public string ObjectSummary { get; set; }

        /// <summary>CHG-v1.1.0-14：主房产楼栋 / 房号（下拉固定四段展示：姓名 / 楼栋 / 房号 / 欠费 N 笔）。</summary>
        public string BuildingNo { get; set; }
        public string RoomNo { get; set; }

        /// <summary>
        /// CHG-v1.1.2-53：车位编号。缴费人名下**没有房产**（只有车位，或业主直缴未绑房产）时，
        /// 下拉用「车位 X」定位缴费对象，避免楼栋/房号两段都退化成「—」、同名缴费人无法分辨。
        /// </summary>
        public string SpaceNo { get; set; }
        /// <summary>CHG-v1.1.2-53：名下车位数量（&gt;1 时下拉标注「等 N 个」）。</summary>
        public int SpaceCount { get; set; }

        public int DueCount { get; set; }

        /// <summary>
        /// CHG-v1.1.0-14：固定四段展示「业主姓名 · 楼栋 · 房号 · 欠费 N 笔」（不再列出欠费项目清单）。
        /// </summary>
        public string DisplayText
        {
            get
            {
                string name = string.IsNullOrEmpty(OwnerName) ? "（未绑定业主）" : OwnerName;
                if (!string.IsNullOrEmpty(PayerName))
                {
                    // CHG-v1.1.0-18：自定义缴费对象无楼栋/房号，按「名称 · 自定义缴费对象 · 欠费 N 笔」展示
                    return PayerName + " · 自定义缴费对象 · 欠费 " + DueCount + " 笔";
                }
                if (!string.IsNullOrEmpty(BuildingNo) || !string.IsNullOrEmpty(RoomNo))
                {
                    string building = string.IsNullOrEmpty(BuildingNo) ? "—" : BuildingNo;
                    string room = string.IsNullOrEmpty(RoomNo) ? "—" : RoomNo;
                    return name + " · " + building + " · " + room + " · 欠费 " + DueCount + " 笔";
                }
                // CHG-v1.1.2-53：无房产的缴费人（车主 / 未绑房产的业主）用「车位 X / 业主直缴」兜底，
                // 不再输出「姓名 · — · — · 欠费 N 笔」这种分辨不出缴费对象的下拉项。
                string label = string.IsNullOrEmpty(SpaceNo)
                    ? "业主直缴"
                    : "车位 " + SpaceNo + (SpaceCount > 1 ? " 等 " + SpaceCount + " 个" : string.Empty);
                return name + " · " + label + " · 欠费 " + DueCount + " 笔";
            }
        }
    }

    /// <summary>
    /// 收款登记页（PG-FIN-03，UC-FIN-003/011，BR-FIN-02/08；T4F-3-1 三合计/经手人/收款日期/收据模板/确认弹窗）。
    /// CHG-v1.1.2-51：下线「多缴自动转入预存账户」提示与链路，改为止付提示（收款金额不得超过账单未收金额）。
    /// </summary>
    public class PaymentEntryViewModel : FinancePageViewModel
    {
        private PropertyPaymentOption _selectedProperty;
        private PaymentBillRow _selectedBill;
        private decimal _payAmount;
        private int _payMethod;
        private DateTime _payDate;
        private bool _printReceipt = true;
        private string _amountWarnText = string.Empty;
        private string _receivableTotalText = "¥0.00";
        private string _paidTotalText = "¥0.00";
        private string _unpaidTotalText = "¥0.00";
        private string _receiptPayeeText = "—";
        private string _receiptItemText = "—";
        private string _receiptMethodText = "现金";
        private string _receiptUnitText = "安怡物业服务中心";
        private string _receiptHandlerText;
        private string _receiptAmountCapitalText = "零元整";
        private string _receiptAmountLowerText = "¥0.00";
        private bool _isConfirmVisible;
        private string _confirmText = string.Empty;
        private string _propertySearchKeyword = string.Empty;
        private string _remark = string.Empty;
        /// <summary>CHG-v1.1.0-13：经手人（可自行填写；留空时回落为当前管理员名称）。</summary>
        private string _handlerName = string.Empty;
        /// <summary>收据卡片「收款流水号」（统一收款时显示）。</summary>
        private string _batchNoText = string.Empty;
        /// <summary>CHG-v1.1.0-12：收款后的账单刷新不写状态栏（保留收款结果提示）。</summary>
        private bool _quietBillsReload;
        private readonly System.Collections.Generic.List<PropertyPaymentOption> _allProperties = new System.Collections.Generic.List<PropertyPaymentOption>();

        public PaymentEntryViewModel(IApiClient api) : base(api)
        {
            _payDate = DateTime.Today;
            var session = SessionManager.Instance.Current;
            _receiptHandlerText = session == null ? "系统管理员" : session.DisplayName;
            ConfirmCommand = new RelayCommand(RequestConfirm);
            ConfirmPaymentCommand = new AsyncRelayCommand(ConfirmPaymentAsync);
            CancelConfirmCommand = new RelayCommand(() => IsConfirmVisible = false);
            ExportReceiptTemplateCommand = new AsyncRelayCommand(ExportReceiptTemplateAsync);
            _ = LoadAsync();
            _ = LoadDefaultHandlerAsync();
        }

        /// <summary>
        /// CHG-v1.1.0-14：经手人默认值取「管理员个人设置」里的名字（未设置则回落登录显示名）。
        /// </summary>
        private async Task LoadDefaultHandlerAsync()
        {
            try
            {
                UserProfileDto profile = await Api.GetProfileAsync();
                if (profile != null && !string.IsNullOrWhiteSpace(profile.DisplayName))
                {
                    _receiptHandlerText = profile.DisplayName.Trim();
                    OnPropertyChanged(nameof(EffectiveHandlerName));
                    OnPropertyChanged(nameof(ReceiptHandlerText));
                    OnPropertyChanged(nameof(HandlerPlaceholderText));
                }
            }
            catch (Exception)
            {
                // 取个人信息失败不阻断收款登记（回落登录显示名）
            }
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

        /// <summary>
        /// CHG-v1.1.0-13：经手人文本（用户可填写）。留空时按当前登录管理员名称落款，不阻断收款。
        /// </summary>
        public string HandlerName
        {
            get { return _handlerName; }
            set
            {
                if (SetProperty(ref _handlerName, value))
                {
                    OnPropertyChanged(nameof(EffectiveHandlerName));
                    OnPropertyChanged(nameof(ReceiptHandlerText));
                    UpdateReceiptPreview();
                }
            }
        }

        /// <summary>实际落款经手人：填写值优先，留空回落管理员名。</summary>
        public string EffectiveHandlerName
        {
            get { return string.IsNullOrWhiteSpace(_handlerName) ? _receiptHandlerText : _handlerName.Trim(); }
        }

        /// <summary>经手人输入框占位提示（默认值提示管理员名）。</summary>
        public string HandlerPlaceholderText { get { return "默认：" + _receiptHandlerText + "（可自行填写）"; } }

        /// <summary>收据卡片「收款流水号」（统一收款才有值，单张为空）。</summary>
        public string BatchNoText { get { return _batchNoText; } private set { SetProperty(ref _batchNoText, value); } }

        public bool HasBatchNo { get { return !string.IsNullOrEmpty(_batchNoText); } }

        /// <summary>CHG-v1.1.0-14：最近一次收款的实际明细（供导出打印模板使用，多张账单逐项列出）。</summary>
        private System.Collections.Generic.List<ReceiptTemplateItemRequest> _lastPaidItems
            = new System.Collections.Generic.List<ReceiptTemplateItemRequest>();

        /// <summary>CHG-v1.1.0-16：最近一次收款的收款流水号（导出模板用，不依赖界面刷新时序）。</summary>
        private string _lastPaidBatchNo = string.Empty;

        /// <summary>
        /// CHG-v1.1.0-20：收据预览/导出锁定在「本次收款」上下文。
        /// 根因：收款后重载待缴列表会让该缴费对象从下拉消失（欠费结清），
        /// 内部刷新会把 SelectedProperty 切到列表首项、把勾选与选中账单清空，
        /// 进而触发 UpdateReceiptPreview 用「新选中态」重算——表现为收款后收据卡片的
        /// 缴款人/项目/金额被清空、导出模板时缴款人与「缴费对象」列错位（显示成别的缴费对象）。
        /// 处置：收款成功即锁定预览与导出的数据源（缴款人/是否自定义缴费人/明细/流水号），
        /// 直到用户主动勾选或选择账单时才解锁回到实时预览。
        /// </summary>
        private bool _receiptPreviewLocked;

        /// <summary>CHG-v1.1.0-20：本次收款的实际缴款人（收款前捕获，不受列表刷新影响）。</summary>
        private string _lastPaidPayeeName = string.Empty;

        /// <summary>CHG-v1.1.0-20：本次收款是否为自定义缴费对象（决定模板是否过滤「缴费对象」列）。</summary>
        private bool _lastPaidCustomPayer;

        /// <summary>最近一次导出的模板文件路径（用于状态提示）。</summary>
        private string _exportedFilePath = string.Empty;
        public string ExportedFilePath { get { return _exportedFilePath; } private set { SetProperty(ref _exportedFilePath, value); } }

        /// <summary>是否已有可导出的收款明细。</summary>
        public bool CanExportTemplate
        {
            get { return _lastPaidItems.Count > 0 || Bills.Any(x => x.IsChecked); }
        }

        private static ReceiptTemplateItemRequest ToTemplateItem(PaymentBillRow row)
        {
            return new ReceiptTemplateItemRequest
            {
                BillNo = row.NoText,
                // CHG-v1.1.0-20：自定义缴费对象账单的「缴费对象」即手工填写的缴费人名称，
                // 明细文本为空或「—」时用账单缴费人兜底，保证模板列过滤判定（对象＝缴款人）成立。
                ObjectText = ResolveObjectText(row),
                ChargeItemName = row.Dto.ChargeItemName,
                CyclePeriod = row.Dto.CyclePeriod,
                Amount = row.UnpaidAmount > 0 ? row.UnpaidAmount : row.Dto.Amount
            };
        }

        private static string ResolveObjectText(PaymentBillRow row)
        {
            string text = row == null ? null : row.ObjectText;
            if (!string.IsNullOrWhiteSpace(text) && text != "—") { return text; }
            string payer = row == null || row.Dto == null ? null : row.Dto.PayerName;
            return string.IsNullOrWhiteSpace(payer) ? (text ?? "—") : payer.Trim();
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

        /// <summary>应缴明细区块标题（CHG-v1.1.0-12：去掉括号与括号内内容，只保留「应缴明细」）。</summary>
        public string BillsTitleText { get { return "应缴明细"; } }

        /// <summary>CHG-v1.1.0-12：全选（勾选当前缴费对象下全部账单）。</summary>
        public bool IsSelectAllBills
        {
            get { return Bills.Count > 0 && Bills.All(x => x.IsChecked); }
            set
            {
                // 全选只勾选可收款账单（已结清/无应缴的行不参与收款）
                foreach (PaymentBillRow row in Bills) { row.IsChecked = value && row.UnpaidAmount > 0; }
                NotifyBillSelectionChanged();
            }
        }

        public int CheckedBillCount { get { return Bills.Count(x => x.IsChecked); } }

        public bool HasCheckedBills { get { return CheckedBillCount > 0; } }

        /// <summary>已勾选账单的未收合计（批量收款金额口径）。</summary>
        public decimal CheckedUnpaidAmount { get { return Bills.Where(x => x.IsChecked).Sum(x => x.UnpaidAmount); } }

        public string CheckedBillsText
        {
            get
            {
                int count = CheckedBillCount;
                return count == 0
                    ? "未勾选账单"
                    : ("已选 " + count + " 张 · 合计 ¥" + CheckedUnpaidAmount.ToString("N2"));
            }
        }

        /// <summary>是否处于统一收款模式（勾选 ≥2 张时金额按合计锁定）。</summary>
        public bool IsBatchPayment { get { return CheckedBillCount >= 2; } }

        private void NotifyBillSelectionChanged()
        {
            OnPropertyChanged(nameof(IsSelectAllBills));
            OnPropertyChanged(nameof(CheckedBillCount));
            OnPropertyChanged(nameof(HasCheckedBills));
            OnPropertyChanged(nameof(CheckedUnpaidAmount));
            OnPropertyChanged(nameof(CheckedBillsText));
            OnPropertyChanged(nameof(IsBatchPayment));
            OnPropertyChanged(nameof(PayAmountHintText));
            if (IsBatchPayment)
            {
                PayAmount = CheckedUnpaidAmount;   // 统一收款：按勾选账单欠费合计锁定
            }
            else if (CheckedBillCount == 1)
            {
                var only = Bills.First(x => x.IsChecked);
                SelectedBill = only;
            }
            else
            {
                SelectedBill = null;
            }
            UpdateReceiptPreview();
        }

        /// <summary>收款金额提示（单张可改、多张锁定为合计）。</summary>
        public string PayAmountHintText
        {
            get
            {
                if (CheckedBillCount == 0) { return "请先勾选待缴账单"; }
                if (IsBatchPayment) { return "统一收款：按勾选账单欠费合计收款（不支持部分缴）"; }
                return "单张收款：可修改金额（不超过应缴金额），支持部分缴";
            }
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
                    UpdateAmountWarn();
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

        /// <summary>CHG-v1.1.2-51：收款金额超限提示（原「多缴自动转入预存账户」提示已下线）。</summary>
        public string AmountWarnText { get { return _amountWarnText; } private set { SetProperty(ref _amountWarnText, value); } }

        public string ReceiptPayeeText { get { return _receiptPayeeText; } private set { SetProperty(ref _receiptPayeeText, value); } }

        public string ReceiptItemText { get { return _receiptItemText; } private set { SetProperty(ref _receiptItemText, value); } }

        public string ReceiptMethodText { get { return _receiptMethodText; } private set { SetProperty(ref _receiptMethodText, value); } }

        public string ReceiptUnitText { get { return _receiptUnitText; } private set { SetProperty(ref _receiptUnitText, value); } }

        /// <summary>CHG-v1.1.0-13：收据落款经手人 = 用户填写值，留空回落管理员名。</summary>
        public string ReceiptHandlerText { get { return EffectiveHandlerName; } }

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

        /// <summary>CHG-v1.1.0-14：导出「收据打印模板」（替代原「打印收据」，收据号已下线）。</summary>
        public IAsyncRelayCommand ExportReceiptTemplateCommand { get; }

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
            // CHG-v1.1.0-13：按**缴费人（业主）**聚合 —— 一个业主一行，覆盖其名下房产、车位与业主直缴的全部欠费；
            // 找不到缴费人的账单（无主对象）退化为按对象分组，避免漏账。
            var groups = page.Items
                // CHG-v1.1.0-18：自定义缴费对象账单（payer_name，无房产/车位/业主）同样纳入收款
                .Where(x => x.Amount > x.PaidAmount && (x.PropertyId.HasValue || x.ParkingId.HasValue || x.OwnerId.HasValue ||
                                                        !string.IsNullOrWhiteSpace(x.PayerName)))
                .GroupBy(x => x.PayerOwnerId.HasValue
                    ? "payer:" + x.PayerOwnerId.Value
                    : (!string.IsNullOrWhiteSpace(x.PayerName) ? "name:" + x.PayerName.Trim()
                        : (x.OwnerId.HasValue ? "owner:" + x.OwnerId.Value
                            : (x.ParkingId.HasValue ? "parking:" + x.ParkingId.Value : "property:" + x.PropertyId.Value))));
            foreach (var g in groups.OrderBy(g => g.First().OwnerName).ThenBy(g => g.Key))
            {
                var first = g.First();
                // 主房产：按楼栋/房号排序取第一套（用于下拉固定四段展示的「楼栋 · 房号」）
                var primaryProperty = g.Where(x => x.PropertyId.HasValue)
                    .OrderBy(x => x.BuildingNo).ThenBy(x => x.RoomNo).FirstOrDefault();
                // CHG-v1.1.0-17：业主直缴账单（办卡费/清理费/维修费等）本身无房产行，
                // 服务端已按业主主房产回填楼栋/房号，此处回落到该行，保证四段格式恒完整。
                if (primaryProperty == null)
                {
                    // CHG-v1.1.2-53：车位账单同样由服务端按缴费人名下主房产回填；
                    // 排序口径与「主房产」一致（楼栋 → 房号），避免同一业主每次进页面显示不同的房产。
                    primaryProperty = g.Where(x => !string.IsNullOrEmpty(x.BuildingNo) || !string.IsNullOrEmpty(x.RoomNo))
                        .OrderBy(x => x.BuildingNo).ThenBy(x => x.RoomNo).FirstOrDefault();
                }
                // CHG-v1.1.2-53：无房产的缴费人（车主 / 未绑房产的业主）用名下车位编号兜底展示
                var spaceBills = g.Where(x => !string.IsNullOrEmpty(x.SpaceNo)).ToList();
                _allProperties.Add(new PropertyPaymentOption
                {
                    PayerOwnerId = first.PayerOwnerId,
                    PayerName = string.IsNullOrWhiteSpace(first.PayerName) ? null : first.PayerName.Trim(),
                    FallbackObjectKind = first.OwnerId.HasValue ? 2 : (first.ParkingId.HasValue ? 1 : 0),
                    FallbackObjectId = first.OwnerId ?? first.ParkingId ?? first.PropertyId ?? 0,
                    OwnerName = first.OwnerName ?? "",
                    BuildingNo = primaryProperty == null ? null : primaryProperty.BuildingNo,
                    RoomNo = primaryProperty == null ? null : primaryProperty.RoomNo,
                    SpaceNo = spaceBills.Count == 0 ? null : spaceBills[0].SpaceNo,
                    SpaceCount = spaceBills.Select(x => x.SpaceNo).Distinct().Count(),
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
                    (x.OwnerName ?? string.Empty).Contains(kw) ||
                    (x.BuildingNo ?? string.Empty).Contains(kw) ||
                    (x.RoomNo ?? string.Empty).Contains(kw) ||
                    // CHG-v1.1.2-53：无房产业主（车主）支持按车位编号检索
                    (x.SpaceNo ?? string.Empty).Contains(kw));
            }
            var matches = source.OrderBy(x => x.OwnerName).ThenBy(x => x.BuildingNo).ThenBy(x => x.RoomNo).ToList();
            PropertyPaymentOption keep = SelectedProperty != null && matches.Any(x => IsSameOption(x, SelectedProperty))
                ? matches.First(x => IsSameOption(x, SelectedProperty))
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

        /// <summary>判断两个下拉项是否指向同一缴费人（无缴费人时退化为同一兜底对象）。</summary>
        private static bool IsSameOption(PropertyPaymentOption a, PropertyPaymentOption b)
        {
            if (a == null || b == null) { return false; }
            // CHG-v1.1.0-18：自定义缴费对象按名称识别（与业主/对象口径互斥）
            if (!string.IsNullOrEmpty(a.PayerName) || !string.IsNullOrEmpty(b.PayerName))
            {
                return string.Equals(a.PayerName, b.PayerName, StringComparison.OrdinalIgnoreCase);
            }
            if (a.PayerOwnerId.HasValue || b.PayerOwnerId.HasValue) { return a.PayerOwnerId == b.PayerOwnerId; }
            return a.FallbackObjectKind == b.FallbackObjectKind && a.FallbackObjectId == b.FallbackObjectId;
        }

        /// <summary>
        /// CHG-v1.1.0-18：缴费人显示名 —— 自定义缴费对象取手工填写的名称，其余取业主姓名。
        /// </summary>
        private static string PayeeName(string ownerName, string payerName)
        {
            if (!string.IsNullOrWhiteSpace(payerName)) { return payerName.Trim(); }
            return ownerName ?? string.Empty;
        }

        /// <summary>
        /// CHG-v1.1.0-20：解析本次收款的缴款人 —— 优先取所选缴费对象（下拉条目），
        /// 缺失时回落本次入账账单自身的缴费人（业主姓名或自定义缴费对象名称）。
        /// </summary>
        private string ResolvePaidPayeeName(System.Collections.Generic.List<PaymentBillRow> paidRows)
        {
            if (_selectedProperty != null)
            {
                string optionName = PayeeName(_selectedProperty.OwnerName, _selectedProperty.PayerName);
                if (!string.IsNullOrWhiteSpace(optionName)) { return optionName.Trim(); }
            }
            PaymentBillRow first = paidRows == null ? null : paidRows.FirstOrDefault(x => x != null && x.Dto != null);
            if (first == null) { return string.Empty; }
            return PayeeName(first.Dto.OwnerName, first.Dto.PayerName).Trim();
        }

        /// <summary>
        /// CHG-v1.1.0-20：用户主动改动账单勾选/选中时解锁收据预览（内部刷新不会调用本方法）。
        /// </summary>
        public void NotifyUserChangedSelection()
        {
            if (!_receiptPreviewLocked) { return; }
            _receiptPreviewLocked = false;
            UpdateReceiptPreview();
        }

        /// <summary>CHG-v1.1.0-20：导出模板的缴款人 —— 有本次收款上下文时以其为准。</summary>
        private string ResolveExportPayeeName()
        {
            if (_lastPaidItems.Count > 0 && !string.IsNullOrWhiteSpace(_lastPaidPayeeName))
            {
                return _lastPaidPayeeName;
            }
            return _selectedProperty == null ? "—" : (PayeeName(_selectedProperty.OwnerName, _selectedProperty.PayerName) ?? "—");
        }

        /// <summary>CHG-v1.1.0-20：导出模板是否按「自定义缴费对象」过滤「缴费对象」列。</summary>
        private bool IsExportCustomPayer()
        {
            if (_lastPaidItems.Count > 0) { return _lastPaidCustomPayer; }
            return _selectedProperty != null && !string.IsNullOrWhiteSpace(_selectedProperty.PayerName);
        }

        private async Task LoadPropertyBillsAsync(PropertyPaymentOption option)
        {
            if (option == null) { return; }
            // CHG-v1.1.0-12：收款成功后的刷新不覆盖「收款结果」提示（否则入账提示被「账单已加载」冲掉）
            if (_quietBillsReload)
            {
                await LoadBillsCoreAsync(option);
                return;
            }
            await RunAsync(() => LoadBillsCoreAsync(option), "账单已加载");
        }

        private async Task LoadBillsCoreAsync(PropertyPaymentOption option)
        {
            {
                Bills.Clear();
                var page = await Api.QueryBillsAsync(new BillQueryRequest
                {
                    // CHG-v1.1.0-13：有缴费人 → 按业主取全部欠费（房 / 车位 / 直缴）；无缴费人 → 退化为按对象取
                    PayerOwnerId = option.PayerOwnerId,
                    // CHG-v1.1.0-18：自定义缴费对象 → 按名称取该对象的全部欠费
                    PayerName = option.PayerName,
                    OwnerId = !option.PayerOwnerId.HasValue && string.IsNullOrEmpty(option.PayerName) && option.FallbackObjectKind == 2 ? (int?)option.FallbackObjectId : null,
                    PropertyId = !option.PayerOwnerId.HasValue && string.IsNullOrEmpty(option.PayerName) && option.FallbackObjectKind != 2 ? (int?)option.FallbackObjectId : null,
                    PageSize = 200
                });
                // CHG-v1.1.0-12：只列可收款账单（草稿未发布不能收款），避免「全选后整批失败」
                foreach (var dto in page.Items.Where(x => x.Status != BillStatus.Draft).OrderBy(x => x.DueAt).ThenBy(x => x.Id))
                {
                    var row = new PaymentBillRow { Dto = dto };
                    row.PropertyChanged += OnBillRowChanged;
                    Bills.Add(row);
                }
                ReceivableTotalText = "¥" + Bills.Sum(x => x.Dto.Amount).ToString("N2");
                PaidTotalText = "¥" + Bills.Sum(x => x.Dto.PaidAmount).ToString("N2");
                UnpaidTotalText = "¥" + Bills.Sum(x => x.UnpaidAmount).ToString("N2");
                // CHG-v1.1.0-12：改由勾选驱动（单选/多选/全选统一收款），不再自动选中最早账单
                SelectedBill = null;
                PayAmount = 0m;
                AmountWarnText = string.Empty;
                NotifyBillSelectionChanged();
            }
        }

        private void OnBillRowChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PaymentBillRow.IsChecked))
            {
                NotifyBillSelectionChanged();
            }
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

        /// <summary>
        /// CHG-v1.1.2-51：下线「多缴自动转入预存账户」—— 原提示承诺了一条并不闭环的业务链路
        /// （超出金额静默转入预存账户，但系统没有预存余额的查看 / 退回入口）。
        /// 现口径：收款金额不得超过该账单未收金额，超出即给出可读提示并阻止提交。
        /// </summary>
        private void UpdateAmountWarn()
        {
            if (IsBatchPayment)
            {
                AmountWarnText = string.Empty;   // 统一收款按勾选账单欠费合计核销，不支持部分缴
                return;
            }
            if (_selectedBill == null)
            {
                AmountWarnText = string.Empty;
                return;
            }
            if (PayAmount > _selectedBill.UnpaidAmount)
            {
                var excess = PayAmount - _selectedBill.UnpaidAmount;
                AmountWarnText = "收款金额超出应缴 ¥" + excess.ToString("0.00") +
                                 "，请调整为不超过该账单未收金额 ¥" + _selectedBill.UnpaidAmount.ToString("0.00");
            }
            else
            {
                AmountWarnText = string.Empty;
            }
        }

        private void UpdateReceiptPreview()
        {
            // CHG-v1.1.0-20：收款成功后卡片锁定为「本次收款」结果，内部列表刷新不再覆盖它
            if (_receiptPreviewLocked) { return; }
            if (IsBatchPayment)
            {
                ReceiptPayeeText = _selectedProperty == null ? "—" : (PayeeName(_selectedProperty.OwnerName, _selectedProperty.PayerName) ?? "—");
                ReceiptItemText = "统一收款 " + CheckedBillCount + " 笔（勾选账单合计）";
                ReceiptMethodText = PayMethodText;
                ReceiptUnitText = "安怡物业服务中心";
                ReceiptAmountLowerText = "¥" + CheckedUnpaidAmount.ToString("N2");
                ReceiptAmountCapitalText = ToChineseCapital(CheckedUnpaidAmount);
                return;
            }
            if (_selectedBill == null)
            {
                ReceiptPayeeText = "—";
                ReceiptItemText = "—";
                ReceiptAmountLowerText = "¥0.00";
                ReceiptAmountCapitalText = "零元整";
                return;
            }
            ReceiptPayeeText = PayeeName(_selectedBill.Dto.OwnerName, _selectedBill.Dto.PayerName);
            ReceiptItemText = _selectedBill.Dto.ChargeItemName + " " + _selectedBill.Dto.CyclePeriod;
            ReceiptMethodText = PayMethodText;
            ReceiptUnitText = "安怡物业服务中心";
            ReceiptAmountLowerText = "¥" + PayAmount.ToString("N2");
            ReceiptAmountCapitalText = ToChineseCapital(PayAmount);
        }

        private void RequestConfirm()
        {
            int checkedCount = CheckedBillCount;
            if (checkedCount == 0)
            {
                ErrorText = "请先勾选待缴账单";
                return;
            }
            if (checkedCount == 1)
            {
                if (_selectedBill == null)
                {
                    ErrorText = "请先勾选待缴账单";
                    return;
                }
                if (PayAmount <= 0)
                {
                    ErrorText = "收款金额必须大于 0";
                    return;
                }
                // CHG-v1.1.2-51：下线「多缴转预存」后，收款金额不得超过该账单未收金额
                if (PayAmount > _selectedBill.UnpaidAmount)
                {
                    ErrorText = "收款金额不能超过该账单未收金额 ¥" +
                                _selectedBill.UnpaidAmount.ToString("0.00") + "，请调整后重新收款";
                    return;
                }
                ConfirmText = "金额 ¥" + PayAmount.ToString("0.00") + "，方式：" + PayMethodText + "，确认入账？";
            }
            else
            {
                // 统一收款：按勾选账单欠费合计
                PayAmount = CheckedUnpaidAmount;
                ConfirmText = "统一收款 " + checkedCount + " 张账单，合计 ¥" + CheckedUnpaidAmount.ToString("0.00") +
                    "，方式：" + PayMethodText + "，确认入账？";
            }
            IsConfirmVisible = true;
        }

        private async Task ConfirmPaymentAsync()
        {
            PaymentBillRow bill = _selectedBill;
            decimal amount = PayAmount;
            bool print = PrintReceipt;
            IsConfirmVisible = false;
            var checkedRows = Bills.Where(x => x.IsChecked && x.UnpaidAmount > 0).ToList();
            bool batchMode = checkedRows.Count >= 2;
            if (checkedRows.Count == 0) { return; }
            if (!batchMode && (bill == null || amount <= 0)) { return; }

            // CHG-v1.1.0-20：在提交收款前捕获「本次收款上下文」——收款后的列表刷新会让
            // 该缴费对象从下拉消失并切换选中态，故缴款人与模板口径必须在刷新前取定。
            var paidRows = batchMode ? checkedRows : new System.Collections.Generic.List<PaymentBillRow> { bill };
            string paidPayeeName = ResolvePaidPayeeName(paidRows);
            bool paidCustomPayer = paidRows.Count > 0 &&
                paidRows.All(x => x != null && x.Dto != null && !string.IsNullOrWhiteSpace(x.Dto.PayerName));
            var paidItems = paidRows.Where(x => x != null && x.Dto != null).Select(ToTemplateItem).ToList();
            if (!batchMode && paidItems.Count > 0) { paidItems[0].Amount = amount; }

            PaymentDto payment = null;
            PaymentBatchResultDto batchResult = null;
            await RunAsync(async () =>
            {
                if (batchMode)
                {
                    // CHG-v1.1.0-12：统一收款 —— 逐张按欠费全额核销，共享收款流水号
                    batchResult = await Api.CreateBatchPaymentAsync(new PaymentBatchCreateRequest
                    {
                        Items = checkedRows.Select(x => new PaymentBatchItemRequest
                        {
                            BillId = x.Dto.Id,
                            Amount = x.UnpaidAmount
                        }).ToList(),
                        PayMethod = (PayMethod)PayMethod,
                        PrintReceipt = print,
                        Remark = (Remark ?? string.Empty).Trim()
                    });
                }
                else
                {
                    payment = await Api.CreatePaymentAsync(new PaymentCreateRequest
                    {
                        BillId = bill.Dto.Id,
                        Amount = amount,
                        PayMethod = (PayMethod)PayMethod,
                        PrintReceipt = print,
                        Remark = (Remark ?? string.Empty).Trim()
                    });
                }

                _quietBillsReload = true;
                try { await ReloadPropertiesAsync(); }
                finally { _quietBillsReload = false; }
                Remark = string.Empty;
                // CHG-v1.1.0-20：先锁上下文再回写卡片（列表刷新的异步续体不会再覆盖）
                _lastPaidPayeeName = paidPayeeName;
                _lastPaidCustomPayer = paidCustomPayer;
                _lastPaidItems = paidItems;
                _lastPaidBatchNo = batchMode
                    ? (batchResult == null ? string.Empty : (batchResult.BatchNo ?? string.Empty))
                    : (payment == null ? string.Empty : (payment.BatchNo ?? string.Empty));
                _receiptPreviewLocked = true;
                OnPropertyChanged(nameof(CanExportTemplate));
                if (batchMode && batchResult != null)
                {
                    await ApplyBatchReceiptAsync(batchResult);
                }
                else if (payment != null)
                {
                    ApplySingleReceipt(payment, bill, amount);
                }
            }, null);

            if (batchResult != null)
            {
                // CHG-v1.1.0-20：模板明细与流水号已在收款前捕获（见上方 paidItems），此处仅提示结果
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "统一收款已入账：" + batchResult.Count + " 张账单，合计 ¥" +
                    batchResult.TotalAmount.ToString("0.00") + "，收款流水号 " + batchResult.BatchNo +
                    "（各账单号保持不变，退款/减免仍按原账单号引用）";
            }
            else if (payment != null)
            {
                // CHG-v1.1.2-51：收款口径只有「全额 / 部分缴」，不再出现「超额转预存」结果提示
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "收款已入账 ¥" + amount.ToString("0.00") +
                    "（流水中：" + (payment.BatchNo ?? string.Empty) + "）";
            }
        }

        /// <summary>
        /// CHG-v1.1.0-15：统一收款模板预览 —— 收据号已前后端下线，
        /// 「收款流水号」即本批流水号（与收支明细流水「关联单据」同一编号）。
        /// </summary>
        private Task ApplyBatchReceiptAsync(PaymentBatchResultDto result)
        {
            BatchNoText = result.BatchNo;
            // CHG-v1.1.0-20：缴款人取收款前捕获的上下文（不再依赖刷新后的选中态）
            ReceiptPayeeText = string.IsNullOrWhiteSpace(_lastPaidPayeeName) ? "—" : _lastPaidPayeeName;
            var names = _lastPaidItems.Select(x => x.ChargeItemName).Where(x => !string.IsNullOrEmpty(x));
            if (names.Count() == 0)
            {
                names = Bills.Where(x => x.IsChecked).Select(x => x.Dto.ChargeItemName).Where(x => !string.IsNullOrEmpty(x));
            }
            ReceiptItemText = "统一收款 " + result.Count + " 笔：" + string.Join("、", names.Distinct());
            ReceiptMethodText = PayMethodText;
            ReceiptUnitText = "安怡物业服务中心";
            ReceiptAmountLowerText = "¥" + result.TotalAmount.ToString("N2");
            ReceiptAmountCapitalText = ToChineseCapital(result.TotalAmount);
            OnPropertyChanged(nameof(HasBatchNo));
            return Task.CompletedTask;
        }

        /// <summary>单张收款模板预览（收款流水号即本笔流水号）。</summary>
        private void ApplySingleReceipt(PaymentDto payment, PaymentBillRow bill, decimal amount)
        {
            BatchNoText = payment == null ? string.Empty : (payment.BatchNo ?? string.Empty);
            // CHG-v1.1.0-20：优先用收款前捕获的缴款人，回落本次账单自身口径
            ReceiptPayeeText = !string.IsNullOrWhiteSpace(_lastPaidPayeeName)
                ? _lastPaidPayeeName
                : (bill == null ? "—" : PayeeName(bill.Dto.OwnerName, bill.Dto.PayerName));
            ReceiptItemText = bill == null ? "—" : bill.Dto.ChargeItemName + " " + bill.Dto.CyclePeriod +
                (amount < bill.UnpaidAmount ? "（部分缴）" : "");
            ReceiptMethodText = PayMethodText;
            ReceiptUnitText = "安怡物业服务中心";
            ReceiptAmountLowerText = "¥" + amount.ToString("N2");
            ReceiptAmountCapitalText = ToChineseCapital(amount);
            OnPropertyChanged(nameof(HasBatchNo));
        }

        /// <summary>
        /// CHG-v1.1.0-14：导出「收据打印模板」（PDF）到本机。
        /// 口径：收据号已下线；模板以收款流水号 + 逐项收款明细（缴费对象/收费项目/账单期间/账单号/金额）呈现，
        /// 统一收款时把同批每张账单逐行写清，便于打印核对与留档。
        /// </summary>
        private async Task ExportReceiptTemplateAsync()
        {
            var items = _lastPaidItems.Count > 0
                ? _lastPaidItems
                : Bills.Where(x => x.IsChecked).Select(ToTemplateItem).ToList();
            if (items.Count == 0)
            {
                ErrorText = "请先完成收款（或勾选待缴账单）后再导出收据模板";
                return;
            }

            decimal total = items.Sum(x => x.Amount);
            var request = new ReceiptTemplateRequest
            {
                // CHG-v1.1.0-20：本次收款上下文优先（收款后选中态可能已切换到别的缴费对象）
                PayeeName = ResolveExportPayeeName(),
                HandlerName = EffectiveHandlerName,
                PayMethod = PayMethodText,
                PaidAt = DateTime.Now,
                Remark = Remark,
                // 优先使用最近一次收款时捕获的流水号（与收支明细流水「关联单据」同源）
                BatchNo = string.IsNullOrEmpty(_lastPaidBatchNo) ? BatchNoText : _lastPaidBatchNo,
                // CHG-v1.1.0-19/20：自定义缴费对象（缴款人＝缴费对象）时隐藏「缴费对象」列，避免同一名称重复两列
                HideObjectColumn = IsExportCustomPayer(),
                Items = items
            };

            await RunAsync(async () =>
            {
                ReportLogDto log = await Api.ExportReceiptTemplateAsync(request);
                if (log == null || log.Id <= 0)
                {
                    throw new InvalidOperationException("收据模板导出失败：服务端未生成导出记录");
                }
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "导出收据打印模板",
                    Filter = "PDF 文件|*.pdf",
                    FileName = "收款收据_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".pdf"
                };
                if (dialog.ShowDialog() != true)
                {
                    return;
                }
                await Api.DownloadReportFileAsync(log.Id, dialog.FileName);
                ExportedFilePath = dialog.FileName;
            }, null);

            if (!string.IsNullOrEmpty(ExportedFilePath))
            {
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "收据打印模板已导出：" + ExportedFilePath +
                    "（共 " + items.Count + " 笔，合计 ¥" + total.ToString("N2") + "）";
            }
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
