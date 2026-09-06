using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Vista.Presentation.Accessibility;

namespace Vista.Presentation.Dialogs
{
    /// <summary>
    /// 发布微博对话框。
    /// 窗口内完成整个发布流程：输入 → 选图 → 点击发布 → 显示进度 → 成功关闭/失败留窗口。
    /// </summary>
    public partial class PublishWindow : Window
    {
        private readonly List<string> _imagePaths = new List<string>();
        private readonly MainViewModel _vm;
        private CancellationTokenSource _cts;

        /// <summary>传入 ViewModel 以便在窗口内直接调用发布逻辑。</summary>
        public PublishWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            ContentBox.Focus();
        }

        public string PostText => ContentBox.Text;
        public string[] ImagePaths => _imagePaths.ToArray();
        public bool PublishSucceeded { get; private set; }

        private void OnContentChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePublishButton();
        }

        private void OnAddImage(object sender, RoutedEventArgs e)
        {
            if (_imagePaths.Count >= 9)
            {
                StatusText.Text = "最多只能添加 9 张图片";
                return;
            }
            var dlg = new OpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.gif;*.bmp",
                Multiselect = true,
                Title = "选择要上传的图片"
            };
            if (dlg.ShowDialog() == true)
            {
                foreach (var f in dlg.FileNames)
                {
                    if (_imagePaths.Count >= 9) break;
                    if (!_imagePaths.Contains(f)) _imagePaths.Add(f);
                }
                UpdateImageCount();
                UpdatePublishButton();
            }
        }

        private void OnRemoveImages(object sender, RoutedEventArgs e)
        {
            _imagePaths.Clear();
            UpdateImageCount();
            UpdatePublishButton();
        }

        private void UpdateImageCount()
        {
            ImageCountText.Text = _imagePaths.Count > 0
                ? $"已添加 {_imagePaths.Count} 张图片"
                : "";
            RemoveImageBtn.IsEnabled = _imagePaths.Count > 0;
        }

        private void UpdatePublishButton()
        {
            PublishBtn.IsEnabled = !string.IsNullOrWhiteSpace(ContentBox.Text) || _imagePaths.Count > 0;
        }

        private async void OnPublish(object sender, RoutedEventArgs e)
        {
            // 进入发布中状态：禁用所有操作按钮
            SetPublishing(true);
            StatusText.Text = "正在发布微博...";
            NarrationService.SpeakAuto("正在发布微博");

            _cts = new CancellationTokenSource();
            try
            {
                var ok = await _vm.PublishPostAsync(ContentBox.Text, _imagePaths.ToArray());
                if (ok)
                {
                    PublishSucceeded = true;
                    StatusText.Text = "发布成功";
                    NarrationService.SpeakAuto("发布成功");
                    await Task.Delay(500); // 让用户看到成功提示
                    DialogResult = true;
                }
                else
                {
                    StatusText.Text = "发布失败：" + _vm.Status;
                    NarrationService.SpeakAuto("发布失败：" + _vm.Status);
                    SetPublishing(false);
                }
            }
            catch (TaskCanceledException)
            {
                StatusText.Text = "已取消发布";
                NarrationService.SpeakAuto("已取消发布");
                SetPublishing(false);
            }
            catch (System.Exception ex)
            {
                StatusText.Text = "发布出错：" + ex.Message;
                NarrationService.SpeakAuto("发布出错");
                SetPublishing(false);
            }
        }

        private void SetPublishing(bool publishing)
        {
            ContentBox.IsEnabled = !publishing;
            AddImageBtn.IsEnabled = !publishing;
            RemoveImageBtn.IsEnabled = !publishing && _imagePaths.Count > 0;
            CancelBtn.IsEnabled = !publishing;
            // 发布中按钮显示"发布中..."且禁用
            PublishBtn.Content = publishing ? "发布中..." : "发布";
            PublishBtn.IsEnabled = !publishing;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            DialogResult = false;
            Close();
        }
    }
}
