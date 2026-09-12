using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Equipment;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>最近登记行（PG-EQP-02 右侧设备卡：保养 + 年检合并）。</summary>
    public class RegistrationRow
    {
        public string DateText { get; set; }
        public string TypeText { get; set; }
        public string ResultText { get; set; }
        public string VendorText { get; set; }
    }

    /// <summary>设备下拉选项（可编辑下拉：显示「编号 · 名称 · 位置」，TextSearch.TextPath=Display 保证选中后回显一致）。</summary>
    public class DeviceOption
    {
        public DeviceDto Dto { get; set; }
        public int Id { get { return Dto == null ? 0 : Dto.Id; } }
        public string Display
        {
            get
            {
                if (Dto == null) return string.Empty;
                string no = string.IsNullOrEmpty(Dto.DeviceNo) ? "EQP-" + Dto.Id.ToString("0000") : Dto.DeviceNo;
                return no + " · " + (Dto.Name ?? string.Empty) + " · " + (Dto.Location ?? string.Empty);
            }
        }
    }

    /// <summary>保养/年检登记（PG-EQP-02，UC-EQP-002/003，BR-EQP-02/03/06）。</summary>
    public class DeviceMaintainViewModel : BaseInfoPageViewModel
    {
        private DeviceDto _selectedDevice;
        private DeviceOption _selectedDeviceOption;
        private MaintainTypeDto _selectedRecordType;
        private int _vendorId;
        private DateTime _date = DateTime.Today;
        private string _result = "合格";
        private string _content = string.Empty;
        private string _failReason = string.Empty;
        private string _costText = string.Empty;
        private string _lastMaintenanceText = "—";
        private string _inspectionDueText = "—";
        private string _deviceSearch = string.Empty;
        private ListCollectionView _deviceView;
        private readonly List<DeviceOption> _allDevices = new List<DeviceOption>();
        private bool _isVendorAdd;
        private string _newVendorName = string.Empty;
        private bool _isVendorDeleteVisible;
        private string _deleteVendorName = string.Empty;
        private int _deleteVendorId;

        public DeviceMaintainViewModel(IApiClient api) : base(api)
        {
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            SaveUnqualifiedCommand = new AsyncRelayCommand(SaveUnqualifiedAsync);
            OpenNewVendorCommand = new RelayCommand(OpenNewVendor);
            ConfirmNewVendorCommand = new AsyncRelayCommand(ConfirmNewVendorAsync);
            CancelNewVendorCommand = new RelayCommand(() => { IsVendorAdd = false; NewVendorName = string.Empty; ErrorText = string.Empty; });
            RequestDeleteVendorCommand = new RelayCommand(RequestDeleteVendor);
            ConfirmDeleteVendorCommand = new AsyncRelayCommand(ConfirmDeleteVendorAsync);
            CancelDeleteVendorCommand = new RelayCommand(() => IsVendorDeleteVisible = false);
            _ = LoadAsync();
        }

        public ObservableCollection<VendorDto> Vendors { get; } = new ObservableCollection<VendorDto>();
        public ObservableCollection<RegistrationRow> RecentRecords { get; } = new ObservableCollection<RegistrationRow>();
        /// <summary>登记类型选项（R6：固定为系统内置 保养/年检，不支持自定义新增/删除）。</summary>
        public ObservableCollection<MaintainTypeDto> RecordTypeOptions { get; } = new ObservableCollection<MaintainTypeDto>();
        /// <summary>设备下拉过滤视图（输入字符自动检索排列）。</summary>
        public ListCollectionView DeviceView { get { return _deviceView; } }
        /// <summary>设备下拉搜索词：输入即按编号/名称/位置过滤并排列。</summary>
        public string DeviceSearch { get { return _deviceSearch; } set { if (SetProperty(ref _deviceSearch, value)) ApplyDeviceFilter(); } }

        /// <summary>设备下拉选中项（显示「编号 · 名称 · 位置」；输入检索过程中的瞬时 null 忽略）。</summary>
        public DeviceOption SelectedDeviceOption
        {
            get { return _selectedDeviceOption; }
            set
            {
                if (value == null) return;
                if (SetProperty(ref _selectedDeviceOption, value)) SelectedDevice = value.Dto;
            }
        }

        /// <summary>所选设备（DeviceDto 整体绑定，修复 SelectedValue/SelectedValuePath 错配 P0）。</summary>
        public DeviceDto SelectedDevice
        {
            get { return _selectedDevice; }
            set
            {
                if (value == null) return; // 输入检索过程中的瞬时 null 忽略，避免误清空
                if (SetProperty(ref _selectedDevice, value))
                {
                    OnPropertyChanged(nameof(SelectedDeviceText));
                    RebuildRecordTypes(value);
                    _ = LoadDeviceRecordsAsync();
                }
            }
        }
        public string SelectedDeviceText
        {
            get
            {
                if (_selectedDevice == null) return "—";
                string no = string.IsNullOrEmpty(_selectedDevice.DeviceNo) ? "EQP-" + _selectedDevice.Id.ToString("0000") : _selectedDevice.DeviceNo;
                return no + " · " + (_selectedDevice.Location ?? "—");
            }
        }
        /// <summary>所选登记类型（Kind 1 = 年检，保存时路由到年检端点）。</summary>
        public MaintainTypeDto SelectedRecordType
        {
            get { return _selectedRecordType; }
            set
            {
                if (value == null) return; // 输入检索过程中的瞬时 null 忽略
                if (SetProperty(ref _selectedRecordType, value)) OnPropertyChanged(nameof(IsInspection));
            }
        }
        /// <summary>登记类型是否为年检（t_maintain_type.kind = 1），保存时路由到年检端点。</summary>
        public bool IsInspection { get { return _selectedRecordType != null && _selectedRecordType.Kind == 1; } }
        public int VendorId { get { return _vendorId; } set { SetProperty(ref _vendorId, value); } }
        public DateTime Date { get { return _date; } set { SetProperty(ref _date, value); } }
        public string Result { get { return _result; } set { SetProperty(ref _result, value); } }
        public string Content { get { return _content; } set { SetProperty(ref _content, value); } }
        /// <summary>不合格说明（检测结果=不合格时必填，BR-EQP-03）。</summary>
        public string FailReason { get { return _failReason; } set { SetProperty(ref _failReason, value); } }
        /// <summary>费用（元）文本，保存时解析（BR-EQP-06）。</summary>
        public string CostText { get { return _costText; } set { SetProperty(ref _costText, value); } }
        public string LastMaintenanceText { get { return _lastMaintenanceText; } private set { SetProperty(ref _lastMaintenanceText, value); } }
        public string InspectionDueText { get { return _inspectionDueText; } private set { SetProperty(ref _inspectionDueText, value); } }

        /// <summary>执行人/单位"＋新增"内联行。</summary>
        public bool IsVendorAdd { get { return _isVendorAdd; } set { SetProperty(ref _isVendorAdd, value); } }
        public string NewVendorName { get { return _newVendorName; } set { SetProperty(ref _newVendorName, value); } }
        /// <summary>删除执行单位二次确认浮层。</summary>
        public bool IsVendorDeleteVisible { get { return _isVendorDeleteVisible; } set { SetProperty(ref _isVendorDeleteVisible, value); } }
        public string DeleteVendorName { get { return _deleteVendorName; } set { SetProperty(ref _deleteVendorName, value); } }

        public IAsyncRelayCommand SaveCommand { get; }
        /// <summary>原型第二按钮「检测不合格 → 转维修」：强制结果不合格（必填说明）→ 保存 → 后端转维修 + 生成 WX 工单。</summary>
        public IAsyncRelayCommand SaveUnqualifiedCommand { get; }
        public IRelayCommand OpenNewVendorCommand { get; }
        public IAsyncRelayCommand ConfirmNewVendorCommand { get; }
        public IRelayCommand CancelNewVendorCommand { get; }
        public IRelayCommand RequestDeleteVendorCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteVendorCommand { get; }
        public IRelayCommand CancelDeleteVendorCommand { get; }

        /// <summary>设备切换：R6 起登记类型固定为系统两类，默认选中「保养」（kind=0），退回首项（年检）。</summary>
        private void RebuildRecordTypes(DeviceDto dev)
        {
            var preferred = RecordTypeOptions.FirstOrDefault(t => t.Kind == 0)
                            ?? RecordTypeOptions.FirstOrDefault();
            _selectedRecordType = preferred;
            OnPropertyChanged(nameof(SelectedRecordType));
            OnPropertyChanged(nameof(IsInspection));
        }

        /// <summary>设备下拉过滤：关键词命中「编号 · 名称 · 位置」（输入字符自动检索排列）。</summary>
        private void ApplyDeviceFilter()
        {
            if (_deviceView == null) return;
            string kw = _deviceSearch == null ? string.Empty : _deviceSearch.Trim();
            _deviceView.Filter = o =>
            {
                var opt = o as DeviceOption;
                if (opt == null) return false;
                if (kw.Length == 0) return true;
                return (opt.Display ?? string.Empty).IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0;
            };
            _deviceView.Refresh();
        }

        private async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var page = await Api.QueryDevicesAsync(new DeviceQueryRequest { PageIndex = 1, PageSize = 100 });
                int keepId = _selectedDevice == null ? 0 : _selectedDevice.Id;
                _allDevices.Clear();
                foreach (var d in page.Items) _allDevices.Add(new DeviceOption { Dto = d });
                _deviceView = new ListCollectionView(_allDevices);
                ApplyDeviceFilter();
                OnPropertyChanged(nameof(DeviceView));
                if (RecordTypeOptions.Count == 0)
                {
                    var types = await Api.GetMaintainTypesAsync();
                    foreach (var t in types) RecordTypeOptions.Add(t);
                }
                var keep = _allDevices.FirstOrDefault(d => d.Id == keepId) ?? _allDevices.FirstOrDefault();
                if (keep != null) SelectedDeviceOption = keep;
                if (Vendors.Count == 0) { foreach (var v in await Api.GetVendorsAsync()) Vendors.Add(v); }
                if (Vendors.Count > 0 && _vendorId == 0) VendorId = Vendors[0].Id;
            }, "设备已加载");
        }

        /// <summary>右侧设备卡：上次保养 / 年检到期 / 最近登记（GetMaintenanceAsync + GetInspectionAsync，含执行方 VendorName）。</summary>
        private async Task LoadDeviceRecordsAsync()
        {
            var dev = _selectedDevice;
            if (dev == null) return;
            await RunAsync(async () =>
            {
                var maint = await Api.GetMaintenanceAsync(dev.Id);
                var insp = await Api.GetInspectionAsync(dev.Id);
                var lm = maint.OrderByDescending(x => x.MDate).FirstOrDefault();
                LastMaintenanceText = lm == null
                    ? "暂无保养记录"
                    : lm.MDate.ToString("yyyy-MM-dd") + " · " + (string.IsNullOrEmpty(lm.Result) ? "—" : lm.Result);
                var li = insp.OrderByDescending(x => x.IDate).FirstOrDefault();
                if (li == null)
                {
                    // 无年检记录时用后端 BR-EQP-02 口径（最近一次年检 + 1 年，无记录回退投运日期）
                    if (dev.NextInspection.HasValue)
                    {
                        int nd = (dev.NextInspection.Value.Date - DateTime.Today).Days;
                        InspectionDueText = dev.NextInspection.Value.ToString("yyyy-MM-dd")
                            + (nd >= 0 ? "（还有 " + nd + " 天）" : "（逾期 " + (-nd) + " 天）");
                    }
                    else
                    {
                        InspectionDueText = "暂无年检记录";
                    }
                }
                else
                {
                    // BR-EQP-02：年检按年度，下次年检 = 最近一次年检 + 1 年
                    var next = li.IDate.AddYears(1);
                    int days = (next.Date - DateTime.Today).Days;
                    InspectionDueText = next.ToString("yyyy-MM-dd") + (days >= 0 ? "（还有 " + days + " 天）" : "（逾期 " + (-days) + " 天）");
                }
                var rows = maint.Select(m => new RegistrationRow
                {
                    DateText = m.MDate.ToString("yyyy-MM-dd"),
                    TypeText = string.IsNullOrEmpty(m.RecordType) ? "保养" : m.RecordType,
                    ResultText = string.IsNullOrEmpty(m.Result) ? "—" : m.Result,
                    VendorText = string.IsNullOrEmpty(m.VendorName) ? "—" : m.VendorName
                }).Concat(insp.Select(i => new RegistrationRow
                {
                    DateText = i.IDate.ToString("yyyy-MM-dd"),
                    TypeText = string.IsNullOrEmpty(i.RecordType) ? "年检" : i.RecordType,
                    ResultText = string.IsNullOrEmpty(i.Result) ? "—" : i.Result,
                    VendorText = string.IsNullOrEmpty(i.VendorName) ? "—" : i.VendorName
                }))
                .OrderByDescending(r => r.DateText, StringComparer.Ordinal)
                .Take(6)
                .ToList();
                RecentRecords.Clear();
                foreach (var r in rows) RecentRecords.Add(r);
            }, null);
        }

        private async Task SaveAsync()
        {
            if (SelectedDevice == null) { ErrorText = "请选择设备"; return; }
            if (!IsInspection && string.IsNullOrWhiteSpace(Content)) { ErrorText = "请填写结果记录"; return; }
            if (string.Equals(_result, "不合格", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(_failReason))
            {
                ErrorText = "检测结果为不合格时必须填写不合格说明（BR-EQP-03）";
                return;
            }
            decimal? cost = null;
            if (!string.IsNullOrWhiteSpace(_costText))
            {
                decimal v;
                if (!decimal.TryParse(_costText.Trim().TrimStart('¥', '￥'), NumberStyles.Number, CultureInfo.CurrentCulture, out v))
                {
                    ErrorText = "费用格式不正确（如 350 或 350.00）";
                    return;
                }
                cost = v;
            }
            var failReason = string.IsNullOrWhiteSpace(_failReason) ? null : _failReason.Trim();
            // R6：登记类型固定为系统两类，未选中时按业务性质回落到「保养」/「年检」
            string recordType = _selectedRecordType == null
                ? (IsInspection ? "年检" : "保养")
                : _selectedRecordType.Name;
            string msg = null;
            await RunAsync(async () =>
            {
                msg = "登记完成，已更新下次保养日期";
                if (!IsInspection)
                {
                    var dto = await Api.AddMaintenanceAsync(SelectedDevice.Id, new MaintenanceRecordRequest
                    {
                        DeviceId = SelectedDevice.Id,
                        VendorId = _vendorId > 0 ? (int?)_vendorId : null,
                        MDate = Date,
                        RecordType = recordType,
                        Content = Content,
                        Result = Result,
                        Cost = cost,
                        FailReason = failReason
                    });
                    if (dto != null && !string.IsNullOrEmpty(dto.FaultNo)) msg = "登记完成：检测结果不合格，已自动转维修并生成维修工单 " + dto.FaultNo;
                }
                else
                {
                    var dto = await Api.AddInspectionAsync(SelectedDevice.Id, new InspectionRecordRequest
                    {
                        DeviceId = SelectedDevice.Id,
                        VendorId = _vendorId > 0 ? (int?)_vendorId : null,
                        IDate = Date,
                        RecordType = recordType,
                        Result = Result,
                        Cost = cost,
                        FailReason = failReason
                    });
                    if (dto != null && !string.IsNullOrEmpty(dto.FaultNo)) msg = "登记完成：检测结果不合格，已自动转维修并生成维修工单 " + dto.FaultNo;
                }
                ErrorText = string.Empty;
            }, null);
            await LoadDeviceRecordsAsync();
            // RunAsync 在无消息参数时会清空状态行，故登记结果提示在数据刷新后回写
            if (!string.IsNullOrEmpty(msg)) StatusText = DateTime.Now.ToString("HH:mm:ss ") + msg;
        }

        /// <summary>「检测不合格 → 转维修」：结果置不合格并校验说明后走同一保存链路（BR-EQP-03）。</summary>
        private async Task SaveUnqualifiedAsync()
        {
            if (SelectedDevice == null) { ErrorText = "请选择设备"; return; }
            Result = "不合格";
            if (string.IsNullOrWhiteSpace(FailReason))
            {
                ErrorText = "检测不合格必须填写不合格说明（BR-EQP-03）";
                return;
            }
            await SaveAsync();
        }

        // ===================== 登记类型：自定义新增 / 删除（参照纠纷登记类型下拉） =====================

        /// <summary>打开"＋新增"内联行。</summary>
        // ===================== 执行人 / 单位：自定义新增 / 删除（参照纠纷登记类型下拉） =====================

        private void OpenNewVendor()
        {
            NewVendorName = string.Empty;
            ErrorText = string.Empty;
            IsVendorAdd = true;
        }

        private async Task ConfirmNewVendorAsync()
        {
            string name = NewVendorName == null ? string.Empty : NewVendorName.Trim();
            if (name.Length == 0) { ErrorText = "请输入执行单位名称"; return; }
            if (Vendors.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                ErrorText = "执行单位已存在：" + name;
                return;
            }
            await RunAsync(async () =>
            {
                var created = await Api.CreateVendorAsync(new VendorRequest { Name = name });
                Vendors.Add(created);
                IsVendorAdd = false;
                NewVendorName = string.Empty;
                VendorId = created.Id;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "执行单位「" + created.Name + "」已新增";
            }, "执行单位已新增");
        }

        private void RequestDeleteVendor()
        {
            var vendor = Vendors.FirstOrDefault(v => v.Id == _vendorId);
            if (vendor == null) { ErrorText = "请先选择需要删除的执行单位"; return; }
            _deleteVendorId = vendor.Id;
            DeleteVendorName = vendor.Name;
            IsVendorDeleteVisible = true;
        }

        /// <summary>确认删除执行单位（软删）：历史记录保留单位名称，下拉不再展示。</summary>
        private async Task ConfirmDeleteVendorAsync()
        {
            int id = _deleteVendorId;
            string name = DeleteVendorName;
            await RunAsync(async () =>
            {
                await Api.DeleteVendorAsync(id);
                var removed = Vendors.FirstOrDefault(v => v.Id == id);
                if (removed != null) Vendors.Remove(removed);
                IsVendorDeleteVisible = false;
                VendorId = Vendors.Count > 0 ? Vendors[0].Id : 0;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "执行单位「" + name + "」已删除";
            }, "执行单位已删除");
        }
    }
}
