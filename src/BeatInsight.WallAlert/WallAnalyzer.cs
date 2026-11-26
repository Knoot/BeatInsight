using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BeatInsight.WallAlert
{
    internal static class WallAnalyzer
    {
        public const float CrouchWallCoverageToleranceBeats = 0.12f;
        public const float CrouchWallSequenceToleranceBeats = 0.5f;

        public static bool IsCrouchWall(ObstacleData obstacle)
        {
            return obstacle != null
                && obstacle.width > 0
                && obstacle.height > 0
                && OccupiesUpperRows(LoadedLayer((int)obstacle.lineLayer), obstacle.height);
        }

        public static WallAnalysis AnalyzeCrouchWalls(string beatmapJson, float beatsPerMinute,bool preserveExtendedLayers=false)
        {
            if (string.IsNullOrWhiteSpace(beatmapJson))
            {
                return WallAnalysis.Empty;
            }

            var root = JObject.Parse(beatmapJson);
            var beatmapFormatVersion = ReadBeatmapFormatVersion(root);
            var parts = ReadCrouchWallParts(root,preserveExtendedLayers);
            if (parts.Count == 0)
            {
                return new WallAnalysis(beatmapFormatVersion, Enumerable.Empty<string>());
            }

            var crouchWallBeats = FindCrouchWallGroups(parts);

            return new WallAnalysis(beatmapFormatVersion, crouchWallBeats.Select(beat => FormatBeatTime(beat, beatsPerMinute)));
        }

        private static List<CrouchWallPart> ReadCrouchWallParts(JObject root,bool preserveExtendedLayers)
        {
            var obstacles = root["_obstacles"] ?? root["obstacles"];
            if (obstacles == null)
            {
                return new List<CrouchWallPart>();
            }

            var parts = new List<CrouchWallPart>();
            var obstacleData = root["obstaclesData"] as JArray;
            foreach (var obstacle in obstacles)
            {
                if (obstacleData != null)
                {
                    var index = obstacle["i"]?.Value<int?>() ?? 0;
                    if (index < 0 || index >= obstacleData.Count)
                    {
                        continue;
                    }

                    var data = obstacleData[index];
                    if (IsV3OrV4CrouchWall(data,preserveExtendedLayers))
                    {
                        parts.Add(ReadV4CrouchWallPart(obstacle, data,preserveExtendedLayers));
                    }
                }
                else if (IsCrouchWall(obstacle,preserveExtendedLayers))
                {
                    parts.Add(ReadCrouchWallPart(obstacle,preserveExtendedLayers));
                }
            }

            return parts;
        }

        private static string ReadBeatmapFormatVersion(JObject root)
        {
            var version = root["_version"]?.Value<string>() ?? root["version"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(version))
            {
                return "-";
            }

            var major = version.Trim().Split('.')[0];
            return int.TryParse(major, out _) ? $"v{major}" : version;
        }

        private static bool IsCrouchWall(JToken obstacle,bool preserveExtendedLayers)
        {
            if (obstacle["_type"] != null)
            {
                return IsV2CrouchWall(obstacle);
            }

            return IsV3OrV4CrouchWall(obstacle,preserveExtendedLayers);
        }

        private static bool IsV2CrouchWall(JToken obstacle)
        {
            const int topObstacleType = 1;
            var type = obstacle["_type"]?.Value<int?>() ?? 0;
            var width = obstacle["_width"]?.Value<int?>() ?? 1;

            return type == topObstacleType && width > 0;
        }

        private static bool IsV3OrV4CrouchWall(JToken obstacle,bool preserveExtendedLayers)
        {
            var layer = JsonLayer(obstacle["y"]?.Value<int?>() ?? 0,preserveExtendedLayers);
            var width = obstacle["w"]?.Value<int?>() ?? 1;
            var height = obstacle["h"]?.Value<int?>() ?? 0;
            if (width <= 0)
            {
                width = 1;
            }

            if (height <= 0)
            {
                height = layer == 1 ? 3 : 5;
            }

            return width > 0 && height > 0 && OccupiesUpperRows(layer, height);
        }

        private static CrouchWallPart ReadCrouchWallPart(JToken obstacle,bool preserveExtendedLayers)
        {
            var beat = ReadFloat(obstacle, "_time", "b");
            var duration = ReadFloat(obstacle, "_duration", "d");
            var lineIndex = obstacle["_lineIndex"]?.Value<int?>() ?? obstacle["x"]?.Value<int?>() ?? 0;
            var width = obstacle["_width"]?.Value<int?>() ?? obstacle["w"]?.Value<int?>() ?? 1;
            var layer = obstacle["_type"] != null
                ? 1
                : JsonLayer(obstacle["y"]?.Value<int?>() ?? 0,preserveExtendedLayers);
            var height = obstacle["_type"] != null
                ? 3
                : ReadHeight(obstacle, layer);
            return new CrouchWallPart(beat, beat + Math.Max(0f, duration), lineIndex, width, layer, height);
        }

        private static CrouchWallPart ReadV4CrouchWallPart(JToken obstacle, JToken data,bool preserveExtendedLayers)
        {
            var beat = ReadFloat(obstacle, "_time", "b");
            var duration = data["d"]?.Value<float?>() ?? 0f;
            var lineIndex = data["x"]?.Value<int?>() ?? 0;
            var width = data["w"]?.Value<int?>() ?? 1;
            var layer = JsonLayer(data["y"]?.Value<int?>() ?? 0,preserveExtendedLayers);
            var height = ReadHeight(data, layer);
            return new CrouchWallPart(beat, beat + Math.Max(0f, duration), lineIndex, width, layer, height);
        }

        private static float ReadFloat(JToken token, string v2Name, string v3Name)
        {
            return token[v2Name]?.Value<float?>() ?? token[v3Name]?.Value<float?>() ?? 0f;
        }

        private static int ReadHeight(JToken token, int layer)
        {
            var height = token["h"]?.Value<int?>() ?? 0;
            if (height > 0)
            {
                return height;
            }

            return layer == 1 ? 3 : 5;
        }

        private static IEnumerable<float> FindCrouchWallGroups(IEnumerable<CrouchWallPart> parts)
        {
            var orderedParts = parts.OrderBy(part => part.StartBeat).ToArray();
            var startBeats = orderedParts
                .Where(part => part.Layer > 0)
                .Select(part => part.StartBeat)
                .Distinct()
                .OrderBy(beat => beat);
            var previousCrouchBeat = float.MinValue;

            foreach (var beat in startBeats)
            {
                if (!HasCrouchCenterCoverage(orderedParts, beat))
                {
                    continue;
                }

                if (beat > previousCrouchBeat + CrouchWallSequenceToleranceBeats)
                {
                    yield return beat;
                }

                previousCrouchBeat = beat;
            }
        }

        public static bool ApplyLaneCoverage(float[] laneEnds, ObstacleData obstacle)
        {
            if (!IsCrouchWall(obstacle))
            {
                return false;
            }

            ApplyLaneCoverage(laneEnds, new CrouchWallPart(
                obstacle.beat,
                // Loaded runtime duration is seconds; coverage is in beats.
                // The supplied beat endpoint is authoritative in this path.
                obstacle.endBeat,
                obstacle.lineIndex,
                obstacle.width,
                1,
                obstacle.height));

            return HasCrouchCenterCoverage(laneEnds, obstacle.beat);
        }

        private static void ApplyLaneCoverage(float[] laneEnds, CrouchWallPart part)
        {
            var startLane = Math.Max(0, part.LineIndex);
            var endLane = Math.Min(4, part.LineIndex + Math.Max(1, part.Width));
            var endBeat = part.EndBeat + CrouchWallCoverageToleranceBeats;

            for (var lane = startLane; lane < endLane; lane++)
            {
                laneEnds[lane] = Math.Max(laneEnds[lane], endBeat);
            }
        }

        public static bool HasCrouchCenterCoverage(float[] laneEnds, float beat)
        {
            return laneEnds[1] >= beat && laneEnds[2] >= beat;
        }

        public static bool IsUpperCrouchWallPart(ObstacleData obstacle)
        {
            return IsCrouchWall(obstacle) && LoadedLayer((int)obstacle.lineLayer) > 0;
        }

        private static bool HasCrouchCenterCoverage(IEnumerable<CrouchWallPart> parts, float beat)
        {
            const int rowCount = 3;
            const int columnCount = 4;
            var field = new bool[rowCount, columnCount];

            foreach (var part in parts)
            {
                if (part.StartBeat > beat || part.EndBeat + CrouchWallCoverageToleranceBeats < beat)
                {
                    continue;
                }

                var startColumn = Math.Max(0, part.LineIndex);
                var endColumn = Math.Min(columnCount, part.LineIndex + Math.Max(1, part.Width));

                for (var row = 1; row < rowCount; row++)
                {
                    for (var column = startColumn; column < endColumn; column++)
                    {
                        field[row, column] = true;
                    }
                }
            }

            return field[1, 1] && field[1, 2] && field[2, 1] && field[2, 2];
        }

        // The ordinary spawn path maps every loaded enum other than 0/1 to Top.
        // The JSON converter normally maps out-of-range values to Base; an
        // installed conversion postfix may preserve them before the spawn path.
        private static int LoadedLayer(int layer)=>layer==0?0:layer==1?1:2;
        private static int JsonLayer(int layer,bool preserve)=>layer>=0&&layer<=2?layer:preserve?LoadedLayer(layer):0;

        private static bool OccupiesUpperRows(int layer, int height)
        {
            const int topPlayableLayer = 2;
            // Elevated walls and walls below the top row do not obstruct the head.
            return layer <= topPlayableLayer && (long)layer + height > topPlayableLayer;
        }

        private static string FormatBeatTime(float beat, float beatsPerMinute)
        {
            if (beatsPerMinute <= 0f)
            {
                beatsPerMinute = 120f;
            }

            var time = TimeSpan.FromSeconds(Math.Max(0f, beat) * 60f / beatsPerMinute);
            return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
        }
    }

    internal struct CrouchWallPart
    {
        public CrouchWallPart(float startBeat, float endBeat, int lineIndex, int width, int layer, int height)
        {
            StartBeat = startBeat;
            EndBeat = Math.Max(startBeat, endBeat);
            LineIndex = lineIndex;
            Width = width;
            Layer = layer;
            Height = height;
        }

        public float StartBeat { get; }

        public float EndBeat { get; }

        public int LineIndex { get; }

        public int Width { get; }

        public int Layer { get; }

        public int Height { get; }

        public float Duration => EndBeat - StartBeat;
    }

    internal sealed class WallAnalysis
    {
        public static readonly WallAnalysis Empty = new WallAnalysis("-", Enumerable.Empty<string>());

        public WallAnalysis(string beatmapFormatVersion, IEnumerable<string> timecodes)
        {
            BeatmapFormatVersion = beatmapFormatVersion;
            Timecodes = timecodes.ToArray();
        }

        public string BeatmapFormatVersion { get; }

        public string[] Timecodes { get; }

        public int SquatCount => Timecodes.Length;
    }
}
