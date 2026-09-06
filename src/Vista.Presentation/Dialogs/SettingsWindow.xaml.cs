using System.Windows;
using System.Windows.Controls;

namespace Vista.Presentation.Dialogs
{
    /// <summary>设置对话框：主题、字体大小、朗读方式。</summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void OnFontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (FontSizePreview != null)
            {
                var size = (int)e.NewValue;
                FontSizePreview.FontSize = size;
                FontSizePreview.Text = $"预览文字：当前字体大小 {size}";
            }
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            // 应用主题
            if (ThemeHighContrast.IsChecked == true)
                ApplyResourceDictionary("Themes/HighContrast.xaml");
            else if (ThemeDark.IsChecked == true)
                ApplyResourceDictionary("Themes/Dark.xaml");
            else
                ApplyResourceDictionary("Themes/Default.xaml");

            // 应用字体大小（全局）
            var size = (int)FontSizeSlider.Value;
            if (Application.Current.Resources.Contains("GlobalFontSize"))
                Application.Current.Resources["GlobalFontSize"] = size;
            else
                Application.Current.Resources.Add("GlobalFontSize", size);

            // 应用朗读设置
            Accessibility.NarrationService.EnableAutoSpeak = AutoNarrate.IsChecked == true;
            Accessibility.NarrationService.EnableManualSpeak = UseSystemSpeech.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static void ApplyResourceDictionary(string source)
        {
            var app = Application.Current;
            var dict = new ResourceDictionary { Source = new System.Uri(source, System.UriKind.Relative) };
            // 替换第一个主题字典
            for (int i = 0; i < app.Resources.MergedDictionaries.Count; i++)
            {
                var src = app.Resources.MergedDictionaries[i].Source?.OriginalString ?? "";
                if (src.Contains("Themes/"))
                {
                    app.Resources.MergedDictionaries[i] = dict;
                    return;
                }
            }
            app.Resources.MergedDictionaries.Insert(0, dict);
        }
    }
}
