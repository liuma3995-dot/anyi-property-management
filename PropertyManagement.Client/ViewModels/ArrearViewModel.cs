using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>
    /// 已移出欠费台账的记录行（CHG-v1.1.2-03）。
    /// 语义：账单本身仍在（账单工作台/收款登记/退款/报表/流水一切不变），只是不在欠费台账列表；
    /// 点「恢复台账」即让它重新出现在台账里。
    /// </summary>
    public class ArrearDismissRow
    {
        public ArrearDismissDto Dto { get; set; }

        public string PropertyText { get { return string.IsNullOrEmpty(Dto.PropertyNo) ? "—" : Dto.PropertyNo; } }

        public string OwnerText { get { return string.IsNullOrEmpty(Dto.OwnerName) ? "—（空置）" : Dto.OwnerName; } }

        public string ChargeItemText { get { return string.IsNullOrEmpty(Dto.ChargeItemName) ? "—" : Dto.ChargeItemName; } }

        public string AmountText { get { return "¥" + Dto.ArrearAmount.ToString("N2"); } }

        public string ReasonText { get { return string.IsNullOrEmpty(Dto.Reason) ? "—" : Dto.Reason; } }

        public string OperatorText { get { return string.IsNullOrEmpty(Dto.Operator) ? "—" : Dto.Operator; } }

        public string CreatedText { get { return Dto.CreatedAt.ToString("yyyy-MM-dd HH:mm"); } }
    }

    /// <summary>欠费台账行（PG-FIN-06，UC-FIN-007，账龄>90 天标红；操作按催缴状态派生 T4F-6-1）。</summary>
    public class ArrearRow : ObservableObject
    {
        private bool _isChecked;
        public ArrearDto Dto { get; set; }
        /// <summary>批量选择标记。</summary>
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }

        public string PropertyNo { get { return Dto.PropertyNo ?? "—"; } }

        /// <summary>
        /// 业主名。为空时按缴费对象类型区分文案（CHG-v1.2.0-27）：
        /// 房产账单 → 「—（空置）」；车位/业主直缴/自定义缴费对象 → 「—」（不再把广告商等外部对象误标成空置房产）。
        /// </summary>
        public string OwnerName
        {
            get
            {
                if (!string.IsNullOrEmpty(Dto.OwnerName)) { return Dto.OwnerName; }
                return string.Equals(Dto.ObjectKind, "property", StringComparison.Ordinal) ? "—（空置）" : "—";
            }
        }

        public string ChargeItemName { get { return Dto.ChargeItemName ?? "—"; } }

        /// <summary>
        /// 欠费期间 —— CHG-v1.1.2-54：直接引用账单真实账期（与收款登记 / 账单工作台同源）。
        /// 原实现按到期日倒推一个月推算，按月账单看似正确，按年 / 一次性账单会显示错误区间。
        /// 同年账期压缩为「yyyy-MM-dd~MM-dd」，避免台账列宽（120px）把结束日期裁掉。
        /// </summary>
        public string PeriodText
        {
            get
            {
                string start = (Dto.CycleStart ?? string.Empty).Trim();
                string end = (Dto.CycleEnd ?? string.Empty).Trim();
                if (start.Length == 0 && end.Length == 0) { return "—"; }
                if (start.Length == 0) { return end; }
                if (end.Length == 0) { return start; }
                if (start.Length >= 10 && end.Length >= 10 && start.Substring(0, 4) == end.Substring(0, 4))
                {
                    return start + "~" + end.Substring(5);
                }
                return start + "~" + end;
            }
        }

        /// <summary>完整账期（悬停提示，不压缩）。</summary>
        public string PeriodFullText
        {
            get
            {
                string start = (Dto.CycleStart ?? string.Empty).Trim();
                string end = (Dto.CycleEnd ?? string.Empty).Trim();
                if (start.Length == 0 && end.Length == 0) { return "该账单未关联计费周期"; }
                return start + " ~ " + end;
            }
        }

        public string AmountText { get { return "¥" + Dto.ArrearAmount.ToString("N2"); } }

        public string EarliestDueText { get { return Dto.DueAt.ToString("yyyy-MM-dd"); } }

        public string AgingText { get { return Dto.AgingDays + " 天"; } }

        /// <summary>账龄 > 90 天标红（D4-5 验收标准）。</summary>
        public bool IsOverdue { get { return Dto.AgingDays > 90; } }

        public Brush AgingBrush { get { return IsOverdue ? DangerBrush : TextBrush; } }

        public Brush AgingBg { get { return IsOverdue ? DangerBg : TextBg; } }

        /// <summary>
        /// CHG-v1.2.0-24：账单「状态」列 —— 逾期 / 部分缴 / 待缴。
        /// 逾期口径由服务端在查询前统一落库（MarkOverdue：到期日之后**超过 1 天**才算逾期），
        /// 台账页原来没有状态列，负责人反馈「逾期 5 天仍看不到逾期」，现按账单状态如实展示。
        /// </summary>
        public string StatusText
        {
            get
            {
                switch (Dto.Status)
                {
                    case 2: return "逾期";
                    case 1: return "部分缴";
                    default: return "待缴";
                }
            }
        }

        public Brush StatusBrush
        {
            get
            {
                switch (Dto.Status)
                {
                    case 2: return DangerBrush;
                    case 1: return WarnBrush;
                    default: return TextBrush;
                }
            }
        }

        public Brush StatusBg
        {
            get
            {
                switch (Dto.Status)
                {
                    case 2: return DangerBg;
                    case 1: return WarnBg;
                    default: return TextBg;
                }
            }
        }

        public string BuildingText { get { return string.IsNullOrEmpty(Dto.BuildingNo) ? "—" : Dto.BuildingNo; } }

        /// <summary>
        /// CHG-v1.2.0-27：「楼栋/房号」列 —— 房产账单取本房产；车位 / 业主直缴账单按业主-房产关系回查主房产
        /// （口径同「收支明细流水」的「楼栋/房号/单元」列：`1号楼/1单元/101`）。
        /// </summary>
        public string BuildingPathText
        {
            get { return string.IsNullOrEmpty(Dto.BuildingPath) ? "—" : Dto.BuildingPath; }
        }

        /// <summary>催缴状态（T4F-6-1：待催缴/已短信催缴/已电话催缴/已函件催缴/已上门催缴/已微信催缴/免催缴）。</summary>
        public string RemindText
        {
            get
            {
                if (string.IsNullOrEmpty(Dto.RemindChannel)) { return "待催缴"; }
                if (Dto.RemindChannel == "免催缴") { return "免催缴"; }
                return "已" + Dto.RemindChannel + "催缴";
            }
        }

        public Brush RemindBrush { get { return IsFreeRemind ? NeutralBrush : (IsPending ? WarnBrush : OkBrush); } }

        public Brush RemindBg { get { return IsFreeRemind ? NeutralBg : (IsPending ? WarnBg : OkBg); } }

        /// <summary>免催缴（不展示催缴/收款，仅备注）。</summary>
        public bool IsFreeRemind { get { return Dto.RemindChannel == "免催缴"; } }

        public bool IsPending { get { return string.IsNullOrEmpty(Dto.RemindChannel); } }

        // 操作派生（T4F-6-1）：待催缴→催缴/收款；已短信→上门；已函件→法务；免催缴→备注；其余→催缴/收款
        public bool CanRemind { get { return IsPending || Dto.RemindChannel == "电话" || Dto.RemindChannel == "上门" || Dto.RemindChannel == "微信"; } }

        public bool CanVisit { get { return Dto.RemindChannel == "短信"; } }

        public bool CanLegal { get { return Dto.RemindChannel == "函件"; } }

        public bool CanNote { get { return IsFreeRemind; } }

        public bool CanCollect { get { return !IsFreeRemind; } }

        private static readonly Brush DangerBrush = Br("#D64545");
        private static readonly Brush DangerBg = Br("#FDECEC");
        private static readonly Brush TextBrush = Br("#344054");
        private static readonly Brush TextBg = Br("#F1F3F7");
        private static readonly Brush OkBrush = Br("#12805C");
        private static readonly Brush OkBg = Br("#E8F7F1");
        private static readonly Brush WarnBrush = Br("#B76E00");
        private static readonly Brush WarnBg = Br("#FFF5DC");
        private static readonly Brush NeutralBrush = Br("#667085");
        private static readonly Brush NeutralBg = Br("#EEF0F4");

        private static Brush Br(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    /// <summary>欠费台账页（PG-FIN-06，UC-FIN-007：台账 + 催缴渠道留痕 + 楼栋/账龄/催缴状态筛选）。</summary>
    public class ArrearViewModel : FinancePageViewModel
    {
        private string _totalText = "¥0";
        private string _totalSubText = "涉及 0 户";
        private string _bucket1Text = "¥0";
        private string _bucket2Text = "¥0";
        private string _bucket3Text = "¥0";
        private string _bucket3SubText = "0 户";
        private ArrearRow _selectedArrear;
        private bool _isRemindVisible;
        private int _remindChannel;
        private string _remindNote = string.Empty;
        private string _keyword = string.Empty;
        private int _buildingFilter;
        private int _agingFilter;
        private int _remindStateFilter;
        private bool _isConfirmVisible;
        private ArrearRow _confirmRow;
        private bool _isBatchConfirmVisible;
        private string _batchConfirmMessage = string.Empty;
        private List<ArrearRow> _batchRows;
        private bool _isSelectAll;
        private bool _isDismissedVisible;

        private static readonly string[] Channels = { "短信", "电话", "函件", "上门", "微信", "法务", "免催缴" };

        public ArrearViewModel(IApiClient api) : base(api)
        {
            RemindCommand = new RelayCommand<ArrearRow>(row => OpenRemind(row, 0));
            VisitCommand = new RelayCommand<ArrearRow>(row => OpenRemind(row, 3));   // 已短信 → 上门
            LegalCommand = new RelayCommand<ArrearRow>(row => OpenRemind(row, 5));   // 已函件 → 法务
            NoteCommand = new RelayCommand<ArrearRow>(row => OpenRemind(row, 6));    // 免催缴 → 备注
            SubmitRemindCommand = new AsyncRelayCommand(SubmitRemindAsync);
            CloseRemindCommand = new RelayCommand(() => IsRemindVisible = false);
            SearchCommand = new AsyncRelayCommand(LoadAsync);
            DeleteCommand = new RelayCommand<ArrearRow>(RequestDelete);
            ConfirmDeleteCommand = new AsyncRelayCommand(ConfirmDeleteAsync);
            CancelDeleteCommand = new RelayCommand(() => { ConfirmRow = null; IsConfirmVisible = false; });
            BatchDeleteCommand = new RelayCommand(RequestBatchDelete);
            ConfirmBatchDeleteCommand = new AsyncRelayCommand(ConfirmBatchDeleteAsync);
            CancelBatchDeleteCommand = new RelayCommand(() => { IsBatchConfirmVisible = false; _batchRows = null; });
            OpenDismissedCommand = new AsyncRelayCommand(LoadDismissedAsync);
            CloseDismissedCommand = new RelayCommand(() => IsDismissedVisible = false);
            RestoreDismissedCommand = new AsyncRelayCommand<ArrearDismissRow>(RestoreDismissedAsync);
            _ = LoadAsync();
        }

        public ObservableCollection<ArrearRow> Items { get; } = new ObservableCollection<ArrearRow>();

        public ObservableCollection<string> Buildings { get; } = new ObservableCollection<string> { "全部" };

        public string TotalText { get { return _totalText; } private set { SetProperty(ref _totalText, value); } }

        public string TotalSubText { get { return _totalSubText; } private set { SetProperty(ref _totalSubText, value); } }

        public string Bucket1Text { get { return _bucket1Text; } private set { SetProperty(ref _bucket1Text, value); } }

        public string Bucket2Text { get { return _bucket2Text; } private set { SetProperty(ref _bucket2Text, value); } }

        public string Bucket3Text { get { return _bucket3Text; } private set { SetProperty(ref _bucket3Text, value); } }

        public string Bucket3SubText { get { return _bucket3SubText; } private set { SetProperty(ref _bucket3SubText, value); } }

        public bool IsRemindVisible { get { return _isRemindVisible; } private set { SetProperty(ref _isRemindVisible, value); } }

        public bool IsConfirmVisible { get { return _isConfirmVisible; } private set { SetProperty(ref _isConfirmVisible, value); } }

        public ArrearRow ConfirmRow { get { return _confirmRow; } private set { SetProperty(ref _confirmRow, value); } }

        public string Keyword { get { return _keyword; } set { SetProperty(ref _keyword, value); } }

        /// <summary>楼栋筛选（0=全部，T4F-6-1；变更即刷新）。</summary>
        public int BuildingFilter { get { return _buildingFilter; } set { if (SetProperty(ref _buildingFilter, value)) { _ = LoadAsync(); } } }

        /// <summary>账龄筛选（0=全部 1=1-30 2=31-90 3=&gt;90，T4F-6-1；变更即刷新）。</summary>
        public int AgingFilter { get { return _agingFilter; } set { if (SetProperty(ref _agingFilter, value)) { _ = LoadAsync(); } } }

        /// <summary>催缴状态筛选（0=全部 1=待催缴 2=已催缴 3=免催缴，T4F-6-1；变更即刷新）。</summary>
        public int RemindStateFilter { get { return _remindStateFilter; } set { if (SetProperty(ref _remindStateFilter, value)) { _ = LoadAsync(); } } }

        public ArrearRow SelectedArrear { get { return _selectedArrear; } set { SetProperty(ref _selectedArrear, value); } }

        public int RemindChannel { get { return _remindChannel; } set { SetProperty(ref _remindChannel, value); } }

        public string RemindChannelText
        {
            get { return RemindChannel >= 0 && RemindChannel < Channels.Length ? Channels[RemindChannel] : "短信"; }
        }

        public string RemindNote { get { return _remindNote; } set { SetProperty(ref _remindNote, value); } }

        public string RemindTargetText
        {
            get
            {
                return _selectedArrear == null
                    ? string.Empty
                    : _selectedArrear.PropertyNo + " · " + _selectedArrear.OwnerName + " · 欠费 ¥" + _selectedArrear.Dto.ArrearAmount.ToString("N2");
            }
        }

        public IRelayCommand<ArrearRow> RemindCommand { get; }

        public IRelayCommand<ArrearRow> VisitCommand { get; }

        public IRelayCommand<ArrearRow> LegalCommand { get; }

        public IRelayCommand<ArrearRow> NoteCommand { get; }

        public IAsyncRelayCommand SubmitRemindCommand { get; }

        public IRelayCommand CloseRemindCommand { get; }

        public IAsyncRelayCommand SearchCommand { get; }

        public IRelayCommand<ArrearRow> DeleteCommand { get; }
        public IRelayCommand BatchDeleteCommand { get; }

        /// <summary>CHG-v1.1.2-03：打开「已移出台账」列表 / 关闭 / 恢复台账。</summary>
        public IAsyncRelayCommand OpenDismissedCommand { get; private set; }
        public IRelayCommand CloseDismissedCommand { get; private set; }
        public IAsyncRelayCommand<ArrearDismissRow> RestoreDismissedCommand { get; private set; }

        public bool IsDismissedVisible { get { return _isDismissedVisible; } private set { SetProperty(ref _isDismissedVisible, value); } }

        public ObservableCollection<ArrearDismissRow> DismissedItems { get; } = new ObservableCollection<ArrearDismissRow>();

        public IAsyncRelayCommand ConfirmDeleteCommand { get; }

        public IRelayCommand CancelDeleteCommand { get; }
        public IAsyncRelayCommand ConfirmBatchDeleteCommand { get; }
        public IRelayCommand CancelBatchDeleteCommand { get; }

        public bool IsBatchConfirmVisible { get { return _isBatchConfirmVisible; } private set { SetProperty(ref _isBatchConfirmVisible, value); } }
        public string BatchConfirmMessage { get { return _batchConfirmMessage; } private set { SetProperty(ref _batchConfirmMessage, value); } }

        /// <summary>全选：勾选/取消勾选当前列表全部行。</summary>
        public bool IsSelectAll
        {
            get { return _isSelectAll; }
            set
            {
                if (SetProperty(ref _isSelectAll, value))
                {
                    foreach (var r in Items) { r.IsChecked = value; }
                }
            }
        }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var page = await Api.QueryArrearsAsync(new BillQueryRequest { ArrearsOnly = true, PageIndex = 1, PageSize = 200 });
                var source = page.Items.Where(x => x.BillId > 0).ToList();

                // T4R-6：楼栋下拉由全量台账派生（先建选项再做楼栋筛选，避免选项被截断）
                var buildingSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string b in source.Where(x => !string.IsNullOrWhiteSpace(x.BuildingNo)).Select(x => x.BuildingNo.Trim()))
                {
                    buildingSet.Add(b);
                }
                // T4R-6：就地同步楼栋下拉并保留当前选中；不可 Clear()（SelectedIndex 被重置为 -1 会导致选中的楼栋跳空、筛选失效）
                string currentBuilding = BuildingFilter > 0 && BuildingFilter < Buildings.Count ? Buildings[BuildingFilter] : "全部";
                for (int i = Buildings.Count - 1; i >= 0; i--)
                {
                    if (Buildings[i] != "全部" && !buildingSet.Contains(Buildings[i]))
                    {
                        Buildings.RemoveAt(i);
                    }
                }
                if (Buildings.Count == 0) { Buildings.Add("全部"); }
                else if (Buildings[0] != "全部") { Buildings.Insert(0, "全部"); }
                int insertPos = 1;
                foreach (string b in buildingSet)
                {
                    if (!Buildings.Contains(b))
                    {
                        if (insertPos >= Buildings.Count) { Buildings.Add(b); }
                        else { Buildings.Insert(insertPos, b); }
                    }
                    insertPos++;
                }
                int newFilter = string.IsNullOrEmpty(currentBuilding) ? 0 : Buildings.IndexOf(currentBuilding);
                if (newFilter < 0) { newFilter = 0; }
                if (BuildingFilter != newFilter)
                {
                    SetProperty(ref _buildingFilter, newFilter, nameof(BuildingFilter));
                }

                IEnumerable<ArrearDto> query = source;
                if (!string.IsNullOrWhiteSpace(Keyword))
                {
                    string kw = Keyword.Trim();
                    // CHG-v1.2.0-27：搜索范围纳入「楼栋/房号」（可直接搜 1号楼101 找到车位/业主直缴的欠费行）
                    query = query.Where(x =>
                        (x.PropertyNo ?? string.Empty).Contains(kw) ||
                        (x.OwnerName ?? string.Empty).Contains(kw) ||
                        (x.BuildingPath ?? string.Empty).Contains(kw) ||
                        (x.BuildingNo ?? string.Empty).Contains(kw));
                }
                if (BuildingFilter > 0 && BuildingFilter < Buildings.Count)
                {
                    string building = Buildings[BuildingFilter];
                    query = query.Where(x => string.Equals(x.BuildingNo, building, StringComparison.OrdinalIgnoreCase));
                }
                if (AgingFilter == 1) { query = query.Where(x => x.AgingDays <= 30); }
                else if (AgingFilter == 2) { query = query.Where(x => x.AgingDays > 30 && x.AgingDays <= 90); }
                else if (AgingFilter == 3) { query = query.Where(x => x.AgingDays > 90); }
                if (RemindStateFilter == 1) { query = query.Where(x => string.IsNullOrEmpty(x.RemindChannel)); }
                else if (RemindStateFilter == 2) { query = query.Where(x => !string.IsNullOrEmpty(x.RemindChannel) && x.RemindChannel != "免催缴"); }
                else if (RemindStateFilter == 3) { query = query.Where(x => x.RemindChannel == "免催缴"); }
                var list = query.ToList();

                Items.Clear();
                foreach (var dto in list.OrderBy(x => x.AgingDays))
                {
                    Items.Add(new ArrearRow { Dto = dto });
                }
                _isSelectAll = false;
                OnPropertyChanged(nameof(IsSelectAll));

                TotalText = "¥" + list.Sum(x => x.ArrearAmount).ToString("N0");
                TotalSubText = "涉及 " + HouseholdCount(list) + " 户";
                Bucket1Text = "¥" + list.Where(x => x.AgingDays <= 30).Sum(x => x.ArrearAmount).ToString("N0");
                Bucket2Text = "¥" + list.Where(x => x.AgingDays > 30 && x.AgingDays <= 90).Sum(x => x.ArrearAmount).ToString("N0");
                var overdue = list.Where(x => x.AgingDays > 90).ToList();
                Bucket3Text = "¥" + overdue.Sum(x => x.ArrearAmount).ToString("N0");
                Bucket3SubText = HouseholdCount(overdue) + " 户";
            }, "欠费台账已加载");
        }

        /// <summary>涉及户数按房产（楼栋+房号/车位）去重，避免同一房产多笔欠费被重复计入（如 401/402 两户却显示 4 户）。</summary>
        private static int HouseholdCount(IEnumerable<ArrearDto> rows)
        {
            return rows
                .Select(x => string.IsNullOrEmpty(x.BuildingNo) ? (x.PropertyNo ?? string.Empty) : x.BuildingNo + "-" + (x.PropertyNo ?? string.Empty))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        private void OpenRemind(ArrearRow row, int channelIndex)
        {
            if (row == null) { return; }
            SelectedArrear = row;
            RemindChannel = channelIndex;
            RemindNote = string.Empty;
            OnPropertyChanged(nameof(RemindTargetText));
            IsRemindVisible = true;
        }

        private async Task SubmitRemindAsync()
        {
            if (_selectedArrear == null)
            {
                ErrorText = "请选择欠费记录";
                return;
            }

            await RunAsync(async () =>
            {
                await Api.RecordRemindAsync(new ArrearRemindRequest
                {
                    BillId = _selectedArrear.Dto.BillId,
                    Channel = RemindChannelText,
                    Note = RemindNote.Trim()
                });
                IsRemindVisible = false;
                await LoadAsync();
            }, "催缴记录已生成（渠道：" + RemindChannelText + "）");
        }

        /// <summary>
        /// 移出台账（CHG-v1.1.2-03，负责人 2026-09-19 裁定 A）：
        /// 只把该行移出欠费台账，不再软删账单 —— 账单工作台/收款登记/退款/报表/流水/业主档案一律不受影响。
        /// </summary>
        private void RequestDelete(ArrearRow row)
        {
            if (row == null) { return; }
            ConfirmRow = row;
            IsConfirmVisible = true;
        }

        private async Task ConfirmDeleteAsync()
        {
            ArrearRow row = ConfirmRow;
            if (row == null) { return; }
            await RunAsync(async () =>
            {
                await Api.DismissArrearsAsync(new ArrearDismissRequest
                {
                    BillIds = new List<int> { row.Dto.BillId },
                    Reason = "台账移出"
                });
                ConfirmRow = null;
                IsConfirmVisible = false;
                await LoadAsync();
            }, "已移出台账（账单与其它模块数据不变，可在「已移出记录」中恢复）");
        }

        private void RequestBatchDelete()
        {
            var rows = Items.Where(x => x.IsChecked).ToList();
            if (rows.Count == 0) { ErrorText = "请先勾选要移出台账的欠费记录"; return; }
            string desc = string.Join("、", rows.Take(3).Select(r => r.PropertyNo + " " + r.OwnerName));
            if (rows.Count > 3) { desc += " 等 " + rows.Count + " 条"; }
            _batchRows = rows;
            BatchConfirmMessage = "将把 " + rows.Count + " 条欠费记录移出台账：\n" + desc +
                "\n\n只影响欠费台账的显示，账单、收款登记、退款记录、财务报表与收支流水都不受影响，可随时恢复。";
            IsBatchConfirmVisible = true;
        }

        private async Task ConfirmBatchDeleteAsync()
        {
            var rows = _batchRows;
            IsBatchConfirmVisible = false;
            if (rows == null || rows.Count == 0) { return; }
            await RunAsync(async () =>
            {
                await Api.DismissArrearsAsync(new ArrearDismissRequest
                {
                    BillIds = rows.Select(r => r.Dto.BillId).ToList(),
                    Reason = "台账批量移出"
                });
                _batchRows = null;
                await LoadAsync();
            }, "已移出 " + rows.Count + " 条欠费记录（账单一律保持不变，可恢复）");
        }

        /// <summary>CHG-v1.1.2-03：打开「已移出台账」列表。</summary>
        private async Task LoadDismissedAsync()
        {
            await RunAsync(async () =>
            {
                List<ArrearDismissDto> items = await Api.QueryDismissedArrearsAsync();
                DismissedItems.Clear();
                foreach (ArrearDismissDto dto in items ?? new List<ArrearDismissDto>())
                {
                    DismissedItems.Add(new ArrearDismissRow { Dto = dto });
                }
                IsDismissedVisible = true;
            }, null);
        }

        /// <summary>CHG-v1.1.2-03：恢复台账（删除剔除记录，账单重新回到台账列表）。</summary>
        private async Task RestoreDismissedAsync(ArrearDismissRow row)
        {
            if (row == null) { return; }
            await RunAsync(async () =>
            {
                await Api.RestoreArrearsAsync(new ArrearDismissRequest { DismissIds = new List<int> { row.Dto.Id } });
                DismissedItems.Remove(row);
                await LoadAsync();
                if (DismissedItems.Count == 0) { IsDismissedVisible = false; }
            }, "已恢复台账：" + row.OwnerText + " " + row.AmountText);
        }
    }
}
