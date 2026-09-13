using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Auth;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>
    /// 内置头像调色板（R17）：key → 展示名 / 首字 / 主题色。
    /// 服务端返回候选项，客户端负责视觉呈现（顶栏头像与设置页保持一致）。
    /// </summary>
    public static class AvatarPalette
    {
        private static readonly string[] Keys =
        {
            "avatar-01", "avatar-02", "avatar-03", "avatar-04", "avatar-05", "avatar-06", "avatar-07", "avatar-08"
        };

        private static readonly string[] Labels = { "管理员", "客服", "工程", "安保", "财务", "保洁", "秩序", "访客" };

        private static readonly string[] Colors =
        {
            "#2B7DE9", "#12805C", "#B76E00", "#D64545", "#7A5AF8", "#0E7490", "#BE185D", "#475569"
        };

        public static bool IsKnown(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && Keys.Contains(key.Trim());
        }

        public static string LabelOf(string key)
        {
            int index = IndexOf(key);
            return index < 0 ? "管理员" : Labels[index];
        }

        public static string GlyphOf(string key)
        {
            return LabelOf(key).Substring(0, 1);
        }

        public static string ColorOf(string key)
        {
            int index = IndexOf(key);
            return index < 0 ? Colors[0] : Colors[index];
        }

        public static Brush BrushOf(string key)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorOf(key)));
        }

        private static int IndexOf(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return -1;
            }
            for (int i = 0; i < Keys.Length; i++)
            {
                if (Keys[i] == key.Trim())
                {
                    return i;
                }
            }
            return -1;
        }
    }

    /// <summary>
    /// 头像候选项（客户端展示模型：Key + 名称 + 首字 + 主题色，避免 XAML 侧再挂转换器）。
    /// </summary>
    public class AvatarChoice
    {
        public string Key { get; set; }

        public string Label { get; set; }

        public string Glyph { get; set; }

        public Brush Brush { get; set; }
    }

    /// <summary>
    /// 个人信息设置（R17，顶栏管理员下拉 → 个人信息设置）：
    /// 名字 / 手机号 / 个人简介 / 内置头像四类，均为**可填可不填**；保存后即时刷新顶栏。
    /// </summary>
    public class ProfileViewModel : ObservableObject
    {
        private readonly IApiClient _api;

        private string _displayName = string.Empty;
        private string _phone = string.Empty;
        private string _bio = string.Empty;
        private AvatarChoice _selectedAvatar;
        private string _errorMessage = string.Empty;
        private bool _isBusy;

        public ProfileViewModel(IApiClient api)
        {
            _api = api;
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            _ = LoadAsync();
        }

        public string DisplayName
        {
            get { return _displayName; }
            set { if (SetProperty(ref _displayName, value)) { OnPropertyChanged(nameof(DisplayNameHint)); } }
        }

        public string Phone
        {
            get { return _phone; }
            set { SetProperty(ref _phone, value); }
        }

        public string Bio
        {
            get { return _bio; }
            set { if (SetProperty(ref _bio, value)) { OnPropertyChanged(nameof(BioHint)); } }
        }

        public AvatarChoice SelectedAvatar
        {
            get { return _selectedAvatar; }
            set
            {
                if (SetProperty(ref _selectedAvatar, value))
                {
                    OnPropertyChanged(nameof(AvatarGlyph));
                    OnPropertyChanged(nameof(AvatarBrush));
                }
            }
        }

        public ObservableCollection<AvatarChoice> AvatarOptions { get; } = new ObservableCollection<AvatarChoice>();

        public string AvatarGlyph
        {
            get { return SelectedAvatar == null ? "管" : SelectedAvatar.Glyph; }
        }

        public Brush AvatarBrush
        {
            get { return SelectedAvatar == null ? AvatarPalette.BrushOf(null) : SelectedAvatar.Brush; }
        }

        public string DisplayNameHint
        {
            get { return (DisplayName ?? string.Empty).Trim().Length + " / 20 字（可不填）"; }
        }

        public string BioHint
        {
            get { return (Bio ?? string.Empty).Trim().Length + " / 200 字（可不填）"; }
        }

        public string ErrorMessage
        {
            get { return _errorMessage; }
            private set { SetProperty(ref _errorMessage, value); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set { SetProperty(ref _isBusy, value); }
        }

        public IAsyncRelayCommand SaveCommand { get; }

        /// <summary>保存成功（窗口据此关闭，并把最新资料回填顶栏）。</summary>
        public event Action<UserProfileDto> Saved;

        private async Task LoadAsync()
        {
            IsBusy = true;
            try
            {
                UserProfileDto profile = await _api.GetProfileAsync();
                DisplayName = profile.DisplayName ?? string.Empty;
                Phone = profile.Phone ?? string.Empty;
                Bio = profile.Bio ?? string.Empty;

                AvatarOptions.Clear();
                if (profile.AvatarOptions != null)
                {
                    foreach (AvatarOptionDto option in profile.AvatarOptions)
                    {
                        AvatarOptions.Add(ToChoice(option.Key, option.Label));
                    }
                }
                if (AvatarOptions.Count == 0)
                {
                    for (int i = 1; i <= 8; i++)
                    {
                        string key = "avatar-0" + i;
                        AvatarOptions.Add(ToChoice(key, AvatarPalette.LabelOf(key)));
                    }
                }

                SelectedAvatar = AvatarOptions.FirstOrDefault(x => x.Key == profile.AvatarKey)
                                 ?? AvatarOptions.FirstOrDefault(x => x.Key == "avatar-01")
                                 ?? AvatarOptions.FirstOrDefault();
                ErrorMessage = string.Empty;
            }
            catch (ApiClientException ex)
            {
                ErrorMessage = ex.Message;
            }
            catch (Exception)
            {
                ErrorMessage = "个人信息加载失败，请确认本地服务已启动";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SaveAsync()
        {
            ErrorMessage = string.Empty;

            string displayName = (DisplayName ?? string.Empty).Trim();
            string phone = (Phone ?? string.Empty).Trim();
            string bio = (Bio ?? string.Empty).Trim();

            // 前端轻校验（与后端同口径，避免无谓往返；三项均可为空）
            if (displayName.Length > 20)
            {
                ErrorMessage = "名字不能超过 20 个字";
                return;
            }
            if (bio.Length > 200)
            {
                ErrorMessage = "个人简介不能超过 200 字";
                return;
            }
            if (phone.Length > 0 && !IsPhoneFormatValid(phone))
            {
                ErrorMessage = "手机号码格式不正确（支持 11 位手机号或带区号座机）";
                return;
            }

            IsBusy = true;
            try
            {
                UserProfileDto saved = await _api.UpdateProfileAsync(new UserProfileRequest
                {
                    DisplayName = displayName,
                    Phone = phone,
                    Bio = bio,
                    AvatarKey = SelectedAvatar == null ? null : SelectedAvatar.Key
                });

                Saved?.Invoke(saved);
            }
            catch (ApiClientException ex)
            {
                ErrorMessage = ex.Message;
            }
            catch (Exception)
            {
                ErrorMessage = "保存失败，请确认本地服务已启动";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static bool IsPhoneFormatValid(string phone)
        {
            var digits = new string(phone.Where(char.IsDigit).ToArray());
            if (digits.Length == 0)
            {
                return false;
            }
            if (digits.Length == 11 && digits[0] == '1')
            {
                return true;
            }
            // 座机：可含区号（0xx / 0xxx），主体 7~8 位
            if (digits.Length == 7 || digits.Length == 8)
            {
                return true;
            }
            if ((digits.Length == 10 || digits.Length == 11) && digits[0] == '0')
            {
                return true;
            }
            return false;
        }

        private static AvatarChoice ToChoice(string key, string label)
        {
            string effectiveKey = AvatarPalette.IsKnown(key) ? key : "avatar-01";
            return new AvatarChoice
            {
                Key = effectiveKey,
                Label = string.IsNullOrWhiteSpace(label) ? AvatarPalette.LabelOf(effectiveKey) : label,
                Glyph = AvatarPalette.GlyphOf(effectiveKey),
                Brush = AvatarPalette.BrushOf(effectiveKey)
            };
        }
    }
}
