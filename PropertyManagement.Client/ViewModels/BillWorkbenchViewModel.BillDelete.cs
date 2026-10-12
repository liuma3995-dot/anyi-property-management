using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>
    /// 账单级删除子窗口「缴费对象行」（CHG-v2.0.0-02，负责人 2026-10-11 裁定 1.A/2.A/3.A）。
    /// 一行 = 一条账单：缴费对象（房产楼栋/单元/房号 ｜ 车位编号 ｜ 业主直缴 ｜ 自定义缴费对象）、
    /// 缴费人、账单期间、金额、已缴、状态。
    /// </summary>
    public class BillDeleteRow : ObservableObject
    {
        private bool _isChecked;

        public BillListItemDto Dto { get; set; }

        /// <summary>所属批次（工具栏批量删除时可跨批次，提交时按批次分组）。</summary>
        public int BatchId { get { return Dto == null || !Dto.GenerateBatchId.HasValue ? 0 : Dto.GenerateBatchId.Value; } }

        public string BatchNoText { get; set; }

        public bool IsChecked
        {
            get { return _isChecked; }
            set { SetProperty(ref _isChecked, value); }
        }

        /// <summary>缴费对象：房产 → 楼栋/单元/房号；车位 → 车位编号；业主直缴 / 自定义对象 → 文字标识。</summary>
        public string ObjectText
        {
            get
            {
                if (Dto == null) { return "—"; }
                if (Dto.ParkingId.HasValue && !string.IsNullOrWhiteSpace(Dto.SpaceNo))
                {
                    return "车位 " + Dto.SpaceNo.Trim();
                }
                if (Dto.PropertyId.HasValue)
                {
                    string path = BuildAddress(Dto.BuildingNo, Dto.UnitNo, Dto.RoomNo);
                    if (path.Length > 0) { return path; }
                }
                if (Dto.OwnerId.HasValue)
                {
                    return string.IsNullOrWhiteSpace(Dto.OwnerName) ? "业主直缴" : "业主直缴（" + Dto.OwnerName.Trim() + "）";
                }
                if (!string.IsNullOrWhiteSpace(Dto.PayerName)) { return Dto.PayerName.Trim(); }
                return string.IsNullOrWhiteSpace(Dto.PropertyNo) ? "—" : Dto.PropertyNo.Trim();
            }
        }

        /// <summary>缴费人：业主（房产有效关系 / 车位绑定业主 / 业主直缴）或自定义缴费对象名称。</summary>
        public string PayerText
        {
            get
            {
                if (Dto == null) { return "—"; }
                if (!string.IsNullOrWhiteSpace(Dto.OwnerName)) { return Dto.OwnerName.Trim(); }
                if (!string.IsNullOrWhiteSpace(Dto.PayerName)) { return Dto.PayerName.Trim() + "（自定义）"; }
                return "—";
            }
        }

        /// <summary>
        /// 账单期间显示文本 —— 与批次表同一口径（CHG-v1.1.0-23）：同年省略结束年份、去掉分隔空格
        /// （2026-01-01 ~ 2026-12-31 → 2026-01-01~12-31），避免 150px 列宽下被省略号裁切。
        /// </summary>
        public string PeriodText
        {
            get
            {
                string raw = Dto == null ? null : Dto.CyclePeriod;
                if (string.IsNullOrWhiteSpace(raw) || raw == "—") { return "—"; }
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

        /// <summary>完整账单期间（悬停提示，避免紧凑显示丢失信息）。</summary>
        public string PeriodFullText { get { return Dto == null || string.IsNullOrWhiteSpace(Dto.CyclePeriod) ? "—" : Dto.CyclePeriod; } }

        public decimal Amount { get { return Dto == null ? 0m : Dto.Amount; } }

        public string AmountText { get { return "¥" + Amount.ToString("N2"); } }

        public string PaidText { get { return "¥" + (Dto == null ? 0m : Dto.PaidAmount).ToString("N2"); } }

        /// <summary>是否有已缴金额（二次确认里统计「含已缴/部分缴 N 条」）。</summary>
        public bool HasPaid { get { return Dto != null && Dto.PaidAmount > 0; } }

        public string StatusText
        {
            get
            {
                if (Dto == null) { return "—"; }
                switch (Dto.Status)
                {
                    case BillStatus.Pending: return "待缴";
                    case BillStatus.Partial: return "部分缴纳";
                    case BillStatus.Overdue: return "逾期";
                    case BillStatus.Paid: return "已缴";
                    case BillStatus.Reversed: return "已冲正";
                    default: return "草稿";
                }
            }
        }

        /// <summary>子窗口搜索匹配（缴费对象 / 缴费人 / 批次号 + 组合地址词条）。</summary>
        public bool Matches(string keyword, string normalizedKeyword)
        {
            if (string.IsNullOrEmpty(keyword)) { return true; }
            if ((ObjectText ?? string.Empty).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            if ((PayerText ?? string.Empty).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            if ((BatchNoText ?? string.Empty).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            if (Dto == null) { return false; }
            if (!string.IsNullOrWhiteSpace(Dto.SpaceNo) &&
                Dto.SpaceNo.Trim().IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            if (normalizedKeyword.Length > 0)
            {
                string full = NormalizeAddress(BuildAddress(Dto.BuildingNo, Dto.UnitNo, Dto.RoomNo));
                if (full.Length > 0 && full.IndexOf(normalizedKeyword, StringComparison.Ordinal) >= 0) { return true; }
                string noUnit = NormalizeAddress(BuildAddress(Dto.BuildingNo, null, Dto.RoomNo));
                if (noUnit.Length > 0 && noUnit.IndexOf(normalizedKeyword, StringComparison.Ordinal) >= 0) { return true; }
            }
            return false;
        }

        /// <summary>「楼栋/单元/房号」——单元已含「单元」原样、否则补后缀；缺项整段省略（与 SqlAddress 同口径）。</summary>
        internal static string BuildAddress(string buildingNo, string unitNo, string roomNo)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(buildingNo)) { parts.Add(buildingNo.Trim()); }
            if (!string.IsNullOrWhiteSpace(unitNo))
            {
                string unit = unitNo.Trim();
                parts.Add(unit.EndsWith("单元", StringComparison.Ordinal) ? unit : unit + "单元");
            }
            if (!string.IsNullOrWhiteSpace(roomNo)) { parts.Add(roomNo.Trim()); }
            return string.Join("/", parts);
        }

        /// <summary>组合词条归一化（去分隔符与空白），与房产列表 / 业主-房产关系同一套规则。</summary>
        internal static string NormalizeAddress(string value)
        {
            if (string.IsNullOrEmpty(value)) { return string.Empty; }
            var sb = new StringBuilder(value.Length);
            foreach (char ch in value)
            {
                if (ch == '/' || ch == '\\' || ch == '-' || ch == '－' || ch == '—'
                    || ch == '、' || ch == '·' || char.IsWhiteSpace(ch))
                {
                    continue;
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 账单工作台「账单级删除」子窗口（CHG-v2.0.0-02）：
    /// 行内「删除」→ 打开该批次明细；工具栏「批量删除」→ 打开所选批次（可跨批次）并**默认全选**。
    /// CHG-v2.0.0-10（负责人 2026-10-12）：**移除「按缴费对象勾选」**（子窗口的多选 / 全选已够用）——
    /// 保留搜索、行勾选、表头全选与「清空勾选」。
    /// 提交前二次确认（条数 / 合计金额 / 含已缴-部分缴条数 / 批次范围），
    /// 确认后按批次分组调用 <c>POST billing/bills/delete</c>，物理清空账单及其下游关联数据。
    /// </summary>
    public partial class BillWorkbenchViewModel
    {
        private readonly List<BillDeleteRow> _billDeleteSource = new List<BillDeleteRow>();
        private DispatcherTimer _billDeleteSearchDebounce;
        private bool _isBillDeleteVisible;
        private bool _isBillDeleteConfirmVisible;
        private bool _billDeleteAllChecked;
        private bool _billDeleteMultiBatch;
        private string _billDeleteKeyword = string.Empty;
        private string _billDeleteTitle = string.Empty;
        private string _billDeleteSubTitle = string.Empty;
        private string _billDeleteHint = string.Empty;
        private string _billDeleteSelectedText = string.Empty;
        private string _billDeleteConfirmText = string.Empty;
        private int _billDeleteSourceCount;

        /// <summary>由主构造器调用：建立命令与搜索防抖。</summary>
        private void InitBillDelete()
        {
            _billDeleteSearchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _billDeleteSearchDebounce.Tick += (s, e) =>
            {
                _billDeleteSearchDebounce.Stop();
                ApplyBillDeleteFilter();
            };

            OpenBillDeleteCommand = new AsyncRelayCommand<BillBatchRow>(row => OpenBillDeleteAsync(
                row == null ? new List<BillBatchRow>() : new List<BillBatchRow> { row }, false));
            BatchBillDeleteCommand = new AsyncRelayCommand(OpenBillDeleteForCheckedAsync);
            CloseBillDeleteCommand = new RelayCommand(CloseBillDelete);
            ClearBillDeleteSelectionCommand = new RelayCommand(ClearBillDeleteSelection);
            RequestBillDeleteCommand = new AsyncRelayCommand(RequestBillDeleteConfirmAsync);
            ConfirmBillDeleteCommand = new AsyncRelayCommand(ConfirmBillDeleteAsync);
            CancelBillDeleteConfirmCommand = new RelayCommand(() => IsBillDeleteConfirmVisible = false);
        }

        /// <summary>子窗口是否可见。</summary>
        public bool IsBillDeleteVisible
        {
            get { return _isBillDeleteVisible; }
            private set { SetProperty(ref _isBillDeleteVisible, value); }
        }

        /// <summary>二次确认浮层是否可见。</summary>
        public bool IsBillDeleteConfirmVisible
        {
            get { return _isBillDeleteConfirmVisible; }
            private set { SetProperty(ref _isBillDeleteConfirmVisible, value); }
        }

        /// <summary>是否跨批次（工具栏批量删除且勾了 ≥2 个批次时显示「批次号」列）。</summary>
        public bool IsBillDeleteMultiBatch
        {
            get { return _billDeleteMultiBatch; }
            private set { SetProperty(ref _billDeleteMultiBatch, value); }
        }

        public string BillDeleteTitle { get { return _billDeleteTitle; } private set { SetProperty(ref _billDeleteTitle, value); } }

        public string BillDeleteSubTitle { get { return _billDeleteSubTitle; } private set { SetProperty(ref _billDeleteSubTitle, value); } }

        public string BillDeleteHint { get { return _billDeleteHint; } private set { SetProperty(ref _billDeleteHint, value); } }

        public string BillDeleteSelectedText { get { return _billDeleteSelectedText; } private set { SetProperty(ref _billDeleteSelectedText, value); } }

        public string BillDeleteConfirmText { get { return _billDeleteConfirmText; } private set { SetProperty(ref _billDeleteConfirmText, value); } }

        public string BillDeleteKeyword
        {
            get { return _billDeleteKeyword; }
            set
            {
                if (SetProperty(ref _billDeleteKeyword, value))
                {
                    if (_billDeleteSearchDebounce != null)
                    {
                        _billDeleteSearchDebounce.Stop();
                        _billDeleteSearchDebounce.Start();
                    }
                }
            }
        }

        /// <summary>过滤后（当前可见）的账单行。</summary>
        public ObservableCollection<BillDeleteRow> BillDeleteRows { get; } = new ObservableCollection<BillDeleteRow>();

        public bool IsBillDeleteAllChecked
        {
            get { return _billDeleteAllChecked; }
            set
            {
                if (!SetProperty(ref _billDeleteAllChecked, value)) { return; }
                foreach (BillDeleteRow row in BillDeleteRows) { row.IsChecked = value; }
                RefreshBillDeleteSummary();
            }
        }

        public bool HasBillDeleteSelection { get { return _billDeleteSource.Any(x => x.IsChecked); } }

        public IAsyncRelayCommand<BillBatchRow> OpenBillDeleteCommand { get; private set; }
        public IAsyncRelayCommand BatchBillDeleteCommand { get; private set; }
        public IRelayCommand CloseBillDeleteCommand { get; private set; }
        /// <summary>CHG-v2.0.0-10：清空当前勾选（替代原先随「按缴费对象勾选」引入的入口）。</summary>
        public IRelayCommand ClearBillDeleteSelectionCommand { get; private set; }
        public IAsyncRelayCommand RequestBillDeleteCommand { get; private set; }
        public IAsyncRelayCommand ConfirmBillDeleteCommand { get; private set; }
        public IRelayCommand CancelBillDeleteConfirmCommand { get; private set; }

        /// <summary>打开子窗口的公共入口（单批次 = 行内删除；多批次 = 工具栏批量删除）。</summary>
        private async Task OpenBillDeleteAsync(IList<BillBatchRow> batches, bool checkAll)
        {
            var targets = (batches ?? new List<BillBatchRow>()).Where(x => x != null && x.Dto != null).ToList();
            if (targets.Count == 0)
            {
                ErrorText = "请先勾选要删除的批次";
                return;
            }

            await RunAsync(async () =>
            {
                var rows = new List<BillDeleteRow>();
                foreach (BillBatchRow batch in targets)
                {
                    List<BillListItemDto> bills = await Api.GetBatchBillsAsync(batch.Dto.Id);
                    foreach (BillListItemDto dto in (bills ?? new List<BillListItemDto>())
                        .OrderBy(x => x.BuildingNo).ThenBy(x => x.RoomNo).ThenBy(x => x.Id))
                    {
                        var row = new BillDeleteRow { Dto = dto, BatchNoText = batch.BatchNoText };
                        row.PropertyChanged += OnBillDeleteRowPropertyChanged;
                        rows.Add(row);
                    }
                }

                _billDeleteSource.Clear();
                _billDeleteSource.AddRange(rows);
                _billDeleteSourceCount = rows.Count;
                IsBillDeleteMultiBatch = targets.Select(x => x.Dto.Id).Distinct().Count() > 1;
                IsBillDeleteConfirmVisible = false;
                _billDeleteKeyword = string.Empty;
                OnPropertyChanged(nameof(BillDeleteKeyword));

                ApplyBillDeleteFilter();
                if (checkAll)
                {
                    IsBillDeleteAllChecked = true;
                }

                BillDeleteTitle = "删除账单（账单级清空）";
                BillDeleteSubTitle = BuildBatchScopeText(targets);
                BillDeleteHint = rows.Count == 0
                    ? "该批次当前没有可删除的账单。"
                    : "勾选要清空的账单（可多选 / 全选），确认后该账单及其在收款登记、退款记录、欠费台账、财务报表、收支明细流水、业主档案缴费概况中的关联数据将一并清空。";
                IsBillDeleteVisible = true;
            }, null);
        }

        /// <summary>工具栏「批量删除」：取当前勾选的批次（默认全选其全部账单）。</summary>
        private Task OpenBillDeleteForCheckedAsync()
        {
            var checkedRows = Batches.Where(x => x.IsChecked).ToList();
            if (checkedRows.Count == 0)
            {
                ErrorText = "请先勾选要删除的批次";
                return Task.FromResult(0);
            }
            return OpenBillDeleteAsync(checkedRows, true);
        }

        private static string BuildBatchScopeText(IList<BillBatchRow> targets)
        {
            if (targets.Count == 1)
            {
                BillBatchRow batch = targets[0];
                return "批次 " + batch.BatchNoText + " · " + batch.ChargeItemName + " · " + batch.CyclePeriod +
                       " · 户数 " + batch.HouseCountText + " · 总金额 " + batch.TotalAmountText;
            }
            return "已选 " + targets.Count + " 个批次 · 共 " + targets.Sum(x => x.Dto.HouseCount) + " 户 · 合计 " +
                   "¥" + targets.Sum(x => x.Dto.TotalAmount).ToString("N2");
        }

        private void CloseBillDelete()
        {
            IsBillDeleteVisible = false;
            IsBillDeleteConfirmVisible = false;
            foreach (BillDeleteRow row in _billDeleteSource) { row.PropertyChanged -= OnBillDeleteRowPropertyChanged; }
            _billDeleteSource.Clear();
            BillDeleteRows.Clear();
            _billDeleteSourceCount = 0;
            _billDeleteAllChecked = false;
            OnPropertyChanged(nameof(IsBillDeleteAllChecked));
        }

        /// <summary>CHG-v2.0.0-10：清空当前勾选（替代原先的「按缴费对象勾选」入口）。</summary>
        private void ClearBillDeleteSelection()
        {
            foreach (BillDeleteRow row in _billDeleteSource) { row.IsChecked = false; }
            RefreshBillDeleteSummary();
        }

        private void OnBillDeleteRowPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BillDeleteRow.IsChecked)) { return; }
            RefreshBillDeleteSummary();
        }

        /// <summary>关键词 → 过滤可见行（CHG-v2.0.0-10：按缴费对象勾选的过滤口径已下线）。</summary>
        private void ApplyBillDeleteFilter()
        {
            string keyword = (_billDeleteKeyword ?? string.Empty).Trim();
            string normalized = BillDeleteRow.NormalizeAddress(keyword);

            BillDeleteRows.Clear();
            foreach (BillDeleteRow row in _billDeleteSource)
            {
                if (!row.Matches(keyword, normalized)) { continue; }
                BillDeleteRows.Add(row);
            }
            RefreshBillDeleteSummary();
        }

        /// <summary>页脚汇总 + 表头全选态 + 「删除所选」可用态。</summary>
        private void RefreshBillDeleteSummary()
        {
            var picked = _billDeleteSource.Where(x => x.IsChecked).ToList();
            decimal sum = picked.Sum(x => x.Amount);
            int paid = picked.Count(x => x.HasPaid);
            int batchCount = picked.Select(x => x.BatchId).Distinct().Count();

            BillDeleteSelectedText = picked.Count == 0
                ? "已选 0 条 / 合计 ¥0.00"
                : "已选 " + picked.Count + " 条 / 合计 ¥" + sum.ToString("N2") +
                  "（含已缴·部分缴 " + paid + " 条" + (batchCount > 1 ? "，涉及 " + batchCount + " 个批次" : "") + "）";

            bool all = BillDeleteRows.Count > 0 && BillDeleteRows.All(x => x.IsChecked);
            if (_billDeleteAllChecked != all)
            {
                _billDeleteAllChecked = all;
                OnPropertyChanged(nameof(IsBillDeleteAllChecked));
            }
            OnPropertyChanged(nameof(HasBillDeleteSelection));
        }

        /// <summary>提交前二次确认（裁定 3.A：条数 / 合计 / 含已缴条数 + 建议先导出归档）。</summary>
        private Task RequestBillDeleteConfirmAsync()
        {
            var picked = _billDeleteSource.Where(x => x.IsChecked).ToList();
            if (picked.Count == 0)
            {
                ErrorText = "请先勾选要删除的账单（可按缴费对象勾选，也可点表头全选）";
                return Task.FromResult(0);
            }

            int paid = picked.Count(x => x.HasPaid);
            int batchCount = picked.Select(x => x.BatchId).Distinct().Count();
            BillDeleteConfirmText =
                "将删除 " + picked.Count + " 条账单（合计金额 ¥" + picked.Sum(x => x.Amount).ToString("N2") +
                "，其中已缴 / 部分缴 " + paid + " 条" +
                (batchCount > 1 ? "，涉及 " + batchCount + " 个批次" : "") + "）。\n" +
                "删除后：这些账单，以及它们在收款登记、退款记录、欠费台账、财务报表、收支明细流水、" +
                "业主档案缴费概况中的关联数据将一并清空（物理清空，不可恢复；批次内账单删空则该批次随之清空）。\n" +
                "如需留存，请先导出归档。确认删除？";
            IsBillDeleteConfirmVisible = true;
            return Task.FromResult(0);
        }

        private async Task ConfirmBillDeleteAsync()
        {
            var picked = _billDeleteSource.Where(x => x.IsChecked).ToList();
            IsBillDeleteConfirmVisible = false;
            if (picked.Count == 0) { return; }

            int deletedBills = 0;
            int deletedPayments = 0;
            int deletedRefunds = 0;
            int clearedBatches = 0;

            // 按批次分组提交：一次接口调用只处理一个批次（接口按批次校验归属，防跨批次误删）
            foreach (var group in picked.GroupBy(x => x.BatchId).OrderBy(g => g.Key))
            {
                var ids = group.Select(x => x.Dto.Id).ToList();
                BillBillsDeleteResultDto result = await Api.DeleteBatchBillsAsync(new BillBillsDeleteRequest
                {
                    BatchId = group.Key,
                    BillIds = ids
                });
                if (result == null) { continue; }
                deletedBills += result.Deleted;
                deletedPayments += result.Payments;
                deletedRefunds += result.Refunds;
                if (result.BatchCleared) { clearedBatches++; }
            }

            await RefreshBatchesAsync();
            await RefreshSummaryAsync();
            RaiseDataChanged();
            CloseBillDelete();

            StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已清空 " + deletedBills + " 条账单" +
                (deletedPayments > 0 ? "，连带清除收款 " + deletedPayments + " 条" : string.Empty) +
                (deletedRefunds > 0 ? "、退款/调整 " + deletedRefunds + " 条" : string.Empty) +
                (clearedBatches > 0 ? "；" + clearedBatches + " 个批次已随账单清空" : string.Empty);
        }
    }
}
