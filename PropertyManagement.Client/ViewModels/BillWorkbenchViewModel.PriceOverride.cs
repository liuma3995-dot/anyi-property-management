using System;
using System.Collections.Generic;
using System.Globalization;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>
    /// 账单工作台 —— 出账可改价（CHG-v1.1.2-50）。
    /// 口径：在「收费项目维护」勾选「出账时可改价」（仅一次性收费标准 / 自定义缴费对象可勾）后，
    /// 生成账单表单可按缴费对象逐行改单价；试算与生成同口径，账单快照记录改后单价，
    /// 价目表单价不变（改价只作用于本次生成的账单）。
    /// </summary>
    public partial class BillWorkbenchViewModel
    {
        /// <summary>档案对象的改价（键 = 缴费对象类型:ID，跨搜索保持；与手填计量参数同口径）。</summary>
        private readonly Dictionary<string, decimal> _objectPrices =
            new Dictionary<string, decimal>(StringComparer.Ordinal);
        /// <summary>模板回填期间抑制改价输入触发的试算（避免打开弹窗就抖一次试算）。</summary>
        private bool _applyingPriceTemplate;

        /// <summary>当前收费项目是否开启「出账时可改价」（收费项目维护里的同一开关）。</summary>
        public bool IsPriceOverrideEnabled
        {
            get { return SelectedItem != null && SelectedItem.AllowPriceOverride; }
        }

        /// <summary>改价口径提示（一行，说明「改了会怎样、留空是什么」）。</summary>
        public string PriceOverrideHintText
        {
            get
            {
                if (SelectedItem == null) { return string.Empty; }
                return IsPriceOverrideEnabled
                    ? "本收费项目已开启「出账时可改价」：可逐行填写本次单价（留空＝按价目表单价）；改后单价只作用于本次生成的账单，价目表单价不变。"
                    : "单价按价目表取值。如需现场议价，请在「收费项目维护」开启「出账时可改价」（仅一次性收费标准 / 自定义缴费对象可开）。";
            }
        }

        /// <summary>把已保存的改价回填到当前可见行（行集合会随搜索重建，故按「类型:ID」回填）。</summary>
        private void ApplyPriceOverrideTemplateToRows()
        {
            _applyingPriceTemplate = true;
            try
            {
                foreach (BillObjectRow row in BillObjects)
                {
                    row.IsPriceEditable = IsPriceOverrideEnabled;
                    if (row.Dto == null) { continue; }
                    decimal price;
                    row.UnitPriceOverrideText = _objectPrices.TryGetValue(ObjectKey(row.Dto.Id), out price)
                        ? price.ToString("0.##")
                        : string.Empty;
                }
                foreach (BillCustomPayerRow row in CustomPayers)
                {
                    row.IsPriceEditable = IsPriceOverrideEnabled;
                }
            }
            finally
            {
                _applyingPriceTemplate = false;
            }
        }

        /// <summary>
        /// 改价输入变化：写回「类型:ID」并触发试算。
        /// 空值 / 非法值 = 不改价（回落价目表单价），不做输入打断。
        /// </summary>
        private void OnPriceOverrideTextChanged(string key, string text)
        {
            if (_applyingPriceTemplate || string.IsNullOrEmpty(key)) { return; }
            decimal value;
            if (TryParsePrice(text, out value))
            {
                _objectPrices[key] = value;
            }
            else
            {
                _objectPrices.Remove(key);
            }
            SchedulePreview();
        }

        /// <summary>切换收费项目 / 重新打开生成账单弹窗时清空改价（不把上个项目的改后价带进来）。</summary>
        private void ClearPriceOverrides()
        {
            _objectPrices.Clear();
            _applyingPriceTemplate = true;
            try
            {
                foreach (BillObjectRow row in BillObjects)
                {
                    row.UnitPriceOverrideText = string.Empty;
                    row.IsPriceEditable = IsPriceOverrideEnabled;
                }
                foreach (BillCustomPayerRow row in CustomPayers)
                {
                    row.UnitPriceOverrideText = string.Empty;
                    row.IsPriceEditable = IsPriceOverrideEnabled;
                }
            }
            finally
            {
                _applyingPriceTemplate = false;
            }
        }

        /// <summary>改价输入解析（留空 / ≤0 / 非法一律视为「不改价」）。</summary>
        internal static bool TryParsePrice(string text, out decimal value)
        {
            value = 0m;
            return decimal.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float,
                       CultureInfo.InvariantCulture, out value) && value > 0m;
        }

        /// <summary>收费项目切换后刷新改价开关与提示（Enable/Disable 由 AllowPriceOverride 决定）。</summary>
        private void NotifyPriceOverrideStateChanged()
        {
            OnPropertyChanged(nameof(IsPriceOverrideEnabled));
            OnPropertyChanged(nameof(PriceOverrideHintText));
        }
    }

    /// <summary>档案对象行的出账改价输入（CHG-v1.1.2-50）。</summary>
    public partial class BillObjectRow
    {
        private bool _isPriceEditable;
        private string _unitPriceOverrideText = string.Empty;
        private string _priceUnitSuffix = string.Empty;
        private bool _isPriceOverridden;

        /// <summary>是否显示改价输入框（收费项目开启「出账时可改价」时为 true）。</summary>
        public bool IsPriceEditable
        {
            get { return _isPriceEditable; }
            set { SetProperty(ref _isPriceEditable, value); }
        }

        /// <summary>本次出账单价（用户填写；留空＝按价目表单价）。</summary>
        public string UnitPriceOverrideText
        {
            get { return _unitPriceOverrideText; }
            set { SetProperty(ref _unitPriceOverrideText, value); }
        }

        /// <summary>单价单位后缀（如「/㎡·月」），供「改价输入 + 单位」并排展示。</summary>
        public string PriceUnitSuffix
        {
            get { return _priceUnitSuffix; }
            private set { SetProperty(ref _priceUnitSuffix, value); }
        }

        /// <summary>服务端确认本行用了改后单价（界面标注「改价」，避免改价被当成原价）。</summary>
        public bool IsPriceOverridden
        {
            get { return _isPriceOverridden; }
            private set { SetProperty(ref _isPriceOverridden, value); }
        }

        /// <summary>本次提交的改价（留空 / 非法＝不提交，服务端按价目表单价计价）。</summary>
        public decimal? UnitPriceOverride
        {
            get
            {
                decimal value;
                return BillWorkbenchViewModel.TryParsePrice(_unitPriceOverrideText, out value)
                    ? (decimal?)value
                    : null;
            }
        }
    }

    /// <summary>自定义缴费对象行的出账改价输入（CHG-v1.1.2-50）。</summary>
    public partial class BillCustomPayerRow
    {
        private bool _isPriceEditable;
        private string _unitPriceOverrideText = string.Empty;

        /// <summary>是否显示改价输入框（收费项目开启「出账时可改价」时为 true）。</summary>
        public bool IsPriceEditable
        {
            get { return _isPriceEditable; }
            set { SetProperty(ref _isPriceEditable, value); }
        }

        /// <summary>本次出账单价（用户填写；留空＝按所选规格的价目表单价）。</summary>
        public string UnitPriceOverrideText
        {
            get { return _unitPriceOverrideText; }
            set { SetProperty(ref _unitPriceOverrideText, value); }
        }

        /// <summary>本次提交的改价（留空 / 非法＝不提交）。</summary>
        public decimal? UnitPriceOverride
        {
            get
            {
                decimal value;
                return BillWorkbenchViewModel.TryParsePrice(_unitPriceOverrideText, out value)
                    ? (decimal?)value
                    : null;
            }
        }
    }
}
