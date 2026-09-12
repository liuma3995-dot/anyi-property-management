using System.Windows;

namespace PropertyManagement.Client.Converters
{
    /// <summary>
    /// 绑定代理（Freezable）：供 DataGridColumn 等**不在可视化树内**的对象绑定到视图 DataContext。
    /// WPF 中 DataGridColumn 无法用 RelativeSource 找到宿主元素，经此代理把 DataContext 传递给它。
    /// </summary>
    public class BindingProxy : Freezable
    {
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register("Data", typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));

        public object Data
        {
            get { return GetValue(DataProperty); }
            set { SetValue(DataProperty, value); }
        }

        protected override Freezable CreateInstanceCore()
        {
            return new BindingProxy();
        }
    }
}
