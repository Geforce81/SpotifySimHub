using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GothiaGripPlugin
{
    internal sealed class GothiaGripSettingsControl : UserControl
    {
        private readonly GothiaGrip plugin;
        private readonly TextBlock testStatus;
        private readonly TextBlock temperatureTestStatus;

        public GothiaGripSettingsControl(GothiaGrip plugin)
        {
            this.plugin = plugin;

            StackPanel panel = new StackPanel
            {
                Margin = new Thickness(28.0),
                MaxWidth = 720.0,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            panel.Children.Add(new TextBlock
            {
                Text = "Gothia Grip Monitor",
                FontSize = 26.0,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Pluginen är laddad och klar.",
                FontSize = 16.0,
                Foreground = Brushes.LightGreen,
                Margin = new Thickness(0.0, 0.0, 0.0, 22.0)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Bind den röda ikonens Visible-egenskap till:",
                FontSize = 15.0
            });

            panel.Children.Add(new TextBlock
            {
                Text = "GothiaGrip.WarningBlink",
                FontSize = 18.0,
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0.0, 6.0, 0.0, 6.0)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Låt den vita ikonen vara synlig och stäng av Sim Dash vanliga Blink-alternativ för den röda.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14.0,
                Margin = new Thickness(0.0, 0.0, 0.0, 24.0)
            });

            Button testButton = new Button
            {
                Content = "Testa röd blinkning i 5 sekunder",
                Padding = new Thickness(16.0, 8.0, 16.0, 8.0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            testButton.Click += TestButton_Click;
            panel.Children.Add(testButton);

            testStatus = new TextBlock
            {
                Text = string.Empty,
                FontSize = 14.0,
                Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
            };
            panel.Children.Add(testStatus);

            panel.Children.Add(new Separator
            {
                Margin = new Thickness(0.0, 28.0, 0.0, 24.0)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Temperaturvarning",
                FontSize = 22.0,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Röd varning vid olja över 125 °C eller vatten över 105 °C. GT7 skickar för närvarande oljetemperatur men inget användbart vattenvärde.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14.0,
                Margin = new Thickness(0.0, 0.0, 0.0, 16.0)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Bind den röda motorikonens Visible-egenskap till:",
                FontSize = 15.0
            });

            panel.Children.Add(new TextBlock
            {
                Text = "GothiaGrip.TemperatureWarningBlink",
                FontSize = 18.0,
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0.0, 6.0, 0.0, 16.0)
            });

            Button temperatureTestButton = new Button
            {
                Content = "Testa temperaturblinkning i 5 sekunder",
                Padding = new Thickness(16.0, 8.0, 16.0, 8.0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            temperatureTestButton.Click += TemperatureTestButton_Click;
            panel.Children.Add(temperatureTestButton);

            temperatureTestStatus = new TextBlock
            {
                Text = string.Empty,
                FontSize = 14.0,
                Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
            };
            panel.Children.Add(temperatureTestStatus);

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            };
        }

        internal static ImageSource CreateMenuIcon()
        {
            DrawingGroup group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(
                Brushes.Transparent,
                new Pen(Brushes.White, 1.8),
                new EllipseGeometry(new Point(12.0, 12.0), 9.0, 9.0)));
            group.Children.Add(new GeometryDrawing(
                Brushes.White,
                null,
                new EllipseGeometry(new Point(12.0, 12.0), 3.2, 3.2)));

            DrawingImage image = new DrawingImage(group);
            image.Freeze();
            return image;
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            plugin.StartTestWarning();
            testStatus.Text = "Testsignalen körs nu.";
            testStatus.Foreground = Brushes.LightGreen;
        }

        private void TemperatureTestButton_Click(object sender, RoutedEventArgs e)
        {
            plugin.StartTemperatureTestWarning();
            temperatureTestStatus.Text = "Temperaturtestet körs nu.";
            temperatureTestStatus.Foreground = Brushes.LightGreen;
        }
    }
}
