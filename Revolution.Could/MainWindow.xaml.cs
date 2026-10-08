using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Revolution.Could.Publishing;

namespace Revolution.Could;

/// <summary>界面只负责引导、预览和确认；目录协议与上传安全检查集中在 Publishing 层。</summary>
public partial class MainWindow : Window
{
    private ReleasePlan? _plan;
    private CancellationTokenSource? _cancellation;
    private bool _busy;

    public MainWindow() => InitializeComponent();

    // 每张卡片依次淡入并微微上移：只在窗口第一次载入播放，避免滚动时重复扰动。
    // 尊重 Windows 的“关闭动画效果”设置，减少不适并保留可访问性。
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        FrameworkElement[] sections = [IntroCard, SourceCard, CloudCard, PublishCard];
        for (int i = 0; i < sections.Length; i++)
        {
            var section = sections[i];
            section.Opacity = 0;
            section.RenderTransform = new TranslateTransform(0, 16);
            var delay = TimeSpan.FromMilliseconds(i * 85);
            section.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360))
            {
                BeginTime = delay,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            ((TranslateTransform)section.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(360))
                {
                    BeginTime = delay,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }
    }

    // 动画都只修改界面属性，不参与上传逻辑；上传失败/取消时也能立即恢复可交互状态。
    private void FlashPreview()
    {
        if (SystemParameters.ClientAreaAnimation)
            PreviewText.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(250)));
    }

    private void SetBadge(System.Windows.Controls.Border badge, Color color)
    {
        var brush = new SolidColorBrush(((SolidColorBrush)badge.Background).Color);
        badge.Background = brush;
        if (SystemParameters.ClientAreaAnimation)
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(230)));
        else brush.Color = color;
    }

    private void SetStatus(string message, Color color, bool breathing = false)
    {
        StatusText.Text = message;
        StatusDot.Fill = new SolidColorBrush(color);
        StatusDot.BeginAnimation(OpacityProperty, null);
        StatusDot.Opacity = 1;
        if (breathing && SystemParameters.ClientAreaAnimation)
            StatusDot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.28, TimeSpan.FromMilliseconds(650))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever
            });
    }

    private void SetProgress(double percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        double previous = PublishProgress.Value;
        // 先设真实值，再让显示值从旧值补间；逻辑进度不会因为动画延迟而回退。
        PublishProgress.Value = percent;
        PercentText.Text = $"{percent:0}%";
        if (SystemParameters.ClientAreaAnimation)
            PublishProgress.BeginAnimation(RangeBase.ValueProperty,
                new DoubleAnimation(previous, percent, TimeSpan.FromMilliseconds(280))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }

    private void LockInputs(bool locked)
    {
        DirectoryBox.IsEnabled = !locked;
        BrowseButton.IsEnabled = !locked;
        PreviewButton.IsEnabled = !locked;
        BucketBox.IsEnabled = !locked;
        RegionBox.IsEnabled = !locked;
        SecretIdBox.IsEnabled = !locked;
        SecretKeyBox.IsEnabled = !locked;
    }

    private void InputsChanged(object sender, TextChangedEventArgs e) => InvalidatePlan();
    private void CredentialsChanged(object sender, RoutedEventArgs e) => UpdateButtons();

    private void InvalidatePlan()
    {
        if (PreviewText == null || TargetText == null) return; // XAML 正在初始化
        _plan = null;
        PreviewText.Text = "目录或配置已变更，请重新校验产物。";
        TargetText.Text = "请先校验产物目录";
        SetBadge(SourceBadge, Color.FromRgb(235, 240, 255));
        SetBadge(PublishBadge, Color.FromRgb(235, 240, 255));
        FlashPreview();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (UploadButton == null || SecretKeyBox == null) return;
        UploadButton.IsEnabled = !_busy && _plan != null && !string.IsNullOrWhiteSpace(BucketBox.Text)
            && !string.IsNullOrWhiteSpace(RegionBox.Text) && !string.IsNullOrWhiteSpace(SecretIdBox.Text)
            && SecretKeyBox.SecurePassword.Length > 0;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择 Unity 的 AB 产物目录（包含 RevHotManifest.txt）" };
        if (dialog.ShowDialog(this) == true) DirectoryBox.Text = dialog.FolderName;
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        LockInputs(true);
        UpdateButtons();
        SetStatus("正在计算本地文件 SHA-256…", Colors.RoyalBlue, true);
        try
        {
            string directory = DirectoryBox.Text.Trim();
            var plan = await Task.Run(() => ReleasePlanBuilder.BuildAsync(directory, CancellationToken.None));
            _plan = plan;
            string url = $"https://{BucketBox.Text.Trim()}.cos.{RegionBox.Text.Trim()}.myqcloud.com/{plan.ManifestKey}";
            PreviewText.Text = $"平台：{plan.Platform}     大版本：{plan.AppVersion}     资源版本：{plan.ResVersion}\n"
                + $"文件：{plan.Contents.Count - 1} 个 AB + ResMap.txt     总量：{plan.TotalBytes / 1048576d:F2} MiB\n"
                + $"清单会在最后上传：{plan.ManifestKey}\n目标清单 URL：{url}";
            TargetText.Text = $"上传到 {BucketBox.Text.Trim()}  ·  {plan.Platform}/{plan.AppVersion}/{plan.ResVersion}";
            SetBadge(SourceBadge, Color.FromRgb(220, 246, 231));
            SetBadge(PublishBadge, Color.FromRgb(220, 246, 231));
            SetStatus("本地校验通过。请检查目标桶和路径，再开始上传。", Colors.SeaGreen);
            FlashPreview();
        }
        catch (Exception ex)
        {
            _plan = null;
            PreviewText.Text = "检查未通过：" + ex.Message;
            SetBadge(SourceBadge, Color.FromRgb(255, 235, 232));
            SetStatus("请先修正产物后重试。", Colors.IndianRed);
            FlashPreview();
        }
        finally { _busy = false; LockInputs(false); UpdateButtons(); }
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _plan == null) return;
        var plan = _plan;
        // 弹窗再次确认，避免误把测试版本投放到正式桶。密钥绝不出现在日志中。
        string bucket = BucketBox.Text.Trim(), region = RegionBox.Text.Trim();
        if (MessageBox.Show(this,
                $"即将发布 {plan.Platform}/{plan.AppVersion}/{plan.ResVersion} 到：\n{bucket} ({region})\n\n"
                + $"入口清单：{plan.ManifestKey}\n\n内容全部上传成功后，才会更新线上清单。确定继续？",
                "确认发布目标", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        _busy = true;
        LockInputs(true);
        _cancellation = new CancellationTokenSource();
        CancelButton.IsEnabled = true;
        UpdateButtons();
        PublishProgress.BeginAnimation(RangeBase.ValueProperty, null);
        PublishProgress.Value = 0;
        PercentText.Text = "0%";
        LogBox.Clear();
        SetBadge(PublishBadge, Color.FromRgb(224, 236, 255));
        SetStatus("正在上传…", Colors.RoyalBlue, true);
        try
        {
            var settings = new CosSettings(bucket, region, SecretIdBox.Text.Trim(), SecretKeyBox.Password);
            var log = new Progress<string>(line => { LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\n"); LogBox.ScrollToEnd(); });
            var progress = new Progress<double>(value => SetProgress(value * 100));
            await new CosPublisher().PublishAsync(plan, settings, log, progress, _cancellation.Token);
            SetProgress(100);
            SetBadge(PublishBadge, Color.FromRgb(220, 246, 231));
            SetStatus("发布成功。请刷新 CDN 清单缓存，并在客户端确认新版本。", Colors.SeaGreen);
        }
        catch (OperationCanceledException)
        {
            SetBadge(PublishBadge, Color.FromRgb(255, 242, 213));
            SetStatus("已取消。若取消发生在清单提交期间，请到 COS 控制台确认实际结果。", Colors.DarkOrange);
        }
        catch (Exception ex)
        {
            SetBadge(PublishBadge, Color.FromRgb(255, 235, 232));
            SetStatus("发布失败：" + ex.Message, Colors.IndianRed);
            LogBox.AppendText("失败：" + ex.Message + "\n");
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _busy = false;
            CancelButton.IsEnabled = false;
            LockInputs(false);
            UpdateButtons();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        CancelButton.IsEnabled = false;
        SetStatus("取消请求已发出，正在等待当前文件上传返回…", Colors.DarkOrange, true);
    }
}
