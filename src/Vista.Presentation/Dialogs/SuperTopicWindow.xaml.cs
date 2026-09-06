using System.Windows;
using System.Windows.Controls;

namespace Vista.Presentation.Dialogs
{
    /// <summary>超话浏览与签到对话框。</summary>
    public partial class SuperTopicWindow : Window
    {
        private readonly MainViewModel _vm;

        public SuperTopicWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            PostList.ItemsSource = _vm.Cards;
        }

        private async void OnBrowse(object sender, RoutedEventArgs e)
        {
            var id = TopicIdBox.Text?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                ResultText.Text = "请输入超话 ID";
                return;
            }
            ResultText.Text = "正在加载超话...";
            await _vm.LoadSuperTopicFeedAsync(id);
            ResultText.Text = _vm.Status;
        }

        private async void OnSignIn(object sender, RoutedEventArgs e)
        {
            var id = TopicIdBox.Text?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                ResultText.Text = "请输入超话 ID";
                return;
            }
            ResultText.Text = "正在签到...";
            await _vm.SignInSuperTopicAsync(id);
            ResultText.Text = _vm.Status;
        }
    }
}
