using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>自定义缴费对象的计量参数输入行（CHG-v1.1.2-26：来源=手填 / 固定值不显示）。</summary>
    public class BillMeasureInputRow : ObservableObject
    {
        private string _valueText;
        public int VariableId { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public bool IsReadOnly { get; set; }
        public string Hint { get; set; }
        public string Label { get { return Name; } }
        public string UnitText { get { return string.IsNullOrWhiteSpace(Unit) ? "—" : Unit; } }
        public string ValueText
        {
            get { return _valueText; }
            set { if (SetProperty(ref _valueText, value)) { OnPropertyChanged(nameof(Amount)); } }
        }
        public decimal Amount
        {
            get
            {
                decimal value;
                return decimal.TryParse((_valueText ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    ? value
                    : 0m;
            }
        }
    }

    /// <summary>
    /// 账单工作台 —— 「出账预演 + 自定义缴费对象计量」（CHG-v1.1.2-26，原型 ⑤ ⑥）。
    /// ① 勾选/取消、切换收费项目与计费周期 → 自动试算，逐行显示命中的规格、单价与预估金额；
    /// ② 自定义缴费对象（无档案）→ 手选规格 + 手填计量参数，并展示完整算式。
    /// </summary>
    public partial class BillWorkbenchViewModel
    {
        private string _previewSummaryText = string.Empty;
        private string _previewFailText = string.Empty;
        private DispatcherTimer _previewDebounce;
        private int _previewSeq;
        private ChargeStandardDto _customStandard;
        /// <summary>CHG-v1.1.2-34：本收费标准启用规格引用到的「手填」变量（档案对象出账行内填写）。</summary>
        private List<ChargeVariableDto> _standardMeasureVars = new List<ChargeVariableDto>();
        /// <summary>CHG-v1.1.2-34：已填写的档案对象计量参数（键 = 缴费对象类型:ID，跨搜索保持）。</summary>
        private readonly Dictionary<string, Dictionary<int, decimal>> _objectMeasures =
            new Dictionary<string, Dictionary<int, decimal>>(StringComparer.Ordinal);
        /// <summary>档案对象计量参数是否按行渲染（无手填变量时回到只读展示）。</summary>
        public bool HasStandardMeasures { get { return _standardMeasureVars.Count > 0; } }

        /// <summary>
        /// CHG-v1.1.2-37：自定义缴费对象「取价目表失败」的可见原因。
        /// 原实现把取标准失败静默吞掉（catch → 标准置空），界面只表现为「规格下拉框/计量参数全空、一个字都不提示」，
        /// 用户无法区分「没维护规格」「项目没绑标准」「后端没起来」。
        /// </summary>
        private string _priceListErrorText = string.Empty;
        public bool HasPriceListError { get { return !string.IsNullOrWhiteSpace(_priceListErrorText); } }
        public string PriceListErrorText { get { return _priceListErrorText; } private set { _priceListErrorText = value; OnPropertyChanged(nameof(PriceListErrorText)); OnPropertyChanged(nameof(HasPriceListError)); } }
        /// <summary>重新加载价目表（取数失败后的重试入口）。</summary>
        public IAsyncRelayCommand ReloadPriceListCommand { get; private set; }

        /// <summary>试算摘要（已选 N 个对象 · 其中 M 条走兜底 · 预估合计 ¥X）。</summary>
        public string PreviewSummaryText { get { return _previewSummaryText; } private set { SetProperty(ref _previewSummaryText, value); } }

        /// <summary>未命中规格的行提示（进入失败明细前先给出预警）。</summary>
        public string PreviewFailText { get { return _previewFailText; } private set { SetProperty(ref _previewFailText, value); } }

        public IAsyncRelayCommand PreviewCommand { get; private set; }

        private void InitBillingPreview()
        {
            _previewDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _previewDebounce.Tick += async (s, e) =>
            {
                _previewDebounce.Stop();
                await PreviewAsync();
            };
            PreviewCommand = new AsyncRelayCommand(PreviewAsync);
            ReloadPriceListCommand = new AsyncRelayCommand(ReloadCustomPayerTemplateAsync);
            PropertyChanged += OnWorkbenchPropertyChanged;
        }

        private void OnWorkbenchPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectedItem) || e.PropertyName == nameof(SelectedCycle))
            {
                SchedulePreview();
            }
        }

        /// <summary>勾选/取消勾选后触发（由 NotifyObjectSelectionChanged 调用）。</summary>
        private void SchedulePreview()
        {
            if (!IsGenerateVisible) { return; }
            _previewDebounce.Stop();
            _previewDebounce.Start();
        }

        private BillPreviewRequest BuildPreviewRequest()
        {
            var request = new BillPreviewRequest { ChargeItemId = SelectedItem == null ? 0 : SelectedItem.Id };
            if (SelectedCycle != null) { request.CycleId = SelectedCycle.Id; }
            if (IsCustomChargeObject)
            {
                request.CustomPayers = BuildCustomPayerRequests();
                return request;
            }
            // CHG-v1.1.2-34：档案对象的「手填」计量参数与生成账单同口径（试算即结果）
            request.ObjectMeasures = BuildObjectMeasures();
            List<int> ids = SelectedObjectIds();
            if (ObjectKindIndex == 1) { request.ParkingIds = ids; }
            else if (ObjectKindIndex == 2) { request.OwnerIds = ids; }
            else { request.PropertyIds = ids; }
            return request;
        }

        private async Task PreviewAsync()
        {
            if (!IsGenerateVisible || SelectedItem == null)
            {
                PreviewSummaryText = string.Empty;
                PreviewFailText = string.Empty;
                return;
            }
            BillPreviewRequest request = BuildPreviewRequest();
            bool hasScope = (request.PropertyIds != null && request.PropertyIds.Count > 0)
                            || (request.ParkingIds != null && request.ParkingIds.Count > 0)
                            || (request.OwnerIds != null && request.OwnerIds.Count > 0)
                            || (request.CustomPayers != null && request.CustomPayers.Count > 0);
            if (!hasScope)
            {
                PreviewSummaryText = IsCustomChargeObject ? "请先填写缴费对象名称" : "请先勾选缴费对象";
                PreviewFailText = string.Empty;
                ResetPreviewCells();
                return;
            }

            int seq = ++_previewSeq;
            try
            {
                BillPreviewResult result = await Api.PreviewBillsAsync(request);
                if (seq != _previewSeq) { return; }
                ApplyPreviewResult(result);
            }
            catch (Exception ex)
            {
                PreviewSummaryText = "试算失败：" + ex.Message;
                PreviewFailText = string.Empty;
            }
        }

        private void ResetPreviewCells()
        {
            foreach (BillObjectRow row in BillObjects) { row.ClearPreview(); }
            foreach (BillCustomPayerRow row in CustomPayers) { row.ClearPreview(); }
        }

        private void ApplyPreviewResult(BillPreviewResult result)
        {
            var byId = new Dictionary<int, BillPreviewRowDto>();
            var byName = new Dictionary<string, BillPreviewRowDto>(StringComparer.OrdinalIgnoreCase);
            foreach (BillPreviewRowDto row in result.Rows ?? new List<BillPreviewRowDto>())
            {
                string[] parts = (row.ObjectKey ?? string.Empty).Split(':');
                if (parts.Length < 3) { byName[row.ObjectText ?? string.Empty] = row; continue; }
                int id;
                if (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0)
                {
                    byId[id] = row;
                }
                else
                {
                    byName[parts[2]] = row;
                }
            }

            foreach (BillObjectRow row in BillObjects)
            {
                if (!row.IsChecked) { row.ClearPreview(); continue; }
                BillPreviewRowDto hit;
                if (row.Dto != null && byId.TryGetValue(row.Dto.Id, out hit)) { row.ApplyPreview(hit); }
                else { row.ClearPreview(); }
            }
            foreach (BillCustomPayerRow row in CustomPayers)
            {
                BillPreviewRowDto hit;
                if (!string.IsNullOrWhiteSpace(row.Name) && byName.TryGetValue(row.Name.Trim(), out hit)) { row.ApplyPreview(hit); }
                else { row.ClearPreview(); }
            }

            PreviewSummaryText = "已选 " + result.MatchedCount + " 个对象 · 其中 " + result.FallbackCount +
                                 " 条走兜底规格 · 预估合计 ¥" + result.TotalAmount.ToString("0.00");
            PreviewFailText = result.FailedCount == 0
                ? string.Empty
                // CHG-v1.1.2-52：原口径固定写「未命中规格」，会把「变量已解绑 / 公式非法」等真实原因掩盖掉
                : "有 " + result.FailedCount + " 个对象无法出账（发布后进入失败明细）：" +
                  string.Join("；", (result.Rows ?? new List<BillPreviewRowDto>())
                      .Where(x => !x.Matched)
                      .Take(3)
                      .Select(x => x.ObjectText + " → " + x.Reason));
        }

        // ---------- 自定义缴费对象：规格手选 + 计量参数手填 ----------

        // ---------- CHG-v1.1.2-34：档案对象（房产 / 车位 / 业主）的手填计量参数 ----------

        /// <summary>
        /// 取本收费标准启用规格引用到的「手填」变量：价目表公式里的手填变量在档案对象下没有取值来源
        /// （档案自动 / 周期派生由系统带出、固定值不显示），故按对象逐行填写。
        /// </summary>
        private async Task LoadStandardMeasureVarsAsync()
        {
            _standardMeasureVars = new List<ChargeVariableDto>();
            int? standardId = SelectedItem == null ? null : SelectedItem.StandardId;
            if (!standardId.HasValue)
            {
                OnPropertyChanged(nameof(HasStandardMeasures));
                return;
            }
            try
            {
                ChargeStandardDto standard = await Api.GetChargeStandardAsync(standardId.Value);
                // CHG-v1.2.0-12：同时取「启用中的规格」作为出账行的手选候选（未手选＝自动匹配）
                BuildSpecOptions(standard);
                var referenced = new HashSet<int>();
                foreach (ChargeStandardSpecDto spec in (standard == null ? new List<ChargeStandardSpecDto>() : standard.Specs ?? new List<ChargeStandardSpecDto>())
                    .Where(x => x.Status == 0))
                {
                    foreach (ChargeFormulaVarDto used in spec.FormulaVars ?? new List<ChargeFormulaVarDto>())
                    {
                        referenced.Add(used.Id);
                    }
                }
                _standardMeasureVars = (standard == null ? new List<ChargeVariableDto>() : standard.Variables ?? new List<ChargeVariableDto>())
                    .Where(x => x.Source == ChargeVariableSource.Manual && referenced.Contains(x.Id))
                    .ToList();
            }
            catch (Exception)
            {
                _standardMeasureVars = new List<ChargeVariableDto>();
                _standardSpecOptions = new List<BillSpecOption>();
            }
            OnPropertyChanged(nameof(HasStandardMeasures));
            OnPropertyChanged(nameof(CanPickSpec));
            OnPropertyChanged(nameof(SpecPickHintText));
        }

        /// <summary>价目表启用规格 → 下拉候选（首项为「自动匹配」哨兵）。</summary>
        private void BuildSpecOptions(ChargeStandardDto standard)
        {
            var options = new List<BillSpecOption>
            {
                new BillSpecOption { SpecId = 0, IsAuto = true, SpecName = "自动匹配" }
            };
            foreach (ChargeStandardSpecDto spec in (standard == null ? new List<ChargeStandardSpecDto>() : standard.Specs ?? new List<ChargeStandardSpecDto>())
                .Where(x => x.Status == 0)
                .OrderByDescending(x => x.IsFallback)
                .ThenBy(x => x.Id))
            {
                options.Add(new BillSpecOption
                {
                    SpecId = spec.Id,
                    SpecName = spec.SpecName,
                    UnitPrice = spec.UnitPrice,
                    PriceUnit = spec.PriceUnit,
                    MatchText = DescribeSpecMatch(spec)
                });
            }
            _standardSpecOptions = options;
            // CHG-v1.2.0-15：表头批选候选同步（默认选中「自动匹配」，或保留已批选的规格）
            BatchSpecOptions.Clear();
            foreach (BillSpecOption option in options) { BatchSpecOptions.Add(option); }
            _applyingSpecTemplate = true;
            try
            {
                _selectedBatchSpec = _defaultSpecId.HasValue
                    ? (BatchSpecOptions.FirstOrDefault(x => !x.IsAuto && x.SpecId == _defaultSpecId.Value) ?? BatchSpecOptions.FirstOrDefault())
                    : BatchSpecOptions.FirstOrDefault();
            }
            finally
            {
                _applyingSpecTemplate = false;
            }
            OnPropertyChanged(nameof(SelectedBatchSpec));
        }

        /// <summary>规格适用条件摘要（与价目表口径一致：住宅/商铺…、楼栋、车位类型，兜底显示「不限」）。</summary>
        private static string DescribeSpecMatch(ChargeStandardSpecDto spec)
        {
            if (spec.IsFallback) { return "不限（兜底）"; }
            var parts = new List<string>();
            if (spec.MatchUsage.HasValue)
                parts.Add(spec.MatchUsage.Value == 0 ? "住宅" : (spec.MatchUsage.Value == 1 ? "商铺" : "空置"));
            if (spec.MatchStatus.HasValue)
                parts.Add("状态" + spec.MatchStatus.Value);
            if (spec.MatchSpaceType.HasValue)
                parts.Add(spec.MatchSpaceType.Value == 0 ? "产权车位" : (spec.MatchSpaceType.Value == 1 ? "普通车位" : "临时车位"));
            if (!string.IsNullOrWhiteSpace(spec.MatchBuilding)) { parts.Add(spec.MatchBuilding.Trim()); }
            return parts.Count == 0 ? "不限" : string.Join(" / ", parts);
        }

        /// <summary>把「手填」计量参数模板铺到当前可见的缴费对象行（已填值按 类型:ID 回填）。</summary>
        private void ApplyMeasureTemplateToRows()
        {
            foreach (BillObjectRow row in BillObjects)
            {
                row.Measures.Clear();
                if (_standardMeasureVars.Count == 0 || row.Dto == null) { continue; }
                string key = ObjectKey(row.Dto.Id);
                Dictionary<int, decimal> stored;
                _objectMeasures.TryGetValue(key, out stored);
                foreach (ChargeVariableDto variable in _standardMeasureVars)
                {
                    decimal existing;
                    string text = stored != null && stored.TryGetValue(variable.Id, out existing) && existing > 0m
                        ? existing.ToString("0.##")
                        : (variable.DefaultValue.HasValue && variable.DefaultValue.Value > 0m
                            ? variable.DefaultValue.Value.ToString("0.##")
                            : string.Empty);
                    var input = new BillMeasureInputRow
                    {
                        VariableId = variable.Id,
                        Name = variable.VarName,
                        Unit = variable.Unit,
                        IsReadOnly = false,
                        Hint = "出账计量：按缴费对象逐行填写（价目表「手填」变量）",
                        ValueText = text
                    };
                    decimal capturedAmount = input.Amount;
                    int variableId = variable.Id;
                    input.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName != nameof(BillMeasureInputRow.ValueText)) { return; }
                        decimal value = ((BillMeasureInputRow)s).Amount;
                        Dictionary<int, decimal> map;
                        if (!_objectMeasures.TryGetValue(key, out map))
                        {
                            map = new Dictionary<int, decimal>();
                            _objectMeasures[key] = map;
                        }
                        if (value > 0m) { map[variableId] = value; } else { map.Remove(variableId); }
                        if (value != capturedAmount)
                        {
                            capturedAmount = value;
                            SchedulePreview();
                        }
                    };
                    row.Measures.Add(input);
                }
                row.NotifyMeasuresChanged();
            }
            // CHG-v1.1.2-50：出账改价输入行与「手填」计量同批铺入（同一个行集合生命周期）
            ApplyPriceOverrideTemplateToRows();
            // CHG-v1.2.0-12：规格候选与手选同批铺入（未手选＝自动匹配）
            ApplySpecTemplateToRows();
        }

        /// <summary>收费项目（= 收费标准）切换后重新解析手填变量并铺到行上。</summary>
        private async Task ReloadMeasureTemplateAsync()
        {
            if (IsCustomChargeObject)
            {
                _standardMeasureVars = new List<ChargeVariableDto>();
                OnPropertyChanged(nameof(HasStandardMeasures));
                return;
            }
            await LoadStandardMeasureVarsAsync();
            ApplyMeasureTemplateToRows();
        }

        /// <summary>本次出账的手填计量参数（只提交已勾选且填写了取值的对象）。</summary>
        private List<BillObjectMeasureRequest> BuildObjectMeasures()
        {
            var list = new List<BillObjectMeasureRequest>();
            // CHG-v1.1.2-50：收费项目开启「出账时可改价」时，即使没有手填计量变量也要提交改价
            if (IsCustomChargeObject) { return list; }
            string prefix = ObjectKind + ":";
            foreach (string key in _checkedObjectKeys)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) { continue; }
                int id;
                if (!int.TryParse(key.Substring(prefix.Length), out id)) { continue; }
                Dictionary<int, decimal> map;
                bool hasMeasures = _objectMeasures.TryGetValue(key, out map) && map.Count > 0;
                decimal price;
                bool hasPrice = _objectPrices.TryGetValue(key, out price) && price > 0m;
                // CHG-v1.2.0-12：手选规格（未手选 = 不提交，服务端按自动匹配计价）
                int specId;
                bool hasSpec = _objectSpecs.TryGetValue(key, out specId) && specId > 0;
                // CHG-v1.2.0-15：单行未手选时用表头批选的默认规格
                if (!hasSpec && _defaultSpecId.HasValue)
                {
                    specId = _defaultSpecId.Value;
                    hasSpec = specId > 0;
                }
                if (!hasMeasures && !hasPrice && !hasSpec) { continue; }
                list.Add(new BillObjectMeasureRequest
                {
                    Kind = ObjectKind,
                    ObjectId = id,
                    Measures = hasMeasures ? new Dictionary<int, decimal>(map) : null,
                    UnitPriceOverride = hasPrice ? (decimal?)price : null,
                    SpecId = hasSpec ? (int?)specId : null
                });
            }
            return list;
        }

        /// <summary>关闭/重开生成账单弹窗时清空档案对象的行内手填计量参数。</summary>
        private void ClearObjectMeasures()
        {
            _objectMeasures.Clear();
            foreach (BillObjectRow row in BillObjects) { row.Measures.Clear(); row.NotifyMeasuresChanged(); }
        }

        /// <summary>按所选收费项目的收费标准，为每一行自定义缴费对象铺好「规格候选 + 计量参数」。</summary>
        private async Task ReloadCustomPayerTemplateAsync()
        {
            _customStandard = null;
            PriceListErrorText = string.Empty;
            if (SelectedItem == null)
            {
                PriceListErrorText = "尚未选择收费项目";
            }
            else if (!SelectedItem.StandardId.HasValue)
            {
                PriceListErrorText = "收费项目「" + SelectedItem.Name + "」未绑定收费标准（价目表），请先在「收费项目维护」重新选择收费标准后再出账";
            }
            else
            {
                try
                {
                    _customStandard = await Api.GetChargeStandardAsync(SelectedItem.StandardId.Value);
                }
                catch (Exception ex)
                {
                    _customStandard = null;
                    PriceListErrorText = "未取到该收费项目的价目表（连接本地服务失败：" + ex.Message +
                                         "）。请确认后端服务已启动，然后点「重新加载价目表」";
                }
            }
            EnsureCustomPayerRow();
            foreach (BillCustomPayerRow row in CustomPayers) { row.ApplyStandard(_customStandard); }
            if (!HasPriceListError && _customStandard != null &&
                (CustomPayers.Count == 0 || CustomPayers[0].HasSpecWarning))
            {
                PriceListErrorText = "收费标准「" + _customStandard.Name + "」没有启用中的规格，请先在「价目表」页签补充规格";
            }
        }

        /// <summary>计量参数 / 规格变化后重算试算（由行的属性变更触发）。</summary>
        private void OnCustomPayerRowChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BillCustomPayerRow.Name)
                || e.PropertyName == nameof(BillCustomPayerRow.SelectedSpec))
            {
                SchedulePreview();
            }
            else if (e.PropertyName == nameof(BillCustomPayerRow.UnitPriceOverrideText))
            {
                // CHG-v1.1.2-50：自定义缴费对象的出账改价（行内保存，不走「类型:ID」字典）
                if (!_applyingPriceTemplate) { SchedulePreview(); }
            }
        }

        /// <summary>生成账单请求中的自定义缴费对象明细（含手选规格与手填计量参数）。</summary>
        private List<BillCustomPayerRequest> BuildCustomPayerRequests()
        {
            var list = new List<BillCustomPayerRequest>();
            foreach (BillCustomPayerRow row in CustomPayers)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Name)) { continue; }
                var request = new BillCustomPayerRequest
                {
                    PayerName = row.Name.Trim(),
                    ContractNo = row.ContractNo,
                    SpecId = row.SelectedSpec == null ? (int?)null : row.SelectedSpec.Id,
                    // CHG-v1.1.2-50：出账改价（留空＝按所选规格的价目表单价）
                    UnitPriceOverride = row.UnitPriceOverride,
                    Measures = new Dictionary<int, decimal>()
                };
                foreach (BillMeasureInputRow measure in row.Measures)
                {
                    request.Measures[measure.VariableId] = measure.Amount;
                }
                list.Add(request);
            }
            return list;
        }
    }

    /// <summary>缴费对象行的试算展示（规格 / 单价 / 计量 / 预估金额 / 状态）。CHG-v1.1.2-26。</summary>
    public partial class BillObjectRow
    {
        private string _specText = "—";
        private string _priceText = "—";
        private string _measureText = "—";
        private string _amountText = "—";
        private string _previewState = "未勾选";
        private Brush _previewBrush = Brushes.Gray;
        private Brush _previewBg = Brushes.Transparent;
        private string _previewReason;
        private bool _hasMeasures;

        public string SpecText { get { return _specText; } private set { SetProperty(ref _specText, value); } }
        public string PriceText { get { return _priceText; } private set { SetProperty(ref _priceText, value); } }
        public string MeasureText { get { return _measureText; } private set { SetProperty(ref _measureText, value); } }
        public string AmountText { get { return _amountText; } private set { SetProperty(ref _amountText, value); } }
        public string PreviewState { get { return _previewState; } private set { SetProperty(ref _previewState, value); } }
        public Brush PreviewBrush { get { return _previewBrush; } private set { SetProperty(ref _previewBrush, value); } }
        public Brush PreviewBg { get { return _previewBg; } private set { SetProperty(ref _previewBg, value); } }
        public string PreviewReason { get { return _previewReason; } private set { SetProperty(ref _previewReason, value); } }

        /// <summary>CHG-v1.1.2-34：本行需要填写的「手填」计量参数（价目表公式引用到的）。</summary>
        public ObservableCollection<BillMeasureInputRow> Measures { get; } = new ObservableCollection<BillMeasureInputRow>();
        public bool HasMeasures { get { return _hasMeasures; } private set { SetProperty(ref _hasMeasures, value); } }

        public void NotifyMeasuresChanged()
        {
            HasMeasures = Measures.Count > 0;
        }

        public void ClearPreview()
        {
            SpecText = "—";
            PriceText = "—";
            PriceUnitSuffix = string.Empty;
            IsPriceOverridden = false;
            MeasureText = "—";
            AmountText = "—";
            PreviewState = IsChecked ? "待试算" : "未勾选";
            PreviewBrush = PreviewBrushes.Muted;
            PreviewBg = Brushes.Transparent;
            PreviewReason = null;
        }

        public void ApplyPreview(BillPreviewRowDto dto)
        {
            if (dto == null) { ClearPreview(); return; }
            if (!dto.Matched)
            {
                SpecText = "未命中";
                PriceText = "—";
                PriceUnitSuffix = string.Empty;
                IsPriceOverridden = false;
                MeasureText = "—";
                AmountText = "—";
                PreviewState = "失败";
                PreviewBrush = PreviewBrushes.Warn;
                PreviewBg = PreviewBrushes.WarnBg;
                PreviewReason = dto.Reason;
                return;
            }
            SpecText = string.IsNullOrWhiteSpace(dto.SpecName) ? "统一价" : dto.SpecName;
            PriceText = "¥" + dto.UnitPrice.ToString("0.00") + (string.IsNullOrWhiteSpace(dto.PriceUnit) ? string.Empty : "/" + dto.PriceUnit);
            // CHG-v1.1.2-50：改价行的「单价单位」单独展示，供「改价输入 + 单位」并排；并标注本行已改价
            PriceUnitSuffix = string.IsNullOrWhiteSpace(dto.PriceUnit) ? string.Empty : "/" + dto.PriceUnit;
            IsPriceOverridden = dto.UnitPriceOverridden;
            MeasureText = string.IsNullOrWhiteSpace(dto.MeasureText) ? "—" : dto.MeasureText;
            AmountText = "¥" + dto.Amount.ToString("0.00");
            PreviewState = dto.IsFallback ? "兜底" : "正常";
            PreviewBrush = dto.IsFallback ? PreviewBrushes.Warn : PreviewBrushes.Ok;
            PreviewBg = dto.IsFallback ? PreviewBrushes.WarnBg : PreviewBrushes.OkBg;
            PreviewReason = dto.IsFallback ? "命中兜底规格，请复核"
                : (dto.UnitPriceOverridden ? "本行按「出账时可改价」的单价计价，价目表单价不变" : null);
        }
    }

    /// <summary>自定义缴费对象行的规格与计量参数（原型 ⑥）。CHG-v1.1.2-26。</summary>
    public partial class BillCustomPayerRow
    {
        private string _contractNo = string.Empty;
        private ChargeStandardSpecDto _selectedSpec;
        private string _specText = "—";
        private string _processText = "—";
        private string _amountText = "—";
        private string _previewState = "待试算";

        public ObservableCollection<ChargeStandardSpecDto> SpecOptions { get; } = new ObservableCollection<ChargeStandardSpecDto>();
        public ObservableCollection<BillMeasureInputRow> Measures { get; } = new ObservableCollection<BillMeasureInputRow>();

        /// <summary>合同 / 备注编号（选填，用于区分同名对象）。</summary>
        public string ContractNo { get { return _contractNo; } set { SetProperty(ref _contractNo, value); } }

        public ChargeStandardSpecDto SelectedSpec
        {
            get { return _selectedSpec; }
            set
            {
                // CHG-v1.1.2-38：下拉框在「清空 + 重填候选」过程中会把 SelectedItem 回写成 null，
                // 单一规格时不能让这次回写把已选规格清掉 —— 否则首次打开弹窗时「规格 / 计量参数」全空，
                // 切到其它页面再回来（重新加载一遍）才显示出规格。
                ChargeStandardSpecDto effective = value;
                if (effective == null && SpecOptions.Count == 1) { effective = SpecOptions[0]; }
                if (!SetProperty(ref _selectedSpec, effective)) { return; }
                SpecText = effective == null
                    ? (SpecOptions.Count == 0 ? "无可用规格" : "请选择规格")
                    : effective.SpecName;
                RefreshMeasures();
            }
        }

        public string SpecText { get { return _specText; } private set { SetProperty(ref _specText, value); } }
        public string ProcessText { get { return _processText; } private set { SetProperty(ref _processText, value); } }
        public string AmountText { get { return _amountText; } private set { SetProperty(ref _amountText, value); } }
        public string PreviewState { get { return _previewState; } private set { SetProperty(ref _previewState, value); } }

        private ChargeStandardDto _standard;

        /// <summary>铺入收费标准的规格候选与计量参数（无档案对象：档案自动类参数降级为手填）。</summary>
        public void ApplyStandard(ChargeStandardDto standard)
        {
            List<ChargeStandardSpecDto> specs = standard == null
                ? new List<ChargeStandardSpecDto>()
                : (standard.Specs ?? new List<ChargeStandardSpecDto>())
                    .Where(x => x.Status == 0)
                    .OrderBy(x => x.IsFallback).ThenBy(x => x.Id)
                    .ToList();
            // CHG-v1.1.2-38：候选未变化时不重建集合 —— 避免下拉框被反复「清空 + 重填」打断选中状态
            // （负责人实测：填写缴费对象名称 → 切走页面 → 切回来再点「生成账单」，规格才出现）。
            bool sameSelection = ReferenceEquals(_standard, standard)
                                 && SpecOptions.Count == specs.Count
                                 && specs.All(x => SpecOptions.Any(y => y.Id == x.Id));
            _standard = standard;
            if (!sameSelection)
            {
                SpecOptions.Clear();
                foreach (ChargeStandardSpecDto spec in specs)
                {
                    SpecOptions.Add(spec);
                }
            }
            // CHG-v1.1.2-36：换收费标准（或原手选规格已不在候选内）时必须重置手选规格 ——
            // 否则会沿用上一条标准的旧规格，导致「计量参数」按旧公式渲染（甚至整行空白）。
            if (standard == null)
            {
                SelectedSpec = null;
                SpecText = "未取到收费标准";
            }
            else if (_selectedSpec == null || !SpecOptions.Any(x => x.Id == _selectedSpec.Id))
            {
                // 只有一条规格时自动选中（负责人裁定：无档案对象规格手选，单条规格免选）
                SelectedSpec = SpecOptions.Count == 1 ? SpecOptions[0] : null;
                OnPropertyChanged(nameof(SelectedSpec));
            }
            OnPropertyChanged(nameof(HasSpecWarning));
            RefreshMeasures();
        }

        /// <summary>CHG-v1.1.2-36：该收费标准没有启用中的规格（界面给出明确引导，而不是空白下拉框）。</summary>
        public bool HasSpecWarning { get { return _standard != null && SpecOptions.Count == 0; } }

        private void RefreshMeasures()
        {
            foreach (BillMeasureInputRow existing in Measures) { existing.PropertyChanged -= OnMeasureChanged; }
            Measures.Clear();
            if (_standard == null || _selectedSpec == null) { return; }
            // CHG-v1.1.2-36：只显示「所选规格的计算规则真正引用」的计量项 ——
            // 公式未用到的变量不再出现空输入框（原型 ⑥：显示哪几项由所选规格的公式决定）。
            var used = new HashSet<int>((_selectedSpec.FormulaVars ?? new List<ChargeFormulaVarDto>()).Select(x => x.Id));
            foreach (ChargeVariableDto variable in _standard.Variables ?? new List<ChargeVariableDto>())
            {
                if (variable.Source == ChargeVariableSource.Fixed) { continue; }
                if (!used.Contains(variable.Id)) { continue; }
                bool readOnly = variable.Source == ChargeVariableSource.CycleDerived;
                var input = new BillMeasureInputRow
                {
                    VariableId = variable.Id,
                    Name = variable.VarName,
                    Unit = variable.Unit,
                    IsReadOnly = readOnly,
                    Hint = readOnly
                        ? "由计费周期自动折算（出账时由服务端计算），无需填写"
                        : (variable.Source == ChargeVariableSource.Archive ? "无档案对象：手工填写" : "按缴费对象手工填写"),
                    ValueText = readOnly
                        ? "自动"
                        : (variable.DefaultValue.HasValue && variable.DefaultValue.Value > 0m
                            ? variable.DefaultValue.Value.ToString("0.##")
                            : string.Empty)
                };
                if (!readOnly) { input.PropertyChanged += OnMeasureChanged; }
                Measures.Add(input);
            }
        }

        /// <summary>CHG-v1.1.2-36：手填计量参数变化后由 VM 重算出账预演（金额与算式随之刷新）。</summary>
        public event EventHandler MeasuresChanged;

        private void OnMeasureChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BillMeasureInputRow.ValueText)) { return; }
            EventHandler handler = MeasuresChanged;
            if (handler != null) { handler(this, EventArgs.Empty); }
        }

        public void ClearPreview()
        {
            ProcessText = "—";
            AmountText = "—";
            PreviewState = "待试算";
        }

        public void ApplyPreview(BillPreviewRowDto dto)
        {
            if (dto == null || !dto.Matched)
            {
                ProcessText = dto == null || string.IsNullOrWhiteSpace(dto.Reason) ? "—" : dto.Reason;
                AmountText = "—";
                PreviewState = dto == null ? "待试算" : "失败";
                return;
            }
            ProcessText = "¥" + dto.UnitPrice.ToString("0.00") +
                          (string.IsNullOrWhiteSpace(dto.PriceUnit) ? string.Empty : "/" + dto.PriceUnit) +
                          (string.IsNullOrWhiteSpace(dto.MeasureText) ? string.Empty : " × " + dto.MeasureText);
            AmountText = "¥" + dto.Amount.ToString("0.00");
            PreviewState = "已试算";
        }
    }

    internal static class PreviewBrushes
    {
        public static readonly Brush Ok = Freeze("#12805C");
        public static readonly Brush OkBg = Freeze("#E8F7F1");
        public static readonly Brush Warn = Freeze("#B76E00");
        public static readonly Brush WarnBg = Freeze("#FFF5DC");
        public static readonly Brush Muted = Freeze("#98A2B3");

        private static Brush Freeze(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            brush.Freeze();
            return brush;
        }
    }
}
