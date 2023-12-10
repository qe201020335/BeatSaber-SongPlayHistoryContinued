﻿using System.Collections.Generic;
using SongPlayHistory.Model;

namespace SongPlayHistory.SongPlayData;

public interface IRecordManager
{
    public IList<ISongPlayRecord> GetRecords(IDifficultyBeatmap beatmap);
    
    public IList<ISongPlayRecord> GetRecords(LevelMapKey key);
    
    public IDictionary<LevelMapKey, IList<ISongPlayRecord>> GetAllRecords();
}