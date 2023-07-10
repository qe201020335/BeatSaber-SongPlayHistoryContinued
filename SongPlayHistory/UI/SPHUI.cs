﻿using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HMUI;
using IPA.Utilities;
using SiraUtil.Logging;
using SongPlayHistory.Configuration;
using SongPlayHistory.Model;
using SongPlayHistory.Utils;
using TMPro;
using UnityEngine;
using Zenject;
using UObject = UnityEngine.Object;

namespace SongPlayHistory.UI
{
    internal class SPHUI: IInitializable, IDisposable
    {

        private readonly LevelStatsView _levelStatsView;

        private readonly LevelParamsPanel _levelParamsPanel;
        
        private readonly StandardLevelDetailViewController _levelDetailViewController;

        private readonly TableView _tableView;

        [Inject]
        private readonly SiraLog _logger = null!;

        [Inject]
        private readonly PlayerDataModel _playerDataModel = null!;

        [Inject]
        private readonly HoverHintController _hoverHintController = null!;

        [Inject]
        private readonly ResultsViewController _resultsViewController = null!;

        public SPHUI(PlatformLeaderboardViewController leaderboardViewController, StandardLevelDetailViewController levelDetailViewController, LevelCollectionViewController levelCollectionViewController)
        {
            _levelStatsView = leaderboardViewController.GetField<LevelStatsView, PlatformLeaderboardViewController>("_levelStatsView");
            
            _levelDetailViewController = levelDetailViewController;
            var levelDetailView = levelDetailViewController.GetField<StandardLevelDetailView, StandardLevelDetailViewController>("_standardLevelDetailView");
            _levelParamsPanel = levelDetailView.GetField<LevelParamsPanel, StandardLevelDetailView>("_levelParamsPanel");
            
            var levelCollectionTableView = levelCollectionViewController.GetField<LevelCollectionTableView, LevelCollectionViewController>("_levelCollectionTableView");
            _tableView = levelCollectionTableView.GetField<TableView, LevelCollectionTableView>("_tableView");
        }
        
        
        public void Initialize()
        {
            _levelDetailViewController.didChangeDifficultyBeatmapEvent -= OnDifficultyChanged;
            _levelDetailViewController.didChangeDifficultyBeatmapEvent += OnDifficultyChanged;
            _levelDetailViewController.didChangeContentEvent -= OnContentChanged;
            _levelDetailViewController.didChangeContentEvent += OnContentChanged;
            _resultsViewController.continueButtonPressedEvent -= OnPlayResultDismiss;
            _resultsViewController.continueButtonPressedEvent += OnPlayResultDismiss;
        }

        public void Dispose()
        {
            _levelDetailViewController.didChangeDifficultyBeatmapEvent -= OnDifficultyChanged;
            _levelDetailViewController.didChangeContentEvent -= OnContentChanged;
            _resultsViewController.continueButtonPressedEvent -= OnPlayResultDismiss;

            _hoverHint = null;
            _playCount = null;
        }

        private HoverHint? _hoverHint;
        private readonly object _hoverHintLock = new();
        private HoverHint HoverHint
        {
            get
            {
                lock (_hoverHintLock)
                {
                    if (_hoverHint == null)
                    {
                        _hoverHint = _levelStatsView.GetComponentsInChildren<HoverHint>().FirstOrDefault(x => x.name == "HoverArea");
                    }
                    
                    if (_hoverHint != null)
                    {
                        return _hoverHint;
                    }
                    
                    _logger.Debug("HoverHint not found, making a new one");
                    var template = _levelParamsPanel.GetComponentsInChildren<RectTransform>().First(x => x.name == "NotesCount");
                    var label = UObject.Instantiate(template, _levelStatsView.transform);
                    label.name = "HoverArea";
                    label.transform.MatchParent();
                    UObject.Destroy(label.transform.Find("Icon").gameObject);
                    UObject.Destroy(label.transform.Find("ValueText").gameObject);
                    UObject.DestroyImmediate(label.GetComponentInChildren<HoverHint>());
                    UObject.Destroy(label.GetComponentInChildren<LocalizedHoverHint>());

                    _hoverHint = label.gameObject.AddComponent<HoverHint>();
                    _hoverHint.SetField("_hoverHintController", _hoverHintController);
                    _hoverHint.text = "";
                    
                    return _hoverHint;
                }
            }
        }

