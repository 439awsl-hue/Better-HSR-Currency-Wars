using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using HsrCurrencyWarsCleanWpf.Services;

namespace HsrCurrencyWarsCleanWpf;

public sealed class GameLogOverlayWindow : Window
{
	private const int GwlExStyle = -20;

	private const int WsExLayered = 524288;

	private const int WsExTransparent = 32;

	private const int WsExToolWindow = 128;

	private const int WsExNoActivate = 134217728;

	private const int HwndTopmost = -1;

	private const int SwpNoSize = 1;

	private const int SwpNoMove = 2;

	private const int SwpNoActivate = 16;

	private const int SwpShowWindow = 64;

	private readonly Queue<string> _lines = new Queue<string>();

	private readonly TextBlock _logText;

	private readonly int _maxLines = 12;

	private readonly int _margin = 14;

	public GameLogOverlayWindow()
	{
		base.WindowStyle = WindowStyle.None;
		base.AllowsTransparency = true;
		base.Background = Brushes.Transparent;
		base.ResizeMode = ResizeMode.NoResize;
		base.ShowInTaskbar = false;
		base.ShowActivated = false;
		base.Topmost = true;
		base.Focusable = false;
		base.Width = 460.0;
		base.Height = 190.0;
		Border root = new Border
		{
			Background = new SolidColorBrush(Color.FromArgb(188, 15, 22, 34)),
			BorderBrush = new SolidColorBrush(Color.FromArgb(52, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(16.0),
			Padding = new Thickness(16.0, 12.0, 16.0, 12.0),
			SnapsToDevicePixels = true
		};
		DockPanel panel = new DockPanel
		{
			LastChildFill = true
		};
		DockPanel header = new DockPanel
		{
			LastChildFill = true,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		TextBlock title = new TextBlock
		{
			Text = "运行日志",
			Foreground = new SolidColorBrush(Color.FromArgb(245, 248, 250, byte.MaxValue)),
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			VerticalAlignment = VerticalAlignment.Center
		};
		DockPanel.SetDock(title, Dock.Left);
		TextBlock hotkey = new TextBlock
		{
			Text = "F8 停止",
			Foreground = new SolidColorBrush(Color.FromArgb(225, 248, 250, byte.MaxValue)),
			FontSize = 12.0,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center
		};
		header.Children.Add(title);
		header.Children.Add(hotkey);
		DockPanel.SetDock(header, Dock.Top);
		_logText = new TextBlock
		{
			Foreground = new SolidColorBrush(Color.FromArgb(238, 248, 250, byte.MaxValue)),
			FontFamily = new FontFamily("NSimSun"),
			FontSize = 12.0,
			TextWrapping = TextWrapping.NoWrap,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		panel.Children.Add(header);
		panel.Children.Add(_logText);
		root.Child = panel;
		base.Content = root;
		base.SourceInitialized += delegate
		{
			ApplyClickThrough();
		};
		Hide();
	}

	public void AppendLog(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		string[] array = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			string line = array[i].TrimEnd();
			if (line.Length != 0)
			{
				_lines.Enqueue(line);
				while (_lines.Count > _maxLines)
				{
					_lines.Dequeue();
				}
			}
		}
		_logText.Text = string.Join(Environment.NewLine, _lines);
	}

	public void ClearLogs()
	{
		_lines.Clear();
		_logText.Text = string.Empty;
	}

	public void UpdateGeometry(WindowClientRect clientRect)
	{
		double width = Math.Clamp((double)clientRect.Width * 0.34, 360.0, 560.0);
		double height = Math.Clamp((double)clientRect.Height * 0.24, 168.0, 240.0);
		int physicalLeft = clientRect.Left + _margin;
		double physicalTop = (double)clientRect.Top + Math.Max(_margin, (double)clientRect.Height - height - (double)_margin);
		PresentationSource source = PresentationSource.FromVisual(this);
		if (source?.CompositionTarget != null)
		{
			Point topLeft = source.CompositionTarget.TransformFromDevice.Transform(new Point(physicalLeft, physicalTop));
			Point size = source.CompositionTarget.TransformFromDevice.Transform(new Point(width, height));
			base.Left = topLeft.X;
			base.Top = topLeft.Y;
			base.Width = size.X;
			base.Height = size.Y;
		}
		else
		{
			base.Left = physicalLeft;
			base.Top = physicalTop;
			base.Width = width;
			base.Height = height;
		}
	}

	public void ShowOverlay()
	{
		ApplyClickThrough();
		if (!base.IsVisible)
		{
			Show();
		}
	}

	public void HideOverlay()
	{
		if (base.IsVisible)
		{
			Hide();
		}
	}

	private void ApplyClickThrough()
	{
		nint hwnd = new WindowInteropHelper(this).Handle;
		if (hwnd != IntPtr.Zero)
		{
			int exStyle = GetWindowLong(hwnd, -20);
			exStyle |= 0x80800A0;
			SetWindowLong(hwnd, -20, exStyle);
			SetWindowPos(hwnd, -1, 0, 0, 0, 0, 83);
		}
	}

	[DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
	private static extern int GetWindowLong(nint hwnd, int index);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
	private static extern int SetWindowLong(nint hwnd, int index, int newStyle);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(nint hwnd, int hwndInsertAfter, int x, int y, int cx, int cy, int flags);
}
