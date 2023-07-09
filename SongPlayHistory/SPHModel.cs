﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using IPA.Utilities;
using Newtonsoft.Json;
using SongPlayHistory.Configuration;

namespace SongPlayHistory
{
    internal class Record
    {
        public long Date = 0L;
        public int ModifiedScore = 0;
        public int RawScore = 0;
        public int LastNote = 0;
        public int Param = 0;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        [DefaultValue(null)]
        public int? MaxRawScore = null;
        [JsonIgnore]
        public int? CalculatedMaxRawScore = null;  // only save in memory
        public string ToShortString()
        {
            return $"ModifiedScore {ModifiedScore}, RawScore {RawScore}, LastNote {LastNote}, MaxRawScore {MaxRawScore ?? CalculatedMaxRawScore}";
        }
    }

    [Flags]
    internal enum Param
    {
        None = 0,
        BatteryEnergy = 1 << 0,
        NoFail = 1 << 1,
        InstaFail = 1 << 2,
        NoObstacles = 1 << 3,
        NoBombs = 1 << 4,
        FastNotes = 1 << 5,
        StrictAngles = 1 << 6,
        DisappearingArrows = 1 << 7,
        FasterSong = 1 << 8,
        SlowerSong = 1 << 9,
        NoArrows = 1 << 10,
        GhostNotes = 1 << 11,
        SuperFastSong = 1 << 12,
        SmallCubes = 1 << 13,
        ProMode = 1 << 14,
        // where is 1 << 15 (0x8000) ??
        SubmissionDisabled = 1 << 16,
        Multiplayer = 1 << 17,
    }

    internal static class ParamHelper
    {
        internal static Param ModsToParam(GameplayModifiers mods)
        {
            Param param = Param.None;
            param |= mods.energyType == GameplayModifiers.EnergyType.Battery ? Param.BatteryEnergy : 0;
            param |= mods.noFailOn0Energy ? Param.NoFail : 0;
            param |= mods.instaFail ? Param.InstaFail : 0;
            param |= mods.enabledObstacleType == GameplayModifiers.EnabledObstacleType.NoObstacles ? Param.NoObstacles : 0;
            param |= mods.noBombs ? Param.NoBombs : 0;
            param |= mods.fastNotes ? Param.FastNotes : 0;
            param |= mods.strictAngles ? Param.StrictAngles : 0;
            param |= mods.disappearingArrows ? Param.DisappearingArrows : 0;
            param |= mods.songSpeed == GameplayModifiers.SongSpeed.SuperFast ? Param.SuperFastSong : 0;
            param |= mods.songSpeed == GameplayModifiers.SongSpeed.Faster ? Param.FasterSong : 0;
            param |= mods.songSpeed == GameplayModifiers.SongSpeed.Slower ? Param.SlowerSong : 0;
            param |= mods.noArrows ? Param.NoArrows : 0;
            param |= mods.ghostNotes ? Param.GhostNotes : 0;
            param |= mods.smallCubes ? Param.SmallCubes : 0;
            param |= mods.proMode ? Param.ProMode : 0;
            return param;
        } 
    }

    internal class UserVote
    {
        public string key = null;
        public string voteType = null;
    }

    internal static class SPHModel
    {
        public static readonly string DataFile = Path.Combine(Environment.CurrentDirectory, "UserData", "SongPlayData.json");
        public static readonly string VoteFile = Path.Combine(Environment.CurrentDirectory, "UserData", "votedSongs.json");

        public static Dictionary<string, IList<Record>> Records { get; set; } = new Dictionary<string, IList<Record>>();
        public static Dictionary<string, UserVote> Votes { get; private set; } = new Dictionary<string, UserVote>();

        private static DateTime _voteLastWritten;

        public static List<Record> GetRecords(IDifficultyBeatmap beatmap)
        {
            var config = PluginConfig.Instance;
            var beatmapCharacteristicName = beatmap.parentDifficultyBeatmapSet.beatmapCharacteristic.serializedName;
            var difficulty = $"{beatmap.level.levelID}___{(int)beatmap.difficulty}___{beatmapCharacteristicName}";

            if (Records.TryGetValue(difficulty, out IList<Record> records))
            {
                // LastNote = -1 (cleared), 0 (undefined), n (failed)
                var filtered = config.ShowFailed ? records : records.Where(s => s.LastNote <= 0);
                var ordered = filtered.OrderByDescending(s => config.SortByDate ? s.Date : s.ModifiedScore);
                return ordered.ToList();
            }

            return new List<Record>();
        }

