using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Common;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>审计日志行（PG-COM-03 八列：时间/操作人/角色/模块/动作/对象/结果/IP 地址）。</summary>
    public class AuditLogRow : ObservableObject
    {
        private static readonly Brush SuccessFg = BrushHelper.FromHex("#12805C");
        private static readonly Brush WarningFg = BrushHelper.FromHex("#B76E00");
        private static readonly Brush DangerFg = BrushHelper.FromHex("#D64545");

        public AuditLogDto Dto { get; set; }

        private bool _isSelected;

        /// <summary>R13：勾选框选中态（批量删除依据）。</summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }
        public string Time { get { return Dto.CreatedAt == default(DateTime) ? "—" : Dto.CreatedAt.ToString("MM-dd HH:mm:ss"); } }
        public string UserName { get { return string.IsNullOrEmpty(Dto.UserName) ? "—" : Dto.UserName; } }
        public string Role { get { return string.IsNullOrEmpty(Dto.Role) ? "—" : Dto.Role; } }
        public string Module { get { return string.IsNullOrEmpty(Dto.Module) ? "—" : Dto.Module; } }
        public string Action { get { return Dto.Action ?? string.Empty; } }
        /// <summary>对象：TargetType:TargetId（详情经 Tooltip 展示）。</summary>
        public string Target
        {
            get
            {
                string type = Dto.TargetType ?? string.Empty;
                string id = Dto.TargetId ?? string.Empty;
                if (type.Length == 0 && id.Length == 0) { return "—"; }
                if (type.Length == 0) { return id; }
                if (id.Length == 0) { return type; }
                return type + ":" + id;
            }
        }
        public string Detail { get { return Dto.Detail ?? string.Empty; } }
        public string ResultText { get { return string.IsNullOrEmpty(Dto.Result) ? "—" : Dto.Result; } }
        public string ResultBg
        {
            get
            {
                string r = Dto.Result ?? string.Empty;
                if (r.Contains("部分失败")) { return "#FFF5DC"; }
                if (r.Contains("失败")) { return "#FDECEC"; }
                return "#E8F7F1";
            }
        }
        public Brush ResultBrush
        {
            get
            {
                string r = Dto.Result ?? string.Empty;
                if (r.Contains("部分失败")) { return WarningFg; }
                if (r.Contains("失败")) { return DangerFg; }
                return SuccessFg;
            }
        }
        public string IpAddr { get { return string.IsNullOrEmpty(Dto.IpAddr) ? "—" : Dto.IpAddr; } }

        private static class BrushHelper
        {
            internal static Brush FromHex(string hex)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                return brush;
            }
        }
    }

    /// <summary>分页按钮（PG-COM-03：页码 1/2/3…，当前页主色高亮）。</summary>
    public class AuditPageButton
    {
        public int Number { get; set; }
        public string Text { get { return Number.ToString(); } }
        public bool IsCurrent { get; set; }
        public Brush Background { get { return IsCurrent ? _primary : _surface; } }
        public Brush BorderBrush { get { return IsCurrent ? _primary : _border; } }
        public Brush Foreground { get { return IsCurrent ? Brushes.White : _textSecondary; } }

        private static readonly Brush _primary = FromHex("#2B7DE9");
        private static readonly Brush _surface = Brushes.White;
        private static readonly Brush _border = FromHex("#E4EAF2");
        private static readonly Brush _textSecondary = FromHex("#667085");

        private static Brush FromHex(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>审计日志查询（PG-COM-03，UC-COM-003，BR-COM-01）。
    /// 工具栏五控件（时间范围/操作人/模块/动作/关键字）+ 导出；8 列只读表格；页码分页；
    /// 导出动作本身由服务端写 t_export_log + AUDIT_EXPORT 审计。页面本身无编辑/删除操作。</summary>
    public class AuditLogViewModel : BaseInfoPageViewModel
    {
        private const int PageSize = 20;

        /// <summary>时间范围快捷项（0=全部时间 … 5=自定义范围）。</summary>
        private static readonly string[] TimeRangeNames =
        {
            "全部时间", "今天", "近 7 天", "近 30 天", "本月", "自定义范围"
        };

        /// <summary>模块下拉：索引 0 = 全部（不过滤），其余与服务端 module 字段一致。</summary>
        private static readonly string[] ModuleNames =
        {
            "系统设置", "系统认证", "财务收费", "财务支出", "报表导出",
            "基础信息", "应急处置", "纠纷调解", "设备台账", "人员组织", "便民电话簿"
        };

        private int _timeRangeIndex;
        private DateTime? _customFrom;
        private DateTime? _customTo;
        private string _operator = "全部";
        private string _action = "全部";
        private string _keyword = string.Empty;
        private int _moduleIndex;
        private int _pageIndex = 1;
        private int _total;
        private string _totalText = "共 0 条记录 · 第 1/1 页";
        private CancellationTokenSource _debounceCts;
        private bool _isConfirmVisible;
        private bool _isSelectionMode;        // R14：选择模式（点击「批量删除」后才显示勾选框列）
        private string _confirmTitle = "确认操作";
        private string _confirmMessage = string.Empty;

        public AuditLogViewModel(IApiClient api) : base(api)
        {
            GoPageCommand = new AsyncRelayCommand<AuditPageButton>(GoPageAsync);
            PrevPageCommand = new AsyncRelayCommand(PrevPageAsync);
            NextPageCommand = new AsyncRelayCommand(NextPageAsync);
            ExportCommand = new AsyncRelayCommand(ExportAsync);
            BatchDeleteCommand = new RelayCommand(RequestBatchDelete);
            CancelSelectionCommand = new RelayCommand(ExitSelectionMode);
            ConfirmDeleteCommand = new AsyncRelayCommand(ConfirmDeleteAsync);
            CancelConfirmCommand = new RelayCommand(() => IsConfirmVisible = false);
            _ = LoadAsync();
        }

        public ObservableCollection<AuditLogRow> Items { get; } = new ObservableCollection<AuditLogRow>();
        public ObservableCollection<AuditPageButton> Pages { get; } = new ObservableCollection<AuditPageButton>();

        /// <summary>时间范围下拉项（全部时间/今天/近 7 天/近 30 天/本月/自定义范围）。</summary>
        public List<string> TimeRanges { get { return new List<string>(TimeRangeNames); } }

        /// <summary>操作人下拉项：全部 + 当前结果集中出现的操作人（可编辑，支持直接输入模糊查询）。</summary>
        public ObservableCollection<string> OperatorOptions { get; } = new ObservableCollection<string> { "全部" };

        /// <summary>动作下拉项：全部 + 当前结果集中出现的动作（可编辑，支持直接输入模糊查询）。</summary>
        public ObservableCollection<string> ActionOptions { get; } = new ObservableCollection<string> { "全部" };

        /// <summary>模块下拉项：全部 + 服务端模块名。</summary>
        public ObservableCollection<string> ModuleOptions { get; } = new ObservableCollection<string> { "全部" };

        public int TimeRangeIndex
        {
            get { return _timeRangeIndex; }
            set
            {
                if (SetProperty(ref _timeRangeIndex, value))
                {
                    OnPropertyChanged(nameof(IsCustomRange));
                    _pageIndex = 1;
                    _ = LoadAsync();
                }
            }
        }

        /// <summary>自定义范围（仅时间范围=自定义范围时展示两个日期选择）。</summary>
        public bool IsCustomRange { get { return _timeRangeIndex == TimeRangeNames.Length - 1; } }

        public DateTime? CustomFrom
        {
            get { return _customFrom; }
            set { if (SetProperty(ref _customFrom, value)) { _pageIndex = 1; _ = LoadAsync(); } }
        }

        public DateTime? CustomTo
        {
            get { return _customTo; }
            set { if (SetProperty(ref _customTo, value)) { _pageIndex = 1; _ = LoadAsync(); } }
        }

        public string Operator
        {
            get { return _operator; }
            set { if (SetProperty(ref _operator, value)) { ScheduleDebouncedReload(); } }
        }

        public string Action
        {
            get { return _action; }
            set { if (SetProperty(ref _action, value)) { ScheduleDebouncedReload(); } }
        }

        public string Keyword
        {
            get { return _keyword; }
            set { if (SetProperty(ref _keyword, value)) { ScheduleDebouncedReload(); } }
        }

        /// <summary>模块下拉索引（0=全部）。</summary>
        public int ModuleIndex
        {
            get { return _moduleIndex; }
            set { if (SetProperty(ref _moduleIndex, value)) { _pageIndex = 1; _ = LoadAsync(); } }
        }

        public int PageIndex
        {
            get { return _pageIndex; }
            private set { SetProperty(ref _pageIndex, value); }
        }

        public int Total
        {
            get { return _total; }
            private set { SetProperty(ref _total, value); }
        }

        /// <summary>分页条文案：共 N 条记录 · 第 x/y 页（原型 PG-COM-03）。</summary>
        public string TotalText
        {
            get { return _totalText; }
            private set { SetProperty(ref _totalText, value); }
        }

        public IAsyncRelayCommand<AuditPageButton> GoPageCommand { get; }
        public IAsyncRelayCommand PrevPageCommand { get; }
        public IAsyncRelayCommand NextPageCommand { get; }
        public IAsyncRelayCommand ExportCommand { get; }
        public IRelayCommand BatchDeleteCommand { get; }
        public IRelayCommand CancelSelectionCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteCommand { get; }
        public IRelayCommand CancelConfirmCommand { get; }

        /// <summary>R13：全选/取消全选（表头勾选框双向绑定）。</summary>
        public bool IsAllItemsSelected
        {
            get { return Items.Count > 0 && Items.All(i => i.IsSelected); }
            set
            {
                foreach (AuditLogRow row in Items) { row.IsSelected = value; }
                OnPropertyChanged(nameof(IsAllItemsSelected));
            }
        }

        /// <summary>刷新全选态（勾选/取消单行后由视图调用）。</summary>
        public void RefreshSelectAllState()
        {
            OnPropertyChanged(nameof(IsAllItemsSelected));
        }

        /// <summary>
        /// R14：选择模式。默认 false（日志列表不显示勾选框列）；点击「批量删除」后进入选择模式，
        /// 勾选框列显示且按钮文案变为「删除所选」；取消或删除完成后退出并清空勾选。
        /// </summary>
        public bool IsSelectionMode
        {
            get { return _isSelectionMode; }
            private set
            {
                if (SetProperty(ref _isSelectionMode, value))
                {
                    OnPropertyChanged(nameof(BatchDeleteButtonText));
                }
            }
        }

        public string BatchDeleteButtonText
        {
            get { return IsSelectionMode ? "删除所选" : "批量删除"; }
        }

        public bool IsConfirmVisible
        {
            get { return _isConfirmVisible; }
            set { SetProperty(ref _isConfirmVisible, value); }
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

        private int PageCount
        {
            get { return Math.Max(1, (Total + PageSize - 1) / PageSize); }
        }

        public async Task LoadAsync()
        {
            if (CustomFrom.HasValue && CustomTo.HasValue && CustomFrom.Value > CustomTo.Value)
            {
                ErrorText = "时间范围无效：开始日期不能晚于结束日期";
                return;
            }

            await RunAsync(async () =>
            {
                PageResult<AuditLogDto> page = await Api.QueryAuditLogsAsync(BuildRequest());
                Items.Clear();
                foreach (var dto in page.Items)
                {
                    Items.Add(new AuditLogRow { Dto = dto });
                }
                Total = page.Total;
                PageIndex = page.PageIndex > 0 ? page.PageIndex : 1;
                TotalText = "共 " + Total + " 条记录 · 第 " + PageIndex + "/" + PageCount + " 页";
                RefreshFilterOptions(page.Items);
                RebuildPages();
            }, null);
        }

        /// <summary>操作人/动作下拉候选随结果集刷新（首项恒为「全部」，保留当前选择）。</summary>
        private void RefreshFilterOptions(List<AuditLogDto> rows)
        {
            FillOptions(OperatorOptions, rows, d => d.UserName, Operator);
            FillOptions(ActionOptions, rows, d => d.Action, Action);
        }

        private static void FillOptions(ObservableCollection<string> target, List<AuditLogDto> rows,
            Func<AuditLogDto, string> selector, string current)
        {
            if (rows != null)
            {
                foreach (AuditLogDto row in rows)
                {
                    string value = selector(row);
                    if (!string.IsNullOrWhiteSpace(value) && !target.Contains(value))
                    {
                        target.Add(value);
                    }
                }
            }
            if (!string.IsNullOrEmpty(current) && !target.Contains(current))
            {
                target.Add(current);
            }
        }

        private void RebuildPages()
        {
            Pages.Clear();
            int count = PageCount;
            int start = 1;
            const int window = 7;
            if (count > window)
            {
                start = Math.Max(1, Math.Min(PageIndex - window / 2, count - window + 1));
            }
            int end = Math.Min(count, start + window - 1);
            for (int number = start; number <= end; number++)
            {
                Pages.Add(new AuditPageButton { Number = number, IsCurrent = number == PageIndex });
            }
        }

        private AuditLogQueryRequest BuildRequest()
        {
            DateTime? from;
            DateTime? to;
            ResolveRange(out from, out to);

            return new AuditLogQueryRequest
            {
                PageIndex = PageIndex <= 0 ? 1 : PageIndex,
                PageSize = PageSize,
                From = from,
                To = to,
                Operator = NullIfAll(Operator),
                Module = ModuleIndex > 0 && ModuleIndex <= ModuleNames.Length ? ModuleNames[ModuleIndex - 1] : null,
                Action = NullIfAll(Action),
                Keyword = NullIfEmpty(Keyword)
            };
        }

        /// <summary>时间范围快捷项 → 起止日期（服务端 To 按含当日处理）。</summary>
        private void ResolveRange(out DateTime? from, out DateTime? to)
        {
            DateTime today = DateTime.Today;
            switch (TimeRangeIndex)
            {
                case 1:
                    from = today; to = today; return;
                case 2:
                    from = today.AddDays(-6); to = today; return;
                case 3:
                    from = today.AddDays(-29); to = today; return;
                case 4:
                    from = new DateTime(today.Year, today.Month, 1); to = today; return;
                case 5:
                    from = CustomFrom; to = CustomTo; return;
                default:
                    from = null; to = null; return;
            }
        }

        private async Task GoPageAsync(AuditPageButton button)
        {
            if (button == null || button.Number == PageIndex) { return; }
            PageIndex = button.Number;
            await LoadAsync();
        }

        /// <summary>
        /// 批量删除审计日志（R13）：勾选后二次确认 → 服务端软删留痕（记录不再出现在列表/导出），
        /// 删除动作本身写入 AUDIT_LOG_DELETE 审计；物理清理由「备份与恢复 → 一键清理残余数据」执行。
        /// </summary>
        private void RequestBatchDelete()
        {
            if (!IsSelectionMode)
            {
                EnterSelectionMode();
                return;
            }

            int count = Items.Count(i => i.IsSelected);
            if (count == 0)
            {
                ErrorText = "请先勾选要删除的记录（可勾选单条，也可勾选表头全选），或点击【取消】退出批量删除";
                return;
            }
            ConfirmTitle = "批量删除审计日志";
            ConfirmMessage = "将删除所选 " + count + " 条审计日志（记录留痕，不再出现在查询与导出中）。\n" +
                             "如需彻底清理留痕数据，请到「系统设置 / 备份与恢复」执行【一键清理残余数据】。\n确认删除？";
            IsConfirmVisible = true;
        }

        private async Task ConfirmDeleteAsync()
        {
            var ids = Items.Where(i => i.IsSelected).Select(i => i.Dto.Id).Distinct().ToList();
            IsConfirmVisible = false;
            if (ids.Count == 0) { return; }
            string message = null;
            await RunAsync(async () =>
            {
                RecordBatchDeleteResultDto result = await Api.BatchDeleteAuditLogsAsync(
                    new RecordBatchDeleteRequest { Ids = ids });
                message = "已删除 " + (result == null ? 0 : result.Deleted) + " 条审计日志";
                await LoadAsync();
                ExitSelectionMode();
            }, null);
            StatusText = string.IsNullOrEmpty(message) ? StatusText : message;
        }

        /// <summary>进入选择模式：显示勾选框列并清空历史勾选。</summary>
        private void EnterSelectionMode()
        {
            foreach (AuditLogRow row in Items) { row.IsSelected = false; }
            IsSelectionMode = true;
            OnPropertyChanged(nameof(IsAllItemsSelected));
        }

        /// <summary>退出选择模式：隐藏勾选框列并清空勾选。</summary>
        private void ExitSelectionMode()
        {
            foreach (AuditLogRow row in Items) { row.IsSelected = false; }
            IsSelectionMode = false;
            OnPropertyChanged(nameof(IsAllItemsSelected));
        }

        private async Task PrevPageAsync()
        {
            if (PageIndex <= 1) { return; }
            PageIndex--;
            await LoadAsync();
        }

        private async Task NextPageAsync()
        {
            if (PageIndex >= PageCount) { return; }
            PageIndex++;
            await LoadAsync();
        }

        /// <summary>导出：SaveFileDialog 写 UTF-8 CSV；服务端按当前筛选导出并写 AUDIT_EXPORT 审计。</summary>
        private async Task ExportAsync()
        {
            if (CustomFrom.HasValue && CustomTo.HasValue && CustomFrom.Value > CustomTo.Value)
            {
                ErrorText = "时间范围无效：开始日期不能晚于结束日期";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV 文件|*.csv",
                FileName = "审计日志_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".csv"
            };
            if (dialog.ShowDialog() != true) { return; }

            string message = null;
            await RunAsync(async () =>
            {
                AuditExportDto export = await Api.ExportAuditLogsAsync(BuildRequest());
                if (export == null || string.IsNullOrEmpty(export.Content))
                {
                    throw new InvalidOperationException("导出内容为空");
                }
                File.WriteAllText(dialog.FileName, export.Content, new UTF8Encoding(true));
                await LoadAsync(); // 导出动作本身已写审计日志，刷新可见
                message = "已导出 " + export.Total + " 条记录到：" + dialog.FileName;
            }, null);
            StatusText = message ?? string.Empty;
        }

        /// <summary>文本筛选防抖（P2：避免每键击触发查询），350ms 后刷新。</summary>
        private async void ScheduleDebouncedReload()
        {
            if (_debounceCts != null)
            {
                _debounceCts.Cancel();
            }
            CancellationTokenSource cts = new CancellationTokenSource();
            _debounceCts = cts;
            try
            {
                await Task.Delay(350, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            if (cts.IsCancellationRequested) { return; }
            _pageIndex = 1;
            await LoadAsync();
        }

        private static string NullIfAll(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) { return null; }
            string trimmed = value.Trim();
            return trimmed == "全部" ? null : trimmed;
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

    }
}
