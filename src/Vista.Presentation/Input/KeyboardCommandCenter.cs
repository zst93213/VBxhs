using System.Windows;
using System.Windows.Input;
using Vista.Presentation.Accessibility;

namespace Vista.Presentation.Input
{
    /// <summary>
    /// 快捷键中心。集中注册全局键盘快捷键。
    ///   F5 = 刷新信息流
    ///   Alt+1..5 = 切换导航
    ///   Alt+F = 一键转发
    ///   Alt+L = 加载当前卡片评论
    ///   Alt+Shift+R = 手动朗读卡片
    ///   Alt+Shift+C = 手动朗读评论
    ///   Alt+O = 保存离线缓存
    ///   J / K = 信息流上下一张
    ///   Esc = 停止朗读
    /// </summary>
    public sealed class KeyboardCommandCenter
    {
        private readonly Window _window;
        private readonly MainViewModel _vm;

        public KeyboardCommandCenter(Window window, MainViewModel vm)
        {
            _window = window;
            _vm = vm;
        }

        public void Attach()
        {
            _window.PreviewKeyDown += OnPreviewKeyDown;
        }

        public void Detach()
        {
            _window.PreviewKeyDown -= OnPreviewKeyDown;
        }

        private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            if (e.Key == Key.F5)
            {
                await _vm.RefreshFeedAsync();
                e.Handled = true; return;
            }

            if (alt)
            {
                if (!shift && e.Key >= Key.D1 && e.Key <= Key.D6)
                {
                    int idx = e.Key - Key.D1;
                    var nav = _window.FindName("NavList") as System.Windows.Controls.ListBox;
                    if (nav != null && nav.Items.Count > idx)
                    {
                        nav.SelectedIndex = idx;
                        e.Handled = true; return;
                    }
                }

                if (shift && e.Key == Key.R) { _vm.NarrateCurrentCard(); e.Handled = true; return; }
                if (shift && e.Key == Key.C) { _vm.NarrateComments(); e.Handled = true; return; }
                if (!shift && e.Key == Key.F) { await _vm.RepostCurrentAsync(); e.Handled = true; return; }
                if (!shift && e.Key == Key.L) { await _vm.LoadCurrentCommentsAsync(); e.Handled = true; return; }
                if (!shift && e.Key == Key.O) { await _vm.SaveFeedOfflineAsync(); e.Handled = true; return; }
            }

            if (e.Key == Key.Escape)
            {
                NarrationService.Stop();
                e.Handled = true; return;
            }

            if (!alt && !ctrl && !shift && (e.Key == Key.J || e.Key == Key.K))
            {
                var list = _window.FindName("CardList") as System.Windows.Controls.ListView;
                if (list != null && list.Items.Count > 0)
                {
                    int next = list.SelectedIndex + (e.Key == Key.J ? 1 : -1);
                    if (next >= 0 && next < list.Items.Count)
                    {
                        list.SelectedIndex = next;
                        var item = list.ItemContainerGenerator.ContainerFromIndex(next) as FrameworkElement;
                        item?.Focus();
                        e.Handled = true;
                    }
                }
            }
        }
    }
}
