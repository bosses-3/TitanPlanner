using System;
using System.Configuration;
using System.IO;
using System.Xml.Linq;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Threading.Tasks;
using log4net;
using SharpCompress.Compressors.Xz;

namespace MissionPlanner.Utilities
{
    public static class ParameterMetaDataRepositoryAPMpdef
    {
        private static readonly ILog log =
            LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static Dictionary<string,XDocument> _parameterMetaDataXML = new Dictionary<string, XDocument>();

        // Per-vehicle name -> XElement lookup so GetParameterMetaData doesn't re-walk the whole XML tree on every call
        private static Dictionary<string, Dictionary<string, XElement>> _parameterIndex = new Dictionary<string, Dictionary<string, XElement>>();

        private static string[] vehicles = new[]
        {
             "SITL", "AP_Periph", "ArduSub", "Rover", "ArduCopter",
            "ArduPlane", "AntennaTracker", "Blimp", "Heli"      
        };

        private static string[] vehicles_versioned = new[] 
        {
            "Copter", "Plane", "Rover", "Sub", "Tracker"
        };

        static string url = "https://autotest.ardupilot.org/Parameters/{0}/apm.pdef.xml.gz";

        static string urlversioned = "https://autotest.ardupilot.org/Parameters/versioned/{0}/stable-{1}/apm.pdef.xml";

