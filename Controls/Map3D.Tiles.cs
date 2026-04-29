using GMap.NET;
using GMap.NET.Internals;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using Microsoft.Scripting.Utils;
using MissionPlanner.Utilities;
using OpenTK;
using MPMathHelper = MissionPlanner.Utilities.MathHelper;
using OpenTK.Graphics;
using OpenTK.Graphics.ES20;
using OpenTK.Platform;
using System;
using System.Collections.Concurrent;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace MissionPlanner.Controls
{
    public partial class Map3D
    {
        #region Tile Fields

        ConcurrentDictionary<GPoint, tileInfo> textureid = new ConcurrentDictionary<GPoint, tileInfo>();
        GMap.NET.Internals.Core core = new GMap.NET.Internals.Core();
        private GMapProvider type;
        private PureProjection prj;
        private List<tileZoomArea> tileArea = new List<tileZoomArea>();
        private readonly object _tileAreaLock = new object();
        private bool _forceRefreshTiles;
        private bool sizeChanged;
        private Thread _imageloaderThread;
        private OpenTK.GameWindow _imageLoaderWindow;
        private IGraphicsContext IMGContext;

        #endregion

        #region Tile Constants

        private const double LOD_NEAR_DISTANCE = 500;   // meters - use max zoom within this distance
        private const double LOD_FAR_DISTANCE = 8000;   // meters - use min zoom beyond this distance

        #endregion

        #region Texture Generation Methods

        static int generateTexture(Bitmap image)
        {
            image.MakeTransparent();
            BitmapData data = image.LockBits(new System.Drawing.Rectangle(0, 0, image.Width, image.Height),
                ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            ConvertColorSpace(data);
            int texture = CreateTexture(data);
            image.UnlockBits(data);
            if (texture == 0)
            {
                image.Dispose();
                var error = GL.GetError();
            }
            else
            {
                image.Dispose();
            }

            return texture;
        }

        static void ConvertColorSpace(BitmapData _data)
        {
            // bgra to rgba
            var x = 0; var y = 0; var width = _data.Width; var height = _data.Height;
            for (y = 0; y < height; y++)
            {
                for (x = width - 1; x >= 0; x -= 1)
                {
                    var offset = y * _data.Stride + x * 4;
                    Marshal.WriteInt32(_data.Scan0, offset, ColorBGRA2RGBA(Marshal.ReadInt32(_data.Scan0, offset)));
                }
            }
        }

        static int CreateTexture(BitmapData data)
        {
            int texture = 0;
            GL.GenTextures(1, out texture);
            if (texture == 0)
            {
                return 0;
            }

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget2d.Texture2D, 0, TextureComponentCount.Rgba, data.Width, data.Height, 0, OpenTK.Graphics.ES20.PixelFormat.Rgba, PixelType.UnsignedByte, data.Scan0);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            if (isPowerOf2(data.Width) && isPowerOf2(data.Height))
            {
                GL.GenerateMipmap(TextureTarget.Texture2D);
            }
            else
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            }
            return texture;
        }

        static bool isPowerOf2(int value)
        {
            return (value & (value - 1)) == 0;
        }

        static int FloorPowerOf2(int value)
        {
            var ans = Math.Log(value, 2);
            return (int)Math.Pow(2, Math.Floor(ans));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int ColorBGRA2RGBA(int x)
        {
            unchecked
            {
                return
                    (int)((x & 0xFF000000) >> 0) |
                    ((x & 0x00FF0000) >> 16) |
                    ((x & 0x0000FF00) << 0) |
                    ((x & 0x000000FF) << 16);
            }
        }

        #endregion

        #region Tile Loading Thread

        void imageLoader()
        {
            core.Zoom = minzoom;
            GMaps.Instance.CacheOnIdleRead = false;
            GMaps.Instance.BoostCacheEngine = true;
            GMaps.Instance.MemoryCache.Capacity = 500;
            _imageLoaderWindow = new OpenTK.GameWindow(640, 480, Context.GraphicsMode);
            _imageLoaderWindow.Visible = false;
            IMGContext = _imageLoaderWindow.Context;
            core.Zoom = 20;

            while (!_stopRequested && !this.IsDisposed)
            {
                if (sizeChanged)
                {
                    sizeChanged = false;
                    core.OnMapSizeChanged(1000, 1000);
                }

                if (_forceRefreshTiles || _center.GetDistance(core.Position) > 30)
                {
                    _forceRefreshTiles = false;
                    core.Position = _center;
                }

                if (DateTime.Now.Second % 3 == 1 && tileArea != null)
                    lock (_tileAreaLock)
                        CleanupOldTextures(tileArea);

                if (core.tileLoadQueue.Count > 0)
                {
                    System.Threading.Thread.Sleep(100);
                    continue;
                }

                if (core.FailedLoads.Count > 0)
                {
                }

                generateTextures();

                System.Threading.Thread.Sleep(100);
            }
        }

        #endregion

        #region Tile Generation

        private void generateTextures()
        {
            core.fillEmptyTiles = false;
            core.LevelsKeepInMemmory = 10;
            core.Provider = type;

            var cameraPos = new utmpos(utmcenter[0] + cameraX, utmcenter[1] + cameraY, utmzone).ToLLA();

            lock (_tileAreaLock)
            {
                tileArea = new List<tileZoomArea>();

                var allTasks = new List<(LoadTask task, int zoomLevel, double dist)>();

                int altitudeZoomAdjust = (_center.Alt >= 500) ? 1 : 0;
                int effectiveMaxZoom = Math.Max(minzoom, zoom - altitudeZoomAdjust);

                for (int z = effectiveMaxZoom; z >= minzoom; z--)
                {
                    double innerDist = (z == effectiveMaxZoom) ? 0 : GetDistanceForZoom(z + 1);
                    double outerDist = GetDistanceForZoom(z);

                    var area = new RectLatLng(cameraPos.Lat, cameraPos.Lng, 0, 0);
                    var offset = cameraPos.newpos(45, outerDist);
                    area.Inflate(Math.Abs(cameraPos.Lat - offset.Lat) * 1.2, Math.Abs(cameraPos.Lng - offset.Lng) * 1.2);

                    int extraTiles = (z == minzoom) ? 4 : 0;
                    var tiles = new tileZoomArea
                    {
                        zoom = z,
                        points = prj.GetAreaTileList(area, z, extraTiles),
                        area = area
                    };
                    tileArea.Add(tiles);

                    foreach (var p in tiles.points)
                    {
                        LoadTask task = new LoadTask(p, z);
                        if (!core.tileLoadQueue.Contains(task))
                        {
                            long tileCenterPxX = (p.X * prj.TileSize.Width) + (prj.TileSize.Width / 2);
                            long tileCenterPxY = (p.Y * prj.TileSize.Height) + (prj.TileSize.Height / 2);
                            var tileCenter = prj.FromPixelToLatLng(tileCenterPxX, tileCenterPxY, z);
                            double dist = cameraPos.GetDistance(new PointLatLngAlt(tileCenter.Lat, tileCenter.Lng));

                            allTasks.Add((task, z, dist));
                        }
                    }
                }

                allTasks.Sort((a, b) =>
                {
                    double priorityA = a.dist / (a.zoomLevel + 1);
                    double priorityB = b.dist / (b.zoomLevel + 1);
                    return priorityB.CompareTo(priorityA);
                });

                foreach (var t in allTasks)
                {
                    core.tileLoadQueue.Push(t.task);
                }

                var totaltiles = allTasks.Count;
                Console.Write(DateTime.Now.Millisecond + " LOD tiles " + totaltiles + "   \r");

                if (DateTime.Now.Second % 3 == 1)
                    CleanupOldTextures(tileArea);
            }

            var C = 2 * Math.PI * 6378137.000;
            var stile = C * Math.Cos(cameraPos.Lat) / Math.Pow(2, zoom);
            var pxstep = 2;

            tileZoomArea[] talist;
            lock (_tileAreaLock)
                talist = tileArea.ToArray();
            foreach (var tilearea in talist)
            {
                stile = C * Math.Cos(cameraPos.Lat) / Math.Pow(2, tilearea.zoom);
                pxstep = (int)(stile / 45);
                pxstep = FloorPowerOf2(pxstep);
                if (pxstep == int.MinValue) pxstep = 0;
                if (pxstep == 0)
                    pxstep = 1;
                foreach (var p in tilearea.points)
                {
                    if (textureid.ContainsKey(p))
                        continue;

                    long xstart = p.X * prj.TileSize.Width;
                    long ystart = p.Y * prj.TileSize.Width;
                    long xend = (p.X + 1) * prj.TileSize.Width;
                    long yend = (p.Y + 1) * prj.TileSize.Width;

                    int gridWidth = (int)((xend - xstart) / pxstep) + 1;
                    int gridHeight = (int)((yend - ystart) / pxstep) + 1;

                    Map3DTileCache.CachedTileData cachedTile = null;
                    if (_diskCacheTiles)
                    {
                        try
                        {
                            cachedTile = Map3DTileCache.LoadTileOrBetter(p.X, p.Y, tilearea.zoom, zoom);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to load tile from cache: {ex.Message}");
                            cachedTile = null;
                        }
                    }

                    if (cachedTile != null && cachedTile.Zoom == tilearea.zoom &&
                        cachedTile.GridWidth == gridWidth && cachedTile.GridHeight == gridHeight &&
                        cachedTile.PxStep == pxstep &&
                        cachedTile.Altitudes != null && cachedTile.Altitudes.Length == gridWidth * gridHeight &&
                        cachedTile.ImageData != null && cachedTile.ImageData.Length > 0)
                    {
                        try
                        {
                            Image cachedImage;
                            using (var ms = new MemoryStream(cachedTile.ImageData))
                            using (var tempImage = Image.FromStream(ms))
                            {
                                cachedImage = new Bitmap(tempImage);
                            }

                            var ti = new tileInfo(Context, this.WindowInfo, textureSemaphore)
                            {
                                point = p,
                                zoom = tilearea.zoom,
                                img = cachedImage
                            };

                            var latlngGrid = new PointLatLng[gridWidth, gridHeight];
                            for (int gx = 0; gx < gridWidth; gx++)
                            {
                                long px = xstart + gx * pxstep;
                                for (int gy = 0; gy < gridHeight; gy++)
                                {
                                    long py = ystart + gy * pxstep;
                                    latlngGrid[gx, gy] = prj.FromPixelToLatLng(px, py, tilearea.zoom);
                                }
                            }

                            var utmCache = new double[gridWidth, gridHeight][];
                            for (int gx = 0; gx < gridWidth; gx++)
                            {
                                for (int gy = 0; gy < gridHeight; gy++)
                                {
                                    utmCache[gx, gy] = convertCoords(latlngGrid[gx, gy]);
                                    utmCache[gx, gy][2] = cachedTile.Altitudes[gx * gridHeight + gy];
                                }
                            }

                            var zindexmod = (20 - ti.zoom) * 1.0;
                            for (int gx = 0; gx < gridWidth - 1; gx++)
                            {
                                long x = xstart + gx * pxstep;
                                long xnext = x + pxstep;
                                for (int gy = 0; gy < gridHeight - 1; gy++)
                                {
                                    long y = ystart + gy * pxstep;
                                    long ynext = y + pxstep;

                                    var utm1 = utmCache[gx, gy];
                                    var utm2 = utmCache[gx, gy + 1];
                                    var utm3 = utmCache[gx + 1, gy];
                                    var utm4 = utmCache[gx + 1, gy + 1];

                                    var imgx = MPMathHelper.map(xnext, xstart, xend, 0, 1);
                                    var imgy = MPMathHelper.map(ynext, ystart, yend, 0, 1);
                                    ti.vertex.Add(new Vertex(utm4[0], utm4[1], utm4[2] - zindexmod, 1, 0, 0, 1, imgx, imgy));
                                    imgx = MPMathHelper.map(xnext, xstart, xend, 0, 1);
                                    imgy = MPMathHelper.map(y, ystart, yend, 0, 1);
                                    ti.vertex.Add(new Vertex(utm3[0], utm3[1], utm3[2] - zindexmod, 0, 1, 0, 1, imgx, imgy));
                                    imgx = MPMathHelper.map(x, xstart, xend, 0, 1);
                                    imgy = MPMathHelper.map(y, ystart, yend, 0, 1);
                                    ti.vertex.Add(new Vertex(utm1[0], utm1[1], utm1[2] - zindexmod, 0, 0, 1, 1, imgx, imgy));
                                    imgx = MPMathHelper.map(x, xstart, xend, 0, 1);
                                    imgy = MPMathHelper.map(ynext, ystart, yend, 0, 1);
                                    ti.vertex.Add(new Vertex(utm2[0], utm2[1], utm2[2] - zindexmod, 1, 1, 0, 1, imgx, imgy));
                                    var startindex = (uint)ti.vertex.Count - 4;
                                    ti.indices.AddRange(new[]
                                    {
                                        startindex + 0, startindex + 1, startindex + 3,
                                        startindex + 1, startindex + 2, startindex + 3
                                    });
                                }
                            }

                            try
                            {
                                var temp2 = ti.idEBO;
                                var temp3 = ti.idVBO;
                            }
                            catch
                            {
                                continue;
                            }

                            textureid[p] = ti;
                            continue;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to load tile from cache: {ex.Message}");
                        }
                    }

                    core.tileDrawingListLock.AcquireReaderLock();
                    core.Matrix.EnterReadLock();
                    try
                    {
                        GMap.NET.Internals.Tile t = core.Matrix.GetTileWithNoLock(tilearea.zoom, p);
                        if (t.NotEmpty)
                        {
                            foreach (var imgPI in t.Overlays)
                            {
                                var img = (GMapImage) imgPI;
                                if (!textureid.ContainsKey(p))
                                {
                                    try
                                    {
                                        var ti = new tileInfo(Context, this.WindowInfo, textureSemaphore)
                                        {
                                            point = p,
                                            zoom = tilearea.zoom,
                                            img = (Image) img.Img.Clone()
                                        };

                                        var latlngGrid = new PointLatLng[gridWidth, gridHeight];
                                        var altCache = new double[gridWidth, gridHeight];
                                        var utmCache = new double[gridWidth, gridHeight][];
                                        bool hasInvalidAlt = false;

                                        for (int gx = 0; gx < gridWidth && !hasInvalidAlt; gx++)
                                        {
                                            long px = xstart + gx * pxstep;
                                            for (int gy = 0; gy < gridHeight; gy++)
                                            {
                                                long py = ystart + gy * pxstep;
                                                latlngGrid[gx, gy] = prj.FromPixelToLatLng(px, py, tilearea.zoom);
                                            }
                                        }

                                        for (int gx = 0; gx < gridWidth && !hasInvalidAlt; gx++)
                                        {
                                            for (int gy = 0; gy < gridHeight; gy++)
                                            {
                                                var latlng = latlngGrid[gx, gy];
                                                var altResult = srtm.getAltitudeFast(latlng.Lat, latlng.Lng);
                                                if (altResult.currenttype == srtm.tiletype.invalid)
                                                {
                                                    hasInvalidAlt = true;
                                                    break;
                                                }
                                                altCache[gx, gy] = altResult.alt;
                                                utmCache[gx, gy] = convertCoords(latlng);
                                                utmCache[gx, gy][2] = altResult.alt;
                                            }
                                        }

                                        if (hasInvalidAlt)
                                        {
                                            ti = null;
                                        }
                                        else
                                        {
                                            var zindexmod = (20 - ti.zoom) * 1.0;
                                            for (int gx = 0; gx < gridWidth - 1; gx++)
                                            {
                                                long x = xstart + gx * pxstep;
                                                long xnext = x + pxstep;
                                                for (int gy = 0; gy < gridHeight - 1; gy++)
                                                {
                                                    long y = ystart + gy * pxstep;
                                                    long ynext = y + pxstep;

                                                    var utm1 = utmCache[gx, gy];
                                                    var utm2 = utmCache[gx, gy + 1];
                                                    var utm3 = utmCache[gx + 1, gy];
                                                    var utm4 = utmCache[gx + 1, gy + 1];

                                                    var imgx = MPMathHelper.map(xnext, xstart, xend, 0, 1);
                                                    var imgy = MPMathHelper.map(ynext, ystart, yend, 0, 1);
                                                    ti.vertex.Add(new Vertex(utm4[0], utm4[1], utm4[2] - zindexmod, 1, 0, 0, 1, imgx, imgy));
                                                    imgx = MPMathHelper.map(xnext, xstart, xend, 0, 1);
                                                    imgy = MPMathHelper.map(y, ystart, yend, 0, 1);
                                                    ti.vertex.Add(new Vertex(utm3[0], utm3[1], utm3[2] - zindexmod, 0, 1, 0, 1, imgx, imgy));
                                                    imgx = MPMathHelper.map(x, xstart, xend, 0, 1);
                                                    imgy = MPMathHelper.map(y, ystart, yend, 0, 1);
                                                    ti.vertex.Add(new Vertex(utm1[0], utm1[1], utm1[2] - zindexmod, 0, 0, 1, 1, imgx, imgy));
                                                    imgx = MPMathHelper.map(x, xstart, xend, 0, 1);
                                                    imgy = MPMathHelper.map(ynext, ystart, yend, 0, 1);
                                                    ti.vertex.Add(new Vertex(utm2[0], utm2[1], utm2[2] - zindexmod, 1, 1, 0, 1, imgx, imgy));
                                                    var startindex = (uint)ti.vertex.Count - 4;
                                                    ti.indices.AddRange(new[]
                                                    {
                                                        startindex + 0, startindex + 1, startindex + 3,
                                                        startindex + 1, startindex + 2, startindex + 3
                                                    });
                                                }
                                            }

                                            if (_diskCacheTiles)
                                            {
                                                try
                                                {
                                                    var imgClone = (Image)img.Img.Clone();
                                                    var altCacheClone = (double[,])altCache.Clone();
                                                    var tileX = p.X;
                                                    var tileY = p.Y;
                                                    var tileZoom = tilearea.zoom;
                                                    ThreadPool.QueueUserWorkItem(_ =>
                                                    {
                                                        try
                                                        {
                                                            Map3DTileCache.SaveTile(tileX, tileY, tileZoom,
                                                                gridWidth, gridHeight, pxstep, altCacheClone, imgClone);
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            Debug.WriteLine($"Failed to save tile to cache: {ex.Message}");
                                                        }
                                                        finally
                                                        {
                                                            imgClone?.Dispose();
                                                        }
                                                    });
                                                }
                                                catch (Exception ex)
                                                {
                                                    Debug.WriteLine($"Failed to queue tile cache save: {ex.Message}");
                                                }
                                            }
                                        }

                                        if (ti != null)
                                        {
                                            try
                                            {
                                                var temp2 = ti.idEBO;
                                                var temp3 = ti.idVBO;
                                            }
                                            catch
                                            {
                                                return;
                                            }

                                            textureid[p] = ti;
                                        }
                                    }
                                    catch
                                    {
                                        return;
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        core.Matrix.LeaveReadLock();
                        core.tileDrawingListLock.ReleaseReaderLock();
                    }
                }
            }
        }

        private void Minimumtile(List<tileZoomArea> tileArea)
        {
            foreach (tileZoomArea tileZoomArea in tileArea.Reverse<tileZoomArea>())
            {
                foreach (var pnt in tileZoomArea.points)
                {
                    var dx = pnt.X / 2.0;
                    var dy = pnt.Y / 2.0;

                    var zoomup = new GPoint(pnt.X / 2, pnt.Y / 2);

                    var pixel = core.Provider.Projection.FromTileXYToPixel(pnt);
                    var pixelup = core.Provider.Projection.FromTileXYToPixel(zoomup);

                    var tilesup = tileArea.Where(a => a.zoom == tileZoomArea.zoom - 1);
                    if (tilesup.Count() > 0 && tilesup.First().points.Contains(zoomup))
                        tilesup.First().points.Remove(zoomup);
                }
            }
        }

        private void AddQuad(tileInfo ti, PointLatLng latlng1, PointLatLng latlng2, PointLatLng latlng3,
            PointLatLng latlng4, long xstart, long x,
            long xnext, long xend, long ystart, long y, long ynext, long yend)
        {
            var zindexmod = (20 - ti.zoom) * 1.0;
            var utm1 = convertCoords(latlng1);
            utm1[2] = srtm.getAltitudeFast(latlng1.Lat, latlng1.Lng).alt;
            var utm2 = convertCoords(latlng2);
            utm2[2] = srtm.getAltitudeFast(latlng2.Lat, latlng2.Lng).alt;
            var utm3 = convertCoords(latlng3);
            utm3[2] = srtm.getAltitudeFast(latlng3.Lat, latlng3.Lng).alt;
            var utm4 = convertCoords(latlng4);
            utm4[2] = srtm.getAltitudeFast(latlng4.Lat, latlng4.Lng).alt;
            var imgx = MPMathHelper.map(xnext, xstart, xend, 0, 1);
            var imgy = MPMathHelper.map(ynext, ystart, yend, 0, 1);
            ti.vertex.Add(new Vertex(utm4[0], utm4[1], utm4[2] - zindexmod, 1, 0, 0, 1, imgx, imgy));
            imgx = MPMathHelper.map(xnext, xstart, xend, 0, 1);
            imgy = MPMathHelper.map(y, ystart, yend, 0, 1);
            ti.vertex.Add(new Vertex(utm3[0], utm3[1], utm3[2] - zindexmod, 0, 1, 0, 1, imgx, imgy));
            imgx = MPMathHelper.map(x, xstart, xend, 0, 1);
            imgy = MPMathHelper.map(y, ystart, yend, 0, 1);
            ti.vertex.Add(new Vertex(utm1[0], utm1[1], utm1[2] - zindexmod, 0, 0, 1, 1, imgx, imgy));
            imgx = MPMathHelper.map(x, xstart, xend, 0, 1);
            imgy = MPMathHelper.map(ynext, ystart, yend, 0, 1);
            ti.vertex.Add(new Vertex(utm2[0], utm2[1], utm2[2] - zindexmod, 1, 1, 0, 1, imgx, imgy));
            var startindex = (uint)ti.vertex.Count - 4;
            ti.indices.AddRange(new[]
            {
                startindex + 0, startindex + 1, startindex + 3,
                startindex + 1, startindex + 2, startindex + 3
            });
        }

        private void CleanupOldTextures(List<tileZoomArea> tileArea)
        {
            textureid.Where(a => !tileArea.Any(b => b.points.Contains(a.Key))).ForEach(c =>
            {
                this.BeginInvoke((MethodInvoker) delegate
                {
                    Console.WriteLine(DateTime.Now.Millisecond + " tile cleanup    \r");
                    tileInfo temp;
                    textureid.TryRemove(c.Key, out temp);
                    temp?.Cleanup();
                });
            });
        }

        #endregion

        #region LOD Helper Methods

        private int GetOptimalZoomForDistance(double distanceMeters)
        {
            if (distanceMeters <= LOD_NEAR_DISTANCE)
                return zoom;
            if (distanceMeters >= LOD_FAR_DISTANCE)
                return minzoom;

            double t = Math.Log(distanceMeters / LOD_NEAR_DISTANCE) / Math.Log(LOD_FAR_DISTANCE / LOD_NEAR_DISTANCE);
            t = Math.Max(0, Math.Min(1, t));

            int optimalZoom = (int)Math.Round(zoom - t * (zoom - minzoom));
            return Math.Max(minzoom, Math.Min(zoom, optimalZoom));
        }

        private double GetDistanceForZoom(int zoomLevel)
        {
            if (zoomLevel >= zoom)
                return LOD_NEAR_DISTANCE;
            if (zoomLevel <= minzoom)
                return LOD_FAR_DISTANCE;

            double t = (double)(zoom - zoomLevel) / (zoom - minzoom);
            return LOD_NEAR_DISTANCE * Math.Pow(LOD_FAR_DISTANCE / LOD_NEAR_DISTANCE, t);
        }

        private GPoint GetParentTile(GPoint tile, int currentZoom, out int parentZoom)
        {
            parentZoom = currentZoom - 1;
            return new GPoint(tile.X / 2, tile.Y / 2);
        }

        private tileInfo FindBestLoadedTile(GPoint targetTile, int targetZoom)
        {
            if (textureid.TryGetValue(targetTile, out var exactTile) && exactTile.zoom == targetZoom)
                return exactTile;

            long x = targetTile.X;
            long y = targetTile.Y;
            for (int z = targetZoom - 1; z >= minzoom; z--)
            {
                x /= 2;
                y /= 2;
                var parentPoint = new GPoint(x, y);
                if (textureid.TryGetValue(parentPoint, out var parentTile) && parentTile.zoom == z)
                    return parentTile;
            }

            return null;
        }

        private bool IsAreaCovered(long x, long y, int zoom, HashSet<(long x, long y, int zoom)> coveredAreas)
        {
            if (coveredAreas.Contains((x, y, zoom)))
                return true;

            long px = x;
            long py = y;
            for (int z = zoom - 1; z >= minzoom; z--)
            {
                px /= 2;
                py /= 2;
                if (coveredAreas.Contains((px, py, z)))
                    return true;
            }

            return false;
        }

        private void MarkAreaCovered(long x, long y, int zoom, HashSet<(long x, long y, int zoom)> coveredAreas)
        {
            coveredAreas.Add((x, y, zoom));
        }

        #endregion

        #region Tile Classes

        public class tileZoomArea
        {
            public List<GPoint> points;
            public int zoom;
            public RectLatLng area { get; set; }
        }

        public class tileInfo : IDisposable
        {
            private Image _img = null;
            private BitmapData _data = null;

            public Image img
            {
                get { return _img; }
                set
                {
                    _img = value;
                    _data = ((Bitmap) _img).LockBits(new System.Drawing.Rectangle(0, 0, _img.Width, _img.Height),
                        ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                }
            }

            public int zoom { get; set; }
            public GPoint point { get; set; }

            public bool textureReady
            {
                get { return _textid != 0; }
            }

            private int _textid = 0;

            public int idtexture
            {
                get
                {
                    if (_textid == 0)
                    {
                        try
                        {
                            if (_data == null)
                                return 0;

                            ConvertColorSpace(_data);
                            _textid = CreateTexture(_data);
                        }
                        catch
                        {
                        }
                    }

                    return _textid;
                }

                set
                {
                    _textmanual = true;
                    _textid = value;
                }
            }

            private int ID_VBO = 0;
            private int ID_EBO = 0;
            private static int _program = 0;

            internal static int Program
            {
                get
                {
                    if (_program == 0)
                        CreateShaders();
                    return _program;
                }
            }

            private bool init;
            private IGraphicsContext Context;
            private IWindowInfo WindowInfo;
            private readonly SemaphoreSlim contextLock;
            private static int positionSlot;
            private static int colorSlot;
            private static int texCoordSlot;
            internal static int projectionSlot;
            internal static int modelViewSlot;
            private static int textureSlot;
            private static int fogStartSlot;
            private static int fogEndSlot;
            private static int fogColorSlot;
            private static float _fogStart = 50000f;
            private static float _fogEnd = 100000f;
            private static float[] _fogColor = { 0.68f, 0.85f, 0.90f, 1.0f };
            private bool _textmanual = false;

            public static void SetFogParams(float start, float end, Color color)
            {
                _fogStart = start;
                _fogEnd = end;
                _fogColor = new float[] { color.R / 255f, color.G / 255f, color.B / 255f, 1.0f };
            }

            public tileInfo(IGraphicsContext context, IWindowInfo windowInfo, SemaphoreSlim contextLock)
            {
                this.Context = context;
                this.WindowInfo = windowInfo;
                this.contextLock = contextLock;
            }

            public int idVBO
            {
                get
                {
                    if (ID_VBO != 0)
                        return ID_VBO;
                    GL.GenBuffers(1, out ID_VBO);
                    if (ID_VBO == 0)
                        return ID_VBO;
                    GL.BindBuffer(BufferTarget.ArrayBuffer, ID_VBO);
                    GL.BufferData(BufferTarget.ArrayBuffer, (vertex.Count * Vertex.Stride), vertex.ToArray(),
                        BufferUsageHint.StaticDraw);
                    int bufferSize;
                    GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out bufferSize);
                    if (vertex.Count * Vertex.Stride != bufferSize)
                        throw new ApplicationException("Vertex array not uploaded correctly");
                    GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
                    return ID_VBO;
                }
            }

            public int idEBO
            {
                get
                {
                    if (ID_EBO != 0)
                        return ID_EBO;
                    GL.GenBuffers(1, out ID_EBO);
                    if (ID_EBO == 0)
                        return ID_EBO;
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, ID_EBO);
                    GL.BufferData(BufferTarget.ElementArrayBuffer, (indices.Count * sizeof(uint)), indices.ToArray(),
                        BufferUsageHint.StaticDraw);
                    int bufferSize;
                    GL.GetBufferParameter(BufferTarget.ElementArrayBuffer, BufferParameterName.BufferSize,
                        out bufferSize);
                    if (indices.Count * sizeof(int) != bufferSize)
                        throw new ApplicationException("Element array not uploaded correctly");
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
                    return ID_EBO;
                }
            }

            public List<Vertex> vertex { get; } = new List<Vertex>();
            public List<uint> indices { get; } = new List<uint>();

            public override string ToString()
            {
                return String.Format("{1}-{0} {2}", point, zoom, textureReady);
            }

            public void Draw(Matrix4 Projection, Matrix4 ModelView)
            {
                if (!init)
                {
                    if (idVBO == 0 || idEBO == 0)
                        return;
                    if (idtexture == 0)
                        return;

                    if (Program == 0)
                        CreateShaders();
                    init = true;
                }

                {
                    GL.UseProgram(Program);

                    GL.EnableVertexAttribArray(positionSlot);
                    GL.EnableVertexAttribArray(colorSlot);
                    GL.EnableVertexAttribArray(texCoordSlot);

                    GL.UniformMatrix4(modelViewSlot, 1, false, ref ModelView.Row0.X);
                    GL.UniformMatrix4(projectionSlot, 1, false, ref Projection.Row0.X);

                    GL.Uniform1(fogStartSlot, _fogStart);
                    GL.Uniform1(fogEndSlot, _fogEnd);
                    GL.Uniform4(fogColorSlot, 1, _fogColor);

                    if (textureReady)
                    {
                        GL.ActiveTexture(TextureUnit.Texture0);
                        GL.Enable(EnableCap.Texture2D);
                        GL.BindTexture(TextureTarget.Texture2D, idtexture);
                        GL.Uniform1(textureSlot, 0);
                    }
                    else
                    {
                        GL.BindTexture(TextureTarget.Texture2D, 0);
                        GL.Disable(EnableCap.Texture2D);
                    }

                    GL.BindBuffer(BufferTarget.ArrayBuffer, idVBO);
                    GL.VertexAttribPointer(positionSlot, 3, VertexAttribPointerType.Float, false,
                        Marshal.SizeOf(typeof(Vertex)), (IntPtr) 0);
                    GL.VertexAttribPointer(colorSlot, 4, VertexAttribPointerType.Float, false,
                        Marshal.SizeOf(typeof(Vertex)), (IntPtr) (sizeof(float) * 3));
                    GL.VertexAttribPointer(texCoordSlot, 2, VertexAttribPointerType.Float, false,
                        Marshal.SizeOf(typeof(Vertex)), (IntPtr) (sizeof(float) * 7));
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, idEBO);
                    GL.DrawElements(PrimitiveType.Triangles, indices.Count, DrawElementsType.UnsignedInt,
                        IntPtr.Zero);

                    GL.DisableVertexAttribArray(positionSlot);
                    GL.DisableVertexAttribArray(colorSlot);
                    GL.DisableVertexAttribArray(texCoordSlot);
                }
            }

            private static void CreateShaders()
            {
                VertexShader = GL.CreateShader(ShaderType.VertexShader);
                FragmentShader = GL.CreateShader(ShaderType.FragmentShader);

                GL.ShaderSource(VertexShader, @"
attribute vec3 Position;
attribute vec4 SourceColor;
attribute vec2 TexCoordIn;
varying vec4 DestinationColor;
varying vec2 TexCoordOut;
varying float vDistance;
uniform mat4 Projection;
uniform mat4 ModelView;
void main(void) {
    vec4 viewPos = ModelView * vec4(Position, 1.0);
    vDistance = length(viewPos.xyz);
    gl_Position = Projection * viewPos;
    TexCoordOut = TexCoordIn;
}
                ");
                GL.ShaderSource(FragmentShader, @"
precision mediump float;
varying vec4 DestinationColor;
varying vec2 TexCoordOut;
varying float vDistance;
uniform sampler2D Texture;
uniform float fogStart;
uniform float fogEnd;
uniform vec4 fogColor;
void main(void) {
    vec4 color = texture2D(Texture, TexCoordOut);
    float fogAmount = clamp((vDistance - fogStart) / (fogEnd - fogStart), 0.0, 1.0);
    gl_FragColor = mix(color, fogColor, fogAmount);
}
                ");
                GL.CompileShader(VertexShader);
                Debug.WriteLine(GL.GetShaderInfoLog(VertexShader));
                GL.GetShader(VertexShader, ShaderParameter.CompileStatus, out var code);
                if (code != (int) All.True)
                {
                    throw new Exception(
                        $"Error occurred whilst compiling Shader({VertexShader}) {GL.GetShaderInfoLog(VertexShader)}");
                }

                GL.CompileShader(FragmentShader);
                Debug.WriteLine(GL.GetShaderInfoLog(FragmentShader));
                GL.GetShader(FragmentShader, ShaderParameter.CompileStatus, out code);
                if (code != (int) All.True)
                {
                    throw new Exception(
                        $"Error occurred whilst compiling Shader({FragmentShader}) {GL.GetShaderInfoLog(FragmentShader)}");
                }

                _program = GL.CreateProgram();
                GL.AttachShader(_program, VertexShader);
                GL.AttachShader(_program, FragmentShader);
                GL.LinkProgram(_program);
                Debug.WriteLine(GL.GetProgramInfoLog(_program));
                GL.GetProgram(_program, GetProgramParameterName.LinkStatus, out code);
                if (code != (int) All.True)
                {
                    throw new Exception(
                        $"Error occurred whilst linking Program({_program}) {GL.GetProgramInfoLog(_program)}");
                }

                GL.UseProgram(_program);
                positionSlot = GL.GetAttribLocation(_program, "Position");
                colorSlot = GL.GetAttribLocation(_program, "SourceColor");
                texCoordSlot = GL.GetAttribLocation(_program, "TexCoordIn");
                projectionSlot = GL.GetUniformLocation(_program, "Projection");
                modelViewSlot = GL.GetUniformLocation(_program, "ModelView");
                textureSlot = GL.GetUniformLocation(_program, "Texture");
                fogStartSlot = GL.GetUniformLocation(_program, "fogStart");
                fogEndSlot = GL.GetUniformLocation(_program, "fogEnd");
                fogColorSlot = GL.GetUniformLocation(_program, "fogColor");
            }

            public static int FragmentShader { get; set; }
            public static int VertexShader { get; set; }

            public void Cleanup(bool nolock = false)
            {
                if (!nolock)
                    contextLock.Wait();
                try
                {
                    if (!nolock)
                        if (!Context.IsCurrent)
                            Context.MakeCurrent(WindowInfo);
                    if (!_textmanual)
                        GL.DeleteTextures(1, ref _textid);
                    GL.DeleteBuffers(1, ref ID_VBO);
                    GL.DeleteBuffers(1, ref ID_EBO);
                    try
                    {
                        if (img != null)
                            img.Dispose();
                    }
                    catch
                    {
                    }
                    if (!nolock)
                        if (Context.IsCurrent)
                            Context.MakeCurrent(null);
                }
                finally
                {
                    if (!nolock)
                        contextLock.Release();
                }
            }

            public void Dispose()
            {
                Cleanup();
            }
        }

        #endregion
    }
}