        private RectTransform? _playCount;
        private readonly object _playCountLock = new();
        private RectTransform PlayCount
        {
            get
            {
                lock (_playCountLock)
                {
                    if (_playCount == null)
                    {
                        _playCount = _levelStatsView.GetComponentsInChildren<RectTransform>().FirstOrDefault(x => x.name == "PlayCount");
                    }

                    if (_playCount != null)
                    {
                        return _playCount;
                    }

                    _logger.Debug("PlayCount text not found, making a new one");
                
                    var maxCombo = _levelStatsView.GetComponentsInChildren<RectTransform>().First(x => x.name == "MaxCombo");
                    var highscore = _levelStatsView.GetComponentsInChildren<RectTransform>().First(x => x.name == "Highscore");
                    var maxRank = _levelStatsView.GetComponentsInChildren<RectTransform>().First(x => x.name == "MaxRank");

                    _playCount = UObject.Instantiate(maxCombo, _levelStatsView.transform);
                    _playCount.name = "PlayCount";

                    const float w = 0.225f;
                    (maxCombo.transform as RectTransform)!.anchorMin = new Vector2(0f, .5f);
                    (maxCombo.transform as RectTransform)!.anchorMax = new Vector2(1 * w, .5f);
                    (highscore.transform as RectTransform)!.anchorMin = new Vector2(1 * w, .5f);
                    (highscore.transform as RectTransform)!.anchorMax = new Vector2(2 * w, .5f);
                    (maxRank.transform as RectTransform)!.anchorMin = new Vector2(2 * w, .5f);
                    (maxRank.transform as RectTransform)!.anchorMax = new Vector2(3 * w, .5f);
                    (_playCount.transform as RectTransform)!.anchorMin = new Vector2(3 * w, .5f);
                    (_playCount.transform as RectTransform)!.anchorMax = new Vector2(4 * w, .5f);
                    var title = _playCount.GetComponentsInChildren<TextMeshProUGUI>().First(x => x.name == "Title");
                    title.SetText("Play Count");
                    
                    return _playCount;
                }
            }
        }
        
        private void OnDifficultyChanged(StandardLevelDetailViewController _, IDifficultyBeatmap beatmap)
        {
            UpdateUI(beatmap);
        }

        private void OnContentChanged(StandardLevelDetailViewController _, StandardLevelDetailViewController.ContentType contentType)
        {
            if (contentType != StandardLevelDetailViewController.ContentType.Loading 
                && contentType != StandardLevelDetailViewController.ContentType.Error
                && contentType != StandardLevelDetailViewController.ContentType.NoAllowedDifficultyBeatmap)
            {
                UpdateUI(_levelDetailViewController.selectedDifficultyBeatmap);
            }
        }

        private void OnPlayResultDismiss(ResultsViewController _)
        {
            // The user may have voted on this map.
            // TODO: VoteTracker
            RecordsManager.ScanVoteData();
            _tableView.RefreshCellsContent();

            UpdateUI(_levelDetailViewController.selectedDifficultyBeatmap);
        }

        private CancellationTokenSource? _tokenSource = new CancellationTokenSource();

