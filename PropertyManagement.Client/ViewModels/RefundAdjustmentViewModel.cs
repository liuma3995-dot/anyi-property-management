using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>退款/减免/调整记录行（PG-FIN-04，T4F-4-1：类型标签/关联房产/状态/当前节点/附件）。</summary>
    public class RefundRow : ObservableObject
    {
        public RefundAdjustmentDto Dto { get; set; }

        /// <summary>CHG-v1.1.2-41：导出本单据 PDF（留档/审计追溯），由页面 VM 注入。</summary>
        public IAsyncRelayCommand ExportCommand { get; set; }

        /// <summary>记录表「经办人」列（旧数据可为空）。</summary>
        public string OperatorText
        {
            get { return string.IsNullOrWhiteSpace(Dto.OperatorName) ? "—" : Dto.OperatorName.Trim(); }
        }

        public string TypeText
        {
            get
            {
                switch (Dto.RefundType)
                {
                    case RefundType.Refund: return "退款";
                    case RefundType.Discount: return "减免";
                    default: return "调整";
                }
            }
        }

        public Brush TypeBrush
        {
            get
            {
                switch (Dto.RefundType)
                {
                    case RefundType.Refund: return Br("#D64545");
                    case RefundType.Discount: return Br("#12805C");
                    default: return Br("#7A5AF8");
                }
            }
        }

        public Brush TypeBg
        {
            get
            {
                switch (Dto.RefundType)
                {
                    case RefundType.Refund: return Br("#FDECEC");
                    case RefundType.Discount: return Br("#E8F7F1");
                    default: return Br("#F1EDFF");
                }
            }
        }

    /// <summary>
    /// CHG-v1.1.2-12：金额方向 —— 退款/减免恒为冲减（−）；
    /// 账务调整由「方式」决定：调增补收＋、调减冲正−（未指定方式默认＋）。
    /// </summary>
    public string SignText
    {
        get
        {
            // CHG-v1.1.2-40：减免＝调减应收（不动资金），不再用代表资金流出的「−¥」符号
            if (Dto.RefundType == RefundType.Discount) { return "¥"; }
            if (Dto.RefundType != RefundType.Adjustment) { return "-¥"; }
            return Dto.AdjustDir == 2 ? "-¥" : "+¥";
        }
    }

        public string NoText { get { return string.IsNullOrEmpty(Dto.RefNo) ? "REF-" + Dto.Id.ToString("D4") : Dto.RefNo; } }

        public string AmountText { get { return SignText + Dto.Amount.ToString("N2"); } }

        /// <summary>CHG-v1.1.2-04：无关联账单的冲正/补收记录（bill_id = 0）明确标注，避免与「数据缺失」混淆。</summary>
        public string PropertyNoText
        {
            get
            {
                if (Dto.BillId <= 0) { return "无关联账单"; }
                return string.IsNullOrEmpty(Dto.PropertyNo) ? "—" : Dto.PropertyNo;
            }
        }

        public string Reason { get { return string.IsNullOrEmpty(Dto.Reason) ? "—" : Dto.Reason; } }

        public string DateText { get { return Dto.CreatedAt.ToString("yyyy-MM-dd"); } }

        /// <summary>节点流仅作状态展示（负责人早期决策：不落地审批流）。</summary>
        public string StatusText { get { return "已完成"; } }

        public string NodeText { get { return "已归档"; } }

        /// <summary>CHG-v1.1.2-12：处理方式（落库留痕，记录表展示）。</summary>
        public string MethodText { get { return string.IsNullOrEmpty(Dto.Method) ? "—" : Dto.Method; } }

        public string AttachmentText { get { return string.IsNullOrEmpty(Dto.AttachmentName) ? "—" : Dto.AttachmentName; } }

        private static Brush Br(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    /// <summary>关联房产选项（PG-FIN-04）。</summary>
    public class RefundPropertyOption
    {
        /// <summary>CHG-v1.1.0-13：关联对象类型 —— 0 房产／1 车位／2 业主直缴（车位与业主直缴账单也需可登记调整）。</summary>
        public int ObjectKind { get; set; }

        public int ObjectId { get; set; }

        /// <summary>
        /// CHG-v1.1.0-18：自定义缴费对象名称（ObjectKind=3 时按名称匹配账单，其余类型为空）。
        /// </summary>
        public string PayerName { get; set; }

        /// <summary>
        /// CHG-v1.2.0-34：缴费人（业主）主键 —— **本轮的关键口径**：关联对象改为按「缴费人」聚合，
        /// 一位业主只出现一条（其名下房产 / 车位 / 业主直缴的全部账单都归到这一条），
        /// 不再因缴费对象不同而重复出现同一业主（负责人反馈的「关联对象有噪音」）。
        /// </summary>
        public int? PayerOwnerId { get; set; }

        /// <summary>缴费人姓名（业主姓名；自定义缴费对象为空）。</summary>
        public string OwnerName { get; set; }

        /// <summary>主房产楼栋 / 单元 / 房号（与收款登记下拉同源：同一套主房产，单元缺失自动省略）。</summary>
        public string BuildingNo { get; set; }
        public string UnitNo { get; set; }
        public string RoomNo { get; set; }

        /// <summary>车库位编号（缴费人名下没有房产或未绑房产时兜底定位）。</summary>
        public string SpaceNo { get; set; }
        public int SpaceCount { get; set; }

        /// <summary>该缴费人名下关联账单笔数（下拉文案「账单 N 笔」）。</summary>
        public int BillCount { get; set; }

        /// <summary>地址段「楼栋/单元/房号」（单元号补「单元」后缀；缺项自动省略）。</summary>
        public string AddressPathText
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(BuildingNo)) { parts.Add(BuildingNo.Trim()); }
                if (!string.IsNullOrWhiteSpace(UnitNo))
                {
                    string unit = UnitNo.Trim();
                    parts.Add(unit.EndsWith("单元", StringComparison.Ordinal) ? unit : unit + "单元");
                }
                if (!string.IsNullOrWhiteSpace(RoomNo)) { parts.Add(RoomNo.Trim()); }
                return string.Join("/", parts);
            }
        }

        /// <summary>
        /// CHG-v1.2.0-34：下拉展示 —— 与收款登记缴费对象同口径
        /// 「业主姓名 · 楼栋/单元/房号 · 账单 N 笔」；无房产的缴费人用「车位 X / 业主直缴」兜底；
        /// 自定义缴费对象用「名称 · 自定义缴费对象 · 账单 N 笔」；首项为「不指定（全部账单可选）」。
        /// </summary>
        public string DisplayText
        {
            get
            {
                if (ObjectKind < 0) { return "不指定（全部账单可选）"; }
                if (!string.IsNullOrWhiteSpace(PayerName))
                {
                    return PayerName.Trim() + " · 自定义缴费对象 · 账单 " + BillCount + " 笔";
                }
                string name = string.IsNullOrWhiteSpace(OwnerName) ? "（未绑定业主）" : OwnerName.Trim();
                string address = AddressPathText;
                if (!string.IsNullOrEmpty(address))
                {
                    return name + " · " + address + " · 账单 " + BillCount + " 笔";
                }
                string label = string.IsNullOrEmpty(SpaceNo)
                    ? "业主直缴"
                    : "车位 " + SpaceNo + (SpaceCount > 1 ? " 等 " + SpaceCount + " 个" : string.Empty);
                return name + " · " + label + " · 账单 " + BillCount + " 笔";
            }
        }
    }

    /// <summary>
    /// CHG-v1.1.2-40：退款/减免/调整页的账单候选行 —— 在收款登记账单行之上叠加本页的金额口径提示
    /// （退款/调整看「实缴」，减免看「未收余额」），避免两种口径在同一个列表里说不清。
    /// </summary>
    public class RefundBillRow : PaymentBillRow
    {
        private string _pickerMoneyText = string.Empty;

        /// <summary>候选行右侧的金额提示，如「实缴 ¥100.00」「未收 ¥80.00」。</summary>
        public string PickerMoneyText
        {
            get { return _pickerMoneyText; }
            set { SetProperty(ref _pickerMoneyText, value); }
        }

        /// <summary>未收余额＝应收 − 实缴（减免的可登记上限）。</summary>
        public decimal UnreceivedAmount
        {
            get { return Dto == null ? 0m : Dto.Amount - Dto.PaidAmount; }
        }
    }

    /// <summary>退款/减免/调整页（PG-FIN-04，UC-FIN-004，BR-FIN-06/10；T4F-4-1 三页签/关联房产/方式/附件/记录表列）。</summary>
    public class RefundAdjustmentViewModel : FinancePageViewModel
    {
        private static readonly string AttachDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PropertyManagement", "attachments");

        private RefundPropertyOption _selectedProperty;
        private PaymentBillRow _selectedBill;
        private int _refundType;
        private int _methodIndex;
        private decimal _amount;
        private string _reason = string.Empty;
        private bool _confirmed;
        private string _largeHint = string.Empty;
        private string _attachmentName = string.Empty;
        private string _attachmentPath = string.Empty;
        private string _formTitle = "退款申请";
        private string _amountLabel = "退款金额";

        /// <summary>CHG-v1.2.0-36：页内确认弹窗（提交范围确认 / 删除旧账单确认共用一个弹层）。</summary>
        private bool _isConfirmVisible;
        private string _confirmTitle = string.Empty;
        private string _confirmMessage = string.Empty;
        private string _confirmOkText = "确认";
        /// <summary>待确认动作：0 无 / 1 提交登记 / 2 删除旧账单。</summary>
        private int _pendingAction;
        private System.Collections.Generic.List<int> _pendingBillIds;

        public RefundAdjustmentViewModel(IApiClient api) : base(api)
        {
            SubmitCommand = new AsyncRelayCommand(SubmitAsync);
            PickAttachmentCommand = new RelayCommand(PickAttachment);
            // CHG-v1.2.0-36：关联账单「删除旧账单」入口 + 共用的确认弹层命令
            RequestDeleteBillsCommand = new RelayCommand(RequestDeleteBills);
            ConfirmActionCommand = new AsyncRelayCommand(ConfirmActionAsync);
            CancelActionCommand = new RelayCommand(CancelAction);
            UpdateMethodOptions();
            _ = LoadAsync();
        }

        public ObservableCollection<RefundPropertyOption> Properties { get; } = new ObservableCollection<RefundPropertyOption>();

        public ObservableCollection<RefundBillRow> Bills { get; } = new ObservableCollection<RefundBillRow>();

        public ObservableCollection<RefundBillRow> FilteredBills { get; } = new ObservableCollection<RefundBillRow>();

        public ObservableCollection<RefundRow> Records { get; } = new ObservableCollection<RefundRow>();

        public ObservableCollection<string> MethodOptions { get; } = new ObservableCollection<string>();

        public RefundPropertyOption SelectedProperty
        {
            get { return _selectedProperty; }
            set
            {
                if (SetProperty(ref _selectedProperty, value))
                {
                    FilterBills();
                }
            }
        }

        public PaymentBillRow SelectedBill
        {
            get { return _selectedBill; }
            set
            {
                if (SetProperty(ref _selectedBill, value))
                {
                    // CHG-v1.1.2-24（负责人裁定）：金额只跟随「关联账单勾选」联动 ——
                    // 勾选 1 张 → 自动补全该账单金额；未勾选 → 0；勾选多张 → 保持用户填写的「每张金额」。
                    // 因此这里不再因「行选中」而改写金额（避免未勾选时被预填）。
                }
            }
        }

        // ---------- CHG-v1.1.0-13：关联账单支持多选 + 全选（批量登记同类型调整） ----------

        /// <summary>关联账单选择面板是否展开（下拉浮层）。</summary>
        public bool IsBillPickerOpen { get { return _isBillPickerOpen; } set { SetProperty(ref _isBillPickerOpen, value); } }
        private bool _isBillPickerOpen;

        /// <summary>下拉框显示文本：未选=提示语；已选 1 张=账单摘要；多张=已选 N 张账单。</summary>
        public string BillPickerText
        {
            get
            {
                int count = CheckedBills.Count;
                if (count == 0)
                {
                    // CHG-v1.1.2-04：账务调整允许不选账单（冲正/补收无对应账单的场景）
                    return RefundType == 2 ? "可不选（无账单的冲正/补收）" : "请选择关联账单（可多选/全选）";
                }
                if (count == 1) { return CheckedBills[0].NoText + " · " + CheckedBills[0].Dto.ChargeItemName; }
                return "已选 " + count + " 张账单";
            }
        }

        /// <summary>已勾选账单（当前页签候选口径筛选后的行）。</summary>
        private System.Collections.Generic.List<RefundBillRow> CheckedBills
        {
            get { return FilteredBills.Where(x => x.IsChecked).ToList(); }
        }

        public int CheckedBillCount { get { return CheckedBills.Count; } }

        /// <summary>
        /// CHG-v1.2.0-36：**全选是否可用** —— 只有选定具体「关联对象」时才允许全选。
        /// 背景（负责人 2026-09-22 反馈）：默认关联对象是「不指定（全部账单可选）」，
        /// 此时全选的作用域是**全库候选账单**（跨业主），误点一次就会把无关业主的账单一起登记退款，
        /// 那些账单重新欠费后各自出现在收款登记应缴明细里 —— 正是「多出了几条账单记录」。
        /// </summary>
        public bool CanSelectAllBills
        {
            get { return _selectedProperty != null && _selectedProperty.ObjectKind >= 0; }
        }

        /// <summary>全选可用性提示（放在「全选」右侧，说明当前全选的作用域）。</summary>
        public string SelectAllHintText
        {
            get
            {
                if (!CanSelectAllBills)
                {
                    return "请先选择「关联对象」后再全选（「不指定」时请逐条勾选）";
                }
                return "全选范围：本关联对象下 " + FilteredBills.Count + " 张候选账单";
            }
        }

        /// <summary>全选：作用域＝当前候选列表（退款/调整＝有实缴；减免＝有未收余额）；「不指定」时禁用。</summary>
        public bool IsSelectAllBills
        {
            get { return FilteredBills.Count > 0 && FilteredBills.All(x => x.IsChecked); }
            set
            {
                // CHG-v1.2.0-36：不指定关联对象时禁止全选（避免跨业主误登记）
                if (value && !CanSelectAllBills) { return; }
                foreach (RefundBillRow row in FilteredBills) { row.IsChecked = value; }
                NotifyBillPickerChanged();
            }
        }

        /// <summary>
        /// 勾选汇总提示（每张金额口径，合计 = 每张金额 × 张数）。
        /// CHG-v1.2.0-36：同时列出**账单号**（前 3 张）—— 让用户在提交前看清「这次到底处理了哪几张」。
        /// </summary>
        public string CheckedBillsText
        {
            get
            {
                int count = CheckedBillCount;
                if (count == 0)
                {
                    return RefundType == 2 ? "未勾选账单：本次登记为无账单的冲正/补收" : "未勾选账单";
                }
                string nos = string.Join("、", CheckedBills.Take(3).Select(x => x.NoText));
                if (count > 3) { nos += " 等 " + count + " 张"; }
                return count == 1
                    ? ("已选 1 张 · " + nos)
                    // CHG-v1.2.0-37：多选＝按各自登记上限核销，此处展示**合计**
                    : ("已选 " + count + " 张 · 合计 ¥" + Amount.ToString("N2") + "（按各自登记上限核销）· " + nos);
            }
        }

        /// <summary>是否处于批量登记模式（勾选 ≥2 张）。</summary>
        public bool IsBatchRefund { get { return CheckedBillCount >= 2; } }

        /// <summary>
        /// CHG-v1.2.0-37：金额是否可编辑 —— 多选（≥2 张）时金额自动置为**勾选账单合计**并锁定
        /// （与收款登记「统一收款」同口径：按合计核销、不支持改额）；单张时仍可改额（支持部分退/减）。
        /// </summary>
        public bool IsAmountEditable { get { return !IsBatchRefund; } }

        private void OnBillRowChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PaymentBillRow.IsChecked)) { NotifyBillPickerChanged(); }
        }

        private void NotifyBillPickerChanged()
        {
            OnPropertyChanged(nameof(BillPickerText));
            OnPropertyChanged(nameof(CheckedBillCount));
            OnPropertyChanged(nameof(IsSelectAllBills));
            // CHG-v1.2.0-36：全选可用性 / 作用域提示随「关联对象」与候选列表变化
            OnPropertyChanged(nameof(CanSelectAllBills));
            OnPropertyChanged(nameof(SelectAllHintText));
            OnPropertyChanged(nameof(CheckedBillsText));
            OnPropertyChanged(nameof(IsBatchRefund));
            OnPropertyChanged(nameof(AmountLabelFull));
            // CHG-v1.2.0-37：多选时金额锁定为「合计」
            OnPropertyChanged(nameof(IsAmountEditable));
            SyncAmountWithCheckedBills();
        }

        /// <summary>
        /// CHG-v1.1.2-24：金额与「关联账单」联动 —— 未勾选 → 0；勾选 1 张 → 自动补全该账单的
        /// 登记上限；**勾选 ≥2 张 → 自动汇总为「勾选账单合计」**（CHG-v1.2.0-37：负责人反馈
        /// 「勾选全选之后，金额框只显示一条账单的金额、没有统计全选后的总金额」）。
        /// CHG-v1.1.2-40：上限随页签口径 —— 退款/调整＝实缴金额，减免＝未收余额（应收 − 实缴）。
        /// </summary>
        private void SyncAmountWithCheckedBills()
        {
            int count = CheckedBillCount;
            if (count == 0) { Amount = 0m; return; }
            if (count == 1)
            {
                Amount = BillCap(CheckedBills[0]);
                return;
            }
            // 多选 / 全选：合计（每张按其登记上限累加）
            Amount = CheckedBills.Sum(x => BillCap(x));
        }

        /// <summary>单张账单在当前页签口径下的登记上限（退款/调整＝实缴；减免＝未收余额）。</summary>
        private decimal BillCap(RefundBillRow row)
        {
            if (row == null || row.Dto == null) { return 0m; }
            return _refundType == 1 ? row.UnreceivedAmount : row.Dto.PaidAmount;
        }

        /// <summary>金额标签（批量时标注为「合计」——CHG-v1.2.0-37）。</summary>
        public string AmountLabelFull
        {
            get { return IsBatchRefund ? (AmountLabel + "（合计）") : AmountLabel; }
        }

        public int RefundType
        {
            get { return _refundType; }
            set
            {
                if (SetProperty(ref _refundType, value))
                {
                    UpdateMethodOptions();
                    switch (value)
                    {
                        // CHG-v1.1.2-10：清除页面可见的 mock 编号（UC-FIN-004 一类开发期痕迹）
                        case 0: FormTitle = "退款申请"; AmountLabel = "退款金额"; break;
                        case 1: FormTitle = "费用减免"; AmountLabel = "减免金额"; break;
                        // CHG-v1.1.2-04：账务调整的金额语义明确为「冲正或补收金额」
                        // CHG-v1.1.2-24：金额统一由「关联账单勾选」联动（下方 NotifyBillPickerChanged →
                        // SyncAmountWithCheckedBills）：未勾选为 0、勾选 1 张补全账单金额
                        default: FormTitle = "账务调整"; AmountLabel = "冲正或补收金额"; break;
                    }
                    RefreshLargeHint();
                    // CHG-v1.1.2-40：页签决定候选口径（退款/调整＝有实缴；减免＝有未收余额）与行内金额提示，
                    // 因此切换页签要重筛候选并刷新提示（旧口径下候选固定为「已缴账单」，减免会选不到未缴账单）。
                    RefreshRowMoneyText();
                    FilterBills();
                    OnPropertyChanged(nameof(DirectionHint));
                }
            }
        }

        public int MethodIndex
        {
            get { return _methodIndex; }
            set
            {
                if (SetProperty(ref _methodIndex, value))
                {
                    OnPropertyChanged(nameof(DirectionHint));
                }
            }
        }

        /// <summary>
        /// CHG-v1.1.2-12：方向提示 —— 账务调整的 +/− 由「方式」决定（调增补收＝＋计入应收、调减冲正＝−冲减）。
        /// CHG-v1.1.2-40：补齐退款/减免两条链路的落账口径说明（退款冲减实缴、减免调减应收）。
        /// </summary>
        public string DirectionHint
        {
            get
            {
                if (RefundType == 0)
                {
                    return "本次按「冲减实缴」登记：从已收金额中退还业主，账单应收金额不变";
                }
                if (RefundType == 1)
                {
                    return "本次按「调减应收」登记：直接减少账单应收金额（已缴金额不变，不产生资金流出）";
                }
                string method = MethodOptions.Count > MethodIndex && MethodIndex >= 0 ? MethodOptions[MethodIndex] : string.Empty;
                if (method == null) { method = string.Empty; }
                if (method.IndexOf("补收", StringComparison.Ordinal) >= 0 || method.IndexOf("调增", StringComparison.Ordinal) >= 0)
                {
                    return "本次按「＋」登记：调增补收，计入应收金额（可无关联账单/对象）";
                }
                if (method.IndexOf("冲正", StringComparison.Ordinal) >= 0 || method.IndexOf("调减", StringComparison.Ordinal) >= 0)
                {
                    return "本次按「−」登记：调减冲正，冲减应收金额（可无关联账单/对象）";
                }
                return "请选择方式以确定登记方向：调增补收（＋）/ 调减冲正（−）";
            }
        }

        public decimal Amount
        {
            get { return _amount; }
            set
            {
                if (SetProperty(ref _amount, value))
                {
                    RefreshLargeHint();
                    OnPropertyChanged(nameof(CheckedBillsText));
                }
            }
        }

        public string Reason { get { return _reason; } set { SetProperty(ref _reason, value); } }

        public bool Confirmed { get { return _confirmed; } set { SetProperty(ref _confirmed, value); } }

        public string LargeHint { get { return _largeHint; } private set { SetProperty(ref _largeHint, value); } }

        public bool HasLargeHint { get { return !string.IsNullOrEmpty(_largeHint); } }

        public string FormTitle { get { return _formTitle; } private set { SetProperty(ref _formTitle, value); } }

        public string AmountLabel { get { return _amountLabel; } private set { SetProperty(ref _amountLabel, value); } }

        public string AttachmentName { get { return _attachmentName; } private set { SetProperty(ref _attachmentName, value); } }
        public string AttachmentPath { get { return _attachmentPath; } private set { SetProperty(ref _attachmentPath, value); } }

        public bool HasAttachment { get { return !string.IsNullOrEmpty(_attachmentName); } }

        public IAsyncRelayCommand SubmitCommand { get; }

        public IRelayCommand PickAttachmentCommand { get; }

        /// <summary>CHG-v1.2.0-36：删除所选旧账单（仅已结清；未缴费账单不可删除）。</summary>
        public IRelayCommand RequestDeleteBillsCommand { get; }

        /// <summary>CHG-v1.2.0-36：确认弹层「确认」——按待确认动作分发（提交登记 / 删除旧账单）。</summary>
        public IAsyncRelayCommand ConfirmActionCommand { get; }

        /// <summary>CHG-v1.2.0-36：确认弹层「取消」。</summary>
        public IRelayCommand CancelActionCommand { get; }

        public bool IsConfirmVisible
        {
            get { return _isConfirmVisible; }
            private set { SetProperty(ref _isConfirmVisible, value); }
        }

        public string ConfirmTitle
        {
            get { return _confirmTitle; }
            private set { SetProperty(ref _confirmTitle, value); }
        }

        public string ConfirmMessage
        {
            get { return _confirmMessage; }
            private set { SetProperty(ref _confirmMessage, value); }
        }

        public string ConfirmOkText
        {
            get { return _confirmOkText; }
            private set { SetProperty(ref _confirmOkText, value); }
        }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
            Bills.Clear();
            Properties.Clear();
            // CHG-v1.1.2-12：关联对象改为**可选**（不选＝不按对象过滤，候选为全部已缴账单），
            // 用于无关联对象/无关联账单的冲正与补收登记。
            Properties.Add(new RefundPropertyOption
            {
                ObjectKind = -1,
                ObjectId = 0,
                // CHG-v1.1.2-40：候选含「已缴账单」（退款/调整）与「有未收余额的账单」（减免）
                // CHG-v1.2.0-34：文案由 ObjectKind=-1 派生为「不指定（全部账单可选）」
            });
            // CHG-v1.2.0-36：候选排除「已删除（归档）的旧账单」—— 用户在关联账单里删除的旧账单不再出现，
            // 关联对象的「账单 N 笔」也随之同步（笔数由本候选集合派生）。
            var page = await Api.QueryBillsAsync(new BillQueryRequest { PageSize = 200, ExcludeArchived = true });
                // CHG-v1.1.2-40：候选口径拆分 —— 减免的登记对象是「还没收到的钱」，未缴账单同样要能选到；
                // 草稿（未发布）一律不参与登记。这里取并集，具体由 FilterBills 按当前页签收敛。
                var paid = page.Items.Where(x => x.Status != BillStatus.Draft &&
                        (x.PaidAmount > 0 || x.Amount > x.PaidAmount)).ToList();
                // CHG-v1.2.0-34：关联对象改为**按缴费人（业主）聚合** —— 口径与收款登记缴费对象一致：
                // 原先按「缴费对象」分组，同一业主名下的房产账单 / 车位账单 / 业主直缴账单会各自成为一条，
                // 下拉里出现「同一位业主多条」的噪音（负责人反馈）；现改为按缴费人聚合，一位业主只一条，
                // 选中即关联其名下**全部**关联账单。找不到缴费人的账单（无主对象）仍按对象兜底分组，不漏账；
                // 自定义缴费对象（无档案）按手工填写的名称聚合。
                var payerGroups = paid.GroupBy(x => x.PayerOwnerId.HasValue
                        ? "payer:" + x.PayerOwnerId.Value
                        : (!string.IsNullOrWhiteSpace(x.PayerName) ? "name:" + x.PayerName.Trim()
                            : (x.OwnerId.HasValue ? "owner:" + x.OwnerId.Value
                                : (x.ParkingId.HasValue ? "parking:" + x.ParkingId.Value
                                    : "property:" + x.PropertyId.Value))))
                    .OrderBy(g => g.First().OwnerName).ThenBy(g => g.Key);
                foreach (var g in payerGroups)
                {
                    var first = g.First();
                    string payerName = string.IsNullOrWhiteSpace(first.PayerName) ? null : first.PayerName.Trim();
                    // 主房产：优先取该缴费人自己的房产账单（按楼栋 → 房号），
                    // 否则回落到服务端已按「缴费人名下主房产」回填过楼栋/房号的账单行（车位 / 业主直缴）
                    var primaryProperty = g.Where(x => x.PropertyId.HasValue)
                            .OrderBy(x => x.BuildingNo).ThenBy(x => x.RoomNo).FirstOrDefault()
                        ?? g.Where(x => !string.IsNullOrEmpty(x.BuildingNo) || !string.IsNullOrEmpty(x.RoomNo))
                            .OrderBy(x => x.BuildingNo).ThenBy(x => x.RoomNo).FirstOrDefault();
                    var spaceBills = g.Where(x => !string.IsNullOrEmpty(x.SpaceNo)).ToList();
                    int kind = first.PayerOwnerId.HasValue
                        ? 2
                        : (payerName != null
                            ? 3
                            : (first.ParkingId.HasValue ? 1 : 0));
                    Properties.Add(new RefundPropertyOption
                    {
                        ObjectKind = kind,
                        ObjectId = kind == 3 ? 0 : (first.OwnerId ?? first.ParkingId ?? first.PropertyId ?? 0),
                        PayerName = payerName,
                        // CHG-v1.2.0-34：按缴费人聚合（选定后关联其名下全部账单）
                        PayerOwnerId = first.PayerOwnerId,
                        OwnerName = first.OwnerName ?? "",
                        BuildingNo = primaryProperty == null ? null : primaryProperty.BuildingNo,
                        // CHG-v1.2.0-33：单元随主房产带出（业主-房产关系绑定房产填了单元即有值）
                        UnitNo = primaryProperty == null ? null : primaryProperty.UnitNo,
                        RoomNo = primaryProperty == null ? null : primaryProperty.RoomNo,
                        SpaceNo = spaceBills.Count == 0 ? null : spaceBills[0].SpaceNo,
                        SpaceCount = spaceBills.Select(x => x.SpaceNo).Distinct().Count(),
                        BillCount = g.Count()
                    });
                }
                foreach (var dto in paid.OrderBy(x => x.DueAt).ThenBy(x => x.Id))
                {
                    Bills.Add(new RefundBillRow { Dto = dto });
                }
                // CHG-v1.1.2-40：行内金额提示须在筛选（SelectedProperty 赋值会触发 FilterBills）之前就绪
                RefreshRowMoneyText();
                // 默认仍选第一个真实对象（保持原便利性）；「不指定」作为可选入口供冲正/补收使用
                // CHG-v1.1.2-17：关联对象默认「不指定（全部已缴账单可选）」—— 由用户自己决定是否要选关联对象
                SelectedProperty = Properties.FirstOrDefault();
                RefreshLargeHint();

                Records.Clear();
                var rec = await Api.QueryRefundsAsync(new PageRequest { PageIndex = 1, PageSize = 20 });
                foreach (var dto in rec.Items)
                {
                    // CHG-v1.1.2-41：每行绑定「导出PDF」命令（按单据主键导出，服务端回查金额与账单口径）
                    Records.Add(new RefundRow
                    {
                        Dto = dto,
                        ExportCommand = new AsyncRelayCommand(() => ExportRecordPdfAsync(dto))
                    });
                }
            }, "退款记录已加载");
        }

        private void FilterBills()
        {
            foreach (RefundBillRow row in FilteredBills) { row.PropertyChanged -= OnBillRowChanged; }
            FilteredBills.Clear();
            var filtered = Bills.Where(IsCandidate)
                .Where(x => _selectedProperty == null || IsSameObject(x.Dto, _selectedProperty))
                .OrderBy(x => x.Dto.DueAt).ThenBy(x => x.Dto.Id).ToList();
            foreach (var item in filtered)
            {
                item.IsChecked = false;
                item.PropertyChanged += OnBillRowChanged;
                FilteredBills.Add(item);
            }
            SelectedBill = FilteredBills.FirstOrDefault();
            // 勾选已全部清空 → 金额归 0（由 NotifyBillPickerChanged → SyncAmountWithCheckedBills 统一处理）
            NotifyBillPickerChanged();
        }

        /// <summary>
        /// CHG-v1.1.2-40：候选口径随页签 —— 减免＝有未收余额的账单（调减应收），
        /// 退款/账务调整＝有实缴的账单（冲减实缴）；已冲正账单仅「账务调整」可用于冲正/补收。
        /// </summary>
        private bool IsCandidate(RefundBillRow row)
        {
            if (row == null || row.Dto == null) { return false; }
            if (row.Dto.Status == BillStatus.Draft) { return false; }
            if (_refundType != 2 && row.Dto.Status == BillStatus.Reversed) { return false; }
            return _refundType == 1 ? row.UnreceivedAmount > 0m : row.Dto.PaidAmount > 0m;
        }

        /// <summary>
        /// CHG-v1.1.2-40：行内金额提示同步当前页签口径（退款/调整＝实缴；减免＝未收余额）。
        /// </summary>
        private void RefreshRowMoneyText()
        {
            foreach (RefundBillRow row in Bills)
            {
                if (row.Dto == null) { continue; }
                row.PickerMoneyText = _refundType == 1
                    ? "未收 ¥" + row.UnreceivedAmount.ToString("N2")
                    : "实缴 ¥" + row.Dto.PaidAmount.ToString("N2");
            }
        }

        /// <summary>
        /// 账单是否属于所选关联对象。
        /// CHG-v1.2.0-34：**优先按缴费人（业主）判定** —— 选中一位业主即关联其名下全部账单
        /// （房产 / 车位 / 业主直缴一并命中）；找不到缴费人的账单与自定义缴费对象仍按原对象口径匹配。
        /// </summary>
        private static bool IsSameObject(BillListItemDto bill, RefundPropertyOption option)
        {
            if (bill == null || option == null) { return false; }
            if (option.ObjectKind < 0) { return true; }   // 不指定对象：全部候选
            if (option.PayerOwnerId.HasValue)
            {
                return bill.PayerOwnerId == option.PayerOwnerId;
            }
            switch (option.ObjectKind)
            {
                case 3: return string.Equals((bill.PayerName ?? string.Empty).Trim(), option.PayerName ?? string.Empty,
                            StringComparison.OrdinalIgnoreCase);
                case 2: return bill.OwnerId == option.ObjectId;
                case 1: return bill.ParkingId == option.ObjectId;
                default: return bill.PropertyId == option.ObjectId;
            }
        }

        private void UpdateMethodOptions()
        {
            MethodOptions.Clear();
            switch (_refundType)
            {
                case 0:
                    MethodOptions.Add("原路退回（3-5 个工作日）");
                    MethodOptions.Add("现金");
                    MethodOptions.Add("转账");
                    break;
                case 1:
                    // CHG-v1.1.2-40：减免＝直接调减账单应收，方式收敛为唯一口径（原「冲减当期账单/自动抵扣下期」
                    // 是「减免当退款用」时代的措辞，会让人误以为动的是已收金额）
                    MethodOptions.Add("直接调减账单应收");
                    break;
                default:
                    MethodOptions.Add("调增补收");
                    MethodOptions.Add("调减冲正");
                    break;
            }
            // CHG-v1.1.2-22：切换页签时 MethodOptions 会被 Clear，ComboBox 借此把 SelectedIndex 写回 -1，
            // 于是重新填充后下拉框空白、方向无从确定 —— 这里把 -1 一并归位到第 1 项（调增补收）
            if (MethodIndex < 0 || MethodIndex >= MethodOptions.Count) { MethodIndex = 0; }
            OnPropertyChanged(nameof(DirectionHint));
        }

        public void RefreshLargeHint()
        {
            LargeHint = Amount > 1000m
                // CHG-v1.1.2-45：提示收敛为一行 —— 大额提示展开时也要给下方记录表留出 2 行位置
                ? "大额登记：金额 > ¥1,000，需勾选负责人确认"
                : string.Empty;
            OnPropertyChanged(nameof(HasLargeHint));
        }

        private void PickAttachment()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择附件（≤5MB）",
                Filter = "图片/文档|*.jpg;*.jpeg;*.png;*.pdf;*.doc;*.docx;*.xlsx;*.zip|所有文件|*.*"
            };
            if (dialog.ShowDialog() != true) { return; }
            var info = new FileInfo(dialog.FileName);
            if (info.Length > 5 * 1024 * 1024)
            {
                ErrorText = "附件不能超过 5MB";
                return;
            }
            try
            {
                Directory.CreateDirectory(AttachDir);
                string ext = Path.GetExtension(dialog.FileName);
                string storedName = "refund-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ext;
                string dest = Path.Combine(AttachDir, storedName);
                File.Copy(dialog.FileName, dest, true);
                AttachmentName = Path.GetFileName(dialog.FileName);
                AttachmentPath = dest;
                OnPropertyChanged(nameof(HasAttachment));
                ErrorText = string.Empty;
            }
            catch (Exception ex)
            {
                ErrorText = "附件保存失败：" + ex.Message;
            }
        }

        private async Task SubmitAsync()
        {
            var checkedBills = CheckedBills;
            // CHG-v1.1.2-04（负责人裁定 A）：账务调整不选账单也允许提交（冲正/补收）；
            // 退款与减免仍需关联账单（要冲减某张账单的实缴金额）。
            if (checkedBills.Count == 0 && RefundType != 2)
            {
                ErrorText = "请勾选关联账单（可多选/全选）";
                return;
            }
            if (Amount <= 0)
            {
                ErrorText = "金额必须大于 0";
                return;
            }
            // CHG-v1.1.2-40：减免只冲减「尚未收到」的应收，超出未收余额先在本页给出可读提示
            if (RefundType == 1)
            {
                RefundBillRow over = checkedBills.Where(x => Amount > x.UnreceivedAmount).FirstOrDefault();
                if (over != null)
                {
                    ErrorText = over.NoText + " 未收余额仅 ¥" + over.UnreceivedAmount.ToString("N2") +
                                "，减免金额超出（减免只调减未收部分）";
                    return;
                }
            }
            if (string.IsNullOrWhiteSpace(Reason))
            {
                ErrorText = "原因必填";
                return;
            }
            // CHG-v1.1.0-14：附件改为可选项（原先必传），不再阻断提交
            if (Amount > 1000m && !Confirmed)
            {
                ErrorText = "大额退款/调整需勾选负责人确认";
                return;
            }

            bool batchMode = checkedBills.Count >= 2;
            // CHG-v1.2.0-35：提交结果提示改为在 RunAsync 之后回写 —— RunAsync 收尾会把 StatusText 复位，
            // 原先在动作内赋值等于「提交成功后状态栏没有回执」（看不到申请编号，也看不到归档解锁说明）。
            // CHG-v1.2.0-36：**多选 / 全选提交前先给范围确认** —— 把「本次处理了哪几张账单」摆到用户眼前，
            // 避免误把无关账单一起登记（负责人反馈「收款登记应缴明细多出几条」的根因即误选范围过大）。
            if (batchMode && _pendingAction != 1)
            {
                _pendingAction = 1;
                ConfirmTitle = "确认" + RefundTypeLabel((RefundType)RefundType);
                ConfirmOkText = "确认提交";
                ConfirmMessage =
                    "本次将对 " + checkedBills.Count + " 张账单登记，合计 ¥" +
                    checkedBills.Sum(x => BillCap(x)).ToString("N2") + "（每张按其登记上限核销）：\n" +
                    string.Join("、", checkedBills.Take(6)
                        .Select(x => x.NoText + " ¥" + BillCap(x).ToString("N2"))) +
                    (checkedBills.Count > 6 ? " 等 " + checkedBills.Count + " 张" : string.Empty) +
                    "\n\n确认后：这些账单会按本次金额冲减；未勾选的账单不受影响。" +
                    (RefundType == 0 ? "退款后若该账单仍有未收金额，会回到收款登记应缴明细。" : string.Empty);
                IsConfirmVisible = true;
                return;
            }
            _pendingAction = 0;
            await ExecuteSubmitAsync(checkedBills);
        }

        /// <summary>
        /// CHG-v1.2.0-36：真正提交（抽出来供「多选范围确认」二次进入）。
        /// 口径：只提交用户勾选的账单（服务端按 BillIds / BillId 逐张处理，不涉及未勾选账单）。
        /// </summary>
        private async Task ExecuteSubmitAsync(System.Collections.Generic.List<RefundBillRow> checkedBills)
        {
            bool batchMode = checkedBills.Count >= 2;
            string submitMessage = string.Empty;
            await RunAsync(async () =>
            {
                var request = new RefundAdjustmentRequest
                {
                    // 无账单的账务调整：BillId 传 0（服务端按「无关联账单」落库）
                    BillId = checkedBills.Count > 0 ? checkedBills[0].Dto.Id : 0,
                    BillIds = checkedBills.Select(x => x.Dto.Id).ToList(),
                    RefundType = (RefundType)RefundType,
                    Amount = Amount,
                    Reason = Reason.Trim(),
                    // CHG-v1.1.2-12：处理方式落库（账务调整的 +/− 由方式决定）
                    Method = MethodOptions.Count > MethodIndex && MethodIndex >= 0 ? MethodOptions[MethodIndex] : null,
                    ConfirmedByManager = Confirmed,
                    AttachmentName = AttachmentName,
                    AttachmentPath = AttachmentPath
                };
                if (batchMode)
                {
                    // CHG-v1.2.0-37：批量按**逐张金额**下发 —— 每张按其登记上限核销（退款/调整＝实缴；
                    // 减免＝未收余额），金额框展示的即这些金额的**合计**；各保留账单号与申请编号。
                    request.Items = checkedBills
                        .Select(x => new RefundBatchItemRequest { BillId = x.Dto.Id, Amount = BillCap(x) })
                        .ToList();
                    request.Amount = request.Items.Sum(x => x.Amount);
                    RefundBatchResultDto batch = await Api.CreateRefundBatchAsync(request);
                    submitMessage = DateTime.Now.ToString("HH:mm:ss ") + "已批量提交：" + batch.Count + " 张账单，合计 ¥" +
                        batch.TotalAmount.ToString("N2") +
                        "（逐张按各自登记上限核销；各账单号保持引用；记录表 → 导出PDF 可留档追溯）" +
                        // CHG-v1.2.0-35：让用户知道归档记录已解锁、账单重新可收
                        (batch.ArchiveReleasedCount > 0
                            ? "；其中 " + batch.ArchiveReleasedCount + " 张原「已结清归档」账单已解除归档 → 重新计入收款登记应缴明细"
                            : string.Empty);
                }
                else
                {
                    RefundAdjustmentDto dto = await Api.CreateRefundAsync(request);
                    submitMessage = DateTime.Now.ToString("HH:mm:ss ") + "已提交：" + dto.RefNo +
                        (checkedBills.Count == 0 ? "（无关联账单的冲正/补收，已单独留痕）" : "（已落账留痕）") +
                        "；记录表 → 导出PDF 可留档追溯" +
                        // CHG-v1.2.0-35：归档账单被退款后重新欠费 → 已自动解锁归档，回到应缴明细
                        (dto.ArchiveReleased
                            ? "；该账单原「已结清归档」已解除 → 重新计入收款登记应缴明细"
                            : string.Empty);
                }
                await LoadAsync();
            }, null);

            if (!string.IsNullOrEmpty(submitMessage)) { StatusText = submitMessage; }
        }

        /// <summary>
        /// CHG-v1.2.0-36：删除「关联账单」里勾选的**旧账单**（已结清记录）——归档语义：
        /// 只从本列表移除，账单与收款、退款、财务报表、收支明细流水、业主档案缴费概况**数据不变**；
        /// **未缴费（未结清）账单不可删除**；删除后重新加载 → 「关联对象」的账单笔数同步更新。
        /// </summary>
        private void RequestDeleteBills()
        {
            var checkedRows = CheckedBills;
            if (checkedRows.Count == 0)
            {
                ErrorText = "请先勾选要删除的旧账单（只删除已结清账单；未缴费账单不可删除）";
                return;
            }
            var deletable = checkedRows.Where(x => x.Dto != null && x.Dto.PaidAmount >= x.Dto.Amount).ToList();
            var blocked = checkedRows.Where(x => x.Dto != null && x.Dto.PaidAmount < x.Dto.Amount).ToList();
            if (deletable.Count == 0)
            {
                ErrorText = "所选 " + checkedRows.Count + " 张账单均未结清（仍有未收金额，未缴费账单不能删除）；" +
                            "请改为勾选已结清的旧账单";
                return;
            }

            _pendingAction = 2;
            _pendingBillIds = deletable.Select(x => x.Dto.Id).ToList();
            ConfirmTitle = "删除旧账单";
            ConfirmOkText = "确认删除";
            ConfirmMessage =
                "将从「关联账单」列表移除 " + deletable.Count + " 条已结清旧账单：" +
                string.Join("、", deletable.Take(6).Select(x => x.NoText)) +
                (deletable.Count > 6 ? " 等 " + deletable.Count + " 张" : string.Empty) +
                "\n\n说明：只从本列表移除，账单与收款、退款、财务报表、收支明细流水、业主档案缴费概况均保留；" +
                "「关联对象」的账单笔数会同步更新。" +
                (blocked.Count > 0 ? "\n未结清的 " + blocked.Count + " 张账单不会被删除。" : string.Empty);
            ErrorText = string.Empty;
            IsConfirmVisible = true;
        }

        /// <summary>CHG-v1.2.0-36：确认弹层「确认」——按待确认动作分发。</summary>
        private async Task ConfirmActionAsync()
        {
            int action = _pendingAction;
            var ids = _pendingBillIds;
            IsConfirmVisible = false;
            _pendingAction = 0;
            _pendingBillIds = null;

            if (action == 1)
            {
                await ExecuteSubmitAsync(CheckedBills);
                return;
            }
            if (action != 2 || ids == null || ids.Count == 0) { return; }

            string deleteMessage = string.Empty;
            await RunAsync(async () =>
            {
                BillArchiveResultDto result = await Api.ArchiveSettledBillsAsync(
                    new SettledBillArchiveRequest { BillIds = ids });
                // 重新加载候选与「关联对象」笔数（删除后笔数同步更新）
                await LoadAsync();
                deleteMessage = result == null || string.IsNullOrWhiteSpace(result.Message)
                    ? ("已删除 " + ids.Count + " 条旧账单（仅从本列表移除，账务记录保留）")
                    : result.Message;
                if (result != null && result.SkippedCount > 0 && result.SkippedItems != null)
                {
                    deleteMessage += "；未删除：" + string.Join("；", result.SkippedItems);
                }
            }, null);

            if (!string.IsNullOrEmpty(deleteMessage))
            {
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + deleteMessage;
            }
        }

        /// <summary>CHG-v1.2.0-36：确认弹层「取消」。</summary>
        private void CancelAction()
        {
            IsConfirmVisible = false;
            _pendingAction = 0;
            _pendingBillIds = null;
        }

        /// <summary>
        /// CHG-v1.1.2-41：导出单据 PDF（提交后留档 / 审计追溯）。
        /// 口径：只传单据主键 —— 金额、账单口径（应收/实缴/未收/状态）、缴费对象、经办人由服务端回查，
        /// 保证 PDF 与库内记录一致；用户可另存到任意文件夹。
        /// </summary>
        private async Task ExportRecordPdfAsync(RefundAdjustmentDto dto)
        {
            if (dto == null || dto.Id <= 0)
            {
                ErrorText = "请选择要导出的单据";
                return;
            }

            string typeText = RefundTypeLabel(dto.RefundType);
            string refNo = string.IsNullOrEmpty(dto.RefNo) ? "REF-" + dto.Id.ToString("D4") : dto.RefNo;
            string savedPath = null;
            await RunAsync(async () =>
            {
                ReportLogDto log = await Api.ExportRefundRecordAsync(new RefundRecordExportRequest { RefundId = dto.Id });
                if (log == null || log.Id <= 0)
                {
                    throw new InvalidOperationException("单据导出失败：服务端未生成导出记录");
                }
                var dialog = new SaveFileDialog
                {
                    Title = "导出" + typeText + "单据（PDF）",
                    Filter = "PDF 文件|*.pdf",
                    FileName = "安怡物业-" + typeText + "-" + refNo + ".pdf"
                };
                if (dialog.ShowDialog() != true) { return; }
                await Api.DownloadReportFileAsync(log.Id, dialog.FileName);
                savedPath = dialog.FileName;
            }, null);

            if (!string.IsNullOrEmpty(savedPath))
            {
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + typeText + "单据已导出：" + savedPath;
            }
        }

        /// <summary>单据类型中文名（与导出 PDF 的标题口径一致）。</summary>
        private static string RefundTypeLabel(RefundType type)
        {
            switch (type)
            {
                // 注意：本类有同名实例属性 RefundType，此处须用全限定名，否则被解析成属性
                case PropertyManagement.Contract.Enums.RefundType.Refund: return "退款申请";
                case PropertyManagement.Contract.Enums.RefundType.Discount: return "费用减免";
                default: return "财务调整";
            }
        }
    }
}
