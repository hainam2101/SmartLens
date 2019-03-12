﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.UI.Xaml.Controls;

namespace SmartLens
{
    public sealed partial class HomePage : Page
    {
        public static event EventHandler<WeatherData> WeatherDataGenarated;
        public static HomePage ThisPage { get; private set; }
        Weather.Root WeatherInfo = new Weather.Root();
        Position.RootObject jp = new Position.RootObject();

        public HomePage()
        {
            InitializeComponent();
            ThisPage = this;
            OnFirstLoad();
        }

        public async void OnFirstLoad()
        {
            Geoposition Position;
            try
            {
                Position = await GetPositionAsync();
            }
            catch (InvalidOperationException)
            {
                WeatherCtr.Error(ErrorReason.Location);
                return;
            }
            catch (Exception)
            {
                WeatherCtr.Error(ErrorReason.NetWork);
                return;
            }

            float lat = (float)Position.Coordinate.Point.Position.Latitude;
            float lon = (float)Position.Coordinate.Point.Position.Longitude;
            string URL = "http://api.map.baidu.com/geocoder/v2/?location=" + lat + "," + lon + "&output=json&ak=qrTMQKoNdBj3H6N7ZTdIbRnbBOQjcDGQ";
            string Result = await GetInfoAsync(URL);
            if (Result != "")
            {
                await Task.Run(async() =>
                {
                    WeatherInfo = await GetWeatherWithCityAsync(GetDistrictByAnalysisJSON(Result));
                    if (WeatherInfo == null)
                    {
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            WeatherCtr.Error(ErrorReason.APIError);
                        });
                        return;
                    }
                    if (WeatherInfo.status == 200)
                    {
                        WeatherDataGenarated?.Invoke(null, new WeatherData(WeatherInfo.data, jp.result.addressComponent.city + jp.result.addressComponent.district));
                    }
                    else
                    {
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            WeatherCtr.Error(ErrorReason.APIError);
                        });
                    }
                });
            }
        }

        private async Task<string> GetInfoAsync(string url)
        {
            string strBuff = "";
            Uri HttpURL = new Uri(url);
            HttpWebRequest httpReq = (HttpWebRequest)WebRequest.Create(HttpURL);
            try
            {
                HttpWebResponse httpResp = (HttpWebResponse)await httpReq.GetResponseAsync();
                Stream respStream = httpResp.GetResponseStream();
                StreamReader respStreamReader = new StreamReader(respStream, Encoding.UTF8);
                strBuff = respStreamReader.ReadToEnd();
            }
            catch (Exception)
            {
                WeatherCtr.Error(ErrorReason.NetWork);
            }
            return strBuff;
        }



        private string GetDistrictByAnalysisJSON(string JSON)
        {
            jp = JsonConvert.DeserializeObject<Position.RootObject>(JSON);
            return jp.result.addressComponent.district;
        }

        private async Task<Geoposition> GetPositionAsync()
        {
            var accessStatus = await Geolocator.RequestAccessAsync();
            if (accessStatus != GeolocationAccessStatus.Allowed)
            {
                throw new InvalidOperationException();
            }
            var geolocator = new Geolocator
            {
                DesiredAccuracyInMeters = 100
            };
            var position = await geolocator.GetGeopositionAsync();
            return position;
        }

        private async Task<Weather.Root> GetWeatherWithCityAsync(string City)
        {
            string URL = null;
            var Jarray = JArray.Parse(File.ReadAllText("Weather/CityCode.json"));

            try
            {
                for (int i = 0; i < Jarray.Count; i++)
                {
                    if (Jarray[i].Last.First.ToString() == City)
                    {
                        URL = "http://t.weather.sojson.com/api/weather/city/" + Jarray[i].Last.Previous.First;
                    }
                }

                string temp = await GetInfoAsync(URL);
                Weather.Root js = new Weather.Root();
                if (temp != "")
                {
                    js = JsonConvert.DeserializeObject<Weather.Root>(temp);
                }
                return js;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
