using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation;
using System.Windows.Controls;
using Vista.Core.Adapters.Models;

namespace Vista.Presentation.Accessibility
{
    /// <summary>
    /// 评论项控件。用于右栏评论区。
    /// 重写 OnCreateAutomationPeer，提供 CommentAutomationPeer。
    /// </summary>
    public class CommentItem : ContentControl
    {
        public static readonly DependencyProperty CommentProperty =
            DependencyProperty.Register(nameof(Comment), typeof(Comment), typeof(CommentItem),
                new PropertyMetadata(null, OnCommentChanged));

        public Comment Comment
        {
            get => (Comment)GetValue(CommentProperty);
            set => SetValue(CommentProperty, value);
        }

        private static void OnCommentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var peer = UIElementAutomationPeer.FromElement((CommentItem)d) as CommentAutomationPeer;
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        protected override AutomationPeer OnCreateAutomationPeer()
            => new CommentAutomationPeer(this);
    }

    /// <summary>
    /// 评论 Automation Peer。争渡 Tab 到评论时按"X楼 + 作者 + 时间 + 内容 + 点赞"分段朗读。
    /// 这是"争渡兼容"的核心：不靠 SAPI 自动说话，而是通过正确的 Name 属性让争渡自己读。
    /// </summary>
    public sealed class CommentAutomationPeer : FrameworkElementAutomationPeer
    {
        private readonly CommentItem _owner;

        public CommentAutomationPeer(CommentItem owner) : base(owner) => _owner = owner;

        protected override AutomationControlType GetAutomationControlTypeCore()
            => AutomationControlType.ListItem;

        protected override string GetNameCore()
        {
            var c = _owner.Comment;
            if (c == null) return "空评论";
            return c.SpokenLabel(_floorIndex);
        }

        protected override string GetHelpTextCore()
            => "Enter 回复该评论，S 点赞该评论";

        // 支持"楼号"：父容器 CommentList 渲染时把 floor 写进 Peer.Tag
        private int _floorIndex;

        internal void SetFloorIndex(int index) => _floorIndex = index;
    }
}
