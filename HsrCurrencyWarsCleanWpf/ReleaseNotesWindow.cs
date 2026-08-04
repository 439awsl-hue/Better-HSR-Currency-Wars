using System.Windows;
using System.Windows.Input;

namespace HsrCurrencyWarsCleanWpf;

public partial class ReleaseNotesWindow : Window
{
	public bool DoNotShowAgain => DoNotShowAgainBox.IsChecked == true;

	public ReleaseNotesWindow()
	{
		InitializeComponent();
	}

	private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.LeftButton == MouseButtonState.Pressed)
		{
			DragMove();
		}
	}

	private void Confirm_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = true;
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
	}
}
