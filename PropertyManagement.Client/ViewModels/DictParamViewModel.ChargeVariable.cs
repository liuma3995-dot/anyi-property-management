using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>计量变量行（系统设置 → 参数 / 字典维护 → 计量变量）。CHG-v1.1.2-26。</summary>
    public class ChargeVariableRow : ObservableObject
    {
        private bool _isSelected;
        public ChargeVariableDto Dto { get; set; }
        public bool IsSelected { get { return _isSelected; } set { SetProperty(ref _isSelected, value); } }
        public int Id { get { return Dto.Id; } }
        public string VarCode { get { return Dto.VarCode ?? string.Empty; } }
        public string VarName { get { return Dto.VarName ?? string.Empty; } }
        public string UnitText { get { return string.IsNullOrWhiteSpace(Dto.Unit) ? "—" : Dto.Unit; } }
        public string TypeText { get { return Dto.ValueType == ChargeVariableValueType.Decimal ? "小数" : "整数"; } }
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
        public string DefaultText { get { return Dto.DefaultValue.HasValue ? Dto.DefaultValue.Value.ToString("0.##") : "—"; } }
        public string ScopeText
        {
            get
            {
                switch (Dto.ObjectScope)
                {
                    case ChargeVariableScope.Property: return "房产";
                    case ChargeVariableScope.Parking: return "车位";
                    case ChargeVariableScope.Owner: return "业主";
                    case ChargeVariableScope.Custom: return "自定义";
                    default: return "不限";
                }
            }
        }
        public string StatusText { get { return Dto.Status == 0 ? "启用" : "停用"; } }
        public string ToggleText { get { return Dto.Status == 0 ? "停用" : "启用"; } }
        public bool IsBuiltin { get { return Dto.IsBuiltin; } }
        public string BuiltinText { get { return Dto.IsBuiltin ? "内置" : "自建"; } }
        public string UsedText { get { return Dto.UsedCount > 0 ? "被 " + Dto.UsedCount + " 个标准引用" : "未被引用"; } }
        public Brush StatusBrush { get { return Dto.Status == 0 ? RowBrushesVar.Ok : RowBrushesVar.Off; } }
        public Brush StatusBg { get { return Dto.Status == 0 ? RowBrushesVar.OkBg : RowBrushesVar.OffBg; } }
    }

    internal static class RowBrushesVar
    {
        public static readonly Brush Ok = Freeze("#12805C");
        public static readonly Brush OkBg = Freeze("#E8F7F1");
        public static readonly Brush Off = Freeze("#667085");
        public static readonly Brush OffBg = Freeze("#F1F3F7");

        private static Brush Freeze(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// 计量变量定义侧（CHG-v1.1.2-26，原型 ⑧）：
    /// 「计量变量」字典分类的列表、新增 / 编辑 / 启停 / 批量删除，以及变量五要素表单。
    /// 口径：内置变量可停用不可删除；用户自建变量只开放「手填 / 固定值」来源。
    /// </summary>
    public partial class DictParamViewModel
    {
        private bool _isVariableFormVisible;
        private int _editingVariableId;
        private string _formVariableName = string.Empty;
        private string _formVariableUnit = string.Empty;
        private int _formVariableTypeIndex;
        private int _formVariableSourceIndex;
        private string _formVariableDefault = string.Empty;
        private int _formVariableScopeIndex = 4;
        private string _formVariableRemark = string.Empty;

        /// <summary>计量变量列表。</summary>
        public ObservableCollection<ChargeVariableRow> VariableItems { get; } = new ObservableCollection<ChargeVariableRow>();

        /// <summary>当前分类是否为「计量变量」（该分类走变量接口，不走走字典项接口）。</summary>
        public bool IsVariableType
        {
            get
            {
                return SelectedType != null &&
                       string.Equals(SelectedType.TypeCode, "charge_variable", StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool IsNotVariableType { get { return !IsVariableType; } }

        // ---------- 变量表单 ----------

        public bool IsVariableFormVisible { get { return _isVariableFormVisible; } private set { SetProperty(ref _isVariableFormVisible, value); } }
        public string VariableFormTitle { get { return _editingVariableId > 0 ? "编辑计量变量" : "新增计量变量"; } }
        public string FormVariableName { get { return _formVariableName; } set { SetProperty(ref _formVariableName, value); } }
        public string FormVariableUnit { get { return _formVariableUnit; } set { SetProperty(ref _formVariableUnit, value); } }
        public int FormVariableTypeIndex { get { return _formVariableTypeIndex; } set { SetProperty(ref _formVariableTypeIndex, value); } }
        public int FormVariableSourceIndex
        {
            get { return _formVariableSourceIndex; }
            set { if (SetProperty(ref _formVariableSourceIndex, value)) { OnPropertyChanged(nameof(FormVariableSourceHint)); } }
        }
        public string FormVariableSourceHint
        {
            get
            {
                return _formVariableSourceIndex == 3
                    ? "固定值：不显示输入项（如「数量 = 1」，等价于按户 / 按次计价）"
                    : "手填：出账时由用户输入（如 桶数 / 台数 / 人次）";
            }
        }
        public string FormVariableDefault { get { return _formVariableDefault; } set { SetProperty(ref _formVariableDefault, value); } }
        public int FormVariableScopeIndex { get { return _formVariableScopeIndex; } set { SetProperty(ref _formVariableScopeIndex, value); } }
        public string FormVariableRemark { get { return _formVariableRemark; } set { SetProperty(ref _formVariableRemark, value); } }

        public IAsyncRelayCommand NewVariableCommand { get; private set; }
        public IRelayCommand<ChargeVariableRow> EditVariableCommand { get; private set; }
        public IAsyncRelayCommand SaveVariableCommand { get; private set; }
        public IRelayCommand CancelVariableCommand { get; private set; }
        public IAsyncRelayCommand<ChargeVariableRow> ToggleVariableCommand { get; private set; }
        public IAsyncRelayCommand<ChargeVariableRow> DeleteVariableCommand { get; private set; }
        public IAsyncRelayCommand VariableBatchDeleteCommand { get; private set; }
        public string VariableBatchDeleteText { get { return IsSelectionMode ? "删除所选变量" : "批量删除"; } }

        private void InitChargeVariable()
        {
            NewVariableCommand = new AsyncRelayCommand(OpenNewVariableAsync);
            EditVariableCommand = new RelayCommand<ChargeVariableRow>(OpenEditVariable);
            SaveVariableCommand = new AsyncRelayCommand(SaveVariableAsync);
            CancelVariableCommand = new RelayCommand(() => IsVariableFormVisible = false);
            ToggleVariableCommand = new AsyncRelayCommand<ChargeVariableRow>(ToggleVariableAsync);
            DeleteVariableCommand = new AsyncRelayCommand<ChargeVariableRow>(DeleteVariableAsync);
            VariableBatchDeleteCommand = new AsyncRelayCommand(VariableBatchDeleteAsync);
        }

        /// <summary>
        /// 计量变量批量删除：首次点击进入选择模式（勾选框列才显示），再次点击删除所选。
        /// 口径：内置变量不可删除；仍被收费标准引用的变量服务端会拦截并给出可读提示。
        /// </summary>
        private async Task VariableBatchDeleteAsync()
        {
            if (!IsSelectionMode)
            {
                IsSelectionMode = true;
                OnPropertyChanged(nameof(VariableBatchDeleteText));
                return;
            }
            List<ChargeVariableRow> picked = VariableItems.Where(x => x.IsSelected).ToList();
            if (picked.Count == 0) { ErrorText = "请先勾选要删除的计量变量"; return; }
            ChargeVariableRow builtin = picked.FirstOrDefault(x => x.IsBuiltin);
            if (builtin != null)
            {
                ErrorText = "内置变量不可删除（如「" + builtin.VarName + "」），请改用停用";
                return;
            }
            await RunAsync(async () =>
            {
                foreach (ChargeVariableRow row in picked) { await Api.DeleteChargeVariableAsync(row.Id); }
                IsSelectionMode = false;
                await LoadVariablesAsync();
            }, "已删除 " + picked.Count + " 个计量变量（软删留痕）");
        }

        private async Task LoadVariablesAsync()
        {
            List<ChargeVariableDto> list = await Api.GetChargeVariablesAsync(null, true);
            // 先取数再替换，避免切换分类时表格塌陷为「仅表头」（与字典项同口径）
            var rows = list.OrderBy(x => x.Sort).ThenBy(x => x.Id)
                .Select(x => new ChargeVariableRow { Dto = x })
                .ToList();
            VariableItems.Clear();
            foreach (ChargeVariableRow row in rows) { VariableItems.Add(row); }
            IsSelectionMode = false;
            OnPropertyChanged(nameof(VariableBatchDeleteText));
            OnPropertyChanged(nameof(IsVariableType));
            OnPropertyChanged(nameof(IsNotVariableType));
        }

        private async Task OpenNewVariableAsync()
        {
            _editingVariableId = 0;
            FormVariableName = string.Empty;
            FormVariableUnit = string.Empty;
            FormVariableTypeIndex = 0;
            FormVariableSourceIndex = 0;   // 0 = 手填
            FormVariableDefault = string.Empty;
            FormVariableScopeIndex = 4;
            FormVariableRemark = string.Empty;
            OnPropertyChanged(nameof(VariableFormTitle));
            IsVariableFormVisible = true;
        }

        private void OpenEditVariable(ChargeVariableRow row)
        {
            if (row == null) { return; }
            _editingVariableId = row.Id;
            FormVariableName = row.Dto.VarName;
            FormVariableUnit = row.Dto.Unit;
            FormVariableTypeIndex = row.Dto.ValueType == ChargeVariableValueType.Decimal ? 1 : 0;
            // 表单只开放「手填(0) / 固定值(1)」两项；内置变量保留原来源用于只读展示
            FormVariableSourceIndex = row.Dto.Source == ChargeVariableSource.Fixed ? 1 : 0;
            FormVariableDefault = row.Dto.DefaultValue.HasValue ? row.Dto.DefaultValue.Value.ToString("0.##") : string.Empty;
            FormVariableScopeIndex = (int)row.Dto.ObjectScope;
            FormVariableRemark = row.Dto.Remark;
            OnPropertyChanged(nameof(VariableFormTitle));
            IsVariableFormVisible = true;
        }

        private async Task SaveVariableAsync()
        {
            await RunAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(FormVariableName)) { throw new InvalidOperationException("变量名称不能为空"); }
                decimal defaultValue;
                decimal? parsed = decimal.TryParse((FormVariableDefault ?? string.Empty).Trim(), out defaultValue)
                    ? (decimal?)defaultValue
                    : null;
                var request = new ChargeVariableRequest
                {
                    VarName = FormVariableName.Trim(),
                    Unit = (FormVariableUnit ?? string.Empty).Trim(),
                    ValueType = FormVariableTypeIndex == 1 ? ChargeVariableValueType.Decimal : ChargeVariableValueType.Integer,
                    Source = FormVariableSourceIndex == 1 ? ChargeVariableSource.Fixed : ChargeVariableSource.Manual,
                    DefaultValue = parsed,
                    ObjectScope = (ChargeVariableScope)FormVariableScopeIndex,
                    Remark = FormVariableRemark,
                    Status = 0
                };
                if (_editingVariableId > 0)
                {
                    await Api.UpdateChargeVariableAsync(_editingVariableId, request);
                }
                else
                {
                    await Api.CreateChargeVariableAsync(request);
                }
                IsVariableFormVisible = false;
                await LoadVariablesAsync();
            }, _editingVariableId > 0 ? "计量变量已更新" : "计量变量已新增");
        }

        private async Task ToggleVariableAsync(ChargeVariableRow row)
        {
            if (row == null) { return; }
            await RunAsync(async () =>
            {
                await Api.ToggleChargeVariableAsync(row.Id, row.Dto.Status == 0 ? 1 : 0);
                await LoadVariablesAsync();
            }, row.Dto.Status == 0 ? "变量已停用（已引用该变量的收费标准将无法生成新账单）" : "变量已启用");
        }

        private async Task DeleteVariableAsync(ChargeVariableRow row)
        {
            if (row == null) { return; }
            if (row.IsBuiltin)
            {
                ErrorText = "内置变量不可删除（避免历史账单公式无法解析），如需停用请点「停用」";
                return;
            }
            await RunAsync(async () =>
            {
                await Api.DeleteChargeVariableAsync(row.Id);
                await LoadVariablesAsync();
            }, "计量变量已删除（软删留痕）");
        }
    }
}
