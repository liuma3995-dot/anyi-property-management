using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>
    /// 账单工作台 —— 出账「规格（自动匹配 / 可手选）」（CHG-v1.2.0-12）。
    ///
    /// 背景（负责人 2026-09-21）：同一价目表里不同规格价格不同，但自动匹配只在「适用条件命中 → 兜底」之间择一，
    /// 实际出账常常只用到同一条规格。现允许在生成账单表单里**逐行手选规格**：
    /// 不选＝沿用自动匹配（原口径）；选了＝该行按所选规格计价并写进账单快照（试算与生成同口径）。
    /// </summary>
    public partial class BillWorkbenchViewModel
    {
        /// <summary>档案对象的手选规格（键 = 缴费对象类型:ID，跨搜索保持；与手填计量/改价同口径）。</summary>
        private readonly Dictionary<string, int> _objectSpecs = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>当前收费项目绑定的价目表里**启用中**的规格（下拉候选，含「自动匹配」哨兵项）。</summary>
        private List<BillSpecOption> _standardSpecOptions = new List<BillSpecOption>();

        /// <summary>
        /// 本批默认规格（CHG-v1.2.0-15，负责人 2026-09-21「表头批选」）：表头下拉框选定后套用到本批全部缴费对象；
        /// 为空 = 自动匹配（按适用条件）。单行仍可手选覆盖（<see cref="_objectSpecs"/>）。
        /// </summary>
        private int? _defaultSpecId;

        /// <summary>铺候选/回填期间抑制行内下拉框变化被记成「单行手选」。</summary>
        private bool _applyingSpecTemplate;

        /// <summary>表头「规格（默认/可手选）」下拉候选（首项＝自动匹配）。</summary>
        public ObservableCollection<BillSpecOption> BatchSpecOptions { get; } = new ObservableCollection<BillSpecOption>();

        private BillSpecOption _selectedBatchSpec;

        /// <summary>
        /// 表头批选规格（默认规格）：选择后本批全部缴费对象按该规格 + 其计算规则出账，无需逐条单选；
        /// 选「自动匹配」＝回到按适用条件自动判定。单行下拉框仍可覆盖本行。
        /// </summary>
        public BillSpecOption SelectedBatchSpec
        {
            get { return _selectedBatchSpec; }
            set
            {
                if (!SetProperty(ref _selectedBatchSpec, value)) { return; }
                if (_applyingSpecTemplate) { return; }
                _defaultSpecId = value == null || value.IsAuto ? (int?)null : value.SpecId;
                // 批选即「本批统一口径」：清掉此前的单行手选，避免出现「表头选了 A、部分行还是 B」的错觉
                _objectSpecs.Clear();
                ApplySpecTemplateToRows();
                SchedulePreview();
            }
        }

        /// <summary>本次出账是否可按行手选规格（价目表有启用中的规格时才显示下拉框）。</summary>
        public bool CanPickSpec { get { return _standardSpecOptions.Count > 1; } }

        /// <summary>规格列口径提示。</summary>
        public string SpecPickHintText
        {
            get
            {
                if (SelectedItem == null) { return string.Empty; }
                if (_standardSpecOptions.Count == 0)
                    return "该收费项目未绑定价目表规格：金额按收费项目自身的计价方式计算。";
                if (_standardSpecOptions.Count == 1)
                    return "价目表只有一条启用规格：自动匹配即该规格，无需手选。";
                return "规格默认「自动匹配」（按适用条件命中，未命中走兜底）；如需按行指定，可在下拉框手选具体规格。";
            }
        }

        /// <summary>把价目表规格候选铺到当前可见行，并按「类型:ID」回填已手选的规格。</summary>
        private void ApplySpecTemplateToRows()
        {
            _applyingSpecTemplate = true;
            try
            {
                foreach (BillObjectRow row in BillObjects)
                {
                    row.SpecOptions.Clear();
                    foreach (BillSpecOption option in _standardSpecOptions) { row.SpecOptions.Add(option); }
                    // 显式置位（ObservableCollection 变化不会触发 CanPickSpec 的 PropertyChanged，
                    // 否则绑定只在首次求值 → 下拉框永远显示不出来，现场表现为「规格列还是只读文本」）
                    row.CanPickSpec = row.SpecOptions.Count > 1;
                    if (row.Dto == null) { continue; }
                    int specId;
                    int? wanted = _objectSpecs.TryGetValue(ObjectKey(row.Dto.Id), out specId) ? specId : _defaultSpecId;
                    row.SelectedSpec = wanted.HasValue
                        ? (row.SpecOptions.FirstOrDefault(x => x.SpecId == wanted.Value) ?? row.SpecOptions.FirstOrDefault())
                        : row.SpecOptions.FirstOrDefault();
                }
            }
            finally
            {
                _applyingSpecTemplate = false;
            }
            OnPropertyChanged(nameof(CanPickSpec));
            OnPropertyChanged(nameof(SpecPickHintText));
        }

        /// <summary>行内手选规格变化：写回「类型:ID」并重新试算（不选＝自动匹配，从字典移除）。</summary>
        private void OnSpecSelectionChanged(string key, int? specId)
        {
            if (string.IsNullOrEmpty(key)) { return; }
            if (_applyingSpecTemplate) { return; }   // 铺模板/批选期间不算「单行手选」
            if (specId.HasValue && specId.Value > 0) { _objectSpecs[key] = specId.Value; }
            else { _objectSpecs.Remove(key); }
            SchedulePreview();
        }

        /// <summary>切换收费项目 / 重新打开生成账单弹窗时清空手选规格（不把上个项目的规格带进来）。</summary>
        private void ClearSpecPicks()
        {
            _objectSpecs.Clear();
            _defaultSpecId = null;
            _applyingSpecTemplate = true;
            try
            {
                _selectedBatchSpec = BatchSpecOptions.FirstOrDefault();
                OnPropertyChanged(nameof(SelectedBatchSpec));
                foreach (BillObjectRow row in BillObjects)
                {
                    row.SelectedSpec = row.SpecOptions.FirstOrDefault();
                }
            }
            finally
            {
                _applyingSpecTemplate = false;
            }
        }
    }

    /// <summary>规格候选（CHG-v1.2.0-12）：IsAuto 为 true 时表示「自动匹配」。</summary>
    public class BillSpecOption
    {
        public int SpecId { get; set; }
        public bool IsAuto { get; set; }
        public string SpecName { get; set; }
        public decimal UnitPrice { get; set; }
        public string PriceUnit { get; set; }
        /// <summary>适用条件摘要（如「住宅 / 1号楼」）；兜底规格显示「不限（兜底）」。</summary>
        public string MatchText { get; set; }

        /// <summary>下拉框显示文本：自动匹配 ｜ 规格名（单价/单位，适用条件）。</summary>
        public string DisplayText
        {
            get
            {
                if (IsAuto) { return "自动匹配（按适用条件）"; }
                string price = UnitPrice.ToString("0.##") + (string.IsNullOrWhiteSpace(PriceUnit) ? string.Empty : "/" + PriceUnit);
                return SpecName + "（" + price + (string.IsNullOrWhiteSpace(MatchText) ? string.Empty : "，" + MatchText) + "）";
            }
        }

        public override string ToString() { return DisplayText; }

        /// <summary>表头批选用的紧凑文本（列宽有限：只显示「自动匹配（按适用条件）」或规格名）。</summary>
        public string ShortDisplayText { get { return IsAuto ? "自动匹配（按适用条件）" : SpecName; } }
    }

    /// <summary>档案对象行的规格手选（CHG-v1.2.0-12）。</summary>
    public partial class BillObjectRow
    {
        /// <summary>可选规格（首项为「自动匹配」哨兵；价目表没有启用规格时为空）。</summary>
        public ObservableCollection<BillSpecOption> SpecOptions { get; } = new ObservableCollection<BillSpecOption>();

        private BillSpecOption _selectedSpec;

        /// <summary>当前选中的规格（null / IsAuto = 自动匹配）。</summary>
        public BillSpecOption SelectedSpec
        {
            get { return _selectedSpec; }
            set { SetProperty(ref _selectedSpec, value); }
        }

        private bool _canPickSpec;

        /// <summary>
        /// 该行是否显示规格下拉框（价目表有多条启用规格时）。
        /// 由 VM 在铺候选时显式置位 —— 集合内容变化不会自动通知该属性。
        /// </summary>
        public bool CanPickSpec
        {
            get { return _canPickSpec; }
            set { SetProperty(ref _canPickSpec, value); }
        }

        /// <summary>本次提交的手选规格 ID（未手选 = null，服务端按自动匹配计价）。</summary>
        public int? SelectedSpecId
        {
            get { return _selectedSpec == null || _selectedSpec.IsAuto ? (int?)null : _selectedSpec.SpecId; }
        }
    }
}
