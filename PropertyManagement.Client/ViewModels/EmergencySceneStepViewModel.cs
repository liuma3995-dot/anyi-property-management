using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Emergency;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>处置步骤行（PG-EMG-01）。</summary>
    public class EmergencyStepRow : ObservableObject
    {
        private EmergencyStepDto _dto;

        public EmergencyStepDto Dto
        {
            get { return _dto; }
            set { if (SetProperty(ref _dto, value)) OnPropertyChanged(string.Empty); }
        }

        public string StepNo { get { return Dto.StepNo.ToString(); } }
        public string Content { get { return Dto.Content ?? string.Empty; } }
        public string Role { get { return Dto.Role ?? string.Empty; } }
        public string TimeLimit { get { return Dto.TimeLimit ?? string.Empty; } }
        public string Action { get { return Dto.Action ?? string.Empty; } }
        /// <summary>联动动作非空渲染执行按钮（原型「联动动作点击后直接执行」）。</summary>
        public bool HasAction { get { return !string.IsNullOrWhiteSpace(Action); } }

        /// <summary>拖拽重排后刷新全部派生属性（StepNo 等）。</summary>
        public void Refresh()
        {
            OnPropertyChanged(string.Empty);
        }
    }

    /// <summary>场景列表行：包装 EmergencySceneDto 并附带批量删除勾选状态。</summary>
    public class EmergencySceneItem : ObservableObject
    {
        private EmergencySceneDto _dto;
        private bool _isChecked;

        public EmergencySceneDto Dto
        {
            get { return _dto; }
            set { if (SetProperty(ref _dto, value)) OnPropertyChanged(string.Empty); }
        }

        public int Id { get { return Dto == null ? 0 : Dto.Id; } }
        public string Name { get { return Dto == null ? string.Empty : Dto.Name; } }
        public string IconKey { get { return Dto == null ? null : Dto.IconKey; } }
        public string TimeLimitText { get { return Dto == null ? string.Empty : Dto.TimeLimitText; } }
        public int Status { get { return Dto == null ? 0 : Dto.Status; } }

        public bool IsChecked
        {
            get { return _isChecked; }
            set { SetProperty(ref _isChecked, value); }
        }
    }

    /// <summary>场景与步骤维护（PG-EMG-01，UC-EMG-001/002，BR-EMG-01/05）。</summary>
    public class EmergencySceneStepViewModel : BaseInfoPageViewModel
    {
        private EmergencySceneItem _selectedScene;
        private bool _isSceneFormVisible;
        private string _formSceneName = string.Empty;
        private string _formSceneCategory = string.Empty;
        private string _formSceneIcon = "Icon.Siren";
        private bool _isStepFormVisible;
        private int? _editStepId;
        private int _formStepNo = 1;
        private string _formContent = string.Empty;
        private string _formRole = string.Empty;
        private string _formTimeLimit = string.Empty;
        private string _formAction = string.Empty;
        private bool _isStatusConfirmVisible;
        private string _statusConfirmText = string.Empty;
        private int _loadSceneId;
        private bool _isBatchConfirmVisible;
        private string _batchConfirmText = string.Empty;
        private bool _isDeleteStepConfirmVisible;
        private string _deleteStepConfirmText = string.Empty;
        private EmergencyStepRow _deleteStepTarget;
        private bool _isBatchMode;

        public EmergencySceneStepViewModel(IApiClient api) : base(api)
        {
            QueryCommand = new AsyncRelayCommand(LoadAsync);
            SelectSceneCommand = new RelayCommand<EmergencySceneItem>(s => SelectedScene = s);
            AddSceneCommand = new RelayCommand(() => { FormSceneName = string.Empty; FormSceneCategory = string.Empty; FormSceneIcon = "Icon.Siren"; IsSceneFormVisible = true; });
            SaveSceneCommand = new AsyncRelayCommand(SaveSceneAsync);
            CancelSceneCommand = new RelayCommand(() => IsSceneFormVisible = false);
            AddStepCommand = new RelayCommand(OpenStepForm);
            EditStepCommand = new RelayCommand<EmergencyStepRow>(EditStep);
            SaveStepCommand = new AsyncRelayCommand(SaveStepAsync);
            CancelStepCommand = new RelayCommand(() => IsStepFormVisible = false);
            DeleteStepCommand = new RelayCommand<EmergencyStepRow>(RequestDeleteStep);
            ConfirmDeleteStepCommand = new AsyncRelayCommand(ConfirmDeleteStepAsync);
            CancelDeleteStepCommand = new RelayCommand(() => IsDeleteStepConfirmVisible = false);
            // 停用/启用场景（BR-EMG-01：停用后不可发起），二次确认浮层
            ToggleSceneStatusCommand = new RelayCommand(RequestToggleSceneStatus);
            ConfirmToggleSceneStatusCommand = new AsyncRelayCommand(ConfirmToggleSceneStatusAsync);
            CancelToggleSceneStatusCommand = new RelayCommand(() => IsStatusConfirmVisible = false);
            // 联动动作执行（降级口径：仅提示留痕，不发起真实拨打/广播/推送）
            RunActionCommand = new RelayCommand<EmergencyStepRow>(RunAction);
            BatchDeleteCommand = new RelayCommand(EnterBatchMode);
            ConfirmBatchDeleteCommand = new AsyncRelayCommand(ConfirmBatchDeleteAsync);
            ConfirmSelectionCommand = new RelayCommand(RequestBatchConfirm);
            CancelBatchDeleteCommand = new RelayCommand(ExitBatchMode);
            Scenes.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasScenesChecked));
                OnPropertyChanged(nameof(IsAllChecked));
                OnPropertyChanged(nameof(CheckedCountText));
            };
            _ = LoadAsync();
        }

        public ObservableCollection<EmergencySceneItem> Scenes { get; } = new ObservableCollection<EmergencySceneItem>();
        public ObservableCollection<EmergencyStepRow> Steps { get; } = new ObservableCollection<EmergencyStepRow>();
        /// <summary>场景图标库：供「新增场景」窗体选择，每个场景可用不同图标标识。</summary>
        public ObservableCollection<string> IconItems { get; } = new ObservableCollection<string>
        {
            "Icon.Flame", "Icon.Droplet", "Icon.Zap", "Icon.Siren", "Icon.Bell",
            "Icon.ClockAlert", "Icon.ShieldCheck", "Icon.Target", "Icon.HardHat",
            "Icon.UserRound", "Icon.PhoneCall", "Icon.Building", "Icon.Search"
        };

        /// <summary>P0 修复：切换场景必须刷新步骤表（setter 触发加载，替代仅初始调用一次的旧实现）。</summary>
        public EmergencySceneItem SelectedScene
        {
            get { return _selectedScene; }
            set
            {
                if (SetProperty(ref _selectedScene, value) && value != null)
                {
                    _ = SelectSceneAsync(value);
                }
            }
        }

        /// <summary>右列标题「{场景} · 处置步骤」（原型标题行）。</summary>
        public string RightTitle { get { return SelectedScene == null ? "处置步骤" : SelectedScene.Name + " · 处置步骤"; } }

        /// <summary>停用/启用按钮文案（按当前场景状态切换）。</summary>
        public string ToggleStatusText { get { return SelectedScene != null && SelectedScene.Status == 1 ? "启用场景" : "停用场景"; } }

        /// <summary>联动动作执行降级提示（审计 P1 降级口径：不接外呼/广播，仅页面留痕提示）。</summary>
        public string ActionHint { get { return "联动动作执行为提示留痕（拨打 / 广播 / 推送需系统话机与广播通道接入后生效）"; } }

        public bool IsSceneFormVisible { get { return _isSceneFormVisible; } set { SetProperty(ref _isSceneFormVisible, value); } }
        public string FormSceneName { get { return _formSceneName; } set { SetProperty(ref _formSceneName, value); } }
        public string FormSceneCategory { get { return _formSceneCategory; } set { SetProperty(ref _formSceneCategory, value); } }
        public string FormSceneIcon { get { return _formSceneIcon; } set { SetProperty(ref _formSceneIcon, value); } }
        public bool IsStepFormVisible { get { return _isStepFormVisible; } set { SetProperty(ref _isStepFormVisible, value); } }
        public int FormStepNo { get { return _formStepNo; } set { SetProperty(ref _formStepNo, value); } }
        public string FormContent { get { return _formContent; } set { SetProperty(ref _formContent, value); } }
        public string FormRole { get { return _formRole; } set { SetProperty(ref _formRole, value); } }
        public string FormTimeLimit { get { return _formTimeLimit; } set { SetProperty(ref _formTimeLimit, value); } }
        public string FormAction { get { return _formAction; } set { SetProperty(ref _formAction, value); } }
        public bool IsStatusConfirmVisible { get { return _isStatusConfirmVisible; } set { SetProperty(ref _isStatusConfirmVisible, value); } }
        public string StatusConfirmText { get { return _statusConfirmText; } set { SetProperty(ref _statusConfirmText, value); } }
        public bool IsDeleteStepConfirmVisible { get { return _isDeleteStepConfirmVisible; } set { SetProperty(ref _isDeleteStepConfirmVisible, value); } }
        public string DeleteStepConfirmText { get { return _deleteStepConfirmText; } set { SetProperty(ref _deleteStepConfirmText, value); } }
        public bool IsBatchConfirmVisible { get { return _isBatchConfirmVisible; } set { SetProperty(ref _isBatchConfirmVisible, value); } }
        public string BatchConfirmText { get { return _batchConfirmText; } set { SetProperty(ref _batchConfirmText, value); } }
        public bool IsBatchMode { get { return _isBatchMode; } set { SetProperty(ref _isBatchMode, value); OnPropertyChanged(nameof(IsNotBatchMode)); } }
        public bool IsNotBatchMode { get { return !_isBatchMode; } }
        public bool HasScenesChecked { get { return Scenes.Any(x => x.IsChecked); } }
        public bool IsAllChecked
        {
            get { return Scenes.Count > 0 && Scenes.All(x => x.IsChecked); }
            set
            {
                foreach (var sc in Scenes) sc.IsChecked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasScenesChecked));
                OnPropertyChanged(nameof(CheckedCountText));
            }
        }
        public string CheckedCountText { get { return "\u5df2\u9009 " + Scenes.Count(x => x.IsChecked) + " / " + Scenes.Count + " \u9879"; } }

        public IAsyncRelayCommand QueryCommand { get; }
        public IRelayCommand<EmergencySceneItem> SelectSceneCommand { get; }
        public IRelayCommand AddSceneCommand { get; }
        public IAsyncRelayCommand SaveSceneCommand { get; }
        public IRelayCommand CancelSceneCommand { get; }
        public IRelayCommand AddStepCommand { get; }
        public IRelayCommand<EmergencyStepRow> EditStepCommand { get; }
        public IAsyncRelayCommand SaveStepCommand { get; }
        public IRelayCommand CancelStepCommand { get; }
        public IRelayCommand<EmergencyStepRow> DeleteStepCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteStepCommand { get; }
        public IRelayCommand CancelDeleteStepCommand { get; }
        public IRelayCommand ToggleSceneStatusCommand { get; }
        public IAsyncRelayCommand ConfirmToggleSceneStatusCommand { get; }
        public IRelayCommand CancelToggleSceneStatusCommand { get; }
        public IRelayCommand<EmergencyStepRow> RunActionCommand { get; }
        public IRelayCommand BatchDeleteCommand { get; }
        public IRelayCommand ConfirmSelectionCommand { get; }
        public IAsyncRelayCommand ConfirmBatchDeleteCommand { get; }
        public IRelayCommand CancelBatchDeleteCommand { get; }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var list = await Api.GetEmergencyScenesAsync();
                Scenes.Clear();
                foreach (var s in list)
                {
                    if (string.IsNullOrEmpty(s.TimeLimitText)) s.TimeLimitText = s.StepCount > 0 ? s.StepCount + " 步" : "0 步";
                    Scenes.Add(WrapScene(s));
                }
                var keep = SelectedScene;
                if (keep != null)
                {
                    var stillThere = Scenes.FirstOrDefault(x => x.Id == keep.Id);
                    if (stillThere != null)
                    {
                        SetProperty(ref _selectedScene, stillThere, nameof(SelectedScene));
                        OnPropertyChanged(nameof(SelectedScene));
                        OnPropertyChanged(nameof(RightTitle));
                        OnPropertyChanged(nameof(ToggleStatusText));
                        await LoadStepsCoreAsync(stillThere.Id);
                        return;
                    }
                }
                if (SelectedScene == null && Scenes.Count > 0) SelectedScene = Scenes[0];
            }, "场景已加载");
        }

        private async Task SelectSceneAsync(EmergencySceneItem scene)
        {
            OnPropertyChanged(nameof(RightTitle));
            OnPropertyChanged(nameof(ToggleStatusText));
            await LoadStepsCoreAsync(scene.Id);
        }

        /// <summary>加载指定场景步骤表（_loadSceneId 防止 setter 与命令双重触发重复请求）。</summary>
        private async Task LoadStepsCoreAsync(int sceneId)
        {
            if (_loadSceneId == sceneId && Steps.Count > 0) return;
            _loadSceneId = sceneId;
            await RunAsync(async () =>
            {
                var steps = await Api.GetEmergencyStepsAsync(sceneId);
                Steps.Clear();
                foreach (var s in steps) Steps.Add(new EmergencyStepRow { Dto = s });
            }, "步骤已加载");
        }

        private void OpenStepForm()
        {
            if (SelectedScene == null) { ErrorText = "请先选择场景"; return; }
            FormStepNo = Steps.Count + 1;
            FormContent = string.Empty; FormRole = string.Empty; FormTimeLimit = string.Empty; FormAction = string.Empty;
            _editStepId = null;
            IsStepFormVisible = true;
        }

        private void EditStep(EmergencyStepRow row)
        {
            if (row == null) return;
            _editStepId = row.Dto.Id;
            FormStepNo = row.Dto.StepNo;
            FormContent = row.Dto.Content; FormRole = row.Dto.Role;
            FormTimeLimit = row.Dto.TimeLimit; FormAction = row.Dto.Action;
            IsStepFormVisible = true;
        }

        private async Task SaveSceneAsync()
        {
            if (string.IsNullOrWhiteSpace(FormSceneName)) { ErrorText = "场景名称不能为空"; return; }
            await RunAsync(async () =>
            {
                var created = await Api.CreateEmergencySceneAsync(new EmergencySceneRequest
                {
                    Name = FormSceneName.Trim(),
                    Category = FormSceneCategory,
                    IconKey = string.IsNullOrWhiteSpace(FormSceneIcon) ? "Icon.Siren" : FormSceneIcon
                });
                IsSceneFormVisible = false;
                var list = await Api.GetEmergencyScenesAsync();
                Scenes.Clear();
                foreach (var s in list)
                {
                    if (string.IsNullOrEmpty(s.TimeLimitText)) s.TimeLimitText = s.StepCount > 0 ? s.StepCount + " 步" : "0 步";
                    Scenes.Add(WrapScene(s));
                }
                // P2：新增场景成功后选中新场景
                var target = Scenes.FirstOrDefault(x => created != null && x.Id == created.Id);
                if (target != null) SelectedScene = target;
            }, "场景已保存");
        }

        private async Task SaveStepAsync()
        {
            if (SelectedScene == null) { ErrorText = "请先选择场景"; return; }
            if (string.IsNullOrWhiteSpace(FormContent)) { ErrorText = "步骤内容不能为空"; return; }
            int sceneId = SelectedScene.Id;
            // 审计 1.1 修复配套：保存前校验行归属场景，避免「看着 A 场景步骤写入 B 场景」
            if (_editStepId.HasValue)
            {
                var row = Steps.FirstOrDefault(x => x.Dto.Id == _editStepId.Value);
                if (row != null && row.Dto.SceneId != sceneId) { ErrorText = "该步骤不属于当前场景，无法保存"; return; }
            }
            await RunAsync(async () =>
            {
                var req = new EmergencyStepRequest
                {
                    SceneId = sceneId, StepNo = FormStepNo, Content = FormContent.Trim(),
                    Role = FormRole, TimeLimit = FormTimeLimit, Action = FormAction
                };
                if (_editStepId.HasValue) await Api.UpdateEmergencyStepAsync(_editStepId.Value, req);
                else await Api.CreateEmergencyStepAsync(sceneId, req);
                IsStepFormVisible = false;
                _loadSceneId = 0;
                await LoadStepsCoreAsync(sceneId);
                // 步骤数/时长变化，刷新场景卡「N 步 · M 分钟」
                var list = await Api.GetEmergencyScenesAsync();
                Scenes.Clear();
                foreach (var s in list)
                {
                    if (string.IsNullOrEmpty(s.TimeLimitText)) s.TimeLimitText = s.StepCount > 0 ? s.StepCount + " 步" : "0 步";
                    Scenes.Add(WrapScene(s));
                }
                var keep = Scenes.FirstOrDefault(x => x.Id == sceneId);
                if (keep != null) SelectedScene = keep;
                else if (Scenes.Count > 0) SelectedScene = Scenes[0];
            }, "步骤已保存");
        }

        private void RequestToggleSceneStatus()
        {
            if (SelectedScene == null) { ErrorText = "请先选择场景"; return; }
            StatusConfirmText = SelectedScene.Status == 0
                ? "确认停用场景「" + SelectedScene.Name + "」？停用后场景状态置为停用，处置步骤历史保留。"
                : "确认启用场景「" + SelectedScene.Name + "」？启用后场景恢复正常使用。";
            IsStatusConfirmVisible = true;
        }

        private async Task ConfirmToggleSceneStatusAsync()
        {
            if (SelectedScene == null) return;
            var sceneId = SelectedScene.Id;
            int status = SelectedScene.Status == 0 ? 1 : 0;
            await RunAsync(async () =>
            {
                await Api.SetEmergencySceneStatusAsync(sceneId, new EmergencySceneStatusRequest { Status = status });
                IsStatusConfirmVisible = false;
                var list = await Api.GetEmergencyScenesAsync();
                Scenes.Clear();
                foreach (var s in list)
                {
                    if (string.IsNullOrEmpty(s.TimeLimitText)) s.TimeLimitText = s.StepCount > 0 ? s.StepCount + " 步" : "0 步";
                    Scenes.Add(WrapScene(s));
                }
                var keep = Scenes.FirstOrDefault(x => x.Id == sceneId);
                if (keep != null) SelectedScene = keep;
                else if (Scenes.Count > 0) SelectedScene = Scenes[0];
            }, status == 1 ? "场景已停用" : "场景已启用");
        }

        /// <summary>联动动作点击（降级：StatusText 提示留痕，不接拨打/广播/推送通道）。</summary>
        private void RunAction(EmergencyStepRow row)
        {
            if (row == null || !row.HasAction) return;
            StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已执行联动动作「" + row.Action + "」（留痕提示）";
        }

        /// <summary>拖拽排序：本地重排 + 重编序号 + ReorderEmergencyStepsAsync 持久化（T6-1-2）。</summary>
        private EmergencySceneItem WrapScene(EmergencySceneDto sc)
        {
            if (string.IsNullOrEmpty(sc.TimeLimitText)) sc.TimeLimitText = sc.StepCount > 0 ? sc.StepCount + " \u6b65" : "0 \u6b65";
            var item = new EmergencySceneItem { Dto = sc };
            item.PropertyChanged += SceneItem_PropertyChanged;
            return item;
        }

        private void SceneItem_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EmergencySceneItem.IsChecked))
            {
                OnPropertyChanged(nameof(HasScenesChecked));
                OnPropertyChanged(nameof(IsAllChecked));
                OnPropertyChanged(nameof(CheckedCountText));
            }
        }

        /// <summary>进入批量删除模式：显示勾选框并清除已勾选。</summary>
        private void EnterBatchMode()
        {
            IsBatchMode = true;
            foreach (var sc in Scenes) sc.IsChecked = false;
            ErrorText = string.Empty;
        }

        /// <summary>退出批量删除模式：隐藏勾选框并清除已勾选。</summary>
        private void ExitBatchMode()
        {
            IsBatchMode = false;
            IsBatchConfirmVisible = false;
            foreach (var sc in Scenes) sc.IsChecked = false;
        }

        /// <summary>请求批量删除确认：打开二次确认浮层。</summary>
        private void RequestBatchConfirm()
        {
            int count = Scenes.Count(x => x.IsChecked);
            if (count == 0) { ErrorText = "请先勾选要删除的场景"; return; }
            BatchConfirmText = "确认删除已勾选的 " + count + " 个场景？删除后不可恢复；若场景存在进行中的应急事件将无法删除。";
            IsBatchConfirmVisible = true;
        }

        /// <summary>批量删除：逐个调用现有删除接口（含事件引用校验与软删），汇总成功后刷新场景库。</summary>
        private async Task ConfirmBatchDeleteAsync()
        {
            var ids = Scenes.Where(x => x.IsChecked).Select(x => x.Id).ToList();
            if (ids.Count == 0) { IsBatchConfirmVisible = false; return; }
            int ok = 0;
            var failed = new List<string>();
            foreach (var id in ids)
            {
                var item = Scenes.FirstOrDefault(x => x.Id == id);
                try
                {
                    await Api.DeleteEmergencySceneAsync(id);
                    ok++;
                }
                catch (ApiClientException ex)
                {
                    failed.Add((item == null ? "场景" : item.Name) + "：" + ex.Message);
                }
            }
            IsBatchConfirmVisible = false;
            await LoadAsync();
            if (ok > 0) StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已删除 " + ok + " 个场景" + (failed.Count > 0 ? "，跳过 " + failed.Count + " 个" : "");
            else StatusText = DateTime.Now.ToString("HH:mm:ss ") + "未删除任何场景（被事件引用）";
            if (failed.Count > 0) ErrorText = string.Join("；", failed);
            IsBatchMode = false;
        }

        /// <summary>请求删除处置步骤：打开二次确认浮层。</summary>
        private void RequestDeleteStep(EmergencyStepRow row)
        {
            if (row == null) return;
            _deleteStepTarget = row;
            DeleteStepConfirmText = "确认删除第 " + row.StepNo + " 条处置步骤？删除后该步骤将归档（不再显示，版本历史保留）。";
            IsDeleteStepConfirmVisible = true;
        }

        /// <summary>确认删除处置步骤：调用现有删除接口（软删/归档 status=1），删除后刷新步骤表与场景卡步数。</summary>
        private async Task ConfirmDeleteStepAsync()
        {
            if (_deleteStepTarget == null || SelectedScene == null) { IsDeleteStepConfirmVisible = false; return; }
            int id = _deleteStepTarget.Dto.Id;
            int sceneId = SelectedScene.Id;
            IsDeleteStepConfirmVisible = false;
            await RunAsync(async () =>
            {
                await Api.DeleteEmergencyStepAsync(id);
                _loadSceneId = 0;
                await LoadStepsCoreAsync(sceneId);
                var list = await Api.GetEmergencyScenesAsync();
                Scenes.Clear();
                foreach (var sc in list)
                {
                    if (string.IsNullOrEmpty(sc.TimeLimitText)) sc.TimeLimitText = sc.StepCount > 0 ? sc.StepCount + " \u6b65" : "0 \u6b65";
                    Scenes.Add(WrapScene(sc));
                }
                var keep = Scenes.FirstOrDefault(x => x.Id == sceneId);
                if (keep != null) SelectedScene = keep;
                else if (Scenes.Count > 0) SelectedScene = Scenes[0];
            }, "步骤已删除");
            _deleteStepTarget = null;
        }

        public async Task MoveStepAsync(int fromIndex, int toIndex)
        {
            if (SelectedScene == null) return;
            if (fromIndex < 0 || fromIndex >= Steps.Count) return;
            if (toIndex < 0 || toIndex >= Steps.Count || fromIndex == toIndex) return;
            await RunAsync(async () =>
            {
                var row = Steps[fromIndex];
                Steps.RemoveAt(fromIndex);
                Steps.Insert(toIndex, row);
                var orderedIds = new List<int>();
                for (int i = 0; i < Steps.Count; i++)
                {
                    Steps[i].Dto.StepNo = i + 1;
                    orderedIds.Add(Steps[i].Dto.Id);
                }
                foreach (var r in Steps) r.Refresh();
                await Api.ReorderEmergencyStepsAsync(SelectedScene.Id, orderedIds);
            }, "步骤顺序已保存");
        }
    }
}
