using System.Windows.Automation.Peers;
using System.Windows.Automation;
using Vista.Core.Adapters.Models;

namespace Vista.Presentation.Accessibility
{
    /// <summary>
    /// 卡片自动化 Peer。设计计划 §4.1：用 ListItem 暴露结构化字段，
    /// 让读屏按"作者/时间/正文/互动"分段朗读，而不是整坨灌进用户耳朵。
    ///
    /// 重写要点（MS 官方模式）：
    ///  - GetAutomationControlTypeCore：ListItem（语义=列表项）
    ///  - GetNameCore：返回 PostCard.SpokenLabel（聚合朗读文本）
    ///  - GetHelpTextCore：操作提示
    ///  - 支持 InvokePattern：Enter 打开
    /// </summary>
    public sealed class CardAutomationPeer : FrameworkElementAutomationPeer, IInvokeProvider
    {
        private readonly CardItem _owner;

        public CardAutomationPeer(CardItem owner) : base(owner) => _owner = owner;

        protected override AutomationControlType GetAutomationControlTypeCore()
            => AutomationControlType.ListItem;

        protected override string GetNameCore()
        {
            var post = _owner.Post;
            if (post == null) return "空卡片";
            return post.SpokenLabel; // 作者 于 时间 发布：摘要，点赞 X，评论 Y
        }

        protected override string GetHelpTextCore()
            => "Enter 打开，L 点赞，C 评论，Alt Shift R 朗读全文";

        // --- IInvokeProvider：让读屏/自动化工具能"执行"卡片（回车打开） ---
        public void Invoke()
        {
            // 双击行为等价于 Enter 打开详情
            _owner.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Control.MouseDoubleClickEvent));
        }

        public override object GetPattern(PatternInterface patternInterface)
        {
            if (patternInterface == PatternInterface.Invoke) return this;
            return base.GetPattern(patternInterface);
        }
    }
}
