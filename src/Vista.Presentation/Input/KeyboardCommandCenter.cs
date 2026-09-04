using System.Windows;
using System.Windows.Input;
using Vista.Presentation.Accessibility;

namespace Vista.Presentation.Input
{
    /// <summary>
    /// 快捷键中心。设计计划 §4.2 的集中注册点。
    /// 新增快捷键对应 XAML 工具栏按钮：
    ///   F5 = 刷新
    ///   Alt+F = 一键转发/分享
    ///   Alt+L = 加载当前卡片评论
    ///   Alt+Shift+R = 手动朗读卡片
    ///   Alt+Shift+C = 手动朗读评论
    ///   Alt+O = 保存离线缓存
    ///   Esc = 停止任何正在朗读的 SAPI 语音
    ///
    /// 所有快捷键均可在"设置"中重映射（M4 实现设置面板）。
    /// </summary>
    public static class KeyboardCommandCenter
    {
        public static void Register(Window window, MainViewModel vm)
        {
            window.PreviewKeyDown += (s, e) => OnPreviewKeyDown(window, vm, e);
        }

        private static async void OnPreviewKeyDown(Window window, MainViewModel vm, KeyEventArgs e)
        {
            var alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            // --- F5：刷新（工具栏按钮也可用） ---
            if (e.Key == Key.F5)
            {
                await vm.RefreshFeedAsync();
                e.Handled = true; return;
            }

            // --- Alt 组合 ---
            if (alt)
            {
                // Alt + 数字键 1..5：切换功能区
                if (!shift && e.Key >= Key.D1 && e.Key <= Key.D5)
                {
                    int idx = e.Key - Key.D1;
                    var nav = window.FindName("NavList") as System.Windows.Controls.ListBox;
                    if (nav != null && nav.Items.Count > idx)
                    {
                        nav.SelectedIndex = idx;
                        e.Handled = true; return;
                    }
                }

                // Alt + Shift + R：手动朗读当前卡片（争渡模式下用户主动调用，非自动）
                if (shift && e.Key == Key.R)
                {
                    vm.NarrateCurrentCard();
                    e.Handled = true; return;
                }

                // Alt + Shift + C：手动朗读评论
                if (shift && e.Key == Key.C)
                {
                    vm.NarrateComments();
                    e.Handled = true; return;
                }

                // Alt + F：一键转发或分享
                if (!shift && e.Key == Key.F)
                {
                    await vm.RepostOrShareCurrentAsync();
                    e.Handled = true; return;
                }

                // Alt + L：加载评论
                if (!shift && e.Key == Key.L)
                {
                    await vm.LoadCurrentCommentsAsync();
                    e.Handled = true; return;
                }

                // Alt + O：保存离线缓存
                if (!shift && e.Key == Key.O)
                {
                    await vm.SaveFeedOfflineAsync();
                    e.Handled = true; return;
                }
            }

            // --- Ctrl + Shift + A：账号管理器 ---
            if (ctrl && shift && e.Key == Key.A)
            {
                MessageBox.Show("账号管理器（M1 实现）", "Vista");
                e.Handled = true; return;
            }

            // --- Ctrl + Shift + O：刷新 ---
            if (ctrl && shift && e.Key == Key.O)
            {
                await vm.RefreshFeedAsync();
                e.Handled = true; return;
            }

            // --- Esc：停止 SAPI 朗读 ---
            if (e.Key == Key.Escape)
            {
                NarrationService.Stop();
                e.Handled = true; return;
            }

            // --- J / K：信息流上下一张 ---
            if (!alt && !ctrl && !shift && (e.Key == Key.J || e.Key == Key.K))
            {
                var list = window.FindName("CardList") as System.Windows.Controls.ListView;
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
