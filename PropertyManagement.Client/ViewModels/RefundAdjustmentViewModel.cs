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

        public string SignText
        {
            get { return Dto.RefundType == RefundType.Adjustment ? "+¥" : "-¥"; }
        }

        public string NoText { get { return string.IsNullOrEmpty(Dto.RefNo) ? "REF-" + Dto.Id.ToString("D4") : Dto.RefNo; } }

        public string AmountText { get { return SignText + Dto.Amount.ToString("N2"); } }

        public string PropertyNoText { get { return string.IsNullOrEmpty(Dto.PropertyNo) ? "—" : Dto.PropertyNo; } }

        public string Reason { get { return string.IsNullOrEmpty(Dto.Reason) ? "—" : Dto.Reason; } }

        public string DateText { get { return Dto.CreatedAt.ToString("yyyy-MM-dd"); } }

        /// <summary>节点流仅作状态展示（负责人早期决策：不落地审批流）。</summary>
        public string StatusText { get { return "已完成"; } }

        public string NodeText { get { return "已归档"; } }

        public string AttachmentText { get { return string.IsNullOrEmpty(Dto.AttachmentName) ? "—" : Dto.AttachmentName; } }

        private static Brush Br(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    /// <summary>关联房产选项（PG-FIN-04）。</summary>
    public class RefundPropertyOption
    {
        public int PropertyId { get; set; }

        public string DisplayText { get; set; }
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
        private string _formTitle = "退款申请（UC-FIN-004）";
        private string _amountLabel = "退款金额";

        public RefundAdjustmentViewModel(IApiClient api) : base(api)
        {
            SubmitCommand = new AsyncRelayCommand(SubmitAsync);
            PickAttachmentCommand = new RelayCommand(PickAttachment);
            SaveDraftCommand = new RelayCommand(SaveDraft);
            UpdateMethodOptions();
            _ = LoadAsync();
        }

        public ObservableCollection<RefundPropertyOption> Properties { get; } = new ObservableCollection<RefundPropertyOption>();

        public ObservableCollection<PaymentBillRow> Bills { get; } = new ObservableCollection<PaymentBillRow>();

        public ObservableCollection<PaymentBillRow> FilteredBills { get; } = new ObservableCollection<PaymentBillRow>();

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
                    if (value != null)
                    {
                        Amount = value.Dto.PaidAmount;
                    }
                }
            }
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
                        case 0: FormTitle = "退款申请（UC-FIN-004）"; AmountLabel = "退款金额"; break;
                        case 1: FormTitle = "费用减免（UC-FIN-004）"; AmountLabel = "减免金额"; break;
                        default: FormTitle = "账务调整（UC-FIN-004）"; AmountLabel = "调整金额"; break;
                    }
                    RefreshLargeHint();
                }
            }
        }

        public int MethodIndex
        {
            get { return _methodIndex; }
            set { SetProperty(ref _methodIndex, value); }
        }

        public decimal Amount
        {
            get { return _amount; }
            set
            {
                if (SetProperty(ref _amount, value))
                {
                    RefreshLargeHint();
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

        public IRelayCommand SaveDraftCommand { get; }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                Bills.Clear();
                Properties.Clear();
                var page = await Api.QueryBillsAsync(new BillQueryRequest { PageSize = 200 });
                var paid = page.Items.Where(x => x.PaidAmount > 0).ToList();
                foreach (var g in paid.Where(x => x.PropertyId.HasValue).GroupBy(x => x.PropertyId.Value).OrderBy(g => g.First().PropertyNo))
                {
                    var first = g.First();
                    Properties.Add(new RefundPropertyOption
                    {
                        PropertyId = g.Key,
                        DisplayText = (first.PropertyNo ?? ("房产-" + g.Key)) + " · " + (first.OwnerName ?? "")
                    });
                }
                foreach (var dto in paid.OrderBy(x => x.DueAt).ThenBy(x => x.Id))
                {
                    Bills.Add(new PaymentBillRow { Dto = dto });
                }
                SelectedProperty = Properties.FirstOrDefault();
                RefreshLargeHint();

                Records.Clear();
                var rec = await Api.QueryRefundsAsync(new PageRequest { PageIndex = 1, PageSize = 20 });
                foreach (var dto in rec.Items)
                {
                    Records.Add(new RefundRow { Dto = dto });
                }
            }, "退款记录已加载");
        }

        private void FilterBills()
        {
            FilteredBills.Clear();
            var filtered = Bills.Where(x => _selectedProperty == null || x.Dto.PropertyId == _selectedProperty.PropertyId)
                .OrderBy(x => x.Dto.DueAt).ThenBy(x => x.Dto.Id).ToList();
            foreach (var item in filtered)
            {
                FilteredBills.Add(item);
            }
            SelectedBill = FilteredBills.FirstOrDefault();
            if (SelectedBill == null)
            {
                Amount = 0m;
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
                    MethodOptions.Add("冲减当期账单");
                    MethodOptions.Add("自动抵扣下期");
                    break;
                default:
                    MethodOptions.Add("调增补收");
                    MethodOptions.Add("调减冲正");
                    break;
            }
            if (MethodIndex >= MethodOptions.Count) { MethodIndex = 0; }
        }

        public void RefreshLargeHint()
        {
            LargeHint = Amount > 1000m
                ? "金额超过 ¥1,000 属大额退款/调整，需勾选负责人确认（BR-FIN-10）"
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
                ErrorText = "附件不能超过 5MB（PG-FIN-04）";
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

        private void SaveDraft()
        {
            Reason = string.Empty;
            Amount = 0m;
            Confirmed = false;
            AttachmentName = string.Empty;
            AttachmentPath = string.Empty;
            OnPropertyChanged(nameof(HasAttachment));
            StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已暂存（草稿未提交）";
        }

        private async Task SubmitAsync()
        {
            if (_selectedBill == null)
            {
                ErrorText = "请选择已缴账单";
                return;
            }
            if (Amount <= 0)
            {
                ErrorText = "金额必须大于 0";
                return;
            }
            if (string.IsNullOrWhiteSpace(Reason))
            {
                ErrorText = "原因必填（UC-FIN-004）";
                return;
            }
            if (!HasAttachment)
            {
                ErrorText = "附件必传（≤5MB，PG-FIN-04）";
                return;
            }
            if (Amount > 1000m && !Confirmed)
            {
                ErrorText = "大额退款/调整需勾选负责人确认（BR-FIN-10）";
                return;
            }

            await RunAsync(async () =>
            {
                var dto = await Api.CreateRefundAsync(new RefundAdjustmentRequest
                {
                    BillId = _selectedBill.Dto.Id,
                    RefundType = (RefundType)RefundType,
                    Amount = Amount,
                    Reason = Reason.Trim(),
                    ConfirmedByManager = Confirmed,
                    AttachmentName = AttachmentName,
                    AttachmentPath = AttachmentPath
                });
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已提交：" + dto.RefNo + "（冲正留痕）";
                await LoadAsync();
            }, null);
        }
    }
}