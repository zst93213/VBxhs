using System.Windows;
using System.Windows.Input;
using Vista.Presentation.Accessibility;

namespace Vista.Presentation.Input
{
    public static class KeyboardCommandCenter
    {
        public static void Register(Window window, MainViewModel vm)
        {
            window.PreviewKeyDown += (s, e) => OnPreviewKeyDown(window, vm, e);
        }

        private static async void OnPreviewKeyDown(Window window, MainViewModel vm, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                if (e.Key >= Key.D1 && e.Key <= Key.D5)
                {
                    int idx = e.Key - Key.D1;
                    var nav = window.FindName("NavList") as System.Windows.Controls.ListBox;
                    if (nav != null && nav.Items.Count > idx)
                    {
                        nav.SelectedIndex = idx;
                        e.Handled = true;
                        return;
                    }
                }

                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift && e.Key == Key.R)
                {
                    vm.NarrateCurrentCard();
                    e.Handled = true;
                    return;
                }

                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift && e.Key == Key.C)
                {
                    vm.NarrateComments();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.R)
                {
                    await vm.RepostCurrentCardAsync();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.O)
                {
                    await vm.SaveFeedOfflineAsync();
                    e.Handled = true;
                    return;
                }
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift
                && e.Key == Key.A)
            {
                MessageBox.Show("账号管理器（M1 实现）", "Vista");
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift
                && e.Key == Key.O)
            {
                await vm.RefreshFeedAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                NarrationService.Stop();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.J || e.Key == Key.K)
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
