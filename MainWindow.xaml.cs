// ReSharper disable InconsistentNaming

using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using fs2ff.SimConnect;

namespace fs2ff
{
    public partial class MainWindow
    {
        private const uint WM_USER_SIMCONNECT = 0x0402;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            this.SaveWindowPosition();
            ((ISimConnectMessageHandler) DataContext).Dispose();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            HwndSource hwndSource = (HwndSource) PresentationSource.FromVisual(this)!;
            hwndSource.AddHook(WndProc);
            ((ISimConnectMessageHandler) DataContext).WindowHandle = hwndSource.Handle;
            this.RestoreWindowPosition();
        }

        private IntPtr WndProc(IntPtr hWnd, int iMsg, IntPtr hWParam, IntPtr hLParam, ref bool bHandled)
        {
            if (iMsg == WM_USER_SIMCONNECT)
            {
                ((ISimConnectMessageHandler) DataContext).ReceiveFlightSimMessage();
            }

            return IntPtr.Zero;
        }

        private void RestoreWindowPosition()
        {
            //TODO: Note that Region is a windows only construct.
            var screen = new Region( new Rectangle(
                Convert.ToInt32(SystemParameters.VirtualScreenLeft), Convert.ToInt32(SystemParameters.VirtualScreenTop),
                Convert.ToInt32(SystemParameters.VirtualScreenWidth), Convert.ToInt32(SystemParameters.VirtualScreenHeight)));

            if (Preferences.Default.HasSetDefaults && screen.IsVisible(Preferences.Default.Location))
            {
                this.WindowState = (WindowState)Preferences.Default.WindowState;
                this.Top = Preferences.Default.Location.Y;
                this.Left = Preferences.Default.Location.X;
                this.Width = Preferences.Default.Size.Width;
                this.Height = Preferences.Default.Size.Height;
            }
        }

        private void SaveWindowPosition()
        {
            Preferences.Default.WindowState = (int) this.WindowState;

            Preferences.Default.Size = new System.Drawing.Size(Convert.ToInt32(this.Width), Convert.ToInt32(Height));
            Preferences.Default.Location = new System.Drawing.Point(Convert.ToInt32((int) this.Left), Convert.ToInt32((int) this.Top));
            Preferences.Default.HasSetDefaults = true;
            Preferences.Default.Save();
        }
    }
}
