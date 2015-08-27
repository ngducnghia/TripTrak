using Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace TripTrak
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        /// <summary>
        /// Gets or sets the MovementThreshold
        /// </summary>
        public int MovementThreshold { get; set; }
        public int DesiredAccuracyInMeters { get; set; }
        public int ReportInterval { get; set; }
        private Windows.Storage.ApplicationDataContainer localSettings;
        public SettingsPage()
        {
            this.InitializeComponent();
            localSettings= Windows.Storage.ApplicationData.Current.LocalSettings;
        }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.NavigationMode == NavigationMode.New)
            {

            }

                MoveThresholdTb.Text = LocationHelper.Geolocator.MovementThreshold.ToString("0");
                AccuracyTb.Text = LocationHelper.Geolocator.DesiredAccuracyInMeters.ToString();
                IntervalTb.Text = LocationHelper.Geolocator.ReportInterval.ToString();
        }

        private void SaveBt_Click(object sender, RoutedEventArgs e)
        {
            LocationHelper.Geolocator.MovementThreshold = Convert.ToDouble(MoveThresholdTb.Text);
            LocationHelper.Geolocator.DesiredAccuracyInMeters = Convert.ToUInt32(AccuracyTb.Text);
            LocationHelper.Geolocator.ReportInterval = Convert.ToUInt32(IntervalTb.Text);

        

            // Create a setting in a container

            Windows.Storage.ApplicationDataContainer container =
               localSettings.CreateContainer("GeolocatorSettings", Windows.Storage.ApplicationDataCreateDisposition.Always);

            if (localSettings.Containers.ContainsKey("GeolocatorSettings"))
            {
                localSettings.Containers["GeolocatorSettings"].Values["MovementThreshold"] = LocationHelper.Geolocator.MovementThreshold;
                localSettings.Containers["GeolocatorSettings"].Values["DesiredAccuracyInMeters"] = LocationHelper.Geolocator.DesiredAccuracyInMeters;
                localSettings.Containers["GeolocatorSettings"].Values["ReportInterval"] = LocationHelper.Geolocator.ReportInterval;
            }
        }
    }
}
