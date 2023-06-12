using ArcGIS.Core.Data.UtilityNetwork.Trace;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using System.Net.Http.Headers;
using static System.Net.WebRequestMethods;
using System.Net;
using System.Xml.Linq;
using System.IO;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Core.Internal.CIM;
using ArcGIS.Core.CIM;
using Microsoft.Win32;
using ArcGIS.Desktop.Internal.Framework;
using System.ComponentModel;
using System.Security.Cryptography;

namespace TAGCatalogoImagens
{
    /// <summary>
    /// Interaction logic for DockpaneSearchView.xaml
    /// </summary>
    public partial class DockpaneSearchView : UserControl
    {
        private MapView mapView;
        private IDisposable _graphic;
        private List<SceneDetails> scenes;
        private CIMPolygonSymbol polygonSymbol;

        public DockpaneSearchView()
        {
            InitializeComponent();
            SliderCloudCover.Value = 100;
            SliderOffNadir.Value = 60;
            SliderResolution.Value = 15;

            mapView = MapView.Active;
            scenes = new List<SceneDetails>();

            var trans = 50.0;//semi transparent
            CIMStroke outline = SymbolFactory.Instance.ConstructStroke(CIMColor.CreateRGBColor(0, 0, 0, trans), 2.0, SimpleLineStyle.Solid);

            //Stroke for the fill
            var solid = SymbolFactory.Instance.ConstructStroke(CIMColor.CreateRGBColor(255, 0, 0, trans), 1.0, SimpleLineStyle.Solid);

            //Mimic cross hatch
            CIMFill[] diagonalCross = {
                new CIMHatchFill() {
                    Enable = true,
                    Rotation = 45.0,
                    Separation = 5.0,
                    LineSymbol = new CIMLineSymbol() { SymbolLayers = new CIMSymbolLayer[1] { solid } }
                },
                new CIMHatchFill() {
                    Enable = true,
                    Rotation = -45.0,
                    Separation = 5.0,
                    LineSymbol = new CIMLineSymbol() { SymbolLayers = new CIMSymbolLayer[1] { solid } }
                }
            };

            List<CIMSymbolLayer> symbolLayers = new List<CIMSymbolLayer> { outline };

            foreach (var fill in diagonalCross)
                symbolLayers.Add(fill);

            polygonSymbol = new CIMPolygonSymbol() { SymbolLayers = symbolLayers.ToArray() };
        }
        
        public class SceneDetails : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;

            public string sceneFeatureID { get; set; }

            private bool _sceneSelected;
            public bool sceneSelected { 
                get { return _sceneSelected; }
                set {
                    _sceneSelected = value;
                    OnPropertyChanged("sceneSelected");
                }
            }
            public string sceneSatellite { get; set; }
            public string sceneDate { get; set; }
            public double sceneResolution { get; set; }
            public double sceneCloudCover { get; set; }
            public double sceneOffNadir { get; set; }

            public ArcGIS.Core.Geometry.Geometry sceneGeometry { get; set; }

