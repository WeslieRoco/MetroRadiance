﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using MetroRadiance.Core;
using MetroRadiance.Core.Win32;

namespace MetroRadiance.Chrome.Internal
{
	/// <summary>
	/// ウィンドウの四辺にアタッチされる発光ウィンドウを表します。
	/// </summary>
	internal class GlowWindow : Window
	{
		static GlowWindow()
		{
			DefaultStyleKeyProperty.OverrideMetadata(
				typeof(GlowWindow),
				new FrameworkPropertyMetadata(typeof(GlowWindow)));
		}

		private static Dpi? systemDpi;

		private IntPtr handle;
		private readonly GlowWindowProcessor processor;
		private bool closed;

		private readonly IWindow owner;
		private WindowState ownerState;

		#region IsGlowing 依存関係プロパティ

		public bool IsGlowing
		{
			get { return (bool)this.GetValue(IsGlowingProperty); }
			set { this.SetValue(IsGlowingProperty, value); }
		}

		public static readonly DependencyProperty IsGlowingProperty =
			DependencyProperty.Register(nameof(IsGlowing), typeof(bool), typeof(GlowWindow), new UIPropertyMetadata(true));

		#endregion

		#region Orientation 依存関係プロパティ

		public Orientation Orientation
		{
			get { return (Orientation)this.GetValue(OrientationProperty); }
			set { this.SetValue(OrientationProperty, value); }
		}

		public static readonly DependencyProperty OrientationProperty =
			DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(GlowWindow), new UIPropertyMetadata(Orientation.Horizontal));

		#endregion

		#region ActiveGlowBrush 依存関係プロパティ

		public Brush ActiveGlowBrush
		{
			get { return (Brush)this.GetValue(ActiveGlowBrushProperty); }
			set { this.SetValue(ActiveGlowBrushProperty, value); }
		}

		public static readonly DependencyProperty ActiveGlowBrushProperty =
			DependencyProperty.Register(nameof(ActiveGlowBrush), typeof(Brush), typeof(GlowWindow), new UIPropertyMetadata(null));

		#endregion

		#region InactiveGlowBrush 依存関係プロパティ

		public Brush InactiveGlowBrush
		{
			get { return (Brush)this.GetValue(InactiveGlowBrushProperty); }
			set { this.SetValue(InactiveGlowBrushProperty, value); }
		}

		public static readonly DependencyProperty InactiveGlowBrushProperty =
			DependencyProperty.Register(nameof(InactiveGlowBrush), typeof(Brush), typeof(GlowWindow), new UIPropertyMetadata(null));

		#endregion

		#region ChromeMode 依存関係プロパティ

		public ChromeMode ChromeMode
		{
			get { return (ChromeMode)this.GetValue(ChromeModeProperty); }
			set { this.SetValue(ChromeModeProperty, value); }
		}
		public static readonly DependencyProperty ChromeModeProperty =
			DependencyProperty.Register(nameof(ChromeMode), typeof(ChromeMode), typeof(GlowWindow), new UIPropertyMetadata(ChromeMode.VisualStudio2013));

		#endregion

		internal GlowWindow(IWindow window, IChromeSettings settings, GlowWindowProcessor processor)
		{
			this.owner = window;
			this.processor = processor;

			this.Title = processor.GetType().Name.Replace("Processor", "");
			this.WindowStyle = WindowStyle.None;
			this.AllowsTransparency = true;
			this.ShowActivated = false;
			this.ShowInTaskbar = false;
			this.Visibility = Visibility.Collapsed;
			this.ResizeMode = ResizeMode.NoResize;
			this.WindowStartupLocation = WindowStartupLocation.Manual;
			this.Left = processor.GetLeft(this.owner.Left, this.owner.ActualWidth);
			this.Top = processor.GetTop(this.owner.Top, this.owner.ActualHeight);
			this.Width = processor.GetWidth(this.owner.Left, this.owner.ActualWidth);
			this.Height = processor.GetHeight(this.owner.Top, this.owner.ActualHeight);
			this.Orientation = processor.Orientation;
			this.HorizontalContentAlignment = processor.HorizontalAlignment;
			this.VerticalContentAlignment = processor.VerticalAlignment;

			this.ownerState = this.WindowState;

			var bindingActive = new Binding(nameof(settings.ActiveBrush)) { Source = settings, };
			this.SetBinding(ActiveGlowBrushProperty, bindingActive);

			var bindingInactive = new Binding(nameof(settings.InactiveBrush)) { Source = settings, };
			this.SetBinding(InactiveGlowBrushProperty, bindingInactive);

			var bindingChromeMode = new Binding(nameof(settings.ChromeMode)) { Source = settings, };
			this.SetBinding(ChromeModeProperty, bindingChromeMode);

			this.owner.ContentRendered += (sender, args) =>
			{
				if (this.closed) return;
				this.Show();
				this.Update();
			};
			this.owner.StateChanged += (sender, args) =>
			{
				if (this.closed) return;
				this.Update();
				this.ownerState = this.owner.WindowState;
			};
			this.owner.LocationChanged += (sender, args) => this.Update();
			this.owner.SizeChanged += (sender, args) => this.Update();
			this.owner.Activated += (sender, args) => this.Update();
			this.owner.Deactivated += (sender, args) => this.Update();
			this.owner.Closed += (sender, args) => this.Close();
		}

