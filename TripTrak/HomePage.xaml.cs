﻿using Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Devices.Geolocation;
using Windows.Networking.Connectivity;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Maps;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Navigation;
using Windows.Media.Capture;
using Windows.Storage;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;
using Windows.UI.Popups;
using Windows.ApplicationModel.ExtendedExecution;
using Windows.Services.Maps;
using Windows.UI;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace TripTrak
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class HomePage : Page
    {
        private LocationData locationInEdit;
        private BasicGeoposition locationInEditOriginalPosition;
        private bool isNewLocationInEdit;
        private bool isMapSelectionEnabled;
        private bool isExistingLocationBeingRepositioned;
        private List<LocationData> filteredLocations { get; set; }

        #region Location data

        /// <summary>
        /// Gets or sets the saved locations. 
        /// </summary>
        public ObservableCollection<LocationData> Locations { get; private set; }

        /// <summary>
        /// Gets or sets the locations represented on the map; this is a superset of Locations, and 
        /// includes the current location and any locations being added but not yet saved. 
        /// </summary>
        public ObservableCollection<LocationData> MappedLocations { get; set; }

        private object selectedLocation;
        /// <summary>
        /// Gets or sets the LocationData object corresponding to the current selection in the locations list. 
        /// </summary>
        public object SelectedLocation
        {
            get { return this.selectedLocation; }
            set
            {
                if (this.selectedLocation != value)
                {
                    var oldValue = this.selectedLocation as LocationData;
                    var newValue = value as LocationData;
                    if (oldValue != null)
                    {
                        oldValue.IsSelected = false;
                        this.InputMap.Routes.Clear();
                    }
                    if (newValue != null)
                    {
                        newValue.IsSelected = true;
                        if (newValue.FastestRoute != null) this.InputMap.Routes.Add(new MapRouteView(newValue.FastestRoute));
                    }
                    this.selectedLocation = newValue;
                }
            }
        }

        #endregion Location data

        #region Initialization and navigation code

        /// <summary>
        /// Initializes a new instance of the class and sets up the association
        /// between the Locations and MappedLocations collections. 
        /// </summary>
        public HomePage()
        {
            this.InitializeComponent();
            if (Locations != null && Locations.Count > 0)
                return;
            else
            {
                this.filteredLocations = new List<LocationData>();
                this.Locations = new ObservableCollection<LocationData>();
                this.MappedLocations = new ObservableCollection<LocationData>(this.Locations);
                // MappedLocations is a superset of Locations, so any changes in Locations
                // need to be reflected in MappedLocations. 
                this.Locations.CollectionChanged += (s, e) =>
                {
                    if (e.NewItems != null) foreach (LocationData item in e.NewItems) this.MappedLocations.Add(item);
                    if (e.OldItems != null) foreach (LocationData item in e.OldItems) this.MappedLocations.Remove(item);
                    try
                    {
                        CheckPointSlider.Maximum = Locations.Count;
                    }
                    catch
                    {

                    }
                };

                // Update the travel times every 5 minutes.
                this.StartTimer(5, async () => await this.UpdateLocationsTravelInfoAsync());

                // Update the freshness timestamp every minute;
                this.StartTimer(1, () => { foreach (var location in this.Locations) location.RefreshFormattedTimestamp(); });

                LocationHelper.RegisterTrafficMonitor();
            }


        }



        /// <summary>
        /// Starts a timer to perform the specified action at the specified interval.
        /// </summary>
        /// <param name="intervalInMinutes">The interval.</param>
        /// <param name="action">The action.</param>
        private void StartTimer(int intervalInMinutes, Action action)
        {
            var timer = new DispatcherTimer();
            timer.Interval = new TimeSpan(0, intervalInMinutes, 0);
            timer.Tick += (s, e) => action();
            timer.Start();
        }

        /// <summary>
        /// Loads the saved location data on first navigation, and 
        /// attaches a Geolocator.StatusChanged event handler. 
        /// </summary>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.NavigationMode == NavigationMode.New)
            {
                App.userLocData = await LocationDataStore.GetBasicGeopositionAsync();
            }

            // Start handling Geolocator and network status changes after loading the data 
            // so that the view doesn't get refreshed before there is something to show.
            LocationHelper.Geolocator.StatusChanged += Geolocator_StatusChanged;
            NetworkInformation.NetworkStatusChanged += NetworkInformation_NetworkStatusChanged;
            LocationHelper.Geolocator.PositionChanged += Geolocator_PositionChanged;
            StartLocationExtensionSession();

        }


        /// <summary>
        /// Cancels any in-flight request to the Geolocator, and
        /// disconnects the Geolocator.StatusChanged event handler. 
        /// </summary>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            LocationHelper.CancelGetCurrentLocation();
            LocationHelper.Geolocator.StatusChanged -= Geolocator_StatusChanged;
            NetworkInformation.NetworkStatusChanged -= NetworkInformation_NetworkStatusChanged;
            LocationHelper.Geolocator.PositionChanged -= Geolocator_PositionChanged;
            StopLocationExtensionSession();
        }

        #endregion Initialization and navigation code

        #region Geolocator and network status and map refresh code

        private void Geolocator_PositionChanged(Geolocator sender, PositionChangedEventArgs args)
        {
            var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                if (args.Position.Coordinate.Accuracy < 55)
                {
                    var item = new SimpleGeoData
                    {
                        Position = args.Position.Coordinate.Point.Position
                    };
                    App.userLocData.Add(item);
                    await LocationDataStore.InsertLocationDataAsync(item);
                }

            });
        }

        /// <summary>
        /// Handles the Geolocator.StatusChanged event to refresh the map and locations list 
        /// if the Geolocator is available, and to display an error message otherwise.
        /// </summary>
        private async void Geolocator_StatusChanged(Geolocator sender, StatusChangedEventArgs args)
        {
            await this.CallOnUiThreadAsync(async () =>
            {
                switch (args.Status)
                {
                    case PositionStatus.Ready:
                        this.UpdateLocationStatus(true);
                        await this.ResetViewAsync();
                        break;
                    case PositionStatus.Initializing:
                        break;
                    case PositionStatus.NoData:
                    case PositionStatus.Disabled:
                    case PositionStatus.NotInitialized:
                    case PositionStatus.NotAvailable:
                    default:
                        this.UpdateLocationStatus(false);
                        await this.ResetViewAsync(false);
                        break;
                }
            });
        }

        /// <summary>
        /// Handles the NetworkInformation.NetworkStatusChanged event to refresh the locations 
        /// list if the internet is available, and to display an error message otherwise.
        /// </summary>
        /// <param name="sender"></param>
        private async void NetworkInformation_NetworkStatusChanged(object sender)
        {
            await this.CallOnUiThreadAsync(async () =>
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                bool isNetworkAvailable = profile != null;
                this.UpdateNetworkStatus(isNetworkAvailable);
                if (isNetworkAvailable) await this.ResetViewAsync();
            });
        }

        /// <summary>
        /// Runs the specified handler on the UI thread at Normal priority. 
        /// </summary>
        private async Task CallOnUiThreadAsync(DispatchedHandler handler) => await
            CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, handler);

        /// <summary>
        /// Updates the UI to account for the user's current position, if available, 
        /// resetting the MapControl bounds and refreshing the travel info. 
        /// </summary>
        /// <param name="isGeolocatorReady">false if the Geolocator is known to be unavailable; otherwise, true.</param>
        /// <returns></returns>
        private async Task ResetViewAsync(bool isGeolocatorReady = true)
        {
            LocationData currentLocation = null;
            if (isGeolocatorReady) currentLocation = await this.GetCurrentLocationAsync();
            if (currentLocation != null)
            {
                if (this.MappedLocations.Count > 0)
                {
                    var currentLoc = this.MappedLocations.FirstOrDefault(loc => loc.IsCurrentLocation == true);
                    if (currentLoc != null && currentLoc.IsCurrentLocation)
                        this.MappedLocations.Remove(currentLoc);
                }
                this.MappedLocations.Add(new LocationData { Position = currentLocation.Position, IsCurrentLocation = true });
                await LocationHelper.TryUpdateMissingLocationInfoAsync(this.MappedLocations[this.MappedLocations.Count - 1], null);
            }
            // Set the current view of the map control. 
            var positions = this.Locations.Select(loc => loc.Position).ToList();
            if (currentLocation != null) positions.Insert(0, currentLocation.Position);
            await setViewOnMap(positions);
            if (currentLocation != null) await this.TryUpdateLocationsTravelInfoAsync(this.Locations, currentLocation);
        }


        /// <summary>
        /// Updates the travel time and distance info for all locations in the Locations collection,
        /// based on the user's current position (if available). 
        /// </summary>
        private async Task UpdateLocationsTravelInfoAsync()
        {
            var currentLocation = await this.GetCurrentLocationAsync();
            if (currentLocation != null) await this.TryUpdateLocationsTravelInfoAsync(this.Locations, currentLocation);
        }

        /// <summary>
        /// Shows or hides the error message relating to network status, depending on the specified value.
        /// </summary>
        /// <param name="isNetworkAvailable">true if network resources are available; otherwise, false.</param>
        private void UpdateNetworkStatus(bool isNetworkAvailable)
        {
            this.MapServicesDisabledMessage.Visibility =
                isNetworkAvailable ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Shows or hides the error message relating to the Geolocator status, depending on the specified value.
        /// </summary>
        /// <param name="isLocationAvailable">true if the Geolocator is available; otherwise, false.</param>
        private void UpdateLocationStatus(bool isLocationAvailable)
        {
            this.LocationDisabledMessage.Visibility =
                isLocationAvailable ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Attempts to update the travel distance and time info for the specified locations, 
        /// relative to the current location, and raises an alert for each flagged location 
        /// if traffic is currently increasing the travel time by 10 minutes or more; also 
        /// updates the network status message depending on the results.
        /// </summary>
        private async Task<bool> TryUpdateLocationsTravelInfoAsync(IEnumerable<LocationData> locations, LocationData currentLocation)
        {
            bool isNetworkAvailable = await LocationHelper.TryUpdateLocationsTravelInfoAsync(this.Locations, currentLocation);
            this.UpdateNetworkStatus(isNetworkAvailable);
            return isNetworkAvailable;
        }

        /// <summary>
        /// Gets the current location if the geolocator is available, 
        /// and updates the Geolocator status message depending on the results.
        /// </summary>
        /// <returns>The current location.</returns>
        private async Task<LocationData> GetCurrentLocationAsync()
        {
            var currentLocation = await LocationHelper.GetCurrentLocationAsync();
            this.UpdateLocationStatus(currentLocation != null);
            return currentLocation;
        }

        #endregion Geolocator status and map refresh code

        #region Primary commands: app-bar buttons, map holding gesture

        /// <summary>
        /// Handles the button click event to hide or show the locations list, enabling 
        /// greater access to the map control with small windows. 
        /// </summary>
        private void HideLocationsButton_Click(object sender, RoutedEventArgs e)
        {
            this.ToggleLocationsPaneVisibility();
        }

        private void mapItemButton_Click(object sender, RoutedEventArgs e)
        {
            var buttonSender = sender as Button;
            LocationData location = buttonSender.DataContext as LocationData;
            LocationsView.SelectedItem = location;
        }

        private async void TakePhotoButton_Click(object sender, RoutedEventArgs e)
        {

            BitmapImage bitmapSource = await PhotoHelper.GetPhotoFromCameraLaunch();
            if (bitmapSource == null)
                return;
            // Specify a random location
            //    var currentLocation = await LocationHelper.GetRandomGeoposition();
            //Geopoint geopoint = new Geopoint(currentLocation);

            var currentLocation = await LocationHelper.GetCurrentLocationAsync();

            LocationData location = new LocationData
            {
                Position = currentLocation.Geopoint.Position,
                ImageSource = bitmapSource
            };
            this.Locations.Add(location);
            this.LocationsView.UpdateLayout();
            LocationsView.SelectedItem = location;
            await LocationHelper.TryUpdateMissingLocationInfoAsync(location, null);
        }
        /// <summary>
        /// Changes the visibility of the locations list, and updates
        /// the app bar button to reflect the new state. 
        /// </summary>
        private void ToggleLocationsPaneVisibility()
        {
            if (this.LocationsView.Visibility == Visibility.Visible)
            {
                this.LocationsView.Visibility = Visibility.Collapsed;
                this.HideLocationsButton.Icon = new SymbolIcon { Symbol = Symbol.OpenPane };
                ToolTipService.SetToolTip(this.HideLocationsButton, "Show locations list");
            }
            else
            {
                this.LocationsView.Visibility = Visibility.Visible;
                this.HideLocationsButton.Icon = new SymbolIcon { Symbol = Symbol.ClosePane };
                ToolTipService.SetToolTip(this.HideLocationsButton, "Hide locations list");
            }
        }

        /// <summary>
        /// Handles clicks to the Add Current button by adding a new location 
        /// to the Locations list with the user's current position.
        /// </summary>
        private async void AddCurrentLocation_Click(object sender, RoutedEventArgs e)
        {
            (sender as Button).IsEnabled = false;
            var currentLocation = await this.GetCurrentLocationAsync();
            if (currentLocation != null)
            {
                // Resolve the address given the geocoordinates.
                await LocationHelper.TryUpdateMissingLocationInfoAsync(currentLocation, currentLocation);

                this.InputMap.Center = currentLocation.Geopoint;
                this.InputMap.ZoomLevel = 15;
                LocationsView.SelectedIndex = LocationsView.Items.Count - 1;
            }

            (sender as Button).IsEnabled = true;
        }

        /// <summary>
        /// Handles clicks to the Add New button by creating a new, empty location
        /// in the Locations list and displaying the editor flyout. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AddNewLocation_Click(object sender, RoutedEventArgs e)
        {
         //   this.EditNewLocation(new LocationData());
        }

        /// <summary>
        /// Handles the Holding event of the MapControl to add a new location
        /// to the Locations list, using the position indicated by the gesture.
        /// </summary>
        private async void InputMap_MapHolding(MapControl sender, MapInputEventArgs args)
        {
            //var location = new LocationData { Position = args.Location.Position };

            //// Resolve the address given the geocoordinates. In this case, because the 
            //// location is unambiguous, there is no need to pass in the current location.
            //await LocationHelper.TryUpdateMissingLocationInfoAsync(location, null);

            //this.EditNewLocation(location);
        }

        #endregion Primary commands: app-bar buttons, map holding gesture

        #region Location commands: per-location buttons

        /// <summary>
        /// Handles edit button clicks to enter edit mode for the selected location.
        /// </summary>
        private void EditLocation_Click(object sender, RoutedEventArgs e)
        {
          //  this.EditLocation(this.GetLocation(sender as Button));
        }

        /// <summary>
        /// Handles delete button clicks to remove the selected
        /// location from the Locations collection. 
        /// </summary>
        private void DeleteLocation_Click(object sender, RoutedEventArgs e)
        {
            //var location = this.GetLocation(sender as Button);
            //int index = this.Locations.IndexOf(location);
            //this.Locations.Remove(location);
            //await LocationDataStore.SaveLocationDataAsync(this.Locations);
        }

        /// <summary>
        /// Handles clicks to the Show Route button to launch the Maps app and display 
        /// the route to the selected location from the user's current position. 
        /// </summary>
        private async void ShowRouteButton_Click(object sender, RoutedEventArgs e)
        {
            var currentLocation = await this.GetCurrentLocationAsync();
            if (currentLocation != null)
            {
                var location = this.GetLocation(sender as Button);
                await LocationHelper.ShowRouteToLocationInMapsAppAsync(location, currentLocation);
            }
        }

        /// <summary>
        /// Handles clicks to the Track button to flag the selected location for monitoring 
        /// by a background task that periodically checks traffic and sends a notification 
        /// whenever traffic adds 10 minutes or more to the travel time.
        /// </summary>
        private void TrackButton_Click(object sender, RoutedEventArgs e)
        {
            //var button = sender as ToggleButton;
            //var location = this.GetLocation(button);
            //location.IsMonitored = button.IsChecked.Value;
            //this.UpdateTrafficMonitor(button.IsChecked.Value);
            //await LocationDataStore.SaveLocationDataAsync(this.Locations);
        }

        /// <summary>
        /// Gets the data context of the specified element as a LocationData instance.
        /// </summary>
        /// <param name="element">The element bound to the location.</param>
        /// <returns>The location bound to the element.</returns>
        private LocationData GetLocation(FrameworkElement element) =>
            (element.FindName("Presenter") as FrameworkElement).DataContext as LocationData;

        /// <summary>
        /// Registers or unregisters the traffic monitoring background task depending 
        /// on whether the number of tracked locations changes from 1 to 0 or from 0 to 1.
        /// </summary>
        /// <param name="isIncrement">true if a location was just flagged; 
        /// false if a location was just unflagged.</param>
        private void UpdateTrafficMonitor(bool isIncrement)
        {
            var monitoredLocationCount = this.Locations.Count(location => location.IsMonitored);
            if (isIncrement && monitoredLocationCount == 1)
                LocationHelper.RegisterTrafficMonitor();
            else if (monitoredLocationCount == 0)
                LocationHelper.UnregisterTrafficMonitor();
        }

        #endregion Location commands: per-location buttons

        #region Editor functionality

        /// <summary>
        /// Handles clicks to the Save button by saving location edits. 
        /// </summary>
        private void FlyoutSave_Click(object sender, RoutedEventArgs e)
        {
            //    await this.SaveAsync((sender as FrameworkElement).DataContext as LocationData);
        }

        /// <summary>
        /// Handles presses to the Enter key by saving location edits. 
        /// </summary>
        private void TextBox_KeyUp(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            //if (e.Key == Windows.System.VirtualKey.Enter)
            //{
            //    await this.SaveAsync((sender as FrameworkElement).DataContext as LocationData);
            //}
        }

        /// <summary>
        /// Adds the specified location to the Locations list and shows the editor flyout.
        /// </summary>
        public void EditNewLocation(LocationData location)
        {
            if (this.LocationsView.Visibility == Visibility.Collapsed) this.ToggleLocationsPaneVisibility();
            this.Locations.Add(location);
            this.LocationsView.UpdateLayout();
            this.isNewLocationInEdit = true;
            this.EditLocation(location);
        }

        /// <summary>
        /// Opens the editor, binding it to a temporary copy of the current location 
        /// that can be saved, or discarded if the user dismisses the editor. 
        /// </summary>
        /// <param name="location"></param>
        private void EditLocation(LocationData location)
        {
            this.locationInEdit = location;
            var element = this.GetTemplateRootForLocation(location);
            var flyout = Flyout.GetAttachedFlyout(element) as Flyout;
            (flyout.Content as FrameworkElement).DataContext = location.Clone();
            flyout.ShowAt(element);
        }

        /// <summary>
        /// Applies the changes represented by the specified LocationData to 
        /// the cached location in edit, saves the changes to the file system,
        /// and updates the user interface to account for the changes. 
        /// </summary>
        private async Task SaveAsync(LocationData workingCopy)
        {
            Flyout.GetAttachedFlyout(this.GetTemplateRootForLocation(this.locationInEdit)).Hide();

            this.isNewLocationInEdit = false;
            this.isExistingLocationBeingRepositioned = false;
            bool isAddressNew = workingCopy.Address != this.locationInEdit.Address;
            bool areCoordinatesNew = !workingCopy.Position.Equals(this.locationInEdit.Position);

            // If just the address OR just the coordinates are new, 
            // clear the other value so that it can be updated.
            if (isAddressNew ^ areCoordinatesNew)
            {
                if (isAddressNew) workingCopy.Position = new BasicGeoposition();
                if (areCoordinatesNew) workingCopy.Address = string.Empty;
            }

            // If the address, the coordinates, or both have changed, clear the travel 
            // info and the route so that it doesn't reflect the old position.
            if (isAddressNew || areCoordinatesNew)
            {
                workingCopy.ClearTravelInfo();
                this.InputMap.Routes.Clear();
            }

            this.locationInEdit.Copy(workingCopy);

            var currentLocation = await this.GetCurrentLocationAsync();
            if (currentLocation != null)
            {
                if (isAddressNew ^ areCoordinatesNew)
                {
                    await LocationHelper.TryUpdateMissingLocationInfoAsync(this.locationInEdit, currentLocation);
                }
            }

            await LocationDataStore.SaveLocationDataAsync(this.Locations);

            if (currentLocation != null)
            {
                bool isNetworkAvailable = await this.TryUpdateLocationsTravelInfoAsync(this.Locations, currentLocation);
                if (isNetworkAvailable) this.InputMap.Routes.Add(new MapRouteView(this.locationInEdit.FastestRoute));
            }
        }

        /// <summary>
        /// Handles the light-dismiss of the editor flyout to cancel edit mode.
        /// </summary>
        private void Flyout_Closed(object sender, object e)
        {
            // Do nothing if the flyout is closing in order to enter map selection mode. 
            if (this.isMapSelectionEnabled) return;

            // If a new location is still in edit, then the user has light-dismissed 
            // the editor without saving. In this case, delete the new location.
            else if (this.isNewLocationInEdit)
            {
                this.isNewLocationInEdit = false;
                this.Locations.RemoveAt(this.Locations.Count - 1);
            }

            // If the user has repositioned an existing location but has not yet 
            // saved the changes, revert the position to the original one. 
            else if (this.isExistingLocationBeingRepositioned)
            {
                this.isExistingLocationBeingRepositioned = false;
                this.locationInEdit.Position = this.locationInEditOriginalPosition;
            }
        }

        /// <summary>
        /// Gets the UI element that represents the specified location; 
        /// used to access the attached editor flyout. 
        /// <param name="location">The location to edit.</param>
        /// <returns>The element that represents the location.</returns>
        private FrameworkElement GetTemplateRootForLocation(LocationData location)
        {
            var item = this.LocationsView.ContainerFromItem(location) as ListViewItem;
            return item.ContentTemplateRoot as FrameworkElement;
        }

        #endregion Editor functionality

        #region Map selection mode for repositioning a location

        /// <summary>
        /// Handles the Tapped event of the map-selection-mode display message to leave selection mode.
        /// </summary>
        private void TextBlock_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            this.DisableMapSelection();
        }

        /// <summary>
        /// Handles the Tapped event of the map control to reposition a location 
        /// when map selection mode is enabled.
        /// </summary>
        private async void InputMap_MapTapped(MapControl sender, MapInputEventArgs args)
        {
            this.InputMap.Routes.Clear();
            this.isExistingLocationBeingRepositioned = true;
            this.locationInEditOriginalPosition = this.locationInEdit.Position;
            this.locationInEdit.Position = args.Location.Position;

            var element = this.GetTemplateRootForLocation(this.locationInEdit);
            var flyout = Flyout.GetAttachedFlyout(element) as Flyout;
            var location = (flyout.Content as FrameworkElement).DataContext as LocationData;

            location.Position = args.Location.Position;
            location.Address = String.Empty;
            await LocationHelper.TryUpdateMissingLocationInfoAsync(location, null);

            this.DisableMapSelection();
            flyout.ShowAt(element);
        }

        /// <summary>
        /// Enters map selection mode, where the user can reposition the selected 
        /// location by tapping a new location on the map control. 
        /// </summary>
        private void EnableMapSelection(object sender, RoutedEventArgs e)
        {
            this.isMapSelectionEnabled = true;
            this.InputMap.MapTapped += InputMap_MapTapped;
            this.InputMap.MapHolding -= InputMap_MapHolding;
            this.LocationsView.Visibility = Visibility.Collapsed;
            this.ChangingLocationMessage.Visibility = Visibility.Visible;
            this.HideLocationsButton.IsEnabled = false;
            this.AddCurrentLocationButton.IsEnabled = false;
            //   this.AddNewLocationButton.IsEnabled = false;
            Flyout.GetAttachedFlyout(this.GetTemplateRootForLocation(this.locationInEdit)).Hide();
        }

        /// <summary>
        /// Leaves map selection mode. 
        /// </summary>
        private void DisableMapSelection()
        {
            this.isMapSelectionEnabled = false;
            this.InputMap.MapTapped -= InputMap_MapTapped;
            this.InputMap.MapHolding += InputMap_MapHolding;
            this.LocationsView.Visibility = Visibility.Visible;
            this.ChangingLocationMessage.Visibility = Visibility.Collapsed;
            this.HideLocationsButton.IsEnabled = true;
            this.AddCurrentLocationButton.IsEnabled = true;
            //    this.AddNewLocationButton.IsEnabled = true;
        }


        #endregion Map selection mode for repositioning a location

        #region TripTrak features
        private void HistoryDatePicker_Loaded(object sender, RoutedEventArgs e)
        {
            HistoryDatePicker.Date = DateTime.Now;
        }

        private void PrevDayButton_Click(object sender, RoutedEventArgs e)
        {
            HistoryDatePicker.Date = HistoryDatePicker.Date.AddDays(-1);
        }

        private void NextDayButton_Click(object sender, RoutedEventArgs e)
        {
            HistoryDatePicker.Date = HistoryDatePicker.Date.AddDays(1);
        }

        private async void ShowPolyline_Click(object sender, RoutedEventArgs e)
        {
            //remove all current polylines on map
            this.InputMap.MapElements.Clear();

            //Get points on specificed date
            var simpleGeos = App.userLocData.Where(p => p.DateCreated.Date.Date == HistoryDatePicker.Date.Date).ToList();

            await GetSelectedDatePhotoPoints();

            //Order points by DateCreated
            var simpleGeoInDateOrder = simpleGeos.OrderBy(x => x.DateCreated).ToList();

            var Coords = new List<BasicGeoposition>();

            //Query Points list to draw Polylines
            for (int i = 0; i < simpleGeoInDateOrder.Count; i++)
            {
                if (Coords.Count == 0)
                    Coords.Add(simpleGeoInDateOrder[i].Position);
                else if (simpleGeoInDateOrder[i].DateCreated - simpleGeoInDateOrder[i - 1].DateCreated < TimeSpan.FromMinutes(2))
                {
                    Coords.Add(simpleGeoInDateOrder[i].Position);
                }
                else
                {
                    this.Locations.Add(new LocationData
                    {
                        Position = simpleGeoInDateOrder[i].Position,
                        DateCreated = simpleGeoInDateOrder[i].DateCreated,
                        Name = "Polyline"
                    });
                    //define polyline
                    MapPolyline mapPolyline = new MapPolyline();
                    mapPolyline.StrokeColor = Colors.Black;
                    mapPolyline.StrokeThickness = 2;
                    mapPolyline.StrokeDashed = true;
                    mapPolyline.Path = new Geopath(Coords);

                    //draw polyline on map
                    this.InputMap.MapElements.Add(mapPolyline);

                    //Clear Coords.
                    Coords.Clear();
                }
            }
            var ResortLocations = this.Locations.OrderBy(t => t.DateCreated).ToList();
            this.Locations.Clear();
            this.MappedLocations.Clear();
            foreach (LocationData item in ResortLocations)
            {
                this.Locations.Add(item);
            }

            //draw last Polyline on map
            if (Coords.Count > 1)
            {
                MapPolyline lastPolyline = new MapPolyline();
                lastPolyline.StrokeColor = Colors.Black;
                lastPolyline.StrokeThickness = 2;
                lastPolyline.StrokeDashed = true;
                lastPolyline.Path = new Geopath(Coords);

                //draw polyline on map
                this.InputMap.MapElements.Add(lastPolyline);
            }

            await setViewOnMap(Coords);
        }

        private async void HistoryDatePicker_DateChanged(object sender, DatePickerValueChangedEventArgs e)
        {
            if (HistoryDatePicker.Date > DateTime.Now)
            {
                HistoryDatePicker.Date = DateTime.Now;
                NextDayButton.IsEnabled = false;
                return;
            }
            else if (HistoryDatePicker.Date < (DateTime.Now.AddDays(-8)))
            {
                HistoryDatePicker.Date = DateTime.Now.AddDays(-7);
                PrevDayButton.IsEnabled = false;
                return;
            }
            else
            {
                PrevDayButton.IsEnabled = true;
                NextDayButton.IsEnabled = true;
            }
            //remove all current polylines on map
            this.InputMap.MapElements.Clear();
            await GetSelectedDatePhotoPoints();
            await this.ResetViewAsync();

        }

        private async Task GetSelectedDatePhotoPoints()
        {
            this.MappedLocations.Clear();
            this.Locations.Clear();
            if (filteredLocations.Count == 0)
            {
                filteredLocations = await PhotoHelper.GetPhotoInDevice();
            }
            for (int i = 0; i < filteredLocations.Count; i++)
            {
                if (filteredLocations[i].DateCreated.Date == HistoryDatePicker.Date.Date)
                {
                    this.Locations.Add(filteredLocations[i]);
                    if (String.IsNullOrEmpty(filteredLocations[i].Address))
                        await LocationHelper.TryUpdateMissingLocationInfoAsync(filteredLocations[i], null);
                }
            }
        }

        #endregion

        #region Private method
        private void Session_Revoked(object sender, ExtendedExecutionRevokedEventArgs args)
        {
            //TODO: clean up session data
            StopLocationExtensionSession();
        }

        private void StopLocationExtensionSession()
        {
            if (session != null)
            {
                session.Dispose();
                session = null;
            }

        }


        private ExtendedExecutionSession session;

        private async void StartLocationExtensionSession()
        {
            session = new ExtendedExecutionSession();
            session.Description = "Location Tracker";
            session.Reason = ExtendedExecutionReason.LocationTracking;
            session.Revoked += Session_Revoked;
            var result = await session.RequestExtensionAsync();
            if (result == ExtendedExecutionResult.Denied)
            {
                //TODO: handle denied
            }
        }

        private async Task setViewOnMap(List<BasicGeoposition> positions)
        {
            if (positions.Count == 0)
                return;
            var bounds = GeoboundingBox.TryCompute(positions);
            double viewWidth = ApplicationView.GetForCurrentView().VisibleBounds.Width;
            var margin = new Thickness((viewWidth >= 500 ? 300 : 10), 10, 10, 10);
            bool isSuccessful = await this.InputMap.TrySetViewBoundsAsync(bounds, margin, MapAnimationKind.Default);
            if (isSuccessful && positions.Count < 2) this.InputMap.ZoomLevel = 15;
            else if (!isSuccessful && positions.Count > 0)
            {
                this.InputMap.Center = new Geopoint(positions[0]);
                this.InputMap.ZoomLevel = 15;
            }
        }
        #endregion
    }
}
