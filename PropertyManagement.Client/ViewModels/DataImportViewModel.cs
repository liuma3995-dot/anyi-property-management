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
using PropertyManagement.Contract.BaseInfo;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>导入批次行（PG-INF-05）。</summary>
    public class ImportLogRow : ObservableObject
    {
        public ImportLogDto Dto { get; set; }
        public int Id { get { return Dto.Id; } }
        public string BatchNo { get { return "DR-" + Dto.Id.ToString("D4"); } }
        public string ModuleText { get { return Dto.ModuleText ?? Dto.Module.ToString(); } }
        public int Total { get { return Dto.Total; } }
        public int Success { get { return Dto.Success; } }
        /// <summary>CHG-v1.1.2-01：覆盖条数（重复数据按导入文件覆盖既有记录的条数）。</summary>
        public int Updated { get { return Dto.Updated; } }
        public int Fail { get { return Dto.Fail; } }
        /// <summary>结果摘要：新增 / 覆盖 / 失败（覆盖为 0 时不展示，避免干扰）。</summary>
        public string ResultText
        {
            get
            {
                string text = "新增 " + Success;
                if (Updated > 0) { text += " / 覆盖 " + Updated; }
                return text + " / 失败 " + Fail;
            }
        }
        public string StatusText { get { return Dto.StatusText ?? string.Empty; } }
        public string TimeText { get { return Dto.CreatedAt == default ? "—" : Dto.CreatedAt.ToString("yyyy-MM-dd HH:mm"); } }
        public Brush StatusBg { get { return Dto.Status == ImportStatus.Success ? Br("#E8F7F1") : (Dto.Status == ImportStatus.PartialSuccess ? Br("#FEF0C7") : Br("#F1F3F7")); } }
        public Brush StatusBrush { get { return Dto.Status == ImportStatus.Success ? Br("#12805C") : (Dto.Status == ImportStatus.PartialSuccess ? Br("#B54708") : Br("#667085")); } }

        private bool _isSelected;

        /// <summary>v1.1.0-⑤：批量删除勾选状态（仅选择模式下显示勾选框列）。</summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        private static Brush Br(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
    }

    /// <summary>基础数据导入页（PG-INF-05，UC-INF-006，BR-INF-05）。</summary>
    public class DataImportViewModel : BaseInfoPageViewModel
    {
        private ImportModule _module = ImportModule.Property;
        private string _selectedFileName = string.Empty;
        private byte[] _selectedFile;
        private bool _isImporting;
        private bool _isSelectionMode;        // v1.1.0-⑤：点击「批量删除记录」后才显示勾选框列
        private bool _isConfirmVisible;
        private string _confirmTitle = "确认操作";
        private string _confirmMessage = string.Empty;

        public DataImportViewModel(IApiClient api) : base(api)
        {
            DownloadTemplateCommand = new AsyncRelayCommand(DownloadTemplateAsync);
            SelectFileCommand = new RelayCommand(SelectFile);
            ImportCommand = new AsyncRelayCommand(ImportAsync);
            DownloadErrorsCommand = new AsyncRelayCommand<ImportLogRow>(DownloadErrorsAsync);
            ReImportCommand = new AsyncRelayCommand<ImportLogRow>(ReImportAsync);
            RefreshCommand = new AsyncRelayCommand(LoadAsync);
            BatchDeleteRecordsCommand = new RelayCommand(BatchDeleteRecords);
            CancelSelectionCommand = new RelayCommand(ExitSelectionMode);
            ConfirmActionCommand = new AsyncRelayCommand(ConfirmActionAsync);
            CancelConfirmCommand = new RelayCommand(CancelConfirm);
            _ = LoadAsync();
        }

        public ObservableCollection<ImportLogRow> Batches { get; } = new ObservableCollection<ImportLogRow>();

        public ImportModule Module { get { return _module; } set { SetProperty(ref _module, value); } }
        public string SelectedFileName { get { return _selectedFileName; } set { SetProperty(ref _selectedFileName, value); } }
        public bool HasFile { get { return _selectedFile != null; } }
        public bool IsImporting { get { return _isImporting; } private set { SetProperty(ref _isImporting, value); } }

        public IAsyncRelayCommand DownloadTemplateCommand { get; }
        public IRelayCommand SelectFileCommand { get; }
        public IAsyncRelayCommand ImportCommand { get; }
        public IAsyncRelayCommand<ImportLogRow> DownloadErrorsCommand { get; }
        public IAsyncRelayCommand<ImportLogRow> ReImportCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }

        // ---------- v1.1.0-⑤：导入批次记录批量删除（软删留痕） ----------

        public IRelayCommand BatchDeleteRecordsCommand { get; }

        public IRelayCommand CancelSelectionCommand { get; }

        public IAsyncRelayCommand ConfirmActionCommand { get; }

        public IRelayCommand CancelConfirmCommand { get; }

        /// <summary>记录全选/取消全选（表头勾选框双向绑定）。</summary>
        public bool IsAllRecordsSelected
        {
            get { return Batches.Count > 0 && Batches.All(r => r.IsSelected); }
            set
            {
                foreach (ImportLogRow row in Batches) { row.IsSelected = value; }
                OnPropertyChanged(nameof(IsAllRecordsSelected));
            }
        }

        /// <summary>刷新全选态（勾选/取消单行后由视图调用）。</summary>
        public void RefreshSelectAllState()
        {
            OnPropertyChanged(nameof(IsAllRecordsSelected));
        }

        /// <summary>
        /// 选择模式：默认 false（批次表不显示勾选框列）；点击「批量删除记录」后进入选择模式，
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

        /// <summary>批量删除入口：第一次点击进入选择模式，已在选择模式时校验勾选并弹出二次确认。</summary>
        private void BatchDeleteRecords()
        {
            if (!IsSelectionMode)
            {
                EnterSelectionMode();
                return;
            }

            List<ImportLogRow> selected = Batches.Where(r => r.IsSelected).ToList();
            if (selected.Count == 0)
            {
                ErrorText = "请先勾选要删除的导入批次记录（可勾选单条，也可勾选表头全选），或点击【取消】退出批量删除";
                return;
            }
            ConfirmTitle = "批量删除导入批次记录";
            ConfirmMessage = "将删除所选 " + selected.Count + " 条导入批次记录（记录留痕，已导入的业务数据不受影响）。确认删除？";
            IsConfirmVisible = true;
        }

        private async Task ConfirmActionAsync()
        {
            IsConfirmVisible = false;
            var ids = Batches.Where(r => r.IsSelected).Select(r => r.Id).Distinct().ToList();
            if (ids.Count == 0) { return; }

            string message = null;
            await RunAsync(async () =>
            {
                RecordBatchDeleteResultDto result = await Api.BatchDeleteImportLogsAsync(
                    new RecordBatchDeleteRequest { Ids = ids });
                message = "已删除 " + (result == null ? 0 : result.Deleted) + " 条导入批次记录（可在「备份与恢复」页一键清理留痕）";
                await LoadAsync();
                ExitSelectionMode();
            }, null);
            StatusText = string.IsNullOrEmpty(message) ? StatusText : message;
        }

        private void CancelConfirm()
        {
            IsConfirmVisible = false;
        }

        /// <summary>进入选择模式：显示勾选框列并清空历史勾选。</summary>
        private void EnterSelectionMode()
        {
            foreach (ImportLogRow row in Batches) { row.IsSelected = false; }
            ErrorText = string.Empty;
            IsSelectionMode = true;
            OnPropertyChanged(nameof(IsAllRecordsSelected));
        }

        /// <summary>退出选择模式：隐藏勾选框列并清空勾选。</summary>
        private void ExitSelectionMode()
        {
            foreach (ImportLogRow row in Batches) { row.IsSelected = false; }
            IsSelectionMode = false;
            IsConfirmVisible = false;
            OnPropertyChanged(nameof(IsAllRecordsSelected));
        }

        private async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var logs = await Api.GetImportLogsAsync();
                Batches.Clear();
                foreach (var dto in logs) Batches.Add(new ImportLogRow { Dto = dto });
            }, "导入记录已加载");
        }

        private async Task DownloadTemplateAsync()
        {
            await RunAsync(async () =>
            {
                var bytes = await Api.DownloadBaseInfoTemplateAsync(Module);
                var dialog = new SaveFileDialog
                {
                    Filter = "Excel 文件|*.xlsx",
                    FileName = "导入模板_" + ModuleLabel(Module) + "_" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx"
                };
                if (dialog.ShowDialog() == true)
                {
                    string saveError;
                    if (!TryWriteFile(dialog.FileName, bytes, out saveError))
                    {
                        ErrorText = saveError;
                        MessageBox.Show(saveError, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    StatusText = DateTime.Now.ToString("HH:mm:ss ") + "模板已下载：" + dialog.FileName;
                    MessageBox.Show("模板已下载到：" + dialog.FileName, "下载成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }, "正在生成模板…");
        }

        /// <summary>导入类型中文名称（用于模板默认文件名）。</summary>
        private static string ModuleLabel(ImportModule m)
        {
            switch (m)
            {
                case ImportModule.Owner: return "业主";
                case ImportModule.Parking: return "车位";
                case ImportModule.OwnerRelation: return "业主-房产关系";
                default: return "房产";
            }
        }

        private void SelectFile()
        {
            var dialog = new OpenFileDialog { Filter = "Excel 文件|*.xlsx;*.xls" };
            if (dialog.ShowDialog() == true)
            {
                // 修复（v1.1.0-①）：文件正被 Excel/WPS 占用时 File.ReadAllBytes 会抛未处理异常
                // → 全局异常处理器 Shutdown(1)，用户看到的是"程序崩溃"。这里改为共享读 + 业务化提示。
                byte[] content;
                string readError;
                if (!TryReadFile(dialog.FileName, out content, out readError))
                {
                    ErrorText = readError;
                    StatusText = string.Empty;
                    MessageBox.Show(readError, "无法读取文件", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _selectedFile = content;
                SelectedFileName = System.IO.Path.GetFileName(dialog.FileName);
                OnPropertyChanged(nameof(HasFile));
                ErrorText = string.Empty;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已选择文件：" + SelectedFileName;
            }
        }

        /// <summary>
        /// 读取用户选择的数据文件。以 <see cref="FileShare.ReadWrite"/> 打开，兼容「文件仍在 Excel/WPS 中打开」
        /// 的常见场景（只读共享即可读到已保存内容）；确实无法读取时返回业务化提示而不是让进程崩溃。
        /// </summary>
        private static bool TryReadFile(string path, out byte[] content, out string error)
        {
            content = null;
            error = null;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                {
                    var buffer = new byte[stream.Length];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int n = stream.Read(buffer, read, buffer.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read != buffer.Length) Array.Resize(ref buffer, read);
                    content = buffer;
                }
                if (content.Length == 0) { error = "所选文件为空，请重新选择。"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)) throw;
                error = "无法读取所选文件：" + ex.Message + Environment.NewLine +
                        "若该文件正在 Excel/WPS 中打开，请先保存并关闭后重试。";
                return false;
            }
        }

        /// <summary>保存导出文件：目标文件被占用/无权限时给出提示而不是崩溃。</summary>
        private static bool TryWriteFile(string path, byte[] bytes, out string error)
        {
            error = null;
            try
            {
                File.WriteAllBytes(path, bytes);
                return true;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)) throw;
                error = "无法写入文件：" + ex.Message + Environment.NewLine +
                        "若目标文件正在 Excel/WPS 中打开，请先关闭后重试，或另存为其它文件名。";
                return false;
            }
        }

        private async Task ImportAsync()
        {
            if (_selectedFile == null || _selectedFile.Length == 0) { ErrorText = "请先选择文件"; return; }
            IsImporting = true;
            try
            {
                ImportResultDto result = null;
                await RunAsync(async () =>
                {
                    result = await Api.ImportAsync(new ImportRequest
                    {
                        Module = Module,
                        FileName = SelectedFileName,
                        FileContent = _selectedFile
                    });
                    _selectedFile = null;
                    SelectedFileName = string.Empty;
                    OnPropertyChanged(nameof(HasFile));
                    await LoadAsync();
                }, "正在导入…");
                if (result != null)
                {
                    string reason = result.Errors.Count > 0 ? (result.Errors[0].Reason ?? "请下载回执查看") : string.Empty;
                    // CHG-v1.1.2-01：重复数据做覆盖处理 —— 结果区分「新增」与「覆盖」，让用户看清这次导入改了什么
                    StatusText = DateTime.Now.ToString("HH:mm:ss ") + "导入完成：新增 " + result.Batch.Success + " 行，覆盖 "
                        + result.Batch.Updated + " 行，失败 " + result.Batch.Fail + " 行"
                        + (reason.Length > 0 ? "；" + reason : string.Empty);
                }
            }
            finally
            {
                IsImporting = false;
            }
        }

        private async Task ReImportAsync(ImportLogRow row)
        {
            if (row == null) return;
            Module = row.Dto.Module;
            var dialog = new OpenFileDialog { Filter = "Excel 文件|*.xlsx;*.xls" };
            if (dialog.ShowDialog() == true)
            {
                byte[] content;
                string readError;
                if (!TryReadFile(dialog.FileName, out content, out readError))
                {
                    ErrorText = readError;
                    MessageBox.Show(readError, "无法读取文件", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _selectedFile = content;
                SelectedFileName = System.IO.Path.GetFileName(dialog.FileName);
                OnPropertyChanged(nameof(HasFile));
                await ImportAsync();
            }
        }

        private async Task DownloadErrorsAsync(ImportLogRow row)
        {
            if (row == null || row.Fail == 0) { MessageBox.Show("该批次无错误清单", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            bool saved = false;
            string savedPath = string.Empty;
            await RunAsync(async () =>
            {
                var bytes = await Api.DownloadImportErrorsAsync(row.Id);
                var dialog = new SaveFileDialog { Filter = "Excel 文件|*.xlsx", FileName = "导入错误_" + row.BatchNo + ".xlsx" };
                if (dialog.ShowDialog() == true)
                {
                    string saveError;
                    if (!TryWriteFile(dialog.FileName, bytes, out saveError))
                    {
                        ErrorText = saveError;
                        MessageBox.Show(saveError, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    saved = true;
                    savedPath = dialog.FileName;
                }
            }, "正在生成错误清单…");
            if (saved)
            {
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "错误清单已下载：" + savedPath;
                MessageBox.Show("错误清单已下载到：" + savedPath, "下载成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
