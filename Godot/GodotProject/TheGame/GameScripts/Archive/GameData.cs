using Godot;
using GodotGameFrameworkCore.Archive;
using System;
using System.Collections.Generic;
[Serializable]
public class GameData : ArchiveData
{
    public int Score;
}
[Serializable]
public class GameCatalogue : ArchiveCatalogue
{
    public string Name;
}