using System.Windows;
using System.Windows.Media.Animation;

namespace AuraScan_Ultrasound_System
{
    public partial class SplashScreen : Window
    {
        public SplashScreen()
        {
            InitializeComponent();
        }

        public void UpdateStatus(string message, double progressPercent)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
                AnimateProgress(progressPercent);
            });
        }

        private void AnimateProgress(double percent)
        {
            double targetWidth = 300.0 * (percent / 100.0);
            var animation = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            ProgressFill.BeginAnimation(WidthProperty, animation);
        }

        public async Task FadeOutAsync()
        {
            var tcs = new TaskCompletionSource();
            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) => tcs.SetResult();
            BeginAnimation(OpacityProperty, fadeOut);
            await tcs.Task;
        }
    }
}
