using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Vista.Presentation.Dialogs
{
    /// <summary>发布微博对话框。</summary>
    public partial class PublishWindow : Window
    {
        private readonly List<string> _imagePaths = new List<string>();

        public PublishWindow()
        {
            InitializeComponent();
        }

        public string PostText => ContentBox.Text;
        public string[] ImagePaths => _imagePaths.ToArray();

        private void OnContentChanged(object sender, TextChangedEventArgs e)
        {
            PublishBtn.IsEnabled = !string.IsNullOrWhiteSpace(ContentBox.Text) || _imagePaths.Count > 0;
        }

        private void OnAddImage(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.gif;*.bmp",
                Multiselect = true,
                Title = "选择要上传的图片"
            };
            if (dlg.ShowDialog() == true)
            {
                foreach (var f in dlg.FileNames)
                    if (!_imagePaths.Contains(f)) _imagePaths.Add(f);
                ImageCountText.Text = $"已添加 {_imagePaths.Count} 张图片";
                PublishBtn.IsEnabled = true;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnPublish(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            // 不在这里 Close，由调用方根据发布结果决定
        }
    }
}