        private void UpdateUI(IDifficultyBeatmap? beatmap)
        {
            _tokenSource?.Cancel();
            _tokenSource?.Dispose();
            if (beatmap == null) return;
            _logger.Info("Updating SPH UI");
            _tokenSource = new CancellationTokenSource();
            Task.Run(() =>
            {
                try
                {
                    SetRecords(beatmap, _tokenSource.Token);
                    SetStats(beatmap, _tokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.Info("Update cancelled");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to update SPH ui, {nameof(ex)}: {ex.Message}");
                    _logger.Debug(ex);
                }
            }, _tokenSource.Token);
        }
        
        private async void SetRecords(IDifficultyBeatmap beatmap, CancellationToken token)
        {
            var records = RecordsManager.GetRecords(beatmap);
            
            token.ThrowIfCancellationRequested();
            
            if (records.Count == 0)
            {
                HoverHint.text = "No record";
                return;
            }

            var beatmapData = await beatmap.GetBeatmapDataAsync(beatmap.GetEnvironmentInfo(), _playerDataModel.playerData.playerSpecificSettings);
            var notesCount = beatmapData.cuttableNotesCount;
            var maxScore = ScoreModel.ComputeMaxMultipliedScoreForBeatmap(beatmapData);
            var builder = new StringBuilder(200);
            
            // we can use the original v2 scoring method to calculate the adjusted max score if there is no slider or burst
            var v2Score = !beatmapData.GetBeatmapDataItems<SliderData>(0).Any();

            static string Space(int len)
            {
                var space = string.Concat(Enumerable.Repeat("_", len));
                return $"<size=1><color=#00000000>{space}</color></size>";
            }
            
            token.ThrowIfCancellationRequested();
            _logger.Debug($"Total number of records: {records.Count}");
            var truncated = records.Take(10).ToList();
            
            foreach (var r in truncated)
            {
                _logger.Trace($"Record: {r.ToShortString()}");
                var localDateTime = DateTimeOffset.FromUnixTimeMilliseconds(r.Date).LocalDateTime;
                
                var hasMaxScoreSaved = r.MaxRawScore != null;
                var levelFinished = r.LastNote < 0;
                
                var adjMaxScore = r.MaxRawScore ?? r.CalculatedMaxRawScore ?? ScoreUtils.CalculateV2MaxScore(r.LastNote);
                var denom = !levelFinished && PluginConfig.Instance.AverageAccuracy ? adjMaxScore : maxScore;
                var accuracy = denom == 0 ? 100 : r.RawScore / (float)denom * 100f;
                // only display acc if we can get the max scores with the data we have on hand
                var shouldShowAcc = levelFinished || hasMaxScoreSaved || v2Score || !PluginConfig.Instance.AverageAccuracy;

                if (v2Score && r.MaxRawScore == null) r.CalculatedMaxRawScore = adjMaxScore;

                var param = ((Param)r.Param).ToParamString();
                if (param.Length == 0 && r.RawScore != r.ModifiedScore)
                {
                    param = "?!";
                }
                var notesRemaining = notesCount - r.LastNote;

                builder.Append(Space(truncated.Count - truncated.IndexOf(r) - 1));
                builder.Append($"<size=2.5><color=#1a252bff>{localDateTime:d}</color></size>");
                builder.Append($"<size=3.5><color=#0f4c75ff> {r.ModifiedScore}</color></size>");
                if (shouldShowAcc && r.RawScore <= denom)
                {
                    // bug: a soft fail record will save total score instead of at the time of fail   
                    // result in the the saved score much greater than the max score
                    builder.Append($"<size=3.5><color=#368cc6ff> {accuracy:0.00}%</color></size>");
                }
                if (param.Length > 0)
                {
                    builder.Append($"<size=2><color=#1a252bff> {param}</color></size>");
                }
                if (PluginConfig.Instance.ShowFailed)
                {
                    if (r.LastNote == -1)
                        builder.Append($"<size=2.5><color=#1a252bff> cleared</color></size>");
                    else if (r.LastNote == 0) // old record (success, fail, or practice)
                        builder.Append($"<size=2.5><color=#584153ff> unknown</color></size>");
                    else
                        builder.Append($"<size=2.5><color=#ff5722ff> +{notesRemaining} notes</color></size>");
                }
                builder.Append(Space(truncated.IndexOf(r)));
                builder.AppendLine();
            }
            
            token.ThrowIfCancellationRequested();
            HoverHint.text = builder.ToString();
        }

        private void SetStats(IDifficultyBeatmap beatmap, CancellationToken token)
        {
            var stats = _playerDataModel.playerData.GetPlayerLevelStatsData(beatmap.level.levelID, beatmap.difficulty, beatmap.parentDifficultyBeatmapSet.beatmapCharacteristic);
            var text = PlayCount.GetComponentsInChildren<TextMeshProUGUI>().First(x => x.name == "Value");
            if (token.IsCancellationRequested) return;
            text.SetText(stats.playCount.ToString());
        }
    }
}