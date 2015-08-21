using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Maps;
using Windows.UI.Xaml.Media.Imaging;

namespace Helpers
{
    public static class PhotoHelper
    {

        static PhotoHelper()
        {

        }

        public static string GenerateFileName(string context)
        {
            return context + "_" + DateTime.Now.ToString("yyyyMMddHH") + "_" + Guid.NewGuid().ToString() + ".jpg";
        }

        public static async Task<List<LocationData>> GetPhotoInDevice()
        {
            List<LocationData> mapPhotoIcons = new List<LocationData>();
            IReadOnlyList<StorageFile> photos = await KnownFolders.CameraRoll.GetFilesAsync();
            for (int i = 0; i < photos.Count; i++)
            {

                Geopoint geopoint = await GeotagHelper.GetGeotagAsync(photos[i]);
                if (geopoint != null)
                {
                    //should use Thumbnail to reduce the size of images, otherwise low-end device will crashes
                    var fileStream = await photos[i].GetThumbnailAsync(ThumbnailMode.PicturesView);
                    var img = new BitmapImage();
                    img.SetSource(fileStream);


                    mapPhotoIcons.Add(new LocationData
                    {
                        Position = geopoint.Position,
                        DateCreated = photos[i].DateCreated,
                        ImageSource = img
                    });
                }
            }
            var retMapPhotos = mapPhotoIcons.OrderBy(x => x.DateCreated).ToList();

            return retMapPhotos;
        }

        public static async Task<BitmapImage> GetPhotoFromCameraLaunch()
        {
            CameraCaptureUI captureUI = new CameraCaptureUI();
            captureUI.PhotoSettings.Format = CameraCaptureUIPhotoFormat.Jpeg;
        
            StorageFile photo = await captureUI.CaptureFileAsync(CameraCaptureUIMode.Photo);

            if (photo == null)
            {
                // User cancelled photo capture
                return null;
            }
            else
            {
                IRandomAccessStream stream = await photo.OpenAsync(FileAccessMode.Read);
                string imageName = GenerateFileName("TripTrak");
                Debug.WriteLine(imageName);
                StorageFile destinationFile = await KnownFolders.CameraRoll.CreateFileAsync(imageName);
                using (var destinationStream = await destinationFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    using (var destinationOutputStream = destinationStream.GetOutputStreamAt(0))
                    {
                        await RandomAccessStream.CopyAndCloseAsync(stream, destinationStream);
                    }
                }

                var fileStream = await photo.GetThumbnailAsync(ThumbnailMode.PicturesView);

                var img = new BitmapImage();
                img.SetSource(fileStream);


                //BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                //SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();

                //SoftwareBitmap softwareBitmapBGR8 = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

                //var bitmapSource = new SoftwareBitmapSource();
                //await bitmapSource.SetBitmapAsync(softwareBitmapBGR8);

                return img;

            }
        }
    }
}
