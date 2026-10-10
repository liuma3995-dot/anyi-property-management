using System;
using System.Collections.Generic;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>
    /// CHG-v1.4.1-14：可被「跳转定位」标记高亮的列表行（欠费台账 / 设备到期提醒 / 纠纷列表）。
    /// 只约束一个属性 —— 由主题的行模板统一渲染（浅黄底），避免各页面自定义 RowStyle
    /// （自定义 RowStyle 会覆盖主题行模板里的 悬停/选中 触发器，导致「点击任意行没有背景反应色」）。
    /// </summary>
    public interface IFocusableRow
    {
        bool IsHighlighted { get; set; }
    }

    /// <summary>
    /// CHG-v1.4.1-14：会发出「请把这一行滚动到可视区」请求的页面 ViewModel
    /// （由 <c>RowFocusAdapter</c> 订阅；VM 不直接持有控件）。
    /// </summary>
    public interface IFocusTargetHost
    {
        event Action<object> FocusRowRequested;
    }

    /// <summary>
    /// 跳转定位（待办 → 目标页面指定行）的公共逻辑：命中 → 高亮；用户改选其它行 → 清掉高亮。
    /// </summary>
    public static class ListRowFocus
    {
        /// <summary>按条件在列表里定位目标行：先清掉旧高亮，再给命中行打标；返回命中行（无则 null）。</summary>
        public static T Apply<T>(IEnumerable<T> items, Func<T, bool> match) where T : class, IFocusableRow
        {
            if (items == null || match == null) { return null; }
            T hit = null;
            foreach (T item in items)
            {
                if (item == null) { continue; }
                if (hit == null && match(item))
                {
                    hit = item;
                    item.IsHighlighted = true;
                }
                else if (item.IsHighlighted)
                {
                    item.IsHighlighted = false;
                }
            }
            return hit;
        }

        /// <summary>
        /// 用户改选了其它行（keep 之外）→ 清掉定位高亮，让行背景回到主题的选中色，
        /// 不再残留「刚跳过来」的浅黄底。
        /// </summary>
        public static void ClearExcept<T>(IEnumerable<T> items, T keep) where T : class, IFocusableRow
        {
            if (items == null) { return; }
            foreach (T item in items)
            {
                if (item == null || ReferenceEquals(item, keep)) { continue; }
                if (item.IsHighlighted) { item.IsHighlighted = false; }
            }
        }
    }
}
