namespace PropertyManagement.Contract.Auth
{
    /// <summary>
    /// 管理员个人信息（R17 新增；t_user 扩展列，全部可填可不填）。
    /// 头像为内置可选项（AvatarKey：avatar-01 ~ avatar-08），不含文件上传。
    /// </summary>
    public class UserProfileDto
    {
        public int Id { get; set; }

        public string UserName { get; set; }

        /// <summary>显示名（空 = 顶栏回退用户名）。</summary>
        public string DisplayName { get; set; }

        /// <summary>手机号码（可空；填了按 BR-TEL-04 口径校验）。</summary>
        public string Phone { get; set; }

        /// <summary>个人简介（≤200 字）。</summary>
        public string Bio { get; set; }

        /// <summary>内置头像 key（avatar-01 ~ avatar-08；空 = 默认）。</summary>
        public string AvatarKey { get; set; }

        public string Role { get; set; }

        /// <summary>可选头像清单（前端渲染用，key + 展示名）。</summary>
        public System.Collections.Generic.List<AvatarOptionDto> AvatarOptions { get; set; }
    }

    /// <summary>内置头像候选项。</summary>
    public class AvatarOptionDto
    {
        public string Key { get; set; }

        public string Label { get; set; }
    }

    /// <summary>个人信息保存请求（三项均可空；传空字符串 = 清空该项）。</summary>
    public class UserProfileRequest
    {
        public string DisplayName { get; set; }

        public string Phone { get; set; }

        public string Bio { get; set; }

        public string AvatarKey { get; set; }
    }
}
