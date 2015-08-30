using SQLitePCL;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.Storage;

namespace Helpers
{
    /// <summary>
    /// Provides access to stored location data. 
    /// </summary>
    public class LocationDatabase
    {
        public static void LoadDatabase(SQLiteConnection db)
        {
            string sql = @"CREATE TABLE IF NOT EXISTS 
                                    UserLocation (Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL
                                                  DateCreated  VARCHAR( 140 ),
                                                  Lat  VARCHAR( 140 ),
                                                  Lon  VARCHAR( 140 ),
                                                  Att  VARCHAR( 140 ) 
                                    );";
            using (var statement = db.Prepare(sql))
            {
                statement.Step();
            }
        }

        /// <summary>
        /// Return true if statement successfuls
        /// </summary>
        /// <param name="simpleGeo"></param>
        /// <param name="db"></param>
        /// <returns></returns>
        public static bool InsertLocation(SimpleGeoData simpleGeo, SQLiteConnection db)
        {
            bool ret = false;
            try
            {
                using (var stmt = db.Prepare("INSERT INTO UserLocation (DateCreated, Lat, Long, Att) VALUES (?, ?, ?)"))
                {
                    stmt.Bind(1, simpleGeo.DateCreated.ToString());
                    stmt.Bind(2, simpleGeo.Position.Latitude.ToString());
                    stmt.Bind(3, simpleGeo.Position.Longitude.ToString());
                    stmt.Bind(4, simpleGeo.Position.Altitude.ToString());
                    stmt.Step();
                }
                ret = true;
            }
            catch (Exception ex)
            {
                //TODO
            }
            return ret;
        }

        public static List<SimpleGeoData> GetAllLocation(SQLiteConnection db)
        {
            var ret = new List<SimpleGeoData>();
            using (var stmt = db.Prepare("SELECT Id, DateCreated, Lat, Long, Att FROM UserLocation"))
            {
                while(stmt.Step()== SQLiteResult.ROW)
                {
                    var item = CreateSimpleGeo(stmt);
                    ret.Add(item);
                }
            }

                return ret;
        }
        
        private static SimpleGeoData CreateSimpleGeo(ISQLiteStatement stmt)
        {
            var basicGeo = new BasicGeoposition();
            basicGeo.Latitude = (double)stmt[2];
            basicGeo.Longitude = (double)stmt[3];
            basicGeo.Altitude = (double)stmt[4];

            var ret = new SimpleGeoData();
            ret.Position = basicGeo;
            ret.DateCreated = DateTimeOffset.Parse((string)stmt[1]); 
            return ret;
        }   
    }
}
