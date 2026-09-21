using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>收费标准行（价目表页签左侧 / 新增项目步骤一候选）。CHG-v1.1.2-26。</summary>
    public class ChargeStandardRow : ObservableObject
    {
        private bool _isChecked;
        private bool _isSelected;
        public ChargeStandardDto Dto { get; set; }
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }
        public bool IsSelected { get { return _isSelected; } set { SetProperty(ref _isSelected, value); } }

        public int Id { get { return Dto.Id; } }
        public string Name { get { return Dto.Name; } }
        public string CategoryText { get { return string.IsNullOrEmpty(Dto.Category) ? "未分类" : Dto.Category; } }
        public int SpecCount { get { return (Dto.Specs ?? new List<ChargeStandardSpecDto>()).Count; } }
        public int EnabledSpecCount
        {
            get { return (Dto.Specs ?? new List<ChargeStandardSpecDto>()).Count(x => x.Status == 0); }
        }
        public string SpecCountText { get { return SpecCount + " 条规格 · " + EnabledSpecCount + " 条启用"; } }
        public string VariableText
        {
            get
            {
                var names = (Dto.Variables ?? new List<ChargeVariableDto>()).Select(x => x.VarName).ToList();
                return names.Count == 0 ? "未引用计量变量" : string.Join(" · ", names);
            }
        }
        public string PriceSummary
        {
            get
            {
                var enabled = (Dto.Specs ?? new List<ChargeStandardSpecDto>()).Where(x => x.Status == 0).ToList();
                if (enabled.Count == 0) { return "未维护启用规格"; }
                return string.Join(" · ", enabled.Select(x => x.SpecName + " ¥" + x.UnitPrice.ToString("0.00")));
            }
        }
        public bool IsEnabled { get { return Dto.Status == 0; } }
        public string StatusText { get { return Dto.Status == 0 ? "启用" : "停用"; } }
        public string ToggleText { get { return IsEnabled ? "停用" : "启用"; } }
        public Brush StatusBrush { get { return Dto.Status == 0 ? RowBrushes.Ok : RowBrushes.Off; } }
        public Brush StatusBg { get { return Dto.Status == 0 ? RowBrushes.OkBg : RowBrushes.OffBg; } }
    }

    /// <summary>规格明细行（价目表页签右侧规格表）。CHG-v1.1.2-26。</summary>
    public class ChargeSpecRow : ObservableObject
    {
        private bool _isChecked;
        public ChargeStandardSpecDto Dto { get; set; }
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }

        public int Id { get { return Dto.Id; } }
        public string SpecName { get { return Dto.SpecName; } }
        /// <summary>公式展示文本（把 {v:ID} 记号替换为变量名，避免列表直接暴露落库记号）。</summary>
        public string DisplayFormula { get; set; }
        public string ConditionText
        {
            get
            {
                var parts = new List<string>();
                if (Dto.MatchUsage.HasValue)
                {
                    // CHG-v1.1.2-48：用途新增「空置」（0 住宅 / 1 商铺 / 2 空置）
                    string usage = Dto.MatchUsage.Value == 1 ? "商铺" : (Dto.MatchUsage.Value == 2 ? "空置" : "住宅");
                    parts.Add("房产用途 = " + usage);
                }
                if (Dto.MatchStatus.HasValue)
                {
                    string text = Dto.MatchStatus.Value == 0 ? "空置" : (Dto.MatchStatus.Value == 1 ? "入住" : "装修中");
                    parts.Add("房产状态 = " + text);
                }
                if (Dto.MatchSpaceType.HasValue)
                {
                    // CHG-v1.1.2-48：车位类型文案统一为「普通」（原旧文案已下线）
                    string text = Dto.MatchSpaceType.Value == 0 ? "产权" : (Dto.MatchSpaceType.Value == 1 ? "普通" : "临时");
                    parts.Add("车位类型 = " + text);
                }
                if (!string.IsNullOrWhiteSpace(Dto.MatchBuilding)) { parts.Add("楼栋 = " + Dto.MatchBuilding); }
                if (Dto.IsFallback) { parts.Add("不限（兜底）"); }
                return parts.Count == 0 ? "不限" : string.Join(" · ", parts);
            }
        }
        public string PriceText
        {
            get { return "¥" + Dto.UnitPrice.ToString("0.00") + (string.IsNullOrWhiteSpace(Dto.PriceUnit) ? string.Empty : " / " + Dto.PriceUnit); }
        }
        public string FormulaText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DisplayFormula)) { return DisplayFormula; }
                return string.IsNullOrWhiteSpace(Dto.Formula) ? "单价" : Dto.Formula;
            }
        }
        public string CycleText { get { return string.IsNullOrWhiteSpace(Dto.CycleName) ? "一次性" : Dto.CycleName; } }
        public string EffectiveText { get { return string.IsNullOrWhiteSpace(Dto.EffectiveFrom) ? "—" : Dto.EffectiveFrom; } }
        public string StatusText { get { return Dto.Status == 0 ? "启用" : "停用"; } }
        public string ToggleText { get { return Dto.Status == 0 ? "停用" : "启用"; } }
        public Brush StatusBrush { get { return Dto.Status == 0 ? RowBrushes.Ok : RowBrushes.Off; } }
        public Brush StatusBg { get { return Dto.Status == 0 ? RowBrushes.OkBg : RowBrushes.OffBg; } }
    }

    /// <summary>计量变量勾选项（规格抽屉「插入变量」）。CHG-v1.1.2-26。</summary>
    public class ChargeVariableOption : ObservableObject
    {
        private bool _isSelected;
        private bool _isBound;
        public ChargeVariableDto Dto { get; set; }
        /// <summary>是否已绑定到当前收费标准（变量区勾选；仅绑定后才可写入计算规则）。</summary>
        public bool IsBound
        {
            get { return _isBound; }
            set
            {
                if (!SetProperty(ref _isBound, value)) { return; }
                OnPropertyChanged(nameof(BindText));
            }
        }
        public string BindText { get { return IsBound ? "已绑定" : "未绑定"; } }
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (!SetProperty(ref _isSelected, value)) { return; }
                OnPropertyChanged(nameof(ChipBackground));
                OnPropertyChanged(nameof(ChipForeground));
                OnPropertyChanged(nameof(ChipBorder));
            }
        }
        public Brush ChipBackground { get { return IsSelected ? RowBrushes.ChipOn : RowBrushes.VariableBg; } }
        public Brush ChipForeground { get { return IsSelected ? RowBrushes.Surface : RowBrushes.ChipOn; } }
        public Brush ChipBorder { get { return IsSelected ? RowBrushes.ChipOn : RowBrushes.Border; } }
        public int Id { get { return Dto.Id; } }
        public string Name { get { return Dto.VarName; } }
        public string UnitText { get { return string.IsNullOrWhiteSpace(Dto.Unit) ? "—" : Dto.Unit; } }
        public string SourceText
        {
            get
            {
                switch (Dto.Source)
                {
                    case ChargeVariableSource.Archive: return "档案自动";
                    case ChargeVariableSource.CycleDerived: return "周期派生";
                    case ChargeVariableSource.Fixed: return "固定值";
                    default: return "手填";
                }
            }
        }
        public string Token { get { return "{v:" + Dto.Id + "}"; } }
    }

    /// <summary>缴费对象 chip（步骤二）。CHG-v1.1.2-26。</summary>
    public class ChargeObjectChip : ObservableObject
    {
        private bool _isSelected;
        public DictItemDto Dto { get; set; }
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (!SetProperty(ref _isSelected, value)) { return; }
                OnPropertyChanged(nameof(ChipBackground));
                OnPropertyChanged(nameof(ChipForeground));
                OnPropertyChanged(nameof(ChipBorder));
            }
        }
        public string Label { get { return Dto == null ? string.Empty : Dto.ItemName; } }
        public Brush ChipBackground { get { return IsSelected ? RowBrushes.ChipOn : RowBrushes.Surface; } }
        public Brush ChipForeground { get { return IsSelected ? RowBrushes.Surface : RowBrushes.Text; } }
        public Brush ChipBorder { get { return IsSelected ? RowBrushes.ChipOn : RowBrushes.Border; } }
    }

    internal static class RowBrushes
    {
        public static readonly Brush Ok = Freeze("#12805C");
        public static readonly Brush OkBg = Freeze("#E8F7F1");
        public static readonly Brush Off = Freeze("#667085");
        public static readonly Brush OffBg = Freeze("#F1F3F7");
        public static readonly Brush ChipOn = Freeze("#1F4B43");
        public static readonly Brush Surface = Freeze("#FFFFFF");
        public static readonly Brush Text = Freeze("#172033");
        public static readonly Brush Border = Freeze("#E4EAF2");
        public static readonly Brush VariableBg = Freeze("#E9F0EE");

        private static Brush Freeze(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// 收费项目维护 —— 「价目表 + 计量变量」部分（CHG-v1.1.2-26，界面按原型 8 屏一比一还原）。
    /// 页签一：收费项目（选收费标准 + 选缴费对象）；页签二：价目表（收费标准 → 规格明细，改价 = 停用旧 + 新增新）。
    /// </summary>
    public partial class ChargeItemsViewModel
    {
        private int _activeTab;
        private string _standardKeyword = string.Empty;
        private ChargeStandardRow _selectedStandard;
        private bool _isBatchMode;
        private bool _isBatchConfirmVisible;
        private string _batchConfirmMessage = string.Empty;
        private bool _isActionConfirmVisible;
        private string _actionConfirmTitle = "确认操作";
        private string _actionConfirmMessage = string.Empty;
        private Action _pendingConfirmAction;
        private bool _isNoticeVisible;
        private string _noticeTitle = "提示";
        private string _noticeMessage = string.Empty;
        private bool _isSpecFormVisible;
        private int _editingSpecId;
        private string _specFormTitle = "新增规格";
        private string _formSpecName = string.Empty;
        private int _formSpecConditionIndex;
        private decimal _formSpecPrice;
        private string _formSpecCycleName = "每月";
        private string _formSpecEffective = DateTime.Now.ToString("yyyy-MM-dd");
        private bool _formSpecFallback;
        private string _formSpecChangeReason = string.Empty;
        private bool _formSpecDeprecate;
        private string _formSpecFormula = string.Empty;
        private bool _isStandardFormVisible;
        private int _editingStandardId;
        private string _formStandardName = string.Empty;
        private string _formStandardCategory = string.Empty;
        private string _formStandardRemark = string.Empty;
        private ChargeStandardRow _formStandard;
        private string _formStandardKeyword = string.Empty;
        private bool _formAllowOverride;
        /// <summary>FIX-v1.1.2-01：收费标准列表的并发加载保护。</summary>
        private int _standardsLoadSeq;
        private readonly object _standardsLoadSync = new object();
        /// <summary>CHG-v1.1.2-52：变量区取数序号（防抖，只采纳最后一次）。</summary>
        private int _variablesLoadSeq;
        /// <summary>CHG-v1.1.2-52：变量区当前对应的收费标准 id（与选中标准不一致时先重载再保存）。</summary>
        private int _variablesStandardId;

        /// <summary>价目表页签：收费标准列表。</summary>
        public ObservableCollection<ChargeStandardRow> Standards { get; } = new ObservableCollection<ChargeStandardRow>();
        /// <summary>价目表页签：所选收费标准的规格明细。</summary>
        public ObservableCollection<ChargeSpecRow> Specs { get; } = new ObservableCollection<ChargeSpecRow>();
        /// <summary>规格抽屉：可插入到公式的计量变量。</summary>
        public ObservableCollection<ChargeVariableOption> VariableOptions { get; } = new ObservableCollection<ChargeVariableOption>();
        /// <summary>新增项目步骤二：缴费对象 chip。</summary>
        public ObservableCollection<ChargeObjectChip> ObjectChips { get; } = new ObservableCollection<ChargeObjectChip>();
        /// <summary>新增项目步骤一：收费标准候选（仅启用）。</summary>
        public ObservableCollection<ChargeStandardRow> FormStandardCandidates { get; } = new ObservableCollection<ChargeStandardRow>();

        /// <summary>计费周期候选（规格抽屉）。</summary>
        public List<string> CycleOptions { get; } = new List<string> { "每月", "每季", "每半年", "每年", "一次性" };

        public IRelayCommand SelectObjectCommand { get; private set; }

        private void SelectObject(ChargeObjectChip chip)
        {
            if (chip == null) { return; }
            foreach (ChargeObjectChip item in ObjectChips) { item.IsSelected = ReferenceEquals(item, chip); }
            FormObject = chip.Dto;
            OnPropertyChanged(nameof(IsOverrideEditable));
            if (!IsOverrideEditable) { FormAllowOverride = false; }
        }

        /// <summary>字典加载完成后重建 chip（保持当前选择）。</summary>
        private void RefreshObjectChips()
        {
            string keep = FormObject == null ? null : FormObject.ItemCode;
            ObjectChips.Clear();
            foreach (DictItemDto dto in ChargeObjects)
            {
                ObjectChips.Add(new ChargeObjectChip
                {
                    Dto = dto,
                    IsSelected = keep != null && string.Equals(dto.ItemCode, keep, StringComparison.OrdinalIgnoreCase)
                });
            }
        }

        // ---------- 页签 ----------

        public bool IsItemTab { get { return _activeTab == 0; } }
        public bool IsPriceTab { get { return _activeTab == 1; } }

        public IRelayCommand<string> SwitchTabCommand { get; private set; }

        private async void SwitchTab(string tab)
        {
            _activeTab = string.Equals(tab, "price", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            OnPropertyChanged(nameof(IsItemTab));
            OnPropertyChanged(nameof(IsPriceTab));
            if (IsPriceTab) { await LoadStandardsCoreAsync(); }
        }

        // ---------- 价目表：收费标准与规格 ----------

        public string StandardKeyword
        {
            get { return _standardKeyword; }
            set { if (SetProperty(ref _standardKeyword, value)) { } }
        }

        public ChargeStandardRow SelectedStandard
        {
            get { return _selectedStandard; }
            set
            {
                if (!SetProperty(ref _selectedStandard, value)) { return; }
                foreach (ChargeStandardRow row in Standards) { row.IsSelected = ReferenceEquals(row, value); }
                RefreshSpecs();
                // CHG-v1.1.2-52：切换收费标准必须重载「变量区」——
                // 原实现只在页面加载/保存后刷新变量区，切换标准后仍显示**上一条标准**的勾选；
                // 此时保存规格会把上一条标准的绑定集合写进当前标准（静默解绑/串味），
                // 表现为「该标准下引用被解绑变量的规格出账未命中」。
                _ = ReloadVariablesForSelectedStandardAsync();
                OnPropertyChanged(nameof(StandardSummaryTitle));
                OnPropertyChanged(nameof(StandardSummaryMeta));
                OnPropertyChanged(nameof(HasSelectedStandard));
            }
        }

        public bool HasSelectedStandard { get { return _selectedStandard != null; } }
        public string StandardSummaryTitle { get { return _selectedStandard == null ? "请选择收费标准" : _selectedStandard.Name; } }
        public string StandardSummaryMeta
        {
            get
            {
                if (_selectedStandard == null) { return "左侧选择一条收费标准后，右侧维护其规格明细"; }
                return "类别 " + _selectedStandard.CategoryText + " · " + _selectedStandard.SpecCountText + " · 变量 " + _selectedStandard.VariableText;
            }
        }

        public bool IsBatchMode
        {
            get { return _isBatchMode; }
            private set
            {
                if (!SetProperty(ref _isBatchMode, value)) { return; }
                OnPropertyChanged(nameof(IsNotBatchMode));
            }
        }

        /// <summary>批量删除按钮组：未进入批量模式时显示「批量删除」，进入后显示「取消 / 确认删除」。</summary>
        public bool IsNotBatchMode { get { return !_isBatchMode; } }

        public IRelayCommand EnterBatchModeCommand { get; private set; }
        public IRelayCommand CancelBatchCommand { get; private set; }
        public IRelayCommand RequestBatchConfirmCommand { get; private set; }
        public IAsyncRelayCommand ConfirmBatchCommand { get; private set; }
        public bool IsBatchConfirmVisible { get { return _isBatchConfirmVisible; } private set { SetProperty(ref _isBatchConfirmVisible, value); } }
        public string BatchConfirmMessage { get { return _batchConfirmMessage; } private set { SetProperty(ref _batchConfirmMessage, value); } }

        // ---------- CHG-v1.1.2-35：删除类操作的「确认弹窗 + 结果提示弹窗」 ----------

        /// <summary>确认弹窗（删除收费标准等需要二次确认的操作）。</summary>
        public bool IsActionConfirmVisible { get { return _isActionConfirmVisible; } private set { SetProperty(ref _isActionConfirmVisible, value); } }
        public string ActionConfirmTitle { get { return _actionConfirmTitle; } private set { SetProperty(ref _actionConfirmTitle, value); } }
        public string ActionConfirmMessage { get { return _actionConfirmMessage; } private set { SetProperty(ref _actionConfirmMessage, value); } }

        /// <summary>提示弹窗（删除被账单引用而拦截等，把服务端原因明确弹给用户）。</summary>
        public bool IsNoticeVisible { get { return _isNoticeVisible; } private set { SetProperty(ref _isNoticeVisible, value); } }
        public string NoticeTitle { get { return _noticeTitle; } private set { SetProperty(ref _noticeTitle, value); } }
        public string NoticeMessage { get { return _noticeMessage; } private set { SetProperty(ref _noticeMessage, value); } }

        public IRelayCommand ConfirmActionCommand { get; private set; }
        public IRelayCommand CancelActionCommand { get; private set; }
        public IRelayCommand CloseNoticeCommand { get; private set; }

        private void AskConfirm(string title, string message, Action action)
        {
            ActionConfirmTitle = title;
            ActionConfirmMessage = message;
            _pendingConfirmAction = action;
            IsActionConfirmVisible = true;
        }

        private void ConfirmAction()
        {
            Action action = _pendingConfirmAction;
            _pendingConfirmAction = null;
            IsActionConfirmVisible = false;
            if (action != null) { action(); }
        }

        private void CancelAction()
        {
            _pendingConfirmAction = null;
            IsActionConfirmVisible = false;
        }

        private void ShowNotice(string title, string message)
        {
            NoticeTitle = title;
            NoticeMessage = message;
            IsNoticeVisible = true;
        }

        private void CloseNotice()
        {
            IsNoticeVisible = false;
        }

        /// <summary>
        /// 删除类操作统一执行：成功写状态条；失败既写状态条也弹「提示」窗
        /// （收费项目 / 规格 / 收费标准被账单或收费项目引用时，服务端会给出可读原因）。
        /// </summary>
        private async Task RunDeleteAsync(Func<Task<string>> action, string failureTitle)
        {
            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                string message = await action();
                StatusText = string.IsNullOrEmpty(message) ? string.Empty : DateTime.Now.ToString("HH:mm:ss ") + message;
            }
            catch (ApiClientException ex)
            {
                ErrorText = ex.Message;
                ShowNotice(failureTitle, ex.Message);
            }
            catch (Exception ex)
            {
                string message = "操作失败：" + ex.Message;
                ErrorText = message;
                ShowNotice(failureTitle, message);
            }
            finally
            {
                IsBusy = false;
            }
        }
        /// <summary>CHG-v1.1.2-33：导出收费项目清单（PDF）。</summary>
        public IAsyncRelayCommand ExportPdfCommand { get; private set; }

        private async Task ExportPdfAsync()
        {
            await RunAsync(async () =>
            {
                ReportLogDto log = await Api.ExportChargeItemsAsync(new ChargeItemExportRequest
                {
                    Format = ExportFormat.Pdf,
                    Keyword = Keyword,
                    Category = CategoryFilterName
                });
                if (log == null) { throw new InvalidOperationException("服务端未生成导出记录"); }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "保存收费项目清单（PDF）",
                    Filter = "PDF 文件|*.pdf",
                    FileName = "收费项目清单_" + DateTime.Now.ToString("yyyyMMddHHmm") + ".pdf"
                };
                if (dialog.ShowDialog() != true)
                {
                    StatusText = DateTime.Now.ToString("HH:mm:ss ") + "PDF 已在服务端生成（导出日志 " + log.Id + "），未另存到本机";
                    return;
                }

                await Api.DownloadReportFileAsync(log.Id, dialog.FileName);
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "收费项目清单已导出：" + dialog.FileName;
                System.Windows.MessageBox.Show("PDF 已导出到：" + dialog.FileName, "导出成功",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }, null);
        }

        /// <summary>进入批量删除：点击后才出现勾选框（未点击不显示）。</summary>
        private void EnterBatchMode()
        {
            ClearChecks();
            ErrorText = string.Empty;
            IsBatchMode = true;
        }

        /// <summary>退出批量删除：清空勾选并关闭确认弹窗。</summary>
        private void CancelBatch()
        {
            IsBatchConfirmVisible = false;
            IsBatchMode = false;
            ClearChecks();
            ErrorText = string.Empty;
        }

        /// <summary>确认删除前置校验：先勾选，再弹确认（两个页签共用同一弹窗）。</summary>
        private void RequestBatchConfirm()
        {
            List<string> names = IsItemTab
                ? Items.Where(x => x.IsChecked).Select(x => x.Name).ToList()
                : Specs.Where(x => x.IsChecked).Select(x => x.SpecName).ToList();
            if (names.Count == 0)
            {
                ErrorText = IsItemTab ? "请先勾选要删除的收费项目" : "请先勾选要删除的规格";
                return;
            }
            string desc = string.Join("、", names.Take(3));
            if (names.Count > 3) { desc += " 等 " + names.Count + " 项"; }
            BatchConfirmMessage = (IsItemTab ? "将删除 " + names.Count + " 项收费项目" : "将删除 " + names.Count + " 条规格") +
                                  "（软删除留痕，历史账单与流水不受影响）：\n" + desc;
            ErrorText = string.Empty;
            IsBatchConfirmVisible = true;
        }

        private void ClearChecks()
        {
            foreach (ChargeItemRow row in Items) { row.IsChecked = false; }
            foreach (ChargeStandardRow row in Standards) { row.IsChecked = false; }
            foreach (ChargeSpecRow row in Specs) { row.IsChecked = false; }
            IsSelectAll = false;
        }

        private async Task ConfirmBatchAsync()
        {
            await RunDeleteAsync(async () =>
            {
                // CHG-v1.1.2-35：逐条删除并逐条收口 —— 被账单引用的记录（收费项目 / 规格）
                // 不阻断其它记录，删除结束后把未删除原因一并弹窗告知。
                var blocked = new List<string>();
                int deleted = 0;
                if (IsItemTab)
                {
                    List<ChargeItemRow> picked = Items.Where(x => x.IsChecked).ToList();
                    if (picked.Count == 0) { throw new InvalidOperationException("请先勾选要删除的收费项目"); }
                    foreach (ChargeItemRow row in picked)
                    {
                        try { await Api.DeleteChargeItemAsync(row.Id); deleted++; }
                        catch (ApiClientException ex) { blocked.Add("收费项目「" + row.Name + "」：" + ex.Message); }
                    }
                }
                else
                {
                    List<ChargeSpecRow> picked = Specs.Where(x => x.IsChecked).ToList();
                    if (picked.Count == 0) { throw new InvalidOperationException("请先勾选要删除的规格"); }
                    foreach (ChargeSpecRow row in picked)
                    {
                        try { await Api.DeleteChargeSpecAsync(row.Id); deleted++; }
                        catch (ApiClientException ex) { blocked.Add("规格「" + row.SpecName + "」：" + ex.Message); }
                    }
                }
                IsBatchMode = false;
                IsBatchConfirmVisible = false;
                ClearChecks();
                await LoadStandardsCoreAsync();
                await LoadItemsCoreAsync();
                if (blocked.Count > 0)
                {
                    ShowNotice("部分记录未能删除",
                        "已删除 " + deleted + " 项；" + blocked.Count + " 项未能删除：\n" +
                        string.Join("\n", blocked.Take(5)) + (blocked.Count > 5 ? "\n…" : string.Empty));
                }
                return deleted == 0
                    ? string.Empty
                    : "已删除 " + deleted + " 项（软删除留痕，历史账单与流水不受影响）";
            }, "删除未完成");
        }

        // ---------- 规格抽屉（新增 / 改价） ----------

        public bool IsSpecFormVisible { get { return _isSpecFormVisible; } private set { SetProperty(ref _isSpecFormVisible, value); } }
        public string SpecFormTitle { get { return _specFormTitle; } private set { SetProperty(ref _specFormTitle, value); } }
        public string FormSpecName { get { return _formSpecName; } set { SetProperty(ref _formSpecName, value); } }
        /// <summary>
        /// CHG-v1.1.2-48：适用条件下拉索引 ↔ 匹配维度映射：
        /// 0 不限 / 1 房产用途=住宅 / 2 房产用途=商铺 / 3 房产用途=空置 / 4 房产状态=空置 /
        /// 5 车位类型=产权 / 6 车位类型=普通 / 7 车位类型=临时。
        /// </summary>
        public int FormSpecConditionIndex
        {
            get { return _formSpecConditionIndex; }
            set
            {
                if (!SetProperty(ref _formSpecConditionIndex, value)) { return; }
                OnPropertyChanged(nameof(FormSpecConditionHint));
            }
        }
        public string FormSpecConditionHint
        {
            get
            {
                switch (_formSpecConditionIndex)
                {
                    case 1: return "仅当房产用途为「住宅」时命中该规格";
                    case 2: return "仅当房产用途为「商铺」时命中该规格";
                    case 3: return "仅当房产用途为「空置」时命中该规格";
                    case 4: return "仅当房产状态为「空置」时命中该规格";
                    case 5: return "仅当车位类型为「产权」时命中该规格";
                    case 6: return "仅当车位类型为「普通」时命中该规格";
                    case 7: return "仅当车位类型为「临时」时命中该规格";
                    default: return "不限：业主 / 自定义缴费对象一律按本条出账（房产 / 车位未命中其它规格时也按本条兜底）";
                }
            }
        }
        public decimal FormSpecPrice { get { return _formSpecPrice; } set { SetProperty(ref _formSpecPrice, value); } }
        public string FormSpecCycleName
        {
            get { return _formSpecCycleName; }
            set { if (SetProperty(ref _formSpecCycleName, value)) { OnPropertyChanged(nameof(IsOneTimeCycle)); } }
        }
        public bool IsOneTimeCycle { get { return !string.IsNullOrWhiteSpace(_formSpecCycleName) && _formSpecCycleName.Contains("一次性"); } }
        public string FormSpecEffective { get { return _formSpecEffective; } set { SetProperty(ref _formSpecEffective, value); } }
        public bool FormSpecFallback { get { return _formSpecFallback; } set { SetProperty(ref _formSpecFallback, value); } }
        public string FormSpecChangeReason
        {
            get { return _formSpecChangeReason; }
            set { if (SetProperty(ref _formSpecChangeReason, value)) { OnPropertyChanged(nameof(FormSpecDeprecateHint)); } }
        }
        public bool FormSpecDeprecate
        {
            get { return _formSpecDeprecate; }
            set { if (SetProperty(ref _formSpecDeprecate, value)) { OnPropertyChanged(nameof(FormSpecDeprecateHint)); } }
        }
        public string FormSpecDeprecateHint
        {
            get
            {
                return _formSpecDeprecate
                    ? "改价口径：保存后同名旧规格自动转为「停用」，历史账单金额不变（变更原因必填）"
                    : "直接新增一条规格；如需调价请勾选「保存并停用同名旧规格」";
            }
        }
        public string FormSpecFormula
        {
            get { return _formSpecFormula; }
            set { if (SetProperty(ref _formSpecFormula, value)) { OnPropertyChanged(nameof(SpecFormulaHint)); OnPropertyChanged(nameof(SpecPriceUnitHint)); } }
        }
        public string SpecPriceUnitHint
        {
            get
            {
                var units = VariableOptions.Where(x => x.IsSelected && x.Dto.Source != ChargeVariableSource.Fixed)
                    .Select(x => x.UnitText).Where(x => x != "—").Distinct().ToList();
                return units.Count == 0 ? "单价单位：元" : "单价单位：元 / " + string.Join("·", units);
            }
        }
        public string SpecFormulaHint
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_formSpecFormula)) { return "留空 = 按单价 × 数量（数量固定 1，适用于按户 / 按次）"; }
                return "公式变量以「插入变量」写入，落库保存变量 ID（改名不影响历史公式）";
            }
        }
        public bool HasSpecFormulaError { get { return false; } }

        public IRelayCommand ToggleVariableCommand { get; private set; }
        /// <summary>变量区：勾选 / 取消勾选「本收费标准使用的计量变量」（未绑定的变量不会出现在计算规则里）。</summary>
        public IRelayCommand ToggleBindVariableCommand { get; private set; }
        /// <summary>变量区已绑定的变量（用于「插入变量」芯片）。</summary>
        public IEnumerable<ChargeVariableOption> BoundVariables
        {
            get { return VariableOptions.Where(x => x.IsBound); }
        }
        public IRelayCommand NewSpecCommand { get; private set; }
        public IRelayCommand EditSpecCommand { get; private set; }
        public IRelayCommand CancelSpecCommand { get; private set; }
        public IAsyncRelayCommand SaveSpecCommand { get; private set; }
        public IAsyncRelayCommand ToggleSpecCommand { get; private set; }
        public IRelayCommand DeleteSpecCommand { get; private set; }

        private void StartNewSpec()
        {
            if (_selectedStandard == null)
            {
                ErrorText = "请先在左侧选择一条收费标准";
                return;
            }
            _editingSpecId = 0;
            SpecFormTitle = "新增规格";
            FormSpecName = string.Empty;
            FormSpecConditionIndex = 0;
            FormSpecPrice = 0m;
            FormSpecCycleName = "每月";
            FormSpecEffective = DateTime.Now.ToString("yyyy-MM-dd");
            FormSpecFallback = false;
            FormSpecChangeReason = string.Empty;
            FormSpecDeprecate = false;
            ResetVariableSelection();
            FormSpecFormula = string.Empty;
            IsSpecFormVisible = true;
        }

        private void StartEditSpec(ChargeSpecRow row)
        {
            if (row == null) { return; }
            _editingSpecId = row.Id;
            SpecFormTitle = "编辑规格";
            FormSpecName = row.Dto.SpecName;
            // CHG-v1.1.2-48：条件 → 下拉索引（与 SaveSpecForm 的映射严格互逆）
            if (row.Dto.MatchUsage.HasValue)
            {
                FormSpecConditionIndex = row.Dto.MatchUsage.Value == 1 ? 2 : (row.Dto.MatchUsage.Value == 2 ? 3 : 1);
            }
            else if (row.Dto.MatchStatus.HasValue && row.Dto.MatchStatus.Value == 0)
            {
                FormSpecConditionIndex = 4;
            }
            else if (row.Dto.MatchSpaceType.HasValue)
            {
                FormSpecConditionIndex = row.Dto.MatchSpaceType.Value == 0 ? 5 : (row.Dto.MatchSpaceType.Value == 1 ? 6 : 7);
            }
            else
            {
                FormSpecConditionIndex = 0;
            }
            FormSpecPrice = row.Dto.UnitPrice;
            FormSpecCycleName = row.Dto.CycleName;
            FormSpecEffective = row.Dto.EffectiveFrom;
            FormSpecFallback = row.Dto.IsFallback;
            FormSpecChangeReason = string.Empty;
            FormSpecDeprecate = false;
            ResetVariableSelection();
            FormSpecFormula = row.Dto.Formula;
            MarkFormulaVariables(row.Dto.Formula);
            IsSpecFormVisible = true;
        }

        private void ResetVariableSelection()
        {
            foreach (ChargeVariableOption option in VariableOptions) { option.IsSelected = false; }
        }

        private void MarkFormulaVariables(string formula)
        {
            if (string.IsNullOrWhiteSpace(formula)) { return; }
            foreach (ChargeVariableOption option in VariableOptions)
            {
                option.IsSelected = formula.Contains(option.Token);
            }
            OnPropertyChanged(nameof(BoundVariables));
        }

        private void ToggleVariable(ChargeVariableOption option)
        {
            if (option == null) { return; }
            option.IsSelected = !option.IsSelected;
            ComposeFormula();
        }

        private void ToggleBindVariable(ChargeVariableOption option)
        {
            if (option == null) { return; }
            option.IsBound = !option.IsBound;
            if (!option.IsBound && option.IsSelected)
            {
                option.IsSelected = false;
                ComposeFormula();
            }
            OnPropertyChanged(nameof(BoundVariables));
            OnPropertyChanged(nameof(SpecPriceUnitHint));
            OnPropertyChanged(nameof(FormSpecVariableHint));
        }

        public string FormSpecVariableHint
        {
            get
            {
                int bound = VariableOptions.Count(x => x.IsBound && !x.Dto.IsBuiltin);
                return "变量区勾选的变量才能写入计算规则；内置变量不占额度，自建变量每标准最多 5 个（当前 " + bound + " 个）";
            }
        }

        /// <summary>按勾选的变量自动拼公式（单价 × 变量…），用户仍可手工微调。</summary>
        private void ComposeFormula()
        {
            // 只有已绑定到本收费标准的变量才能进入公式（避免服务端「引用了未绑定变量」的拦截）
            List<ChargeVariableOption> picked = VariableOptions.Where(x => x.IsSelected && x.IsBound).ToList();
            var parts = new List<string> { "单价" };
            parts.AddRange(picked.Select(x => x.Token));
            FormSpecFormula = string.Join(" * ", parts);
            OnPropertyChanged(nameof(SpecPriceUnitHint));
        }

        /// <summary>把「变量区」的勾选结果保存到收费标准（新增/编辑规格前自动调用）。</summary>
        private async Task PersistVariableBindingAsync()
        {
            if (_selectedStandard == null) { return; }
            // CHG-v1.1.2-52：变量区与本标准不同步（刚切换标准 / 取数失败）时先重载，
            // 否则会把「上一条标准」的勾选结果写进当前标准 —— 静默解绑其它变量。
            if (_variablesStandardId != _selectedStandard.Id)
            {
                await LoadVariablesAsync();
                if (_variablesStandardId != _selectedStandard.Id) { return; }
            }
            List<int> ids = VariableOptions.Where(x => x.IsBound).Select(x => x.Id).ToList();
            List<int> current = (_selectedStandard.Dto.Variables ?? new List<ChargeVariableDto>()).Select(x => x.Id).ToList();
            if (ids.Count == current.Count && ids.All(current.Contains)) { return; }

            ChargeStandardDto updated = await Api.UpdateChargeStandardAsync(_selectedStandard.Id, new ChargeStandardRequest
            {
                Name = _selectedStandard.Dto.Name,
                Category = _selectedStandard.Dto.Category,
                Remark = _selectedStandard.Dto.Remark,
                Status = _selectedStandard.Dto.Status,
                VariableIds = ids
            });
            _selectedStandard.Dto.Variables = updated == null ? new List<ChargeVariableDto>() : updated.Variables;
        }

        private async Task SaveSpecAsync()
        {
            await RunAsync(async () =>
            {
                if (_selectedStandard == null) { throw new InvalidOperationException("请先选择收费标准"); }
                if (string.IsNullOrWhiteSpace(FormSpecName)) { throw new InvalidOperationException("规格名称不能为空"); }
                if (FormSpecPrice <= 0m) { throw new InvalidOperationException("单价必须大于 0"); }
                if (FormSpecDeprecate && string.IsNullOrWhiteSpace(FormSpecChangeReason))
                {
                    throw new InvalidOperationException("改价必须填写变更原因（用于审计追溯）");
                }

                // 变量区勾选先落库（新增 / 编辑规格都以「已绑定变量」为准）
                await PersistVariableBindingAsync();

                var request = new ChargeStandardSpecRequest
                {
                    SpecName = FormSpecName.Trim(),
                    UnitPrice = FormSpecPrice,
                    Formula = FormSpecFormula,
                    CycleName = FormSpecCycleName,
                    EffectiveFrom = FormSpecEffective,
                    IsFallback = FormSpecConditionIndex == 0,
                    Status = 0,
                    DeprecateSameName = FormSpecDeprecate,
                    ChangeReason = FormSpecChangeReason
                };
                switch (FormSpecConditionIndex)
                {
                    case 1: request.MatchUsage = 0; break;
                    case 2: request.MatchUsage = 1; break;
                    case 3: request.MatchUsage = 2; break;   // CHG-v1.1.2-48：房产用途 = 空置
                    case 4: request.MatchStatus = 0; break;
                    // CHG-v1.1.2-48：车位类型适用条件（产权 / 普通 / 临时）
                    case 5: request.MatchSpaceType = 0; break;
                    case 6: request.MatchSpaceType = 1; break;
                    case 7: request.MatchSpaceType = 2; break;
                }

                if (_editingSpecId > 0)
                {
                    await Api.UpdateChargeSpecAsync(_editingSpecId, request);
                }
                else
                {
                    await Api.CreateChargeSpecAsync(_selectedStandard.Id, request);
                }
                IsSpecFormVisible = false;
                // FIX-v1.1.2-04（负责人反馈「新增规格后收费项目表格未同步、需切换页面才更新」）：
                // 收费项目列表的「规格 / 默认单价」由收费标准的规格聚合而来，规格变更后必须一并刷新。
                await LoadStandardsCoreAsync();
                await LoadItemsCoreAsync();
            }, FormSpecDeprecate ? "规格已保存，同名旧规格已转停用" : "规格已保存");
        }

        private async Task ToggleSpecAsync(ChargeSpecRow row)
        {
            if (row == null) { return; }
            await RunAsync(async () =>
            {
                await Api.ToggleChargeSpecAsync(row.Id, row.Dto.Status == 0 ? 1 : 0);
                await LoadStandardsCoreAsync();
                await LoadItemsCoreAsync();
            }, row.Dto.Status == 0 ? "规格已停用（只影响后续出账）" : "规格已启用");
        }

        private async Task DeleteSpecAsync(ChargeSpecRow row)
        {
            if (row == null) { return; }
            await RunDeleteAsync(async () =>
            {
                await Api.DeleteChargeSpecAsync(row.Id);
                await LoadStandardsCoreAsync();
                await LoadItemsCoreAsync();
                return "规格已删除（软删除留痕）";
            }, "规格未能删除");
        }

        // ---------- 收费标准维护 ----------

        public bool IsStandardFormVisible { get { return _isStandardFormVisible; } private set { SetProperty(ref _isStandardFormVisible, value); } }
        public string FormStandardName { get { return _formStandardName; } set { SetProperty(ref _formStandardName, value); } }
        public string FormStandardCategory { get { return _formStandardCategory; } set { SetProperty(ref _formStandardCategory, value); } }
        public string FormStandardRemark { get { return _formStandardRemark; } set { SetProperty(ref _formStandardRemark, value); } }

        public IRelayCommand NewStandardCommand { get; private set; }
        public IRelayCommand EditStandardCommand { get; private set; }
        public IRelayCommand CancelStandardCommand { get; private set; }
        public IAsyncRelayCommand SaveStandardCommand { get; private set; }
        public IAsyncRelayCommand ToggleStandardCommand { get; private set; }
        /// <summary>CHG-v1.1.2-35：删除收费标准 = 先弹确认，再执行删除。</summary>
        public IRelayCommand DeleteStandardCommand { get; private set; }

        private void StartNewStandard()
        {
            _editingStandardId = 0;
            FormStandardName = string.Empty;
            FormStandardCategory = string.Empty;
            FormStandardRemark = string.Empty;
            IsStandardFormVisible = true;
        }

        private void StartEditStandard()
        {
            if (_selectedStandard == null)
            {
                ErrorText = "请先在左侧选择一条收费标准";
                return;
            }
            _editingStandardId = _selectedStandard.Id;
            FormStandardName = _selectedStandard.Dto.Name;
            FormStandardCategory = _selectedStandard.Dto.Category;
            FormStandardRemark = _selectedStandard.Dto.Remark;
            IsStandardFormVisible = true;
        }

        private async Task SaveStandardAsync()
        {
            await RunAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(FormStandardName)) { throw new InvalidOperationException("收费标准名称不能为空"); }
                if (string.IsNullOrWhiteSpace(FormStandardCategory)) { throw new InvalidOperationException("请填写收费项目类别"); }
                var request = new ChargeStandardRequest
                {
                    Name = FormStandardName.Trim(),
                    Category = FormStandardCategory.Trim(),
                    Remark = FormStandardRemark,
                    Status = 0
                };
                if (_editingStandardId > 0)
                {
                    await Api.UpdateChargeStandardAsync(_editingStandardId, request);
                }
                else
                {
                    await Api.CreateChargeStandardAsync(request);
                }
                IsStandardFormVisible = false;
                await LoadStandardsCoreAsync();
                // FIX-v1.1.2-04：收费标准改名/改名后收费项目列表的「收费标准」列同样需要同步
                await LoadItemsCoreAsync();
            }, "收费标准已保存");
        }

        private async Task ToggleStandardAsync()
        {
            if (_selectedStandard == null) { return; }
            await RunAsync(async () =>
            {
                await Api.ToggleChargeStandardAsync(_selectedStandard.Id, _selectedStandard.Dto.Status == 0 ? 1 : 0);
                await LoadStandardsCoreAsync();
                await LoadItemsCoreAsync();
            }, _selectedStandard.Dto.Status == 0 ? "收费标准已停用" : "收费标准已启用");
        }

        private async Task DeleteStandardAsync()
        {
            if (_selectedStandard == null) { return; }
            await RunDeleteAsync(async () =>
            {
                await Api.DeleteChargeStandardAsync(_selectedStandard.Id);
                await LoadStandardsCoreAsync();
                await LoadItemsCoreAsync();
                return "收费标准已删除（软删除留痕；已被收费项目引用的标准不能删除，可改用「停用」）";
            }, "收费标准未能删除");
        }

        /// <summary>CHG-v1.1.2-35：删除收费标准前先弹确认（原实现点一下即删）。</summary>
        private void StartDeleteStandard()
        {
            if (_selectedStandard == null)
            {
                ErrorText = "请先在左侧选择一条收费标准";
                return;
            }
            AskConfirm("删除收费标准",
                "确认删除收费标准「" + _selectedStandard.Name + "」（" + _selectedStandard.CategoryText + "）？\n" +
                "· 删除为软删除留痕，历史账单金额不变；\n" +
                "· 已被收费项目引用的收费标准不能删除，如需停止使用请改用「停用」。",
                () => { _ = DeleteStandardAsync(); });
        }

        // ---------- 新增 / 编辑收费项目（3 步：选标准 → 选对象 → 保存） ----------

        public ObservableCollection<ChargeStandardRow> StandardsForForm { get { return FormStandardCandidates; } }

        public string FormStandardKeyword
        {
            get { return _formStandardKeyword; }
            set { if (SetProperty(ref _formStandardKeyword, value)) { RefreshFormStandardCandidates(); } }
        }

        public ChargeStandardRow FormStandard
        {
            get { return _formStandard; }
            set
            {
                if (!SetProperty(ref _formStandard, value)) { return; }
                OnPropertyChanged(nameof(FormStandardSummary));
                OnPropertyChanged(nameof(FormStandardSpecSummary));
                OnPropertyChanged(nameof(IsOverrideEditable));
                if (value != null && !IsOverrideEditable) { FormAllowOverride = false; }
            }
        }

        public string FormStandardSummary
        {
            get { return _formStandard == null ? "尚未选择收费标准" : _formStandard.Name + "（" + _formStandard.CategoryText + "）"; }
        }

        public string FormStandardSpecSummary
        {
            get { return _formStandard == null ? "收费标准决定单价、计算规则与计费周期" : _formStandard.PriceSummary; }
        }

        public bool FormAllowOverride
        {
            get { return _formAllowOverride; }
            set { SetProperty(ref _formAllowOverride, value); }
        }

        /// <summary>出账可改价仅对一次性收费标准或自定义缴费对象开放（负责人裁定 ⑤）。</summary>
        public bool IsOverrideEditable
        {
            get
            {
                bool oneTime = _formStandard != null &&
                               (_formStandard.Dto.Specs ?? new List<ChargeStandardSpecDto>())
                                   .Any(x => x.Status == 0 && !string.IsNullOrWhiteSpace(x.CycleName) && x.CycleName.Contains("一次性"));
                return oneTime || IsCustomObjectSelected;
            }
        }

        public IRelayCommand OpenItemFormCommand { get; private set; }
        public IRelayCommand OpenEditItemCommand { get; private set; }
        public IAsyncRelayCommand SaveItemCommand { get; private set; }
        public IRelayCommand CancelItemFormCommand { get; private set; }

        private void StartNewItem()
        {
            _editingId = 0;
            RefreshFormStandardCandidates();
            FormStandard = null;
            FormObject = ChargeObjects.FirstOrDefault(x => IsFixedObjectCode(x.ItemCode) && x.ItemCode == "property")
                         ?? ChargeObjects.FirstOrDefault();
            FormEnabled = true;
            FormAllowOverride = false;
            OnPropertyChanged(nameof(FormTitle));
            OnPropertyChanged(nameof(IsEditing));
            IsFormVisible = true;
        }

        private void StartEditItem(ChargeItemRow row)
        {
            if (row == null) { return; }
            _editingId = row.Id;
            RefreshFormStandardCandidates();
            ChargeStandardRow match = FormStandardCandidates.FirstOrDefault(x => x.Id == (row.Dto.StandardId ?? 0));
            FormStandard = match;
            FormObject = ChargeObjects.FirstOrDefault(x => x.ItemCode == row.Dto.ObjectCode);
            FormEnabled = row.Dto.Status == 0;
            FormAllowOverride = row.Dto.AllowPriceOverride;
            OnPropertyChanged(nameof(FormTitle));
            OnPropertyChanged(nameof(IsEditing));
            IsFormVisible = true;
        }

        private void RefreshFormStandardCandidates()
        {
            FormStandardCandidates.Clear();
            string kw = (_formStandardKeyword ?? string.Empty).Trim();
            foreach (ChargeStandardRow row in Standards)
            {
                if (row.Dto.Status != 0) { continue; }
                if (kw.Length > 0 && row.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                FormStandardCandidates.Add(row);
            }
        }

        private async Task SaveItemAsync()
        {
            await RunAsync(async () =>
            {
                if (FormStandard == null) { throw new InvalidOperationException("请选择收费标准"); }
                if (FormObject == null) { throw new InvalidOperationException("请选择缴费对象"); }
                var request = new ChargeItemRequest
                {
                    StandardId = FormStandard.Id,
                    ObjectCode = FormObject.ItemCode,
                    ObjectType = IsCustomObjectSelected ? ChargeObjectType.Custom : (ChargeObjectType?)null,
                    Status = FormEnabled ? 0 : 1,
                    AllowPriceOverride = FormAllowOverride,
                    // 价格 / 单位 / 公式 / 周期由服务端按价目表带出（此处不再逐项提交）
                    Name = FormStandard.Name,
                    Category = FormStandard.Dto.Category,
                    PayMode = ChargePayMode.Monthly
                };
                if (_editingId > 0)
                {
                    await Api.UpdateChargeItemAsync(_editingId, request);
                }
                else
                {
                    await Api.CreateChargeItemAsync(request);
                }
                IsFormVisible = false;
                await LoadItemsCoreAsync();
            }, _editingId > 0 ? "收费项目已更新" : "收费项目已新增");
        }

        // ---------- 加载 ----------

        private async Task LoadStandardsCoreAsync()
        {
            int keepId = _selectedStandard == null ? 0 : _selectedStandard.Id;
            // FIX-v1.1.2-01：与收费项目列表同口径的并发加载保护（页签切换 + 刷新可能同时触发）
            int seq = ++_standardsLoadSeq;
            List<ChargeStandardDto> list = await Api.GetChargeStandardsAsync(StandardKeyword, null, true);
            List<ChargeStandardRow> rows = list.OrderBy(x => x.Id)
                .Select(x => new ChargeStandardRow { Dto = x })
                .ToList();
            lock (_standardsLoadSync)
            {
                if (seq != _standardsLoadSeq) { return; }
                Standards.Clear();
                foreach (ChargeStandardRow row in rows)
                {
                    Standards.Add(row);
                }
            }
            ChargeStandardRow keep = Standards.FirstOrDefault(x => x.Id == keepId) ?? Standards.FirstOrDefault();
            SelectedStandard = keep;
            await LoadVariablesAsync();
            RefreshFormStandardCandidates();
            RefreshObjectChips();
        }

        private async Task LoadVariablesAsync()
        {
            int seq = ++_variablesLoadSeq;
            List<ChargeVariableDto> list = await Api.GetChargeVariablesAsync(null, false);
            // CHG-v1.1.2-52：只采纳最后一次请求的结果（切换标准时会连续触发重载）
            if (seq != _variablesLoadSeq) { return; }

            ChargeStandardRow standard = _selectedStandard;
            List<int> bound = standard == null
                ? new List<int>()
                : (standard.Dto.Variables ?? new List<ChargeVariableDto>()).Select(x => x.Id).ToList();
            string formula = _formSpecFormula;
            VariableOptions.Clear();
            foreach (ChargeVariableDto dto in list)
            {
                VariableOptions.Add(new ChargeVariableOption
                {
                    Dto = dto,
                    IsBound = bound.Contains(dto.Id),
                    IsSelected = false
                });
            }
            // 记录变量区对应的标准：保存规格前若发现不一致，先重载再写绑定（防止把别的标准的绑定写进来）
            _variablesStandardId = standard == null ? 0 : standard.Id;
            MarkFormulaVariables(formula);
            OnPropertyChanged(nameof(SpecPriceUnitHint));
            OnPropertyChanged(nameof(BoundVariables));
            OnPropertyChanged(nameof(FormSpecVariableHint));
        }

        /// <summary>
        /// CHG-v1.1.2-52：切换收费标准后重载变量区（后台执行，失败只影响提示不影响主流程）。
        /// </summary>
        private async Task ReloadVariablesForSelectedStandardAsync()
        {
            try
            {
                await LoadVariablesAsync();
            }
            catch (Exception)
            {
                // 变量区取数失败不阻塞规格维护；保存规格前还有一次兜底重载
            }
        }

        private void RefreshSpecs()
        {
            Specs.Clear();
            if (_selectedStandard == null) { return; }
            foreach (ChargeStandardSpecDto spec in (_selectedStandard.Dto.Specs ?? new List<ChargeStandardSpecDto>())
                .OrderBy(x => x.IsFallback).ThenBy(x => x.Id))
            {
                Specs.Add(new ChargeSpecRow { Dto = spec, DisplayFormula = BuildDisplayFormula(spec.Formula) });
            }
        }

        /// <summary>把「{v:ID}」记号替换为该收费标准已绑定变量的名称（列表演示口径）。</summary>
        private string BuildDisplayFormula(string formula)
        {
            if (string.IsNullOrWhiteSpace(formula) || _selectedStandard == null) { return formula; }
            string text = formula;
            foreach (ChargeVariableDto variable in _selectedStandard.Dto.Variables ?? new List<ChargeVariableDto>())
            {
                text = text.Replace("{v:" + variable.Id + "}", variable.VarName);
            }
            // 未绑定（或已删除）的变量：不把落库记号直接暴露在列表里
            while (text.Contains("{v:"))
            {
                int start = text.IndexOf("{v:", StringComparison.Ordinal);
                int end = text.IndexOf('}', start);
                if (end < 0) { break; }
                text = text.Substring(0, start) + "未绑定变量" + text.Substring(end + 1);
            }
            return text;
        }

        /// <summary>页签与抽屉命令装配（由构造函数调用）。</summary>
        private void InitPriceList()
        {
            SwitchTabCommand = new RelayCommand<string>(SwitchTab);
            EnterBatchModeCommand = new RelayCommand(EnterBatchMode);
            CancelBatchCommand = new RelayCommand(CancelBatch);
            RequestBatchConfirmCommand = new RelayCommand(RequestBatchConfirm);
            ConfirmBatchCommand = new AsyncRelayCommand(ConfirmBatchAsync);
            ExportPdfCommand = new AsyncRelayCommand(ExportPdfAsync);
            NewSpecCommand = new RelayCommand(StartNewSpec);
            EditSpecCommand = new RelayCommand<ChargeSpecRow>(StartEditSpec);
            CancelSpecCommand = new RelayCommand(() => { IsSpecFormVisible = false; });
            SaveSpecCommand = new AsyncRelayCommand(SaveSpecAsync);
            ToggleSpecCommand = new AsyncRelayCommand<ChargeSpecRow>(ToggleSpecAsync);
            DeleteSpecCommand = new RelayCommand<ChargeSpecRow>(row => _ = DeleteSpecAsync(row));
            ToggleVariableCommand = new RelayCommand<ChargeVariableOption>(ToggleVariable);
            ToggleBindVariableCommand = new RelayCommand<ChargeVariableOption>(ToggleBindVariable);
            NewStandardCommand = new RelayCommand(StartNewStandard);
            EditStandardCommand = new RelayCommand(StartEditStandard);
            CancelStandardCommand = new RelayCommand(() => { IsStandardFormVisible = false; });
            SaveStandardCommand = new AsyncRelayCommand(SaveStandardAsync);
            ToggleStandardCommand = new AsyncRelayCommand(ToggleStandardAsync);
            // CHG-v1.1.2-35：删除收费标准改为「确认弹窗 → 执行」
            DeleteStandardCommand = new RelayCommand(StartDeleteStandard);
            ConfirmActionCommand = new RelayCommand(ConfirmAction);
            CancelActionCommand = new RelayCommand(CancelAction);
            CloseNoticeCommand = new RelayCommand(CloseNotice);
            OpenItemFormCommand = new RelayCommand(StartNewItem);
            SelectObjectCommand = new RelayCommand<ChargeObjectChip>(SelectObject);
            OpenEditItemCommand = new RelayCommand<ChargeItemRow>(StartEditItem);
            SaveItemCommand = new AsyncRelayCommand(SaveItemAsync);
            CancelItemFormCommand = new RelayCommand(() => { IsFormVisible = false; });
        }
    }
}
