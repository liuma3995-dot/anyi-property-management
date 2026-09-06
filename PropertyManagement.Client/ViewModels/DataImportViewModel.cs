using System;
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
        public int Fail { get { return Dto.Fail; } }
        public string StatusText { get { return Dto.StatusText ?? string.Empty; } }
        public string TimeText { get { return Dto.CreatedAt == default ? "—" : Dto.CreatedAt.ToString("yyyy-MM-dd HH:mm"); } }
        public Brush StatusBg { get { return Dto.Status == ImportStatus.Success ? Br("#E8F7F1") : (Dto.Status == ImportStatus.PartialSuccess ? Br("#FEF0C7") : Br("#F1F3F7")); } }
        public Brush StatusBrush { get { return Dto.Status == ImportStatus.Success ? Br("#12805C") : (Dto.Status == ImportStatus.PartialSuccess ? Br("#B54708") : Br("#667085")); } }
        private static Brush Br(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
    }

    /// <summary>基础数据导入页（PG-INF-05，UC-INF-006，BR-INF-05）。</summary>
    public class DataImportViewModel : BaseInfoPageViewModel
    {
        private ImportModule _module = ImportModule.Property;
        private string _selectedFileName = string.Empty;
        private byte[] _selectedFile;
        private bool _isImporting;

        public DataImportViewModel(IApiClient api) : base(api)
        {
            DownloadTemplateCommand = new AsyncRelayCommand(DownloadTemplateAsync);
            SelectFileCommand = new RelayCommand(SelectFile);
            ImportCommand = new AsyncRelayCommand(ImportAsync);
            DownloadErrorsCommand = new AsyncRelayCommand<ImportLogRow>(DownloadErrorsAsync);
            ReImportCommand = new AsyncRelayCommand<ImportLogRow>(ReImportAsync);
            RefreshCommand = new AsyncRelayCommand(LoadAsync);
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
                    File.WriteAllBytes(dialog.FileName, bytes);
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
                _selectedFile = File.ReadAllBytes(dialog.FileName);
                SelectedFileName = System.IO.Path.GetFileName(dialog.FileName);
                OnPropertyChanged(nameof(HasFile));
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
                    StatusText = DateTime.Now.ToString("HH:mm:ss ") + "导入完成：成功 " + result.Batch.Success + " 行，失败 " + result.Batch.Fail + " 行"
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
                _selectedFile = File.ReadAllBytes(dialog.FileName);
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
                    File.WriteAllBytes(dialog.FileName, bytes);
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