		protected override void OnSourceInitialized(EventArgs e)
		{
			base.OnSourceInitialized(e);

			var source = PresentationSource.FromVisual(this) as HwndSource;
			if (source != null)
			{
				this.handle = source.Handle;
				this.SetWindowStyle();
				source.AddHook(this.WndProc);
			}

			var wrapper = this.owner as WindowWrapper;
			if (wrapper != null)
			{
				this.Owner = wrapper.Window;
			}
		}

		protected override void OnClosed(EventArgs e)
		{
			base.OnClosed(e);
			var source = PresentationSource.FromVisual(this) as HwndSource;
			source?.RemoveHook(this.WndProc);
			this.closed = true;
		}

		public async void Update()
		{
			if (this.closed) return;

			if (this.owner.Visibility == Visibility.Hidden)
			{
				this.Visibility = Visibility.Hidden;
				this.UpdateCore();
			}
			else if (this.owner.WindowState == WindowState.Normal)
			{
				if (this.ownerState == WindowState.Minimized
					&& SystemParameters.MinimizeAnimation)
				{
					// 最小化から復帰 && 最小化アニメーションが有効の場合
					// アニメーションが完了しウィンドウが表示されるまで遅延させる (それがだいたい 250 ミリ秒くらい)
					await Task.Delay(250);
				}

				this.Visibility = Visibility.Visible;
				this.UpdateCore();
			}
			else
			{
				this.Visibility = Visibility.Collapsed;
			}
		}

		private void UpdateCore()
		{
			this.IsGlowing = this.owner.IsActive;

			var dpi = systemDpi ?? (systemDpi = this.GetSystemDpi()) ?? Dpi.Default;
			var left = (int)Math.Round(this.processor.GetLeft(this.owner.Left, this.owner.ActualWidth) * dpi.ScaleX);
			var top = (int)Math.Round(this.processor.GetTop(this.owner.Top, this.owner.ActualHeight) * dpi.ScaleY);
			var width = (int)Math.Round(this.processor.GetWidth(this.owner.Left, this.owner.ActualWidth) * dpi.ScaleX);
			var height = (int)Math.Round(this.processor.GetHeight(this.owner.Top, this.owner.ActualHeight) * dpi.ScaleY);

			NativeMethods.SetWindowPos(this.handle, this.owner.Handle, left, top, width, height, SWP.NOACTIVATE);
		}


		private void SetWindowStyle()
		{
			var wsex = this.handle.GetWindowLongEx();
			wsex |= WSEX.TOOLWINDOW;
			this.handle.SetWindowLongEx(wsex);

			var cs = this.handle.GetClassLong(ClassLongFlags.GclStyle);
			cs |= ClassStyles.DblClks;
			this.handle.SetClassLong(ClassLongFlags.GclStyle, cs);
		}


		private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (msg == (int)WM.MOUSEACTIVATE)
			{
				handled = true;
				return new IntPtr(3);
			}

			if (msg == (int)WM.LBUTTONDOWN)
			{
				if (!this.owner.IsActive)
				{
					this.owner.Activate();
				}

				var ptScreen = lParam.ToPoint();

				NativeMethods.PostMessage(
					this.owner.Handle,
					(uint)WM.NCLBUTTONDOWN,
					(IntPtr)this.processor.GetHitTestValue(ptScreen, this.ActualWidth, this.ActualHeight),
					IntPtr.Zero);
			}
			if (msg == (int)WM.NCHITTEST)
			{
				if (this.owner.ResizeMode == ResizeMode.NoResize)
				{
					return IntPtr.Zero;
				}

				var ptScreen = lParam.ToPoint();
				var ptClient = this.PointFromScreen(ptScreen);

				this.Cursor = this.processor.GetCursor(ptClient, this.ActualWidth, this.ActualHeight);
			}

			if (msg == (int)WM.LBUTTONDBLCLK)
			{
				if (this.processor.GetType() == typeof(GlowWindowProcessorTop))
				{
					NativeMethods.SendMessage(this.owner.Handle, WM.NCLBUTTONDBLCLK, (IntPtr)HitTestValues.HTTOP, IntPtr.Zero);
				}
				else if (this.processor.GetType() == typeof(GlowWindowProcessorBottom))
				{
					NativeMethods.SendMessage(this.owner.Handle, WM.NCLBUTTONDBLCLK, (IntPtr)HitTestValues.HTBOTTOM, IntPtr.Zero);
				}
			}

			return IntPtr.Zero;
		}
	}
}
