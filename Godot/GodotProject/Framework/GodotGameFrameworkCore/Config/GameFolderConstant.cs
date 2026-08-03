using Godot;
using System;
namespace GameConfig.Constant
{
	/// <summary>游戏文件夹常量 需要手动补充</summary> 
	public static partial class GameFolderConstant
	{
		public const string ReadOnlyPath = "res://";
		public const string ReadWritePath = "user://";
		public const string GameFrameworkVersionData = "GameFrameworkVersion.dat";
		public const string Main = "res://TheGame/";
		public const string Audios = "res://TheGame/Audios/{0}.{1}";
		public const string LocalizationPath = "res://TheGame/DataTables/Localizations/";
		public const string LocalizationFiles = "res://TheGame/DataTables/Localizations/{0}.txt";
		public const string Entities = "res://TheGame/DataTables/Entitiys/{0}.tscn";
		public const string GameConfigs = "res://TheGame/DataTables/GameConfigs/{0}.bytes";
		public const string Scenes = "res://TheGame/Scenes";
		//Godot的资源脚本文件夹路径
		public const string MainPackResources = "res://TheGame/MainPack/Resources/{0}.tres";
		public const string GameConfigsPath = "res://TheGame/DataTables/GameConfigs";
	}
}

