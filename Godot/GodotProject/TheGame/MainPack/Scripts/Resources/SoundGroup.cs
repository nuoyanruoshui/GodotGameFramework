using Godot;

[GlobalClass]
public partial class SoundGroup : Resource
{
    [Export] // 音效组名称
    public string Name;
    [Export] // 音效组代理数量
    public int AgentCounts;
    [Export] // 音效组避免被同优先级音效替换
    public bool AvoidBeingReplacedBySamePriority;
}