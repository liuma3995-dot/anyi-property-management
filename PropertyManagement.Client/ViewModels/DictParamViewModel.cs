using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>字典项行（PG-COM-01：编码/名称/显示值/备注/修改人/修改时间 + 停用灰显）。</summary>
    public class DictItemRow : ObservableObject
    {
        private bool _isSelected;

        public DictItemDto Dto { get; set; }

        /// <summary>R13：勾选框选中态（批量删除依据；单选/全选均通过该属性驱动）。</summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        public string ItemCode { get { return Dto.ItemCode ?? string.Empty; } }
        public string ItemName { get { return Dto.ItemName ?? string.Empty; } }
        public string DisplayValue { get { return string.IsNullOrEmpty(Dto.DisplayValue) ? "—" : Dto.DisplayValue; } }
        public string Remark { get { return string.IsNullOrEmpty(Dto.Remark) ? "—" : Dto.Remark; } }
        public string ModifiedBy { get { return string.IsNullOrEmpty(Dto.ModifiedBy) ? "—" : Dto.ModifiedBy; } }
        public string ModifiedAtText
        {
            get { return Dto.ModifiedAt.HasValue ? Dto.ModifiedAt.Value.ToString("yyyy-MM-dd") : "—"; }
        }
        public bool IsDisabled { get { return Dto.Status != DictItemStatus.Enabled; } }
        public bool IsEnabledVisible { get { return !IsDisabled; } }
        public bool IsDisabledVisible { get { return IsDisabled; } }
        public int Sort { get { return Dto.Sort; } }
    }

    /// <summary>字典分类行（PG-COM-01 左栏卡片：图标 + 分类名 + 右箭头）。
    /// 图标按分类编码映射，未知分类回落通用图标。</summary>
    public class DictTypeRow : ObservableObject
    {
        public DictTypeDto Dto { get; set; }
        public string TypeCode { get { return Dto.TypeCode ?? string.Empty; } }
        public string TypeName { get { return Dto.TypeName ?? string.Empty; } }
        public string IconKey { get { return ResolveIconKey(Dto.TypeCode); } }

        private static string ResolveIconKey(string typeCode)
        {
            switch (typeCode)
            {
                case "charge_method":
                case "charge_mode":
                case "pay_mode": return "Icon.WalletCards";
                case "id_card_type": return "Icon.BadgeCheck";
                case "charge_category": return "Icon.CircleDollarSign";
                case "charge_cycle": return "Icon.CalendarDays";
                case "parking_type": return "Icon.Car";
                case "shift_define": return "Icon.Clock";
                case "dispute_type": return "Icon.MessagesSquare";
                case "emergency_scene": return "Icon.Siren";
                case "equipment_type": return "Icon.HardHat";
                case "expense_category": return "Icon.ReceiptText";
                case "phone_category": return "Icon.PhoneCall";
                default: return "Icon.ReceiptText";
            }
        }
    }

    /// <summary>参数/字典维护（PG-COM-01，UC-COM-004，BR-COM-05）。
    /// 分类切换经 SelectedType setter 联动加载（含停用项，管理端可恢复）；
    /// 编辑走确认弹窗「修改将影响相关业务展示，确认保存？」；系统级字典停用二次确认。</summary>
    public class DictParamViewModel : BaseInfoPageViewModel
    {
        private DictTypeRow _selectedType;
        private bool _isFormVisible;
        private bool _isConfirmVisible;
        private int? _editId;
        private string _formTitle = "新增字典项";
        private string _formItemCode = string.Empty;
        private string _formName = string.Empty;
        private string _formDisplayValue = string.Empty;
        private string _formRemark = string.Empty;
        private string _formSort = string.Empty;
        private string _confirmTitle = "确认操作";
        private string _confirmMessage = string.Empty;
        private DictItemRow _pendingRow;      // 待停用确认行
        private bool _pendingEditConfirm;     // 待编辑保存确认
        private bool _pendingBatchDelete;     // 待批量删除确认
        private bool _isSelectionMode;        // R14：选择模式（点击「批量删除」后才显示勾选框列）
        private bool _isNoticeVisible;        // 提示弹窗（批量删除被拒绝等）
        private string _noticeTitle = "提示";
        private string _noticeMessage = string.Empty;

        public DictParamViewModel(IApiClient api) : base(api)
        {
            SelectTypeCommand = new AsyncRelayCommand<DictTypeRow>(SelectTypeAsync);
            NewCommand = new RelayCommand(OpenNew);
            EditCommand = new RelayCommand<DictItemRow>(OpenEdit);
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new RelayCommand(() => IsFormVisible = false);
            DisableCommand = new RelayCommand<DictItemRow>(ConfirmDisable);
            RestoreCommand = new AsyncRelayCommand<DictItemRow>(r => SetStatusAsync(r.Dto.Id, DictItemStatus.Enabled, "字典项已恢复"));
            ConfirmCommand = new AsyncRelayCommand(ExecuteConfirmAsync);
            CancelConfirmCommand = new RelayCommand(CancelConfirm);
            BatchDeleteCommand = new RelayCommand(BatchDelete);
            CancelSelectionCommand = new RelayCommand(ExitSelectionMode);
            CloseNoticeCommand = new RelayCommand(() => IsNoticeVisible = false);
            _ = LoadAsync();
        }

        public ObservableCollection<DictTypeRow> Types { get; } = new ObservableCollection<DictTypeRow>();
        public ObservableCollection<DictItemRow> Items { get; } = new ObservableCollection<DictItemRow>();

        /// <summary>选中分类：setter 内触发该分类字典项加载（P0 修复：切换失效）。</summary>
        public DictTypeRow SelectedType
        {
            get { return _selectedType; }
            set
            {
                if (ReferenceEquals(_selectedType, value)) { return; }
                _selectedType = value;
                OnPropertyChanged(nameof(SelectedType));
                if (value != null)
                {
                    var type = value;
                    _ = SelectTypeAsync(type);
                }
            }
        }

        public bool IsFormVisible
        {
            get { return _isFormVisible; }
            set { SetProperty(ref _isFormVisible, value); }
        }

        public bool IsConfirmVisible
        {
            get { return _isConfirmVisible; }
            set { SetProperty(ref _isConfirmVisible, value); }
        }

        public string FormTitle
        {
            get { return _formTitle; }
            private set { SetProperty(ref _formTitle, value); }
        }

        /// <summary>编码：编辑只读回显；新增由服务端自动生成（{类型前缀}-{序号}，如 JFFS-01）。</summary>
        public string FormItemCode
        {
            get { return _formItemCode; }
            private set { SetProperty(ref _formItemCode, value); }
        }

        public string FormName
        {
            get { return _formName; }
            set { SetProperty(ref _formName, value); }
        }

        public string FormDisplayValue
        {
            get { return _formDisplayValue; }
            set { SetProperty(ref _formDisplayValue, value); }
        }

        public string FormRemark
        {
            get { return _formRemark; }
            set { SetProperty(ref _formRemark, value); }
        }

        public string FormSort
        {
            get { return _formSort; }
            set { SetProperty(ref _formSort, value); }
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

        /// <summary>提示弹窗（单按钮）：批量删除被拒绝（存在未停用字典项）时使用。</summary>
        public bool IsNoticeVisible
        {
            get { return _isNoticeVisible; }
            set { SetProperty(ref _isNoticeVisible, value); }
        }

        public string NoticeTitle
        {
            get { return _noticeTitle; }
            private set { SetProperty(ref _noticeTitle, value); }
        }

        public string NoticeMessage
        {
            get { return _noticeMessage; }
            private set { SetProperty(ref _noticeMessage, value); }
        }

        /// <summary>R13：全选/取消全选（表头勾选框双向绑定）。</summary>
        public bool IsAllItemsSelected
        {
            get { return Items.Count > 0 && Items.All(i => i.IsSelected); }
            set
            {
                foreach (DictItemRow item in Items) { item.IsSelected = value; }
                OnPropertyChanged(nameof(IsAllItemsSelected));
            }
        }

        /// <summary>刷新全选态（勾选/取消单行后由视图调用）。</summary>
        public void RefreshSelectAllState()
        {
            OnPropertyChanged(nameof(IsAllItemsSelected));
        }

        public IAsyncRelayCommand<DictTypeRow> SelectTypeCommand { get; }
        public IRelayCommand NewCommand { get; }
        public IRelayCommand<DictItemRow> EditCommand { get; }
        public IAsyncRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand<DictItemRow> DisableCommand { get; }
        public IAsyncRelayCommand<DictItemRow> RestoreCommand { get; }
        public IAsyncRelayCommand ConfirmCommand { get; }
        public IRelayCommand CancelConfirmCommand { get; }
        public IRelayCommand BatchDeleteCommand { get; }
        public IRelayCommand CancelSelectionCommand { get; }
        public IRelayCommand CloseNoticeCommand { get; }

        /// <summary>
        /// R14：选择模式。默认 false（字典项列表不显示勾选框列）；点击工具栏「批量删除」后进入选择模式，
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

        private async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                // 先取数再一次性替换：避免清空集合触发中间帧塌陷（列表/表格闪动）
                List<DictTypeDto> types = await Api.GetSystemDictTypesAsync();
                Types.Clear();
                foreach (var t in types) Types.Add(new DictTypeRow { Dto = t });
                if (Types.Count > 0 && _selectedType == null) await SelectTypeAsync(Types[0]);
            }, "字典分类已加载");
        }

        private async Task SelectTypeAsync(DictTypeRow type)
        {
            if (type == null) return;
            if (!ReferenceEquals(_selectedType, type))
            {
                _selectedType = type;
                OnPropertyChanged(nameof(SelectedType));
            }
            await RunAsync(async () =>
            {
                // R10 修复：原实现先 Items.Clear() 再 await 取数，表格在等待期间塌陷为「仅表头」，
                // 下方「交互标注」被顶到表格位置渲染一帧后再落回 → 切换分类时闪现。
                // 现改为先取数、再一次性替换，页面不出现空表中间态。
                List<DictItemDto> items = await Api.GetSystemDictItemsAsync(type.Dto.TypeCode, true);
                Items.Clear();
                // 管理端 includeDisabled=true：停用项灰显并可恢复（业务下拉仍走仅启用端点）
                foreach (var i in items)
                {
                    Items.Add(new DictItemRow { Dto = i });
                }
            }, "字典项已加载");
        }

        private void OpenNew()
        {
            if (SelectedType == null) { ErrorText = "请先选择字典分类"; return; }
            _editId = null;
            FormTitle = "新增字典项";
            FormItemCode = string.Empty;
            FormName = string.Empty;
            FormDisplayValue = string.Empty;
            FormRemark = string.Empty;
            FormSort = string.Empty;
            IsFormVisible = true;
        }

        private void OpenEdit(DictItemRow row)
        {
            if (row == null) return;
            _editId = row.Dto.Id;
            FormTitle = "编辑字典项";
            FormItemCode = row.ItemCode;
            FormName = row.Dto.ItemName;
            FormDisplayValue = row.Dto.DisplayValue ?? string.Empty;
            FormRemark = row.Dto.Remark ?? string.Empty;
            FormSort = row.Dto.Sort > 0 ? row.Dto.Sort.ToString() : string.Empty;
            IsFormVisible = true;
        }

        private async Task SaveAsync()
        {
            if (SelectedType == null) { ErrorText = "请先选择字典分类"; return; }
            if (string.IsNullOrWhiteSpace(FormName)) { ErrorText = "字典项名称不能为空"; return; }
            if (!string.IsNullOrWhiteSpace(FormSort) && !int.TryParse(FormSort.Trim(), out _))
            {
                ErrorText = "排序必须为数字";
                return;
            }

            if (_editId.HasValue)
            {
                // 编辑 → 确认弹窗「修改将影响相关业务展示，确认保存？」（原型 PG-COM-01 交互标注）
                _pendingEditConfirm = true;
                _pendingRow = null;
                ConfirmTitle = "确认修改";
                ConfirmMessage = "修改将影响相关业务展示，确认保存？";
                IsConfirmVisible = true;
                return;
            }

            // 新增：system 端点（/system/dict-types/{code}/items）携带 名称/显示值/备注/排序；编码由服务端生成
            int newSort;
            int.TryParse((FormSort ?? string.Empty).Trim(), out newSort);
            string name = FormName.Trim();
            string remark = string.IsNullOrWhiteSpace(FormRemark) ? null : FormRemark.Trim();
            string displayValue = string.IsNullOrWhiteSpace(FormDisplayValue) ? null : FormDisplayValue.Trim();
            await RunAsync(async () =>
            {
                await Api.CreateDictItemAsync(SelectedType.Dto.TypeCode, new DictItemRequest
                {
                    TypeCode = SelectedType.Dto.TypeCode,
                    ItemName = name,
                    DisplayValue = displayValue,
                    Remark = remark,
                    Sort = newSort
                });
                IsFormVisible = false;
                await SelectTypeAsync(SelectedType);
            }, "字典项已新增");
        }

        private void ConfirmDisable(DictItemRow row)
        {
            if (row == null) return;
            _pendingRow = row;
            _pendingEditConfirm = false;
            ConfirmTitle = "停用字典项";
            ConfirmMessage = "「" + row.ItemName + "」为系统级字典项，停用后相关业务下拉将不再展示该项（已引用数据不受影响）。确认停用？";
            IsConfirmVisible = true;
        }

        private async Task ExecuteConfirmAsync()
        {
            if (_pendingEditConfirm && _editId.HasValue)
            {
                int id = _editId.Value;
                _pendingEditConfirm = false;
                IsConfirmVisible = false;
                await UpdateAsync(id);
                return;
            }
            if (_pendingBatchDelete)
            {
                _pendingBatchDelete = false;
                IsConfirmVisible = false;
                await ExecuteBatchDeleteAsync();
                return;
            }
            if (_pendingRow != null)
            {
                DictItemRow row = _pendingRow;
                _pendingRow = null;
                IsConfirmVisible = false;
                await SetStatusAsync(row.Dto.Id, DictItemStatus.Disabled, "字典项已停用");
            }
        }

        private void CancelConfirm()
        {
            _pendingRow = null;
            _pendingEditConfirm = false;
            _pendingBatchDelete = false;
            IsConfirmVisible = false;
        }

        /// <summary>
        /// 批量删除字典项（PG-COM-01 / R12）：仅「已停用」字典项可删除；
        /// 命中未停用项时弹窗提示无法删除（整批不删除），全部已停用时二次确认后软删。
        /// </summary>
        private void BatchDelete()
        {
            if (!IsSelectionMode)
            {
                EnterSelectionMode();
                return;
            }

            List<DictItemRow> selected = Items.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0)
            {
                ShowNotice("批量删除", "请先勾选要删除的字典项（可勾选单条，也可勾选表头全选），或点击【取消】退出批量删除。");
                return;
            }

            List<DictItemRow> enabled = selected.Where(r => r.Dto.Status != DictItemStatus.Disabled).ToList();
            if (enabled.Count > 0)
            {
                string names = string.Join("、", enabled.Select(r => r.ItemName));
                ShowNotice("无法删除",
                    "以下字典项未停用，无法删除：\n" + names + "\n\n请先【停用】后再删除。");
                return;
            }

            _pendingRow = null;
            _pendingEditConfirm = false;
            _pendingBatchDelete = true;
            ConfirmTitle = "批量删除字典项";
            ConfirmMessage = "将删除所选 " + selected.Count + " 个已停用字典项，删除后不再出现在字典项列表与业务下拉中。确认删除？";
            IsConfirmVisible = true;
        }

        private async Task ExecuteBatchDeleteAsync()
        {
            var ids = Items.Where(i => i.IsSelected).Select(r => r.Dto.Id).Distinct().ToList();
            if (ids.Count == 0) { return; }
            string message = null;
            await RunAsync(async () =>
            {
                DictItemBatchDeleteResultDto result = await Api.BatchDeleteDictItemsAsync(
                    new DictItemBatchDeleteRequest { Ids = ids });
                if (result != null && result.Blocked != null && result.Blocked.Count > 0)
                {
                    // 服务端兜底：存在未停用项 → 整批拒绝
                    string names = string.Join("、", result.Blocked.Select(b => b.ItemName));
                    ShowNotice("无法删除", "以下字典项未停用，无法删除：\n" + names + "\n\n请先【停用】后再删除。");
                    return;
                }
                int deleted = result == null ? 0 : result.Deleted;
                message = "已删除 " + deleted + " 个字典项";
                if (SelectedType != null) { await SelectTypeAsync(SelectedType); }
                ExitSelectionMode();
            }, null);
            StatusText = string.IsNullOrEmpty(message) ? StatusText : message;
        }

        /// <summary>进入选择模式：显示勾选框列并清空历史勾选。</summary>
        private void EnterSelectionMode()
        {
            foreach (DictItemRow item in Items) { item.IsSelected = false; }
            IsSelectionMode = true;
            OnPropertyChanged(nameof(IsAllItemsSelected));
        }

        /// <summary>退出选择模式：隐藏勾选框列并清空勾选。</summary>
        private void ExitSelectionMode()
        {
            foreach (DictItemRow item in Items) { item.IsSelected = false; }
            IsSelectionMode = false;
            OnPropertyChanged(nameof(IsAllItemsSelected));
        }

        private void ShowNotice(string title, string message)
        {
            NoticeTitle = title;
            NoticeMessage = message;
            IsNoticeVisible = true;
        }

        private async Task UpdateAsync(int id)
        {
            int sort;
            int.TryParse(FormSort.Trim(), out sort);
            string name = FormName.Trim();
            await RunAsync(async () =>
            {
                // 编辑保留原状态（Status 不随编辑变更，启停仅经停用/恢复操作）
                await Api.UpdateDictItemAsync(id, new DictItemRequest
                {
                    TypeCode = SelectedType.Dto.TypeCode,
                    ItemCode = FormItemCode,
                    ItemName = name,
                    DisplayValue = string.IsNullOrWhiteSpace(FormDisplayValue) ? null : FormDisplayValue.Trim(),
                    Remark = FormRemark == null ? null : FormRemark,
                    Sort = sort
                });
                IsFormVisible = false;
                await SelectTypeAsync(SelectedType);
            }, "字典项已保存");
        }

        private async Task SetStatusAsync(int id, DictItemStatus status, string message)
        {
            await RunAsync(async () =>
            {
                await Api.SetDictItemStatusAsync(id, status);
                if (SelectedType != null) await SelectTypeAsync(SelectedType);
            }, message);
        }
    }
}
