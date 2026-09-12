using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    /// <summary>备份/恢复记录行（R12：时间 / 类型 / 备份与恢复路径 / 操作人 / 结果，复核人列下线）。</summary>
    public class BackupRecordRow : ObservableObject
    {
        private static readonly Brush SuccessFg = BrushHelper.FromHex("#12805C");
        private static readonly Brush DangerFg = BrushHelper.FromHex("#D64545");
        private static readonly Brush MutedFg = BrushHelper.FromHex("#98A2B3");

        public BackupDto Dto { get; set; }

        private bool _isSelected;

        /// <summary>R13：勾选框选中态（批量删除依据）。</summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        public string TimeText
        {
            get { return Dto.CreatedAt == default(DateTime) ? "—" : Dto.CreatedAt.ToString("yyyy-MM-dd HH:mm"); }
        }

        /// <summary>类型：数据恢复（kind=restore）/ 自动备份 / 手动备份（含恢复前自动快照）。</summary>
        public string KindText
        {
            get
            {
                if (Dto.Kind == "restore") { return "数据恢复"; }
                return Dto.BackupType == "auto" ? "自动备份" : "手动备份";
            }
        }

        /// <summary>备份与恢复路径（完整路径，便于直接定位数据文件）。</summary>
        public string PathText { get { return string.IsNullOrEmpty(Dto.FilePath) ? "—" : Dto.FilePath; } }

        public string Operator { get { return string.IsNullOrEmpty(Dto.Operator) ? "系统" : Dto.Operator; } }

        public string Result { get { return string.IsNullOrEmpty(Dto.Result) ? "—" : Dto.Result; } }

        public Brush ResultBrush
        {
            get
            {
                string result = Dto.Result ?? string.Empty;
                if (result.Contains("失败")) { return DangerFg; }
                if (result.Length == 0) { return MutedFg; }
                return SuccessFg;
            }
        }

        public Brush ResultBg
        {
            get
            {
                string result = Dto.Result ?? string.Empty;
                if (result.Contains("失败")) { return BrushHelper.FromHex("#FDECEC"); }
                if (result.Length == 0) { return BrushHelper.FromHex("#F4F7FB"); }
                return BrushHelper.FromHex("#E8F7F1");
            }
        }

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

    /// <summary>
    /// 备份与恢复（PG-COM-02，UC-COM-005，BR-COM-04；R12 调整）：
    /// 1) 立即备份支持手动选择保存位置（文件夹 + 文件名）；
    /// 2) 发起恢复支持手动选择备份数据文件（并校验为本系统 SQLite 备份）；
    /// 3) 恢复表单移除第二管理员确认（本系统无第二管理员业务），保留操作密码 + 确认文本；
    /// 4) 备份计划表下线；「最近恢复/演练记录」调整为「备份/恢复记录」（去掉复核人列、路径列展示完整路径）。
    /// </summary>
    public class BackupViewModel : BaseInfoPageViewModel
    {
        private const string RestoreConfirmText = "覆盖当前数据不可撤销";

        private static readonly Brush SuccessBrush = BrushHelper.FromHex("#12805C");
        private static readonly Brush DangerBrush = BrushHelper.FromHex("#D64545");
        private static readonly Brush MutedBrush = BrushHelper.FromHex("#98A2B3");
        private static readonly Brush SuccessSoftBrush = BrushHelper.FromHex("#E8F7F1");
        private static readonly Brush DangerSoftBrush = BrushHelper.FromHex("#FDECEC");
        private static readonly Brush MutedSoftBrush = BrushHelper.FromHex("#F4F7FB");

        private string _lastAutoBackupText = "—";
        private string _lastAutoBackupResult = "尚未执行备份";
        private Brush _lastAutoBackupResultBrush = MutedBrush;
        private Brush _lastAutoBackupResultBg = MutedSoftBrush;
        private string _databaseSizeText = "—";
        private string _databaseSizeSubText = "SQLite 主库文件";
        private bool _isRestoreFormVisible;
        private bool _isRestoreConfirmVisible;
        private string _restoreConfirmInput = string.Empty;
        private string _operationPassword = string.Empty;
        private string _restoreSourcePath = string.Empty;
        private bool _isConfirmVisible;
        private bool _isSelectionMode;        // R14：选择模式（点击「批量删除记录」后才显示勾选框列）
        private string _confirmTitle = "确认操作";
        private string _confirmMessage = string.Empty;
        private PendingAction _pendingAction = PendingAction.None;

        /// <summary>待确认动作（R13：记录批量删除 / 一键清理残余数据）。</summary>
        private enum PendingAction { None, BatchDeleteRecords, Purge }

        public BackupViewModel(IApiClient api) : base(api)
        {
            RunCommand = new AsyncRelayCommand(RunBackupAsync);
            RefreshCommand = new AsyncRelayCommand(LoadAsync);
            OpenRestoreCommand = new RelayCommand(OpenRestoreForm);
            BrowseRestoreFileCommand = new RelayCommand(BrowseRestoreFile);
            SubmitRestoreCommand = new RelayCommand(SubmitRestore);
            ConfirmRestoreCommand = new AsyncRelayCommand(ConfirmRestoreAsync);
            CancelRestoreCommand = new RelayCommand(CancelRestore);
            BatchDeleteRecordsCommand = new RelayCommand(BatchDeleteRecords);
            CancelSelectionCommand = new RelayCommand(ExitSelectionMode);
            PurgeCommand = new RelayCommand(RequestPurge);
            ConfirmActionCommand = new AsyncRelayCommand(ConfirmActionAsync);
            CancelConfirmCommand = new RelayCommand(CancelConfirm);
            _ = LoadAsync();
        }

        public ObservableCollection<BackupRecordRow> Records { get; } = new ObservableCollection<BackupRecordRow>();

        // ---------- 状态卡（GET /system/backups/status） ----------
        public string LastAutoBackupText
        {
            get { return _lastAutoBackupText; }
            private set { SetProperty(ref _lastAutoBackupText, value); }
        }

        public string LastAutoBackupResult
        {
            get { return _lastAutoBackupResult; }
            private set { SetProperty(ref _lastAutoBackupResult, value); }
        }

        public Brush LastAutoBackupResultBrush
        {
            get { return _lastAutoBackupResultBrush; }
            private set { SetProperty(ref _lastAutoBackupResultBrush, value); }
        }

        public Brush LastAutoBackupResultBg
        {
            get { return _lastAutoBackupResultBg; }
            private set { SetProperty(ref _lastAutoBackupResultBg, value); }
        }

        public string DatabaseSizeText
        {
            get { return _databaseSizeText; }
            private set { SetProperty(ref _databaseSizeText, value); }
        }

        /// <summary>数据库大小卡副文案：含附件目录占用（GET /system/backups/status attachmentsSizeText）。</summary>
        public string DatabaseSizeSubText
        {
            get { return _databaseSizeSubText; }
            private set { SetProperty(ref _databaseSizeSubText, value); }
        }

        /// <summary>R13：记录全选/取消全选（表头勾选框双向绑定）。</summary>
        public bool IsAllRecordsSelected
        {
            get { return Records.Count > 0 && Records.All(r => r.IsSelected); }
            set
            {
                foreach (BackupRecordRow row in Records) { row.IsSelected = value; }
                OnPropertyChanged(nameof(IsAllRecordsSelected));
            }
        }

        /// <summary>刷新全选态（勾选/取消单行后由视图调用）。</summary>
        public void RefreshSelectAllState()
        {
            OnPropertyChanged(nameof(IsAllRecordsSelected));
        }

        /// <summary>
        /// R14：选择模式。默认 false（记录表不显示勾选框列）；点击「批量删除记录」后进入选择模式，
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
            get { return IsSelectionMode ? "删除所选" : "批量删除记录"; }
        }

        /// <summary>确认弹窗（记录批量删除 / 一键清理残余数据共用）。</summary>
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

        // ---------- 恢复表单（密码框经视图代码后置回填） ----------

        /// <summary>恢复数据文件路径（手动选择）。</summary>
        public string RestoreSourcePath
        {
            get { return _restoreSourcePath; }
            set { SetProperty(ref _restoreSourcePath, value); }
        }

        public string OperationPassword
        {
            get { return _operationPassword; }
            set { SetProperty(ref _operationPassword, value); }
        }

        /// <summary>恢复步骤一弹层：选择备份文件 + 操作密码（R12 起无第二管理员）。</summary>
        public bool IsRestoreFormVisible
        {
            get { return _isRestoreFormVisible; }
            set { SetProperty(ref _isRestoreFormVisible, value); }
        }

        public bool IsRestoreConfirmVisible
        {
            get { return _isRestoreConfirmVisible; }
            set { SetProperty(ref _isRestoreConfirmVisible, value); }
        }

        public string RestoreConfirmInput
        {
            get { return _restoreConfirmInput; }
            set { SetProperty(ref _restoreConfirmInput, value); }
        }

        public IAsyncRelayCommand RunCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }
        public IRelayCommand OpenRestoreCommand { get; }
        public IRelayCommand BrowseRestoreFileCommand { get; }
        public IRelayCommand SubmitRestoreCommand { get; }
        public IAsyncRelayCommand ConfirmRestoreCommand { get; }
        public IRelayCommand CancelRestoreCommand { get; }
        public IRelayCommand BatchDeleteRecordsCommand { get; }
        public IRelayCommand CancelSelectionCommand { get; }
        public IRelayCommand PurgeCommand { get; }
        public IAsyncRelayCommand ConfirmActionCommand { get; }
        public IRelayCommand CancelConfirmCommand { get; }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                BackupStatusDto status = await Api.GetSystemBackupStatusAsync();
                ApplyStatus(status);

                List<BackupDto> backups = await Api.GetSystemBackupsAsync() ?? new List<BackupDto>();
                Records.Clear();
                foreach (BackupDto dto in backups)
                {
                    Records.Add(new BackupRecordRow { Dto = dto });
                }
            }, null);
        }

        private void ApplyStatus(BackupStatusDto status)
        {
            if (status == null) { return; }
            LastAutoBackupText = status.LastAutoBackupAt.HasValue ? status.LastAutoBackupAt.Value.ToString("MM-dd HH:mm") : "—";
            LastAutoBackupResult = string.IsNullOrEmpty(status.LastAutoBackupResult) ? "尚未执行备份" : status.LastAutoBackupResult;
            LastAutoBackupResultBrush = LastAutoBackupResult.Contains("成功") ? SuccessBrush : (LastAutoBackupResult.Contains("失败") ? DangerBrush : MutedBrush);
            LastAutoBackupResultBg = LastAutoBackupResult.Contains("成功") ? SuccessSoftBrush : (LastAutoBackupResult.Contains("失败") ? DangerSoftBrush : MutedSoftBrush);
            DatabaseSizeText = string.IsNullOrEmpty(status.DatabaseSizeText) ? "—" : status.DatabaseSizeText;
            DatabaseSizeSubText = string.IsNullOrEmpty(status.AttachmentsSizeText)
                ? "SQLite 主库文件"
                : "含附件 " + status.AttachmentsSizeText;
        }

        // ---------- R13：记录批量删除 / 一键清理残余数据 ----------
        private void BatchDeleteRecords()
        {
            if (!IsSelectionMode)
            {
                EnterSelectionMode();
                return;
            }

            List<BackupRecordRow> selected = Records.Where(r => r.IsSelected).ToList();
            if (selected.Count == 0)
            {
                ErrorText = "请先勾选要删除的记录（可勾选单条，也可勾选表头全选），或点击【取消】退出批量删除";
                return;
            }
            _pendingAction = PendingAction.BatchDeleteRecords;
            ConfirmTitle = "批量删除记录";
            ConfirmMessage = "将删除所选 " + selected.Count + " 条备份/恢复记录（记录留痕，备份文件本身保留）。确认删除？";
            IsConfirmVisible = true;
        }

        private void RequestPurge()
        {
            _pendingAction = PendingAction.Purge;
            ConfirmTitle = "一键清理残余数据";
            ConfirmMessage = "将清理系统中所有「软删留痕」数据记录，此操作不可撤销——一旦删除，一切过往软删留痕记录将不存在。\n\n" +
                             "清理只作用于已删除的留痕数据，不会破坏现有在用数据。\n" +
                             "温馨提醒：建议先执行一次【立即备份】再清理，确保数据可回溯。";
            IsConfirmVisible = true;
        }

        private async Task ConfirmActionAsync()
        {
            PendingAction action = _pendingAction;
            _pendingAction = PendingAction.None;
            IsConfirmVisible = false;

            if (action == PendingAction.BatchDeleteRecords)
            {
                var ids = Records.Where(r => r.IsSelected).Select(r => r.Dto.Id).Distinct().ToList();
                if (ids.Count == 0) { return; }
                string message = null;
                await RunAsync(async () =>
                {
                    RecordBatchDeleteResultDto result = await Api.BatchDeleteBackupRecordsAsync(
                        new RecordBatchDeleteRequest { Ids = ids });
                    message = "已删除 " + (result == null ? 0 : result.Deleted) + " 条记录";
                    await LoadAsync();
                    ExitSelectionMode();
                }, null);
                StatusText = string.IsNullOrEmpty(message) ? StatusText : message;
                return;
            }

            if (action == PendingAction.Purge)
            {
                string message = null;
                await RunAsync(async () =>
                {
                    PurgeSoftDeletedResultDto result = await Api.PurgeSoftDeletedAsync();
                    int total = result == null ? 0 : result.TotalPurged;
                    message = total > 0
                        ? "已清理 " + total + " 行软删留痕数据"
                        : "没有可清理的软删留痕数据";
                    await LoadAsync();
                }, null);
                StatusText = string.IsNullOrEmpty(message) ? StatusText : message;
            }
        }

        private void CancelConfirm()
        {
            _pendingAction = PendingAction.None;
            IsConfirmVisible = false;
        }

        /// <summary>进入选择模式：显示勾选框列并清空历史勾选。</summary>
        private void EnterSelectionMode()
        {
            foreach (BackupRecordRow row in Records) { row.IsSelected = false; }
            IsSelectionMode = true;
            OnPropertyChanged(nameof(IsAllRecordsSelected));
        }

        /// <summary>退出选择模式：隐藏勾选框列并清空勾选。</summary>
        private void ExitSelectionMode()
        {
            foreach (BackupRecordRow row in Records) { row.IsSelected = false; }
            IsSelectionMode = false;
            OnPropertyChanged(nameof(IsAllRecordsSelected));
        }

        /// <summary>
        /// 立即备份（R12）：先让用户选择保存位置（文件夹 + 文件名），再把备份数据文件写入该路径；
        /// 取消保存对话框则不执行备份。
        /// </summary>
        private async Task RunBackupAsync()
        {
            var dialog = new SaveFileDialog
            {
                Title = "选择备份文件保存位置",
                Filter = "备份数据文件 (*.db)|*.db",
                FileName = "备份_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".db",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                OverwritePrompt = true
            };
            if (dialog.ShowDialog() != true) { return; }

            string targetPath = dialog.FileName;
            string message = null;
            await RunAsync(async () =>
            {
                BackupDto result = await Api.RunSystemBackupAsync("手动全量备份", targetPath);
                message = result != null && !string.IsNullOrEmpty(result.FilePath) ? result.FilePath : targetPath;
                await LoadAsync();
            }, null);
            StatusText = string.IsNullOrEmpty(message) ? StatusText : "备份完成：" + message;
        }

        // ---------- 高危恢复链路（R12：选择备份数据文件 → 操作密码 → 确认文本） ----------
        private void OpenRestoreForm()
        {
            ErrorText = string.Empty;
            IsRestoreFormVisible = true;
        }

        /// <summary>选择恢复数据文件（默认定位到系统备份目录）。</summary>
        private void BrowseRestoreFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择备份数据文件",
                Filter = "备份数据文件 (*.db)|*.db|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (!string.IsNullOrWhiteSpace(RestoreSourcePath))
            {
                try
                {
                    string dir = Path.GetDirectoryName(RestoreSourcePath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) { dialog.InitialDirectory = dir; }
                }
                catch (Exception)
                {
                    // 路径解析失败时使用系统默认目录
                }
            }
            if (dialog.ShowDialog() == true)
            {
                RestoreSourcePath = dialog.FileName;
                ErrorText = string.Empty;
            }
        }

        /// <summary>步骤一校验 → 步骤二（二次弹窗「覆盖当前数据不可撤销」）。</summary>
        private void SubmitRestore()
        {
            if (string.IsNullOrWhiteSpace(RestoreSourcePath))
            {
                ErrorText = "请先选择要恢复的备份数据文件";
                return;
            }
            if (!File.Exists(RestoreSourcePath))
            {
                ErrorText = "恢复数据文件不存在，请重新选择";
                return;
            }
            if (string.IsNullOrEmpty(OperationPassword))
            {
                ErrorText = "请输入操作密码（当前登录账号密码）";
                return;
            }

            IsRestoreFormVisible = false;
            RestoreConfirmInput = string.Empty;
            ErrorText = string.Empty;
            IsRestoreConfirmVisible = true;
        }

        private async Task ConfirmRestoreAsync()
        {
            string confirm = (RestoreConfirmInput ?? string.Empty).Trim();
            if (confirm != RestoreConfirmText)
            {
                ErrorText = "确认文本不正确，请输入：" + RestoreConfirmText;
                return;
            }

            string sourcePath = RestoreSourcePath;
            string message = null;
            await RunAsync(async () =>
            {
                await Api.RestoreSystemBackupAsync(new BackupRestoreRequest
                {
                    BackupId = 0,
                    SourcePath = sourcePath,
                    OperationPassword = OperationPassword,
                    ConfirmText = confirm
                });
                IsRestoreConfirmVisible = false;
                OperationPassword = string.Empty;
                message = "数据恢复完成，系统已恢复至所选备份数据文件。";
                await LoadAsync();
            }, null);
            StatusText = message ?? StatusText;
        }

        private void CancelRestore()
        {
            IsRestoreFormVisible = false;
            IsRestoreConfirmVisible = false;
            RestoreConfirmInput = string.Empty;
            OperationPassword = string.Empty;
        }

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
}