            protected void OnPropertyChanged(string name)
            {
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null)
                {
                    handler(this, new PropertyChangedEventArgs(name));
                }
            }
        }
        
        public static string CreateRandomWord
        {
            get
            {
                var randomGenerator = RandomNumberGenerator.Create(); // Compliant for security-sensitive use cases
                byte[] data = new byte[16];
                randomGenerator.GetBytes(data);
                return BitConverter.ToString(data);
            }
        }
        
        private async void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            // Limpa o mapa de camadas WMS
            var map = MapView.Active.Map;
            if (map != null)
            {
                IReadOnlyList<WMSLayer> wmsLayers = map.Layers.OfType<WMSLayer>().ToList();
                if (wmsLayers != null)
                    foreach (var wmsLayer in wmsLayers)
                        await QueuedTask.Run(() => { map.RemoveLayer(wmsLayer); });
            }

            // Identifica ID das cenas selecionadas
            string featureIDs = "";
            foreach (SceneDetails item in GridSearchResult.ItemsSource)
                if (item.sceneSelected == true)
                    featureIDs += (featureIDs == "") ? item.sceneFeatureID : "," + item.sceneFeatureID;

            // Adiciona as camadas de Imagem ao mapa
            try
            {
                var serverConnection = new CIMInternetServerConnection { 
                    URL = "https://securewatch.digitalglobe.com/mapservice/wmsaccess?connectid=" + txtConnectID.Password, 
                    User = txtUser.Text, 
                    Password = txtPass.Password 
                };
                var connection = new CIMWMSServiceConnection { ServerConnection = serverConnection, LayerName = "DigitalGlobe:Imagery" };
                
                LayerCreationParams parameters = new LayerCreationParams(connection);
                parameters.ServiceCustomParameters = new Dictionary<string, string> { { "FEATURECOLLECTION", featureIDs } };
                parameters.AutoZoomOnEmptyMap = true;
                parameters.MapMemberPosition = MapMemberPosition.AddToTop;

                await QueuedTask.Run(() =>
                {
                    var compositeLyr = LayerFactory.Instance.CreateLayer<WMSLayer>(parameters, MapView.Active.Map);
                    var wmsLayers = compositeLyr.Layers[0] as ServiceCompositeSubLayer;
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($@"Error: {ex}");
            }
        }
        
        private async void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            scenes.Clear();
            this.Dispatcher.Invoke(() => { GridSearchResult.ItemsSource = new List<SceneDetails>(); });

            var map = MapView.Active.Map;
            if (map != null)
            {
                IReadOnlyList<WMSLayer> wmsLayers = map.Layers.OfType<WMSLayer>().ToList();
                if (wmsLayers != null)
                    foreach (var wmsLayer in wmsLayers)
                        await QueuedTask.Run(() => { map.RemoveLayer(wmsLayer); });
            }
        }

        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            double cloudCover = 0;
            double offNadir = 0;
            double resolution = 0;

            this.Dispatcher.Invoke(() => { 
                GridSearchResult.ItemsSource = new List<SceneDetails>();
                GridSearchResult.IsEnabled = false;

                BtnClear.IsEnabled = false;
                BtnSave.IsEnabled = false;
                BtnSearch.IsEnabled = true;

                cloudCover = double.Parse(SliderCloudCover.Value.ToString());
                offNadir = double.Parse(SliderOffNadir.Value.ToString());
                resolution = double.Parse(SliderResolution.Value.ToString());
            });

            mapView = MapView.Active;
            if (mapView != null)
            {
                var mapExtent = GeometryEngine.Instance.Project(mapView.Extent, SpatialReferences.WGS84);
                var bbox = mapExtent.Extent.YMin.ToString().Replace(",", ".") + "," + mapExtent.Extent.XMin.ToString().Replace(",", ".") + "," +
                              mapExtent.Extent.YMax.ToString().Replace(",", ".") + "," + mapExtent.Extent.XMax.ToString().Replace(",", ".");

                var serviceUrl = "https://securewatch.digitalglobe.com/catalogservice/wfsaccess";
                string authenticationToken = System.Convert.ToBase64String(Encoding.GetEncoding("ISO-8859-1").GetBytes(txtUser.Text + ":" + txtPass.Password));

                var queryParameters = new Dictionary<string, string>() {
                    { "REQUEST", "GetFeature" },
                    { "TYPENAME", "DigitalGlobe:FinishedFeature" },
                    { "SERVICE", "WFS" },
                    { "VERSION", "1.1.0" },
                    { "CONNECTID", txtConnectID.Password },
                    { "SRSNAME", "EPSG:4326" },
                    { "FEATUREPROFILE", "Default_Profile" },
                    { "WIDTH", "3000" },
                    { "HEIGHT", "3000" },
                    { "CQL_FILTER", "BBOX(geometry," + bbox + ")AND(cloudCover<" + SliderCloudCover.Value + ")" },
                    { "OUTPUTFORMAT", "json" },
                    { CreateRandomWord, "" }
                };
                var encodedContent = new FormUrlEncodedContent(queryParameters);
                var queryResult = string.Empty;

                using(HttpClient httpclient = new HttpClient())
                {
                    httpclient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authenticationToken);

                    HttpResponseMessage httpResponse = await httpclient.PostAsync(serviceUrl, encodedContent);
                    try
                    {
                        httpResponse.EnsureSuccessStatusCode();

                        if (httpResponse.Content is object && httpResponse.Content.Headers.ContentType.MediaType == "application/json")
                        {
                            var contentStream = await httpResponse.Content.ReadAsStreamAsync();

                            using var streamReader = new StreamReader(contentStream);
                            using var jsonReader = new JsonTextReader(streamReader);

                            JsonSerializer serializer = new JsonSerializer();

                            var resultJSON = JObject.Parse(serializer.Deserialize(jsonReader).ToString());

                            foreach (var feature in resultJSON["features"])
                            {
                                var scene = new SceneDetails();
                                scene.sceneFeatureID = (string)feature["id"];
                                scene.sceneSatellite = (string)feature["properties"]["source"];
                                scene.sceneDate = (string)feature["properties"]["acquisitionDate"];
                                scene.sceneResolution = (double)feature["properties"]["groundSampleDistance"];
                                scene.sceneCloudCover = (double)feature["properties"]["cloudCover"];
                                scene.sceneOffNadir = (double)feature["properties"]["offNadirAngle"];

                                if (scene.sceneCloudCover <= cloudCover && scene.sceneOffNadir <= offNadir && scene.sceneResolution <= resolution)
                                    scene.sceneSelected = false;

                                List<Coordinate2D> coords = new List<Coordinate2D>();
                                foreach (var coordinates in feature["geometry"]["coordinates"])
                                    foreach (var coordinate in coordinates)
                                        coords.Add(new Coordinate2D((double)coordinate[0], (double)coordinate[1]));

                                ArcGIS.Core.Geometry.Polygon polygon = PolygonBuilderEx.CreatePolygon(coords, SpatialReferences.WGS84);
                                scene.sceneGeometry = polygon;

                                scenes.Add(scene);
                            }
                        }
                    } catch (Exception ex)
                    {
                        MessageBox.Show("Serviço MAXAR Secure Watch está indisponível.\nTente novamente em alguns minutos");
                    }
                }

                this.Dispatcher.Invoke(() => { 
                    GridSearchResult.ItemsSource = scenes;
                    GridSearchResult.IsEnabled = true;

                    BtnClear.IsEnabled = true;
                    BtnSearch.IsEnabled = true;
                    BtnLoad.IsEnabled = true;
                    BtnSave.IsEnabled = true;
                });
            }
            else
            {
                MessageBox.Show("Abra o mapa e posicione a visualização na área de interesse para busca de imagens");
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            Stream stream;
            SaveFileDialog saveFileDialog1 = new SaveFileDialog();

            saveFileDialog1.Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*";
            saveFileDialog1.FilterIndex = 2;
            saveFileDialog1.RestoreDirectory = true;

            if (saveFileDialog1.ShowDialog() == true)
            {   
                if ((stream = saveFileDialog1.OpenFile()) != null)
                {
                    stream.Write(Encoding.ASCII.GetBytes("SATELITE,DATA,RESOLUCAO,COBERTURA NUVEM,OFF NADIR\n"));

                    foreach (SceneDetails scene in scenes)
                        stream.Write(Encoding.ASCII.GetBytes(
                            scene.sceneSatellite + "," +
                            scene.sceneDate + "," +
                            scene.sceneResolution + "," +
                            scene.sceneCloudCover + "," +
                            scene.sceneOffNadir +
                            "\n"
                        ));

                    stream.Close();
                }
            }
        }
    }
}
