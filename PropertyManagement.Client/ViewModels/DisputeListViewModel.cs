using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Dispute;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>纠纷行（PG-DIS-01）：超期红标（IsOverdue）与操作列（已结案=查看，未结案=处理）。</summary>
    public class DisputeRow : ObservableObject
    {
        private bool _isChecked;
        public DisputeCaseDto Dto { get; set; }
        public int CaseId { get { return Dto.Id; } }
        public string CaseNo { get { return Dto.CaseNo ?? ("JF-" + Dto.Id.ToString("D4")); } }
        public string TypeName { get { return Dto.TypeName ?? string.Empty; } }
        public string PartySummary { get { return string.IsNullOrEmpty(Dto.PartySummary) ? "—" : Dto.PartySummary; } }
        public string OccurTime { get { return Dto.OccurTimeText ?? Dto.OccurTime.ToString("MM-dd"); } }
        public string Mediator { get { return Dto.MediatorName ?? "—"; } }
        public string RecordCount { get { return Dto.RecordCount + " 次"; } }
        public string StatusText { get { return string.IsNullOrEmpty(Dto.StatusText) ? "已登记" : Dto.StatusText; } }
        public bool IsOverdue { get { return Dto.IsOverdue; } }
        public bool IsClosed { get { return Dto.Status == DisputeCaseStatus.Closed; } }
        public string ActionText { get { return IsClosed ? "查看" : "处理"; } }
        /// <summary>批量删除勾选态。</summary>
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }
    }

    /// <summary>类型筛选项（「全部」= null，绑定后端 t_dispute_type）。</summary>
    public class DisputeTypeOption
    {
        public int? Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>状态筛选项（含原型「待调解」= Registered）。</summary>
    public class DisputeStatusOption
    {
        public DisputeCaseStatus? Value { get; set; }
        public string Name { get; set; }
    }

    /// <summary>纠纷列表页（PG-DIS-01，UC-DIS-006）：
    /// 统计卡改后端口径（本月新增/环比、调解中/含超期、本月结案、近12个月成功率），
    /// 类型筛选绑 Types（全部=null）、状态筛选含待调解，超期红标，分页条，登记入口，回车搜索防抖。</summary>
    public class DisputeListViewModel : BaseInfoPageViewModel
    {
        private const int PageSize = 20;

        private string _keyword = string.Empty;
        private DisputeTypeOption _selectedType;
        private DisputeStatusOption _selectedStatus;
        private int _pageIndex = 1;
        private int _total;
        private string _totalText = string.Empty;
        private int _monthNew;
        private string _monthNewDeltaText = "环比 —";
        private int _handlingCount;
        private int _overdueCount;
        private int _monthClosed;
        private string _successRateText = "—";
        private string _successRateNote = "近12个月";
        private bool _isBatchMode;
        private bool _isBatchConfirmVisible;
        private string _batchConfirmText = string.Empty;

        public event Action<int> OpenHandle;

        /// <summary>「纠纷登记」按钮：请求外壳跳转纠纷登记页（ShellViewModel 需订阅，见整改报告；未订阅时降级为提示）。</summary>
        public event Action OpenCreate;

        public DisputeListViewModel(IApiClient api) : base(api)
        {
            QueryCommand = new AsyncRelayCommand(SearchAsync);
            HandleCommand = new AsyncRelayCommand<DisputeRow>(r =>
            {
                if (r != null) OpenHandle?.Invoke(r.CaseId);
                return Task.CompletedTask;
            });
            NewCommand = new RelayCommand(OpenCreatePage);
            ExportCommand = new AsyncRelayCommand(ExportAsync);
            PrevPageCommand = new RelayCommand(() => { if (_pageIndex > 1) { _pageIndex--; _ = LoadAsync(); } });
            NextPageCommand = new RelayCommand(() => { if ((long)_pageIndex * PageSize < _total) { _pageIndex++; _ = LoadAsync(); } });
            EnterBatchModeCommand = new RelayCommand(EnterBatchMode);
            ConfirmSelectionCommand = new RelayCommand(RequestBatchConfirm);
            ConfirmBatchDeleteCommand = new AsyncRelayCommand(ConfirmBatchDeleteAsync);
            CancelBatchDeleteCommand = new RelayCommand(ExitBatchMode);

            StatusOptions = new ObservableCollection<DisputeStatusOption>
            {
                new DisputeStatusOption { Value = null, Name = "状态：全部" },
                new DisputeStatusOption { Value = DisputeCaseStatus.Registered, Name = "状态：待调解" },
                new DisputeStatusOption { Value = DisputeCaseStatus.Handling, Name = "状态：调解中" },
                new DisputeStatusOption { Value = DisputeCaseStatus.Closed, Name = "状态：已结案" }
            };
            _selectedStatus = StatusOptions[0];
            _ = LoadAsync();
        }

        public ObservableCollection<DisputeRow> Items { get; } = new ObservableCollection<DisputeRow>();
        public ObservableCollection<DisputeTypeDto> Types { get; } = new ObservableCollection<DisputeTypeDto>();
        public ObservableCollection<DisputeTypeOption> TypeOptions { get; } = new ObservableCollection<DisputeTypeOption>();
        public ObservableCollection<DisputeStatusOption> StatusOptions { get; }

        /// <summary>本月新增（后端口径）。</summary>
        public int MonthNew { get { return _monthNew; } private set { SetProperty(ref _monthNew, value); } }
        /// <summary>本月新增环比子行（如“环比 -2”）。</summary>
        public string MonthNewDeltaText { get { return _monthNewDeltaText; } private set { SetProperty(ref _monthNewDeltaText, value); } }
        public int HandlingCount { get { return _handlingCount; } private set { SetProperty(ref _handlingCount, value); } }
        /// <summary>调解中含超期数（子行，&gt;0 红显）。</summary>
        public int OverdueCount { get { return _overdueCount; } private set { SetProperty(ref _overdueCount, value); } }
        /// <summary>本月结案数（统计卡“已结案”数值）。</summary>
        public int MonthClosed { get { return _monthClosed; } private set { SetProperty(ref _monthClosed, value); } }
        /// <summary>调解成功率（后端近 12 个月口径，仅展示）。</summary>
        public string SuccessRateText { get { return _successRateText; } private set { SetProperty(ref _successRateText, value); } }
        /// <summary>成功率口径标签（“近12个月”）。</summary>
        public string SuccessRateNote { get { return _successRateNote; } private set { SetProperty(ref _successRateNote, value); } }
        public string TotalText { get { return _totalText; } private set { SetProperty(ref _totalText, value); } }

        /// <summary>关键字：输入即自动检索（参考电话查询模块），重置到第 1 页。</summary>
        public string Keyword { get { return _keyword; } set { if (SetProperty(ref _keyword, value)) { _pageIndex = 1; _ = LoadAsync(); } } }

        public DisputeTypeOption SelectedType
        {
            get { return _selectedType; }
            set { if (SetProperty(ref _selectedType, value)) { _pageIndex = 1; _ = LoadAsync(); } }
        }

        public DisputeStatusOption SelectedStatus
        {
            get { return _selectedStatus; }
            set { if (SetProperty(ref _selectedStatus, value)) { _pageIndex = 1; _ = LoadAsync(); } }
        }

        public IAsyncRelayCommand QueryCommand { get; }
        public IRelayCommand NewCommand { get; }
        public IAsyncRelayCommand ExportCommand { get; }
        public IAsyncRelayCommand<DisputeRow> HandleCommand { get; }
        public IRelayCommand PrevPageCommand { get; }
        public IRelayCommand NextPageCommand { get; }
        public IRelayCommand EnterBatchModeCommand { get; }
        public IRelayCommand ConfirmSelectionCommand { get; }
        public IAsyncRelayCommand ConfirmBatchDeleteCommand { get; }
        public IRelayCommand CancelBatchDeleteCommand { get; }

        /// <summary>批量删除模式：激活后才显示勾选列与确认/取消按钮。</summary>
        public bool IsBatchMode
        {
            get { return _isBatchMode; }
            set { if (SetProperty(ref _isBatchMode, value)) OnPropertyChanged(nameof(IsNotBatchMode)); }
        }
        public bool IsNotBatchMode { get { return !_isBatchMode; } }
        public bool HasChecked { get { return Items.Any(x => x.IsChecked); } }
        public bool IsAllChecked
        {
            get { return Items.Count > 0 && Items.All(x => x.IsChecked); }
            set
            {
                foreach (var row in Items) row.IsChecked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasChecked));
                OnPropertyChanged(nameof(CheckedCountText));
            }
        }
        public string CheckedCountText { get { return "已选 " + Items.Count(x => x.IsChecked) + " / " + Items.Count + " 项"; } }
        public bool IsBatchConfirmVisible { get { return _isBatchConfirmVisible; } set { SetProperty(ref _isBatchConfirmVisible, value); } }
        public string BatchConfirmText { get { return _batchConfirmText; } set { SetProperty(ref _batchConfirmText, value); } }

        /// <summary>回车 / 筛选触发：重置到第 1 页。</summary>
        public Task SearchAsync()
        {
            _pageIndex = 1;
            return LoadAsync();
        }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var query = new DisputeQueryRequest { PageIndex = _pageIndex, PageSize = PageSize, Keyword = _keyword };
                if (_selectedType != null && _selectedType.Id.HasValue) query.TypeId = _selectedType.Id.Value;
                if (_selectedStatus != null && _selectedStatus.Value.HasValue) query.Status = _selectedStatus.Value.Value;
                var page = await Api.QueryDisputesAsync(query);
                Items.Clear();
                foreach (var dto in page.Items) Items.Add(WrapRow(dto));
                _total = page.Total;
                int pages = (int)Math.Ceiling(_total / (double)PageSize);
                if (pages < 1) pages = 1;
                TotalText = "共 " + _total + " 条记录 · 第 " + _pageIndex + "/" + pages + " 页";

                if (Types.Count == 0)
                {
                    foreach (var t in await Api.GetDisputeTypesAsync()) Types.Add(t);
                    TypeOptions.Add(new DisputeTypeOption { Id = null, Name = "类型：全部" });
                    foreach (var t in Types) TypeOptions.Add(new DisputeTypeOption { Id = t.Id, Name = "类型：" + t.Name });
                    if (_selectedType == null && TypeOptions.Count > 0)
                    {
                        _selectedType = TypeOptions[0];
                        OnPropertyChanged(nameof(SelectedType));
                    }
                }

                var stat = await Api.GetDisputeStatisticsAsync();
                MonthNew = stat.MonthNew;
                MonthNewDeltaText = "环比 " + (stat.MonthNewDelta >= 0 ? "+" : string.Empty) + stat.MonthNewDelta;
                HandlingCount = stat.Handling;
                OverdueCount = stat.OverdueCount;
                MonthClosed = stat.MonthClosed;
                SuccessRateText = Math.Round(stat.SuccessRate) + "%";
                SuccessRateNote = string.IsNullOrEmpty(stat.SuccessRateNote) ? "近12个月" : stat.SuccessRateNote;
            }, null);
        }

        private DisputeRow WrapRow(DisputeCaseDto dto)
        {
            var row = new DisputeRow { Dto = dto };
            row.PropertyChanged += Row_PropertyChanged;
            return row;
        }

        private void Row_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DisputeRow.IsChecked))
            {
                OnPropertyChanged(nameof(HasChecked));
                OnPropertyChanged(nameof(IsAllChecked));
                OnPropertyChanged(nameof(CheckedCountText));
            }
        }

        /// <summary>「纠纷登记」入口：优先事件交给外壳跳转；外壳未订阅时降级提示（不谎报成功）。</summary>
        private void OpenCreatePage()
        {
            if (OpenCreate != null)
            {
                OpenCreate();
            }
            else
            {
                ErrorText = string.Empty;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "请从左侧导航进入「纠纷调解 → 纠纷登记」";
            }
        }

        /// <summary>导出当前筛选结果为 CSV（后端导出端点未建，前端本地导出降级）。</summary>
        private async Task ExportAsync()
        {
            await RunAsync(async () =>
            {
                var query = new DisputeQueryRequest { PageIndex = 1, PageSize = 1000, Keyword = _keyword };
                if (_selectedType != null && _selectedType.Id.HasValue) query.TypeId = _selectedType.Id.Value;
                if (_selectedStatus != null && _selectedStatus.Value.HasValue) query.Status = _selectedStatus.Value.Value;
                var page = await Api.QueryDisputesAsync(query);

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV 文件|*.csv",
                    FileName = "纠纷列表_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv"
                };
                if (dialog.ShowDialog() != true) return;

                var sb = new StringBuilder();
                sb.AppendLine("编号,类型,当事人（甲方 / 乙方）,登记日期,调解员,调解次数,状态");
                foreach (var dto in page.Items)
                {
                    var row = new DisputeRow { Dto = dto };
                    sb.AppendLine(string.Join(",",
                        Csv(row.CaseNo), Csv(row.TypeName), Csv(dto.PartySummary), Csv(dto.OccurTimeText),
                        Csv(dto.MediatorName), dto.RecordCount.ToString(), Csv(row.StatusText)));
                }
                System.IO.File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已导出 " + page.Items.Count + " 条到 " + dialog.FileName;
            }, null);
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>进入批量删除模式：显示勾选列，清空已勾选。</summary>
        private void EnterBatchMode()
        {
            foreach (var row in Items) row.IsChecked = false;
            ErrorText = string.Empty;
            IsBatchMode = true;
            OnPropertyChanged(nameof(HasChecked));
            OnPropertyChanged(nameof(IsAllChecked));
            OnPropertyChanged(nameof(CheckedCountText));
        }

        /// <summary>退出批量删除模式：隐藏勾选列并清空勾选。</summary>
        private void ExitBatchMode()
        {
            IsBatchMode = false;
            IsBatchConfirmVisible = false;
            foreach (var row in Items) row.IsChecked = false;
            OnPropertyChanged(nameof(HasChecked));
            OnPropertyChanged(nameof(IsAllChecked));
            OnPropertyChanged(nameof(CheckedCountText));
        }

        /// <summary>请求批量删除确认：打开二次确认浮层。</summary>
        private void RequestBatchConfirm()
        {
            int count = Items.Count(x => x.IsChecked);
            if (count == 0) { ErrorText = "请先勾选要删除的纠纷案件"; return; }
            BatchConfirmText = "确认删除已勾选的 " + count + " 个纠纷案件？删除后不再展示，历史数据保留（软删）。";
            IsBatchConfirmVisible = true;
        }

        /// <summary>批量删除：逐个调用删除接口，汇总成功数，完成后刷新列表并退出批量模式。</summary>
        private async Task ConfirmBatchDeleteAsync()
        {
            var ids = Items.Where(x => x.IsChecked).Select(x => x.CaseId).ToList();
            if (ids.Count == 0) { IsBatchConfirmVisible = false; return; }
            int ok = 0;
            var failed = new System.Collections.Generic.List<string>();
            foreach (var id in ids)
            {
                var row = Items.FirstOrDefault(x => x.CaseId == id);
                try { await Api.DeleteDisputeAsync(id); ok++; }
                catch (ApiClientException ex) { failed.Add((row == null ? "案件" : row.CaseNo) + "：" + ex.Message); }
                catch (Exception ex) { failed.Add((row == null ? "案件" : row.CaseNo) + "：" + ex.Message); }
            }
            IsBatchConfirmVisible = false;
            await LoadAsync();
            if (ok > 0) StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已删除 " + ok + " 个纠纷案件" + (failed.Count > 0 ? "，跳过 " + failed.Count + " 个" : string.Empty);
            else StatusText = DateTime.Now.ToString("HH:mm:ss ") + "未删除任何案件";
            if (failed.Count > 0) ErrorText = string.Join("；", failed);
            IsBatchMode = false;
        }
    }
}
