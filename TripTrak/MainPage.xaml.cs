﻿using Helpers;
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

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace TripTrak
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage(Frame frame)
        {
            this.InitializeComponent();
            this.ShellSplitView.Content = frame;

            Windows.Storage.ApplicationDataContainer localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            // Read data from a setting in a container
            bool hasGeoSettings = localSettings.Containers.ContainsKey("GeolocatorSettings");

            if (hasGeoSettings)
            {
                LocationHelper.Geolocator.MovementThreshold = Convert.ToDouble(localSettings.Containers["GeolocatorSettings"].Values["MovementThreshold"]);
                LocationHelper.Geolocator.DesiredAccuracyInMeters = Convert.ToUInt32(localSettings.Containers["GeolocatorSettings"].Values["DesiredAccuracyInMeters"]);
                LocationHelper.Geolocator.ReportInterval = Convert.ToUInt32(localSettings.Containers["GeolocatorSettings"].Values["ReportInterval"]);
            }
            else
            {
                LocationHelper.Geolocator.DesiredAccuracy = Windows.Devices.Geolocation.PositionAccuracy.High; 
            }

        }

        private void OnHomeButtonChecked(object sender, RoutedEventArgs e)
        {
            ShellSplitView.IsPaneOpen = false;
            if (ShellSplitView.Content != null)
                ((Frame)ShellSplitView.Content).Navigate(typeof(HomePage));
        }

        private void OnSettingsButtonChecked(object sender, RoutedEventArgs e)
        {
            ShellSplitView.IsPaneOpen = false;
            if (ShellSplitView.Content != null)
                ((Frame)ShellSplitView.Content).Navigate(typeof(SettingsPage));
        }

        private void OnAboutButtonChecked(object sender, RoutedEventArgs e)
        {
            ShellSplitView.IsPaneOpen = false;
            if (ShellSplitView.Content != null)
                ((Frame)ShellSplitView.Content).Navigate(typeof(AboutPage));
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ShellSplitView.IsPaneOpen = !ShellSplitView.IsPaneOpen;
        }
    }
}
