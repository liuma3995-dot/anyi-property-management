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
    /// <summary>欠费台账行（PG-FIN-06，UC-FIN-007，账龄>90 天标红；操作按催缴状态派生 T4F-6-1）。</summary>
    public class ArrearRow : ObservableObject
    {
        private bool _isChecked;
        public ArrearDto Dto { get; set; }
        /// <summary>批量选择标记。</summary>
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }

        public string PropertyNo { get { return Dto.PropertyNo ?? "—"; } }

        public string OwnerName { get { return string.IsNullOrEmpty(Dto.OwnerName) ? "—（空置）" : Dto.OwnerName; } }

        public string ChargeItemName { get { return Dto.ChargeItemName ?? "—"; } }

        public string PeriodText { get { return Dto.DueAt.AddMonths(-1).ToString("yyyy-MM") + " ~ " + Dto.DueAt.ToString("yyyy-MM"); } }

        public string AmountText { get { return "¥" + Dto.ArrearAmount.ToString("N2"); } }

        public string EarliestDueText { get { return Dto.DueAt.ToString("yyyy-MM-dd"); } }

        public string AgingText { get { return Dto.AgingDays + " 天"; } }

        /// <summary>账龄 > 90 天标红（D4-5 验收标准）。</summary>
        public bool IsOverdue { get { return Dto.AgingDays > 90; } }

        public Brush AgingBrush { get { return IsOverdue ? DangerBrush : TextBrush; } }

        public Brush AgingBg { get { return IsOverdue ? DangerBg : TextBg; } }

        public string BuildingText { get { return string.IsNullOrEmpty(Dto.BuildingNo) ? "—" : Dto.BuildingNo; } }

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
                    query = query.Where(x =>
                        (x.PropertyNo ?? string.Empty).Contains(kw) ||
                        (x.OwnerName ?? string.Empty).Contains(kw));
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

        /// <summary>欠费台账删除（软删除：del_flag=1，保留查账轨迹）。</summary>
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
                await Api.DeleteArrearAsync(row.Dto.BillId);
                ConfirmRow = null;
                IsConfirmVisible = false;
                await LoadAsync();
            }, "欠费记录已删除（软删除，保留操作轨迹）");
        }

        private void RequestBatchDelete()
        {
            var rows = Items.Where(x => x.IsChecked).ToList();
            if (rows.Count == 0) { ErrorText = "请先勾选要删除的欠费记录"; return; }
            string desc = string.Join("、", rows.Take(3).Select(r => r.PropertyNo + " " + r.OwnerName));
            if (rows.Count > 3) { desc += " 等 " + rows.Count + " 条"; }
            _batchRows = rows;
            BatchConfirmMessage = "将删除 " + rows.Count + " 条欠费记录（软删除，保留操作轨迹）：\n" + desc;
            IsBatchConfirmVisible = true;
        }

        private async Task ConfirmBatchDeleteAsync()
        {
            var rows = _batchRows;
            IsBatchConfirmVisible = false;
            if (rows == null || rows.Count == 0) { return; }
            await RunAsync(async () =>
            {
                foreach (var r in rows) { await Api.DeleteArrearAsync(r.Dto.BillId); }
                _batchRows = null;
                await LoadAsync();
            }, "已批量删除 " + rows.Count + " 条欠费记录（软删除）");
        }
    }
}
