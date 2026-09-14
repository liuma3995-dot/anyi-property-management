using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Auth;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>修改密码页（PG-COM-04，UC-COM-002）。
    /// 密码框值由视图代码后置经 hc:PasswordBox 的 PasswordChanged 事件回填（Password 非 DP，不可直接 Binding）；
    /// 成功后弹窗确认并触发 Succeeded → 视图清会话强制退出至登录页（同 ChangePasswordWindow 链路）。</summary>
    public class ChangePasswordPageViewModel : BaseInfoPageViewModel
    {
        /// <summary>字母（大小写均可，简化规则不再区分大写）。</summary>
        private static readonly Regex LetterRegex = new Regex("[A-Za-z]", RegexOptions.Compiled);
        private static readonly Regex DigitRegex = new Regex("[0-9]", RegexOptions.Compiled);

        private string _oldPassword = string.Empty;
        private string _newPassword = string.Empty;
        private string _confirmPassword = string.Empty;
        private string _strengthText = "强度：—";
        private Brush _strengthBrush1 = BrushesHelper.Track;
        private Brush _strengthBrush2 = BrushesHelper.Track;
        private Brush _strengthBrush3 = BrushesHelper.Track;

        /// <param name="canCancel">首次登录强制改密时为 false（UC-COM-002：禁点取消；R16 已下线 90 天强制更换）。</param>
        public ChangePasswordPageViewModel(IApiClient api, bool canCancel = true) : base(api)
        {
            SubmitCommand = new AsyncRelayCommand(SubmitAsync);
            CancelCommand = new RelayCommand(Cancel);
            CurrentAccountText = BuildAccountText();
            CanCancel = canCancel;
        }

        /// <summary>改密成功后由视图强制退出（清会话 + 切登录窗），见 ChangePasswordPageView.xaml.cs。</summary>
        public event Action Succeeded;

        /// <summary>是否允许取消：首登强制改密流程禁用「取消」（原型 PG-COM-04 交互标注 / UC-COM-002）。</summary>
        public bool CanCancel { get; private set; }

        /// <summary>强制改密提示文案（仅禁用取消时展示）。</summary>
        public string ForcedHintText
        {
            get
            {
                return CanCancel ? string.Empty : "首次登录须先修改密码，修改后将重新登录。";
            }
        }
        /// <summary>当前账号（会话用户名，不硬编码；SessionInfo 暂无角色字段，角色文案随系统唯一管理员角色展示）。</summary>
        public string CurrentAccountText { get; }

        public string OldPassword
        {
            get { return _oldPassword; }
            set { if (SetProperty(ref _oldPassword, value)) { UpdateStrength(); } }
        }

        public string NewPassword
        {
            get { return _newPassword; }
            set { if (SetProperty(ref _newPassword, value)) { UpdateStrength(); } }
        }

        public string ConfirmPassword
        {
            get { return _confirmPassword; }
            set { SetProperty(ref _confirmPassword, value); }
        }

        public string StrengthText
        {
            get { return _strengthText; }
            private set { SetProperty(ref _strengthText, value); }
        }

        public Brush StrengthBrush1
        {
            get { return _strengthBrush1; }
            private set { SetProperty(ref _strengthBrush1, value); }
        }

        public Brush StrengthBrush2
        {
            get { return _strengthBrush2; }
            private set { SetProperty(ref _strengthBrush2, value); }
        }

        public Brush StrengthBrush3
        {
            get { return _strengthBrush3; }
            private set { SetProperty(ref _strengthBrush3, value); }
        }

        public IAsyncRelayCommand SubmitCommand { get; }
        public IRelayCommand CancelCommand { get; }

        private async Task SubmitAsync()
        {
            // 客户端先校验一致性与规则（6-20 位 + 必须含字母与数字；不要求大写/特殊字符），服务端规则错误经 ErrorText 展示（422 文案透传）
            if (string.IsNullOrWhiteSpace(OldPassword)) { ErrorText = "请输入原密码"; return; }
            if (string.IsNullOrEmpty(NewPassword)) { ErrorText = "请输入新密码"; return; }
            if (NewPassword.Length < 6 || NewPassword.Length > 20) { ErrorText = "新密码长度需为 6-20 位"; return; }
            if (!LetterRegex.IsMatch(NewPassword) || !DigitRegex.IsMatch(NewPassword))
            {
                ErrorText = "新密码必须同时包含字母和数字";
                return;
            }
            if (string.Equals(NewPassword, OldPassword, StringComparison.Ordinal)) { ErrorText = "新密码不能与原密码相同"; return; }
            if (NewPassword != ConfirmPassword) { ErrorText = "两次输入的新密码不一致"; return; }

            await RunAsync(async () =>
            {
                await Api.ChangePasswordAsync(new ChangePasswordRequest { OldPassword = OldPassword, NewPassword = NewPassword });
                MessageBox.Show("密码已更新，请重新登录。", "修改成功", MessageBoxButton.OK, MessageBoxImage.Information);
                ClearInputs();
                var handler = Succeeded;
                if (handler != null) { handler(); }
            }, "密码已更新，请重新登录");
        }

        /// <summary>取消：清空输入并复位强度条。</summary>
        private void Cancel()
        {
            // 强制改密流程（首登/管理员标记）禁用取消
            if (!CanCancel) { return; }
            ClearInputs();
            ErrorText = string.Empty;
        }

        private void ClearInputs()
        {
            // 赋空字符串触发 PasswordChanged 事件由视图回填，保证密码框显示同步清空
            OldPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
        }

        /// <summary>强度（简化规则）：三条件（长度≥6 / 含字母 / 含数字）→ ≤1 弱、2 中、3 强。</summary>
        private void UpdateStrength()
        {
            if (string.IsNullOrEmpty(NewPassword))
            {
                StrengthText = "强度：—";
                StrengthBrush1 = BrushesHelper.Track;
                StrengthBrush2 = BrushesHelper.Track;
                StrengthBrush3 = BrushesHelper.Track;
                return;
            }

            int score = 0;
            if (NewPassword.Length >= 6) { score++; }
            if (LetterRegex.IsMatch(NewPassword)) { score++; }
            if (DigitRegex.IsMatch(NewPassword)) { score++; }

            if (score >= 3)
            {
                StrengthText = "强度：强";
                StrengthBrush1 = BrushesHelper.Success;
                StrengthBrush2 = BrushesHelper.Success;
                StrengthBrush3 = BrushesHelper.Success;
            }
            else if (score >= 2)
            {
                StrengthText = "强度：中";
                StrengthBrush1 = BrushesHelper.Warning;
                StrengthBrush2 = BrushesHelper.Warning;
                StrengthBrush3 = BrushesHelper.Track;
            }
            else
            {
                StrengthText = "强度：弱";
                StrengthBrush1 = BrushesHelper.Danger;
                StrengthBrush2 = BrushesHelper.Track;
                StrengthBrush3 = BrushesHelper.Track;
            }
        }

        /// <summary>当前账号文案：会话用户名（LoginResult.DisplayName 即 Username）+ 唯一系统管理员角色标签。</summary>
        private static string BuildAccountText()
        {
            string name = SessionManager.Instance.Current != null ? SessionManager.Instance.Current.DisplayName : null;
            if (string.IsNullOrEmpty(name))
            {
                name = "未登录";
            }
            return "当前账号：" + name + "（系统管理员）";
        }

        /// <summary>强度条颜色取值（对齐 Theme.xaml 设计令牌）。</summary>
        private static class BrushesHelper
        {
            internal static readonly Brush Track = FromHex("#EAE5DA");
            internal static readonly Brush Success = FromHex("#12805C");
            internal static readonly Brush Warning = FromHex("#B76E00");
            internal static readonly Brush Danger = FromHex("#D64545");

            private static Brush FromHex(string hex)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                return brush;
            }
        }
    }
}