        static ParameterMetaDataRepositoryAPMpdef()
        {
            GetMetaData();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ParameterMetaDataRepository"/> class.
        /// </summary>
        public static void CheckLoad(string vehicle = "")
        {
            if (!_parameterMetaDataXML.ContainsKey(vehicle))
                Reload(vehicle);
        }

        public static async Task GetMetaDataVersioned(Version version)
        {
            List<Task> tlist = new List<Task>();

            vehicles_versioned.ForEach(a =>
            {
                try
                {
                    var newurl = String.Format(urlversioned, a, version.ToString());
                    var file = Path.Combine(Settings.GetDataDirectory(), a + version.ToString() + ".apm.pdef.xml");
                    if (File.Exists(file))
                        if (new FileInfo(file).LastWriteTime.AddDays(7) > DateTime.Now)
                            return;
                    var dltask = Download.getFilefromNetAsync(newurl, file);
                    tlist.Add(dltask);
                }
                catch (Exception ex) { log.Error(ex); }
            });

            await Task.WhenAll(tlist);

            vehicles_versioned.ForEach(a =>
            {
                try
                {
                    Reload(a + version.ToString());

                    var veh = vehicles.First(b => b.Contains(a));

                    if (_parameterMetaDataXML.ContainsKey(a + version.ToString()))
                    {
                        _parameterMetaDataXML[veh] = _parameterMetaDataXML[a + version.ToString()];
                        if (_parameterIndex.ContainsKey(a + version.ToString()))
                            _parameterIndex[veh] = _parameterIndex[a + version.ToString()];
                    }
                }
                catch (Exception ex) { log.Error(ex); }
            });
        }

        public static async Task GetMetaData(bool force = false)
        {
            List<Task> tlist = new List<Task>();

            vehicles.ForEach(a =>
            {
                try
                {
                    var newurl = String.Format(url, a);
                    // try the gzipped version first
                    var file = Path.Combine(Settings.GetDataDirectory(), a + ".apm.pdef.xml.gz");
                    if(File.Exists(file))
                        if (new FileInfo(file).LastWriteTime.AddDays(7) > DateTime.Now && !force)
                            return;
                    // try just the xml
                    var file2 = Path.Combine(Settings.GetDataDirectory(), a + ".apm.pdef.xml");
                    if (File.Exists(file2))
                        if (new FileInfo(file2).LastWriteTime.AddDays(7) > DateTime.Now && !force)
                            return;
                    var dltask = Download.getFilefromNetAsync(newurl, file);
                    tlist.Add(dltask);
                }
                catch (Exception ex) { log.Error(ex); }
            });

            await Task.WhenAll(tlist);

            vehicles.ForEach(a =>
            {
                try
                {
                    var fileout = Path.Combine(Settings.GetDataDirectory(), a + ".apm.pdef.xml");
                    var fileouttemp = Path.Combine(Path.GetTempFileName());
                    var file = Path.Combine(Settings.GetDataDirectory(), a + ".apm.pdef.xml.gz");
                    if (File.Exists(file))
                    {
                        // drop out to prevent unnessary fileio at startup
                        if (File.Exists(fileout) && new FileInfo(fileout).LastWriteTime.AddDays(7) > DateTime.Now && !force)
                            return;
                        using (var read = File.OpenRead(file))
                        {
                            //if (XZStream.IsXZStream(read))
                            {
                                read.Position = 0;
                                var stream = new GZipStream(read, CompressionMode.Decompress);
                                //var stream = new XZStream(read);
                                using (var outst = File.Open(fileouttemp, FileMode.Create))
                                {
                                    stream.CopyTo(outst);
                                }
                                // move after good decompress
                                File.Delete(fileout);
                                File.Move(fileouttemp, fileout);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                }
            });

            Reset();
        }

        public static void Reset()
        {
            _parameterMetaDataXML.Clear();
            _parameterIndex.Clear();
        }

        public static void Reload(string vehicle = "")
        {
            string paramMetaDataXMLFileName =
                String.Format("{0}{1}", Settings.GetDataDirectory(), vehicle + ".apm.pdef.xml");

            try
            {
                if (File.Exists(paramMetaDataXMLFileName))
                {
                    var doc = XDocument.Load(paramMetaDataXMLFileName);
                    _parameterMetaDataXML[vehicle] = doc;
                    _parameterIndex[vehicle] = BuildIndex(doc);
                }

            }
            catch (System.Xml.XmlException ex) 
            {
                try
                {
                    if (File.Exists(paramMetaDataXMLFileName))
                        File.Delete(paramMetaDataXMLFileName);
                }
                catch { }
                log.Error(paramMetaDataXMLFileName);
                log.Error(ex);
            }
            catch (Exception ex)
            {
                log.Error(paramMetaDataXMLFileName);
                log.Error(ex);
            }
        }

        // Build a flat name -> XElement index for the document so per-param lookups are O(1)
        private static Dictionary<string, XElement> BuildIndex(XDocument doc)
        {
            var index = new Dictionary<string, XElement>(StringComparer.Ordinal);
            try
            {
                var root = doc.Element("paramfile");
                if (root == null)
                    return index;

                foreach (var paramfile in root.Elements())
                {
                    foreach (var parameters in paramfile.Elements())
                    {
                        if (!parameters.HasAttributes)
                            continue;
                        foreach (var param in parameters.Elements())
                        {
                            var name = param.Attribute("name")?.Value;
                            if (string.IsNullOrEmpty(name))
                                continue;
                            // Preserve old "first match wins" behaviour
                            if (!index.ContainsKey(name))
                                index[name] = param;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error("BuildIndex error", ex);
            }
            return index;
        }

        /// <summary>
        /// Gets the parameter meta data.
        /// </summary>
        /// <param name="nodeKey">The node key.</param>
        /// <param name="metaKey">The meta key.</param>
        /// <returns></returns>
        public static string GetParameterMetaData(string nodeKey, string metaKey, string vechileType)
        {
            // remap names
            if (vechileType == "ArduCopter2")
                vechileType = "ArduCopter";
            if (vechileType == "ArduRover")
                vechileType = "Rover";
            if (vechileType == "ArduTracker")
                vechileType = "AntennaTracker";

            CheckLoad(vechileType);

            // remap keys
            if (metaKey == ParameterMetaDataConstants.DisplayName)
                metaKey = "humanName";
            if (metaKey == ParameterMetaDataConstants.Description)
                metaKey = "documentation";
            if (metaKey == ParameterMetaDataConstants.User)
                metaKey = "user";

            if (!_parameterIndex.TryGetValue(vechileType, out var index))
                return string.Empty;

            try
            {
                var vechileKey = vechileType + ":" + nodeKey;
                XElement param;
                if (!index.TryGetValue(vechileKey, out param))
                    if (!index.TryGetValue(nodeKey, out param))
                        return string.Empty;

                var attr = param.Attribute(metaKey);
                if (attr != null)
                    return attr.Value;

                if (metaKey == ParameterMetaDataConstants.Values)
                {
                    var ans = "";
                    param.Elements("values").Elements().ForEach(a =>
                    {
                        if (a.Name == "value")
                        {
                            var code = a.Attribute("code");
                            var value = a.Value.ToString();
                            ans += String.Format("{0}:{1},", code.Value, value);
                        }
                    });
                    return ans;
                }

                foreach (var xElement in param.Elements())
                {
                    if (xElement.Name == "field")
                    {
                        var name = xElement.Attribute("name");
                        if (name != null && name.Value == metaKey)
                        {
                            return xElement.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }

            return string.Empty;
        }
    }
}