        public static void SaveRecord(IDifficultyBeatmap? beatmap, int? MaxRawScore, LevelCompletionResults? result, bool submissionDisabled, bool isMultiplayer)
        {
            if (beatmap == null || result == null)
            {
                return;
            }

            // Cancelled.
            if (result.levelEndStateType == LevelCompletionResults.LevelEndStateType.Incomplete)
            {
                return;
            }

            // We now keep failed records.
            var cleared = result.levelEndStateType == LevelCompletionResults.LevelEndStateType.Cleared;

            // If submissionDisabled = true, we assume custom gameplay modifiers are applied.
            var param = ParamHelper.ModsToParam(result.gameplayModifiers);
            param |= submissionDisabled ? Param.SubmissionDisabled : 0;
            param |= isMultiplayer ? Param.Multiplayer : 0;

            var record = new Record
            {
                Date = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                ModifiedScore = result.modifiedScore,
                RawScore = result.multipliedScore,
                LastNote = cleared ? -1 : result.goodCutsCount + result.badCutsCount + result.missedCount,
                Param = (int)param,
                MaxRawScore = MaxRawScore
            };
            
            Plugin.Log.Info($"Saving result.");
            Plugin.Log.Debug($"Record: {record.ToShortString()}");

            var beatmapCharacteristicName = beatmap.parentDifficultyBeatmapSet.beatmapCharacteristic.serializedName;
            var difficulty = $"{beatmap.level.levelID}___{(int)beatmap.difficulty}___{beatmapCharacteristicName}";

            if (!Records.ContainsKey(difficulty))
            {
                Records.Add(difficulty, new List<Record>());
            }
            Records[difficulty].Add(record);

            // Save to a file. We do this synchronously because the overhead is small. (400 ms / 15 MB, 60 ms / 1 MB)
            SaveRecordsToFile();

            Plugin.Log?.Info($"Saved a new record {difficulty} ({result.modifiedScore}).");
        }

        public static bool ScanVoteData()
        {
            Plugin.Log?.Info($"Scanning {Path.GetFileName(VoteFile)}...");

            if (!File.Exists(VoteFile))
            {
                Plugin.Log?.Warn("The file doesn't exist.");
                return false;
            }
            try
            {
                if (_voteLastWritten != File.GetLastWriteTime(VoteFile))
                {
                    _voteLastWritten = File.GetLastWriteTime(VoteFile);

                    var text = File.ReadAllText(VoteFile);
                    Votes = JsonConvert.DeserializeObject<Dictionary<string, UserVote>>(text) ?? new Dictionary<string, UserVote>();

                    Plugin.Log?.Info("Update done.");
                }

                return true;
            }
            catch (Exception ex) // IOException, JsonException
            {
                Plugin.Log?.Error(ex.ToString());
                return false;
            }
        }

        internal static void SaveRecordsToFile()
        {
            try
            {
                if (Records.Count > 0)
                {
                    var serialized = JsonConvert.SerializeObject(Records, Formatting.Indented);
                    File.WriteAllText(DataFile, serialized);
                }
            }
            catch (Exception ex) // IOException, JsonException
            {
                Plugin.Log?.Error(ex.ToString());
            }
        }

        public static void InitializeRecords()
        {
            // We don't anymore support migrating old records from a config file.
            if (!File.Exists(DataFile))
            {
                return;
            }

            // Read records from a data file.
            var text = File.ReadAllText(DataFile);
            try
            {
                Records = JsonConvert.DeserializeObject<Dictionary<string, IList<Record>>>(text);
                if (Records == null)
                {
                    throw new JsonReaderException("Unable to deserialize an empty JSON string.");
                }
            }
            catch (JsonException ex)
            {
                // The data file is corrupted.
                Plugin.Log?.Error(ex.ToString());

                // Try to restore from a backup.
                var backup = new FileInfo(Path.ChangeExtension(DataFile, ".bak"));
                if (backup.Exists && backup.Length > 0)
                {
                    Plugin.Log?.Info("Restoring from a backup...");
                    text = File.ReadAllText(backup.FullName);

                    Records = JsonConvert.DeserializeObject<Dictionary<string, IList<Record>>>(text);
                    if (Records == null)
                    {
                        // Fail hard to prevent overwriting any previous data or breaking the game.
                        throw new Exception("Failed to restore data.");
                    }
                }
                else
                {
                    // There's nothing more we can try. Overwrite the file.
                    Records = new Dictionary<string, IList<Record>>();
                }
                SaveRecordsToFile();
            }
        }

        public static void BackupRecords()
        {
            if (!File.Exists(DataFile))
            {
                return;
            }

            var backupFile = Path.ChangeExtension(DataFile, ".bak");
            try
            {
                if (File.Exists(backupFile))
                {
                    // Compare file sizes instead of the last modified.
                    if (new FileInfo(DataFile).Length > new FileInfo(backupFile).Length)
                    {
                        File.Copy(DataFile, backupFile, true);
                    }
                    else
                    {
                        Plugin.Log?.Info("Nothing to backup.");
                    }
                }
                else
                {
                    File.Copy(DataFile, backupFile);
                }
            }
            catch (IOException ex)
            {
                Plugin.Log?.Error(ex.ToString());
            }
        }
    }
}
