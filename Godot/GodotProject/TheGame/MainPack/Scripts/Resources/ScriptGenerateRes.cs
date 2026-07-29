using Calcatz.EzpzInspector;
using Godot;
using System;
[GlobalClass]
public partial class ScriptGenerateRes : Resource
{
    public static class Parameters
    {
        public static readonly string NameSpace = "NameSpace";
        public static readonly string NodePrefix = "NodePrefix";
        public static readonly string UIOutPutPathGe = "UIOutPutPathGe";
        public static readonly string UIOutPutPathLogic = "UIOutPutPathLogic";
        public static readonly string EntityOutPutPathGe = "EntityOutPutPathGe";
        public static readonly string EntityOutPutPathLogic = "EntityOutPutPathLogic";
    }
    [Export]
    public string NameSpace = "GameLogic";
    [Export]
    public string NodePrefix = "m_";
    [UpperDescription("UI")]
    [Export(PropertyHint.Dir)]
    public string UIOutPutPathGe = "res://TheGame/GameScripts/GameProto/UIGe/";
    [Export(PropertyHint.Dir)]
    public string UIOutPutPathLogic = "res://TheGame/GameScripts/UI/";
    [UpperDescription("Entity")]
    [Export(PropertyHint.Dir)]
    public string EntityOutPutPathGe = "res://TheGame/GameScripts/GameProto/EntityGe/";
    [Export(PropertyHint.Dir)]
    public string EntityOutPutPathLogic = "res://TheGame/GameScripts/Entity/";

}
