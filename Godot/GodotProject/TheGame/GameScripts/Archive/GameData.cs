using Godot;
using GodotGameFramework.Archive;
using System;
using System.Collections.Generic;
[Serializable]
public class GameData : ArchiveData
{
    public int Score;
    public List<ActorData> Actors;
    public GameData()
    {
        Actors = new List<ActorData>();
    }
}
[Serializable]
public class GameCatalogue : ArchiveCatalogue
{
    public string Name;
}