using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Equipment;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>最近故障行（PG-EQP-03 右列表：故障单/设备/级别/状态/报修时间/操作）。</summary>
    public class FaultRow
    {
        public FaultRecordDto Dto { get; set; }
        public string FaultNoText { get { return string.IsNullOrEmpty(Dto.FaultNo) ? "—" : Dto.FaultNo; } }
        public string DeviceText { get { return string.IsNullOrEmpty(Dto.DeviceName) ? ("EQP-" + Dto.DeviceId.ToString("0000")) : Dto.DeviceName; } }
        public string LevelText { get { return string.IsNullOrEmpty(Dto.LevelText) ? (Dto.Level == 1 ? "重大" : "一般") : Dto.LevelText; } }
        public string StatusText
        {
            get
            {
                if (!string.IsNullOrEmpty(Dto.StatusText)) return Dto.StatusText;
                switch (Dto.Status)
                {
                    case 1: return "维修中";
                    case 2: return "已修复";
                    default: return "待接单";
                }
            }
        }
        public string FTimeText { get { return Dto.FTime.ToString("MM-dd HH:mm"); } }
        /// <summary>待接单行：操作列「生成工单」（Excel 派工单，签发即转维修中）。</summary>
        public bool ShowDispatch { get { return Dto.Status == 0; } }
        /// <summary>维修中行：操作列「维修完成」（线下维修后回填原因/处理结果 → 已修复 + 设备回在用）。</summary>
        public bool ShowRepairDone { get { return Dto.Status == 1; } }
        /// <summary>已修复行：操作列「—」。</summary>
        public bool ShowNoAction { get { return Dto.Status >= 2; } }
        /// <summary>行悬浮说明（面板较窄时不占用列宽：设备 + 明细 + 处理结果；R7 起不再展示应急关联）。</summary>
        public string RowTip
        {
            get
            {
                string tip = DeviceText + "  ·  " + LevelText + "  ·  " + StatusText;
                if (!string.IsNullOrWhiteSpace(Dto.Cause)) tip += "\n故障原因：" + Dto.Cause;
                if (!string.IsNullOrWhiteSpace(Dto.Handle)) tip += "\n处理结果：" + Dto.Handle;
                return tip;
            }
        }
        /// <summary>级别彩色标签：重大红 / 一般橙（原型 PG-EQP-03 右表）。</summary>
        public Brush LevelBg { get { return Solid(Dto.Level == 1 ? "#FDECEC" : "#FFF5DC"); } }
        public Brush LevelFg { get { return Solid(Dto.Level == 1 ? "#D64545" : "#B76E00"); } }
        /// <summary>状态彩色标签：待接单橙 / 维修中紫 / 已修复绿 / 其他灰。</summary>
        public string StatusKey
        {
            get
            {
                string t = StatusText;
                if (t == "维修中") return "repair";
                if (t == "已修复") return "repaired";
                if (t == "待接单") return "pending";
                return "other";
            }
        }
        public Brush StatusBg
        {
            get
            {
                switch (StatusKey)
                {
                    case "repair": return Solid("#EFEEFF");
                    case "repaired": return Solid("#E8F7F1");
                    case "pending": return Solid("#FFF5DC");
                    default: return Solid("#F2F4F8");
                }
            }
        }
        public Brush StatusFg
        {
            get
            {
                switch (StatusKey)
                {
                    case "repair": return Solid("#5B3DF5");
                    case "repaired": return Solid("#12805C");
                    case "pending": return Solid("#B76E00");
                    default: return Solid("#667085");
                }
            }
        }
        private static Brush Solid(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>故障设备下拉项（可编辑下拉：显示「编号 · 名称 · 位置」，TextSearch.TextPath=Display 保证选中回显）。</summary>
    public class FaultDeviceOption
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

    /// <summary>
    /// 故障登记（PG-EQP-03，UC-EQP-004，BR-EQP-04）。
    /// R4 变更：「重大故障 → 转应急发起」功能已下线，本页不再跳转应急处置，也不做强提示。
    /// R7 变更：移除主面板「生成维修工单」入口（统一走右表操作列）与「关联应急事件」下拉（应急无业务联动）。
    /// </summary>
    public class DeviceFaultViewModel : BaseInfoPageViewModel
    {
        private DeviceDto _selectedDevice;
        private FaultDeviceOption _selectedDeviceOption;
        private int _level;
        private string _reporter = string.Empty;
        private string _ftimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        private string _faultDesc = string.Empty;
        private string _deviceSearch = string.Empty;
        private ListCollectionView _deviceView;
        private readonly List<FaultDeviceOption> _allDevices = new List<FaultDeviceOption>();

        public DeviceFaultViewModel(IApiClient api) : base(api)
        {
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            GenerateWorkOrderCommand = new AsyncRelayCommand<FaultRow>(GenerateWorkOrderAsync);
            OpenRepairDoneCommand = new RelayCommand<FaultRow>(OpenRepairDone);
            CancelRepairDoneCommand = new RelayCommand(CloseRepairDone);
            ConfirmRepairDoneCommand = new AsyncRelayCommand(ConfirmRepairDoneAsync);
            _ = LoadAsync();
        }

        public ObservableCollection<FaultRow> RecentFaults { get; } = new ObservableCollection<FaultRow>();
        /// <summary>故障设备下拉过滤视图（输入字符自动检索排列）。</summary>
        public ListCollectionView DeviceView { get { return _deviceView; } }
        /// <summary>故障设备搜索词：输入即按编号/名称/位置过滤。</summary>
        public string DeviceSearch { get { return _deviceSearch; } set { if (SetProperty(ref _deviceSearch, value)) ApplyDeviceFilter(); } }
        /// <summary>故障设备下拉选中项（显示「编号 · 名称 · 位置」；输入检索过程中的瞬时 null 忽略）。</summary>
        public FaultDeviceOption SelectedDeviceOption
        {
            get { return _selectedDeviceOption; }
            set
            {
                if (value == null) return;
                if (SetProperty(ref _selectedDeviceOption, value)) SelectedDevice = value.Dto;
            }
        }
        /// <summary>故障设备（DeviceDto 整体绑定，修复 SelectedValue/SelectedValuePath 错配 P0：故障单永远挂第一台设备）。</summary>
        public DeviceDto SelectedDevice
        {
            get { return _selectedDevice; }
            set
            {
                if (value == null) return; // 输入检索过程中的瞬时 null 忽略，避免误清空
                SetProperty(ref _selectedDevice, value);
            }
        }

        /// <summary>跨页导航钩子（ShellViewModel 中央接线用）：设备列表「报修」→ 本页预选设备。</summary>
        public void PreselectDevice(int deviceId)
        {
            var dev = _allDevices.FirstOrDefault(x => x.Id == deviceId);
            if (dev != null) SelectedDeviceOption = dev;
        }
        /// <summary>故障级别索引：0 一般 1 重大。</summary>
        public int Level { get { return _level; } set { SetProperty(ref _level, value); } }
        public string Reporter { get { return _reporter; } set { SetProperty(ref _reporter, value); } }
        /// <summary>发现时间文本（默认当前，可改，保存时解析）。</summary>
        public string FTimeText { get { return _ftimeText; } set { SetProperty(ref _ftimeText, value); } }
        public string FaultDesc { get { return _faultDesc; } set { SetProperty(ref _faultDesc, value); } }
        public IAsyncRelayCommand SaveCommand { get; }
        /// <summary>最近故障行「生成工单」（CommandParameter=FaultRow）；R7 起主面板不再提供工单入口。</summary>
        public IAsyncRelayCommand<FaultRow> GenerateWorkOrderCommand { get; }
        /// <summary>最近故障行「维修完成」→ 打开原因/处理结果浮层。</summary>
        public IRelayCommand<FaultRow> OpenRepairDoneCommand { get; }
        public IRelayCommand CancelRepairDoneCommand { get; }
        public IAsyncRelayCommand ConfirmRepairDoneCommand { get; }

        // ---------- 维修完成浮层（线下维修后线上确认，形成闭环） ----------
        private FaultRow _repairTarget;
        private bool _isRepairDoneVisible;
        private string _repairDoneTitle = string.Empty;
        private string _repairCause = string.Empty;
        private string _repairHandle = string.Empty;
        private string _repairError = string.Empty;

        public bool IsRepairDoneVisible { get { return _isRepairDoneVisible; } private set { SetProperty(ref _isRepairDoneVisible, value); } }
        public string RepairDoneTitle { get { return _repairDoneTitle; } private set { SetProperty(ref _repairDoneTitle, value); } }
        /// <summary>故障原因（可沿用原值，可留空）。</summary>
        public string RepairCause { get { return _repairCause; } set { SetProperty(ref _repairCause, value); } }
        /// <summary>处理结果（必填，BR-EQP-04）。</summary>
        public string RepairHandle { get { return _repairHandle; } set { SetProperty(ref _repairHandle, value); } }
        public string RepairErrorText { get { return _repairError; } private set { SetProperty(ref _repairError, value); } }

        private async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var page = await Api.QueryDevicesAsync(new DeviceQueryRequest { PageIndex = 1, PageSize = 100 });
                int keepId = _selectedDevice == null ? 0 : _selectedDevice.Id;
                _allDevices.Clear();
                foreach (var d in page.Items) _allDevices.Add(new FaultDeviceOption { Dto = d });
                _deviceView = new ListCollectionView(_allDevices);
                ApplyDeviceFilter();
                OnPropertyChanged(nameof(DeviceView));
                var keep = _allDevices.FirstOrDefault(d => d.Id == keepId) ?? _allDevices.FirstOrDefault();
                if (keep != null) SelectedDeviceOption = keep;
                await LoadRecentFaultsAsync();
            }, "设备已加载");
        }

        /// <summary>故障设备下拉过滤：关键词命中「编号 · 名称 · 位置」（输入字符自动检索排列）。</summary>
        private void ApplyDeviceFilter()
        {
            if (_deviceView == null) return;
            string kw = _deviceSearch == null ? string.Empty : _deviceSearch.Trim();
            _deviceView.Filter = o =>
            {
                var opt = o as FaultDeviceOption;
                if (opt == null) return false;
                if (kw.Length == 0) return true;
                return (opt.Display ?? string.Empty).IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0;
            };
            _deviceView.Refresh();
        }

        private async Task LoadRecentFaultsAsync()
        {
            var faults = await Api.GetAllFaultsAsync();
            RecentFaults.Clear();
            foreach (var f in faults.OrderByDescending(x => x.FTime)) RecentFaults.Add(new FaultRow { Dto = f });
        }

        private async Task SaveAsync()
        {
            if (SelectedDevice == null) { ErrorText = "请选择故障设备"; return; }
            if (string.IsNullOrWhiteSpace(FaultDesc)) { ErrorText = "故障描述不能为空"; return; }
            if (string.IsNullOrWhiteSpace(Reporter)) { ErrorText = "请填写发现人"; return; }
            DateTime ftime;
            if (!DateTime.TryParse(FTimeText, out ftime)) { ErrorText = "发现时间格式应为 yyyy-MM-dd HH:mm"; return; }
            var request = new FaultRecordRequest
            {
                DeviceId = SelectedDevice.Id,
                EventId = null, // R7：关联应急事件入口已下线（应急模块无业务联动），新故障单不再填写关联
                FTime = ftime,
                Symptom = FaultDesc.Trim(),
                Level = _level,
                Reporter = Reporter.Trim()
            };
            string msg = null;
            await RunAsync(async () =>
            {
                var dto = await Api.AddFaultAsync(SelectedDevice.Id, request);
                string no = dto == null ? null : dto.FaultNo;
                msg = string.IsNullOrEmpty(no)
                    ? "已保存故障单"
                    : "已生成故障单 " + no + "，已通知工程班组";
                FaultDesc = string.Empty;
                await LoadRecentFaultsAsync();
            }, null);
            // RunAsync 无消息参数时清空状态行，故保存结果提示在链路结束后回写
            if (!string.IsNullOrEmpty(msg)) StatusText = DateTime.Now.ToString("HH:mm:ss ") + msg;
        }

        /// <summary>
        /// 生成维修工单（R5）：以 Excel 派工单形式生成并落盘到本机「文档」（可打印/可线下回填），
        /// 故障单由「待接单」推进为「维修中」，设备状态同步为维修中（设备列表随即可见）。
        /// row=null 时取左侧所选设备最早的未闭环故障单（左表单按钮入口）。
        /// </summary>
        private async Task GenerateWorkOrderAsync(FaultRow row)
        {
            var fault = row == null ? null : row.Dto;
            if (fault == null)
            {
                if (SelectedDevice == null) { ErrorText = "请选择故障设备"; return; }
                var faults = await Api.GetFaultsAsync(SelectedDevice.Id);
                var open = (faults ?? new List<FaultRecordDto>()).Where(f => f.Status < 2).OrderBy(f => f.Id).FirstOrDefault();
                if (open == null)
                {
                    ErrorText = (faults == null || faults.Count == 0) ? "该设备暂无故障单，请先保存故障单" : "该设备故障均已修复，无需生成工单";
                    return;
                }
                fault = open;
            }
            string msg = null;
            await RunAsync(async () =>
            {
                var result = await Api.GenerateFaultWorkOrderAsync(fault.Id);
                if (result == null || result.Id <= 0) { ErrorText = "维修工单生成失败，请重试"; return; }
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string savePath = Path.Combine(dir, string.IsNullOrEmpty(result.FileName) ? ("维修工单_" + fault.Id + ".xlsx") : result.FileName);
                await Api.DownloadExportFileAsync(result.Id, savePath);
                msg = "已生成维修工单 " + (string.IsNullOrEmpty(fault.FaultNo) ? ("WX-" + fault.Id) : fault.FaultNo)
                      + "（Excel 已保存到「文档」：" + Path.GetFileName(savePath) + "），故障已转维修中";
                await LoadRecentFaultsAsync();
            }, null);
            if (!string.IsNullOrEmpty(msg)) StatusText = DateTime.Now.ToString("HH:mm:ss ") + msg;
        }

        /// <summary>维修完成浮层：线下维修完成后回填原因/处理结果（R5 闭环：已修复 + 设备回在用）。</summary>
        private void OpenRepairDone(FaultRow row)
        {
            if (row == null) return;
            _repairTarget = row;
            RepairDoneTitle = (string.IsNullOrEmpty(row.Dto.FaultNo) ? "故障单" : row.Dto.FaultNo) + " · " + row.DeviceText;
            RepairCause = row.Dto.Cause ?? string.Empty;
            RepairHandle = string.Empty;
            RepairErrorText = string.Empty;
            IsRepairDoneVisible = true;
        }

        private void CloseRepairDone()
        {
            IsRepairDoneVisible = false;
            _repairTarget = null;
        }

        private async Task ConfirmRepairDoneAsync()
        {
            if (_repairTarget == null) return;
            if (string.IsNullOrWhiteSpace(RepairHandle)) { RepairErrorText = "处理结果不能为空"; return; }
            var target = _repairTarget;
            string msg = null;
            await RunAsync(async () =>
            {
                var dto = await Api.HandleFaultAsync(target.Dto.Id, new FaultHandleRequest
                {
                    Status = 2,
                    Cause = (RepairCause ?? string.Empty).Trim(),
                    Handle = RepairHandle.Trim()
                });
                msg = "故障单 " + (string.IsNullOrEmpty(dto.FaultNo) ? target.Dto.FaultNo : dto.FaultNo) + " 已修复，设备状态已同步回「在用」";
                IsRepairDoneVisible = false;
                _repairTarget = null;
                await LoadRecentFaultsAsync();
            }, null);
            if (!string.IsNullOrEmpty(msg)) StatusText = DateTime.Now.ToString("HH:mm:ss ") + msg;
        }
    }
}
